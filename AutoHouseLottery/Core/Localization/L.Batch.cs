namespace AutoHouseLottery.Core.Localization;

internal static partial class L
{
    internal static class Characters
    {
        public static readonly LocString Title = new("characters.title", "Characters");
        public static readonly LocPlural Selected = new("characters.selected", "{0} selected", "{0} selected");
        public static readonly LocString Help = new("characters.help", "Tick the characters that should each make one entry. Each one pays from its own gil, with its own limits.");
        public static readonly LocString LoggedIn = new("characters.loggedIn", "Logged in · {0}");
        public static readonly LocString GilEstimate = new("characters.gilEstimate", "About {0} at its last login, {1}");
        public static readonly LocString GilUnknown = new("characters.gilUnknown", "Gil not seen yet");
        public static readonly LocString AlreadyEntered = new("characters.alreadyEntered", "Already entered");
        public static readonly LocString Buyer = new("characters.buyer", "Buyer");
        public static readonly LocString Target = new("characters.target", "Target");
        public static readonly LocString BestMatch = new("characters.bestMatch", "Best match");
        public static readonly LocString TargetGone = new("characters.targetGone", "Its chosen plot is no longer on the shortlist. Choose another target.");
        public static readonly LocString Cap = new("characters.cap", "Deposit cap");
        public static readonly LocString CapHint = new("characters.capHint", "Price ceiling");
        public static readonly LocString ReserveHint = new("characters.reserveHint", "Shared");
        public static readonly LocString Account = new("characters.account", "Service account");
        public static readonly LocString AccountNone = new("characters.accountNone", "Not set");
        public static readonly LocString AccountNumber = new("characters.accountNumber", "Account {0}");
        public static readonly LocString AccountHelp = new("characters.accountHelp", "Characters on one service account and home world can hold only one plot between them. Until you set this, private characters on one world count as one account.");
    }

