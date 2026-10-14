## What

<!-- One or two sentences on what this PR changes. -->

## Why

<!-- The motivating problem, linked issue, or user-visible behavior this fixes. -->

Closes #

## How to test

<!--
Minimum steps a reviewer can run to verify the change. Testing usually means opening Find a home, shortlisting a plot or two and pressing Enter, watching the plugin travel, open the plot's placard and enter, with the Console open. Every entry pays the deposit for real, so say which plot you entered, on a plot and with a budget you chose deliberately, and that you watched it the whole way. Make sure Lifestream, listed in /ahl deps, is installed first. For UI-only changes, describe what to click.
-->

## Checklist

- [ ] `dotnet build -c Release` passes and `dotnet test -c Release` is green
- [ ] Verified in-game with one supervised entry, refund or claim when this touches entries, refunds or claims
- [ ] New or changed user-facing text is in `L*.cs` and all nine catalogs, same keys in the same order
- [ ] If this changes user-visible behavior, README is updated
- [ ] If this touches a run, an entry, a refund or a claim, relevant `[AHL]` log lines make the sequence auditable
