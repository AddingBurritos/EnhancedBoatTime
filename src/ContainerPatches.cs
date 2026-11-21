using HarmonyLib;
using UnityEngine;

namespace EnhancedBoatTime
{
    internal static class ContainerPatches
    {
        private static ZNetView ShipNViewFor(Container container)
        {
            var ship = container.GetComponentInParent<Ship>();
            return ship ? ship.GetComponent<ZNetView>() : null;
        }

        /// <summary>
        /// Grow the chest before the inventory is created, so items saved in upgraded rows are
        /// never dropped when the ship loads.
        /// </summary>
        [HarmonyPatch(typeof(Container), "Awake")]
        private static class AwakePatch
        {
            private static void Prefix(Container __instance)
            {
                var nview = ShipNViewFor(__instance);
                if (!nview) return;
                __instance.m_height += ShipExt.ExtraRowsFor(nview.GetZDO());
            }
        }

        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
        private static class HoverPatch
        {
            private static void Postfix(Container __instance, ref string __result)
            {
                if (!Plugin.UpgradesEnabled.Value || !__instance.GetComponentInParent<Ship>()) return;
                __result += Localization.instance.Localize(
                    "\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $ebt_shipwright");
            }
        }

        [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
        private static class InteractPatch
        {
            private static bool Prefix(Container __instance, Humanoid character, bool hold, bool alt, ref bool __result)
            {
                if (!alt || hold || !Plugin.UpgradesEnabled.Value) return true;
                var ship = __instance.GetComponentInParent<Ship>();
                var ext = ship ? ship.GetComponent<ShipExt>() : null;
                if (!ext || character != Player.m_localPlayer) return true;
                ShipwrightUI.Open(ext);
                __result = true;
                return false;
            }
        }
    }
}
