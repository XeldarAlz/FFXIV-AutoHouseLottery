namespace AutoHouseLottery.Core.Localization;

internal static partial class L
{
    internal static class Common
    {
        public static readonly LocString Close = new("common.close", "Close");
        public static readonly LocString Pause = new("common.pause", "Pause");
        public static readonly LocString Resume = new("common.resume", "Resume");
        public static readonly LocString StopRun = new("common.stopRun", "Stop the run");
        public static readonly LocString DragAdjustHint = new("common.dragAdjustHint", "Drag to adjust · Ctrl+click to type");
        public static readonly LocString NoMatches = new("common.noMatches", "Nothing matches “{0}”.");
        public static readonly LocString Gil = new("common.gil", "{0} gil");
    }

    internal static class Time
    {
        public static readonly LocString JustNow = new("time.justNow", "just now");
        public static readonly LocString MinutesAgo = new("time.minutesAgo", "{0}m ago");
        public static readonly LocString HoursAgo = new("time.hoursAgo", "{0}h ago");
        public static readonly LocString DaysAgo = new("time.daysAgo", "{0}d ago");
    }

    internal static class Shell
    {
        public static readonly LocString NavFindHome = new("shell.nav.findHome", "Find a home");
        public static readonly LocString NavMyLottery = new("shell.nav.myLottery", "My lottery");
        public static readonly LocString NavSaved = new("shell.nav.saved", "Saved");
        public static readonly LocString NavSettings = new("shell.nav.settings", "Settings");
        public static readonly LocString NavPlugins = new("shell.nav.plugins", "Plugins");
        public static readonly LocString NavLog = new("shell.nav.log", "Console");
        public static readonly LocString NavChangelog = new("shell.nav.changelog", "Changelog");
        public static readonly LocString NavAbout = new("shell.nav.about", "About");
        public static readonly LocString StatusRunning = new("shell.status.running", "Running");
        public static readonly LocString StatusPaused = new("shell.status.paused", "Paused");
        public static readonly LocString StatusReady = new("shell.status.ready", "Ready");
        public static readonly LocString StatusBlocked = new("shell.status.blocked", "Not ready");
        public static readonly LocString StatusSetupNeeded = new("shell.status.setupNeeded", "Setup needed");
        public static readonly LocString StatusIdle = new("shell.status.idle", "Idle");
        public static readonly LocString Minimize = new("shell.minimize", "Minimize to the title strip");
        public static readonly LocString Restore = new("shell.restore", "Restore the window");
        public static readonly LocString ResumeBlocked = new("shell.resumeBlocked", "Resumes automatically once you leave the duty");
    }

    internal static class MyLottery
    {
        public static readonly LocString Title = new("myLottery.title", "My lottery");
        public static readonly LocString Subtitle = new("myLottery.subtitle", "Your lottery entries, their results, and the refunds and houses waiting to be collected.");
        public static readonly LocString Empty = new("myLottery.empty", "Your lottery entries will appear here.");
    }

    internal static class Dock
    {
        public static readonly LocString Stop = new("dock.stop", "STOP");
        public static readonly LocString PauseCaps = new("dock.pause", "PAUSE");
        public static readonly LocString ResumeCaps = new("dock.resume", "RESUME");
        public static readonly LocString InContent = new("dock.inContent", "in content");
        public static readonly LocString StateRunning = new("dock.state.running", "running");
        public static readonly LocString StatePaused = new("dock.state.paused", "paused");
    }

    internal static class Plugins
    {
        public static readonly LocString Title = new("plugins.title", "Plugins");
        public static readonly LocString AllInstalled = new("plugins.allInstalled", "All required plugins are installed and loaded.");
        public static readonly LocPlural Missing = new("plugins.missing", "{0} required plugin is missing.", "{0} required plugins are missing.");
        public static readonly LocString Required = new("plugins.required", "Required");
        public static readonly LocString Optional = new("plugins.optional", "Optional");
        public static readonly LocString Installed = new("plugins.installed", "Installed");
        public static readonly LocString Install = new("plugins.install", "Install");
        public static readonly LocString Installing = new("plugins.installing", "Installing…");
        public static readonly LocString RepoHint = new("plugins.repoHint", "Repo: {0}\nLeft-click to open repo URL · right-click to copy");
        public static readonly LocString Footer = new("plugins.footer",
            "Install adds the plugin's source repository to Dalamud and queues an install. If one-click install fails (URL drift, network), right-click a plugin name to copy its repo URL and add it manually via /xlsettings -> Experimental -> Custom Plugin Repositories.");
        public static readonly LocString PurposeLifestream = new("plugins.purpose.lifestream", "Travels to a plot's world, ward and plot for entries, result checks and refunds.");
        public static readonly LocString PurposeVnavmesh = new("plugins.purpose.vnavmesh", "Walks the last metres to a plot's placard. Without it, you walk up to the placard when the plugin asks.");
    }

