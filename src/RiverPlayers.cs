using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace QuietRoughSeas
{
    public partial class QuietRoughSeasSystem
    {
        static bool riversPushPlayers;

        void InstallRiverPlayers(ICoreAPI api)
        {
            if (api.ModLoader.GetMod(RiversModId) == null) return;
            try
            {
                var targets = new List<MethodInfo>();
                foreach (Type t in ModTypes(api, RiversModId))
                {
                    if (t.Name != "DoApplyPrefix" || t.DeclaringType == null || t.DeclaringType.Name != "EntityBehaviorPhysicsPatch") continue;
                    var method = t.GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static, null,
                        new Type[] { typeof(PModuleInLiquid), typeof(float), typeof(Entity), typeof(EntityPos), typeof(EntityControls) }, null);
                    if (method != null && method.ReturnType == typeof(bool)) targets.Add(method);
                }
                if (Apply(targets, "RiverPlayerCurrentPrefix", null) == 0)
                    api.Logger.Warning("[QuietRoughSeas] Rivers player current prefix not found; player currents remain upstream.");
            }
            catch (Exception e) { api.Logger.Warning("[QuietRoughSeas] Could not control Rivers player currents: {0}", e); }
        }

        // This prefixes Rivers' prefix ITSELF, not vanilla DoApply. __2 is its entity argument.
        // A true RESULT lets Rivers' caller continue vanilla DoApply even though we skip Rivers' body.
        static bool RiverPlayerCurrentPrefix(Entity __2, ref bool __result)
        {
            if (riversPushPlayers || !(__2 is EntityPlayer)) return true;
            __result = true;
            return false;
        }
    }
}
