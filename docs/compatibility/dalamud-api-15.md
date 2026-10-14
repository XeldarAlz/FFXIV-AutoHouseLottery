# Compatibility report: Dalamud API 15, global client

This is the compatibility record of Auto House Lottery 1.0.0.0 for the global game client with Dalamud API 15 (built against Dalamud 15.0.3.7). It says what the plugin covers, what it refuses, how each game interaction is recognized, and which values have been checked in game. The game version string is not recorded yet: the first in-game validation fills it in from the plugin's Copy diagnostics (Plugins page), which prints it, and this file is renamed after it then.

| Item | Value |
| --- | --- |
| Plugin version | 1.0.0.0 |
| Dalamud API level | 15 |
| Game version string | Not recorded yet (first in-game validation) |
| In-game validation | Not started: every row below marked "not yet checked in game" is still open |

## Regions

| Region | Data center region row | Status |
| --- | --- | --- |
| Japan | 1 | Supported |
| North America | 2 | Supported |
| Europe | 3 | Supported |
| Oceania | 4 | Supported |
| China | 5 | Refused |
| Korea | 6 (assumed; any row other than 1 to 4 is refused) | Refused |

The region of a world comes from its data center's row in the game's World and WorldDCGroupType sheets (`Core/Housing/Model/WorldRegion.cs`, `Core/Game/WorldRegionReader.cs`). A world whose region cannot be read is refused too. The housing listings, the lottery calendar and the dialog rows the plugin reads are the global client's, so on a refused world:

- Find a home asks the housing service nothing about that world, nor about a refused world a saved search, list or the shortlist points at, and says it is not supported (`findHome.unsupportedRegion`, or `runStart.unknownRegion` when the region could not be read).
- An entry run refuses to start (`StartRefusal.UnsupportedRegion`, "Housing on Chinese and Korean worlds is not supported.", or `StartRefusal.UnknownRegion` when the region could not be read).
- A result visit, a refund or a claim refuses to start (`SettlementRefusal.UnsupportedRegion` or `UnknownRegion`), and the automatic refund scheduler stays off for that character (`SchedulerBlock.UnsupportedRegion`), so nothing is retried.
- The Plugins page reports the game region as Blocked with its reason.

## Districts and divisions

Every district has 30 wards of 60 plots: plots 1 to 30 in the main division and 31 to 60 in the subdivision.

| District | Territory | Lifestream city aetheryte | Listings and map | Entry, result check, refund, claim |
| --- | --- | --- | --- | --- |
| Mist | 339 | 8 | Implemented, main and subdivision | Implemented; not yet checked in game |
| The Lavender Beds | 340 | 2 | Implemented, main and subdivision | Implemented; not yet checked in game |
| The Goblet | 341 | 9 | Implemented, main and subdivision | Implemented; not yet checked in game |
| Shirogane | 641 | 111 | Implemented, main and subdivision | Implemented; not yet checked in game |
| Empyreum | 979 | 70 | Implemented, main and subdivision | Implemented; not yet checked in game |

Apartments, rooms and first-come plots are outside this version: a plot sold first come, first served is shown with its status and never entered.

## Client languages

| Client language | Dialogs and messages | Unit tests | In game |
| --- | --- | --- | --- |
| English | Matched by sheet row | Result rows 7122, 7123, 7124 and 7128 in their real text | Not yet checked |
| German | Matched by sheet row | Same rows in German | Not yet checked |
| French | Matched by sheet row | Same rows in French | Not yet checked |
| Japanese | Matched by sheet row | Same rows in Japanese | Not yet checked |

The plugin's own windows are translated into nine languages (English, German, French, Spanish, Brazilian Portuguese, Russian, Turkish, Japanese, Chinese); that choice is independent of the client language.

## Interactions matched by sheet rows

Every housing text below is read from the client in its own language by its row and matched by its static segments, never by English text (`Core/Game/Model/HousingText.cs`). This works the same way in every client language.