    internal static class Log
    {
        public static readonly LocString Title = new("log.title", "Console");
        public static readonly LocPlural Entries = new("log.entries", "{0} line in the buffer", "{0} lines in the buffer");
        public static readonly LocString Empty = new("log.empty", "Nothing logged yet. Start a run and every step the plugin takes shows up here.");
        public static readonly LocString NoMatches = new("log.noMatches", "No lines match the current filters.");
        public static readonly LocString Footer = new("log.footer", "Every line also goes to the Dalamud log (/xllog) with the {0} prefix. When reporting a bug, press Copy log and paste the result into the issue.");
        public static readonly LocString SearchHint = new("log.searchHint", "Search messages and sources");
        public static readonly LocString CopyAll = new("log.copyAll", "Copy log");
        public static readonly LocPlural CopyFiltered = new("log.copyFiltered", "Copy {0} line", "Copy {0} lines");
        public static readonly LocString CopyTooltip = new("log.copyTooltip", "Copies the lines shown below with a header naming the plugin version, Dalamud version and zone, ready to paste into a bug report.");
        public static readonly LocString Copied = new("log.copied", "Copied");
        public static readonly LocPlural CopiedLines = new("log.copiedLines", "Copied {0} line to the clipboard", "Copied {0} lines to the clipboard");
        public static readonly LocString Clear = new("log.clear", "Clear");
        public static readonly LocString ConfirmClear = new("log.confirmClear", "Click again to clear");
        public static readonly LocString Close = new("log.close", "Close");
        public static readonly LocString LevelVerbose = new("log.level.verbose", "Verbose");
        public static readonly LocString LevelDebug = new("log.level.debug", "Debug");
        public static readonly LocString LevelInfo = new("log.level.info", "Info");
        public static readonly LocString LevelWarning = new("log.level.warning", "Warnings");
        public static readonly LocString LevelError = new("log.level.error", "Errors");
        public static readonly LocString LevelTooltip = new("log.levelTooltip", "Click to show or hide these lines. Shift-click to show only this level.");
        public static readonly LocString SourceChip = new("log.sourceChip", "Source: {0}");
        public static readonly LocString SourceChipTooltip = new("log.sourceChipTooltip", "Click to stop filtering by source.");
        public static readonly LocString JumpLatest = new("log.jumpLatest", "Jump to latest");
        public static readonly LocPlural NewLines = new("log.newLines", "{0} new line", "{0} new lines");
        public static readonly LocString Showing = new("log.showing", "Showing {0} of {1}");
        public static readonly LocString ResetFilters = new("log.resetFilters", "Reset filters");
        public static readonly LocString Shortcuts = new("log.shortcuts", "Ctrl+F search · Ctrl+C copy · Shift-click selects a range · Double-click copies a line");
        public static readonly LocPlural Selected = new("log.selected", "{0} line selected", "{0} lines selected");
        public static readonly LocString CopySelection = new("log.copySelection", "Copy selection");
        public static readonly LocString ClearSelection = new("log.clearSelection", "Clear selection");
        public static readonly LocString CopyLine = new("log.copyLine", "Copy line");
        public static readonly LocString CopyToEnd = new("log.copyToEnd", "Copy from here to the end");
        public static readonly LocString OnlySource = new("log.onlySource", "Show only {0}");
        public static readonly LocString Repeated = new("log.repeated", "Repeated {0} times in a row");
        public static readonly LocString HasDetails = new("log.hasDetails", "Has a stack trace. Select the line to read it.");
    }

    internal static class Changelog
    {
        public static readonly LocString Title = new("changelog.title", "What's new");
        public static readonly LocString Subtitle = new("changelog.subtitle", "Every update, newest first.");
        public static readonly LocString Version = new("changelog.version", "Version {0}");
        public static readonly LocString Latest = new("changelog.latest", "Latest");
        public static readonly LocString New = new("changelog.new", "New");
        public static readonly LocPlural Changes = new("changelog.changes", "{0} change", "{0} changes");

