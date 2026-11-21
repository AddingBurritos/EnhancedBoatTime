using UnityEngine;

namespace EnhancedBoatTime
{
    /// <summary>
    /// A workbench that lives on the ship. It has no model of its own; it sits at the ship's
    /// chest and is opened from the Shipwright menu. Because it is a real CraftingStation it
    /// also lets you repair the ship with a hammer at sea.
    /// </summary>
    internal static class DeckWorkbench
    {
        public static CraftingStation Create(ShipExt ext)
        {
            var template = ZNetScene.instance ? ZNetScene.instance.GetPrefab("piece_workbench") : null;
            var src = template ? template.GetComponent<CraftingStation>() : null;
            if (!src)
            {
                Plugin.Log.LogWarning("piece_workbench not found; deck workbench unavailable");
                return null;
            }

            var go = new GameObject("EBT_DeckWorkbench");
            go.SetActive(false);
            go.transform.SetParent(ext.Container.transform, false);

            var station = go.AddComponent<CraftingStation>();
            station.m_name = src.m_name; // must match "$piece_workbench" so recipes accept it
            station.m_icon = src.m_icon;
            station.m_discoverRange = src.m_discoverRange;
            station.m_rangeBuild = 8f; // enough to repair the ship from anywhere on deck
            station.m_extraRangePerLevel = 0f;
            station.m_craftRequireRoof = false;
            station.m_craftRequireFire = false;
            station.m_showBasicRecipies = src.m_showBasicRecipies;
            station.m_useDistance = 4f;
            station.m_useAnimation = src.m_useAnimation;
            station.m_craftingSkill = src.m_craftingSkill;
            station.m_hasCraftTab = src.m_hasCraftTab;
            station.m_canRepair = true;
            station.m_upgrader = false;
            station.m_craftItemEffects = src.m_craftItemEffects;
            station.m_craftItemDoneEffects = src.m_craftItemDoneEffects;
            station.m_repairItemDoneEffects = src.m_repairItemDoneEffects;
            station.m_craftItemDoneFailEffects = src.m_craftItemDoneFailEffects;
            go.SetActive(true);
            return station;
        }

        public static void Open(ShipExt ext, Player player)
        {
            var station = ext.Workbench;
            if (!station || !player) return;
            if (!station.InUseDistance(player))
            {
                player.Message(MessageHud.MessageType.Center, "$piece_toofar");
                return;
            }
            player.SetCraftingStation(station);
            InventoryGui.instance.Show(null, 3);
        }
    }
}