| Interaction | Sheet and rows |
| --- | --- |
| Placard buttons | Addon 7045 (Enter Lottery), 7127 (View Lottery Results) |
| Entry confirmation | Addon 7046 (text), 7047 (proceed checkbox), 7048 (Confirm) |
| Result dialogs | Addon 7122 (won), 7123 (claim forfeited), 7124 (lost, full refund), 7125 (refund expired), 7128 (sole entrant won) |
| Yes and No labels | Addon 3 and 576 (Yes), 4 and 577 (No); which of each pair the result dialog uses is not yet checked in game |
| Timers lottery lines | Addon 8844 (entry period), 8845 (results period), 8847 (address), 8848 (lottery number), 8849 (entry type) |
| Entry refusals | LogMessage 3420 (caretaker items), 3497 (land already owned), 6097 (after a demolition), 6098 (Free Company membership), 6099 (one entry per period), 6100 (earlier refund or plot waits), 6101 (action failed), 6102 (left the Free Company) |

## Values not yet checked in game

These are read or pressed exactly as the community sources describe them, behind checks that refuse to act when they do not fit. Each one is a named release gate.

| Value | Where | Source |
| --- | --- | --- |
| Placard window `HousingSignBoard`, address text node 56, details text node 64 | `Core/Game/Placard/PlacardReader.cs` | AltMate, HousingHerald |
| Placard sale handler signature and the sale block layout | `Core/Game/Placard/PlacardSaleHook.cs`, `Core/Game/Model/PlacardSaleInfo.cs` | PaissaHouse |
| Placard agent field patterns (ward, plot, territory, for sale, size, owner Free Company) | `Core/Game/Placard/SignboardAgentLayout.cs` | LlamaLibrary |
| Placard button callback value (candidate 1; presses use the button's own click event) | `Core/Game/Dialogs/HousingDialogs.cs` | LlamaLibrary |
| Entry confirmation addon `SelectYesNoTextScroll` and its checkbox and Confirm button | `Core/Game/Dialogs/HousingDialogs.cs` | HousingHerald, AltMate |
| Result dialog addon `SelectYesno` | `Core/Game/Dialogs/HousingDialogs.cs` | AltMate |
| Ward list window `HousingSelectBlock` values (ward 1, name 2, buyer flags 3, ward count 4, row count 35, rows from 36, seven values per row) | `Core/Game/Model/WardLayout.cs` | HouseHunter, LlamaLibrary |
| Ward packet purchase and tenant type bytes (0x968, 0x96A) | `Core/Game/Ward/WardPortalObserver.cs` | PaissaHouse |
| Personal lottery record behind `HousingManager + 0xB0` and its layout; until a build is listed as validated, the Timers window is read instead | `Core/Game/Model/LotterySnapshot.cs` | HouseHunter |
| Timers window addons `ContentsInfo` and `ContentsInfoDetail` | `Core/Game/Lottery/LotteryReader.cs` | HouseHunter |
| Placard event object base ID 2002736, 3.5 m interaction range, 25 m search radius around Lifestream's plot entrance | `Core/Travel/PlacardApproach.cs` | ffxiv-datamining, estimate |
| Lifestream IPC gates (GoToHousingAddress, IsBusy, Abort, IsHere, IsQuickTravelAvailable, GetPlotEntrance, GetCurrentPlotInfo) on the installed version | `Core/Ipc/LifestreamIpc.cs` | Lifestream `IPC/IPCProvider.cs` |
| The Korean data center region row (6) | `Core/Housing/Model/WorldRegion.cs` | Assumed; refused either way |

## Readings and their validation

| Reading | Flag | Current state |
| --- | --- | --- |
| Direct lottery record read | `LotteryRecordAccess.ValidatedGameVersions` | empty: the Timers window is read instead |

A game version is added only after the record has been read correctly in game on that version, with its report attached here.
