using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;

namespace QuietRoughSeas
{
    public partial class QuietRoughSeasSystem
    {
        // Only the three state reads in the pinned MountStrokes implementation.
        // Keep its virtual lookup (including FP/-ifp routing), not Animator's raw lookup.
        static RunningAnimation ReadyStrokeState(IAnimationManager manager, string code)
        {
            return manager == null || manager.Animator == null ? null : manager.GetAnimationState(code);
        }

        void InstallRowingAnimations(List<Type> types)
        {
            Type mount = types.Find(t => t.FullName == "RoughSeas.Propulsion.MountStrokes");
            if (mount == null) throw new TypeLoadException("RoughSeas.Propulsion.MountStrokes");
            Assembly harmonyAssembly = harmonyMethodType.Assembly;
            Type instruction = harmonyAssembly.GetType("HarmonyLib.CodeInstruction", true);
            MethodInfo rewrite = typeof(QuietRoughSeasSystem).GetMethod("RewriteStrokeReads", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(instruction);
            object transpiler = Activator.CreateInstance(harmonyMethodType, new object[] { rewrite });
            foreach (string name in new[] { "DrawnShare", "Pace", "Finish" })
            {
                MethodInfo target = mount.GetMethod(name, Declared);
                if (target == null) throw new MissingMethodException(mount.FullName, name);
                object[] args = new object[harmonyPatch.GetParameters().Length];
                args[0] = target;
                args[3] = transpiler;
                harmonyPatch.Invoke(harmony, args);
            }
            Mod.Logger.Notification("Guarded Rough Seas rowing animation reads until each manager's animator is ready.");
        }

        // Closed over Harmony's actual CodeInstruction type at runtime so the
        // installable source mod still needs no compile-time Harmony reference.
        static IEnumerable<T> RewriteStrokeReads<T>(IEnumerable<T> instructions)
        {
            FieldInfo opcode = typeof(T).GetField("opcode");
            FieldInfo operand = typeof(T).GetField("operand");
            MethodInfo read = typeof(IAnimationManager).GetMethod("GetAnimationState", new[] { typeof(string) });
            MethodInfo safe = typeof(QuietRoughSeasSystem).GetMethod("ReadyStrokeState", BindingFlags.Static | BindingFlags.NonPublic);
            int replaced = 0;
            foreach (T instruction in instructions)
            {
                if ((OpCode)opcode.GetValue(instruction) == OpCodes.Callvirt && Equals(operand.GetValue(instruction), read))
                {
                    opcode.SetValue(instruction, OpCodes.Call);
                    operand.SetValue(instruction, safe);
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("Expected exactly one MountStrokes animation state read; found " + replaced);
        }
    }
}