    internal static class Batch
    {
        public static readonly LocString Placement = new("batch.placement", "Placement");
        public static readonly LocString PlacementAutomatic = new("batch.placement.automatic", "By group");
        public static readonly LocString PlacementSamePlot = new("batch.placement.samePlot", "Same plot");
        public static readonly LocString PlacementSpread = new("batch.placement.spread", "Spread");
        public static readonly LocString PlacementHelp = new("batch.placement.help", "Same plot puts every entry on the top plot. Spread gives each character the next plot in rank order. By group uses Same plot inside an ownership group and Spread across groups.");
        public static readonly LocString OddsTitle = new("batch.odds.title", "Estimate from reported entries");
        public static readonly LocString OddsAny = new("batch.odds.any", "{0} chance of at least one win");
        public static readonly LocString OddsTwoOrMore = new("batch.odds.twoOrMore", "{0} chance of two or more wins in one group");
        public static readonly LocString MultipleWinsWarning = new("batch.multipleWins.warning", "Spreading inside one ownership group can win more than one plot, and only one of them can be claimed. Each unclaimed win costs part of its deposit.");
        public static readonly LocString MultipleWinsConfirm = new("batch.multipleWins.confirm", "I understand only one win per group can be claimed");
        public static readonly LocString Exact = new("batch.exact", "exact plot");
        public static readonly LocPlural Fallbacks = new("batch.fallbacks", "then {0} fallback", "then {0} fallbacks");
        public static readonly LocString UpTo = new("batch.upTo", "up to {0}");
        public static readonly LocString Needs = new("batch.needs", "needs {0} with its reserve");
        public static readonly LocString Has = new("batch.has", "has {0}");
        public static readonly LocString HasEstimate = new("batch.hasEstimate", "about {0} at its last login");
        public static readonly LocString RefundFirst = new("batch.refundFirst", "collects an earlier refund first");
        public static readonly LocString TotalDeposits = new("batch.totalDeposits", "All deposits at most");
        public static readonly LocString NotPooled = new("batch.notPooled", "Each character pays from its own gil. Nothing is pooled.");
        public static readonly LocString SwitchingOn = new("batch.switchingOn", "Character switching is allowed for this batch: the run logs in each character in turn through Lifestream.");
        public static readonly LocString SwitchingOff = new("batch.switchingOff", "The logged-in character enters now. The others wait for their own login, then press Continue.");
        public static readonly LocString TargetNotOnShortlist = new("batch.targetNotOnShortlist", "A character's chosen plot is not on the shortlist.");
        public static readonly LocPlural Enter = new("batch.enter", "Enter for {0} character", "Enter for {0} characters");
        public static readonly LocString TitleRunning = new("batch.title.running", "Batch in progress");
        public static readonly LocString TitleSwitching = new("batch.title.switching", "Switching to {0}");
        public static readonly LocString TitleWaiting = new("batch.title.waiting", "Waiting for the next character");
        public static readonly LocString TitleHalted = new("batch.title.halted", "Batch stopped");
        public static readonly LocString TitleFinished = new("batch.title.finished", "Batch finished");
        public static readonly LocString WaitingBody = new("batch.waitingBody", "Log in with a waiting character, then press Continue.");
        public static readonly LocString SwitchingBody = new("batch.switchingBody", "Nothing is entered until the game confirms the requested character.");
        public static readonly LocString Summary = new("batch.summary", "Entered {0} · Dry runs {1} · Waiting {2} · Not entered {3}");
        public static readonly LocString MemberEntered = new("batch.member.entered", "{0}: entered {1} for {2}");
        public static readonly LocString MemberDryRun = new("batch.member.dryRun", "{0}: dry run reached {1} at {2}");
        public static readonly LocString MemberRunning = new("batch.member.running", "{0}: entering now");
        public static readonly LocString MemberQueued = new("batch.member.queued", "{0}: waiting for its turn");
        public static readonly LocString MemberHeld = new("batch.member.held", "{0}: waiting while an entry in its ownership group is being checked");
        public static readonly LocString MemberUncertain = new("batch.member.uncertain", "{0}: checking whether its entry completed");
        public static readonly LocString MemberNotEntered = new("batch.member.notEntered", "{0}: not entered. {1}");
        public static readonly LocString MemberExcluded = new("batch.member.excluded", "{0}: left out, {1}");
        public static readonly LocString Continue = new("batch.continue", "Continue");
        public static readonly LocString Discard = new("batch.discard", "Discard batch");
        public static readonly LocString HaltStopped = new("batch.halt.stopped", "You stopped the batch.");
        public static readonly LocString HaltInterrupted = new("batch.halt.interrupted", "A logout, an unload or an alert interrupted the batch.");
        public static readonly LocString HaltCharacterChanged = new("batch.halt.characterChanged", "A different character is logged in than the one the batch was entering for.");
        public static readonly LocString HaltWrongCharacter = new("batch.halt.wrongCharacter", "Another character logged in than the one the batch asked for, so it stopped.");
        public static readonly LocString HaltSwitchFailed = new("batch.halt.switchFailed", "The character switch did not finish.");
        public static readonly LocString HaltReconciliation = new("batch.halt.reconciliation", "An entry is being checked. Check it on My lottery, then continue.");
        public static readonly LocString HaltJournal = new("batch.halt.journal", "Entries cannot be recorded right now.");
        public static readonly LocString HaltCheckpoint = new("batch.halt.checkpoint", "The batch could not be saved before the switch, so it did not switch.");
        public static readonly LocString HaltReloaded = new("batch.halt.reloaded", "The plugin reloaded during the batch. Nothing restarts on its own.");
        public static readonly LocString HaltCycleEnded = new("batch.halt.cycleEnded", "The entry period ended.");
        public static readonly LocString HaltSwitchBlocked = new("batch.halt.switchBlocked", "Another task was using the game (a queue, a vnavmesh path or AutoRetainer's multi-mode), so the batch did not switch characters. Continue once it is done.");
        public static readonly LocString DryRunOnly = new("batch.dryRunOnly", "Dry run for every character: each goes as far as its lottery confirmation and closes it. Real batch entries stay off until batches are validated in game.");
        public static readonly LocString LoginNotice = new("batch.loginNotice", "{0} is waiting in your lottery batch. Open /ahl and press Continue to enter for it.");
    }
}
