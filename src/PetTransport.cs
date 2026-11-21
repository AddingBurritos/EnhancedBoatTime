using HarmonyLib;
using UnityEngine;

namespace EnhancedBoatTime
{
    /// <summary>
    /// Tamed creatures that are following a player jump aboard when that player boards a ship,
    /// hold their spot on deck while sailing (their AI is paused), and hop ashore next to the
    /// player when the player steps onto land. Pets told to stay remain aboard.
    ///
    /// State lives on the pet's ZDO so it survives logouts and zone reloads. All of this runs
    /// on the client that owns the pet, the same client that runs its AI.
    /// </summary>
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    internal static class PetTransport
    {
        public const string KeyShip = "ebt_pet_ship";
        public const string KeySlot = "ebt_pet_slot";

        private static bool Prefix(MonsterAI __instance, float dt, ref bool __result)
        {
            if (!Plugin.PetTransportEnabled.Value) return true;
            var nview = Access.AINView(__instance);
            if (!nview || !nview.IsValid() || !nview.IsOwner()) return true;
            var character = Access.AICharacter(__instance);
            if (!character || !character.IsTamed()) return true;

            var passenger = character.GetComponent<PetPassenger>();
            if (!passenger) passenger = character.gameObject.AddComponent<PetPassenger>();
            if (passenger.Tick(__instance, character, nview.GetZDO()))
            {
                __result = true;
                return false; // aboard: skip the normal AI
            }
            return true;
        }
    }

    public class PetPassenger : MonoBehaviour
    {
        private static readonly System.Collections.Generic.List<PetPassenger> All =
            new System.Collections.Generic.List<PetPassenger>();

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private float _nextCheck;
        private float _shipMissingSince = -1f;
        private ShipExt _ship;
        private Rigidbody _body;
        private Tameable _tameable;

        /// <summary>Returns true while the pet is aboard (AI suppressed).</summary>
        public bool Tick(MonsterAI ai, Character character, ZDO zdo)
        {
            if (!_body) _body = GetComponent<Rigidbody>();
            if (!_tameable) _tameable = GetComponent<Tameable>();
            if (_tameable && _tameable.HaveRider()) return false; // being ridden: the rider is in charge
            ZDOID shipId = zdo.GetZDOID(PetTransport.KeyShip);
            var followPlayer = ai.GetFollowTarget() ? ai.GetFollowTarget().GetComponent<Player>() : null;

            if (shipId.IsNone())
            {
                if (Time.time >= _nextCheck)
                {
                    _nextCheck = Time.time + 1f;
                    TryBoard(character, zdo, followPlayer);
                }
                return false;
            }

            // Aboard.
            if (!_ship || _ship.Zdo == null || _ship.Zdo.m_uid != shipId)
            {
                var go = ZNetScene.instance.FindInstance(shipId);
                _ship = go ? go.GetComponent<ShipExt>() : null;
            }
            if (!_ship)
            {
                // Ship not loaded (yet) or gone. Give it a while before letting the pet go.
                if (_shipMissingSince < 0f) _shipMissingSince = Time.time;
                if (Time.time - _shipMissingSince > 20f) Release(zdo);
                return false;
            }
            _shipMissingSince = -1f;

            ai.StopMoving();
            HoldSpot(zdo);

            if (Time.time >= _nextCheck)
            {
                _nextCheck = Time.time + 0.5f;
                if (followPlayer && ShouldDisembark(followPlayer))
                {
                    Vector3 shore = followPlayer.transform.position - followPlayer.transform.forward * 1.5f
                                    + Random.insideUnitSphere.Horizontal() * 1f;
                    Teleport(shore + Vector3.up * 0.3f, Vector3.zero);
                    Release(zdo);
                    return false;
                }
            }
            return true;
        }

        private void TryBoard(Character character, ZDO zdo, Player followPlayer)
        {
            if (!followPlayer) return;
            var ext = ShipExt.Find(followPlayer);
            if (!ext || Mathf.Abs(ext.Ship.GetSpeed()) > 3f) return;
            float range = Plugin.PetBoardRange.Value;
            if (Vector3.Distance(transform.position, ext.transform.position) > range + ext.FloatCollider.size.z * 0.5f) return;

            int slot = FreeSlot(ext);
            if (slot < 0)
            {
                return; // deck is full
            }
            zdo.Set(PetTransport.KeyShip, ext.Zdo.m_uid);
            zdo.Set(PetTransport.KeySlot, slot);
            _ship = ext;
            Vector3 spot = ext.transform.TransformPoint(ext.PetSlots[slot]);
            Teleport(spot + Vector3.up * 0.2f, ext.Body.GetPointVelocity(spot));
            followPlayer.Message(MessageHud.MessageType.TopLeft, Localizations.L("ebt_pet_aboard", character.GetHoverName()));
        }

        private static int FreeSlot(ShipExt ext)
        {
            var slots = ext.PetSlots;
            var taken = new bool[slots.Count];
            foreach (var p in All)
            {
                var nv = p.GetComponent<ZNetView>();
                var z = nv && nv.IsValid() ? nv.GetZDO() : null;
                if (z == null || z.GetZDOID(PetTransport.KeyShip) != ext.Zdo.m_uid) continue;
                int s = z.GetInt(PetTransport.KeySlot, -1);
                if (s >= 0 && s < taken.Length) taken[s] = true;
            }
            for (int i = 0; i < taken.Length; i++)
            {
                if (!taken[i]) return i;
            }
            return -1;
        }

        private void HoldSpot(ZDO zdo)
        {
            var slots = _ship.PetSlots;
            int slot = zdo.GetInt(PetTransport.KeySlot);
            if (slot < 0 || slot >= slots.Count) return;
            Vector3 spot = _ship.transform.TransformPoint(slots[slot]);
            Vector3 delta = transform.position - spot;
            // Fell over the side, got shoved, or the ship lurched: put it back.
            if (new Vector2(delta.x, delta.z).magnitude > 1.5f || delta.y < -1.5f || delta.y > 3f)
            {
                Teleport(spot + Vector3.up * 0.2f, _ship.Body.GetPointVelocity(spot));
            }
        }

        private bool ShouldDisembark(Player player)
        {
            if (_ship.Ship.IsPlayerInBoat(player)) return false;
            if (player.IsSwimming() || player.InNumShipVolumes > 0) return false;
            if (Vector3.Distance(player.transform.position, _ship.transform.position) > 40f) return false;
            // Standing on land or a dock, not treading water.
            return ZoneSystem.instance.GetSolidHeight(player.transform.position) >= ZoneSystem.instance.m_waterLevel - 0.2f;
        }

        private void Teleport(Vector3 pos, Vector3 velocity)
        {
            transform.position = pos;
            if (_body)
            {
                _body.position = pos;
                _body.linearVelocity = velocity;
                _body.angularVelocity = Vector3.zero;
            }
        }

        private void Release(ZDO zdo)
        {
            zdo.Set(PetTransport.KeyShip, ZDOID.None);
            zdo.Set(PetTransport.KeySlot, -1);
            _ship = null;
            _shipMissingSince = -1f;
        }
    }

    internal static class VectorExt
    {
        public static Vector3 Horizontal(this Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
