using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EnhancedBoatTime
{
    /// <summary>
    /// Added to every ship. Holds this mod's per-ship state (stored on the ship's ZDO so it is
    /// saved with the world and synced to everyone) and applies it: storage size, deck
    /// workbench, decor, and the rowing force on the ship owner's client.
    /// </summary>
    public class ShipExt : MonoBehaviour
    {
        public const string KeyStorage = "ebt_storage";
        public const string KeyStorageRows = "ebt_storage_rows";
        public const string KeyWorkbench = "ebt_workbench";
        public const string KeySail = "ebt_sail";
        public const string KeyLanterns = "ebt_lanterns";
        public const string KeyFigurehead = "ebt_figurehead";
        public const string KeyRowPower = "ebt_rowpower";
        public const string KeyRowers = "ebt_rowers";
        public const string KeySynced = "ebt_synced";

        private const string RpcSetInt = "EBT_SetInt";
        private const string RpcStroke = "EBT_Stroke";

        public static readonly List<ShipExt> All = new List<ShipExt>();

        public Ship Ship { get; private set; }
        public ZNetView NView { get; private set; }
        public Rigidbody Body { get; private set; }
        public Container Container { get; private set; }

        private CraftingStation _workbench;
        private ShipDecor _decor;
        private uint _appliedRevision = uint.MaxValue;
        private float _nextApply;

        // Rowing, owner side: latest stroke per rower.
        private struct Stroke
        {
            public int Beat;
            public float Quality;
            public float ReceivedAt;
        }

        private readonly Dictionary<long, Stroke> _strokes = new Dictionary<long, Stroke>();
        private float _publishedPower = -1f;
        private int _publishedRowers = -1;
        private int _publishedSynced = -1;

        private List<Vector3> _petSlots;

        private void Awake()
        {
            Ship = GetComponent<Ship>();
            NView = GetComponent<ZNetView>();
            Body = GetComponent<Rigidbody>();
            Container = GetComponentInChildren<Container>(true);
            _decor = new ShipDecor(this);

            NView.Register<string, int>(RpcSetInt, RPC_SetInt);
            NView.Register<int, float>(RpcStroke, RPC_Stroke);
            All.Add(this);
        }

        private void OnDestroy()
        {
            All.Remove(this);
            _decor?.Clear();
        }

        public static ShipExt Find(Player player)
        {
            if (!player) return null;
            foreach (var ext in All)
            {
                if (ext && ext.Ship.IsPlayerInBoat(player)) return ext;
            }
            return null;
        }

        public ZDO Zdo => NView && NView.IsValid() ? NView.GetZDO() : null;

        public int GetInt(string key) => Zdo?.GetInt(key) ?? 0;

        /// <summary>Asks the ship owner to store a value. Cost must already be paid by the caller.</summary>
        public void RequestSetInt(string key, int value)
        {
            if (Zdo == null) return;
            NView.InvokeRPC(RpcSetInt, key, value);
        }

        private void RPC_SetInt(long sender, string key, int value)
        {
            if (!NView.IsOwner()) return;
            Zdo.Set(key, value);
        }

        // ---------------- Visual/state application ----------------

        private void Update()
        {
            if (Zdo == null || Time.time < _nextApply) return;
            _nextApply = Time.time + 0.5f;
            if (Zdo.DataRevision == _appliedRevision) return;
            _appliedRevision = Zdo.DataRevision;
            ApplyStorage();
            ApplyWorkbench();
            _decor.Apply();
        }

        /// <summary>Called from the Container.Awake prefix so the chest is big enough before its items load.</summary>
        /// <remarks>
        /// The rows a ship has paid for are stored on the ship itself, never derived from config,
        /// so an admin changing or disabling settings can never shrink a chest that holds items.
        /// </remarks>
        public static int ExtraRowsFor(ZDO zdo)
        {
            if (zdo == null) return 0;
            int rows = zdo.GetInt(KeyStorageRows, -1);
            if (rows >= 0) return rows;
            // Ships upgraded with v0.1 only stored the level.
            return zdo.GetInt(KeyStorage) * Plugin.StorageRowsPerLevel.Value;
        }

        private void ApplyStorage()
        {
            if (!Container) return;
            var inv = Container.GetInventory();
            if (inv == null) return;
            // Container.m_height already includes the extra rows when the ship was loaded with
            // upgrades (see ContainerPatches); here we only ever grow, never shrink, so items are safe.
            int wanted = BaseHeight + ExtraRowsFor(Zdo);
            if (Access.InvHeight(inv) < wanted)
            {
                Access.InvHeight(inv) = wanted;
            }
        }

        private int? _baseHeight;
        public int BaseHeight
        {
            get
            {
                if (_baseHeight == null)
                {
                    var prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(Zdo.GetPrefab()) : null;
                    var prefabContainer = prefab ? prefab.GetComponentInChildren<Container>(true) : null;
                    _baseHeight = prefabContainer ? prefabContainer.m_height : (Container ? Container.m_height : 0);
                }
                return _baseHeight.Value;
            }
        }

        private void ApplyWorkbench()
        {
            bool want = Plugin.UpgradesEnabled.Value && GetInt(KeyWorkbench) > 0 && Container;
            if (want && !_workbench)
            {
                _workbench = DeckWorkbench.Create(this);
            }
            else if (!want && _workbench)
            {
                Destroy(_workbench.gameObject);
                _workbench = null;
            }
        }

        public CraftingStation Workbench => _workbench;

        // ---------------- Rowing ----------------

        public void SendStroke(int beat, float quality)
        {
            if (Zdo == null) return;
            NView.InvokeRPC(RpcStroke, beat, quality);
        }

        private void RPC_Stroke(long sender, int beat, float quality)
        {
            if (!NView.IsOwner()) return;
            _strokes[sender] = new Stroke { Beat = beat, Quality = Mathf.Clamp01(quality), ReceivedAt = Time.time };
        }

        private void FixedUpdate()
        {
            if (!NView.IsValid() || !NView.IsOwner()) return;
            if (!Plugin.RowingEnabled.Value)
            {
                Publish(0f, 0, 0);
                return;
            }

            float period = Mathf.Max(0.5f, Plugin.BeatInterval.Value);
            double now = ZNet.instance.GetTimeSeconds();
            int latestBeat = int.MinValue;
            int active = 0;
            float power = 0f;
            List<long> expired = null;

            foreach (var kv in _strokes)
            {
                double age = now - kv.Value.Beat * (double)period;
                if (age > period * 1.5 || Time.time - kv.Value.ReceivedAt > period * 2f)
                {
                    (expired ??= new List<long>()).Add(kv.Key);
                    continue;
                }
                active++;
                // Surge: strongest just after the catch, tapering off before the next beat.
                float surge = Mathf.Lerp(1.3f, 0.6f, Mathf.Clamp01((float)age / (period * 1.5f)));
                power += kv.Value.Quality * Plugin.PowerPerRower.Value * surge;
                if (kv.Value.Beat > latestBeat) latestBeat = kv.Value.Beat;
            }
            if (expired != null)
            {
                foreach (var key in expired) _strokes.Remove(key);
            }

            int synced = 0;
            foreach (var kv in _strokes)
            {
                if (kv.Value.Beat == latestBeat && kv.Value.Quality >= 0.6f) synced++;
            }
            if (synced >= 2)
            {
                power *= 1f + Plugin.SyncBonusPerRower.Value * (synced - 1);
            }

            Publish(power, active, synced);

            if (power <= 0f || Ship.GetSpeed() > Plugin.MaxRowingSpeed.Value || !IsAfloat()) return;
            float accel = Ship.m_backwardForce * power * Plugin.RowingForce.Value;
            Body.AddForceAtPosition(transform.forward * (accel * Body.mass * Time.fixedDeltaTime),
                Body.worldCenterOfMass, ForceMode.Impulse);
        }

        private WaterVolume _waterCache;

        private bool IsAfloat()
        {
            float water = Floating.GetWaterLevel(Body.worldCenterOfMass, ref _waterCache);
            return Body.worldCenterOfMass.y - water < 2f;
        }

        private void Publish(float power, int rowers, int synced)
        {
            if (Mathf.Abs(power - _publishedPower) > 0.02f || rowers != _publishedRowers || synced != _publishedSynced)
            {
                _publishedPower = power;
                _publishedRowers = rowers;
                _publishedSynced = synced;
                Zdo.Set(KeyRowPower, power);
                Zdo.Set(KeyRowers, rowers);
                Zdo.Set(KeySynced, synced);
            }
        }

        // ---------------- Deck geometry ----------------

        /// <summary>Raycasts down onto this ship's own colliders. Returns the deck height in world space.</summary>
        public bool TryGetDeckPoint(Vector3 localXZ, out Vector3 worldPoint)
        {
            Vector3 top = transform.TransformPoint(new Vector3(localXZ.x, 6f, localXZ.z));
            worldPoint = Vector3.zero;
            float best = float.MinValue;
            foreach (var hit in Physics.RaycastAll(top, -transform.up, 12f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.attachedRigidbody != Body) continue;
                if (hit.point.y > best)
                {
                    best = hit.point.y;
                    worldPoint = hit.point;
                }
            }
            return best > float.MinValue;
        }

        public BoxCollider FloatCollider => Ship.m_floatCollider;

        /// <summary>Local-space spots on the deck where pets can stand, nearest the middle first.</summary>
        public List<Vector3> PetSlots
        {
            get
            {
                if (_petSlots != null) return _petSlots;
                _petSlots = new List<Vector3>();
                var fc = FloatCollider;
                if (!fc) return _petSlots;
                Vector3 center = transform.InverseTransformPoint(fc.transform.position);
                float halfLen = fc.size.z * 0.35f;
                float halfWidth = fc.size.x * 0.2f;
                var candidates = new List<Vector3>();
                for (float z = -halfLen; z <= halfLen; z += 1.3f)
                {
                    candidates.Add(new Vector3(center.x, 0f, center.z + z));
                    if (halfWidth > 0.6f)
                    {
                        candidates.Add(new Vector3(center.x - halfWidth, 0f, center.z + z));
                        candidates.Add(new Vector3(center.x + halfWidth, 0f, center.z + z));
                    }
                }
                candidates.Sort((a, b) => (a - center).sqrMagnitude.CompareTo((b - center).sqrMagnitude));
                foreach (var c in candidates)
                {
                    if (TryGetDeckPoint(c, out var world) && !NearFixture(world))
                    {
                        _petSlots.Add(transform.InverseTransformPoint(world) + Vector3.up * 0.1f);
                    }
                }
                return _petSlots;
            }
        }

        private bool NearFixture(Vector3 world)
        {
            foreach (var chair in GetComponentsInChildren<Chair>())
            {
                if (chair.m_attachPoint && Vector3.Distance(chair.m_attachPoint.position, world) < 0.9f) return true;
            }
            if (Ship.m_shipControlls && Vector3.Distance(Ship.m_shipControlls.transform.position, world) < 1.2f) return true;
            if (Container && Vector3.Distance(Container.transform.position, world) < 1.0f) return true;
            if (Ship.m_mastObject && Vector3.Distance(Ship.m_mastObject.transform.position, world) < 0.8f) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Ship), "Awake")]
    internal static class ShipAwakePatch
    {
        private static void Postfix(Ship __instance)
        {
            var nview = Access.ShipNView(__instance);
            if (nview && nview.GetZDO() != null && !__instance.GetComponent<ShipExt>())
            {
                __instance.gameObject.AddComponent<ShipExt>();
            }
        }
    }
}
