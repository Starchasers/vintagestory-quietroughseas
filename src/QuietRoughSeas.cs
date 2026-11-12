using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;

namespace QuietRoughSeas
{
    /// <summary>
    /// Rough Seas writes all of its debug output through small "Log" helper
    /// methods on its mod systems (each one ends in Mod.Logger.Debug). This
    /// mod puts a Harmony prefix on every one of them that skips the call, so
    /// nothing is formatted or written. Notifications, warnings and errors do
    /// not go through those helpers and still show up.
    ///
    /// The game compiles this file itself and does not hand source mods a
    /// reference to Harmony, so Harmony is driven through reflection.
    /// </summary>
    public class QuietRoughSeasSystem : ModSystem
    {
        const string TargetModId = "roughseas";
        const string HarmonyId = "quietroughseas";

        // Harmony patches are process-wide; in single player the client and
        // the server both start this system, so only patch once.
        static bool patched;
        static readonly object patchLock = new object();

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
                int count = Patch(api);
                if (count > 0) Mod.Logger.Notification("Silenced {0} Rough Seas debug log methods.", count);
                else Mod.Logger.Warning("Found no Rough Seas debug log methods to silence. Rough Seas probably changed how it logs; this mod needs an update.");
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Could not silence Rough Seas debug logs: {0}", e);
            }
        }

        int Patch(ICoreAPI api)
        {
            List<MethodInfo> targets = FindLogMethods(api);
            if (targets.Count == 0) return 0;

            Assembly harmonyAsm = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "0Harmony") { harmonyAsm = asm; break; }
            }
            if (harmonyAsm == null) harmonyAsm = Assembly.Load("0Harmony");

            Type harmonyType = harmonyAsm.GetType("HarmonyLib.Harmony", true);
            Type harmonyMethodType = harmonyAsm.GetType("HarmonyLib.HarmonyMethod", true);

            object harmony = Activator.CreateInstance(harmonyType, new object[] { HarmonyId });
            MethodInfo skip = typeof(QuietRoughSeasSystem).GetMethod("Skip", BindingFlags.Static | BindingFlags.NonPublic);
            object prefix = Activator.CreateInstance(harmonyMethodType, new object[] { skip });

            // Harmony.Patch(MethodBase original, HarmonyMethod prefix, postfix, transpiler, finalizer)
            MethodInfo patch = null;
            foreach (MethodInfo m in harmonyType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (m.Name != "Patch") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length >= 2 && ps[0].ParameterType == typeof(MethodBase) && ps[1].ParameterType == harmonyMethodType)
                {
                    patch = m;
                    break;
                }
            }
            if (patch == null) throw new MissingMethodException("HarmonyLib.Harmony", "Patch");

            int count = 0;
            foreach (MethodInfo target in targets)
            {
                object[] args = new object[patch.GetParameters().Length];
                args[0] = target;
                args[1] = prefix;
                try
                {
                    patch.Invoke(harmony, args);
                    count++;
                }
                catch (Exception e)
                {
                    Mod.Logger.Warning("Could not silence {0}.{1}: {2}", target.DeclaringType.FullName, target.Name, e.InnerException ?? e);
                }
            }
            return count;
        }

        /// <summary>Every void "Log" method declared on a Rough Seas mod system.</summary>
        static List<MethodInfo> FindLogMethods(ICoreAPI api)
        {
            List<MethodInfo> found = new List<MethodInfo>();
            Mod target = api.ModLoader.GetMod(TargetModId);
            if (target == null) return found;

            List<Assembly> assemblies = new List<Assembly>();
            foreach (ModSystem system in target.Systems)
            {
                Assembly asm = system.GetType().Assembly;
                if (!assemblies.Contains(asm)) assemblies.Add(asm);
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (Assembly asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }

                foreach (Type type in types)
                {
                    if (type == null || !typeof(ModSystem).IsAssignableFrom(type)) continue;
                    foreach (MethodInfo method in type.GetMethods(flags))
                    {
                        if (method.Name == "Log" && method.ReturnType == typeof(void) && !method.IsAbstract && !method.IsGenericMethodDefinition)
                        {
                            found.Add(method);
                        }
                    }
                }
            }
            return found;
        }

        // Harmony prefix: returning false skips the original method.
        static bool Skip() { return false; }
    }
}
