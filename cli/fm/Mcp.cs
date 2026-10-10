using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FmCli;

/// <summary>
/// The commands as tools for a model, over the Model Context Protocol's stdio transport:
/// one JSON-RPC message per line in, one per line out.
///
/// A tool is a command and nothing more. Its arguments are turned into the words a user would
/// have typed, the command runs as it would in a terminal, and what it printed is the result.
/// So anything that works in the CLI works here, and there is one place where behaviour lives.
/// </summary>
internal static class Mcp
{
	private sealed record Tool(string Name, string Description, string Schema, Func<JsonElement, List<string>> Words);

	private const string None = """{"type":"object","properties":{}}""";

	private static readonly Tool[] Tools =
	[
		new("fm_status", "The in-game date and time, and what the game is waiting for (free to continue, a message to answer, a press conference, match day, or the score if a match is on). Call this first.",
			None, _ => ["status"]),
		new("fm_next", "Press Continue and wait for the game to settle; reports the new date and the messages that arrived. Stops when something needs doing first and says what, including a question left open after a match (answer it with fm_talk, or pass `skip`). `times` repeats it.",
			"""{"type":"object","properties":{"times":{"type":"integer","minimum":1,"maximum":30,"description":"How many times to continue (default 1); stops early when blocked."},"skip":{"type":"boolean","description":"Go on past an open post-match question unanswered."}}}""",
			a => ["next", .. Opt(a, "times"), .. Flag(a, "skip", "--skip")]),
		new("fm_inbox", "List the inbox with message numbers; flags which messages are unread, need an answer (these block Continue), or can optionally be answered.",
			None, _ => ["inbox"]),
		new("fm_reply", "Open a message: its full text and the answers it offers. Without `answer` it only shows them, so this is also how a message is read. Some answers open a tour (use fm_tour) or a conversation (use fm_talk) instead of closing the message.",
			"""{"type":"object","required":["message"],"properties":{"message":{"type":"string","description":"Message number from fm_inbox, or a piece of its subject."},"answer":{"type":"string","description":"Answer number from the listing, or a piece of its text."},"dry":{"type":"boolean"}}}""",
			a => ["reply", Str(a, "message"), .. Opt(a, "answer"), .. Flag(a, "dry", "--dry")]),
		new("fm_talk", "The open conversation (squad meeting, press conference, team talk): what was said, how the listeners react, and the numbered lines that can be said. With `say`, says one (number or a piece of its text) and returns the reply and the next options. say=\"leave\" walks out.",
			"""{"type":"object","properties":{"say":{"type":"string","description":"Option number, a piece of its text, or \"leave\"."}}}""",
			a => ["talk", .. Opt(a, "say")]),
		new("fm_tour", "Walk the open introduction tour to its end, printing what the guide says at each step; `skip` skips it if the tour allows.",
			"""{"type":"object","properties":{"skip":{"type":"boolean"}}}""",
			a => ["tour", .. Flag(a, "skip", "--skip")]),
		new("fm_dialog", "Show the open confirmation dialog, or answer it with one of its buttons.",
			"""{"type":"object","properties":{"answer":{"type":"string","description":"Button number or text, e.g. \"Yes\"."}}}""",
			a => ["dialog", .. Opt(a, "answer")]),
		new("fm_squad", "The first-team squad. Default view: position, age, status, wage, value. view=fitness: condition and match sharpness; view=morale: morale, hierarchy, social group; view=stats: this season's games, minutes, goals, assists, average rating and cards. Any other `view` is one of the squad table's own views by name (Contract, Injuries, Playing Time, Transfer, Development…; view=list prints them) and prints whatever columns it carries.",
			"""{"type":"object","properties":{"view":{"type":"string"}}}""",
			a => ["squad", .. Str(a, "view") switch { "" or "general" => Array.Empty<string>(), "fitness" => ["--fitness"], "morale" => ["--morale"], "stats" => ["--stats"], "list" => ["--views"], var other => ["--view", other] }]),
		new("fm_search", "The game's own search: players, staff, clubs and competitions matching a text. It matches loosely.",
			"""{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""",
			a => ["search", Str(a, "text")]),
		new("fm_player", "A player's page: age, nation, positions, value, wage, contract end, condition, personality, traits and all attributes, his 1-20 rating in each position (20 natural, 15+ accomplished, 10+ competent) and his injury history. A player of your own squad is opened from the squad table; anyone else is found with the game's search, so give enough of the name to single him out. For a player not scouted, some fields read \"Scouting Required\" and wages are ranges. `career` gives his career season by season instead: club, how he came (fee, loan, free), division, appearances, goals, rating, and totals.",
			"""{"type":"object","required":["name"],"properties":{"name":{"type":"string","description":"The player's name or a unique piece of it."},"career":{"type":"boolean"}}}""",
			a => ["player", Str(a, "name"), .. Flag(a, "career", "--career")]),
		new("fm_offer", "Bid for a player: opens his offer screen (or carries on one already open, including the other club's counter-offer after 'View Offer' or 'Negotiate'), sets the fee and any other terms, prints the offer as it stands and sends it. It cannot be taken back, so it only sends with send=true; without it the offer is prepared and left on screen (a dry run). Takes about five seconds. The other club answers through the inbox, by the deadline if one is set. loan=true makes a loan offer instead.",
			"""{"type":"object","required":["name"],"properties":{"name":{"type":"string","description":"The player's name or a unique piece of it."},"fee":{"type":"string","description":"The fee, e.g. 7.5M or 750K. Left out, the fee on screen stays."},"terms":{"type":"array","items":{"type":"string"},"description":"Other terms as 'Label=value[,value]', with the game's labels and choices: 'Percentage Of Next Sale=20%', 'After League Appearances=20 games,250K', 'Transfer Date=End Of Season', 'Negotiable=Non-Negotiable', 'Deadline=Tomorrow'. A fee or clause not on the offer yet is added. A bonus for a trophy or an award, or a player to exchange, sits in a submenu: give the path with '>', e.g. 'Bonus After Competition Achievement>Turkish Cup>Bonus upon reaching the Semi Final of the Turkish Cup=100K', or 'Players To Exchange' then 'Other Player From Squad>Senior Squad>Mert Müldür=6 Month Loan'. A dry run prints the terms and their choices."},"loan":{"type":"boolean"},"send":{"type":"boolean","description":"true sends the offer; otherwise it is only prepared."}}}""",
			a => ["offer", Str(a, "name"), .. Opt(a, "fee"), .. Each(a, "terms", "--term"), .. Flag(a, "loan", "--loan"), .. (a.TryGetProperty("send", out var send) && send.ValueKind == JsonValueKind.True ? new[] { "--yes" } : ["--dry"])]),
		new("fm_contract", "Contract talks with a player, drawn as a card of terms. They open after a club accepts our bid (reply to '<player> offer accepted' with 'Enter Contract Negotiations' first) or, for one of our own players, with `name`: a new contract (renewal). Two stages: promises first (squad status, start date, any promise the agent asks for), then terms (wage, length, agent fee, bonuses, clauses). `terms` sets any term by name, 'Label=value' (e.g. 'Wage=20K', 'Contract Length=3 years', 'Yearly Wage Rise=10%', 'Appearance Fee=remove', 'Will appoint player as captain' to add a promise, 'Entire offer=Non-Negotiable' or a lock name on a term); a term the stage does not have is tried on the next. `next` takes the promises as they stand and moves on to the terms. `sign` puts the terms to the agent: his own are finalised, changed ones he answers with his own — call again with sign once the card shows what he wants. A renewal is decided by the player a few days later (inbox). Signing cannot be undone.",
			"""{"type":"object","properties":{"name":{"type":"string","description":"One of our players, to open talks on a new contract."},"wage":{"type":"string","description":"Weekly wage, e.g. 9000 or 12.5K."},"terms":{"type":"array","items":{"type":"string"},"description":"'Label=value' per term, in English or as the card shows it."},"next":{"type":"boolean"},"sign":{"type":"boolean"},"cancel":{"type":"boolean","description":"Call the talks off."}}}""",
			a => ["contract", .. Flag(a, "cancel", "--cancel"), .. (Str(a, "name") is { Length: > 0 } n ? new[] { n } : []), .. (Str(a, "wage") is { Length: > 0 } w ? new[] { "--wage", w } : []), .. Each(a, "terms", "--term"), .. Flag(a, "next", "--next"), .. (a.TryGetProperty("sign", out var sign) && sign.ValueKind == JsonValueKind.True ? new[] { "--sign", "--yes" } : [])]),
		new("fm_register", "The squad list a competition asks for (a message 'squad to be decided' blocks time until it is sent). Without arguments it shows every player — on the list or not, homegrown flags, who cannot be changed (loaned out) — and the competition's rules with whether the list meets each. `add` / `remove` put players on or take them off; `auto` lets the assistant pick first; `send` submits it, which only works once every rule is met. A homegrown quota the club cannot fill is met by leaving that many places empty: take players without a homegrown flag off. Takes 20-60 seconds.",
			"""{"type":"object","properties":{"add":{"type":"array","items":{"type":"string"},"description":"Names (or unique pieces) to put on the list."},"remove":{"type":"array","items":{"type":"string"},"description":"Names (or unique pieces) to take off."},"auto":{"type":"boolean"},"send":{"type":"boolean"}}}""",
			a => ["register", .. Flag(a, "auto", "--auto"), .. Each(a, "add", "--in"), .. Each(a, "remove", "--out"), .. Flag(a, "send", "--send")]),
		new("fm_numbers", "Submit the squad numbers the competition asks for: the assistant assigns them.",
			None, _ => ["numbers", "--auto"]),
		new("fm_players", "The player database (every player the game knows), as far as the page has rows on screen — about twenty. `filter` applies one of the game's quick filters (Transfer Listed, Loan listed, Contract Expiring, Unhappy, Goalkeepers…) and it stays applied; `view` chooses which columns the rows carry (Overview, General Info, Contract, Transfer…). list=filters or list=views prints the choices.",
			"""{"type":"object","properties":{"filter":{"type":"string"},"view":{"type":"string"},"list":{"type":"string","enum":["filters","views"]},"clear":{"type":"boolean","description":"Remove the filter in force first."}}}""",
			a => ["players", .. Str(a, "list") switch { "filters" => new[] { "--filters" }, "views" => ["--views"], _ => [] }, .. Flag(a, "clear", "--clear"),
				.. Str(a, "filter") is { Length: > 0 } filter ? new[] { "--filter", filter } : [],
				.. Str(a, "view") is { Length: > 0 } view ? new[] { "--view", view } : []]),
		new("fm_table", "The league table.", None, _ => ["table"]),
		new("fm_fixtures", "The team's last played and upcoming fixtures: date, time, venue, opponent, competition, result. `all` gives the whole season instead, read month by month from the calendar (about 15 seconds).",
			"""{"type":"object","properties":{"all":{"type":"boolean"}}}""",
			a => ["fixtures", .. Flag(a, "all", "--all")]),
		new("fm_calendar", "One month of the club's calendar, day by day: matches (opponent, league place, venue, competition, time or our score) and notes such as the transfer window opening or closing, cup draws and internationals. `month` as \"september\", \"eylül\", \"9\" or \"2025-09\"; this month when left out. `youth` adds the under-19s matches.",
			"""{"type":"object","properties":{"month":{"type":"string"},"youth":{"type":"boolean"}}}""",
			a => ["calendar", .. (Str(a, "month") is { Length: > 0 } m ? new[] { m } : []), .. Flag(a, "youth", "--u19")]),
		new("fm_report", "The team's last played match: score, scorers, player of the match, and possession, shots, shots on target, expected goals, corners and fouls for both sides.", None, _ => ["report"]),
		new("fm_tactic", "The team's tactic: style, mentality, how familiar the team is with it, and for each of the eleven slots the position and role with and without the ball, the player picked and how familiar he is with the tactic.",
			None, _ => ["tactic"]),
		new("fm_tactic_set", "Show or change the tactic's mentality or style. Without `value` it lists the choices and marks the current one.",
			"""{"type":"object","required":["setting"],"properties":{"setting":{"type":"string","enum":["mentality","style"]},"value":{"type":"string","description":"Choice number, name, or a unique piece of the name."},"dry":{"type":"boolean"}}}""",
			a => ["tactic", Str(a, "setting"), .. Opt(a, "value"), .. Flag(a, "dry", "--dry")]),
		new("fm_tactic_role", "Show or change the role of one slot of the tactic. `position` is the slot as fm_tactic prints it (GK, DR, DCR, DML, ST…); during a match it is a player's name instead. Without `role` it lists the roles on offer and marks the current one. `without_ball` picks the out-of-possession role instead.",
			"""{"type":"object","required":["position"],"properties":{"position":{"type":"string"},"role":{"type":"string","description":"Role number, name, or a unique piece of the name."},"without_ball":{"type":"boolean"},"dry":{"type":"boolean"}}}""",
			a => ["tactic", "role", Str(a, "position"), .. Opt(a, "role"), .. Flag(a, "without_ball", "--oop"), .. Flag(a, "dry", "--dry")]),
		new("fm_tactic_player", "Put a substitute into a slot of the starting eleven; whoever was there goes to the bench. `position` is the slot as fm_tactic prints it; `player` is a name from fm_tactic's bench line.",
			"""{"type":"object","required":["position","player"],"properties":{"position":{"type":"string"},"player":{"type":"string"},"dry":{"type":"boolean"}}}""",
			a => ["tactic", "player", Str(a, "position"), Str(a, "player"), .. Flag(a, "dry", "--dry")]),
		new("fm_tactic_pick", "Let the game pick the starting eleven (Quick Pick).", None, _ => ["tactic", "pick"]),
		new("fm_tactic_instructions", "The team instructions in possession, phase by phase (Overview, Final Third, Progression, Buildup), or with `name` and `value` changes one. A name in two phases is given as 'Final Third/Dribbling'; leaving `value` out lists its choices. Final Third and Progression Dribbling are one setting in the game.",
			"""{"type":"object","properties":{"name":{"type":"string"},"value":{"type":"string"}}}""",
			a => ["tactic", "instructions", .. (Str(a, "name") is { Length: > 0 } n ? new[] { n } : []), .. (Str(a, "value") is { Length: > 0 } v ? new[] { v } : [])]),
		new("fm_training", "Individual training: per player the position or role trained, a role out of possession and an extra focus. Without `player` lists everyone. With `player`, `focus` (e.g. 'Endurance', 'None'), `position` and `role` change his; position and role are submenus given as a path, e.g. 'Midfielder (Centre)>Central Midfielder' (a submenu name alone lists its choices). report=true with `player` asks a coach (`coach`, a part of his name; without it the coaches are listed) for a training report on him and prints it. Traits (player preferred moves): traits=true lists his and the ones he can learn; `trait` starts learning one, `trait_remove` starts unlearning one, trait_cancel=true stops the current one, trait_suggest=true asks the assistant. The assistant's misgivings come back as a dialog; yes=true goes ahead anyway.",
			"""{"type":"object","properties":{"player":{"type":"string"},"focus":{"type":"string"},"position":{"type":"string"},"role":{"type":"string"},"report":{"type":"boolean"},"coach":{"type":"string"},"traits":{"type":"boolean"},"trait":{"type":"string"},"trait_remove":{"type":"string"},"trait_cancel":{"type":"boolean"},"trait_suggest":{"type":"boolean"},"yes":{"type":"boolean"}}}""",
			a => ["training", .. (Str(a, "player") is { Length: > 0 } p ? new[] { p } : []), .. Opt2(a, "focus", "--focus"), .. Opt2(a, "position", "--position"), .. Opt2(a, "role", "--role"), .. Flag(a, "report", "--report"), .. Opt2(a, "coach", "--coach"),
				.. Flag(a, "traits", "--trait"), .. Opt2(a, "trait", "--trait"), .. Opt2(a, "trait_remove", "--trait-remove"), .. Flag(a, "trait_cancel", "--trait-cancel"), .. Flag(a, "trait_suggest", "--trait-suggest"), .. Flag(a, "yes", "--yes")]),
		new("fm_intensity", "Training intensity per player. Without arguments lists every player's. `player` with `level` sets one: automatic, half, normal, double, 'rest 3' (no pitch or gym work for 3 days; 7 or 14 for a week or two) or 'holiday 1' (weeks). auto=true leaves all of them to the head of fitness.",
			"""{"type":"object","properties":{"player":{"type":"string"},"level":{"type":"string"},"auto":{"type":"boolean"}}}""",
			a => ["intensity", .. Flag(a, "auto", "--auto"), .. (Str(a, "player") is { Length: > 0 } p ? new[] { p } : []), .. (Str(a, "level") is { Length: > 0 } l ? new[] { l } : [])]),
		new("fm_opposition", "Opposition instructions for the next match: per opposing player tight marking, closing down (pressing), tackling and which foot to show him onto. advice=true applies the assistant's advice, clear=true clears them all; `player` with `set` (e.g. ['Pres=Always', 'Sert giriş=Hard'] or Marking/Pressing/Tackling/Foot=…) sets one player's.",
			"""{"type":"object","properties":{"advice":{"type":"boolean"},"clear":{"type":"boolean"},"player":{"type":"string"},"set":{"type":"array","items":{"type":"string"}}}}""",
			a => ["opposition", .. Flag(a, "advice", "--advice"), .. Flag(a, "clear", "--clear"), .. (Str(a, "player") is { Length: > 0 } p ? new[] { p } : []), .. (a.TryGetProperty("set", out var set) && set.ValueKind == JsonValueKind.Array ? set.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : [])]),
		new("fm_release", "Let one of our players go: free=true releases him on a free (the game names the compensation and asks; yes=true agrees), mutual=true offers a mutual termination (the game does not ask, so yes=true is required). Without either it lists the two ways.",
			"""{"type":"object","properties":{"player":{"type":"string"},"free":{"type":"boolean"},"mutual":{"type":"boolean"},"yes":{"type":"boolean"}},"required":["player"]}""",
			a => ["release", Str(a, "player") ?? "", .. Flag(a, "free", "--free"), .. Flag(a, "mutual", "--mutual"), .. Flag(a, "yes", "--yes")]),
		new("fm_prep", "The match preparation focus in training: the tactical focus in and out of possession and the two set-piece focuses, each with what it works on.",
			"""{"type":"object","properties":{}}""",
			_ => ["prep"]),
		new("fm_schedule", "The training schedule for this week and the next: each day's three sessions (Recovery, Match Tactics, Rest, Travel…) and the match days (H/A/N).",
			"""{"type":"object","properties":{}}""",
			_ => ["schedule"]),
		new("fm_happiness", "How happy each first-team player is (Very Unhappy … Very Happy), least happy first, with his status flags (Wnt: wanted by another club, Unr: unregistered, FtT: future transfer, Rst: resting…).",
			"""{"type":"object","properties":{}}""",
			_ => ["happiness"]),
		new("fm_depth", "Squad depth: for each position (GK, DL/DC/DR, WBL/DM/WBR, ML/MC/MR, AML/AMC/AMR, ST) the first-team players who can play it, from their position strings; loanees and under-19s left out. Then the age profile.",
			"""{"type":"object","properties":{}}""",
			_ => ["depth"]),
		new("fm_settings", "The game's preferences: match highlights (how much of a match is shown) and match speed during / between highlights, auto save on/off, interval and type, language, currency and display units. With `name` (e.g. \"vurgu hızı\", \"kayıt sıklığı\") and `value` (e.g. \"2x\", \"Day\") it changes one and confirms it; with only `name` it lists that setting's choices.",
			"""{"type":"object","properties":{"name":{"type":"string"},"value":{"type":"string"}}}""",
			a => ["settings", .. (Str(a, "name") is { Length: > 0 } n ? new[] { n } : []), .. (Str(a, "value") is { Length: > 0 } v ? new[] { v } : [])]),
		new("fm_dugout", "During a match: the assistant's advice in the Dugout panel (minute and text: a team or opposition instruction, a word with a player, a substitution, a remark). With `number` and `action` (\"uygula\" to do it, \"kaldır\" to dismiss) acts on one.",
			"""{"type":"object","properties":{"number":{"type":"integer"},"action":{"type":"string","enum":["uygula","kaldır"]}}}""",
			a => ["dugout", .. (a.TryGetProperty("number", out var n) && n.ValueKind == JsonValueKind.Number ? new[] { n.GetInt32().ToString() } : []), .. (Str(a, "action") is { Length: > 0 } act ? new[] { act } : [])]),
		new("fm_budget", "The club's transfer, wage and scouting budgets. With `adjust`, lists the board's transfer↔wage slider: every step with the transfer and weekly wage budget it gives; with `adjust` and `step` it applies that step.",
			"""{"type":"object","properties":{"adjust":{"type":"boolean"},"step":{"type":"integer"}}}""",
			a => ["budget", .. (a.TryGetProperty("adjust", out var adj) && adj.ValueKind == JsonValueKind.True ? new[] { "ayarla" } : []), .. (a.TryGetProperty("step", out var st) && st.ValueKind == JsonValueKind.Number ? new[] { st.GetInt32().ToString() } : [])]),
		new("fm_league", "A competition's leaders: the teams with most goals, possession, yellow cards and clean sheets, and the players with the best average rating, most goals, assists and clean sheets. `competition` is one of the club's (\"Super League\", \"UEFA Europa League\", \"Turkish Cup\"…); the league when left out.",
			"""{"type":"object","properties":{"competition":{"type":"string"}}}""",
			a => ["league", .. (Str(a, "competition") is { Length: > 0 } c ? new[] { c } : [])]),
		new("fm_health", "The squad's health: who is injured (what and for how long), who is tired (condition under 85%), who lacks match sharpness (under 60%), and who carries the highest injury risk by the game's own score.",
			"""{"type":"object","properties":{}}""",
			_ => ["health"]),
		new("fm_club", "The club at a glance: the board's grade and mood, the club vision's objectives with their importance and status, supporters' mood, the bank balance, last month's income and expenditure, the wage bill against its budget, reputation, stadium and facilities, and each staff team's filled places and vacant roles.",
			"""{"type":"object","properties":{}}""",
			_ => ["club"]),
		new("fm_staff", "The club's staff, one by one: name, job, age, nation, weekly wage and contract end, senior squad then U19. Then each team (coaching, recruitment, medical) with the roles still short of the board's minimum and the board's word on its size.",
			"""{"type":"object","properties":{}}""",
			_ => ["staff"]),
		new("fm_finances", "The club's finances: bank balance and its month-by-month history, this month's income and expenditure, remaining and original debt, financial fair play status, the weekly wage bill against its budget and split by player status (Star Player, Regular Starter…), each sponsorship deal (type, value per season, end date), and the board's balance and turnover projections. With `breakdown` (\"gelir\" income or \"gider\" expenditure) it lists that side item by item instead, for `period` \"ay\" (this month, default), \"geçen ay\", \"sezon\" or \"geçen sezon\".",
			"""{"type":"object","properties":{"breakdown":{"type":"string","enum":["gelir","gider"]},"period":{"type":"string","enum":["ay","geçen ay","sezon","geçen sezon"]}}}""",
			a => ["finances", .. (Str(a, "breakdown") is { Length: > 0 } b ? new[] { b } : []), .. (Str(a, "breakdown") is { Length: > 0 } && Str(a, "period") is { Length: > 0 } p ? new[] { p } : [])]),
		new("fm_supporters", "The supporters: their overall mood, influence on the board and reaction after recent matches, average attendance, season ticket holders and waiting list, social media followers, rivals, each expectation (text, importance, type, reaction), the fan base's make-up (Core, Family, Fair Weather…) and the spokesman's description.",
			"""{"type":"object","properties":{}}""",
			_ => ["supporters"]),
		new("fm_board", "What the board and the supporters think of the manager: a sentence each on the overall verdict, recent results, backing from the squad, tactics, transfers, the club's vision and culture and the competitions, then what pleased them and what worries them.",
			"""{"type":"object","properties":{}}""",
			_ => ["board"]),
		new("fm_facilities", "The club's facilities: the stadium (name, grade, capacity, year built, owner, under-soil heating, roof, surface, pitch size and condition), the training ground's grade, the youth facilities, academy coaching and youth recruitment, and the corporate facilities.",
			"""{"type":"object","properties":{}}""",
			_ => ["facilities"]),
		new("fm_contracts", "When each squad player's contract ends, soonest first, with age, position and time left.",
			"""{"type":"object","properties":{}}""",
			_ => ["contracts"]),
		new("fm_captains", "The captain and vice-captain; `captain` or `vice` with a player's name changes one (the game's confirmation is answered).",
			"""{"type":"object","properties":{"captain":{"type":"string"},"vice":{"type":"string"}}}""",
			a => ["captains", .. Opt2(a, "captain", "--captain"), .. Opt2(a, "vice", "--vice")]),
		new("fm_opponent", "The next opponent's report summary: their form, most used formation, top scorer and assister, best-rated player and team news (injuries, unregistered players).",
			"""{"type":"object","properties":{}}""",
			_ => ["opponent"]),
		new("fm_shortlist", "The players on the club's shortlist (anyone an offer was made for is put there too): age, value, scouting grade, status.",
			"""{"type":"object","properties":{}}""",
			_ => ["shortlist"]),
		new("fm_shout", "A touchline shout during a match (Encourage, Calm Down, Focus, Fire Up, No Pressure, Demand More, Praise, Berate). Without `shout` lists them. fm_tactic_set with setting=mentality also works during a match.",
			"""{"type":"object","properties":{"shout":{"type":"string"}}}""",
			a => ["shout", .. (Str(a, "shout") is { Length: > 0 } x ? new[] { x } : [])]),
		new("fm_tactic_pi", "One position's player instructions in possession (Shooting, Forward Runs, Dribbling Frequency, Passing Risk…). `position` is the slot as fm_tactic shows it (DR, AML, ST…); with `name` and `value` one is changed, with `name` alone its choices are listed.",
			"""{"type":"object","required":["position"],"properties":{"position":{"type":"string"},"name":{"type":"string"},"value":{"type":"string"}}}""",
			a => ["tactic", "pi", Str(a, "position"), .. (Str(a, "name") is { Length: > 0 } n ? new[] { n } : []), .. (Str(a, "value") is { Length: > 0 } v ? new[] { v } : [])]),
		new("fm_tactic_slot", "The team's tactic slots (up to three) and which is active; `slot` makes another one active.",
			"""{"type":"object","properties":{"slot":{"type":"string","description":"Slot number, 1-3."}}}""",
			a => ["tactic", "slot", .. (Str(a, "slot") is { Length: > 0 } n ? new[] { n } : [])]),
		new("fm_tactic_copy", "Copy a tactic into a new slot (up to three); the copy, 'Copy of: …', becomes the active one. Without `tactic` the only one is copied, or the choices are listed.",
			"""{"type":"object","properties":{"tactic":{"type":"string"}}}""",
			a => ["tactic", "copy", .. (Str(a, "tactic") is { Length: > 0 } t ? new[] { t } : [])]),
		new("fm_tactic_clear", "Clear a tactic slot (it is made active first). Without yes=true it only says what would be deleted. The last tactic is never cleared.",
			"""{"type":"object","properties":{"slot":{"type":"string"},"yes":{"type":"boolean"}}}""",
			a => ["tactic", "clear", .. (Str(a, "slot") is { Length: > 0 } n ? new[] { n } : []), .. Flag(a, "yes", "--yes")]),
		new("fm_tactic_new", "Create a tactic through the game's wizard: a style, then a formation in possession, then one out of possession. This is also how the formation is changed: with a tactic in place it goes into a new slot (up to three) and becomes the active one; the old one stays in its slot (fm_tactic_slot). Leave a step out to get its choices listed; the wizard stays where it is.",
			"""{"type":"object","properties":{"style":{"type":"string"},"in_possession":{"type":"string"},"out_of_possession":{"type":"string"}}}""",
			a => ["tactic", "new", .. Opt(a, "style"), .. Opt(a, "in_possession"), .. Opt(a, "out_of_possession")]),
		new("fm_setpieces_new", "Set up set piece routines through the game's six-question wizard (only while none are set up). `answers` gives an option number per question in order; a question left out keeps the game's own choice.",
			"""{"type":"object","properties":{"answers":{"type":"array","items":{"type":"integer","minimum":1}}}}""",
			a => ["setpieces", "new", .. a.TryGetProperty("answers", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(n => n.ToString()) : []]),
		new("fm_match", "Play match day once fm_status says it is match day. How is the manager's choice, so ask if you do not know it. mode=instant leaves the match to the staff and returns the result in seconds. mode=play plays it out with the commentary and stops at each team talk with the lines on offer: say one with fm_talk, then call fm_match again to go on. mode=play_no_talk plays it out passing the talks by. Playing takes about six minutes. If a player is injured the match stops and it returns, naming him from the assistant's note: make the change with fm_sub (which also restarts play), or call fm_match again to go on without one.",
			"""{"type":"object","properties":{"mode":{"type":"string","enum":["instant","play","play_no_talk"]}},"required":["mode"]}""",
			a => ["match", .. Str(a, "mode") switch { "instant" => new[] { "--instant" }, "play_no_talk" => ["--play", "--no-talk"], _ => ["--play"] }]),
		new("fm_sub", "During a match: without arguments, who is on the pitch (mood, condition) and who is on the bench; with both, take `off` off for `on`. At an injury stop it also confirms the change, which restarts play.",
			"""{"type":"object","properties":{"off":{"type":"string"},"on":{"type":"string"},"dry":{"type":"boolean"}}}""",
			a => ["sub", .. Opt(a, "off"), .. Opt(a, "on"), .. Flag(a, "dry", "--dry")]),
		new("fm_save", "Save the game over its current file. This cannot be undone; `confirm` must be true.",
			"""{"type":"object","required":["confirm"],"properties":{"confirm":{"type":"boolean"}}}""",
			a => ["save", .. Flag(a, "confirm", "--yes")]),
		new("fm_pages", "The game's pages by tab, for fm_open. `filter` narrows the list.",
			"""{"type":"object","properties":{"filter":{"type":"string"}}}""",
			a => ["pages", .. Opt(a, "filter")]),
		new("fm_open", "Open one of the game's pages by name (see fm_pages); `tab` tells apart pages that share a name.",
			"""{"type":"object","required":["page"],"properties":{"page":{"type":"string"},"tab":{"type":"string"}}}""",
			a => ["open", Str(a, "page"), .. Str(a, "tab") is { Length: > 0 } tab ? new[] { "--tab", tab } : []]),
		new("fm_buttons", "The labelled controls on the current screen and the event each would send. For exploring a screen no dedicated tool covers.",
			"""{"type":"object","properties":{"filter":{"type":"string"}}}""",
			a => ["buttons", .. Opt(a, "filter")]),
		new("fm_press", "Press a control on the current screen by its label (see fm_buttons); `name` tells apart controls that share a label.",
			"""{"type":"object","required":["label"],"properties":{"label":{"type":"string"},"name":{"type":"string"},"dry":{"type":"boolean"}}}""",
			a => ["press", Str(a, "label"), .. Str(a, "name") is { Length: > 0 } name ? new[] { "--name", name } : [], .. Flag(a, "dry", "--dry")]),
		new("fm_dropdowns", "The dropdowns and radio groups on the current screen with their choices.",
			"""{"type":"object","properties":{"filter":{"type":"string"}}}""",
			a => ["dropdowns", .. Opt(a, "filter")]),
		new("fm_select", "Choose from a dropdown or radio group on the current screen (see fm_dropdowns).",
			"""{"type":"object","required":["list","choice"],"properties":{"list":{"type":"string"},"choice":{"type":"string"},"dry":{"type":"boolean"}}}""",
			a => ["select", Str(a, "list"), Str(a, "choice"), .. Flag(a, "dry", "--dry")]),
	];

	public static async Task<int> Serve(Func<string[], Task<int>> run)
	{
		// Standard output is the protocol's channel, so nothing but messages may reach it.
		var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
		var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));

