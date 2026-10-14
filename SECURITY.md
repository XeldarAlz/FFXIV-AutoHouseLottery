# Security policy

## Supported versions

Only the latest release published to the custom Dalamud repo is supported. If you're on an older version, please update before reporting.

## Reporting a vulnerability

Please report security issues privately via GitHub's private vulnerability reporting:

https://github.com/XeldarAlz/FFXIV-AutoHouseLottery/security/advisories/new

Please don't open a public issue or Discussion for anything that could let someone else exploit users of the plugin before a fix is out.

What counts:

- Code execution or crashes triggerable by crafted game state, by a crafted answer from the housing listing service, or by a crafted reply from a helper plugin such as Lifestream.
- The plugin entering a lottery it shouldn't: a plot you did not pick and that is not on your shortlist, a deposit above the amount the run showed before you pressed Enter, a buyer type (Private or Free Company) you did not choose, a second entry for the same character in one lottery period, a character you did not select, a Chinese or Korean world, or anything at all during a dry run or while entering is shown as dry run only on the Plugins page. The one exception is a `/ahl dev allow-real-entry` permission you gave yourself: it allows one real entry for the named character on the named plot, up to its gil ceiling, until it is used, revoked, or the session ends.
- The plugin settling an entry the wrong way: collecting a refund on a plot you won, claiming a plot you did not choose, pressing Yes on a refund or a claim while that action is shown as dry run only (apart from the one press a `/ahl dev allow-real-refund` or `allow-real-claim` permission you gave allows), entering again after an entry whose result was unclear, or answering any game dialog other than the housing dialogs it recognizes by their rows in the game data.
- The plugin teleporting, travelling, or persisting something it shouldn't, beyond the documented housing automation, which is the feature.
- Data exfiltration: the plugin's only network calls are reads of public housing listings from the Aetherphone housing service, and the one-click helper install on the Plugins page only asks Dalamud to add that helper's repository and install it. Your characters, your gil and your entries never leave your machine. Sending them anywhere would be a bug.

What it reads and keeps, so you can tell a bug from the design:

- **Read from the game, never sent anywhere**: your character's home world, gil and Free Company, your lottery entries and their results, and, during a run, the plot's placard, the lottery windows and any dialog that opens.
- **Kept on disk in the plugin's config folder**: your settings, including the characters seen on this install by name, home world and content id (the number Dalamud uses to tell characters apart) so My lottery can name them; `shortlist.json` and `saved.json` with your shortlist, saved searches and plot lists; the housing service's answers under `housing-cache` (each world's listings for 7 days and the list of worlds for 30, deleted when the plugin loads after that); `lottery-schedules.json` with the lottery calendar; and `journal.jsonl`, a record of every entry, refund and claim the plugin made, keyed by content id and kept until each one is settled so an interrupted run is checked instead of repeated.
- **Copied only when you press Copy log or Copy diagnostics**: the Console lines with a header naming the plugin version, the Dalamud version and your zone, or the diagnostics report with the game version, your client language, the Compatibility list and the last 200 Console lines. Both replace every known character's name and content id with an alias, and your home folder (on Windows, Wine, Linux or macOS) and the plugin configuration folder with placeholders; a name, id or home path that still shows in a copy is a bug. The Console never holds the address of an estate you own or the text of a window that is not a housing dialog.

What doesn't:

- The fact that Auto House Lottery enters the housing lottery, collects refunds and travels for you. That's against Square Enix's Terms of Service and is the whole feature; turning it on is the user's choice.

I'll aim to acknowledge reports within a few days and to ship a fix or workaround as soon as I've verified the issue.
