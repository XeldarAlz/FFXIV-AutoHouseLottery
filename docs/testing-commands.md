# Testing and debug commands

None of these is needed to use the plugin. `probe` reads what the game shows and does only things that cost nothing; `dev` is the maintainer's tool for driving one game action by hand. Both write to the Console (`/ahl log`).

| Command | Action |
|---|---|
| `/autohouselottery probe` | Log every housing reading for where you stand: character, location, lottery entry, ward list, placard, open lottery dialogs, housing messages and Lifestream |
| `/autohouselottery probe scan` | Page the open ward list through every ward and record each ward it reads |
| `/autohouselottery probe lottery` | Open the Timers window once to load your lottery entry, then log it |
| `/autohouselottery probe goto <district> <ward> <plot>` | Travel with Lifestream to a plot on this world (`mist`, `lavender`, `goblet`, `shirogane`, `empyreum`), walk to the nearest placard, and once you open it, confirm from its address that it is that plot; nothing is pressed |
| `/autohouselottery probe approach <ward> <plot>` | Walk to a plot's placard in the district you are in |
| `/autohouselottery probe press <enter \| results \| cancel \| no>` | Do one thing that costs nothing: press Enter Lottery or View Lottery Results on the placard, close the entry confirmation, or press No on a result dialog. Each button is checked by its own label first, and nothing is pressed while a run or a probe task is going |
| `/autohouselottery probe stop` | Stop the probe task and the travel it started |
| `/autohouselottery dev run <district> <ward> <plot> <deposit> [fc]` | Enter one plot on your home world for exactly that deposit. The deposit is paid for real |
| `/autohouselottery dev settle <receipt or district ward plot> [claim]` | Visit the plot of one of your entries (by the first characters of its receipt id, or by its address), read its result, and collect the refund or, with `claim`, claim the house |

**Plugins that answer dialogs.** YesAlready is paused through its stop requests for as long as an entry run, a result visit or a probe task drives the game, and resumed when it ends; every press that opens a lottery dialog first checks that the pause is in effect. A fork or renamed build (any other plugin whose internal name contains YesAlready) may not honour that pause, so while one is loaded nothing that opens a lottery dialog starts. The probe's `press enter` and `press results` leave the dialog open for you, so they are refused while any of these plugins is loaded, YesAlready included. Such a plugin could press Confirm or Yes on a dialog the plugin means to check first or to close.
