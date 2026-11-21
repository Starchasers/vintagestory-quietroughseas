using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace QuietRoughSeas
{
    public partial class QuietRoughSeasSystem
    {
        /// <summary>Whether lightning still hurts animals. Read from the config file.</summary>
        static bool lightningDamagesAnimals = new QuietRoughSeasConfig().LightningDamagesAnimals;

        // Rough Seas strikes its storm lightning through the game's own
        // WeatherSystemServer.SpawnLightningFlash, and the game's flash then
        // damages every creature around the strike through Entity.ReceiveDamage.
        // Every override of ReceiveDamage in the game ends up in that base
        // method, so the guard sits there. It cannot tell a Rough Seas strike
        // from one of the game's own thunderstorms: both are spared.
        void InstallLightning()
        {
            List<MethodInfo> targets = new List<MethodInfo>();
            MethodInfo receive = typeof(Entity).GetMethod("ReceiveDamage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null, new Type[] { typeof(DamageSource), typeof(float) }, null);
            if (receive != null && receive.ReturnType == typeof(bool)) targets.Add(receive);

            if (Apply(targets, "SpareAnimalFromLightning", null) > 0) Mod.Logger.Notification("Lightning damage to animals is {0}.", lightningDamagesAnimals ? "left on by the config" : "off");
            else Mod.Logger.Warning("Found no entity damage method to patch. Lightning still hurts animals; this mod needs an update.");
        }

        // "damageSource" is matched by name to the parameter of Entity.ReceiveDamage.
        // Players are still hurt, and a vessel is no EntityAgent, so a strike still breaks a hull.
        static bool SpareAnimalFromLightning(Entity __instance, DamageSource damageSource, ref bool __result)
        {
            if (lightningDamagesAnimals || damageSource == null) return true;
            if (damageSource.Source != EnumDamageSource.Weather || damageSource.Type != EnumDamageType.Electricity) return true;
            if (!(__instance is EntityAgent) || __instance is EntityPlayer) return true;

            __result = false;
            return false;
        }
    }
}
