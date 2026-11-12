using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace QuietRoughSeas
{
    /// <summary>Contents of ModConfig/quietroughseas.json.</summary>
    public class QuietRoughSeasConfig
    {
        /// <summary>
        /// With the Rivers mod: the river's push against a boat rowed upstream
        /// is divided by this. 1 leaves Rivers as it is, higher makes rowing
        /// upstream easier.
        /// </summary>
        public double RiverUpstreamDivisor = 8;
    }

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
    /// 3. Rowing upstream (only if the Rivers mod is installed). Rivers pushes
    ///    every rowed boat along the river's flow. The part of that push that
    ///    works against the direction the boat is being rowed is divided by
    ///    RiverUpstreamDivisor from ModConfig/quietroughseas.json (default 8).
    ///    The push sideways and downstream is untouched, and so are swimmers.
    ///
    /// The game compiles this file itself and does not hand source mods a
    /// reference to Harmony, so Harmony is driven through reflection.
    /// </summary>
    public class QuietRoughSeasSystem : ModSystem
    {
        const string TargetModId = "roughseas";
        const string RiversModId = "rivers";
        const string HarmonyId = "quietroughseas";

        const string ConfigFile = "quietroughseas.json";

        /// <summary>How much weaker the river's push is against a boat rowed upstream. Read from the config file.</summary>
        static double riverUpstreamDivisor = new QuietRoughSeasConfig().RiverUpstreamDivisor;
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
                List<Type> types = ModTypes(api, TargetModId);
                LoadHarmony();

                int logs = Apply(FindLogMethods(types), "SkipLog", null);
                if (logs > 0) Mod.Logger.Notification("Silenced {0} Rough Seas debug log methods.", logs);
                else Mod.Logger.Warning("Found no Rough Seas debug log methods to silence. Rough Seas probably changed how it logs; this mod needs an update.");

                int swings = Apply(FindSwingMethods(types), "AllowSwing", null);
                if (swings > 0) Mod.Logger.Notification("Boats now only take hit damage from axes.");
                else Mod.Logger.Warning("Found no Rough Seas hull swing method to patch. Boats can still be damaged by hand; this mod needs an update.");

                LoadConfig(api);

                if (api.ModLoader.GetMod(RiversModId) != null)
                {
                    int pushes = Apply(FindRiverBoatPush(ModTypes(api, RiversModId)), "BeforeRiverPush", "AfterRiverPush");
                    if (pushes > 0) Mod.Logger.Notification("River current against a boat rowed upstream is divided by {0}.", riverUpstreamDivisor);
                    else Mod.Logger.Warning("Found no Rivers boat push method to patch. Rowing upstream is unchanged; this mod needs an update.");
                }
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Could not patch Rough Seas: {0}", e);
            }
        }

        /// <summary>Reads ModConfig/quietroughseas.json, writing it with the defaults if it is missing.</summary>
        void LoadConfig(ICoreAPI api)
        {
            QuietRoughSeasConfig config = null;
            try
            {
                config = api.LoadModConfig<QuietRoughSeasConfig>(ConfigFile);
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Could not read {0}, using the defaults: {1}", ConfigFile, e.Message);
            }

            if (config == null)
            {
                config = new QuietRoughSeasConfig();
                try { api.StoreModConfig(config, ConfigFile); }
                catch (Exception e) { Mod.Logger.Warning("Could not write {0}: {1}", ConfigFile, e.Message); }
            }

            double divisor = config.RiverUpstreamDivisor;
            if (double.IsNaN(divisor) || double.IsInfinity(divisor) || divisor <= 0)
            {
                double fallback = new QuietRoughSeasConfig().RiverUpstreamDivisor;
                Mod.Logger.Warning("RiverUpstreamDivisor in {0} must be a number above 0, using {1}.", ConfigFile, fallback);
                divisor = fallback;
            }
            riverUpstreamDivisor = divisor;
        }

        // ---- River push on boats (wrapped around the Rivers mod's own boat patch) ----

        // The method being wrapped is Rivers' own static Harmony postfix,
        // Postfix(EntityBoat __instance). For a static method Harmony's
        // "__instance" is null, so the boat has to be taken as the first
        // argument, "__0".

        // Remember the boat's motion before Rivers adds its push.
        static void BeforeRiverPush(EntityBoat __0, out double[] __state)
        {
            __state = null;
            EntityPos pos = __0 == null ? null : __0.Pos;
            if (pos == null || pos.Motion == null) return;
            __state = new double[] { pos.Motion.X, pos.Motion.Z };
        }

        // Work out what Rivers added, and take back most of the part that
        // works against the direction the boat is being rowed.
        static void AfterRiverPush(EntityBoat __0, double[] __state)
        {
            if (__state == null || __0 == null || __0.ForwardSpeed == 0) return;

            EntityPos pos = __0.Pos;
            if (pos == null || pos.Motion == null) return;
            double pushX = pos.Motion.X - __state[0];
            double pushZ = pos.Motion.Z - __state[1];
            if (pushX == 0 && pushZ == 0) return;

            // Same direction vanilla rows the boat in: the view vector times -ForwardSpeed.
            Vec3f view = pos.GetViewVector();
            double sign = __0.ForwardSpeed > 0 ? -1 : 1;
            double headX = view.X * sign;
            double headZ = view.Z * sign;
            double length = Math.Sqrt(headX * headX + headZ * headZ);
            if (length < 1e-6) return;
            headX /= length;
            headZ /= length;

            double along = pushX * headX + pushZ * headZ;
            if (along >= 0) return; // the river is helping, or pushing sideways

            double takeBack = along * (1 - 1 / riverUpstreamDivisor);
            pos.Motion.X -= takeBack * headX;
            pos.Motion.Z -= takeBack * headZ;
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

        /// <summary>Every type in the assemblies of the given mod's mod systems.</summary>
        static List<Type> ModTypes(ICoreAPI api, string modId)
        {
            List<Type> types = new List<Type>();
            Mod target = api.ModLoader.GetMod(modId);
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

        /// <summary>The Rivers mod's Harmony postfix that pushes a rowed boat along the flow.</summary>
        static List<MethodInfo> FindRiverBoatPush(List<Type> types)
        {
            List<MethodInfo> found = new List<MethodInfo>();
            foreach (Type type in types)
            {
                if (type.Name != "UpdateBoatAngleAndMotionPostfix") continue;
                foreach (MethodInfo method in type.GetMethods(Declared))
                {
                    if (method.Name != "Postfix" || !method.IsStatic || method.ReturnType != typeof(void)) continue;
                    ParameterInfo[] ps = method.GetParameters();
                    if (ps.Length == 1 && ps[0].Name == "__instance" && ps[0].ParameterType == typeof(EntityBoat)) found.Add(method);
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

        /// <summary>Puts the named static prefix (and optional postfix) of this class on every target. Returns how many were patched.</summary>
        int Apply(List<MethodInfo> targets, string prefixName, string postfixName)
        {
            if (targets.Count == 0) return 0;

            object prefix = HarmonyMethodFor(prefixName);
            object postfix = postfixName == null ? null : HarmonyMethodFor(postfixName);

            int count = 0;
            foreach (MethodInfo target in targets)
            {
                object[] args = new object[harmonyPatch.GetParameters().Length];
                args[0] = target;
                args[1] = prefix;
                args[2] = postfix;
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

        object HarmonyMethodFor(string name)
        {
            MethodInfo method = typeof(QuietRoughSeasSystem).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException("QuietRoughSeasSystem", name);
            return Activator.CreateInstance(harmonyMethodType, new object[] { method });
        }
    }
}
