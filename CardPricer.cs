using System;
using System.Collections.Generic;
using UnityEngine;

namespace PriceSlinger
{
    /// <summary>
    /// Handles pricing logic for normal (non-graded) cards on shelves.
    /// </summary>
    internal static class CardPricer
    {
        /// <summary>
        /// Prices every normal card in a single shelf compartment.
        /// Graded cards found in the compartment are routed to
        /// <see cref="GradedCardPricer"/>.
        /// </summary>
        /// <param name="compartment">The shelf compartment containing cards
        /// to price.</param>
        internal static void PriceCompartment(InteractableCardCompartment compartment)
        {
            if (compartment == null)
            {
                return;
            }

            try
            {
                List<InteractableCard3d> stored = compartment.m_StoredCardList;
                if (stored == null || stored.Count == 0)
                {
                    return;
                }

                float markupPct = (float)Plugin.CardMarkupPercent.Value;
                float mult = 1f + markupPct / 100f;
                bool roundEnabled = Plugin.CardRoundingEnabled.Value;
                float roundStep = Plugin.CardRoundToNearest.Value;
                bool noUnderMarket = Plugin.CardPreventBelowMarket.Value
                                     && markupPct > 0f;

                float convRate = GameInstance.GetCurrencyConversionRate();
                float roundDiv = GameInstance.GetCurrencyRoundDivideAmount();

                if (convRate <= 0f)
                {
                    LogHelper.LogWarnThrottled("ConvRateCard",
                        "Currency conversion rate is <= 0, skipping compartment.");
                    return;
                }

                if (roundDiv <= 0f)
                {
                    roundDiv = 1f;
                }

                for (int i = 0; i < stored.Count; i++)
                {
                    try
                    {
                        InteractableCard3d card3d = stored[i];
                        if (card3d == null ||
                            card3d.m_Card3dUI == null ||
                            card3d.m_Card3dUI.m_CardUI == null)
                        {
                            LogHelper.LogDebug(
                                "Skipping null card3d chain at index " + i);
                            continue;
                        }

                        CardData cd = card3d.m_Card3dUI.m_CardUI.GetCardData();
                        if (cd == null)
                        {
                            LogHelper.LogDebug(
                                "Skipping null CardData at index " + i);
                            continue;
                        }

                        // Route graded cards to their own pricer
                        if (cd.cardGrade > 0)
                        {
                            GradedCardPricer.PriceSingleGradedCard(cd, compartment);
                            continue;
                        }

                        float marketPrice =
                            CPlayerData.GetCardMarketPrice(cd) * convRate;
                        if (marketPrice <= 0f)
                        {
                            LogHelper.LogDebug(
                                "Skipping card with 0 market price: " +
                                cd.monsterType);
                            continue;
                        }

                        float price = (float)Mathf.RoundToInt(
                            marketPrice * mult * roundDiv) / roundDiv;
                        price = PricingMath.ApplyRounding(
                            price, marketPrice, roundEnabled,
                            roundStep, noUnderMarket);

                        // Convert back from display currency to base currency
                        price = (float)Mathf.RoundToInt(
                            price / convRate * 100f) / 100f;

                        CPlayerData.SetCardPrice(cd, price);
                        LogHelper.LogDebug(
                            "Priced " + cd.monsterType + " @ " + price);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.LogWarnThrottled("CompartCard." + i,
                            "Failed pricing card at index " + i + ": " +
                            ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "[PriceSlinger] PriceCompartment failed: " + ex);
            }
        }

        /// <summary>
        /// Iterates all card shelves and prices every normal card compartment.
        /// </summary>
        internal static void PriceAllShelfCards()
        {
            try
            {
                ShelfManager shelfManager =
                    CSingleton<ShelfManager>.Instance;
                if (shelfManager == null)
                {
                    LogHelper.LogWarnThrottled("NoShelfMgr",
                        "ShelfManager singleton is null.");
                    return;
                }

                List<CardShelf> shelves = shelfManager.m_CardShelfList;
                if (shelves == null)
                {
                    return;
                }

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
                        PriceCompartment(comps[j]);
                    }
                }

                LogHelper.LogDebug("PriceAllShelfCards complete.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "[PriceSlinger] PriceAllShelfCards failed: " + ex);
            }
        }
    }
}