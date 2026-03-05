using System;
using System.Collections.Generic;
using HarmonyLib;

namespace PriceSlinger
{
    /// <summary>
    /// Harmony postfix on <c>CGameManager.Update</c> to detect
    /// pricing hotkey presses each frame.
    /// </summary>
    /// <remarks>
    /// <c>CGameManager.Update</c> is private, so <c>nameof()</c> cannot be
    /// used here. The string literal <c>"Update"</c> is required.
    /// </remarks>
    [HarmonyPatch(typeof(CGameManager), "Update")]
    internal static class GameManagerUpdate_Patch
    {
        /// <summary>
        /// Reentrancy guard to prevent overlapping pricing operations
        /// within the same frame or across nested calls.
        /// </summary>
        private static bool _isRunning;

        /// <summary>
        /// Postfix on <c>CGameManager.Update</c>. Checks configured hotkeys
        /// and triggers the appropriate pricing operations.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (_isRunning)
            {
                return;
            }

            try
            {
                _isRunning = true;

                if (Plugin.PriceAllKey.Value.IsDown())
                {
                    Pricer.PriceAllShelfCards();
                    Pricer.PriceAllGradedShelfCards();
                    Pricer.PriceAllItems();
                    Pricer.PlaySound();
                    return;
                }

                if (Plugin.PriceCardsKey.Value.IsDown())
                {
                    Pricer.PriceAllShelfCards();
                    Pricer.PlaySound();
                    return;
                }

                if (Plugin.PriceGradedKey.Value.IsDown())
                {
                    Pricer.PriceAllGradedShelfCards();
                    Pricer.PlaySound();
                    return;
                }

                if (Plugin.PriceItemsKey.Value.IsDown())
                {
                    Pricer.PriceAllItems();
                    Pricer.PlaySound();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[PriceSlinger] Hotkey check failed: " + ex);
            }
            finally
            {
                _isRunning = false;
            }
        }
    }

    /// <summary>
    /// Harmony postfix on <c>InteractableCardCompartment.OnMouseButtonUp</c>
    /// to automatically price cards when the player places them into a shelf
    /// compartment.
    /// </summary>
    [HarmonyPatch(typeof(InteractableCardCompartment),
        nameof(InteractableCardCompartment.OnMouseButtonUp))]
    internal static class CardCompartment_OnMouseButtonUp_Patch
    {
        /// <summary>
        /// Postfix on <c>InteractableCardCompartment.OnMouseButtonUp</c>.
        /// Inspects the compartment contents and routes to the appropriate
        /// pricer based on whether the compartment contains graded or normal
        /// cards.
        /// </summary>
        /// <param name="__instance">The compartment that received the
        /// mouse-up event.</param>
        [HarmonyPostfix]
        public static void Postfix(InteractableCardCompartment __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }

                List<InteractableCard3d> stored = __instance.m_StoredCardList;
                if (stored == null || stored.Count == 0)
                {
                    return;
                }

                // Determine if the first card in the compartment is graded
                bool hasGraded = false;
                try
                {
                    InteractableCard3d firstCard = stored[0];
                    if (firstCard != null &&
                        firstCard.m_Card3dUI != null &&
                        firstCard.m_Card3dUI.m_CardUI != null)
                    {
                        CardData cd = firstCard.m_Card3dUI.m_CardUI.GetCardData();
                        if (cd != null && cd.cardGrade > 0)
                        {
                            hasGraded = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.LogWarnThrottled("CompartDetect",
                        "Failed to detect card type in compartment: " +
                        ex.Message);
                }

                if (hasGraded)
                {
                    if (!Plugin.PriceGradedOnCardPlaced.Value)
                    {
                        return;
                    }

                    // Price each graded card in this compartment individually
                    for (int i = 0; i < stored.Count; i++)
                    {
                        try
                        {
                            InteractableCard3d card3d = stored[i];
                            if (card3d == null ||
                                card3d.m_Card3dUI == null ||
                                card3d.m_Card3dUI.m_CardUI == null)
                            {
                                continue;
                            }

                            CardData cd = card3d.m_Card3dUI.m_CardUI.GetCardData();
                            if (cd != null && cd.cardGrade > 0)
                            {
                                Pricer.PriceGradedCompartmentCard(cd, __instance);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.LogWarnThrottled("CompartGraded." + i,
                                "Failed pricing graded card at index " + i +
                                " in compartment: " + ex.Message);
                        }
                    }
                }
                else
                {
                    if (!Plugin.PriceOnCardPlaced.Value)
                    {
                        return;
                    }

                    Pricer.PriceCompartment(__instance);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "[PriceSlinger] OnCardCompartmentMouseUp failed: " + ex);
            }
        }
    }
}