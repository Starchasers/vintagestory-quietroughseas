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
        /// With the Rivers mod: false stops rivers pushing boats in any
        /// direction, true leaves Rivers as it is. Swimmers are always pushed.
        /// </summary>
        public bool RiversPushBoats = false;

        /// <summary>
        /// Rough Seas stroke name ("paddling", "sculling", "rowing", "pole",
        /// "seatedpole") to a multiplier on the power that stroke puts into
        /// the water. Hunger is unchanged. Top speed grows with roughly the
        /// cube root of the power, so 8 about doubles it. Left out or 1
        /// leaves a stroke as Rough Seas has it.
        /// </summary>
        public Dictionary<string, double> StrokePowerMultiplier;

        public static Dictionary<string, double> DefaultStrokePowerMultiplier()
        {
            Dictionary<string, double> defaults = new Dictionary<string, double>();
            defaults["paddling"] = 8;
            defaults["sculling"] = 8;
            return defaults;
        }
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
    /// 3. Rivers and boats (only if the Rivers mod is installed). Rivers pushes
    ///    every rowed boat along the river's flow from its own Harmony postfix
    ///    on the vanilla boat motion step. That postfix is skipped, so rivers
    ///    do not move boats at all (RiversPushBoats in
    ///    ModConfig/quietroughseas.json, default false). Swimmers are pushed
    ///    by a different Rivers patch, which is left alone.
    ///
    /// 4. Paddling speed. Rough Seas gives a rower a fixed body power and each
    ///    stroke a "gross efficiency", the share of it that reaches the blade;
    ///    hull drag then settles the speed. The efficiency of the strokes named
    ///    in StrokePowerMultiplier is multiplied in Rough Seas' loaded config,
    ///    which makes those boats faster without costing more hunger. No
    ///    Harmony patch is involved.
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

        /// <summary>Whether the Rivers mod is left to push boats. Read from the config file.</summary>
        static bool riversPushBoats = new QuietRoughSeasConfig().RiversPushBoats;

        /// <summary>Stroke name to power multiplier. Read from the config file.</summary>
        static Dictionary<string, double> strokePowerMultiplier = QuietRoughSeasConfig.DefaultStrokePowerMultiplier();

        // Rough Seas stroke configs whose efficiency has already been multiplied, so none is done twice.
        static readonly List<object> scaledStrokes = new List<object>();
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

                if (!riversPushBoats && api.ModLoader.GetMod(RiversModId) != null)
                {
                    int pushes = Apply(FindRiverBoatPush(ModTypes(api, RiversModId)), "SkipRiverBoatPush", null);
                    if (pushes > 0) Mod.Logger.Notification("Rivers no longer push boats.");
                    else Mod.Logger.Warning("Found no Rivers boat push method to patch. Rivers still push boats; this mod needs an update.");
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

            if (config == null) config = new QuietRoughSeasConfig();
            if (config.StrokePowerMultiplier == null) config.StrokePowerMultiplier = QuietRoughSeasConfig.DefaultStrokePowerMultiplier();

            // Written back every time, so a file from an older version gains the newer settings.
            try { api.StoreModConfig(config, ConfigFile); }
            catch (Exception e) { Mod.Logger.Warning("Could not write {0}: {1}", ConfigFile, e.Message); }

            strokePowerMultiplier = config.StrokePowerMultiplier;

            riversPushBoats = config.RiversPushBoats;
        }

        // ---- Stroke power (edits Rough Seas' loaded propulsion config) ----

        // Rough Seas reads its propulsion config in AssetsLoaded, once on each
        // side, so this runs on each side after that.
        public override void AssetsFinalize(ICoreAPI api)
        {
            try
            {
                ScaleStrokePower(api);
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Could not change Rough Seas stroke power: {0}", e);
            }
        }

        void ScaleStrokePower(ICoreAPI api)
        {
            Mod target = api.ModLoader.GetMod(TargetModId);
            if (target == null || strokePowerMultiplier == null || strokePowerMultiplier.Count == 0) return;

            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            System.Collections.IDictionary strokes = null;
            foreach (ModSystem system in target.Systems)
            {
                if (system.GetType().Name != "PropulsionModSystem") continue;
                PropertyInfo configProperty = system.GetType().GetProperty("Config", any);
                object config = configProperty == null ? null : configProperty.GetValue(system);
                FieldInfo strokesField = config == null ? null : config.GetType().GetField("Strokes", any);
                strokes = strokesField == null ? null : strokesField.GetValue(config) as System.Collections.IDictionary;
            }
            if (strokes == null)
            {
                Mod.Logger.Warning("Found no Rough Seas propulsion config. Boat speed is unchanged; this mod needs an update.");
                return;
            }

            foreach (KeyValuePair<string, double> entry in strokePowerMultiplier)
            {
                double multiplier = entry.Value;
                if (double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier <= 0)
                {
                    Mod.Logger.Warning("StrokePowerMultiplier for '{0}' in {1} must be a number above 0, ignoring it.", entry.Key, ConfigFile);
                    continue;
                }
                if (!strokes.Contains(entry.Key))
                {
                    Mod.Logger.Warning("Rough Seas has no stroke called '{0}' ({1}), ignoring it.", entry.Key, ConfigFile);
                    continue;
                }

                object stroke = strokes[entry.Key];
                FieldInfo efficiency = stroke == null ? null : stroke.GetType().GetField("GrossEfficiency", any);
                if (efficiency == null || efficiency.FieldType != typeof(double))
                {
                    Mod.Logger.Warning("Rough Seas stroke '{0}' has no efficiency to change; this mod needs an update.", entry.Key);
                    continue;
                }

                lock (scaledStrokes)
                {
                    bool done = false;
                    foreach (object seen in scaledStrokes)
                    {
                        if (ReferenceEquals(seen, stroke)) { done = true; break; }
                    }
                    if (done) continue;
                    scaledStrokes.Add(stroke);
                }

                double before = (double)efficiency.GetValue(stroke);
                efficiency.SetValue(stroke, before * multiplier);
                Mod.Logger.Notification("Rough Seas '{0}' stroke power multiplied by {1} on the {2} (efficiency {3} to {4}).", entry.Key, multiplier, api.Side, before, before * multiplier);
            }
        }

        // ---- Harmony prefixes (returning false skips the original method) ----

        static bool SkipLog() { return false; }

        // On the Rivers mod's own Harmony postfix that pushes a rowed boat along the flow.
        static bool SkipRiverBoatPush() { return false; }

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
