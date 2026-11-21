using UnityEngine;

namespace EnhancedBoatTime
{
    /// <summary>
    /// Local player side of team rowing. Sit on one of the ship's benches and tap Run
    /// (Left Shift / the gamepad run button) on the drum beat. The beat is derived from the
    /// shared network clock, so every rower hears the same one. Strokes that land on the beat
    /// give more power, and rowers who hit the same beat together get a sync bonus.
    /// </summary>
    public class RowingClient : MonoBehaviour
    {
        private int _lastBeat = int.MinValue;
        private string _feedback = "";
        private float _feedbackUntil;
        private ShipExt _hudShip;

        private GUIStyle _bigStyle;
        private GUIStyle _smallStyle;
        private Texture2D _white;

        public static ShipExt GetRowingShip(Player player)
        {
            if (!player || !player.IsAttached()) return null;
            var attach = player.GetAttachPoint();
            if (!attach || !attach.GetComponentInParent<Chair>()) return null;
            var ship = attach.GetComponentInParent<Ship>();
            return ship ? ship.GetComponent<ShipExt>() : null;
        }

        private static double Now => ZNet.instance ? ZNet.instance.GetTimeSeconds() : 0.0;
        private static float Period => Mathf.Max(0.5f, Plugin.BeatInterval.Value);

        private void Update()
        {
            var player = Player.m_localPlayer;
            _hudShip = null;
            if (!Plugin.RowingEnabled.Value || !player || !ZNet.instance) return;

            var rowingShip = GetRowingShip(player);
            _hudShip = rowingShip ? rowingShip : ShipExt.Find(player);

            if (!rowingShip || UiBlocksInput() || !(ZInput.GetButtonDown("Run") || ZInput.GetButtonDown("JoyRun"))) return;

            double now = Now;
            int beat = (int)System.Math.Round(now / Period);
            if (beat == _lastBeat)
            {
                return; // one stroke per beat
            }
            if (!player.HaveStamina(Plugin.StaminaPerStroke.Value))
            {
                Show(Localizations.L("ebt_row_tired"));
                Hud.instance.StaminaBarEmptyFlash();
                return;
            }
            _lastBeat = beat;
            player.UseStamina(Plugin.StaminaPerStroke.Value);

            float offset = Mathf.Abs((float)(now - beat * (double)Period));
            float quality;
            if (offset <= Plugin.PerfectWindow.Value)
            {
                quality = 1f;
                Show(Localizations.L("ebt_row_perfect"));
            }
            else if (offset <= Plugin.GoodWindow.Value)
            {
                quality = 0.6f;
                Show(Localizations.L("ebt_row_good"));
            }
            else
            {
                quality = 0.2f;
                Show(Localizations.L("ebt_row_off"));
            }
            rowingShip.SendStroke(beat, quality);
        }

        private static bool UiBlocksInput()
        {
            return InventoryGui.IsVisible() || Menu.IsVisible() || Minimap.IsOpen() || TextInput.IsVisible()
                   || (Chat.instance && Chat.instance.HasFocus()) || Console.IsVisible() || ShipwrightUI.IsOpen;
        }

        private void Show(string text)
        {
            _feedback = text;
            _feedbackUntil = Time.time + 0.8f;
        }

        private void OnGUI()
        {
            var player = Player.m_localPlayer;
            if (!_hudShip || !player || Hud.IsUserHidden()) return;
            bool seated = GetRowingShip(player) == _hudShip;
            int rowers = _hudShip.GetInt(ShipExt.KeyRowers);
            if (!seated && rowers == 0) return;

            EnsureStyles();
            float w = 320f, h = 18f;
            float x = (Screen.width - w) / 2f;
            float y = Screen.height * 0.78f;

            if (seated)
            {
                // Drum track: the marker sweeps toward the centre line, which is the beat.
                double phase = Now / Period - System.Math.Round(Now / Period); // -0.5..0.5, 0 = on the beat
                float good = Plugin.GoodWindow.Value / Period;
                float perfect = Plugin.PerfectWindow.Value / Period;
                DrawRect(new Rect(x, y, w, h), new Color(0f, 0f, 0f, 0.55f));
                DrawRect(new Rect(x + w * (0.5f - good), y, w * good * 2f, h), new Color(1f, 0.85f, 0.2f, 0.35f));
                DrawRect(new Rect(x + w * (0.5f - perfect), y, w * perfect * 2f, h), new Color(0.5f, 1f, 0.3f, 0.5f));
                DrawRect(new Rect(x + w / 2f - 1f, y - 4f, 2f, h + 8f), Color.white);
                float mx = x + w * (0.5f + (float)phase);
                DrawRect(new Rect(mx - 4f, y + 2f, 8f, h - 4f), new Color(1f, 1f, 1f, 0.9f));

                bool onBeat = System.Math.Abs(phase) < 0.08;
                GUI.Label(new Rect(x, y - 34f, w, 30f), onBeat ? Localizations.L("ebt_row_now") : Localizations.L("ebt_row_wait"), _bigStyle);
                GUI.Label(new Rect(x, y + h + 2f, w, 20f),
                    Localization.instance.Localize("[<color=yellow>$KEY_Run</color>] $ebt_row_stroke"), _smallStyle);
                if (Time.time < _feedbackUntil)
                {
                    GUI.Label(new Rect(x, y + h + 20f, w, 24f), _feedback, _smallStyle);
                }
            }

            if (rowers > 0)
            {
                int synced = _hudShip.GetInt(ShipExt.KeySynced);
                float power = _hudShip.Zdo?.GetFloat(ShipExt.KeyRowPower) ?? 0f;
                string sync = synced >= 2 ? " " + Localizations.L("ebt_row_synced", synced.ToString()) : "";
                GUI.Label(new Rect(x, y - 58f, w, 24f),
                    Localizations.L("ebt_row_status", rowers.ToString(), Mathf.RoundToInt(power * 100f).ToString()) + sync, _smallStyle);
            }
        }

        private void EnsureStyles()
        {
            if (_bigStyle != null) return;
            _bigStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 22, richText = true,
                normal = { textColor = Color.white }
            };
            _smallStyle = new GUIStyle(_bigStyle) { fontSize = 14 };
            _white = Texture2D.whiteTexture;
        }

        private void DrawRect(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }
    }
}
