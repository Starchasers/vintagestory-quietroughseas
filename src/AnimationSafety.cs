using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace QuietRoughSeas
{
    public partial class QuietRoughSeasSystem
    {
        static readonly FieldInfo animationApi = typeof(AnimationManager).GetField("api", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo animationEntity = typeof(AnimationManager).GetField("entity", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo animationMeta = typeof(EntityClientProperties).GetField("AnimationsByMetaCode");
        static bool AnimationMetadataReady(Entity entity)
        {
            // Native source-loader omits System.Collections reference; reflect the
            // API's Dictionary field rather than introducing that loader dependency.
            return entity?.Properties?.Client != null && animationMeta.GetValue(entity.Properties.Client) != null;
        }
        sealed class CosmeticLife { public bool Disposed; }
        static readonly ConditionalWeakTable<object, CosmeticLife> cosmeticLife = new ConditionalWeakTable<object, CosmeticLife>();
        sealed class CosmeticLog { public long Next; public int Skipped; }
        static readonly Dictionary<string, CosmeticLog> cosmeticLogs = new Dictionary<string, CosmeticLog>();
        static Action<string> cosmeticWarning;

        static void CosmeticDiagnostic(string boundary, string reason)
        {
            // Bounded by the fixed patch allowlist, not entity IDs or exception messages.
            lock (cosmeticLogs)
            {
                CosmeticLog state;
                if (!cosmeticLogs.TryGetValue(boundary, out state)) cosmeticLogs[boundary] = state = new CosmeticLog();
                long now = Stopwatch.GetTimestamp();
                if (now < state.Next) { if (state.Skipped < int.MaxValue) state.Skipped++; return; }
                cosmeticWarning?.Invoke("Cosmetic lifecycle guard " + boundary + ": " + reason + "; suppressed repeats=" + state.Skipped + ". Will retry when ready.");
                state.Skipped = 0;
                state.Next = now + Stopwatch.Frequency * 30L;
            }
        }

        static bool AnimationContextReady(IAnimationManager manager, bool read)
        {
            if (manager == null || (read && manager.Animator == null)) return false;
            // Native string-start and stop dereference the initialized entity/world.
            // Foreign implementations retain their own virtual contracts.
            if (manager.GetType() == typeof(AnimationManager) && !read)
            {
                Entity native = animationEntity.GetValue(manager) as Entity;
                return native != null && !native.ShouldDespawn && native.World != null && AnimationMetadataReady(native);
            }
            if (!(manager is PlayerAnimationManager)) return true;
            ICoreAPI api = animationApi.GetValue(manager) as ICoreAPI;
            EntityPlayer player = animationEntity.GetValue(manager) as EntityPlayer;
            if (api == null || player == null || player.ShouldDespawn || !AnimationMetadataReady(player)) return false;
            if (api.Side != EnumAppSide.Client) return true;
            ICoreClientAPI client = api as ICoreClientAPI;
            // Native useFpAnimSet/fpEnding and StopAnimation access all of these.
            return client?.World?.Player?.Entity != null && client.Settings != null &&
                player.TpAnimManager != null && player.SelfFpAnimManager != null;
        }

        static IClientPlayer ReadyPosePlayer(IClientWorldAccessor world) { return world?.Player; }
        static EntityPlayer ReadyPosePlayerEntity(IPlayer player) { return player?.Entity; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool ReadyAnimationStart(IAnimationManager manager, string code)
        {
            if (!AnimationContextReady(manager, false)) { CosmeticDiagnostic("animation-start", "manager context not ready"); return false; }
            return manager.StartAnimation(code);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool ReadyAnimationMeta(IAnimationManager manager, AnimationMetaData meta)
        {
            if (!AnimationContextReady(manager, false)) { CosmeticDiagnostic("animation-start", "manager context not ready"); return false; }
            return manager.StartAnimation(meta);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ReadyAnimationStop(IAnimationManager manager, string code)
        {
            Entity native = manager is AnimationManager ? animationEntity.GetValue(manager) as Entity : null;
            if (!AnimationContextReady(manager, false) || (manager is PlayerAnimationManager && (native?.World == null || !(((EntityPlayer)native).OtherAnimManager is PlayerAnimationManager)))) { CosmeticDiagnostic("animation-stop", "manager context not ready"); return; }
            manager.StopAnimation(code);
        }

        static object CosmeticField(object obj, string name)
        {
            return obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj);
        }
        static bool CosmeticEntityReady(Entity entity)
        {
            return entity != null && !entity.ShouldDespawn && entity.Properties?.Client != null;
        }
        static bool OwnOarsReady(object __instance)
        {
            Entity vessel = CosmeticField(__instance, "vessel") as Entity;
            if (CosmeticEntityReady(vessel) && vessel.World != null && AnimationContextReady(vessel.AnimManager, true)) return true;
            CosmeticDiagnostic("OwnOars.Follow", "vessel/animator not ready or despawning");
            return false;
        }
        static bool RendererReady(object __instance, MethodBase __originalMethod)
        {
            CosmeticLife life = cosmeticLife.GetOrCreateValue(__instance);
            if (life.Disposed) return false;
            Entity vessel = CosmeticField(__instance, "entity") as Entity;
            // Shape invalidation does not require an initialized renderer/player.
            // Preserve it during spawn so the first ready frame rebuilds correctly.
            if (__originalMethod.Name == "Shaped") return vessel != null && !vessel.ShouldDespawn;
            bool ready = CosmeticEntityReady(vessel);
            ICoreClientAPI api = CosmeticField(__instance, "capi") as ICoreClientAPI;
            ready &= api?.Render != null;
            if (__instance.GetType().Name == "BilgeRenderer" || __instance.GetType().Name == "RigRenderer") ready &= api?.Tesselator != null;
            if (__originalMethod.Name == "OnRenderFrame")
            {
                ready &= vessel?.Pos != null;
                if (__instance.GetType().Name == "RigRenderer")
                    ready &= api?.World?.Player?.Entity != null && CosmeticField(__instance, "weave") != null && CosmeticField(__instance, "ropes") != null;
                if (__instance.GetType().Name == "BilgeRenderer") ready &= vessel?.SelectionBox != null;
            }
            if (ready) return true;
            CosmeticDiagnostic(__instance.GetType().Name + "." + __originalMethod.Name, "render context not ready or despawning");
            return false;
        }
        static bool RendererDispose(object __instance)
        {
            CosmeticLife life = cosmeticLife.GetOrCreateValue(__instance);
            if (life.Disposed) return false;
            // Mark before resource release: late events must never re-upload a mesh.
            life.Disposed = true;
            return true;
        }

        static Exception CosmeticFailure(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception == null) return null;
            // Only exact mod-owned throw sites. Native/Harmony/other-mod callbacks,
            // GPU failures, allocation failures and gameplay/network errors propagate.
            if (!(__exception is NullReferenceException) && !(__exception is IndexOutOfRangeException)) return __exception;
            MethodBase site = new StackTrace(__exception).GetFrame(0)?.GetMethod();
            if (site != __originalMethod)
            {
                // Harmony emits a replacement method; resolve its registered origin,
                // never infer ownership merely from a spoofable method-name prefix.
                MethodInfo origin = Type.GetType("HarmonyLib.Harmony, 0Harmony")?.GetMethod("GetOriginalMethod", BindingFlags.Public | BindingFlags.Static);
                if (!(site is MethodInfo) || origin == null || !Equals(origin.Invoke(null, new object[] { site }), __originalMethod)) return __exception;
            }
            CosmeticDiagnostic(__originalMethod.DeclaringType.Name + "." + __originalMethod.Name,
                __exception.GetType().Name + " at " + site + ": " + __exception.Message);
            return null;
        }

        static void ItemCosmeticUnloaded(object __instance) { cosmeticLife.GetOrCreateValue(__instance).Disposed = true; }
        static bool ItemCosmeticReady(object __instance, ICoreClientAPI capi, ItemStack itemstack, ItemRenderInfo renderinfo)
        {
            if (cosmeticLife.GetOrCreateValue(__instance).Disposed) return false;
            if (capi?.Render != null && capi.Tesselator != null && capi.World != null && itemstack?.Attributes != null && renderinfo != null) return true;
            CosmeticDiagnostic(__instance.GetType().Name + ".OnBeforeRender", "item render context not ready");
            return false;
        }

        static readonly ModuleBuilder animationPatchModule = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("QuietRoughSeas.CosmeticTranspilers"), AssemblyBuilderAccess.Run).DefineDynamicModule("patches");
        static int animationPatchSequence;
        static MethodInfo AnimationTranspiler(MethodInfo closed, Type instruction)
        {
            // Harmony stores patch MethodInfo by metadata token. A closed generic
            // method loses its type arguments on re-patch; emit a non-generic bridge.
            Type enumerable = typeof(IEnumerable<>).MakeGenericType(instruction);
            TypeBuilder type = animationPatchModule.DefineType("Adapter" + (++animationPatchSequence), TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            MethodBuilder method = type.DefineMethod("Transpile", MethodAttributes.Public | MethodAttributes.Static, enumerable, new[] { enumerable });
            method.DefineParameter(1, ParameterAttributes.None, "instructions");
            ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, closed); il.Emit(OpCodes.Ret);
            return type.CreateType().GetMethod("Transpile");
        }

        void InstallAnimationSafety(List<Type> types)
        {
            cosmeticWarning = message => Mod.Logger.Warning(message);
            Type instruction = harmonyMethodType.Assembly.GetType("HarmonyLib.CodeInstruction", true);
            MethodInfo rewrite = typeof(QuietRoughSeasSystem).GetMethod("RewriteCosmeticAnimations", BindingFlags.Static | BindingFlags.Public).MakeGenericMethod(instruction);
            rewrite = AnimationTranspiler(rewrite, instruction);
            foreach (string target in new[] { "Propulsion.MountStrokes.Show", "Vessels.OwnOars.Follow", "Vessels.OwnOars.Match", "Vessels.OwnOars.Stop", "Vessels.SeatPoses.Show", "Shipyard.CollectibleBehaviorLightWithFirestarter.Begin", "Shipyard.CollectibleBehaviorLightWithFirestarter.End", "Shipyard.EntityBehaviorBurnout.Chip" })
            {
                int dot = target.LastIndexOf('.');
                Type type = types.Find(t => t.FullName == "RoughSeas." + target.Substring(0, dot));
                MethodInfo method = type?.GetMethod(target.Substring(dot + 1), Declared);
                if (method == null) throw new MissingMethodException(target);
                object[] args = new object[harmonyPatch.GetParameters().Length];
                args[0] = method; args[3] = Activator.CreateInstance(harmonyMethodType, new object[] { rewrite });
                harmonyPatch.Invoke(harmony, args);
            }
            // OwnOars' state read was not covered by the original MountStrokes patch.
            Type oars = types.Find(t => t.FullName == "RoughSeas.Vessels.OwnOars");
            Patch(oars.GetMethod("Follow", Declared), "OwnOarsReady", null);
            PatchCosmeticFinalizer(oars.GetMethod("Follow", Declared));
            // Pace finalizer is installed atomically with its generic transpiler.
            foreach (string name in new[] { "Cargo.CargoRenderer", "Sailing.RigRenderer", "Storms.BilgeRenderer" })
            {
                Type type = types.Find(t => t.FullName == "RoughSeas." + name);
                if (type == null) throw new TypeLoadException(name);
                foreach (string method in new[] { "OnRenderFrame", "Show", "Shaped" })
                {
                    MethodInfo target = type.GetMethod(method, Declared);
                    if (target != null) Patch(target, "RendererReady", null);
                }
                Patch(type.GetMethod("Dispose", Declared), "RendererDispose", null);
            }
            foreach (string name in new[] { "Shipyard.CollectibleBehaviorIcon", "Shipyard.CollectibleBehaviorCarriedBuild" })
            {
                Type type = types.Find(t => t.FullName == "RoughSeas." + name);
                Patch(type.GetMethod("OnBeforeRender", Declared), "ItemCosmeticReady", null);
                Patch(type.GetMethod("OnUnloaded", Declared), "ItemCosmeticUnloaded", null);
            }
            Mod.Logger.Notification("Guarded Rough Seas cosmetic animation and renderer lifecycles (bounded mod-owned exception containment).");
        }
        void Patch(MethodInfo target, string prefix, string unused)
        {
            if (target == null) throw new MissingMethodException("Cosmetic lifecycle target absent");
            object[] args = new object[harmonyPatch.GetParameters().Length];
            args[0] = target;
            args[1] = Activator.CreateInstance(harmonyMethodType, new object[] { typeof(QuietRoughSeasSystem).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic) });
            harmonyPatch.Invoke(harmony, args);
        }
        void PatchCosmeticFinalizer(MethodInfo method)
        {
            object[] args = new object[harmonyPatch.GetParameters().Length];
            args[0] = method;
            args[4] = Activator.CreateInstance(harmonyMethodType, new object[] { typeof(QuietRoughSeasSystem).GetMethod("CosmeticFailure", BindingFlags.Static | BindingFlags.NonPublic) });
            harmonyPatch.Invoke(harmony, args);
        }
        public static IEnumerable<T> RewriteCosmeticAnimations<T>(IEnumerable<T> instructions)
        {
            FieldInfo opcode = typeof(T).GetField("opcode"), operand = typeof(T).GetField("operand");
            int replaced = 0;
            foreach (T instruction in instructions)
            {
                MethodInfo method = operand.GetValue(instruction) as MethodInfo;
                if ((OpCode)opcode.GetValue(instruction) == OpCodes.Callvirt && method?.DeclaringType == typeof(IAnimationManager))
                {
                    string replacement = method.Name == "StartAnimation" ? (method.GetParameters()[0].ParameterType == typeof(string) ? "ReadyAnimationStart" : "ReadyAnimationMeta") : method.Name == "StopAnimation" ? "ReadyAnimationStop" : method.Name == "GetAnimationState" ? "ReadyStrokeState" : null;
                    if (replacement != null)
                    {
                        opcode.SetValue(instruction, OpCodes.Call);
                        operand.SetValue(instruction, typeof(QuietRoughSeasSystem).GetMethod(replacement, BindingFlags.Static | BindingFlags.NonPublic));
                        replaced++;
                    }
                }
                if ((OpCode)opcode.GetValue(instruction) == OpCodes.Callvirt && method != null && (Equals(method, typeof(IClientWorldAccessor).GetProperty("Player").GetGetMethod()) || Equals(method, typeof(IPlayer).GetProperty("Entity").GetGetMethod())))
                {
                    string replacement = method.Name == "get_Player" ? "ReadyPosePlayer" : "ReadyPosePlayerEntity";
                    opcode.SetValue(instruction, OpCodes.Call);
                    operand.SetValue(instruction, typeof(QuietRoughSeasSystem).GetMethod(replacement, BindingFlags.Static | BindingFlags.NonPublic));
                }
                yield return instruction;
            }
            if (replaced == 0) throw new InvalidOperationException("Pinned Rough Seas animation callsites absent");
        }
    }
}
