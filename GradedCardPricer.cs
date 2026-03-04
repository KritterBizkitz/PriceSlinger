using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PriceSlinger
{
    /// <summary>
    /// Handles pricing logic for graded cards on shelves.
    /// Uses a safe write path that bypasses the EnhancedPrefabLoader Harmony
    /// patch on <c>CPlayerData.SetCardPrice</c>, which incorrectly uses
    /// <c>cardIndex</c> instead of <c>GetCardSaveIndex</c> for graded cards,
    /// causing <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    internal static class GradedCardPricer
    {
        // ── Reflection cache: vanilla graded price storage ────────────────

        private static bool _gradedReflectionCached;
        private static MethodInfo _getCardSaveIndexMethod;

        private static FieldInfo _gradedListTetramon;
        private static FieldInfo _gradedListDestiny;
        private static FieldInfo _gradedListGhost;
        private static FieldInfo _gradedListGhostBlack;
        private static FieldInfo _gradedListMegabot;
        private static FieldInfo _gradedListFantasyRPG;
        private static FieldInfo _gradedListCatJob;

        private static FieldInfo _floatDataListField;

        // ── Reflection cache: GradingOverhaul soft dependency ─────────────

        private static bool _gradingOverhaulCached;
        private static MethodInfo _decodeGradeMethod;

        // ── Reflection cache: event firing ────────────────────────────────

        private static bool _eventReflectionCached;
        private static Type _cardPriceChangedEventType;
        private static MethodInfo _queueEventMethod;

        // ── Reflection cache: fallback ────────────────────────────────────

        private static MethodInfo _fallbackSetCardPrice;

        // ══════════════════════════════════════════════════════════════════
        //  Reflection initialisation
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Caches all reflection targets needed for direct graded price
        /// writes. Safe to call multiple times; only runs once.
        /// </summary>
        private static void EnsureGradedReflectionCached()
        {
            if (_gradedReflectionCached)
            {
                return;
            }
            _gradedReflectionCached = true;

            try
            {
                _getCardSaveIndexMethod = AccessTools.Method(
                    typeof(CPlayerData), "GetCardSaveIndex",
                    new[] { typeof(CardData) });

                if (_getCardSaveIndexMethod == null)
                {
                    Plugin.Log.LogWarning(
                        "[PriceSlinger] CPlayerData.GetCardSaveIndex not found" +
                        " — graded pricing will use fallback.");
                }

                _gradedListTetramon = AccessTools.Field(
                    typeof(CPlayerData), "m_GradedCardPriceSetList");
                _gradedListDestiny = AccessTools.Field(
                    typeof(CPlayerData), "m_GradedCardPriceSetListDestiny");
                _gradedListGhost = AccessTools.Field(
                    typeof(CPlayerData), "m_GradedCardPriceSetListGhost");
                _gradedListGhostBlack = AccessTools.Field(
                    typeof(CPlayerData), "m_GradedCardPriceSetListGhostBlack");
                _gradedListMegabot = AccessTools.Field(
                    typeof(CPlayerData), "m_GradedCardPriceSetListMegabot");
                _gradedListFantasyRPG = AccessTools.Field(
                    typeof(CPlayerData), "m_GradedCardPriceSetListFantasyRPG");
                _gradedListCatJob = AccessTools.Field(
                    typeof(CPlayerData), "m_GradedCardPriceSetListCatJob");

                if (_gradedListTetramon != null)
                {
                    LogHelper.LogDebug(
                        "Found m_GradedCardPriceSetList — will use direct " +
                        "vanilla write path.");
                }
                else
                {
                    Plugin.Log.LogWarning(
                        "[PriceSlinger] m_GradedCardPriceSetList not found.");
                }

                // Discover floatDataList on the wrapper element type
                if (_gradedListTetramon != null)
                {
                    try
                    {
                        Type listType = _gradedListTetramon.FieldType;
                        Type elemType = null;

                        if (listType.IsGenericType)
                        {
                            Type[] genericArgs = listType.GetGenericArguments();
                            if (genericArgs.Length > 0)
                            {
                                elemType = genericArgs[0];
                            }
                        }

                        if (elemType == null)
                        {
                            elemType = listType.GetElementType();
                        }

                        if (elemType != null)
                        {
                            _floatDataListField =
                                AccessTools.Field(elemType, "floatDataList") ??
                                AccessTools.Field(elemType, "m_FloatDataList") ??
                                AccessTools.Field(elemType, "FloatDataList");

                            if (_floatDataListField != null)
                            {
                                LogHelper.LogDebug(
                                    "Found floatDataList field on " +
                                    elemType.Name);
                            }
                            else
                            {
                                Plugin.Log.LogWarning(
                                    "[PriceSlinger] floatDataList not found " +
                                    "on element type " + elemType.Name);
                            }
                        }
                        else
                        {
                            Plugin.Log.LogWarning(
                                "[PriceSlinger] Could not determine element " +
                                "type of graded list.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning(
                            "[PriceSlinger] Could not inspect floatDataList: " +
                            ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "[PriceSlinger] EnsureGradedReflectionCached failed: " + ex);
            }
        }

        /// <summary>
        /// Caches GradingOverhaul's DecodeGrade method if that mod is
        /// installed. Safe to call multiple times; only runs once.
        /// </summary>
        private static void EnsureGradingOverhaulCached()
        {
            if (_gradingOverhaulCached)
            {
                return;
            }
            _gradingOverhaulCached = true;

            try
            {
                Type helperType = AccessTools.TypeByName(
                    "TCGCardShopSimulator.GradingOverhaul.Helper");
                if (helperType != null)
                {
                    _decodeGradeMethod =
                        AccessTools.Method(helperType, "DecodeGrade");
                    if (_decodeGradeMethod != null)
                    {
                        LogHelper.LogDebug(
                            "Found GradingOverhaul.Helper.DecodeGrade — " +
                            "will decode encoded grades.");
                    }
                }
                else
                {
                    LogHelper.LogDebug(
                        "GradingOverhaul not detected — using raw " +
                        "cardGrade as grade index.");
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogWarnThrottled("GradingOverhaulCache",
                    "Failed caching GradingOverhaul: " + ex.Message);
            }
        }

        /// <summary>
        /// Caches event-related reflection for firing
        /// CEventPlayer_CardPriceChanged.
        /// </summary>
        private static void EnsureEventReflectionCached()
        {
            if (_eventReflectionCached)
            {
                return;
            }
            _eventReflectionCached = true;

            try
            {
                _cardPriceChangedEventType =
                    AccessTools.TypeByName("CEventPlayer_CardPriceChanged");
                if (_cardPriceChangedEventType != null)
                {
                    _queueEventMethod = AccessTools.Method(
                        typeof(CEventManager), "QueueEvent");
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogWarnThrottled("EventCache",
                    "Failed caching event types: " + ex.Message);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Grade decoding
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns the actual 1–10 grade from a potentially
        /// GradingOverhaul-encoded <c>cardGrade</c> value. If
        /// GradingOverhaul is not installed, returns the raw value.
        /// </summary>
        /// <param name="encodedGrade">The <c>CardData.cardGrade</c> value,
        /// which may be a large encoded integer if GradingOverhaul is
        /// present.</param>
        /// <returns>The actual grade in the range 1–10.</returns>
        private static int GetActualGrade(int encodedGrade)
        {
            EnsureGradingOverhaulCached();

            if (_decodeGradeMethod != null)
            {
                try
                {
                    // DecodeGrade(int cardGrade, out GradingCompany company,
                    //             out int grade, out int cert)
                    object[] args = new object[]
                        { encodedGrade, null, null, null };
                    _decodeGradeMethod.Invoke(null, args);
                    int grade = (int)args[2];
                    return grade;
                }
                catch (Exception ex)
                {
                    LogHelper.LogWarnThrottled("DecodeGrade",
                        "DecodeGrade failed for " + encodedGrade + ": " +
                        ex.Message);
                }
            }

            return encodedGrade;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Expansion → field mapping
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Selects the correct per-expansion graded price list
        /// <see cref="FieldInfo"/> based on the card's
        /// <see cref="ECardExpansionType"/>.
        /// </summary>
        private static FieldInfo GetExpansionGradedListField(CardData cd)
        {
            if (cd == null)
            {
                return _gradedListTetramon;
            }

            switch (cd.expansionType)
            {
                case ECardExpansionType.Tetramon:
                    return _gradedListTetramon;
                case ECardExpansionType.Destiny:
                    return _gradedListDestiny;
                case ECardExpansionType.Megabot:
                    return _gradedListMegabot;
                case ECardExpansionType.FantasyRPG:
                    return _gradedListFantasyRPG;
                case ECardExpansionType.CatJob:
                    return _gradedListCatJob;
                case ECardExpansionType.Ghost:
                    return cd.isDestiny
                        ? _gradedListGhostBlack
                        : _gradedListGhost;
                default:
                    LogHelper.LogDebug(
                        "Unknown expansion " + cd.expansionType +
                        " — defaulting to Tetramon graded list.");
                    return _gradedListTetramon;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Safe graded price write
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Sets the price of a graded card, bypassing
        /// EnhancedPrefabLoader's broken <c>SetCardPrice</c> Harmony patch.
        /// Replicates vanilla <c>SetCardPrice</c> logic exactly: uses
        /// <c>GetCardSaveIndex</c> for the outer list index and
        /// <c>floatDataList[actualGrade - 1]</c> for the inner index.
        /// </summary>
        /// <remarks>
        /// Falls back to invoking <c>SetCardPrice</c> directly if the
        /// reflection setup failed. Note that the fallback will still
        /// trigger any Harmony patches on <c>SetCardPrice</c>, which may
        /// include the broken EPL patch.
        /// </remarks>
        /// <param name="cd">The graded card's data. Must have
        /// <c>cardGrade &gt; 0</c>.</param>
        /// <param name="price">The price to set.</param>
        private static void SetGradedCardPriceSafe(CardData cd, float price)
        {
            if (cd == null)
            {
                return;
            }

            EnsureGradedReflectionCached();

            int actualGrade = GetActualGrade(cd.cardGrade);
            if (actualGrade < 1 || actualGrade > 10)
            {
                LogHelper.LogWarnThrottled("BadGrade",
                    "Unexpected actualGrade " + actualGrade +
                    " for encoded " + cd.cardGrade +
                    " on " + cd.monsterType +
                    " — skipping graded price write.");
                return;
            }

            int gradeIndex = actualGrade - 1;

            // ── Primary path: replicate vanilla directly ──────────────────
            if (_getCardSaveIndexMethod != null && _floatDataListField != null)
            {
                try
                {
                    int saveIndex = (int)_getCardSaveIndexMethod.Invoke(
                        null, new object[] { cd });
                    FieldInfo listField = GetExpansionGradedListField(cd);

                    if (listField != null)
                    {
                        object outerList = listField.GetValue(null);
                        IList ilist = outerList as IList;

                        if (ilist != null && saveIndex >= 0 &&
                            saveIndex < ilist.Count)
                        {
                            object element = ilist[saveIndex];
                            if (element != null)
                            {
                                IList innerList =
                                    _floatDataListField.GetValue(element)
                                    as IList;

                                if (innerList != null && gradeIndex >= 0 &&
                                    gradeIndex < innerList.Count)
                                {
                                    innerList[gradeIndex] = price;
                                    FirePriceChangedEvent(cd, price);

                                    LogHelper.LogDebug(
                                        "SetGradedCardPriceSafe: " +
                                        cd.expansionType + " " +
                                        cd.monsterType + " grade " +
                                        actualGrade + " [saveIndex=" +
                                        saveIndex + "] @ " + price);
                                    return;
                                }

                                LogHelper.LogWarnThrottled("GradedInnerOOB",
                                    "floatDataList out of range: gradeIndex=" +
                                    gradeIndex + " count=" +
                                    (innerList != null
                                        ? innerList.Count.ToString()
                                        : "null") +
                                    " for " + cd.monsterType);
                            }
                        }
                        else
                        {
                            LogHelper.LogWarnThrottled("GradedOuterOOB",
                                "outer graded list out of range: saveIndex=" +
                                saveIndex + " count=" +
                                (ilist != null
                                    ? ilist.Count.ToString()
                                    : "null") +
                                " expansion=" + cd.expansionType);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.LogWarnThrottled("GradedDirectWrite",
                        "Direct vanilla write failed: " + ex.Message +
                        ". Falling back to SetCardPrice invoke.");
                }
            }

            // ── Fallback path: invoke SetCardPrice directly ───────────────
            try
            {
                if (_fallbackSetCardPrice == null)
                {
                    _fallbackSetCardPrice = AccessTools.Method(
                        typeof(CPlayerData), "SetCardPrice",
                        new[] { typeof(CardData), typeof(float) });
                }

                if (_fallbackSetCardPrice != null)
                {
                    _fallbackSetCardPrice.Invoke(
                        null, new object[] { cd, price });
                    LogHelper.LogDebug(
                        "SetGradedCardPriceSafe: fell back to " +
                        "SetCardPrice invoke for " + cd.monsterType);
                    return;
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogWarnThrottled("GradedFallback",
                    "SetCardPrice fallback also failed: " + ex.Message);
            }

            Plugin.Log.LogWarning(
                "[PriceSlinger] SetGradedCardPriceSafe: all approaches " +
                "exhausted, price not saved for " + cd.monsterType +
                " grade=" + cd.cardGrade);
        }

        /// <summary>
        /// Fires <c>CEventPlayer_CardPriceChanged</c> via reflection to
        /// match vanilla behavior. Failure is silently ignored.
        /// </summary>
        private static void FirePriceChangedEvent(CardData cd, float price)
        {
            try
            {
                EnsureEventReflectionCached();

                if (_cardPriceChangedEventType != null &&
                    _queueEventMethod != null)
                {
                    object evt = Activator.CreateInstance(
                        _cardPriceChangedEventType,
                        new object[] { cd, price });
                    _queueEventMethod.Invoke(null, new[] { evt });
                }
            }
            catch (Exception)
            {
                // Event fire is best-effort; never crash for this.
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Public pricing API
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Prices a single graded card that is on a shelf compartment.
        /// </summary>
        /// <param name="cd">The graded card's data. Must have
        /// <c>cardGrade &gt; 0</c>.</param>
        /// <param name="compartment">The compartment the card resides in
        /// (used for context only).</param>
        /// <returns><c>true</c> if the card was successfully priced;
        /// otherwise <c>false</c>.</returns>
        internal static bool PriceSingleGradedCard(
            CardData cd, InteractableCardCompartment compartment)
        {
            try
            {
                if (cd == null || cd.cardGrade <= 0)
                {
                    return false;
                }

                float markupPct = (float)Plugin.GradedCardMarkupPercent.Value;
                float mult = 1f + markupPct / 100f;
                bool roundEnabled = Plugin.GradedCardRoundingEnabled.Value;
                float roundStep = Plugin.GradedCardRoundToNearest.Value;
                bool noUnderMarket =
                    Plugin.GradedCardPreventBelowMarket.Value
                    && markupPct > 0f;

                float convRate = GameInstance.GetCurrencyConversionRate();
                float roundDiv = GameInstance.GetCurrencyRoundDivideAmount();

                if (convRate <= 0f)
                {
                    LogHelper.LogWarnThrottled("ConvRateGraded",
                        "Currency conversion rate is <= 0, skipping " +
                        "graded card.");
                    return false;
                }

                if (roundDiv <= 0f)
                {
                    roundDiv = 1f;
                }

                float marketPrice = 0f;
                try
                {
                    marketPrice =
                        CPlayerData.GetCardMarketPrice(cd) * convRate;
                }
                catch (Exception ex)
                {
                    LogHelper.LogWarnThrottled(
                        "GradedMP." + cd.monsterType,
                        "GetCardMarketPrice threw for graded card " +
                        cd.monsterType + " grade " + cd.cardGrade +
                        ": " + ex.Message);
                }

                if (marketPrice <= 0f)
                {
                    LogHelper.LogDebug(
                        "Skipping graded card with 0 or invalid market " +
                        "price: " + cd.monsterType + " grade " +
                        cd.cardGrade);
                    return false;
                }

                float price = (float)Mathf.RoundToInt(
                    marketPrice * mult * roundDiv) / roundDiv;
                price = PricingMath.ApplyRounding(
                    price, marketPrice, roundEnabled,
                    roundStep, noUnderMarket);

                // Convert back from display currency to base currency
                price = (float)Mathf.RoundToInt(
                    price / convRate * 100f) / 100f;

                SetGradedCardPriceSafe(cd, price);

                LogHelper.LogDebug(
                    "Priced graded " + cd.monsterType + " grade " +
                    cd.cardGrade + " @ " + price);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.LogWarnThrottled(
                    "GradedCard." +
                    (cd != null ? cd.monsterType.ToString() : "null"),
                    "PriceSingleGradedCard failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Iterates all card shelves and prices every graded card found.
        /// </summary>
        internal static void PriceAllGradedShelfCards()
        {
            try
            {
                ShelfManager shelfManager =
                    CSingleton<ShelfManager>.Instance;
                if (shelfManager == null)
                {
                    LogHelper.LogWarnThrottled("NoShelfMgrGraded",
                        "ShelfManager singleton is null.");
                    return;
                }

                List<CardShelf> shelves = shelfManager.m_CardShelfList;
                if (shelves == null)
                {
                    return;
                }

                int priced = 0;
                int skipped = 0;

                for (int i = 0; i < shelves.Count; i++)
                {
                    if (shelves[i] == null)
                    {
                        continue;
                    }

                    List<InteractableCardCompartment> comps =
                        shelves[i].GetCardCompartmentList();
                    if (comps == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < comps.Count; j++)
                    {
                        InteractableCardCompartment comp = comps[j];
                        if (comp == null)
                        {
                            continue;
                        }

                        List<InteractableCard3d> stored =
                            comp.m_StoredCardList;
                        if (stored == null || stored.Count == 0)
                        {
                            continue;
                        }

                        for (int k = 0; k < stored.Count; k++)
                        {
                            try
                            {
                                InteractableCard3d card3d = stored[k];
                                if (card3d == null ||
                                    card3d.m_Card3dUI == null ||
                                    card3d.m_Card3dUI.m_CardUI == null)
                                {
                                    skipped++;
                                    continue;
                                }

                                CardData cd = card3d.m_Card3dUI
                                    .m_CardUI.GetCardData();
                                if (cd == null || cd.cardGrade <= 0)
                                {
                                    continue;
                                }

                                if (PriceSingleGradedCard(cd, comp))
                                {
                                    priced++;
                                }
                                else
                                {
                                    skipped++;
                                }
                            }
                            catch (Exception ex)
                            {
                                skipped++;
                                LogHelper.LogWarnThrottled(
                                    "GradedShelfCard." + k,
                                    "Error pricing graded shelf card: " +
                                    ex.Message);
                            }
                        }
                    }
                }

                LogHelper.LogDebug(
                    "PriceAllGradedShelfCards: priced=" + priced +
                    " skipped=" + skipped);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "[PriceSlinger] PriceAllGradedShelfCards failed: " + ex);
            }
        }
    }
}