using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace QuietRoughSeas
{
    public partial class QuietRoughSeasSystem
    {
        sealed class MotionSample
        {
            public double X, Y, Z, MX, MY, MZ;
            public int Dimension;
            public long EntityId;
            public bool Ready, Warned;
        }

        static readonly ConditionalWeakTable<EntityPlayer, MotionSample> motionSamples = new ConditionalWeakTable<EntityPlayer, MotionSample>();
        static PropertyInfo motionPlayer;
        static ILogger motionLogger;
        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        static bool FinitePosition(EntityPlayer player)
        {
            var p = player.Pos;
            return Finite(p.X) && Finite(p.Y) && Finite(p.Z)
                && Finite(p.Motion.X) && Finite(p.Motion.Y) && Finite(p.Motion.Z);
        }
        static void RememberMotion(EntityPlayer player)
        {
            if (player == null) return;
            var p = player.Pos;
            // Copy first, then validate the exact values retained in the snapshot.
            double x = p.X, y = p.Y, z = p.Z, mx = p.Motion.X, my = p.Motion.Y, mz = p.Motion.Z;
            int dimension = p.Dimension;
            long entityId = player.EntityId;
            MotionSample sample = motionSamples.GetOrCreateValue(player);
            lock (sample)
            {
                if (sample.Dimension != dimension || sample.EntityId != entityId) sample.Ready = false;
                if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(mx) || !Finite(my) || !Finite(mz)) return;
                sample.X = x; sample.Y = y; sample.Z = z;
                sample.MX = mx; sample.MY = my; sample.MZ = mz;
                sample.Dimension = dimension; sample.EntityId = entityId;
                sample.Ready = true;
            }
        }

        // Capture BEFORE integration and the in-tick network call, not after a bad
        // packet has already thrown. Never use another entity's/stale dimension's
        // position, nor manufacture an origin/server-position teleport.
        static void RememberPlayerMotion(EntityBehavior __instance)
        {
            RememberMotion(__instance.entity as EntityPlayer);
        }

        static bool FinitePlayerPacket(object ___game)
        {
            EntityPlayer player = motionPlayer.GetValue(___game) as EntityPlayer;
            if (player == null) return true;
            if (FinitePosition(player)) { RememberMotion(player); return true; }
            MotionSample sample = motionSamples.GetOrCreateValue(player);
            lock (sample)
            {
                if (sample.Dimension != player.Pos.Dimension || sample.EntityId != player.EntityId) sample.Ready = false;
                bool recover = sample.Ready;
                if (!sample.Warned)
                {
                    sample.Warned = true;
                    if (motionLogger != null) motionLogger.Warning("Rejected non-finite player position/motion before sending a packet; {0}. This is containment, not identification of the corrupting mod.", recover ? "restoring the last finite local sample" : "no finite local sample in this dimension; packet skipped until server correction or reconnect");
                }
                if (!recover) return false;
                var p = player.Pos;
                p.X = sample.X; p.Y = sample.Y; p.Z = sample.Z;
                p.Motion.Set(sample.MX, sample.MY, sample.MZ);
                return true;
            }
        }

        sealed class MountWarning
        {
            public long EntityId;
            public int Dimension;
            public bool Warned;
        }
        static readonly ConditionalWeakTable<Entity, MountWarning> mountWarnings = new ConditionalWeakTable<Entity, MountWarning>();

        // A send boundary cannot reconstruct a mount's persistent physics state.
        // Reject only unsafe data, without restoring snapshots, clearing velocity,
        // dismounting, or substituting player/server coordinates. Finite vanilla
        // mounts and Rough Seas vessels retain the original serializer untouched.
        static bool FiniteMountPacket(Entity mount)
        {
            if (mount == null) return true; // Preserve native null semantics.
            var p = mount.Pos;
            if (Finite(p.X) && Finite(p.Y) && Finite(p.Z)
                && Finite(p.Motion.X) && Finite(p.Motion.Y) && Finite(p.Motion.Z)) return true;
            MountWarning warning = mountWarnings.GetOrCreateValue(mount);
            lock (warning)
            {
                if (warning.EntityId != mount.EntityId || warning.Dimension != p.Dimension)
                    warning.Warned = false;
                warning.EntityId = mount.EntityId;
                warning.Dimension = p.Dimension;
                if (!warning.Warned)
                {
                    warning.Warned = true;
                    if (motionLogger != null) motionLogger.Warning("Rejected non-finite mount position/motion packet for entity {0} in dimension {1}; no mount state was restored or changed. This is containment, not identification of the corrupting mod; server correction or reconnect may be required.", mount.EntityId, p.Dimension);
                }
            }
            return false;
        }

        // Rough Seas divides SecondsIn(dt, speed) by dt before stepping its body.
        // At zero time this is 0/0; even a zero current then poisons X/Z via NaN*0.
        // Do not initialize/update its persistent swimming/wading state that tick.
        // true is Afloat's normal "continue vanilla liquid physics" result.
        static bool FiniteSwimmingStep(float dt, ref bool __result)
        {
            if (dt > 0 && Finite(dt)) return true;
            __result = true;
            return false;
        }

        // A bad drawn pose is cosmetic: it must not contaminate the player's
        // temporary send/render Y or get added back as NaN by RestoreLift.
        static void FiniteVesselLift(ref double __result)
        {
            if (!Finite(__result)) __result = 0;
        }

        void MotionPatch(MethodInfo target, string prefix, string postfix, bool first = false)
        {
            if (target == null) throw new MissingMethodException("Player-motion compatibility target not found");
            object[] args = new object[harmonyPatch.GetParameters().Length];
            args[0] = target;
            if (prefix != null)
            {
                object patch = Activator.CreateInstance(harmonyMethodType, new object[] { typeof(QuietRoughSeasSystem).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic) });
                if (first)
                {
                    harmonyMethodType.GetField("priority").SetValue(patch, 800);
                    harmonyMethodType.GetField("before").SetValue(patch, new[] { "roughseas.storms.motion" });
                }
                args[1] = patch;
            }
            if (postfix != null) args[2] = Activator.CreateInstance(harmonyMethodType, new object[] { typeof(QuietRoughSeasSystem).GetMethod(postfix, BindingFlags.Static | BindingFlags.NonPublic) });
            harmonyPatch.Invoke(harmony, args);
        }

        void InstallPlayerMotion(List<Type> types)
        {
            Type afloat = types.Find(t => t.FullName == "RoughSeas.Person.BodyAfloat");
            Type vessel = types.Find(t => t.FullName == "RoughSeas.Floating.VesselMotion");
            if (afloat == null || vessel == null) throw new TypeLoadException("Rough Seas player-motion compatibility targets");
            Type network = Type.GetType("Vintagestory.Client.NoObf.NetworkAPI, VintagestoryLib", true);
            Type client = Type.GetType("Vintagestory.Client.NoObf.ClientMain, VintagestoryLib", true);
            motionPlayer = client.GetProperty("EntityPlayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (motionPlayer == null) throw new MissingMemberException(client.FullName, "EntityPlayer");
            motionLogger = Mod.Logger;
            MotionPatch(afloat.GetMethod("Afloat", Declared), "FiniteSwimmingStep", null);
            MotionPatch(vessel.GetMethod("Lift", Declared), null, "FiniteVesselLift");
            MotionPatch(typeof(EntityBehaviorPlayerPhysics).GetMethod("OnPhysicsTick"), "RememberPlayerMotion", null, true);
            MotionPatch(network.GetMethod("SendPlayerPositionPacket", Type.EmptyTypes), "FinitePlayerPacket", null, true);
            MotionPatch(network.GetMethod("SendPlayerMountPositionPacket", new[] { typeof(Entity) }), "FiniteMountPacket", null, true);
            Mod.Logger.Notification("Guarded Rough Seas zero-time swimming, non-finite vessel lift, player and mount motion packets.");
        }
    }
}