        public static readonly LocString[] Release1200 =
        [
            new("changelog.r1200.1", "Added Chat commands to the settings: list your own commands, one per line (such as /li home or /ays m), and they run when a run finishes on its own, never after Stop or a failure"),
            new("changelog.r1200.2", "Fixed a run waiting forever when a plot is in a residential district the character has not unlocked: such plots are skipped, or the run ends with a message saying the district is locked, and a batch moves on to the next character"),
            new("changelog.r1200.3", "The main window now moves only when you drag its top bar, so a drag inside it no longer moves it by accident; turn on Drag from anywhere in the settings for the old behaviour"),
        ];

        public static readonly LocString[] Release1110 =
        [
            new("changelog.r1110.1", "Fixed long sessions ending in a crash or in runs that no longer start; moving around no longer sets up new game hooks each time"),
        ];

        public static readonly LocString[] Release1100 =
        [
            new("changelog.r1100.1", "A Characters list on Find a home: tick several characters, each with its own buyer type, target, deposit cap, reserve and service account"),
            new("changelog.r1100.2", "Enter for N characters enters for each character in turn, on the same plot inside an ownership group and spread across groups, with an estimate of the chance to win"),
            new("changelog.r1100.3", "Each character pays from its own gil, and nothing is entered for a character until the game confirms it is the one logged in"),
            new("changelog.r1100.4", "Several wins in one ownership group ask you to choose which plot to claim, and a second claim in that group is refused"),
            new("changelog.r1100.5", "Characters are switched through Lifestream, for batches and, with its option on, for collecting refunds; when Lifestream cannot switch, each character waits for its own login and a Continue"),
        ];

        public static readonly LocString[] Release1000 =
        [
            new("changelog.r1000.1", "Find a home: the plots reported vacant on the district map and in a list, filtered by district, ward, division, size, price, buyer type and lottery phase, with the age of every report"),
            new("changelog.r1000.2", "A shortlist you fill plot by plot or with Add all matching, and a run preview with the maximum deposit, your gil reserve and anything that would stop the run"),
            new("changelog.r1000.3", "Enter on a plot travels there with Lifestream, reads its placard and enters that plot only; Enter best match checks up to five shortlisted plots and enters at most one"),
            new("changelog.r1000.4", "Enter pays the plot's deposit for real: the lottery confirmation is checked for your character and the amount, then confirmed once"),
            new("changelog.r1000.5", "Refunds are collected for you: a result check reads a lost entry or a win not claimed in time and collects the refund it is owed"),
            new("changelog.r1000.6", "Claim house on My lottery visits a plot you won and claims it for you"),
            new("changelog.r1000.7", "An entry whose placard no longer offers a result is settled only on the strongest evidence, otherwise it stays pending on My lottery, and it never holds up your next entry"),
            new("changelog.r1000.8", "My lottery lists your entries and their results with one action each, keeps their receipts, and records a refund you collected yourself with Settled in game"),
            new("changelog.r1000.9", "Automatic refunds, on by default, check your results while you are idle, outside combat, duties and cutscenes, within a 10,000 gil travel budget per session and your gil reserve"),
            new("changelog.r1000.10", "Every press is written down before it is made, an entry, refund or claim whose outcome is unclear is read back and never repeated, and housing dialogs are recognized by the game's own data in your client's language"),
            new("changelog.r1000.11", "Saved searches and plot lists with notices, a run panel with Pause and Stop that follows you across pages, a GM alert, and a Compatibility list on the Plugins page with Copy diagnostics"),
            new("changelog.r1000.12", "Nine languages for the windows; Japanese, North American, European and Oceanian worlds, while Chinese and Korean worlds are not supported"),
            new("changelog.r1000.13", "YesAlready can stay on: it is paused while the plugin works with the lottery dialogs and resumes afterwards, while a fork or renamed build of it holds runs until it is turned off"),
        ];
    }

