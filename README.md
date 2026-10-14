<p align="center">
  <img src="AutoHouseLottery/Images/Icon.png" width="180" alt="Auto House Lottery icon" />
</p>

<h1 align="center">Auto House Lottery</h1>

<p align="center">
  <a href="https://discord.gg/hppkAvdBEE"><img alt="Discord" src="https://img.shields.io/badge/Discord-join-5865F2?style=flat-square&logo=discord&logoColor=white"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoHouseLottery/releases/latest"><img alt="Release" src="https://img.shields.io/github/v/release/XeldarAlz/FFXIV-AutoHouseLottery?style=flat-square&color=blue"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoHouseLottery/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/XeldarAlz/FFXIV-AutoHouseLottery/total?style=flat-square&color=blue&cacheSeconds=300"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoHouseLottery/actions/workflows/ci.yml"><img alt="Build" src="https://img.shields.io/github/actions/workflow/status/XeldarAlz/FFXIV-AutoHouseLottery/ci.yml?style=flat-square"></a>
  <a href="LICENSE.md"><img alt="License" src="https://img.shields.io/badge/license-AGPL--3.0--or--later-blue?style=flat-square"></a>
</p>

<p align="center">
  <em>Find an empty plot, enter its housing lottery, collect your refund or claim your house. Built on Dalamud.</em>
</p>

---

## What it does

Shows empty housing plots on a map from public listings, enters the lottery on the plot you pick or the best match on your shortlist, then checks the result and collects your refund or claims your house.

Every housing plot in FFXIV is sold through a lottery: during the entry period each character can enter one lottery, paying the full price as a deposit, and when the results come in the winner claims the house while everyone else collects their deposit back.

Every game action goes through the same safety model: the plugin checks the plot, the character, the buyer type and the deposit at the placard and again on the lottery confirmation, writes down what it is about to do before it presses anything that spends gil, and never presses again after a press whose outcome is unclear. Actions that have not yet been validated in game stop before their final press, as described below.

## Validation status

A press that spends gil or takes a house is enabled only after it has been validated in game on the supported game version. Until then that action runs as a dry run: the plugin does everything up to the final press, shows you what it would do, and closes the dialog instead. The one exception is the validation itself: the maintainer's `/ahl dev allow-real-*` permissions each allow one real press, for the logged-in character on one named plot, for the current session only, and the Plugins page shows it while one is open. Current status:

- **Entering the lottery**: dry run only until validated. Enter goes as far as the lottery confirmation and closes it; no deposit is paid.
- **Collecting refunds**: dry run only until validated. Result checks read the refund and close it; collect refunds in game.
- **Claiming a house**: dry run only until validated. A win is read and closed; claim it in game.
- **Placards without a result**: dry run only until validated. An entry whose placard no longer offers a result stays pending on My lottery instead of being recorded as settled.
- **Entering for several characters**: dry run only until validated, on its own after single entries. Enter for N characters takes every character as far as its lottery confirmation and closes it; a one-plot real-entry permission never makes a batch real.
- **Switching characters**: off until validated. Each character is processed at its own login. A maintainer's `/ahl dev allow-switching` or `allow-refund-switching` permission lets the next batch, or the refund queue, switch characters for at most two hours.

The Plugins page (`/ahl deps`) shows the same lines under Compatibility, next to the state of everything else the plugin depends on.

## Features

