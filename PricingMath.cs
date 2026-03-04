using UnityEngine;

namespace PriceSlinger
{
    /// <summary>
    /// Shared mathematical helpers for price rounding and clamping.
    /// </summary>
    internal static class PricingMath
    {
        /// <summary>
        /// Maximum iterations for the rounding-up loop to prevent infinite loops
        /// caused by floating-point precision issues.
        /// </summary>
        private const int MaxRoundUpIterations = 1000;

        /// <summary>
        /// Applies rounding, prevent-below-market logic, and absolute minimum
        /// price clamping.
        /// </summary>
        /// <param name="price">The raw price after markup.</param>
        /// <param name="marketPrice">The market price to enforce as a floor
        /// (if enabled).</param>
        /// <param name="roundingEnabled">Whether to snap to the nearest
        /// <paramref name="roundToNearest"/>.</param>
        /// <param name="roundToNearest">The rounding increment
        /// (e.g. 0.25).</param>
        /// <param name="preventBelowMarket">If true, ensure the final price
        /// is not below <paramref name="marketPrice"/>.</param>
        /// <returns>The final adjusted price, never below
        /// <see cref="Plugin.AbsoluteMinPrice"/>.</returns>
        internal static float ApplyRounding(float price, float marketPrice,
                                            bool roundingEnabled,
                                            float roundToNearest,
                                            bool preventBelowMarket)
        {
            float absMin = Plugin.AbsoluteMinPrice.Value;

            if (roundingEnabled)
            {
                float step = roundToNearest <= 0f ? 1f : roundToNearest;
                price = Mathf.Round(price / step) * step;

                if (preventBelowMarket)
                {
                    int safety = 0;
                    while (price < marketPrice && safety < MaxRoundUpIterations)
                    {
                        price += step;
                        safety++;
                    }
                }
            }
            else if (preventBelowMarket)
            {
                price = Mathf.Max(price, marketPrice);
            }

            return Mathf.Max(absMin, price);
        }
    }
}