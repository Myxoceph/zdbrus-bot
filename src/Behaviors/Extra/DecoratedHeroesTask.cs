using System;
using System.Collections;
using System.Linq;
using Firebot.Core;
using Firebot.Core.Tasks;
using UnityEngine;
using Logger = Firebot.Core.Logger;

namespace Firebot.Behaviors.Extra;

/// <summary>
///     Claims finished Decorated Heroes challenges. The event has its own menu (not the mini-event one Event
///     Challenges handles): Events, the Decorated Heroes entry, then the Challenges tab, where every
///     challenge has a Claim button for each milestone reached.
/// </summary>
public class DecoratedHeroesTask : BotTask
{
    public override GameFeature RequiredFeature => GameFeature.EventManager;
    protected override bool EnabledByDefault => true;

    private const string List = P.Events + "/EventManager/bg/verticalLayout/Scroll View/Viewport/Content/activeEvents";
    private const string Shop = P.Events + "/DecoratedHeroesShop";
    private const string ChallengesTab = Shop + "/bg/submenuButtons/decoratedHeroesChallenges";
    private const string Challenges = Shop + "/bg/submenus/decoratedHeroesChallenges/challengesLayout";
    private const string Mini = P.Events + "/MiniEvents/bg";

    // A challenge has three milestones; each one is its own claim.
    private const int ClaimsPerChallenge = 3;

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddMinutes(30);

        var opened = false;
        yield return OpenShop(value => opened = value);
        if (!opened)
        {
            Logger.Info("[Decorated Heroes] Event not found in the event list; trying again later.");
            NextRunTime = DateTime.Now.AddHours(2);
            yield return CloseEvents();
            yield break;
        }

        yield return Ui.Click(ChallengesTab, 1.5f);
        if (!Ui.Visible(Challenges))
        {
            Logger.Warning("[Decorated Heroes] The Challenges tab did not open; skipped.");
            yield return CloseEvents();
            yield break;
        }

        var confirmed = 0;
        var unconfirmed = 0;
        foreach (var challenge in Ui.VisibleChildren(Challenges).Where(t => t.name.StartsWith("challenge")).ToList())
        {
            var claim = challenge.Find("claimButton");
            for (var n = 0; n < ClaimsPerChallenge && Ui.Clickable(claim); n++)
            {
                var title = Ui.Text(challenge.Find("challengeTitleText"));
                var rewardWasVisible = Ui.Visible(P.RewardsClose);
                var progressBefore = Ui.Text(challenge.Find("progressBar/challengeProgressText"));
                var changed = false;
                yield return Ui.ClickUntil(claim,
                    () => !Ui.Clickable(claim) || (!rewardWasVisible && Ui.Visible(P.RewardsClose)),
                    value => changed = value, "Decorated Heroes challenge reward", 1.5f);
                if (changed)
                {
                    confirmed++;
                    Logger.Info($"[Decorated Heroes] Claimed '{title}' ({progressBefore}).");
                }
                else
                {
                    unconfirmed++;
                    Logger.Warning($"[Decorated Heroes] Claim for '{title}' was not confirmed; stopping on this challenge.");
                    yield return Ui.CloseRewards();
                    break;
                }

                yield return Ui.CloseRewards();
            }
        }

        yield return CloseEvents();
        if (confirmed > 0 || unconfirmed > 0)
            Logger.Info($"[Decorated Heroes] {confirmed} challenge reward(s) claimed; {unconfirmed} unconfirmed.");
    }

    /// <summary>
    ///     Opens the event from the event list: by its title first, then (other languages) by trying the entries
    ///     until one opens the Decorated Heroes menu, closing whatever else opens on the way.
    /// </summary>
    private static IEnumerator OpenShop(Action<bool> result)
    {
        if (Ui.Visible(Shop))
        {
            result(true);
            yield break;
        }

        yield return Ui.Click(P.EventsButton, 2f);
        if (!Ui.Visible(List))
        {
            result(false);
            yield break;
        }

        var entries = Ui.VisibleChildren(List).Where(t => t.name.StartsWith("eventInteraction")).ToList();
        var byTitle = entries.FirstOrDefault(t =>
            (Ui.Text(t.Find("mainElements/title")) ?? "").IndexOf("decorated", StringComparison.OrdinalIgnoreCase) >= 0);
        if (byTitle != null)
        {
            yield return Ui.Click(byTitle, 2f);
            if (Ui.Visible(Shop))
            {
                result(true);
                yield break;
            }
        }

        foreach (var entry in entries.Where(t => t != byTitle))
        {
            if (!Ui.Visible(List)) yield return Ui.Click(P.EventsButton, 2f);
            if (!Ui.Visible(List)) break;
            yield return Ui.Click(entry, 2f);
            if (Ui.Visible(Shop))
            {
                result(true);
                yield break;
            }

            yield return CloseOtherEvent();
        }

        result(false);
    }

    /// <summary>Closes an event menu other than the event list (also used by Event Challenges).</summary>
    internal static IEnumerator CloseOtherEvent()
    {
        if (Ui.Visible(Mini))
        {
            yield return Ui.Click(Mini + "/closeButton", 1.5f);
            yield break;
        }

        // Any other event menu that opened over the list: they share the bg/closeButton layout.
        foreach (var menu in Ui.VisibleChildren(P.Events).Where(t => t.name != "EventManager").ToList())
        {
            var close = menu.Find("bg/closeButton");
            if (Ui.Clickable(close)) yield return Ui.Click(close, 1.5f);
        }
    }

    private static IEnumerator CloseEvents()
    {
        if (Ui.Visible(Shop)) yield return Ui.Click(Shop + "/bg/closeButton", 1.5f);
        if (Ui.Visible(P.Events + "/EventManager")) yield return Ui.Click(P.Events + "/EventManager/bg/closeButton", 1.5f);
    }
}
