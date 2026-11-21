using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace QuietRoughSeas
{
    public partial class QuietRoughSeasSystem
    {
        /// <summary>
        /// Install after LoadHarmony and LoadConfig, under the caller's process-wide patch-once lock.
        /// RiversPushBoats=true leaves both current sources vanilla. No worldgen/config changes.
        /// </summary>
        void InstallCurrents(ICoreAPI api, List<Type> roughTypes)
        {
            if (riversPushBoats) return;

            try
            {
                var targets = new List<MethodInfo>();
                foreach (Type type in roughTypes)
                {
                    if (type.FullName != "RoughSeas.Floating.EntityBehaviorCurrentDrift") continue;
                    MethodInfo method = type.GetMethod("Current", BindingFlags.Instance | BindingFlags.NonPublic,
                        null, Type.EmptyTypes, null);
                    if (typeof(EntityBehavior).IsAssignableFrom(type) && method != null &&
                        method.ReturnType == typeof(ValueTuple<double, double>)) targets.Add(method);
                }
                // The original reflection Apply helper requires a named prefix even for a postfix-only patch.
                int patched = Apply(targets, "KeepCurrentSampling", "HelpfulRoughCurrentPostfix");
                if (patched == 0)
                    api.Logger.Warning("[QuietRoughSeas] Rough Seas Current() was not patched; boat currents remain unfiltered. This mod needs an update.");
            }
            catch (Exception e)
            {
                api.Logger.Warning("[QuietRoughSeas] Could not filter Rough Seas boat currents: {0}", e);
            }

            // Rivers is optional. Only its boat patch is intercepted, never the swimmer physics patch.
            if (api.ModLoader.GetMod(RiversModId) == null) return;
            try
            {
                var targets = new List<MethodInfo>();
                foreach (Type type in ModTypes(api, RiversModId))
                {
                    if (type.Name != "UpdateBoatAngleAndMotionPostfix" || type.DeclaringType == null ||
                        type.DeclaringType.Name != "EntityBehaviorPhysicsPatch") continue;
                    MethodInfo method = type.GetMethod("Postfix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new Type[] { typeof(EntityBoat) }, null);
                    if (method != null && method.ReturnType == typeof(void)) targets.Add(method);
                }
                int patched = Apply(targets, "SnapshotRiverBoatPush", "HelpfulRiverBoatPushPostfix");
                if (patched == 0)
                    api.Logger.Warning("[QuietRoughSeas] Rivers boat Postfix(EntityBoat) was not patched; Rivers boat currents remain unfiltered. This mod needs an update.");
            }
            catch (Exception e)
            {
                api.Logger.Warning("[QuietRoughSeas] Could not filter Rivers boat currents: {0}", e);
            }
        }

        static void KeepCurrentSampling() { }

        /// <summary>
        /// Pure horizontal projection: retain only the positive component along the supplied heading.
        /// Heading need not be unit length. Zero/invalid heading or non-finite input gives no assistance.
        /// No speed boost, lateral component, or upstream component is manufactured.
        /// </summary>
        public static (double X, double Z) ProjectHelpfulCurrent(double currentX, double currentZ,
            double headingX, double headingZ)
        {
            if (!FiniteCurrentNumber(currentX) || !FiniteCurrentNumber(currentZ) ||
                !FiniteCurrentNumber(headingX) || !FiniteCurrentNumber(headingZ)) return (0, 0);

            double scale = Math.Max(Math.Abs(headingX), Math.Abs(headingZ));
            if (scale == 0) return (0, 0);
            double x = headingX / scale;
            double z = headingZ / scale;
            double length = Math.Sqrt(x * x + z * z);
            x /= length;
            z /= length;
            double along = currentX * x + currentZ * z;
            if (!FiniteCurrentNumber(along) || along <= 0) return (0, 0);
            return (x * along, z * along);
        }

        static bool FiniteCurrentNumber(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        // Source of direction is deliberately NOT Pos.Motion, Course.Velocity or RS's HullSet:
        // those contain current-driven drift and would let a sideways/adverse stream justify itself.
        // RS HullShape/Heading uses (sin(yaw), cos(yaw)); vanilla boat motion is
        // -ForwardSpeed * GetViewVector(), giving exactly the same horizontal heading.
        // ForwardSpeed is the row/sail control scalar, not the current-influenced world velocity.
        // Negative control reverses the helpful axis. At rest the hull's bow remains the axis.
        static (double X, double Z) HelpfulHullHeading(Entity entity)
        {
            double sign = 1;
            EntityBoat boat = entity as EntityBoat;
            if (boat != null)
            {
                if (boat.ForwardSpeed < 0) sign = -1;
            }
            else
            {
                // Rough Seas also supports registered boat copies which do not inherit EntityBoat.
                // Their shared currentdrift behavior still needs the same cap, including reverse control.
                FieldInfo speed = CurrentSpeedField(entity.GetType());
                if (speed != null && (double)speed.GetValue(entity) < 0) sign = -1;
            }
            return (sign * Math.Sin(entity.Pos.Yaw), sign * Math.Cos(entity.Pos.Yaw));
        }

        static readonly Dictionary<Type, FieldInfo> currentSpeedFields = new Dictionary<Type, FieldInfo>();
        static readonly object currentSpeedFieldsLock = new object();

        static FieldInfo CurrentSpeedField(Type type)
        {
            lock (currentSpeedFieldsLock)
            {
                FieldInfo field;
                if (currentSpeedFields.TryGetValue(type, out field)) return field;
                field = type.GetField("ForwardSpeed", BindingFlags.Instance | BindingFlags.Public);
                if (field != null && field.FieldType != typeof(double)) field = null;
                currentSpeedFields[type] = field;
                return field;
            }
        }

        // Behavior-level interception covers every hull carrying currentdrift, not only a selected
        // boat code/player controller. No RS assembly is referenced at compile time.
        static void HelpfulRoughCurrentPostfix(EntityBehavior __instance, ref (double X, double Z) __result)
        {
            if (riversPushBoats) return;
            var heading = HelpfulHullHeading(__instance.entity);
            __result = ProjectHelpfulCurrent(__result.X, __result.Z, heading.X, heading.Z);
        }

        struct RiverBoatPushSnapshot
        {
            public bool Captured;
            public double MotionX, MotionZ, HeadingX, HeadingZ;
        }

        // The target is Rivers' STATIC Postfix(EntityBoat __instance), not EntityBoat's update method.
        // Harmony __instance would be null here! __0 binds the target's first ARGUMENT instead.
        // Harmony's per-invocation __state also avoids cross-boat/thread/reentrancy snapshot leakage.
        static void SnapshotRiverBoatPush(EntityBoat __0, out RiverBoatPushSnapshot __state)
        {
            __state = default(RiverBoatPushSnapshot);
            if (riversPushBoats || __0 == null) return;
            var heading = HelpfulHullHeading(__0);
            __state.Captured = true;
            __state.MotionX = __0.Pos.Motion.X;
            __state.MotionZ = __0.Pos.Motion.Z;
            __state.HeadingX = heading.X;
            __state.HeadingZ = heading.Z;
        }

        static void HelpfulRiverBoatPushPostfix(EntityBoat __0, RiverBoatPushSnapshot __state)
        {
            if (riversPushBoats || !__state.Captured || __0 == null) return;
            var motion = __0.Pos.Motion;
            var helpful = ProjectHelpfulCurrent(motion.X - __state.MotionX, motion.Z - __state.MotionZ,
                __state.HeadingX, __state.HeadingZ);
            // Replace only Rivers' increment, preserving propulsion, earlier physics and vertical motion.
            motion.X = __state.MotionX + helpful.X;
            motion.Z = __state.MotionZ + helpful.Z;
        }
    }
}