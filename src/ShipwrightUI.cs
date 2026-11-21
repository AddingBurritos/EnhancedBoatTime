using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EnhancedBoatTime
{
    /// <summary>
    /// The Shipwright menu, opened with Shift+E on a ship's chest. Lists every upgrade and decor
    /// option with its cost; whoever buys an upgrade pays for it from their own inventory.
    /// </summary>
    public class ShipwrightUI : MonoBehaviour
    {
        private static ShipwrightUI _instance;
        private static ShipExt _ship;

        public static bool IsOpen => _ship;

        private Rect _window = new Rect(0, 0, 460, 560);
        private Vector2 _scroll;
        private GUIStyle _rich;
        private GUIStyle _header;

        private void Awake()
        {
            _instance = this;
        }

        public static void Open(ShipExt ship)
        {
            _ship = ship;
            if (_instance)
            {
                _instance._window.x = (Screen.width - _instance._window.width) / 2f;
                _instance._window.y = (Screen.height - _instance._window.height) / 2f;
            }
        }

        public static void Close()
        {
            _ship = null;
        }

        private void Update()
        {
            if (!IsOpen) return;
            var player = Player.m_localPlayer;
            if (!player || player.IsDead() || !_ship.Container
                || Vector3.Distance(player.transform.position, _ship.Container.transform.position) > 6f
                || InventoryGui.IsVisible())
            {
                Close();
            }
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            EnsureStyles();
            _window = GUILayout.Window(0x5EB0A7, _window, DrawWindow, Localizations.L("ebt_shipwright"));
        }

        private void DrawWindow(int id)
        {
            var player = Player.m_localPlayer;
            if (!player || !_ship) return;

            _scroll = GUILayout.BeginScrollView(_scroll);

            // Storage
            int storage = _ship.GetInt(ShipExt.KeyStorage);
            int max = Plugin.StorageMaxLevel.Value;
            Section(Localizations.L("ebt_sw_hold", storage.ToString(), max.ToString()));
            GUILayout.Label(Localizations.L("ebt_sw_hold_desc", Plugin.StorageRowsPerLevel.Value.ToString()), _rich);
            if (storage < max)
            {
                int rows = ShipExt.ExtraRowsFor(_ship.Zdo);
                BuyRow(Localizations.L("ebt_sw_hold_buy"), Plugin.StorageCost.Value, () =>
                {
                    _ship.RequestSetInt(ShipExt.KeyStorageRows, rows + Plugin.StorageRowsPerLevel.Value);
                    _ship.RequestSetInt(ShipExt.KeyStorage, storage + 1);
                });
            }

            // Workbench
            Section(Localizations.L("ebt_sw_bench"));
            if (_ship.GetInt(ShipExt.KeyWorkbench) > 0)
            {
                GUILayout.Label(Localizations.L("ebt_sw_bench_desc"), _rich);
                if (GUILayout.Button(Localizations.L("ebt_sw_bench_open")))
                {
                    var ship = _ship;
                    Close();
                    DeckWorkbench.Open(ship, player);
                }
            }
            else
            {
                GUILayout.Label(Localizations.L("ebt_sw_bench_desc"), _rich);
                BuyRow(Localizations.L("ebt_sw_bench_buy"), Plugin.WorkbenchCost.Value, () =>
                    _ship.RequestSetInt(ShipExt.KeyWorkbench, 1));
            }

            // Lanterns
            Section(Localizations.L("ebt_sw_lanterns"));
            if (_ship.GetInt(ShipExt.KeyLanterns) > 0)
            {
                if (GUILayout.Button(Localizations.L("ebt_sw_lanterns_remove"))) _ship.RequestSetInt(ShipExt.KeyLanterns, 0);
            }
            else
            {
                BuyRow(Localizations.L("ebt_sw_lanterns_buy"), Plugin.LanternCost.Value, () =>
                    _ship.RequestSetInt(ShipExt.KeyLanterns, 1));
            }

            // Sail dye
            if (_ship.Ship.m_sailObject)
            {
                int current = _ship.GetInt(ShipExt.KeySail);
                Section(Localizations.L("ebt_sw_dye", Localize(ShipDecor.Dyes[Mathf.Clamp(current, 0, ShipDecor.Dyes.Length - 1)].Name)));
                for (int i = 0; i < ShipDecor.Dyes.Length; i++)
                {
                    if (i == current) continue;
                    int index = i;
                    var dye = ShipDecor.Dyes[i];
                    string hex = ColorUtility.ToHtmlStringRGB(dye.Color);
                    BuyRow($"<color=#{hex}>■</color> {Localize(dye.Name)}", dye.Cost, () =>
                        _ship.RequestSetInt(ShipExt.KeySail, index));
                }
            }

            // Figurehead
            Section(Localizations.L("ebt_sw_figurehead"));
            int figurehead = _ship.GetInt(ShipExt.KeyFigurehead);
            if (figurehead != 0)
            {
                var item = ObjectDB.instance.GetItemPrefab(figurehead);
                string name = item ? Localize(item.GetComponent<ItemDrop>().m_itemData.m_shared.m_name) : "?";
                GUILayout.Label(Localizations.L("ebt_sw_figurehead_mounted", name), _rich);
                if (GUILayout.Button(Localizations.L("ebt_sw_figurehead_remove")))
                {
                    ReturnTrophy(player, figurehead);
                    _ship.RequestSetInt(ShipExt.KeyFigurehead, 0);
                }
            }
            var trophies = TrophiesIn(player);
            if (trophies.Count == 0)
            {
                GUILayout.Label(Localizations.L("ebt_sw_figurehead_none"), _rich);
            }
            foreach (var trophy in trophies)
            {
                var t = trophy;
                BuyRow(Localizations.L("ebt_sw_figurehead_buy", Localize(t.m_shared.m_name)), Plugin.FigureheadExtraCost.Value, () =>
                {
                    if (!player.NoCostCheat()) player.GetInventory().RemoveItem(t.m_shared.m_name, 1);
                    if (figurehead != 0) ReturnTrophy(player, figurehead);
                    _ship.RequestSetInt(ShipExt.KeyFigurehead, t.m_dropPrefab.name.GetStableHashCode());
                });
            }

            GUILayout.EndScrollView();
            GUILayout.Space(6);
            if (GUILayout.Button(Localizations.L("ebt_sw_close"))) Close();
            GUI.DragWindow();
        }

        private void Section(string title)
        {
            GUILayout.Space(8);
            GUILayout.Label(title, _header);
        }

        /// <summary>A button plus its cost; disabled (with missing items in red) when unaffordable.</summary>
        private void BuyRow(string label, string cost, System.Action onBuy)
        {
            var player = Player.m_localPlayer;
            var reqs = Cost.Parse(cost);
            bool free = player.NoCostCheat();
            bool canAfford = free || Cost.CanAfford(player, reqs);

            GUILayout.BeginHorizontal();
            GUI.enabled = canAfford;
            if (GUILayout.Button(label, GUILayout.Width(230)))
            {
                if (free || Cost.TryPay(player, reqs))
                {
                    onBuy();
                    player.Message(MessageHud.MessageType.Center, Localizations.L("ebt_sw_done"));
                }
            }
            GUI.enabled = true;
            GUILayout.Label(reqs.Count == 0 ? Localizations.L("ebt_sw_free") : Cost.Describe(player, reqs), _rich);
            GUILayout.EndHorizontal();
        }

        private static List<ItemDrop.ItemData> TrophiesIn(Player player)
        {
            var result = new List<ItemDrop.ItemData>();
            var seen = new HashSet<string>();
            foreach (var item in player.GetInventory().GetAllItems())
            {
                if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy && item.m_dropPrefab
                    && ItemStand.GetAttachPrefab(item.m_dropPrefab) && seen.Add(item.m_shared.m_name))
                {
                    result.Add(item);
                }
            }
            return result;
        }

        private static void ReturnTrophy(Player player, int itemHash)
        {
            var prefab = ObjectDB.instance.GetItemPrefab(itemHash);
            if (!prefab) return;
            if (player.GetInventory().CanAddItem(prefab, 1))
            {
                player.GetInventory().AddItem(prefab, 1);
            }
            else
            {
                Instantiate(prefab, player.transform.position + Vector3.up, Quaternion.identity);
            }
        }

        private static string Localize(string s) => Localization.instance.Localize(s);

        private void EnsureStyles()
        {
            if (_rich != null) return;
            _rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            _header = new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold, fontSize = 15 };
        }

        // ---- Keep the cursor free and the player still while the window is open ----

        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
        private static class CursorPatch
        {
            private static bool Prefix()
            {
                if (!IsOpen) return true;
                ZCursor.LockState = CursorLockMode.None;
                ZCursor.Show();
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), "TakeInput")]
        private static class PlayerInputPatch
        {
            private static void Postfix(ref bool __result)
            {
                if (IsOpen) __result = false;
            }
        }

        [HarmonyPatch(typeof(PlayerController), "TakeInput")]
        private static class ControllerInputPatch
        {
            private static void Postfix(ref bool __result)
            {
                if (IsOpen) __result = false;
            }
        }

        [HarmonyPatch(typeof(Menu), "Update")]
        private static class MenuPatch
        {
            private static bool Prefix()
            {
                if (!IsOpen) return true;
                if (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB")) Close();
                return false; // Escape closes our window instead of opening the game menu
            }
        }
    }

    internal static class Cost
    {
        public struct Req
        {
            public string Prefab;
            public string SharedName;
            public int Amount;
        }

        private static readonly Dictionary<string, List<Req>> Cache = new Dictionary<string, List<Req>>();

        public static List<Req> Parse(string spec)
        {
            spec ??= "";
            if (Cache.TryGetValue(spec, out var cached)) return cached;
            var list = new List<Req>();
            if (!ObjectDB.instance) return list;
            Cache[spec] = list;
            foreach (var part in spec.Split(','))
            {
                var kv = part.Split(':');
                if (kv.Length != 2 || !int.TryParse(kv[1].Trim(), out int amount) || amount <= 0) continue;
                var prefab = ObjectDB.instance.GetItemPrefab(kv[0].Trim());
                var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
                if (!drop)
                {
                    Plugin.Log.LogWarning($"Unknown item in cost '{spec}': {kv[0]}");
                    continue;
                }
                list.Add(new Req { Prefab = prefab.name, SharedName = drop.m_itemData.m_shared.m_name, Amount = amount });
            }
            return list;
        }

        public static bool CanAfford(Player player, List<Req> reqs)
        {
            foreach (var r in reqs)
            {
                if (player.GetInventory().CountItems(r.SharedName) < r.Amount) return false;
            }
            return true;
        }

        public static bool TryPay(Player player, List<Req> reqs)
        {
            if (!CanAfford(player, reqs)) return false;
            foreach (var r in reqs) player.GetInventory().RemoveItem(r.SharedName, r.Amount);
            return true;
        }

        public static string Describe(Player player, List<Req> reqs)
        {
            var parts = new List<string>();
            foreach (var r in reqs)
            {
                int have = player.GetInventory().CountItems(r.SharedName);
                string color = have >= r.Amount ? "#c8e6c9" : "#ff8a80";
                parts.Add($"<color={color}>{r.Amount} {Localization.instance.Localize(r.SharedName)}</color>");
            }
            return string.Join(", ", parts);
        }
    }
}
