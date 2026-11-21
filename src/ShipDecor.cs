using System.Collections.Generic;
using UnityEngine;

namespace EnhancedBoatTime
{
    public struct SailDye
    {
        public string Name;
        public Color Color;
        public string Cost;

        public SailDye(string name, Color color, string cost)
        {
            Name = name;
            Color = color;
            Cost = cost;
        }
    }

    /// <summary>Client-side visuals driven by the ship's ZDO: sail dye, deck lanterns, trophy figurehead.</summary>
    public class ShipDecor
    {
        public static readonly SailDye[] Dyes =
        {
            new SailDye("$ebt_dye_natural", Color.white, ""),
            new SailDye("$ebt_dye_crimson", new Color(0.78f, 0.16f, 0.13f), "Raspberry:10"),
            new SailDye("$ebt_dye_blue", new Color(0.22f, 0.33f, 0.82f), "Blueberries:10"),
            new SailDye("$ebt_dye_green", new Color(0.3f, 0.55f, 0.25f), "Guck:3"),
            new SailDye("$ebt_dye_gold", new Color(0.95f, 0.78f, 0.25f), "Dandelion:10"),
            new SailDye("$ebt_dye_black", new Color(0.18f, 0.18f, 0.2f), "Coal:10"),
        };

        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly ShipExt _ext;
        private int _appliedSail = -1;
        private int _appliedLanterns = -1;
        private int _appliedFigurehead = -1;
        private readonly List<GameObject> _lanterns = new List<GameObject>();
        private GameObject _figurehead;

        public ShipDecor(ShipExt ext)
        {
            _ext = ext;
        }

        public void Apply()
        {
            int sail = Mathf.Clamp(_ext.GetInt(ShipExt.KeySail), 0, Dyes.Length - 1);
            if (sail != _appliedSail)
            {
                _appliedSail = sail;
                ApplySail(sail);
            }

            int lanterns = _ext.GetInt(ShipExt.KeyLanterns);
            if (lanterns != _appliedLanterns)
            {
                _appliedLanterns = lanterns;
                ApplyLanterns(lanterns > 0);
            }

            int figurehead = _ext.GetInt(ShipExt.KeyFigurehead);
            if (figurehead != _appliedFigurehead)
            {
                _appliedFigurehead = figurehead;
                ApplyFigurehead(figurehead);
            }
        }

        public void Clear()
        {
            foreach (var go in _lanterns)
            {
                if (go) Object.Destroy(go);
            }
            _lanterns.Clear();
            if (_figurehead) Object.Destroy(_figurehead);
        }

        private void ApplySail(int index)
        {
            var sail = _ext.Ship.m_sailObject;
            if (!sail) return;
            var block = new MaterialPropertyBlock();
            foreach (var renderer in sail.GetComponentsInChildren<Renderer>(true))
            {
                if (index == 0)
                {
                    renderer.SetPropertyBlock(null);
                    continue;
                }
                renderer.GetPropertyBlock(block);
                block.SetColor(ColorId, Dyes[index].Color);
                renderer.SetPropertyBlock(block);
            }
        }

        private void ApplyLanterns(bool on)
        {
            foreach (var go in _lanterns)
            {
                if (go) Object.Destroy(go);
            }
            _lanterns.Clear();
            if (!on) return;

            var fc = _ext.FloatCollider;
            if (!fc) return;
            Transform t = _ext.transform;
            Vector3 center = t.InverseTransformPoint(fc.transform.position);
            float halfLen = fc.size.z * 0.5f;

            // Bow, stern, and one at the mast if there is one.
            var spots = new List<Vector3>();
            if (FindDeckAlong(center, halfLen * 0.75f, -0.1f, out var bow)) spots.Add(bow);
            if (FindDeckAlong(center, -halfLen * 0.75f, 0.1f, out var stern)) spots.Add(stern);
            foreach (var local in spots)
            {
                _lanterns.Add(MakeLantern(t, local + Vector3.up * Plugin.LanternHeight.Value));
            }
            if (_ext.Ship.m_mastObject)
            {
                Vector3 mastLocal = t.InverseTransformPoint(_ext.Ship.m_mastObject.transform.position);
                _lanterns.Add(MakeLantern(t, mastLocal + Vector3.up * Plugin.LanternHeight.Value));
            }
        }

        /// <summary>Walks from z toward the middle until the ray hits the deck (prows are narrow).</summary>
        private bool FindDeckAlong(Vector3 center, float z, float step, out Vector3 local)
        {
            local = Vector3.zero;
            for (int i = 0; i < 8; i++)
            {
                var probe = new Vector3(center.x, 0f, center.z + z + step * i * Mathf.Abs(z));
                if (_ext.TryGetDeckPoint(probe, out var world))
                {
                    local = _ext.transform.InverseTransformPoint(world);
                    return true;
                }
            }
            return false;
        }

        private static GameObject MakeLantern(Transform parent, Vector3 localPos)
        {
            var root = new GameObject("EBT_Lantern");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;

            var item = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab("Lantern") : null;
            var attach = item ? ItemStand.GetAttachPrefab(item) : null;
            if (attach)
            {
                var visual = Object.Instantiate(ItemStand.GetAttachGameObject(attach), root.transform, false);
                visual.transform.localPosition = Vector3.zero;
                StripPhysics(visual);
            }

            var light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.42f);
            light.range = 10f;
            light.intensity = 1.4f;
            light.shadows = LightShadows.None;
            return root;
        }

        private void ApplyFigurehead(int itemHash)
        {
            if (_figurehead) Object.Destroy(_figurehead);
            _figurehead = null;
            if (itemHash == 0 || !ObjectDB.instance) return;

            var item = ObjectDB.instance.GetItemPrefab(itemHash);
            var attach = item ? ItemStand.GetAttachPrefab(item) : null;
            var fc = _ext.FloatCollider;
            if (!attach || !fc) return;

            Transform t = _ext.transform;
            Vector3 center = t.InverseTransformPoint(fc.transform.position);
            if (!FindDeckAlong(center, fc.size.z * 0.5f, -0.06f, out var bow)) return;

            _figurehead = new GameObject("EBT_Figurehead");
            _figurehead.transform.SetParent(t, false);
            _figurehead.transform.localPosition = bow + Vector3.up * Plugin.FigureheadHeight.Value
                                                  + Vector3.forward * Plugin.FigureheadForward.Value;
            _figurehead.transform.localRotation = Quaternion.Euler(0f, Plugin.FigureheadYaw.Value, 0f);

            var visual = Object.Instantiate(ItemStand.GetAttachGameObject(attach), _figurehead.transform, false);
            visual.transform.localPosition = attach.transform.localPosition;
            visual.transform.localRotation = attach.transform.localRotation;
            visual.transform.localScale = attach.transform.localScale * Plugin.FigureheadScale.Value;
            StripPhysics(visual);
        }

        private static void StripPhysics(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(rb);
        }
    }
}
