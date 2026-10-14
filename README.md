<p align="center">
  <img src="AutoHouseLottery/Images/Icon.png" width="180" alt="Auto House Lottery icon" />
</p>

<h1 align="center">Auto House Lottery</h1>

<p align="center">
  <a href="https://discord.gg/hppkAvdBEE"><img alt="Discord" src="https://img.shields.io/badge/Discord-join-5865F2?style=flat-square&logo=discord&logoColor=white"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoHouseLottery/actions/workflows/ci.yml"><img alt="Build" src="https://img.shields.io/github/actions/workflow/status/XeldarAlz/FFXIV-AutoHouseLottery/ci.yml?style=flat-square"></a>
  <a href="LICENSE.md"><img alt="License" src="https://img.shields.io/badge/license-AGPL--3.0--or--later-blue?style=flat-square"></a>
</p>

<p align="center">
  <em>Find an empty plot, enter its housing lottery, collect your refund or claim your house. Built on Dalamud.</em>
</p>

---

## Status

**In development, not released.** The window, its settings, the Console and the changelog are in place. Find a home already shows the reported plots on the district map and in a list, with the shortlist, the best match and a run preview, and Saved keeps searches, plot lists and their notices; Enter on a plot or on the best match runs the entry as a dry run that goes as far as the lottery confirmation and closes it, and My lottery lists your entries and checks their results; collecting a refund or claiming a house reads the result and closes it as a dry run until each has been validated on a real result. There is nothing to install yet, and this README describes what the plugin is being built to do. A full README comes with the release.

## What it will do

Every housing plot in FFXIV is sold through a lottery: during the entry period each character can enter one lottery, paying the full price as a deposit, and when the results come in the winner claims the house while everyone else collects their deposit back. Auto House Lottery shows the empty plots reported by public listings on a map, enters the lottery on the plot you pick or the best match on your shortlist, then checks the result and collects your refund or claims your house.

