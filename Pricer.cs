using System;

namespace PriceSlinger
{
    /// <summary>
    /// Orchestration facade that delegates to the specialised pricers
    /// (<see cref="CardPricer"/>, <see cref="GradedCardPricer"/>,
    /// <see cref="ItemPricer"/>) and handles sound effects.
    /// </summary>
    internal static class Pricer
    {
        /// <summary>
        /// Prices all normal (non-graded) cards on all shelves.
        /// </summary>
        internal static void PriceAllShelfCards()
        {
            CardPricer.PriceAllShelfCards();
        }

        /// <summary>
        /// Prices all graded cards on all shelves.
        /// </summary>
        internal static void PriceAllGradedShelfCards()
        {
            GradedCardPricer.PriceAllGradedShelfCards();
        }

        /// <summary>
        /// Prices all shop items (packs, bulk boxes, accessories, etc.).
        /// </summary>
        internal static void PriceAllItems()
        {
            ItemPricer.PriceAllItems();
        }

        /// <summary>
        /// Prices a single shelf compartment containing normal cards.
        /// Graded cards within are routed to <see cref="GradedCardPricer"/>
        /// automatically.
        /// </summary>
        /// <param name="compartment">The compartment to price.</param>
        internal static void PriceCompartment(
            InteractableCardCompartment compartment)
        {
            CardPricer.PriceCompartment(compartment);
        }

        /// <summary>
        /// Prices a single graded card on a shelf compartment.
        /// </summary>
        /// <param name="cd">The graded card data.</param>
        /// <param name="compartment">The compartment the card belongs
        /// to.</param>
        /// <returns><c>true</c> if priced successfully.</returns>
        internal static bool PriceGradedCompartmentCard(
            CardData cd, InteractableCardCompartment compartment)
        {
            return GradedCardPricer.PriceSingleGradedCard(cd, compartment);
        }

        /// <summary>
        /// Plays a confirmation sound effect if enabled in config.
        /// </summary>
        internal static void PlaySound()
        {
            try
            {
                if (Plugin.PlaySoundOnPrice.Value)
                {
                    SoundManager.PlayAudio("SFX_Popup2", 1f, 0.3f);
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogWarnThrottled("PlaySound",
                    "Failed to play pricing sound: " + ex.Message);
            }
        }
    }
}