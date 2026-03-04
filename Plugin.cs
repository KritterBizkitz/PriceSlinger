using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace PriceSlinger
{
    /// <summary>
    /// BepInEx plugin entry point for PriceSlinger.
    /// Handles configuration binding and Harmony patch application.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        /// <summary>Unique identifier for this plugin.</summary>
        public const string PluginGuid = "com.kritterbizkit.priceslinger";

        /// <summary>Human-readable plugin name.</summary>
        public const string PluginName = "PriceSlinger";

        /// <summary>Semantic version of this plugin.</summary>
        public const string PluginVersion = "1.1.1";

        /// <summary>Shared logger instance for all PriceSlinger classes.</summary>
        internal static ManualLogSource Log;

        private Harmony _harmony;

        // ── Cards ────────────────────────────────────────────────────────────

        /// <summary>Percentage markup over market price for normal shelf cards.</summary>
        internal static ConfigEntry<int> CardMarkupPercent;

        /// <summary>Whether to round normal card prices to the nearest increment.</summary>
        internal static ConfigEntry<bool> CardRoundingEnabled;

        /// <summary>Increment to round normal card prices to (e.g. 0.25).</summary>
        internal static ConfigEntry<float> CardRoundToNearest;

        /// <summary>Prevent normal card prices from dropping below market value after rounding.</summary>
        internal static ConfigEntry<bool> CardPreventBelowMarket;

        // ── Graded Cards ─────────────────────────────────────────────────────

        /// <summary>Percentage markup over market price for graded cards.</summary>
        internal static ConfigEntry<int> GradedCardMarkupPercent;

        /// <summary>Whether to round graded card prices to the nearest increment.</summary>
        internal static ConfigEntry<bool> GradedCardRoundingEnabled;

        /// <summary>Increment to round graded card prices to.</summary>
        internal static ConfigEntry<float> GradedCardRoundToNearest;

        /// <summary>Prevent graded card prices from dropping below market value after rounding.</summary>
        internal static ConfigEntry<bool> GradedCardPreventBelowMarket;

        // ── Items ────────────────────────────────────────────────────────────

        /// <summary>Percentage markup over market price for standard items.</summary>
        internal static ConfigEntry<int> ItemMarkupPercent;

        /// <summary>Percentage markup specifically for bulk box items.</summary>
        internal static ConfigEntry<int> BulkBoxMarkupPercent;

        /// <summary>Whether to round item prices to the nearest increment.</summary>
        internal static ConfigEntry<bool> ItemRoundingEnabled;

        /// <summary>Increment to round item prices to.</summary>
        internal static ConfigEntry<float> ItemRoundToNearest;

        /// <summary>Prevent item prices from dropping below market value after rounding.</summary>
        internal static ConfigEntry<bool> ItemPreventBelowMarket;

        /// <summary>If true, item markup is applied to average purchase cost instead of market price.</summary>
        internal static ConfigEntry<bool> ItemMarkupOnAvgCost;

        /// <summary>Absolute minimum price for any item or card.</summary>
        internal static ConfigEntry<float> AbsoluteMinPrice;

        // ── Triggers ─────────────────────────────────────────────────────────

        /// <summary>Hotkey to price all shelf cards, graded cards, and items at once.</summary>
        internal static ConfigEntry<KeyboardShortcut> PriceAllKey;

        /// <summary>Hotkey to price only normal shelf cards.</summary>
        internal static ConfigEntry<KeyboardShortcut> PriceCardsKey;

        /// <summary>Hotkey to price only graded shelf cards.</summary>
        internal static ConfigEntry<KeyboardShortcut> PriceGradedKey;

        /// <summary>Hotkey to price only items.</summary>
        internal static ConfigEntry<KeyboardShortcut> PriceItemsKey;

        /// <summary>Automatically price normal cards when placed on a shelf.</summary>
        internal static ConfigEntry<bool> PriceOnCardPlaced;

        /// <summary>Automatically price graded cards when placed on a shelf.</summary>
        internal static ConfigEntry<bool> PriceGradedOnCardPlaced;

        // ── Misc ─────────────────────────────────────────────────────────────

        /// <summary>Play a sound effect when a hotkey pricing run completes.</summary>
        internal static ConfigEntry<bool> PlaySoundOnPrice;

        /// <summary>Enable verbose debug logging for troubleshooting.</summary>
        internal static ConfigEntry<bool> DebugLogging;

        /// <summary>
        /// Called by BepInEx when the plugin is loaded. Initializes config and patches.
        /// </summary>
        private void Awake()
        {
            Log = base.Logger;
            InitConfig();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            Log.LogInfo(PluginName + " v" + PluginVersion + " loaded!");
        }

        /// <summary>
        /// Called when the plugin MonoBehaviour is destroyed. Unpatches Harmony.
        /// </summary>
        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
            }
        }

        /// <summary>
        /// Binds all configuration entries with their sections, keys, defaults,
        /// and descriptions.
        /// </summary>
        private void InitConfig()
        {
            // ── Cards ────────────────────────────────────────────────────────
            CardMarkupPercent = Config.Bind(
                "Cards", "MarkupPercent", 10,
                "Percentage markup over market price for shelf cards. 0 = market price exactly.");

            CardRoundingEnabled = Config.Bind(
                "Cards", "RoundingEnabled", true,
                "Round card prices to the nearest CardRoundToNearest value.");

            CardRoundToNearest = Config.Bind(
                "Cards", "RoundToNearest", 0.25f,
                "Round card prices to this increment (e.g. 0.25 = nearest quarter). " +
                "Ignored if rounding is off.");

            CardPreventBelowMarket = Config.Bind(
                "Cards", "PreventPricingBelowMarket", true,
                "Never set a card price below market value, even after rounding.");

            // ── Graded Cards ─────────────────────────────────────────────────
            GradedCardMarkupPercent = Config.Bind(
                "GradedCards", "MarkupPercent", 15,
                "Percentage markup over market price for graded cards. " +
                "Separate from normal card markup.");

            GradedCardRoundingEnabled = Config.Bind(
                "GradedCards", "RoundingEnabled", true,
                "Round graded card prices to the nearest GradedCardRoundToNearest value.");

            GradedCardRoundToNearest = Config.Bind(
                "GradedCards", "RoundToNearest", 0.50f,
                "Round graded card prices to this increment.");

            GradedCardPreventBelowMarket = Config.Bind(
                "GradedCards", "PreventPricingBelowMarket", true,
                "Never set a graded card price below market value, even after rounding.");

            // ── Items ────────────────────────────────────────────────────────
            ItemMarkupPercent = Config.Bind(
                "Items", "MarkupPercent", 10,
                "Percentage markup over market price for standard items " +
                "(packs, accessories).");

            BulkBoxMarkupPercent = Config.Bind(
                "Items", "BulkBoxMarkupPercent", 5,
                "Percentage markup specifically for bulk box items.");

            ItemMarkupOnAvgCost = Config.Bind(
                "Items", "MarkupOnAverageCost", false,
                "If true, item markup is applied to average purchase cost " +
                "instead of market price.");

            ItemRoundingEnabled = Config.Bind(
                "Items", "RoundingEnabled", true,
                "Round item prices to the nearest ItemRoundToNearest value.");

            ItemRoundToNearest = Config.Bind(
                "Items", "RoundToNearest", 0.25f,
                "Round item prices to this increment.");

            ItemPreventBelowMarket = Config.Bind(
                "Items", "PreventPricingBelowMarket", true,
                "Never set an item price below market value, even after rounding.");

            AbsoluteMinPrice = Config.Bind(
                "Items", "AbsoluteMinPrice", 0.25f,
                "No item or card will ever be priced below this value.");

            // ── Triggers ─────────────────────────────────────────────────────
            PriceAllKey = Config.Bind(
                "Hotkeys", "PriceEverything",
                new KeyboardShortcut(KeyCode.F6),
                "Price all shelf cards, graded cards, and items at once.");

            PriceCardsKey = Config.Bind(
                "Hotkeys", "PriceCards",
                new KeyboardShortcut(KeyCode.F7),
                "Price only shelf cards (non-graded).");

            PriceGradedKey = Config.Bind(
                "Hotkeys", "PriceGradedCards",
                new KeyboardShortcut(KeyCode.F8),
                "Price only graded cards on shelves.");

            PriceItemsKey = Config.Bind(
                "Hotkeys", "PriceItems",
                new KeyboardShortcut(KeyCode.F5),
                "Price only items (packs, bulk boxes, etc.).");

            PriceOnCardPlaced = Config.Bind(
                "Triggers", "PriceCardOnPlace", true,
                "Automatically price a normal card compartment when a card " +
                "is placed into it.");

            PriceGradedOnCardPlaced = Config.Bind(
                "Triggers", "PriceGradedCardOnPlace", true,
                "Automatically price a graded card compartment when a graded " +
                "card is placed into it.");

            // ── Misc ─────────────────────────────────────────────────────────
            PlaySoundOnPrice = Config.Bind(
                "Misc", "PlaySoundOnPrice", true,
                "Play a sound effect when a hotkey pricing run completes.");

            DebugLogging = Config.Bind(
                "Misc", "DebugLogging", false,
                "Enable verbose debug logging. Leave off unless troubleshooting.");
        }
    }
}