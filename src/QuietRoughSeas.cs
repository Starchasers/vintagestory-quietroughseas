using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace QuietRoughSeas
{
    /// <summary>
    /// Small Harmony patches on top of the Rough Seas mod:
    ///
    /// 1. Debug logs. Rough Seas writes all of its debug output through "Log"
    ///    helper methods on its mod systems (each one ends in
    ///    Mod.Logger.Debug). Every one of them gets a prefix that skips the
    ///    call. Notifications, warnings and errors do not go through those
    ///    helpers and still show up.
    ///
    /// 2. Accidental hull damage. Left-clicking a boat makes Rough Seas "Swing"
    ///    at its hull, and even an empty hand does 0.5 damage per click. The
    ///    swing is skipped unless an axe is held, so a boat can still be
    ///    broken up on purpose but not by a stray click or in a fight.
    ///
    /// The game compiles this file itself and does not hand source mods a
    /// reference to Harmony, so Harmony is driven through reflection.
    /// </summary>
    public class QuietRoughSeasSystem : ModSystem
    {
        const string TargetModId = "roughseas";
        const string HarmonyId = "quietroughseas";
        const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // Harmony patches are process-wide; in single player the client and
        // the server both start this system, so only patch once.
        static bool patched;
        static readonly object patchLock = new object();

        Type harmonyMethodType;
        object harmony;
        MethodInfo harmonyPatch;

        // Run before every other mod system gets a chance to log anything.
        public override double ExecuteOrder() { return -1000; }

        public override void StartPre(ICoreAPI api)
        {
            lock (patchLock)
            {
                if (patched) return;
                patched = true;
            }

            try
            {
                List<Type> types = RoughSeasTypes(api);
                LoadHarmony();

                int logs = Apply(FindLogMethods(types), "SkipLog");
                if (logs > 0) Mod.Logger.Notification("Silenced {0} Rough Seas debug log methods.", logs);
                else Mod.Logger.Warning("Found no Rough Seas debug log methods to silence. Rough Seas probably changed how it logs; this mod needs an update.");

                int swings = Apply(FindSwingMethods(types), "AllowSwing");
                if (swings > 0) Mod.Logger.Notification("Boats now only take hit damage from axes.");
                else Mod.Logger.Warning("Found no Rough Seas hull swing method to patch. Boats can still be damaged by hand; this mod needs an update.");
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Could not patch Rough Seas: {0}", e);
            }
        }

        // ---- Harmony prefixes (returning false skips the original method) ----

        static bool SkipLog() { return false; }

        // "slot" is matched by name to the parameter of Rough Seas' Swing method.
        static bool AllowSwing(ItemSlot slot)
        {
            ItemStack stack = slot == null ? null : slot.Itemstack;
            return stack != null && stack.Collectible != null && stack.Collectible.Tool == EnumTool.Axe;
        }

        // ---- Finding the methods to patch ----

        static List<Type> RoughSeasTypes(ICoreAPI api)
        {
            List<Type> types = new List<Type>();
            Mod target = api.ModLoader.GetMod(TargetModId);
            if (target == null) return types;

            List<Assembly> assemblies = new List<Assembly>();
            foreach (ModSystem system in target.Systems)
            {
                Assembly asm = system.GetType().Assembly;
                if (!assemblies.Contains(asm)) assemblies.Add(asm);
            }

            foreach (Assembly asm in assemblies)
            {
                Type[] found;
                try { found = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { found = e.Types; }
                foreach (Type type in found)
                {
                    if (type != null) types.Add(type);
                }
            }
            return types;
        }

        /// <summary>Every void "Log" method declared on a Rough Seas mod system.</summary>
        static List<MethodInfo> FindLogMethods(List<Type> types)
        {
            List<MethodInfo> found = new List<MethodInfo>();
            foreach (Type type in types)
            {
                if (!typeof(ModSystem).IsAssignableFrom(type)) continue;
                foreach (MethodInfo method in type.GetMethods(Declared))
                {
                    if (method.Name == "Log" && method.ReturnType == typeof(void) && !method.IsAbstract && !method.IsGenericMethodDefinition)
                    {
                        found.Add(method);
                    }
                }
            }
            return found;
        }

        /// <summary>The "Swing" method(s) on Rough Seas entity behaviors that take the attacker's ItemSlot as "slot".</summary>
        static List<MethodInfo> FindSwingMethods(List<Type> types)
        {
            List<MethodInfo> found = new List<MethodInfo>();
            foreach (Type type in types)
            {
                if (!typeof(EntityBehavior).IsAssignableFrom(type)) continue;
                foreach (MethodInfo method in type.GetMethods(Declared))
                {
                    if (method.Name != "Swing" || method.ReturnType != typeof(void) || method.IsAbstract) continue;
                    foreach (ParameterInfo p in method.GetParameters())
                    {
                        if (p.Name == "slot" && p.ParameterType == typeof(ItemSlot))
                        {
                            found.Add(method);
                            break;
                        }
                    }
                }
            }
            return found;
        }

        // ---- Harmony through reflection ----

        void LoadHarmony()
        {
            Assembly harmonyAsm = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "0Harmony") { harmonyAsm = asm; break; }
            }
            if (harmonyAsm == null) harmonyAsm = Assembly.Load("0Harmony");

            Type harmonyType = harmonyAsm.GetType("HarmonyLib.Harmony", true);
            harmonyMethodType = harmonyAsm.GetType("HarmonyLib.HarmonyMethod", true);
            harmony = Activator.CreateInstance(harmonyType, new object[] { HarmonyId });

            // Harmony.Patch(MethodBase original, HarmonyMethod prefix, postfix, transpiler, finalizer)
            foreach (MethodInfo m in harmonyType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (m.Name != "Patch") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length >= 2 && ps[0].ParameterType == typeof(MethodBase) && ps[1].ParameterType == harmonyMethodType)
                {
                    harmonyPatch = m;
                    break;
                }
            }
            if (harmonyPatch == null) throw new MissingMethodException("HarmonyLib.Harmony", "Patch");
        }

        /// <summary>Puts the named static prefix of this class on every target. Returns how many were patched.</summary>
        int Apply(List<MethodInfo> targets, string prefixName)
        {
            if (targets.Count == 0) return 0;

            MethodInfo prefixMethod = typeof(QuietRoughSeasSystem).GetMethod(prefixName, BindingFlags.Static | BindingFlags.NonPublic);
            object prefix = Activator.CreateInstance(harmonyMethodType, new object[] { prefixMethod });

            int count = 0;
            foreach (MethodInfo target in targets)
            {
                object[] args = new object[harmonyPatch.GetParameters().Length];
                args[0] = target;
                args[1] = prefix;
                try
                {
                    harmonyPatch.Invoke(harmony, args);
                    count++;
                }
                catch (Exception e)
                {
                    Mod.Logger.Warning("Could not patch {0}.{1}: {2}", target.DeclaringType.FullName, target.Name, e.InnerException ?? e);
                }
            }
            return count;
        }
    }
}
