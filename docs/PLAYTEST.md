# Test checklist

## Session start

- [ ] `fm status` gives the date, club, next match and what is waiting.
- [ ] `fm doctor --install` checks all seven install steps.
- [ ] `fm lang english` switches the CLI while the game stays Turkish; `fm lang auto` follows the game again.
- [ ] `fm inbox --tasks` shows offers and registration messages on the Tasks tab.
- [ ] `next` and `press` do not hang when the game is not frontmost; the command brings it forward.

## Daily flow

- [ ] `next` stops only at a real blocker and says why and how to clear it.
- [ ] Press conference, injury, offer and discipline messages open and close with `reply`.
- [ ] `talk` shows the options; a post-match press conference can be passed to the assistant.
- [ ] After `save --yes`, `fm saves` shows the new time and file.

## Match day

- [ ] `tactic pick` fills the eleven; `match` says so clearly when the squad is empty.
- [ ] At the start of a season, squad numbers can be sent with `numbers --auto`.
- [ ] `match --instant` prints **this** match's score, not the previous report.
- [ ] `match --play` gets through narration, goals, half time and full time.
- [ ] At a terminal, `match --play` shows the live score/event panel, the shape pitch and the radar;
  `a` hides and shows the radar; a redirected run stays line-by-line.
- [ ] `fm settings panel view key pitch off events 5 colour off radar off` changes the next match; `reset` restores.
- [ ] `next` closes the result screen afterwards and the bridge still answers.

## Known break points

- **Event queue:** if a press/dialog source never arrives, later `press` calls wait too; `/queue`
  must be zero.
- **One command at a time:** two UI commands run in parallel can fight over the bound page and hang
  the bridge; run them in sequence.
- **Focus:** FM handles some Continue/UI events only when frontmost; `next`/`press` activate it.
- **Tasks:** an offer message may not be on the default inbox tab; check `inbox --tasks`.
- **Stale report:** after an instant result, do not read the report before the last fixture changes.
- **Squad numbers:** the `SquadNumbers` screen must not block `match` at the start of a season.
- **Injury:** if the treatment buttons are not in `reply` options, use `buttons` and a full `press`.
- **Plugin version:** a running game keeps the plugin it started with. If `/press` answers
  "unknown route" (404), save and restart: `save --yes`, `stop --no-save`, `start`.

## Open ends

- The shadow app and BepInEx are in the package; a second macOS user without Steam cannot launch.
- Some offer rows can stay unanswered in "Contract"; check the Tasks filter and `offers` together.