- **Find a home**: the plots reported vacant for your home world (or any other world, to look around), on the district map and in a list. Filter by district, ward, main division or subdivision, size, price, buyer type (Private or Free Company) and lottery phase, and under More filters by plot number, reported entries and observation age. Each plot shows its deposit, its reported entries and how long ago it was observed; an entry count nobody reported stays unknown instead of reading as zero. Listings refresh every 20 minutes while the window is open, and a saved copy is shown with its date when the service does not answer.
- **Shortlist**: add plots one by one from the map, the list or a plot's details, or every matching plot at once with Add all matching, including wards that are not on screen. The shortlist holds the plots you would accept; a run makes at most one entry per character from it, as the game allows, and the header always says so (42 plots · 1 entry this cycle).
- **Enter a plot or the best match**: Enter on a plot travels there, reads its placard and enters that plot only, for the exact deposit it showed, with no fallback. Enter best match ranks the shortlist with the plots reported open for entry first, then by fewest reported entries (unknown counts last), lowest deposit and address, inspects at most five plots and enters the first one that still passes the live checks, stopping after one entry. The run preview shows the maximum deposit, the gil you keep in reserve and anything that would stop the run before you press.
- **Run panel**: the active run's address, deposit and step (getting ready, travelling, approaching the plot, checking eligibility, entering the lottery, verifying the entry), with Pause and Stop. When travel ends away from the plot, Visit manually lets you walk there and open the placard yourself. A compact strip with the same controls follows you to the other pages and stays on screen when the main window is closed.
- **My lottery**: your entries with their deposit, lottery number, phase and deadline, and one action each: Check result, Claim house, Collect refund, Verify outcome or View receipt. A win is presented as a claim and never collected as a refund while it can still be claimed; once its claim window has passed, the game offers the forfeit refund (the deposit less a 50% fee), which is collected like any other refund. If you collected a refund in game yourself, **Settled in game** (after a confirmation) records it as refunded for an amount not seen. An entry made outside the plugin shows up once the plugin has read your Timers window.
- **Automatic refunds**: on by default per character, since the game refuses a new entry while an earlier refund is waiting. See [Automatic refunds](#automatic-refunds).
- **Several characters**: one entry per ticked character in one run, each paying from its own gil, placed by ownership group. See [Multiple characters](#multiple-characters).
- **Saved**: named searches (areas and filters, matched again each time) and fixed plot lists, with notices when a saved plot opens for entry, is about to close, or has its results, and when a saved search finds a new plot.
- **Compatibility and diagnostics**: the Plugins page lists every part the plugin depends on with its state and a plain reason, and Copy diagnostics puts a bug-report-ready summary on your clipboard with character names and IDs replaced.
- **Console**: every step of a run is logged with the `[AHL]` prefix, ready to copy into a bug report.
- **GM alert**: ends a run when a GM is near, with an optional pop-up, chat line, beeps, custom commands, or closing the game.

## How a run works

1. Pick a plot on Find a home and press **Enter for (deposit)**, or build a shortlist and press **Enter best match**. Choose Private or Free Company first, and for a best match a maximum price; the button says why when it cannot start (no character logged in, an entry already made this lottery period, an earlier entry still being checked, the entry period closed, and so on).
2. The run freezes what you saw: the plot or the ranked shortlist, the deposit limit, the gil reserve and the lottery period. If an earlier refund is known to wait and automatic refunds are on, the run first visits that plot to collect it; while collecting refunds is dry run only, that visit reads the refund and the run stops there, so you collect it in game. With automatic refunds off, the run stops and says so. An earlier win stops the run until you claim it or give it up.
3. It travels to the plot with Lifestream and walks up to its placard with vnavmesh, or asks you to walk the last metres.
4. It reads the placard and checks that it is the plot it came for, still for sale by lottery, in its entry period, for your buyer type, at the same price, in the same lottery period. A best match skips a plot that fails and tries the next, up to five.
5. It presses Enter Lottery and reads the lottery confirmation: the deposit must match the one the run started with and fit your limit and gil after the reserve. A dry run presses Cancel here. A real entry records its intent first, then confirms, and verifies the entry from the game's own answer.
6. My lottery keeps the receipt, and the plugin checks the result once the results period starts.

## Safety

- **Dry run**: every Enter runs as a dry run until entering has been validated (see [Validation status](#validation-status)). A dry run travels, reads the placard and opens the real lottery confirmation, then presses Cancel; nothing is paid. Result checks, refunds and claims follow their own validation lines the same way.
- **Intent before every press**: before any press that spends gil or takes a house, the plugin writes what it is about to do (character, plot, buyer type, deposit, lottery period) to its entry records on disk. If the game, the plugin or the connection stops right after, the next load reads the press back from the game instead of guessing.
- **Never twice**: one character makes at most one entry per lottery period. An entry, refund or claim whose outcome is unclear is never pressed again, and no other attempt of that kind starts, until it has been read back from your Timers window or the plot's placard.
- **Exact means exact**: Enter on a plot never enters another plot, and never for a different deposit. Enter best match enters at most one plot.
- **Dialogs by game data**: every housing dialog and message is recognized by its row in the game's own data, read in your client's language, never by English text. A dialog the plugin does not recognize is left alone and the run stops.
- **Refunds and claims are separate**: a refund is collected only for a confirmed loss, or for a win whose claim window has passed (the forfeit refund, less the game's 50% fee), and counts only when your gil rose by exactly the amount the game offered. A win that can still be claimed only ever leads to a claim.
- **Plugins that answer dialogs**: while a plugin such as YesAlready is loaded, nothing that opens a lottery dialog starts, dry run or not, and a run checks again when it resumes and right before it presses Enter Lottery or View Lottery Results. See [Testing and debug commands](#testing-and-debug-commands).
- **Stops for you**: Pause and Stop act before the next step; a press that may already have reached the game is verified first. A duty pauses the run (Settings, General), a GM alert ends it, and a logout or a reload ends it without resuming on its own.
- **What it never does**: buy or sell anything else, enter a plot you did not choose or for more than its limit, collect a refund on a win that can still be claimed, relinquish an estate, or answer a prompt it does not know.

## Automatic refunds

On by default for every character; turn it off per character on My lottery. Once results are out and you have left the game alone for 30 seconds (keyboard, mouse and gamepad), outside combat, duties and cutscenes, the plugin visits your entered plots and reads their results, within a 10,000 gil travel budget per session and your gil reserve. When you act again it stops before its next step; once it has pressed Yes on a refund it first finishes checking that the refund went through, which can take about a minute. A refund counts as collected only when your gil rose by exactly the amount the game offered and the placard no longer offers the result; anything else is set aside for you to check. After a GM alert no automatic visit starts until you act in the game again.

An entry the plugin cannot settle (its placard offers you no result, its result could not be read, or its address names no single plot) stays pending on My lottery and is left to the game: it never holds up your next entry, since the game itself refuses an entry while an earlier refund waits, and when it does, the plugin opens those entries again. My lottery shows the refunds received, the travel spent, the net returned and what is still pending.

While collecting refunds is dry run only (see [Validation status](#validation-status)), these visits check and record each result and close the refund dialog: the result is on My lottery and the refund waits for you in game.

## Multiple characters

Tick characters on the Characters list in the shortlist drawer and the drawer plans exactly the ticked ones (with none ticked it plans the logged-in character as before). Each character makes at most one entry per lottery period and pays from its own gil; nothing is pooled.

- **Characters list**: every character the plugin has seen logged in. Give each ticked character its own buyer type, target (the best match or one plot of the shortlist on its home world), deposit cap and reserve; anything left blank takes the filters' buyer type, the price ceiling and the shared reserve. A character that is not logged in shows the gil it had at its last login, marked as an estimate: that amount never proves or rules out that it can pay. Characters that already entered this period, or whose home world is not supported, say so.
- **Ownership groups**: the game lets only one plot be claimed per Free Company for Free Company plots, and per service account and home world for private plots. Set each character's service account on the Characters list; until you do, private characters on one world count as one account, which can never produce a second win that cannot be claimed.
- **Placement**: By group (the default) puts a group's characters on the same plot and spreads separate groups across the shortlist in rank order; Same plot puts every group on the top plot; Spread gives each character the next plot. A character's own target is entered exactly, with no fallback, and the rest of its group goes on that plot too, with no fallback; a member that cannot enter it, or whose own target is another plot, is left out with that reason. A character that already entered this period, or has an entry being checked, holds its group on its plot the same way, and one whose entered plot could not be read leaves the rest of its group out. When a member enters, or its own check finds it already entered, the members of its group still waiting are pinned to that plot, and any whose plan does not include it are left out, so under By group or Same plot a group never enters two plots. While a member's entry is being checked, the rest of its group waits, and the batch card says so, until a Continue reads it back from the journal; other groups go on meanwhile. Next to Placement the run preview shows the estimated chance of at least one win and, when Spread puts one group on several plots, of two or more wins, from the reported entry counts; it is hidden when any count is unknown. Spreading inside one group needs your explicit confirmation that only one of its wins can be claimed.
- **Enter for N characters**: always a dry run for now. Every character goes through exactly the same steps and checks as a single run, including the check for a plugin that answers dialogs on its own before each Enter Lottery, as far as its lottery confirmation, which it closes. Real batch entries stay off until batches have been validated in game. The logged-in character goes first; right before its turn each character's gil is checked against its whole target order, and its run reads its lottery record before anything else. The others wait for their own login: log in with one of them and press **Continue** on the batch card (the chat reminds you); the card says why when Continue cannot go on. The batch card lists every character's outcome with the entries first, so characters that were left out, could not pay or have not logged in yet never hide the entries the others made. A new batch is refused while one still waits; continue it or discard it. The batch ends with the entry period.
- **Several wins in one group**: once claiming is validated, My lottery asks you to choose which plot to claim (press Claim house twice on the one you want); a second claim in that group is always refused.

### Character switching

The plugin can switch characters itself through Lifestream's character change, but this is **off until it has been validated in game**. While it is off, a batch processes only the logged-in character and every other character waits for its own login, and each character's results and refunds are handled at its own login. For the validation, `/ahl dev allow-switching` lets the next batch started (or continued) switch, until that batch finishes, is discarded or fails a switch, or for two hours at most; `/ahl dev allow-refund-switching` lets the refund queue switch for two hours; `/ahl dev revoke-switching` withdraws both and stops a switch either one started.

With switching allowed, a batch saves its progress to disk, asks Lifestream to log in the next character, and does nothing for it until the game shows exactly that character (content ID and home world) and Lifestream has finished; its gil, Free Company and estates are then read again and its run reads its lottery record before anything else. No switch starts during a duty, match or world visit queue, while vnavmesh is moving you, or while AutoRetainer's multi-mode is on. The switch has a five-minute deadline; a failed login, another character chosen at the character screen, a cancel, or a plugin reload on the title screen stops the batch, and a stopped or reloaded batch continues only from your Continue.

**Switch characters to collect refunds** on My lottery (off by default) lets the refund queue, once the logged-in character has nothing left to settle and you have left the game alone, log in the next character ticked on the Characters list that has a result waiting and check it there; while collecting refunds is dry run only, that check reads the refund and closes it. Each character is visited once until the plugin is unloaded, never an unticked character and never one whose home world is not supported. The plugin passes only a character name and world to Lifestream and stores no account credentials.

## Install

In-game: `/xlsettings` → **Experimental** → paste into **Custom Plugin Repositories**:

```
https://raw.githubusercontent.com/XeldarAlz/DalamudPlugins/main/repo.json
```

Tick **Enabled**, click **+**, then **Save and Close**. Open `/xlplugins` → **All Plugins**, search for **Auto House Lottery**, and install.

## Requirements

- **Lifestream** for travelling to a plot. Browsing, the shortlist, Saved and My lottery work without it; entering, checking results and collecting refunds need it.
- **vnavmesh**, optional, for the last metres from where travel ends to the plot's placard. Without it the plugin asks you to walk up to the placard yourself.
- **A world on the global client**: Japanese, North American, European and Oceanian worlds. Chinese and Korean worlds have their own housing services and are not supported.
- **A character the game lets enter**: the game's own housing rules still apply, such as one entry per character per lottery period, the Private and Free Company requirements, and no other land owned on the same service account and world.

Open `/ahl deps` after install to see the helpers, one-click install each missing one, and check Compatibility.

## Commands

| Command | Action |
|---|---|
| `/autohouselottery` | Toggle the main window |
| `/ahl` | Alias for `/autohouselottery` |
| `/autohouselottery config` | Open the Settings page |
| `/autohouselottery lottery` | Open the My lottery page |
| `/autohouselottery saved` | Open the Saved page |
| `/autohouselottery deps` | Open the Plugins page with Compatibility (`/autohouselottery dependencies` works too) |
| `/autohouselottery log` | Open the Console page |
| `/autohouselottery changelog` | Open the Changelog page |
| `/autohouselottery about` | Open the About page |
| `/autohouselottery stop` | Stop the run or the result visit before its next step; a press that may already have reached the game is verified first |
| `/autohouselottery pause` | Pause the run before its next step |
| `/autohouselottery resume` | Resume a paused run, or retry one that waits for you, from a fresh check of the character and the plot |
| `/autohouselottery target` | Log the targeted object's BaseId (debug helper) |

## Testing and debug commands

None of these is needed to use the plugin. `probe` reads what the game shows and does only things that cost nothing; `dev` is the maintainer's tool for validating each game action in game. Both write to the Console (`/ahl log`).

| Command | Action |
|---|---|
| `/autohouselottery probe` | Log every housing reading for where you stand: character, location, lottery entry, ward list, placard, open lottery dialogs, housing messages and Lifestream |
| `/autohouselottery probe scan` | Page the open ward list through every ward and record each ward it reads |
| `/autohouselottery probe lottery` | Open the Timers window once to load your lottery entry, then log it |
| `/autohouselottery probe goto <district> <ward> <plot>` | Travel with Lifestream to a plot on this world (`mist`, `lavender`, `goblet`, `shirogane`, `empyreum`), walk to the nearest placard, and once you open it, confirm from its address that it is that plot; nothing is pressed |
| `/autohouselottery probe approach <ward> <plot>` | Walk to a plot's placard in the district you are in |
| `/autohouselottery probe press <enter \| results \| cancel \| no>` | Do one thing that costs nothing: press Enter Lottery or View Lottery Results on the placard, close the entry confirmation, or press No on a result dialog. Each button is checked by its own label first, and nothing is pressed while a run or a probe task is going |
| `/autohouselottery probe stop` | Stop the probe task and the travel it started |
| `/autohouselottery dev run <district> <ward> <plot> <deposit> [fc] [real]` | Enter one plot on your home world for exactly that deposit. It is a dry run unless the word `real` is given and a real-entry permission covers that plot |
| `/autohouselottery dev allow-real-entry <character name> <district> <ward> <plot> <max gil>` | Allow one real entry for the logged-in character on that plot, up to that deposit, for a `dev run ... real` on exactly that plot. It never applies to a best match, lives only in this session and ends at logout, at unload, or after the one entry |
| `/autohouselottery dev revoke-real-entry` | Withdraw the real-entry permission |
| `/autohouselottery dev settle <receipt or district ward plot> [claim] [real]` | Visit the plot of one of your entries (by the first characters of its receipt id, or by its address) and read its result. It closes the result as a dry run unless the word `real` is given and a real refund (or, with `claim`, a real claim) permission covers that plot |
| `/autohouselottery dev allow-real-refund <character name> <district> <ward> <plot>` | Allow the logged-in character to accept the refund of its unsettled entry on that plot once, through `dev settle ... real` or an automatic refund. It lives only in this session and ends at logout, at unload, or after the one press |
| `/autohouselottery dev allow-real-claim <character name> <district> <ward> <plot>` | Allow the logged-in character to claim its winning entry's plot once, through `dev settle ... claim real`. It lives only in this session and ends at logout, at unload, or after the one press |
| `/autohouselottery dev revoke-real-settlement` | Withdraw the real refund and claim permissions |
| `/autohouselottery dev allow-switching` | Let the next batch started or continued switch characters through Lifestream, until it finishes, is discarded or fails a switch, or for two hours at most. Its entries stay dry runs |
| `/autohouselottery dev allow-refund-switching` | Let the refund queue switch to the ticked characters through Lifestream, with Switch characters to collect refunds on, for two hours |
| `/autohouselottery dev revoke-switching` | Withdraw both switching permissions and stop a switch either one started |

**Turn off plugins that answer dialogs.** While a plugin that answers dialogs on its own is loaded (any plugin whose internal name contains YesAlready), the plugin refuses to start anything that opens a lottery dialog: entry runs (dry or real), result checks, refunds, claims and the probe's `press enter` and `press results`. An entry run checks again when it resumes, after a pause, a duty or Visit manually, and right before it presses Enter Lottery or View Lottery Results, and stops for you if one was turned on meanwhile. A dry run opens the real lottery confirmation and the real result dialogs, and such a plugin could press Confirm or Yes on them before the plugin closes them.

## Privacy

- **Fetched from the housing service**: the public listings of the world you browse and of the worlds your saved searches, lists and shortlist point at, from `https://housing-api.yozoracho.dev/api/v1`, every 20 minutes while the window is open and every hour while only Saved or the shortlist needs them. The requests carry the plugin's name and version, and nothing about you or your characters.
- **Read from the game, never sent anywhere**: your character's name, home world, gil and Free Company, your lottery entries and their results, and during a run the plot's placard, the lottery windows and any dialog that opens.
- **Kept on disk in the plugin's config folder**: your settings (including the characters seen on this install, by name, home world and content id, so My lottery can name them), your shortlist and Saved library, the housing service's answers (each world's listings for 7 days and the list of worlds for 30, deleted when the plugin loads after that), the lottery calendar, and `journal.jsonl`, the record of every entry, refund and claim the plugin made, kept until each one is settled so an interrupted run is checked instead of repeated.
- **Copied only when you press a copy button**: Copy log on the Console and Copy diagnostics on the Plugins page. Both replace character names and content IDs with aliases, and your home folder (on Windows, Wine, Linux or macOS) and the plugin configuration folder with placeholders.

## Languages

The windows are available in English, Deutsch, Français, Español, Português (Brasil), Русский, Türkçe, 日本語, and 中文. The plugin picks a language from your Dalamud and game client settings on first launch; change it any time under Settings, General, Language. Game data such as world, district and zone names always follows the game client.

The housing dialogs, buttons and messages are matched by the game's own text rows in your client's language, never by English text, in every client language. Which client languages have been checked in game so far, and what is still to check, is in the compatibility report under [docs/compatibility](docs/compatibility).

Spotted a wrong or awkward translation? Open a [translation issue](https://github.com/XeldarAlz/FFXIV-AutoHouseLottery/issues/new?template=translation_report.yml) and tell me what it should say instead.

## FAQ

**Does it bid on every plot on my shortlist?** No. The shortlist holds the plots you would accept. A run makes at most one entry per character, on the plot you pressed Enter on or on the first plot of the shortlist that passes the live checks.

**Why does Enter only go as far as the confirmation?** Entering has not been validated in game on this version yet; see [Validation status](#validation-status). The dry run still travels, reads the placard and opens the real confirmation, so you can watch every check it makes.

**Can it enter for my other characters?** Not yet. This version enters for the character you are logged in with.

**Does it find every empty plot?** It shows what the public listings report and when each plot was last observed. A plot nobody reported is not shown, and the plugin checks the placard in game before it enters.

**Why did my run stop?** The run panel says why, and the Console has every step. Common reasons: the plot was sold or changed price, the entry period closed, an earlier refund still waits, Lifestream was busy, or a plugin that answers dialogs is loaded.

**I won. What now?** Claim it in game at the plot's placard before the results period ends: an unclaimed win is forfeited, and its refund keeps back a 50% fee. Until claiming is validated (see [Validation status](#validation-status)), the win's row on My lottery and its notice say to claim it in game, and the row's button only reads the win and closes it. Building the house is a separate step in game.

**A refund shows as pending. Is my deposit lost?** No. The game keeps the refund until the deadline My lottery shows. Collect it at the plot's placard in game, or let Check result read it again. If you already collected it in game, press Settled in game on My lottery.

## Community

Questions, ideas, or just want to hang out with other players? Come say hi on Discord.

→ [Join our Discord](https://discord.gg/hppkAvdBEE)

## More from me

If you liked this plugin, take a look at my other Dalamud work. You might find something else there for you.

→ [XeldarAlz Dalamud Plugins](https://github.com/XeldarAlz/DalamudPlugins)

## License

AGPL-3.0-or-later. See [LICENSE.md](LICENSE.md). [NOTICE](NOTICE) adds the attribution terms the AGPL allows: a fork, or any project that reuses this code, must credit the original author and must not pass itself off as the original. The license covers the code, not the name or the icon: read the [trademark and naming policy](TRADEMARK.md) before you publish a fork.

Third-party code, data and references are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). How AI is used to build this plugin is written down in [AI usage](AI-USAGE.md).