- **Find a home**: reported empty plots for your home world on the district map and in a list, filtered by district, ward, size, price and lottery phase. Each plot shows its deposit, its reported number of entries and how long ago it was seen, and an entry count nobody reported stays unknown instead of reading as zero.
- **Shortlist**: add plots one by one from the map or the list, or every matching plot at once. The shortlist holds the plots you would accept; a run makes at most one entry per character from it, as the game allows, and the header always says so (42 plots · 1 entry this cycle).
- **Enter a plot or the best match**: Enter on a plot travels there, reads its placard and enters that plot only, for the exact deposit it showed. Enter best match ranks the shortlist by fewest reported entries, then lowest deposit, then address, and enters the first plot that still passes the live checks, stopping after one entry.
- **My lottery**: your entries with their deposit, lottery number and phase, and one action each: Check result, Claim house, Collect refund or View receipt. A win is always presented as a claim and never collected as a refund.
- **Automatic refunds**: on by default per character, since the game refuses a new entry while an earlier refund is waiting. When results are in and you have left the game alone for 30 seconds (keyboard, mouse and gamepad), outside combat, duties and cutscenes, the plugin visits your entered plots and collects the deposits of confirmed losses, within a 10,000 gil travel budget per session and your gil reserve. When you act again it stops before its next step; once it has pressed Yes on a refund it first finishes checking that the refund went through, which can take about a minute. A refund counts as collected only when your gil rose by exactly the amount the game offered and the placard no longer offers the result; anything else is set aside for you to check. A plot whose placard no longer offers you a result stays pending on My lottery: the plugin does not treat it as settled. Such an entry, one whose result could not be read, or one whose address names no single plot never holds up your next entry: the game itself refuses an entry while an earlier refund waits, and when it does, the plugin opens those entries again. If you collected a refund in game yourself, **Settled in game** on My lottery (after a confirmation) records it as refunded for an amount not seen; it does not apply to an entry whose submission is still in question. After a GM alert the plugin stops at once and starts no automatic visit until you act in the game again. My lottery shows the refunds received, the travel spent, the net returned and what is still pending.
- **Saved**: named searches and fixed plot lists to start the next lottery period from.
- **Careful with your gil**: the plugin records what it is about to do before it confirms an entry, never submits again after an entry whose result is unclear, and has a dry run that goes all the way to the lottery confirmation and presses Cancel.
- **Several characters (1.1)**: see [Multiple characters](#multiple-characters) below.

## Multiple characters

Version 1.1 enters for several of your characters in one run. Each character still makes at most one entry per lottery period and pays from its own gil; nothing is pooled.

- **Characters list**: the Characters expander in the shortlist drawer lists every character the plugin has seen logged in. Tick the ones that should enter, and give each its own buyer type, target (the best match or one plot of the shortlist on its home world), deposit cap and reserve; anything left blank takes the filters' buyer type, the price ceiling and the shared reserve. A character that is not logged in shows the gil it had at its last login, marked as an estimate: that amount never proves or rules out that it can pay, and its gil is read again before its turn. Characters that already entered this period say so.
- **Ownership groups**: the game lets only one plot be claimed per Free Company for Free Company plots, and per service account and home world for private plots. Set each character's service account on the Characters list; until you do, private characters on one world count as one account, which can never produce a second win that cannot be claimed.
- **Placement**: By group (the default) puts a group's characters on the same plot and spreads separate groups across the shortlist in rank order; Same plot puts everyone on the top plot; Spread gives each character the next plot. A character's own target is always entered exactly, with no fallback, and the rest of its group follows it. Next to Placement the run preview shows the estimated chance of at least one win and, when one group spreads over several plots, of two or more wins, from the reported entry counts; it is hidden when any count is unknown. Spreading inside one group needs your explicit confirmation that only one of its wins can be claimed. When a Same plot member falls back to another plot, the rest of its group follows the plot it entered.
- **Enter for N characters**: a dry run, like every Enter, that goes as far as each character's lottery confirmation and closes it. Every character goes through exactly the same steps and checks as a single run, including the check for a plugin that answers dialogs on its own before each Enter Lottery; a real entry needs the same one-character, one-plot permission as a single run and is never widened by a batch. The logged-in character goes first. The others wait for their own login: log in with one of them and press **Continue** on the batch card (the chat reminds you). The batch card lists every character's outcome with the entries first, so characters that were left out, could not pay or have not logged in yet never hide the entries the others made. The batch ends with the entry period.
- **Several wins in one group**: My lottery asks you to choose which plot to claim (press Claim house twice on the one you want), and a second claim in that group is refused afterwards.

### Character switching

The plugin can switch characters itself through Lifestream's character change, but this is **off until it has been validated in game**. While it is off, a batch processes only the logged-in character and every other character waits for its own login, and refunds are collected by each character at its own login. A maintainer can allow switching for one session with `/ahl dev allow-switching`. With it allowed, a batch saves its progress to disk, asks Lifestream to log in the next character, and does nothing for it until the game shows exactly that character (content ID and home world); its gil, entry slot, Free Company and estates are then read again. The switch has a five-minute deadline; a failed login, another character chosen at the character screen, a cancel, or a plugin reload on the title screen stops the batch, and a reloaded batch continues only from your Continue. **Switch characters to collect refunds** on My lottery (off by default) then lets the refund queue log in the next character ticked on the Characters list that has a result or refund waiting, once per session per character, never any other character. The plugin passes only a character name and world to Lifestream and stores no account credentials.

## Requirements

- **Lifestream** for travelling to a plot. Browsing, the shortlist and My lottery work without it; entering, checking results and collecting refunds need it. The Plugins page (`/ahl deps`) installs it in one click.
- **A character the game lets enter**: the game's own housing rules still apply, such as one entry per character per lottery period, the Private and Free Company requirements, and no other land owned on the same service account and world.

## Commands

| Command | Action |
|---|---|
| `/autohouselottery` | Toggle the main window |
| `/ahl` | Alias for `/autohouselottery` |
| `/autohouselottery config` | Open the Settings page |
| `/autohouselottery lottery` | Open the My lottery page |
| `/autohouselottery saved` | Open the Saved page |
| `/autohouselottery deps` | Open the Plugins page (`/autohouselottery dependencies` works too) |
| `/autohouselottery log` | Open the Console page |
| `/autohouselottery changelog` | Open the Changelog page |
| `/autohouselottery about` | Open the About page |
| `/autohouselottery stop` | Stop the running lottery run, batch or result visit before its next step; a press that may already have reached the game is verified first |
| `/autohouselottery pause` | Pause the running lottery run before its next game-changing step |
| `/autohouselottery resume` | Resume a paused run, or retry one that is waiting for you, from a fresh check of the character and the plot; with no run going, continue a waiting batch for the logged-in character |
| `/autohouselottery target` | Log the targeted object's BaseId (debug helper) |
| `/autohouselottery probe` | Log every housing reading for where you stand to the Console: character, location, lottery entry, ward list, placard, open lottery dialogs, housing messages and Lifestream (debug helper) |
| `/autohouselottery probe scan` | Page the open ward selector through every ward and log each acknowledged ward |
| `/autohouselottery probe lottery` | Open the Timers window once to load your lottery entry, then log it |
| `/autohouselottery probe goto <district> <ward> <plot>` | Travel with Lifestream to a plot on this world (`mist`, `lavender`, `goblet`, `shirogane`, `empyreum`), walk to the nearest placard, and once you open it, confirm from its address that it is that plot; nothing is pressed |
| `/autohouselottery probe approach <ward> <plot>` | Walk to a plot's placard in the district you are in |
| `/autohouselottery probe press <enter \| results \| cancel \| no>` | Do one thing that costs nothing: press Enter Lottery or View Lottery Results on the placard, close the entry confirmation, or press No on a result dialog. Each button is checked by its own label first, and nothing is pressed while a run or a probe task is going |
| `/autohouselottery probe stop` | Stop the probe task and the travel it started |
| `/autohouselottery dev run <district> <ward> <plot> <deposit> [fc] [real]` | Maintainer tool for the in-game ladder: enter one plot on your home world for exactly that deposit. It is a dry run that stops at the lottery confirmation and closes it, unless the word `real` is given and a real-entry permission covers that plot |
| `/autohouselottery dev allow-real-entry <character name> <district> <ward> <plot> <max gil>` | Maintainer tool for the one supervised real entry: allow one real entry for the logged-in character on that plot, up to that deposit, for a `dev run ... real` on exactly that plot. It never applies to a best match, lives only in this session and ends at logout, at unload, or after the one entry |
| `/autohouselottery dev revoke-real-entry` | Withdraw the real-entry permission; every run is a dry run again |
| `/autohouselottery dev settle <receipt or district ward plot> [claim] [real]` | Maintainer tool for the in-game ladder: visit the plot of one of your entries (by the first characters of its receipt id, or by its address), read its result through View Lottery Results and record it. It closes the result as a dry run unless the word `real` is given and a real refund (or, with `claim`, a real claim) permission covers that plot |
| `/autohouselottery dev allow-real-refund <character name> <district> <ward> <plot>` | Maintainer tool for the supervised real refund: allow the logged-in character to accept the refund of its unsettled entry on that plot once, through `dev settle ... real` or an automatic refund. It is bound to that entry, lives only in this session and ends at logout, at unload, or after the one press |
| `/autohouselottery dev allow-real-claim <character name> <district> <ward> <plot>` | Maintainer tool for the supervised real claim: allow the logged-in character to claim its winning entry's plot once, through `dev settle ... claim real`. It is bound to that entry, lives only in this session and ends at logout, at unload, or after the one press |
| `/autohouselottery dev revoke-real-settlement` | Withdraw the real refund and claim permissions; every result visit is a dry run again |
| `/autohouselottery dev allow-switching` | Maintainer tool for the in-game ladder: let batches and, with Switch characters to collect refunds on, the refund queue switch characters through Lifestream for this session. Entries stay dry runs unless a real-entry permission covers them; it ends at unload |
| `/autohouselottery dev revoke-switching` | Withdraw the switching permission; each character is processed at its own login again |

While a plugin that answers dialogs on its own, such as YesAlready, is loaded, the plugin refuses to start anything that opens a lottery dialog: entry runs (dry or real), result checks, refunds, claims and the probe's `press enter` and `press results`. An entry run checks again when it resumes, after a pause, a duty or Visit manually, and right before it presses Enter Lottery, and stops for you if one was turned on meanwhile. A dry run opens the real lottery confirmation and the real result dialogs, and such a plugin could press Confirm or Yes on them. Turn it off first.

## Languages

The windows are available in English, Deutsch, Français, Español, Português (Brasil), Русский, Türkçe, 日本語, and 中文. The plugin picks a language from your Dalamud and game client settings on first launch; change it any time under Settings, General, Language. Game data such as world, district and zone names always follows the game client.

Spotted a wrong or awkward translation? Open a [translation issue](https://github.com/XeldarAlz/FFXIV-AutoHouseLottery/issues/new?template=translation_report.yml) and tell me what it should say instead.

## Community

Questions, ideas, or just want to hang out with other players? Come say hi on Discord.

→ [Join our Discord](https://discord.gg/hppkAvdBEE)

## More from me

If you liked this plugin, take a look at my other Dalamud work. You might find something else there for you.

→ [XeldarAlz Dalamud Plugins](https://github.com/XeldarAlz/DalamudPlugins)

## License

AGPL-3.0-or-later. See [LICENSE.md](LICENSE.md). [NOTICE](NOTICE) adds the attribution terms the AGPL allows: a fork, or any project that reuses this code, must credit the original author and must not pass itself off as the original. The license covers the code, not the name or the icon: read the [trademark and naming policy](TRADEMARK.md) before you publish a fork.

Third-party code, data and references are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). How AI is used to build this plugin is written down in [AI usage](AI-USAGE.md).