		while (await input.ReadLineAsync() is { } line)
		{
			if (string.IsNullOrWhiteSpace(line)) continue;

			JsonElement message;
			try { message = JsonDocument.Parse(line).RootElement; }
			catch (JsonException)
			{
				await output.WriteLineAsync("""{"jsonrpc":"2.0","id":null,"error":{"code":-32700,"message":"Parse error"}}""");
				continue;
			}

			// A message without an id is a notification and is never answered.
			if (!message.TryGetProperty("id", out var id)) continue;

			var method = message.TryGetProperty("method", out var m) ? m.GetString() : null;
			var parameters = message.TryGetProperty("params", out var p) ? p : default;

			JsonNode result = null;
			JsonNode error = null;

			switch (method)
			{
				case "initialize":
					var version = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("protocolVersion", out var asked)
						? asked.GetString()
						: "2024-11-05";
					result = new JsonObject
					{
						["protocolVersion"] = version,
						["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
						["serverInfo"] = new JsonObject { ["name"] = "fm-cli", ["version"] = "0.1.0" },
						["instructions"] = "Plays Football Manager 26 through the running game. Start with fm_status; it says what the game is waiting for and which tool deals with it. Command output is in Turkish.",
					};
					break;

				case "ping":
					result = new JsonObject();
					break;

				case "tools/list":
					var listed = new JsonArray();
					foreach (var tool in Tools)
						listed.Add(new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["inputSchema"] = JsonNode.Parse(tool.Schema) });
					result = new JsonObject { ["tools"] = listed };
					break;

				case "tools/call":
					result = await Call(parameters, run);
					if (result == null) error = new JsonObject { ["code"] = -32602, ["message"] = "Unknown tool" };
					break;

				default:
					error = new JsonObject { ["code"] = -32601, ["message"] = $"Method not found: {method}" };
					break;
			}

			var reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = JsonNode.Parse(id.GetRawText()) };
			if (error != null) reply["error"] = error;
			else reply["result"] = result;

