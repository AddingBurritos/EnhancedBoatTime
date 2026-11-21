using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace EnhancedBoatTime
{
    /// <summary>
    /// Fishing from a ship:
    ///  - the reel pays out line as the ship moves, so the line no longer snaps the moment you sail;
    ///  - trolling: dragging bait behind a moving ship occasionally draws a strike from a fish that
    ///    lives in these waters and likes your bait. At speed the hook sets itself; when the ship is
    ///    slow you still need to pull (block) on the nibble, as usual.
    /// </summary>
    [HarmonyPatch(typeof(FishingFloat), "FixedUpdate")]
    internal static class BoatFishing
    {
        private class FloatState
        {
            public float NextRoll;
            public bool WarnedNoFish;
        }

        private static readonly ConditionalWeakTable<FishingFloat, FloatState> States =
            new ConditionalWeakTable<FishingFloat, FloatState>();

        private static void Prefix(FishingFloat __instance)
        {
            if (!Plugin.BoatFishingEnabled.Value) return;
            var nview = Access.FloatNView(__instance);
            if (!nview || !nview.IsValid() || !nview.IsOwner()) return;

            var owner = Access.FloatGetOwner(__instance) as Player;
            if (!owner || owner.InNumShipVolumes == 0) return;
            var ext = ShipExt.Find(owner);
            if (!ext) return;

            if (Plugin.LinePaysOut.Value && !owner.IsBlocking())
            {
                var rodTop = Utils.FindChild(owner.transform, "_RodTop");
                if (rodTop)
                {
                    float dist = Vector3.Distance(rodTop.position, __instance.transform.position);
                    float maxLine = __instance.m_maxDistance * 0.9f;
                    ref float line = ref Access.FloatLineLength(__instance);
                    if (dist > line && line < maxLine)
                    {
                        line = Mathf.Min(dist, maxLine);
                    }
                }
            }

            if (Plugin.TrollingEnabled.Value)
            {
                Troll(__instance, owner, ext);
            }
        }

        private static void Troll(FishingFloat ff, Player owner, ShipExt ext)
        {
            var state = States.GetOrCreateValue(ff);
            if (Time.time < state.NextRoll) return;
            state.NextRoll = Time.time + Mathf.Max(1f, Plugin.TrollingInterval.Value);

            if (!ff.IsInWater() || ff.GetCatch() || Access.FloatNibbler(ff)) return;
            if (Mathf.Abs(ext.Ship.GetSpeed()) < Plugin.TrollingMinSpeed.Value) return;
            if (CountFishNear(ff.transform.position, 15f) >= 3) return;

            var bait = ff.GetBait();
            var biome = WorldGenerator.instance.GetBiome(ff.transform.position);
            var fishPrefab = FishCatalog.Pick(biome, bait, out bool anyForBiome);
            if (!fishPrefab)
            {
                if (!state.WarnedNoFish)
                {
                    state.WarnedNoFish = true;
                    owner.Message(MessageHud.MessageType.TopLeft,
                        Localizations.L(anyForBiome ? "ebt_fish_wrongbait" : "ebt_fish_nofish"));
                }
                return;
            }
            if (Random.value > Plugin.TrollingChance.Value) return;

            Vector3 pos = ff.transform.position + Vector3.down * 1.2f;
            var go = Object.Instantiate(fishPrefab, pos, Quaternion.LookRotation(-ext.transform.forward));
            var fish = go.GetComponent<Fish>();
            if (!fish) return;
            ff.Nibble(fish, true);
            owner.Message(MessageHud.MessageType.Center, Localizations.L("ebt_fish_strike"));
        }

        private static int CountFishNear(Vector3 pos, float range)
        {
            int n = 0;
            float r2 = range * range;
            foreach (var updater in Fish.Instances)
            {
                if (updater is Fish fish && fish && (fish.transform.position - pos).sqrMagnitude < r2) n++;
            }
            return n;
        }
    }

    /// <summary>Fish that naturally spawn in each biome, read from the game's own spawn tables.</summary>
    internal static class FishCatalog
    {
        private struct Entry
        {
            public GameObject Prefab;
            public Fish Fish;
            public Heightmap.Biome Biome;
            public string RequiredGlobalKey;
        }

        private static List<Entry> _entries;

        private static void Build()
        {
            _entries = new List<Entry>();
            var seen = new HashSet<string>();
            foreach (var list in Resources.FindObjectsOfTypeAll<SpawnSystemList>())
            {
                foreach (var spawn in list.m_spawners)
                {
                    if (!spawn.m_enabled || !spawn.m_prefab) continue;
                    var fish = spawn.m_prefab.GetComponent<Fish>();
                    if (!fish || fish.m_baits == null || fish.m_baits.Count == 0) continue;
                    if (!seen.Add(spawn.m_prefab.name + "|" + (int)spawn.m_biome)) continue;
                    _entries.Add(new Entry
                    {
                        Prefab = spawn.m_prefab, Fish = fish, Biome = spawn.m_biome,
                        RequiredGlobalKey = spawn.m_requiredGlobalKey
                    });
                }
            }
            Plugin.Log.LogInfo($"Fish catalog: {_entries.Count} biome/fish entries");
        }

        public static GameObject Pick(Heightmap.Biome biome, string bait, out bool anyForBiome)
        {
            if (_entries == null || _entries.Count == 0) Build();
            anyForBiome = false;
            float total = 0f;
            var options = new List<KeyValuePair<GameObject, float>>();
            foreach (var e in _entries)
            {
                if ((e.Biome & biome) == 0) continue;
                if (!string.IsNullOrEmpty(e.RequiredGlobalKey) && ZoneSystem.instance
                    && !ZoneSystem.instance.GetGlobalKey(e.RequiredGlobalKey)) continue;
                anyForBiome = true;
                foreach (var b in e.Fish.m_baits)
                {
                    if (b.m_bait && b.m_bait.name == bait && b.m_chance > 0f)
                    {
                        options.Add(new KeyValuePair<GameObject, float>(e.Prefab, b.m_chance));
                        total += b.m_chance;
                        break;
                    }
                }
            }
            if (options.Count == 0) return null;
            float roll = Random.value * total;
            foreach (var o in options)
            {
                roll -= o.Value;
                if (roll <= 0f) return o.Key;
            }
            return options[options.Count - 1].Key;
        }
    }
}
