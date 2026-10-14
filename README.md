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

**In development, not released.** The window, its settings, the Console and the changelog are in place, and the Find a home, My lottery and Saved pages show empty states until the features below land. There is nothing to install yet, and this README describes what the plugin is being built to do. A full README comes with the release.

## What it will do

Every housing plot in FFXIV is sold through a lottery: during the entry period each character can enter one lottery, paying the full price as a deposit, and when the results come in the winner claims the house while everyone else collects their deposit back. Auto House Lottery shows the empty plots reported by public listings on a map, enters the lottery on the plot you pick or the best match on your shortlist, then checks the result and collects your refund or claims your house.

- **Find a home**: reported empty plots for your home world on the district map and in a list, filtered by district, ward, size, price and lottery phase. Each plot shows its deposit, its reported number of entries and how long ago it was seen, and an entry count nobody reported stays unknown instead of reading as zero.
- **Shortlist**: add plots one by one from the map or the list, or every matching plot at once. The shortlist holds the plots you would accept; a run makes at most one entry per character from it, as the game allows, and the header always says so (42 plots · 1 entry this cycle).
- **Enter a plot or the best match**: Enter on a plot travels there, reads its placard and enters that plot only, for the exact deposit it showed. Enter best match ranks the shortlist by fewest reported entries, then lowest deposit, then address, and enters the first plot that still passes the live checks, stopping after one entry.
- **My lottery**: your entries with their deposit, lottery number and phase, and one action each: Check result, Claim house, Collect refund or View receipt. A win is always presented as a claim and never collected as a refund.
- **Automatic refunds**: on by default, since the game refuses a new entry while an earlier refund is waiting. When results are in and you are idle, the plugin visits your entered plots and collects the deposits of confirmed losses.
- **Saved**: named searches and fixed plot lists to start the next lottery period from.
- **Careful with your gil**: the plugin records what it is about to do before it confirms an entry, never submits again after an entry whose result is unclear, and has a dry run that goes all the way to the lottery confirmation and presses Cancel.
- **Later versions**: entries for several characters in one run, placed on the same plot or spread across the shortlist by ownership group.

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
| `/autohouselottery stop` | Stop the running lottery run before its next step; a press that may already have reached the game is verified first |
| `/autohouselottery pause` | Pause the running lottery run before its next game-changing step |
| `/autohouselottery resume` | Resume a paused run, or retry one that is waiting for you, from a fresh check of the character and the plot |
| `/autohouselottery target` | Log the targeted object's BaseId (debug helper) |
| `/autohouselottery probe` | Log every housing reading for where you stand to the Console: character, location, lottery entry, ward list, placard, open lottery dialogs, housing messages and Lifestream (debug helper) |
| `/autohouselottery probe scan` | Page the open ward selector through every ward and log each acknowledged ward |
| `/autohouselottery probe lottery` | Open the Timers window once to load your lottery entry, then log it |
| `/autohouselottery probe goto <district> <ward> <plot>` | Travel with Lifestream to a plot on this world (`mist`, `lavender`, `goblet`, `shirogane`, `empyreum`), walk to the nearest placard, and once you open it, confirm from its address that it is that plot; nothing is pressed |
| `/autohouselottery probe approach <ward> <plot>` | Walk to a plot's placard in the district you are in |
| `/autohouselottery probe press <enter \| results \| cancel \| no>` | Do one thing that costs nothing: press Enter Lottery or View Lottery Results on the placard, close the entry confirmation, or press No on a result dialog. Each button is checked by its own label first, and nothing is pressed while a run or a probe task is going |
| `/autohouselottery probe stop` | Stop the probe task and the travel it started |
| `/autohouselottery dev run <district> <ward> <plot> <deposit> [fc]` | Maintainer tool until Find a home starts runs: enter one plot on your home world for exactly that deposit. It is a dry run that stops at the lottery confirmation and closes it, unless a real-entry permission covers it |
| `/autohouselottery dev allow-real-entry <character name> <district> <ward> <plot> <max gil>` | Maintainer tool for the one supervised real entry: allow one real entry for the logged-in character on that plot, up to that deposit. It lives only in this session and ends at logout, at unload, or after the one entry |
| `/autohouselottery dev revoke-real-entry` | Withdraw the real-entry permission; every run is a dry run again |

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