    internal static class About
    {
        public static readonly LocString SupportTitle = new("about.support.title", "Made with love and care");
        public static readonly LocString SupportBody = new("about.support.body", "This plugin is a one-person project, built in my free time because I love this game and its community. Keeping it updated takes a lot of those hours. If it has helped you, supporting me on Patreon or buying me a coffee means I can keep giving it that time. Thank you for being here.");
        public static readonly LocString SupportButton = new("about.support.button", "Support on Patreon");
        public static readonly LocString PatreonHint = new("about.support.hint", "Open Patreon · right-click to copy");
        public static readonly LocString CoffeeButton = new("about.support.coffeeButton", "Buy me a coffee");
        public static readonly LocString CoffeeHint = new("about.support.coffeeHint", "Open Buy Me a Coffee · right-click to copy");
        public static readonly LocString LinkHint = new("about.linkHint", "Click to open · right-click to copy");
        public static readonly LocString MadeBy = new("about.madeBy", "Made by {0}");
        public static readonly LocString Version = new("about.version", "v {0}");
        public static readonly LocString Community = new("about.community", "Community");
        public static readonly LocString DiscordTitle = new("about.discordTitle", "Join the Discord");
        public static readonly LocString DiscordBody = new("about.discordBody", "Get help, report bugs, share ideas and hear about updates first.");
        public static readonly LocString GitHubTitle = new("about.githubTitle", "View on GitHub");
        public static readonly LocString GitHubBody = new("about.githubBody", "Browse the source code and every release.");
        public static readonly LocString ReminderTitle = new("about.reminder.title", "A little reminder");
        public static readonly LocString FactsTitle = new("about.facts.title", "Did you know?");
        public static readonly LocString QuotesTitle = new("about.quotes.title", "Words to live by");
        public static readonly LocString JokesTitle = new("about.jokes.title", "Just for fun");

        public static readonly LocString[] Reminders =
        [
            new("about.reminder.1", "Been at it a while? Roll your shoulders and take one slow breath."),
            new("about.reminder.2", "Hydration check. When did you last drink some water?"),
            new("about.reminder.3", "Blink a few times and let your eyes rest for a moment."),
            new("about.reminder.4", "Stand up, stretch, and shake out your hands. Future you says thanks."),
            new("about.reminder.5", "Sit up and settle in comfortably. Your back will thank you later."),
            new("about.reminder.6", "Remember to eat something today. You matter more than any score."),
            new("about.reminder.7", "Eyes feel tired? Look at something far away for twenty seconds."),
            new("about.reminder.8", "Whatever you're chasing, you're allowed to take a break whenever."),
            new("about.reminder.9", "You're doing great. Be a little kinder to yourself today."),
            new("about.reminder.10", "A glass of water and a quick stretch can reset a long session."),
            new("about.reminder.11", "Unclench your jaw and drop your shoulders. There you go."),
            new("about.reminder.12", "Rest is part of the journey too. Step away whenever you need to."),
        ];

        public static readonly LocString[] Facts =
        [
            new("about.facts.1", "Honey never spoils. Jars over 3,000 years old have been found still edible."),
            new("about.facts.2", "Octopuses have three hearts and blue blood."),
            new("about.facts.3", "A day on Venus is longer than a whole year on Venus."),
            new("about.facts.4", "Bananas are berries, but strawberries aren't."),
            new("about.facts.5", "There are more possible chess games than atoms in the observable universe."),
            new("about.facts.6", "Sharks have been around longer than trees have."),
            new("about.facts.7", "A group of flamingos is called a flamboyance."),
            new("about.facts.8", "Honeybees can recognize individual human faces."),
            new("about.facts.9", "Wombat droppings are cube shaped."),
            new("about.facts.10", "The Eiffel Tower can grow over 15 cm taller on a hot day."),
            new("about.facts.11", "Hot water can sometimes freeze faster than cold water."),
            new("about.facts.12", "A bolt of lightning is roughly five times hotter than the surface of the Sun."),
        ];

        public static readonly LocString[] Quotes =
        [
            new("about.quotes.1", "Done is better than perfect. You can always polish later."),
            new("about.quotes.2", "Small steps every day add up to surprising distances."),
            new("about.quotes.3", "Comparison is the thief of joy. Run your own race."),
            new("about.quotes.4", "Progress, not perfection."),
            new("about.quotes.5", "You don't have to be great to start, but you have to start to be great."),
            new("about.quotes.6", "Be patient with yourself. Growth takes time."),
            new("about.quotes.7", "The best time to begin was yesterday. The second best is right now."),
            new("about.quotes.8", "Celebrate the small wins. They count too."),
            new("about.quotes.9", "Slow progress is still progress."),
            new("about.quotes.10", "Your only real competition is who you were yesterday."),
        ];

