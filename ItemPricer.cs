using System;
using UnityEngine;

namespace PriceSlinger
{
    /// <summary>
    /// Handles pricing logic for shop items (packs, accessories,
    /// bulk boxes, etc.).
    /// </summary>
    internal static class ItemPricer
    {
        /// <summary>
        /// Checks whether the given <see cref="EItemType"/> represents a
        /// bulk box. Values 37–38 and 89–94 are bulk box types.
        /// </summary>
        /// <param name="itemType">The item type to check.</param>
        /// <returns><c>true</c> if the item type is a bulk box.</returns>
        private static bool IsBulkBox(EItemType itemType)
        {
            int t = (int)itemType;
            return (t >= 37 && t <= 38) || (t >= 89 && t <= 94);
        }

        /// <summary>
        /// Iterates all <see cref="EItemType"/> values and sets their prices
        /// based on configured markup, rounding, and floor settings.
        /// </summary>
        internal static void PriceAllItems()
        {
            try
            {
                float absMin = Plugin.AbsoluteMinPrice.Value;
                float itemMult = 1f + Plugin.ItemMarkupPercent.Value / 100f;
                float bulkMult = 1f + Plugin.BulkBoxMarkupPercent.Value / 100f;
                bool roundEnabled = Plugin.ItemRoundingEnabled.Value;
                float roundStep = Plugin.ItemRoundToNearest.Value;
                bool noUnderMarket = Plugin.ItemPreventBelowMarket.Value
                                     && Plugin.ItemMarkupPercent.Value > 0;
                bool useAvgCost = Plugin.ItemMarkupOnAvgCost.Value;

                foreach (EItemType itemType in Enum.GetValues(typeof(EItemType)))
                {
                    if ((int)itemType < 0)
                    {
                        continue;
                    }

                    try
                    {
                        float marketPrice;
                        float baseForMarkup;
                        float mult;

                        if (IsBulkBox(itemType))
                        {
                            marketPrice =
                                CPlayerData.GetItemMarketPrice(itemType);
                            baseForMarkup = marketPrice;
                            mult = bulkMult;
                        }
                        else
                        {
                            marketPrice =
                                CPlayerData.GetItemMarketPrice(itemType);
                            baseForMarkup = useAvgCost
                                ? CPlayerData.GetAverageItemCost(itemType)
                                : marketPrice;
                            mult = itemMult;
                        }

                        float price = (float)Mathf.RoundToInt(
                            baseForMarkup * mult * 100f) / 100f;
                        price = PricingMath.ApplyRounding(
                            price, marketPrice, roundEnabled,
                            roundStep, noUnderMarket);
                        // ApplyRounding already clamps to AbsoluteMinPrice

                        CPlayerData.SetItemPrice(itemType, price);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.LogWarnThrottled(
                            "ItemPrice." + itemType,
                            "Failed pricing item " + itemType + ": " +
                            ex.Message);
                    }
                }

                LogHelper.LogDebug("PriceAllItems complete.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "[PriceSlinger] PriceAllItems failed: " + ex);
            }
        }
    }
}