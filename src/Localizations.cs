using System.Collections.Generic;
using Jotunn.Managers;

namespace EnhancedBoatTime
{
    /// <summary>
    /// English text for every player-facing string, registered with Jotunn so translation mods
    /// (or a translation file dropped next to the plugin) can override any token.
    /// Arguments use Valheim's $1, $2 placeholders.
    /// </summary>
    internal static class Localizations
    {
        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            // Rowing
            { "ebt_row_tired", "Too tired to row" },
            { "ebt_row_perfect", "<color=#7CFC00>Perfect stroke!</color>" },
            { "ebt_row_good", "<color=#FFD700>Good stroke</color>" },
            { "ebt_row_off", "<color=#FF7F50>Off the beat</color>" },
            { "ebt_row_now", "<b>ROW!</b>" },
            { "ebt_row_wait", "<color=#bbbbbb>row on the drum</color>" },
            { "ebt_row_stroke", "stroke" },
            { "ebt_row_status", "Rowers: $1   Oar power: $2%" },
            { "ebt_row_synced", "<color=#7CFC00>$1 in sync!</color>" },

            // Fishing
            { "ebt_fish_wrongbait", "Nothing in these waters takes that bait" },
            { "ebt_fish_nofish", "Nothing seems to live in these waters" },
            { "ebt_fish_strike", "Something strikes at your bait!" },

            // Pets
            { "ebt_pet_aboard", "$1 jumps aboard" },

            // Shipwright
            { "ebt_shipwright", "Shipwright" },
            { "ebt_sw_done", "Shipwright: done" },
            { "ebt_sw_close", "Close" },
            { "ebt_sw_free", "free" },
            { "ebt_sw_hold", "Cargo hold  (level $1/$2)" },
            { "ebt_sw_hold_desc", "Each level adds $1 row(s) to the ship's chest." },
            { "ebt_sw_hold_buy", "Expand cargo hold" },
            { "ebt_sw_bench", "Deck workbench" },
            { "ebt_sw_bench_desc", "Craft and repair at sea. Also lets you repair the ship with a hammer." },
            { "ebt_sw_bench_open", "Open workbench" },
            { "ebt_sw_bench_buy", "Build deck workbench" },
            { "ebt_sw_lanterns", "Lanterns" },
            { "ebt_sw_lanterns_buy", "Hang lanterns at bow, stern and mast" },
            { "ebt_sw_lanterns_remove", "Take down lanterns" },
            { "ebt_sw_dye", "Sail dye  (current: $1)" },
            { "ebt_sw_figurehead", "Trophy figurehead" },
            { "ebt_sw_figurehead_mounted", "Mounted: $1" },
            { "ebt_sw_figurehead_remove", "Take down figurehead (returns the trophy)" },
            { "ebt_sw_figurehead_none", "<color=#aaaaaa>Carry a trophy to mount it on the prow.</color>" },
            { "ebt_sw_figurehead_buy", "Mount $1" },

            // Sail dyes
            { "ebt_dye_natural", "Natural" },
            { "ebt_dye_crimson", "Crimson" },
            { "ebt_dye_blue", "Royal blue" },
            { "ebt_dye_green", "Bog green" },
            { "ebt_dye_gold", "Dandelion gold" },
            { "ebt_dye_black", "Raven black" },
        };

        public static void Register()
        {
            LocalizationManager.Instance.GetLocalization().AddTranslation("English", English);
        }

        public static string L(string token, params string[] args)
        {
            return Localization.instance.Localize("$" + token, args);
        }
    }
}
