using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn;
using Jotunn.Utils;

namespace EnhancedBoatTime
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    [SynchronizationMode(AdminOnlyStrictness.IfOnServer)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "addingburritos.enhancedboattime";
        public const string Name = "EnhancedBoatTime";
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        // Rowing
        internal static ConfigEntry<bool> RowingEnabled;
        internal static ConfigEntry<float> BeatInterval;
        internal static ConfigEntry<float> PerfectWindow;
        internal static ConfigEntry<float> GoodWindow;
        internal static ConfigEntry<float> PowerPerRower;
        internal static ConfigEntry<float> SyncBonusPerRower;
        internal static ConfigEntry<float> RowingForce;
        internal static ConfigEntry<float> MaxRowingSpeed;
        internal static ConfigEntry<float> StaminaPerStroke;

        // Fishing
        internal static ConfigEntry<bool> BoatFishingEnabled;
        internal static ConfigEntry<bool> LinePaysOut;
        internal static ConfigEntry<bool> TrollingEnabled;
        internal static ConfigEntry<float> TrollingInterval;
        internal static ConfigEntry<float> TrollingChance;
        internal static ConfigEntry<float> TrollingMinSpeed;

        // Pets
        internal static ConfigEntry<bool> PetTransportEnabled;
        internal static ConfigEntry<float> PetBoardRange;

        // Shipwright upgrades
        internal static ConfigEntry<bool> UpgradesEnabled;
        internal static ConfigEntry<int> StorageMaxLevel;
        internal static ConfigEntry<int> StorageRowsPerLevel;
        internal static ConfigEntry<string> StorageCost;
        internal static ConfigEntry<string> WorkbenchCost;
        internal static ConfigEntry<string> LanternCost;
        internal static ConfigEntry<string> FigureheadExtraCost;

        // Decor placement (tune if something looks off on a particular hull)
        internal static ConfigEntry<float> LanternHeight;
        internal static ConfigEntry<float> FigureheadHeight;
        internal static ConfigEntry<float> FigureheadForward;
        internal static ConfigEntry<float> FigureheadYaw;
        internal static ConfigEntry<float> FigureheadScale;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            BindConfig();
            Localizations.Register();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            gameObject.AddComponent<RowingClient>();
            gameObject.AddComponent<ShipwrightUI>();
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        /// <summary>
        /// Every setting is admin-only: on a server, Jotunn pushes the server's values to all
        /// clients so costs and balance match for everyone; only admins can change them in game.
        /// </summary>
        private ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            return Config.Bind(section, key, defaultValue, new ConfigDescription(description, null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
        }

        private void BindConfig()
        {
            RowingEnabled = Bind("Rowing", "Enabled", true,
                "Seated passengers can row in time with the drum beat to push the ship forward.");
            BeatInterval = Bind("Rowing", "BeatInterval", 1.4f,
                "Seconds between drum beats. Everyone aboard hears the same beat.");
            PerfectWindow = Bind("Rowing", "PerfectWindow", 0.12f,
                "Seconds either side of the beat that count as a perfect stroke.");
            GoodWindow = Bind("Rowing", "GoodWindow", 0.3f,
                "Seconds either side of the beat that count as a good stroke.");
            PowerPerRower = Bind("Rowing", "PowerPerRower", 0.35f,
                "Rowing power a single perfect stroke contributes, as a fraction of the ship's own paddle force.");
            SyncBonusPerRower = Bind("Rowing", "SyncBonusPerRower", 0.15f,
                "Extra multiplier per additional rower who hits the same beat (2 in sync = +15%, 3 = +30%...).");
            RowingForce = Bind("Rowing", "ForceMultiplier", 1f,
                "Global multiplier on rowing force. Raise it if rowing feels too weak.");
            MaxRowingSpeed = Bind("Rowing", "MaxSpeed", 12f,
                "Rowing stops adding force above this forward speed (m/s).");
            StaminaPerStroke = Bind("Rowing", "StaminaPerStroke", 5f,
                "Stamina used per stroke.");

            BoatFishingEnabled = Bind("Fishing", "Enabled", true, "Master switch for the boat fishing changes.");
            LinePaysOut = Bind("Fishing", "LinePaysOut", true,
                "When fishing from a ship, the reel pays out line as the ship moves instead of snapping.");
            TrollingEnabled = Bind("Fishing", "Trolling", true,
                "Fishing from a moving ship occasionally draws a strike from a local fish that takes your bait.");
            TrollingInterval = Bind("Fishing", "TrollingInterval", 6f, "Seconds between trolling strike rolls.");
            TrollingChance = Bind("Fishing", "TrollingChance", 0.3f, "Chance of a strike each roll, before bait preference.");
            TrollingMinSpeed = Bind("Fishing", "TrollingMinSpeed", 1.0f, "Minimum ship speed (m/s) for trolling.");

            PetTransportEnabled = Bind("Pets", "Enabled", true,
                "Tamed creatures following you jump aboard when you board a ship, hold still while sailing, and follow you ashore.");
            PetBoardRange = Bind("Pets", "BoardRange", 15f,
                "How close (m) a following pet must be to the ship to jump aboard.");

            UpgradesEnabled = Bind("Shipwright", "Enabled", true,
                "Shift+E on a ship's chest opens the Shipwright menu for upgrades and decor.");
            StorageMaxLevel = Bind("Shipwright", "StorageMaxLevel", 3, "Number of storage upgrades a ship can take.");
            StorageRowsPerLevel = Bind("Shipwright", "StorageRowsPerLevel", 1, "Chest rows added per storage upgrade. Changing this only affects future upgrades; existing ships keep the rows they were built with.");
            StorageCost = Bind("Shipwright", "StorageCost", "FineWood:10,BronzeNails:20",
                "Cost of each storage upgrade (PrefabName:Amount, comma separated).");
            WorkbenchCost = Bind("Shipwright", "WorkbenchCost", "Wood:15,FineWood:5,Bronze:2",
                "Cost of the deck workbench.");
            LanternCost = Bind("Shipwright", "LanternCost", "Resin:5,BronzeNails:4,Wood:4",
                "Cost of deck lanterns.");
            FigureheadExtraCost = Bind("Shipwright", "FigureheadCost", "FineWood:4",
                "Cost of mounting a trophy figurehead (the trophy itself is also consumed).");

            LanternHeight = Bind("Decor", "LanternHeight", 1.6f, "Height of deck lanterns above the deck (m).");
            FigureheadHeight = Bind("Decor", "FigureheadHeight", 0.6f, "Height of the figurehead above the prow (m).");
            FigureheadForward = Bind("Decor", "FigureheadForward", 0.2f, "How far forward of the prow the figurehead sits (m).");
            FigureheadYaw = Bind("Decor", "FigureheadYaw", 0f, "Rotation of the figurehead around the vertical axis (degrees).");
            FigureheadScale = Bind("Decor", "FigureheadScale", 1.5f, "Size of the figurehead relative to a wall-mounted trophy.");
        }
    }
}