			await output.WriteLineAsync(reply.ToJsonString());
		}

		return 0;
	}

	/// <summary>Runs the command a tool stands for and hands back what it printed.</summary>
	private static async Task<JsonNode> Call(JsonElement parameters, Func<string[], Task<int>> run)
	{
		if (parameters.ValueKind != JsonValueKind.Object) return null;

		var name = parameters.TryGetProperty("name", out var n) ? n.GetString() : null;
		var tool = Tools.FirstOrDefault(t => t.Name == name);
		if (tool == null) return null;

		var arguments = parameters.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object
			? a
			: JsonDocument.Parse("{}").RootElement;

		var printed = new StringWriter();
		var (stdout, stderr) = (Console.Out, Console.Error);
		int code;
		try
		{
			Console.SetOut(printed);
			Console.SetError(printed);
			code = await run(tool.Words(arguments).ToArray());
		}
		catch (Exception ex)
		{
			printed.WriteLine(ex.Message);
			code = 1;
		}
		finally
		{
			Console.SetOut(stdout);
			Console.SetError(stderr);
		}

		var text = printed.ToString().TrimEnd();
		return new JsonObject
		{
			["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text.Length > 0 ? text : "(çıktı yok)" }),
			["isError"] = code != 0,
		};
	}

	private static string Str(JsonElement arguments, string name) =>
		arguments.TryGetProperty(name, out var value)
			? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString()
			: "";

	private static string[] Opt(JsonElement arguments, string name) =>
		Str(arguments, name) is { Length: > 0 } value ? [value] : [];

	private static string[] Flag(JsonElement arguments, string name, string flag) =>
		arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True ? [flag] : [];

	/// <summary>A list argument as a repeated option: ["a", "b"] → --in a --in b. A lone string counts as one.</summary>
	private static string[] Each(JsonElement arguments, string name, string flag)
	{
		if (!arguments.TryGetProperty(name, out var value)) return [];
		var items = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(v => v.GetString() ?? "") : [value.GetString() ?? ""];
		return items.Where(i => i.Length > 0).SelectMany(i => new[] { flag, i }).ToArray();
	}

	/// <summary>An argument as an option with its value: focus → --focus <value>.</summary>
	private static string[] Opt2(JsonElement a, string name, string flag) => Str(a, name) is { Length: > 0 } v ? [flag, v] : [];
}