        public static readonly LocString[] Jokes =
        [
            new("about.jokes.1", "Why don't scientists trust atoms? Because they make up everything."),
            new("about.jokes.2", "I would tell you a chemistry joke, but I know I wouldn't get a reaction."),
            new("about.jokes.3", "Why did the scarecrow win an award? He was outstanding in his field."),
            new("about.jokes.4", "I'm reading a book about anti-gravity. It's impossible to put down."),
            new("about.jokes.5", "Why don't skeletons fight each other? They don't have the guts."),
            new("about.jokes.6", "What do you call fake spaghetti? An impasta."),
            new("about.jokes.7", "Why did the bicycle fall over? It was two tired."),
            new("about.jokes.8", "What do you call cheese that isn't yours? Nacho cheese."),
            new("about.jokes.9", "I'm on a seafood diet. I see food, and I eat it."),
            new("about.jokes.10", "I only know 25 letters of the alphabet. I don't know y."),
        ];
    }

    internal static class Settings
    {
        public static readonly LocString Title = new("settings.title", "Settings");
        public static readonly LocString Language = new("settings.language", "Language");
        public static readonly LocString LanguageHelp = new("settings.languageHelp", "The language of this plugin's windows. World, district and zone names always follow the game client.");
        public static readonly LocString SearchHint = new("settings.searchHint", "Search...");

        public static readonly LocString CatGeneral = new("settings.cat.general", "General");
        public static readonly LocString CatGeneralSub = new("settings.cat.generalSub", "Window and behavior preferences.");

        public static readonly LocString GeneralWindow = new("settings.general.window", "Window");
        public static readonly LocString OpenOnLogin = new("settings.general.openOnLogin", "Open on login");
        public static readonly LocString OpenOnLoginHelp = new("settings.general.openOnLoginHelp", "Pop the main window automatically the next time you log in.");
        public static readonly LocString DragAnywhere = new("settings.general.dragAnywhere", "Drag from anywhere");
        public static readonly LocString DragAnywhereHelp = new("settings.general.dragAnywhereHelp", "Move the window by dragging anywhere inside it. When off, only the top bar moves it.");
        public static readonly LocString GeneralBehavior = new("settings.general.behavior", "Behavior");
        public static readonly LocString AutoPause = new("settings.general.autoPause", "Auto-pause in content");
        public static readonly LocString AutoPauseHelp = new("settings.general.autoPauseHelp", "Pause the run while you are inside a duty, trial, raid, or any other instanced content, then resume it once you are back outside.");
        public static readonly LocString PauseTyping = new("settings.general.pauseTyping", "Hold while typing");
        public static readonly LocString PauseTypingHelp = new("settings.general.pauseTypingHelp", "While a text box of the game has focus, like the chat line, a run starts no new move until you close it. A move already under way carries on.");
        public static readonly LocString PostRunGroup = new("settings.postRun.group", "Chat commands");
        public static readonly LocString PostRunLabel = new("settings.postRun.label", "Run when the run finishes");
        public static readonly LocString PostRunHelp = new("settings.postRun.help", "One command per line, each starting with a slash. They run when a run ends on its own, never after Stop or a failure.");
        public static readonly LocString PostRunCaption = new("settings.postRun.caption", "Lines that do not start with a slash are skipped, so nothing is ever said in chat. Try /li home to head home or /ays m to hand off to AutoRetainer.");
    }

    internal static class Plugin
    {
        public static readonly LocString CommandHelp = new("plugin.commandHelp", "Toggle the Auto House Lottery window. /autohouselottery config | lottery | saved | deps | log | changelog | about | stop | pause | resume | target (dump the current target's BaseId).");
        public static readonly LocString CommandHelpAlias = new("plugin.commandHelpAlias", "Alias for /autohouselottery.");
        public static readonly LocString ConfigReset = new("plugin.configReset", "Your settings could not be read, so Auto House Lottery started with default settings. The old file is kept as {0}.");
        public static readonly LocString ConfigResetUnsaved = new("plugin.configResetUnsaved", "Your settings could not be read, so Auto House Lottery started with default settings. The old file could not be kept; the log has the details.");
    }
}
