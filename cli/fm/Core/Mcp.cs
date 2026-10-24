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
		new("fm_squad", "The first-team squad. Default view: position, age, status, wage, value. view=fitness: condition and match sharpness; view=morale: morale, hierarchy, social group; view=stats: this season's games, minutes, goals, assists, average rating and cards; view=youth: the club's youth sides (U21, U18…), each from its own squad page. Any other `view` is one of the squad table's own views by name (Contract, Injuries, Playing Time, Transfer, Development…; view=list prints them) and prints whatever columns it carries.",
			"""{"type":"object","properties":{"view":{"type":"string"}}}""",
			a => ["squad", .. Str(a, "view") switch { "" or "general" => Array.Empty<string>(), "fitness" => ["--fitness"], "morale" => ["--morale"], "stats" => ["--stats"], "youth" => ["--u19"], "list" => ["--views"], var other => ["--view", other] }]),
		new("fm_search", "The game's own search: players, staff, clubs and competitions matching a text. It matches loosely.",
			"""{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""",
			a => ["search", Str(a, "text")]),
		new("fm_player", "A player's page: age, nation, positions, value, wage, contract end, condition, personality, traits and all attributes, his 1-20 rating in each position (20 natural, 15+ accomplished, 10+ competent) and his injury history. A player of your own squad is opened from the squad table; anyone else is found with the game's search, so give enough of the name to single him out. For a player not scouted, some fields read \"Scouting Required\" and wages are ranges. `career` gives his career season by season instead: club, how he came (fee, loan, free), division, appearances, goals, rating, and totals.",
			"""{"type":"object","required":["name"],"properties":{"name":{"type":"string","description":"The player's name or a unique piece of it."},"career":{"type":"boolean"},"report":{"type":"boolean","description":"The scouts' report instead of the page: grade and recommendation, ability and potential in words, his interest in a move, value and wage estimate, pros and cons, where he would rank in our squad. Only for a player of another club who has been scouted."}}}""",
			a => ["player", Str(a, "name"), .. Flag(a, "career", "--career"), .. Flag(a, "report", "--report"), .. (a.TryGetProperty("report", out var rep) && rep.ValueKind == JsonValueKind.True ? new[] { "--json" } : [])]),
		new("fm_offer", "Bid for a player: opens his offer screen (or carries on one already open, including the other club's counter-offer after 'View Offer' or 'Negotiate'), sets the fee and any other terms, prints the offer as it stands and sends it. It cannot be taken back, so it only sends with send=true; without it the offer is prepared and left on screen (a dry run). Takes about five seconds. The other club answers through the inbox, by the deadline if one is set. loan=true makes a loan offer instead.",
			"""{"type":"object","required":["name"],"properties":{"name":{"type":"string","description":"The player's name or a unique piece of it."},"fee":{"type":"string","description":"The fee, e.g. 7.5M or 750K. Left out, the fee on screen stays."},"terms":{"type":"array","items":{"type":"string"},"description":"Other terms as 'Label=value[,value]', with the game's labels and choices: 'Percentage Of Next Sale=20%', 'After League Appearances=20 games,250K', 'Transfer Date=End Of Season', 'Negotiable=Non-Negotiable', 'Deadline=Tomorrow'. A fee or clause not on the offer yet is added. A bonus for a trophy or an award, or a player to exchange, sits in a submenu: give the path with '>', e.g. 'Bonus After Competition Achievement>Turkish Cup>Bonus upon reaching the Semi Final of the Turkish Cup=100K', or 'Players To Exchange' then 'Other Player From Squad>Senior Squad>Mert Müldür=6 Month Loan'. A dry run prints the terms and their choices."},"loan":{"type":"boolean"},"send":{"type":"boolean","description":"true sends the offer; otherwise it is only prepared."}}}""",
			a => ["offer", Str(a, "name"), .. Opt(a, "fee"), .. Each(a, "terms", "--term"), .. Flag(a, "loan", "--loan"), .. (a.TryGetProperty("send", out var send) && send!.ValueKind == JsonValueKind.True ? new[] { "--yes" } : ["--dry"])]),
		new("fm_contract", "Contract talks with a player, drawn as a card of terms. They open after a club accepts our bid (reply to '<player> offer accepted' with 'Enter Contract Negotiations' first) or, for one of our own players, with `name`: a new contract (renewal). Two stages: promises first (squad status, start date, any promise the agent asks for), then terms (wage, length, agent fee, bonuses, clauses). `terms` sets any term by name, 'Label=value' (e.g. 'Wage=20K', 'Contract Length=3 years', 'Yearly Wage Rise=10%', 'Appearance Fee=remove', 'Will appoint player as captain' to add a promise, 'Entire offer=Non-Negotiable' or a lock name on a term); a term the stage does not have is tried on the next. `next` takes the promises as they stand and moves on to the terms. `sign` puts the terms to the agent: his own are finalised, changed ones he answers with his own — call again with sign once the card shows what he wants. A renewal is decided by the player a few days later (inbox). Signing cannot be undone.",
			"""{"type":"object","properties":{"name":{"type":"string","description":"One of our players, to open talks on a new contract."},"wage":{"type":"string","description":"Wage, in the game's own wage period (Preferences › Your World › Wages), e.g. 9000 or 12.5K."},"terms":{"type":"array","items":{"type":"string"},"description":"'Label=value' per term, in English or as the card shows it."},"next":{"type":"boolean"},"sign":{"type":"boolean"},"cancel":{"type":"boolean","description":"Call the talks off."}}}""",
			a => ["contract", .. Flag(a, "cancel", "--cancel"), .. (Str(a, "name") is { Length: > 0 } n ? new[] { n } : []), .. (Str(a, "wage") is { Length: > 0 } w ? new[] { "--wage", w } : []), .. Each(a, "terms", "--term"), .. Flag(a, "next", "--next"), .. (a.TryGetProperty("sign", out var sign) && sign.ValueKind == JsonValueKind.True ? new[] { "--sign", "--yes" } : [])]),
		new("fm_register", "The squad list a competition asks for (a message 'squad to be decided' blocks time until it is sent). Without arguments it shows every player — on the list or not, homegrown flags, who cannot be changed (loaned out) — and the competition's rules with whether the list meets each. `add` / `remove` put players on or take them off; `auto` lets the assistant pick first; `send` submits it, which only works once every rule is met. A homegrown quota the club cannot fill is met by leaving that many places empty: take players without a homegrown flag off. Takes 20-60 seconds.",
			"""{"type":"object","properties":{"add":{"type":"array","items":{"type":"string"},"description":"Names (or unique pieces) to put on the list."},"remove":{"type":"array","items":{"type":"string"},"description":"Names (or unique pieces) to take off."},"auto":{"type":"boolean"},"send":{"type":"boolean"}}}""",
			a => ["register", .. Flag(a, "auto", "--auto"), .. Each(a, "add", "--in"), .. Each(a, "remove", "--out"), .. Flag(a, "send", "--send")]),
		new("fm_numbers", "The squad numbers: who wears what, the number a player asked for, the free numbers, and whether the game takes changes now (only at the start of the season or when a signing arrives; once sent they stay until next season). `player` + `number` picks a free number for him on the page; `send` registers what is on the page; `auto` has the assistant hand them all out and sends.",
			"""{"type":"object","properties":{"player":{"type":"string"},"number":{"type":"integer"},"auto":{"type":"boolean"},"send":{"type":"boolean"}}}""",
			a => ["numbers", .. Str(a, "player") is { Length: > 0 } who ? new[] { who, Str(a, "number") } : [], .. Flag(a, "auto", "--auto"), .. Flag(a, "send", "--send")]),
		new("fm_players", "The player database (every player the game knows), as far as the page has rows on screen — about twenty. `filter` applies one of the game's quick filters (Transfer Listed, Loan listed, Contract Expired — the free agents —, Contract Expiring, Unhappy, Goalkeepers…) and it stays applied; `view` chooses which columns the rows carry (Overview, General Info, Contract, Transfer…). list=filters or list=views prints the choices. A real search goes through the page's search panel and also stays on until clear=true: `role` (a role in possession he can play: Centre Forward, Wing-Back, Deep-Lying Playmaker…), `age` (a range, \"18-23\"), `grade` (the scouts' minimum grade, A+ to F), `contract` (Expiring (1 Year), Expiring (6 Months), Expired, Unattached…), `listed` / `loan_listed`.",
			"""{"type":"object","properties":{"filter":{"type":"string"},"view":{"type":"string"},"list":{"type":"string","enum":["filters","views"]},"clear":{"type":"boolean","description":"Remove the filter in force first."},"role":{"type":"string"},"age":{"type":"string"},"grade":{"type":"string"},"contract":{"type":"string"},"listed":{"type":"boolean"},"loan_listed":{"type":"boolean"},"position":{"type":"string","description":"The position he is natural at: GK, DR, DL, DC, WBR, WBL, DM, MR, ML, MC, AMR, AML, AMC, ST."},"max_value":{"type":"string","description":"The most he may be worth, e.g. 10M or 500K (the largest of the game's round figures not above it is used)."},"attributes":{"type":"array","items":{"type":"string"},"description":"Attribute conditions on the 1-20 scale, e.g. [\"pace>=16\", \"finishing>=13\"]. Players the scouts do not know have no known attributes and do not match."}}}""",
			a => ["players", .. Str(a, "list") switch { "filters" => new[] { "--filters" }, "views" => ["--views"], _ => [] }, .. Flag(a, "clear", "--clear"),
				.. Opt2(a, "role", "--role"), .. Opt2(a, "age", "--age"), .. Opt2(a, "grade", "--grade"), .. Opt2(a, "contract", "--contract"),
				.. Flag(a, "listed", "--listed"), .. Flag(a, "loan_listed", "--loan-listed"), .. Each(a, "attributes", "--attr"), .. Opt2(a, "position", "--position"), .. Opt2(a, "max_value", "--max-value"),
				.. Str(a, "filter") is { Length: > 0 } filter ? new[] { "--filter", filter } : [],
				.. Str(a, "view") is { Length: > 0 } view ? new[] { "--view", view } : []]),
		new("fm_table", "A league table: our league by default, or with `competition` another of ours that has one (e.g. \"UEFA Europa League\" for its league phase). Cups have none.",
			"""{"type":"object","properties":{"competition":{"type":"string"}}}""",
			a => ["table", .. Opt(a, "competition")]),
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
		new("fm_tactic_instructions", "The team instructions with the ball, phase by phase, and without it (line of engagement, defensive line, pressing, tackling…); without_ball=true lists only those. With `name` and `value` changes one; leaving `value` out lists its choices. Read and changed with no page opened; during a match it changes the match's tactic, applied at the next stoppage.",
			"""{"type":"object","properties":{"name":{"type":"string"},"value":{"type":"string"},"without_ball":{"type":"boolean"}}}""",
			a => ["tactic", "instructions", .. (Str(a, "name") is { Length: > 0 } n ? new[] { n } : []), .. (Str(a, "value") is { Length: > 0 } v ? new[] { v } : []), .. Flag(a, "without_ball", "--oop")]),
		new("fm_training", "Individual training: per player the position or role trained, a role out of possession and an extra focus. Without `player` lists everyone. With `player`, `focus`, `position` and `role` change his, written in the game's language as the list shows them (a start of one is enough; a wrong value lists the choices); position and role are submenus given as a path, e.g. 'Striker (Core)>Target Forward' (a submenu name alone lists its choices). report=true with `player` asks a coach (`coach`, a part of his name; without it the coaches are listed) for a training report on him and prints it. Traits (player preferred moves): traits=true lists his and the ones he can learn; `trait` starts learning one, `trait_remove` starts unlearning one, trait_cancel=true stops the current one, trait_suggest=true asks the assistant. The assistant's misgivings come back as a dialog; yes=true goes ahead anyway.",
			"""{"type":"object","properties":{"player":{"type":"string"},"focus":{"type":"string"},"position":{"type":"string"},"role":{"type":"string"},"report":{"type":"boolean"},"coach":{"type":"string"},"traits":{"type":"boolean"},"trait":{"type":"string"},"trait_remove":{"type":"string"},"trait_cancel":{"type":"boolean"},"trait_suggest":{"type":"boolean"},"yes":{"type":"boolean"}}}""",
			a => ["training", .. (Str(a, "player") is { Length: > 0 } p ? new[] { p } : []), .. Opt2(a, "focus", "--focus"), .. Opt2(a, "position", "--position"), .. Opt2(a, "role", "--role"), .. Flag(a, "report", "--report"), .. Opt2(a, "coach", "--coach"),
				.. Flag(a, "traits", "--trait"), .. Opt2(a, "trait", "--trait"), .. Opt2(a, "trait_remove", "--trait-remove"), .. Flag(a, "trait_cancel", "--trait-cancel"), .. Flag(a, "trait_suggest", "--trait-suggest"), .. Flag(a, "yes", "--yes")]),
		new("fm_intensity", "Training intensity per player. Without arguments lists every player's. `player` with `level` sets one: automatic, half, normal, double, 'rest 3' (no pitch or gym work for 3 days; 7 or 14 for a week or two) or 'holiday 1' (weeks); a player on holiday stays on it until it ends. auto=true leaves all of them to the head of fitness: the game first warns that every advised player may be sent on holiday, and without yes=true that is answered no and nothing changes.",
			"""{"type":"object","properties":{"player":{"type":"string"},"level":{"type":"string"},"auto":{"type":"boolean"},"yes":{"type":"boolean"}}}""",
			a => ["intensity", .. Flag(a, "auto", "--auto"), .. Flag(a, "yes", "--yes"), .. (Str(a, "player") is { Length: > 0 } p ? new[] { p } : []), .. (Str(a, "level") is { Length: > 0 } l ? new[] { l } : [])]),
		new("fm_coaches", "Coach assignments: which coach leads each of the nine training areas (attacking/defending/possession tactical and technical, goalkeeping, fitness, set pieces), with his stars there, each area's workload and its lead coach's stars. `coach` with `area` (e.g. 'Cohen', 'topla taktik' or 'Poss Tact') assigns him; off=true takes him off it. auto=true leaves the assignments to the assistant.",
			"""{"type":"object","properties":{"coach":{"type":"string"},"area":{"type":"string"},"off":{"type":"boolean"},"auto":{"type":"boolean"}}}""",
			a => ["coaches", .. Flag(a, "auto", "--auto"), .. Flag(a, "off", "--off"), .. (Str(a, "coach") is { Length: > 0 } c ? new[] { c } : []), .. (Str(a, "area") is { Length: > 0 } ar ? ar.Split(' ') : [])]),
		new("fm_opposition", "Opposition instructions for the next match: per opposing player tight marking, closing down (pressing), tackling and which foot to show him onto. advice=true applies the assistant's advice, clear=true clears them all; `player` with `set` (e.g. ['Pres=Always', 'Sert giriş=Hard'] or Marking/Pressing/Tackling/Foot=…) sets one player's.",
			"""{"type":"object","properties":{"advice":{"type":"boolean"},"clear":{"type":"boolean"},"player":{"type":"string"},"set":{"type":"array","items":{"type":"string"}}}}""",
			a => ["opposition", .. Flag(a, "advice", "--advice"), .. Flag(a, "clear", "--clear"), .. (Str(a, "player") is { Length: > 0 } p ? new[] { p } : []), .. (a.TryGetProperty("set", out var set) && set!.ValueKind == JsonValueKind.Array ? set.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : [])]),
		new("fm_release", "Let one of our players go: free=true releases him on a free (the game names the compensation and asks; yes=true agrees), mutual=true offers a mutual termination (the game does not ask, so yes=true is required). Without either it lists the two ways.",
			"""{"type":"object","properties":{"player":{"type":"string"},"free":{"type":"boolean"},"mutual":{"type":"boolean"},"yes":{"type":"boolean"}},"required":["player"]}""",
			a => ["release", Str(a, "player") ?? "", .. Flag(a, "free", "--free"), .. Flag(a, "mutual", "--mutual"), .. Flag(a, "yes", "--yes")]),
		new("fm_offers", "Offers other clubs have made for our players that still wait for an answer. Without arguments: the list (player, club, sale or loan, status, fee). With `offer` (its number or the player's name) and `answer`: view shows the full terms, stall plays for time, accept and reject answer it for good and need yes=true.",
			"""{"type":"object","properties":{"offer":{"type":"string"},"answer":{"type":"string","enum":["view","accept","stall","reject"]},"yes":{"type":"boolean"}}}""",
			a => Str(a, "offer") is { Length: > 0 } o && Str(a, "answer") is { Length: > 0 } answer ? ["offers", o, answer, .. Flag(a, "yes", "--yes")] : ["offers", "--json"]),
		new("fm_propose", "Offer one of our players to other clubs: for sale at `fee` (e.g. \"2M\"; left out = unspecified) or on loan (loan=true). Without yes=true it only shows what would be sent.",
			"""{"type":"object","properties":{"player":{"type":"string"},"fee":{"type":"string"},"loan":{"type":"boolean"},"yes":{"type":"boolean"}},"required":["player"]}""",
			a => ["propose", Str(a, "player") ?? "", .. Opt(a, "fee"), .. Flag(a, "loan", "--loan"), .. Flag(a, "yes", "--yes")]),
		new("fm_internationals", "Our players' national teams: country, caps and goals in all, and games and goals for it this season.",
			"""{"type":"object","properties":{}}""",
			a => ["internationals", "--json"]),
		new("fm_mentoring", "The mentoring groups (three players each, grouped to learn from one another) with each member's personality. auto=true asks the assistant to set the groups up (with groups already there the game deletes them all first: confirm must be true, else the question is shown and answered no). candidates=true lists who can be picked (age, workload, personality, standing). players (exactly three names, optional name) makes a group, not on a game day a group was taken apart; remove takes a group apart (confirm must be true).",
			"""{"type":"object","properties":{"auto":{"type":"boolean"},"candidates":{"type":"boolean"},"players":{"type":"array","items":{"type":"string"},"minItems":3,"maxItems":3},"name":{"type":"string"},"remove":{"type":"string"},"confirm":{"type":"boolean"}}}""",
			a => Str(a, "remove") is { Length: > 0 } gone ? ["mentoring", "remove", gone, .. Flag(a, "confirm", "--yes")]
				: a.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Array ? ["mentoring", "new", .. players.EnumerateArray().Select(p => p.GetString() ?? ""), .. Opt2(a, "name", "--name")]
				: a.TryGetProperty("candidates", out var c) && c!.ValueKind == JsonValueKind.True ? ["mentoring", "new", "--json"]
				: ["mentoring", .. Flag(a, "auto", "--auto"), .. Flag(a, "confirm", "--yes"), "--json"]),
		new("fm_dynamics", "The dressing room: its atmosphere, how many players back the manager, and each player's place in the hierarchy (Team Leader, Highly Influential, Influential, Other) and social group.",
			"""{"type":"object","properties":{}}""",
			a => ["dynamics", "--json"]),
		new("fm_jobs", "Managerial vacancies, for a manager out of work: every job in the world with its league, position, reputation and finances, and whether the club takes applications (asked of the jobs that are open; a filled one answers \"?\"). With a club, that job's detail (wage bill, facilities, manager rules). With an action, applies or declares interest; that needs confirm, and is the user's choice of club.",
			"""{"type":"object","properties":{"club":{"type":"string","description":"The club's name or its number in the list."},"action":{"type":"string","enum":["apply","interest"],"description":"Apply for the job, or declare interest in it."},"confirm":{"type":"boolean","description":"Send the application or interest. Only when the user has chosen this club."}}}""",
			a => ["jobs", .. Opt(a, "club"), .. Opt(a, "action"), .. Flag(a, "confirm", "--yes"), "--json"]),
		new("fm_news", "The media's headlines from the game's news site, in the page's own order: title, what it is about (a club, a competition), age and opening sentence where there is one. Not the inbox.",
			"""{"type":"object","properties":{"item":{"type":"string","description":"A headline's number in the list or a piece of its title: gives that item in full."}}}""",
			a => ["news", .. Opt(a, "item"), "--json"]),
		new("fm_prep", "The match preparation focus in training for the next match: the tactical focus in and out of possession and the two set-piece focuses, each with what it works on and what it could be instead (the game's suggestion marked). With `slot` (\"topla\" in possession, \"topsuz\" out of possession, \"duran1\" / \"duran2\" the set-piece slots) and `focus` (\"Progression\", \"Mid Block\", \"Attacking Free Kicks\"…) it changes that one.",
			"""{"type":"object","properties":{"slot":{"type":"string"},"focus":{"type":"string"}}}""",
			a => ["prep", .. (Str(a, "slot") is { Length: > 0 } sl ? new[] { sl } : []), .. (Str(a, "focus") is { Length: > 0 } fo ? new[] { fo } : [])]),
		new("fm_schedule", "The training schedule for this week and the next: each day's three sessions (Recovery, Match Tactics, Rest, Travel…) and the match days (H/A/N). With `day` (\"2025-10-14\", \"14\", \"14 ekim\") it changes it: `slot` (1-3) with `session` (\"Quickness\", \"Physical/Recovery\") sets that session, `slot` alone lists the categories and the sessions on offer; `style` (\"Gegenpress\", \"Training Style/Balanced\", \"Pre-Season Early/Light\") sets the whole week's schedule, and style=\"?\" lists the kinds. Past days and match slots cannot be changed.",
			"""{"type":"object","properties":{"day":{"type":"string"},"slot":{"type":"integer"},"session":{"type":"string"},"style":{"type":"string"}}}""",
			a => Str(a, "day") is not { Length: > 0 } day ? ["schedule"]
				: Str(a, "style") is { Length: > 0 } style ? ["schedule", "style", day, .. (style == "?" ? Array.Empty<string>() : [style])]
				: ["schedule", "set", day, a.TryGetProperty("slot", out var slot) ? slot.ToString() : "", .. (Str(a, "session") is { Length: > 0 } s ? new[] { s } : [])]),
		new("fm_penalties", "The penalty takers in order (composure, penalty taking, where each starts) and the three rules on when the next taker steps up. `action` changes the order: \"move\" puts `player` (a name or his place) at `place`; \"add\" adds one of our players, at `place` when given, else last; \"remove\" takes `player` out. `rule` (1-3) with `on` switches a rule.",
			"""{"type":"object","properties":{"action":{"type":"string","enum":["move","add","remove"]},"player":{"type":"string"},"place":{"type":"integer"},"rule":{"type":"integer"},"on":{"type":"boolean"}}}""",
			a => Str(a, "action") is { Length: > 0 } action
				? ["penalties", action, .. (Str(a, "player") is { Length: > 0 } p ? new[] { p } : []), .. (a.TryGetProperty("place", out var place) ? new[] { place.ToString() } : [])]
				: a.TryGetProperty("rule", out var rule) ? ["penalties", rule.ToString(), a.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.True ? "açık" : "kapalı"]
				: ["penalties"]),
		new("fm_query", "Filter our own players by conditions joined with \"ve\": a field, a comparison (< <= > >= = !=) and a value. Fields: yaş, mevki (text, contains), no, ülke, boy, kondisyon, form (match sharpness), sakat (flag: \"sakat\" or \"!sakat\"), potansiyel (out of 200, as the club sees it), maaş (weekly: 50K), sözleşme (time left: 1yıl, 18ay), statü, maç, gol, asist, puan. Example: \"yaş<23 ve potansiyel>140\". `sort` names the field to order by, `ascending` smallest first.",
			"""{"type":"object","required":["where"],"properties":{"where":{"type":"string"},"sort":{"type":"string"},"ascending":{"type":"boolean"}}}""",
			a => ["query", Str(a, "where") ?? "", .. (Str(a, "sort") is { Length: > 0 } sort ? new[] { "--sort", sort } : []), .. Flag(a, "ascending", "--asc")]),
		new("fm_takers", "The set piece takers. Without `kind`: the order for all five kinds. With `kind` (korner, frikik = free kick in shooting range, kanat = wide free kick, derin = deep free kick, tac = throw-in in the attacking third): its two lists (right and left) with each taker's attributes. `side` (sağ or sol) with `action` changes that list: \"move\" puts `player` (a name or his place) at `place`; \"add\" adds one of our players, at `place` when given, else last; \"remove\" takes `player` out.",
			"""{"type":"object","properties":{"kind":{"type":"string","enum":["korner","frikik","kanat","derin","tac"]},"side":{"type":"string","enum":["sağ","sol"]},"action":{"type":"string","enum":["move","add","remove"]},"player":{"type":"string"},"place":{"type":"integer"}}}""",
			a => Str(a, "kind") is not { Length: > 0 } kind ? ["takers"]
				: Str(a, "side") is { Length: > 0 } side && Str(a, "action") is { Length: > 0 } action
					? ["takers", kind, side, action, .. (Str(a, "player") is { Length: > 0 } p ? new[] { p } : []), .. (a.TryGetProperty("place", out var place) ? new[] { place.ToString() } : [])]
					: ["takers", kind]),
		new("fm_setpieces", "A set piece routine: delivery (Inswinger / Outswinger), where the ball is aimed, and each place in the routine (A1, C2, D3…) with its task. `kind`: korner, frikik = free kick in shooting range, kanat = wide free kick, derin = deep free kick, tac = throw-in in the attacking third. To change it give one of: `delivery` (in or out); `aim` (a choice's name or a piece of it); `slot` with `task` (and `otherwise` for the second choice some tasks have). The result lists the choices.",
			"""{"type":"object","required":["kind"],"properties":{"kind":{"type":"string","enum":["korner","frikik","kanat","derin","tac"]},"delivery":{"type":"string","enum":["in","out"]},"aim":{"type":"string"},"slot":{"type":"string"},"task":{"type":"string"},"otherwise":{"type":"string"}}}""",
			a => Str(a, "delivery") is { Length: > 0 } delivery ? ["setpieces", Str(a, "kind"), "vurus", delivery, "--json"]
				: Str(a, "aim") is { Length: > 0 } aim ? ["setpieces", Str(a, "kind"), "hedef", aim, "--json"]
				: Str(a, "slot") is { Length: > 0 } slot && Str(a, "task") is { Length: > 0 } task ? ["setpieces", Str(a, "kind"), slot, task, .. Opt(a, "otherwise"), "--json"]
				: ["setpieces", Str(a, "kind"), "--json"]),
		new("fm_happiness", "How happy each first-team player is (Very Unhappy … Delighted), least happy first, with his status flags (Wnt: wanted by another club, Unr: unregistered, FtT: future transfer, Rst: resting…). `player` gives one player's reasons: morale, standing in the dressing room, what he is pleased about and what he holds against the club. `why` lists every player who has a complaint, with the complaints (takes ~15 seconds).",
			"""{"type":"object","properties":{"player":{"type":"string"},"why":{"type":"boolean"}}}""",
			a => ["happiness", .. Str(a, "player") is { Length: > 0 } who ? new[] { who } : [], .. Flag(a, "why", "--why")]),
		new("fm_depth", "Squad depth: for each position (GK, DL/DC/DR, WBL/DM/WBR, ML/MC/MR, AML/AMC/AMR, ST) the first-team players who can play it, from their position strings; loanees and under-19s left out. Then the age profile.",
			"""{"type":"object","properties":{}}""",
			_ => ["depth"]),
		new("fm_targets", "Transfer targets. Without `position`: the transfer and wage budget left and the positions where the squad has one fit player or none (which to search is the manager's call). With `position` (GK, DL, DC, DR, WBL, DM, WBR, ML, MC, MR, AML, AMC, AMR, ST, or Turkish like \"sol bek\"): the player database searched for that position with a value up to the transfer budget, or `max_value` (\"5M\"); `age` (\"18-23\") narrows it.",
			"""{"type":"object","properties":{"position":{"type":"string"},"max_value":{"type":"string"},"age":{"type":"string"}}}""",
			a => ["targets", .. (Str(a, "position") is { Length: > 0 } p ? new[] { p } : []), .. (Str(a, "max_value") is { Length: > 0 } m ? new[] { "--max-value", m } : []), .. (Str(a, "age") is { Length: > 0 } ag ? new[] { "--age", ag } : [])]),
		new("fm_settings", "The game's preferences: match highlights (how much of a match is shown) and match speed during / between highlights, auto save on/off, interval and type, language, currency and display units. With `name` (e.g. \"vurgu hızı\", \"kayıt sıklığı\") and `value` (e.g. \"2x\", \"Day\") it changes one and confirms it; with only `name` it lists that setting's choices.",
			"""{"type":"object","properties":{"name":{"type":"string"},"value":{"type":"string"}}}""",
			a => ["settings", .. (Str(a, "name") is { Length: > 0 } n ? new[] { n } : []), .. (Str(a, "value") is { Length: > 0 } v ? new[] { v } : [])]),
		new("fm_dugout", "During a match: the assistant's advice in the Dugout panel (minute and text: a team or opposition instruction, a word with a player, a substitution, a remark). With `number` and `action` (\"uygula\" to do it, \"kaldır\" to dismiss) acts on one.",
			"""{"type":"object","properties":{"number":{"type":"integer"},"action":{"type":"string","enum":["uygula","kaldır"]}}}""",
			a => ["dugout", .. (a.TryGetProperty("number", out var n) && n.ValueKind == JsonValueKind.Number ? new[] { n.GetInt32().ToString() } : []), .. (Str(a, "action") is { Length: > 0 } act ? new[] { act } : [])]),
		new("fm_budget", "The club's transfer, wage and scouting budgets. With `adjust`, lists the board's transfer↔wage slider: every step with the transfer and weekly wage budget it gives; with `adjust` and `step` it applies that step.",
			"""{"type":"object","properties":{"adjust":{"type":"boolean"},"step":{"type":"integer"}}}""",
			a => ["budget", .. (a.TryGetProperty("adjust", out var adj) && adj.ValueKind == JsonValueKind.True ? new[] { "ayarla" } : []), .. (a.TryGetProperty("step", out var st) && st!.ValueKind == JsonValueKind.Number ? new[] { st.GetInt32().ToString() } : [])]),
		new("fm_league", "A competition's statistics. Bare: its leaders — ten team and ten player statistics, each with who leads it. With `stat`: everyone ranked by that statistic (top scorers with \"Goals\", \"Assists\", \"Average Rating\", \"Expected Goals\", \"Saves\"…; `teams` for the team rankings such as \"Form\", \"Average Attendance\", \"Goals\"), the first `top` rows (20 when left out). `stats` lists every statistic's name. `competition` is one of the club's (\"Super League\", \"UEFA Europa League\", \"Turkish Cup\"…); the league when left out.",
			"""{"type":"object","properties":{"competition":{"type":"string"},"stat":{"type":"string"},"teams":{"type":"boolean"},"top":{"type":"integer"},"stats":{"type":"boolean"}}}""",
			a => ["league", .. (Str(a, "competition") is { Length: > 0 } c ? new[] { c } : []),
				.. (Str(a, "stat") is { Length: > 0 } st ? new[] { "--stat", st } : []),
				.. (a.TryGetProperty("teams", out var tm) && tm.ValueKind == JsonValueKind.True ? new[] { "--teams" } : []),
				.. (a.TryGetProperty("top", out var tp) && tp.ValueKind == JsonValueKind.Number ? new[] { "--top", tp.GetInt32().ToString() } : []),
				.. (a.TryGetProperty("stats", out var all) && all.ValueKind == JsonValueKind.True ? new[] { "--stats" } : [])]),
		new("fm_health", "The squad's health: who is injured (what and for how long), who is tired (condition under 85%), who lacks match sharpness (under 60%), and who carries the highest injury risk by the game's own score.",
			"""{"type":"object","properties":{}}""",
			_ => ["health"]),
		new("fm_club", "The club at a glance: the board's grade and mood, the club vision's objectives with their importance and status, supporters' mood, the bank balance, last month's income and expenditure, the wage bill against its budget, reputation, stadium and facilities, and each staff team's filled places and vacant roles.",
			"""{"type":"object","properties":{}}""",
			_ => ["club"]),
		new("fm_staff", "The club's staff, one by one: name, job, age, nation, wage (as the game shows it) and contract end, senior squad then U19. Then each team (coaching, recruitment, medical) with the roles still short of the board's minimum and the board's word on its size.",
			"""{"type":"object","properties":{}}""",
			_ => ["staff"]),
		new("fm_finances", "The club's finances: bank balance and its month-by-month history, this month's income and expenditure, remaining and original debt, financial fair play status, the weekly wage bill against its budget and split by player status (Star Player, Regular Starter…), each sponsorship deal (type, value per season, end date), and the board's balance and turnover projections. With `breakdown` (\"gelir\" income or \"gider\" expenditure) it lists that side item by item instead, for `period` \"ay\" (this month, default), \"geçen ay\", \"sezon\" or \"geçen sezon\".",
			"""{"type":"object","properties":{"breakdown":{"type":"string","enum":["gelir","gider"]},"period":{"type":"string","enum":["ay","geçen ay","sezon","geçen sezon"]}}}""",
			a => ["finances", .. (Str(a, "breakdown") is { Length: > 0 } b ? new[] { b } : []), .. (Str(a, "breakdown") is { Length: > 0 } && Str(a, "period") is { Length: > 0 } p ? new[] { p } : [])]),
		new("fm_supporters", "The supporters: their overall mood, influence on the board and reaction after recent matches, average attendance, season ticket holders and waiting list, social media followers, rivals, each expectation (text, importance, type, reaction), the fan base's make-up (Core, Family, Fair Weather…) and the spokesman's description.",
			"""{"type":"object","properties":{}}""",
			_ => ["supporters"]),
		new("fm_doctor", "What is wrong with the squad, in one look: who is injured and how many days he has left, who is tired (condition under 85%) or short of match fitness (under 60%), whose contract ends within a year, which positions have one fit player or none, and what the board is worried about. Each finding names the tool that goes into it.",
			"""{"type":"object","properties":{}}""",
			_ => ["doctor"]),
		new("fm_board", "What the board and the supporters think of the manager: a sentence each on the overall verdict, recent results, backing from the squad, tactics, transfers, the club's vision and culture and the competitions, then what pleased them and what worries them.",
			"""{"type":"object","properties":{}}""",
			_ => ["board"]),
		new("fm_board_request", "Ask the board for something. Without `request`: everything that can be asked for, by kind (facilities, budgets, affiliates and youth recruitment, staff numbers and wages, a new stadium, a new contract). With `request` (a name or a distinguishing part of one) and confirm=true the request is put to the board; its decision arrives later as an inbox message. The board takes no new requests while some are awaiting its answer.",
			"""{"type":"object","properties":{"request":{"type":"string"},"confirm":{"type":"boolean"}}}""",
			a => ["board", "request", .. Opt(a, "request"), .. Flag(a, "confirm", "--yes"), "--json"]),
		new("fm_facilities", "The club's facilities: the stadium (name, grade, capacity, year built, owner, under-soil heating, roof, surface, pitch size and condition), the training ground's grade, the youth facilities, academy coaching and youth recruitment, and the corporate facilities.",
			"""{"type":"object","properties":{}}""",
			_ => ["facilities"]),
		new("fm_contracts", "When each squad player's contract ends, soonest first, with age, position and time left.",
			"""{"type":"object","properties":{}}""",
			_ => ["contracts"]),
		new("fm_vision", "The club vision meeting with the board, open after `Negotiate` on its end-of-season message: what the board says and the objectives, each numbered with its weight. `remove` (a number or part of an objective's text) strikes one that is not required, `action` is one of suggest (put the changes to the board), undo, reset, confirm (accept the vision; not taken back) or end (close the meeting).",
			"""{"type":"object","properties":{"remove":{"type":"string"},"action":{"type":"string","enum":["suggest","undo","reset","confirm","end"]}}}""",
			a => Str(a, "remove") is { Length: > 0 } r ? ["vision", "remove", r]
				: Str(a, "action") is { Length: > 0 } act ? act == "confirm" ? ["vision", "confirm", "--yes"] : ["vision", act]
				: ["vision", "--json"]),
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
		new("fm_tactic_pi", "One position's player instructions with the ball (Shooting, Forward Runs, Dribbling Frequency, Passing Risk…) and without it (Trigger Press, Marking, Tackling, Marking Target). Ones marked as from the role are fixed by it and change only with the role. `position` is the slot as fm_tactic shows it (DR, AML, ST…) or the player in it; during a match changes go to the match's tactic. With `name` and `value` one is changed, with `name` alone its choices are listed.",
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
		new("fm_tactic_file", "Tactics kept as files. Without arguments: the saved tactics. save=<name> writes the active tactic under that name (the tactic takes the name too; overwrite=true to replace a file). load=<name> puts a saved tactic in the active slot in place of the one there (confirm must be true). delete=<name> removes a file (confirm must be true).",
			"""{"type":"object","properties":{"save":{"type":"string"},"note":{"type":"string"},"overwrite":{"type":"boolean"},"load":{"type":"string"},"delete":{"type":"string"},"confirm":{"type":"boolean"}}}""",
			a => Str(a, "save") is { Length: > 0 } saved ? ["tactic", "save", saved, .. Opt2(a, "note", "--note"), .. Flag(a, "overwrite", "--yes")]
				: Str(a, "load") is { Length: > 0 } loaded ? ["tactic", "load", loaded, .. Flag(a, "confirm", "--yes")]
				: Str(a, "delete") is { Length: > 0 } gone ? ["tactic", "saved", "delete", gone, .. Flag(a, "confirm", "--yes")]
				: ["tactic", "saved", "--json"]),
		new("fm_tactic_new", "Create a tactic through the game's wizard: a style, then a formation in possession, then one out of possession. This is also how the formation is changed: with a tactic in place it goes into a new slot (up to three) and becomes the active one; the old one stays in its slot (fm_tactic_slot). Leave a step out to get its choices listed; the wizard stays where it is.",
			"""{"type":"object","properties":{"style":{"type":"string"},"in_possession":{"type":"string"},"out_of_possession":{"type":"string"}}}""",
			a => ["tactic", "new", .. Opt(a, "style"), .. Opt(a, "in_possession"), .. Opt(a, "out_of_possession")]),
		new("fm_setpieces_new", "Set up set piece routines through the game's six-question wizard (only while none are set up). `answers` gives an option number per question in order; a question left out keeps the game's own choice.",
			"""{"type":"object","properties":{"answers":{"type":"array","items":{"type":"integer","minimum":1}}}}""",
			a => ["setpieces", "new", .. a.TryGetProperty("answers", out var list) && list!.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(n => n.ToString()) : []]),
		new("fm_match", "Play match day once fm_status says it is match day. How is the manager's choice, so ask if you do not know it. mode=instant leaves the match to the staff and returns the result in seconds. mode=play plays it out with the commentary and stops at each team talk with the lines on offer: say one with fm_talk, then call fm_match again to go on. mode=play_no_talk plays it out passing the talks by. Playing takes about six minutes. If a player is injured the match stops and it returns, naming him from the assistant's note: make the change with fm_sub (which also restarts play), or call fm_match again to go on without one.",
			"""{"type":"object","properties":{"mode":{"type":"string","enum":["instant","play","play_no_talk"]}},"required":["mode"]}""",
			a => ["match", .. Str(a, "mode") switch { "instant" => new[] { "--instant" }, "play_no_talk" => ["--play", "--no-talk"], _ => ["--play"] }]),
		new("fm_sub", "During a match: without arguments, who is on the pitch (mood, condition) and who is on the bench; with both, take `off` off for `on`. At an injury stop it also confirms the change, which restarts play.",
			"""{"type":"object","properties":{"off":{"type":"string"},"on":{"type":"string"},"dry":{"type":"boolean"}}}""",
			a => ["sub", .. Opt(a, "off"), .. Opt(a, "on"), .. Flag(a, "dry", "--dry")]),
		new("fm_save", "Save the game over its current file, or with `as` under another name — from then on the game saves to that file, its automatic saves included. Overwriting cannot be undone; `confirm` must be true.",
			"""{"type":"object","required":["confirm"],"properties":{"confirm":{"type":"boolean"},"as":{"type":"string"}}}""",
			a => ["save", .. Opt2(a, "as", "--as"), .. Flag(a, "confirm", "--yes")]),
		new("fm_stages", "A competition stage by stage: a cup round's ties with scores (penalties, extra time, aggregate), a league stage's table. Any competition in the game's world can be named, not only the club's own (`list` gives them all); without `stage` it is the current one, `all` reads every stage.",
			"""{"type":"object","properties":{"competition":{"type":"string"},"stage":{"type":"string"},"all":{"type":"boolean"},"list":{"type":"boolean"}}}""",
			a => ["stages", .. Opt(a, "competition"), .. Opt(a, "stage"), .. Flag(a, "all", "--all"), .. Flag(a, "list", "--list"), "--json"]),
		new("fm_scout", "The recruitment focuses: what the scouts are looking for, its status, who is assigned, and how many players are matched, nearly matched and still being assessed. With `focus` (its number or name): the players that focus has found — positions, best role, club, value range, the scouts' grade and recommendation. `show` picks matched, near or progress; by default matched, or nearly matched when none is.",
			"""{"type":"object","properties":{"focus":{"type":"string"},"show":{"type":"string","enum":["matched","near","progress"]}}}""",
			a => ["scout", .. Opt(a, "focus"), .. (Str(a, "show") is { Length: > 0 } show ? new[] { "--" + show } : []), "--json"]),
		new("fm_world", "Which save is loaded, when it was last saved (real and in-game), the autosave setting, time played, and the size of the game world: nations, leagues and players loaded, detail levels.",
			"""{"type":"object","properties":{}}""",
			_ => ["world", "--json"]),
		new("fm_responsibilities", "Who does what among the staff duties (hiring and firing, contract renewals, coaching courses, team selection): each duty, who holds it, and whether the manager kept it or delegated it. `filter` narrows the list. `delegate` or `take` with a duty's name (or its distinguishing words) hands it on to the person the game recommends, or takes it back. `give` with a duty's name hands it to someone in particular: `to` names him; without `to` the people who can take it are listed.",
			"""{"type":"object","properties":{"filter":{"type":"string"},"delegate":{"type":"string"},"take":{"type":"string"},"give":{"type":"string"},"to":{"type":"string"}}}""",
			a => Str(a, "give") is { Length: > 0 } g ? ["responsibilities", "give", g, .. Opt(a, "to")]
				: Str(a, "delegate") is { Length: > 0 } d ? ["responsibilities", "delegate", d]
				: Str(a, "take") is { Length: > 0 } t ? ["responsibilities", "take", t]
				: ["responsibilities", .. Opt(a, "filter"), "--json"]),
		new("fm_youth", "The youth setup: the academy's overall rating, coaching and recruitment, youth level and stadium, staff numbers, the youngsters thought ready for the first team, the date of the next youth intake and whether its candidates have arrived. `training`=true gives the under-19s' training week instead: how it went, the average rating, each player's extra focus and rating. `candidates`=true gives the year's intake once it has arrived: each candidate's group (Elite, Top, Good, Decent, Poor), age, position, role, nation, ability and potential in stars (potential is a range).",
			"""{"type":"object","properties":{"training":{"type":"boolean"},"candidates":{"type":"boolean"}}}""",
			a => ["youth", .. Flag(a, "training", "--training"), .. Flag(a, "candidates", "--candidates"), "--json"]),
		new("fm_progress", "Who in the squad is improving and who is declining: each player's ability (out of 200) at the last four monthly measurements, the change across them, and the potential the club sees. Sorted from most improved to most declined.",
			"""{"type":"object","properties":{}}""",
			_ => ["progress", "--json"]),
		new("fm_staff_find", "Search the game's staff database (coaches, scouts, physios, analysts): job, contract, wage, status (FrA = free agent). `filter` is one of the game's ready filters (staff interested in joining the first team, reserves or youth team); `view` changes the columns (Contract, Rating, Coaching, Medical, Mental, Scouting — the last four give attribute ratings out of 20); `text` narrows by name or job.",
			"""{"type":"object","properties":{"text":{"type":"string"},"role":{"type":"string","description":"Scout, Coach, Physio, Goalkeeping Coach, Assistant Manager, Fitness Coach, Sports Scientist, Recruitment Analyst…"},"free":{"type":"boolean","description":"only those not employed"},"reputation":{"type":"string","description":"minimum: Decent, Good, Very Good, Superb…"},"filter":{"type":"string"},"view":{"type":"string"},"clear":{"type":"boolean","description":"reset the search first; a search stays on between calls"}}}""",
			a => ["staff", "find", .. Opt(a, "text"), .. Flag(a, "clear", "--clear"), .. Opt2(a, "role", "--role"), .. Flag(a, "free", "--free"), .. Opt2(a, "reputation", "--reputation"),
				.. Opt2(a, "filter", "--filter"), .. Opt2(a, "view", "--view"), "--json"]),
		new("fm_staff_hire", "Approach someone to join the staff (find candidates with fm_staff_find). Opens the contract talks — job, wage, length — and shows what he asks for; nothing is offered yet. Carry them on with fm_contract (wage, terms, sign), or pass `wage` and `sign` + `confirm` here. His answer arrives in the inbox some days after the offer.",
			"""{"type":"object","properties":{"name":{"type":"string"},"wage":{"type":"string","description":"wage to offer, in the game's own wage period (Preferences › Your World › Wages), e.g. 1200 or 3.5K"},"sign":{"type":"boolean","description":"put the offer to him"},"confirm":{"type":"boolean","description":"required with sign"}},"required":["name"]}""",
			a => ["staff", "hire", Str(a, "name") ?? "", .. Opt2(a, "wage", "--wage"), .. Flag(a, "sign", "--sign"), .. Flag(a, "confirm", "--yes")]),
		new("fm_staff_fire", "Let one of our staff go. Without `confirm` it only shows the game's question with the compensation to be paid, and answers No. `mutual` offers a mutual termination instead (the game does not ask about that one; it needs `confirm` too).",
			"""{"type":"object","properties":{"name":{"type":"string"},"mutual":{"type":"boolean"},"confirm":{"type":"boolean"}},"required":["name"]}""",
			a => ["staff", "fire", Str(a, "name") ?? "", .. Flag(a, "mutual", "--mutual"), .. Flag(a, "confirm", "--yes")]),
		new("fm_actions", "What the game lets be done about a person — the menu a right click on the name raises: for a player scouting assignments, shortlist, declare interest, offer a trial, squad planner; for staff approach to sign, shortlist; for our own staff contract talks, termination, coaching course. Without `choose` it lists the menu (paths joined with ' > '); with `choose` (a path, or its last part when that is unique) it does it at once and says what followed (a question from the game, a conversation, contract talks).",
			"""{"type":"object","properties":{"person":{"type":"string"},"choose":{"type":"string","description":"e.g. 'Add To Shortlist > Indefinitely', 'Send On Coaching Course'"}},"required":["person"]}""",
			a => Str(a, "choose") is { Length: > 0 } pick ? ["actions", Str(a, "person") ?? "", pick] : ["actions", Str(a, "person") ?? "", "--json"]),
		new("fm_scout_focus", "Sets up, changes, stops or restarts a recruitment focus. `action` new: at least `position` (e.g. DL, 'D (C)', STC) or `area`; `role` goes with a position; `area` is one or more comma-separated places — a continent, region or nation by name ('Northern Europe', 'Türkiye') or a path for a competition ('Competition>Türkiye>Super League'); `age` like '18-23'; `ability` and `potential` are the least stars sought (0.5 to 5 in halves); `by` names the scouts to put on it, comma-separated (see `action` staff; otherwise the game picks); `name` up to 25 letters; `priority` (top, standard, ongoing) is required. `action` edit changes the focus named in `focus` with the same fields except position and role (areas are added; only what is passed changes; the game keeps the old version listed as complete). `action` areas lists the area choices under `area` (a path, or empty for the top). `action` remove stops the focus named in `focus` (number or name; it stays listed as stopped; needs `confirm`), restart sets a stopped or complete one going again. `action` staff lists the scouts: how well each judges ability and potential, how many regions and nations he knows, the focuses he is on.",
			"""{"type":"object","properties":{"action":{"type":"string","enum":["new","edit","areas","remove","restart","staff"]},"focus":{"type":"string"},"position":{"type":"string"},"role":{"type":"string"},"area":{"type":"string"},"age":{"type":"string"},"ability":{"type":"string"},"potential":{"type":"string"},"by":{"type":"string"},"analysts":{"type":"string","description":"Comma-separated analysts to put on the focus instead of any analyst."},"name":{"type":"string"},"priority":{"type":"string","enum":["top","standard","ongoing"]},"confirm":{"type":"boolean"}},"required":["action"]}""",
			a => Str(a, "action") switch
			{
				"areas" => ["scout", "areas", .. Opt(a, "area"), "--json"],
				"staff" => ["scout", "staff", "--json"],
				"remove" => ["scout", "remove", Str(a, "focus") ?? "", .. Flag(a, "confirm", "--yes")],
				"restart" => ["scout", "restart", Str(a, "focus") ?? ""],
				"edit" => ["scout", "edit", Str(a, "focus") ?? "", .. Opt2(a, "area", "--area"), .. Opt2(a, "age", "--age"), .. Opt2(a, "ability", "--ability"), .. Opt2(a, "potential", "--potential"),
					.. Opt2(a, "by", "--by"), .. Opt2(a, "analysts", "--analysts"), .. Opt2(a, "name", "--name"), .. Opt2(a, "priority", "--priority")],
				_ => ["scout", "new", .. Opt2(a, "position", "--position"), .. Opt2(a, "role", "--role"), .. Opt2(a, "area", "--area"), .. Opt2(a, "age", "--age"), .. Opt2(a, "ability", "--ability"), .. Opt2(a, "potential", "--potential"),
					.. Opt2(a, "by", "--by"), .. Opt2(a, "analysts", "--analysts"), .. Opt2(a, "name", "--name"), .. Opt2(a, "priority", "--priority")],
			}),
		new("fm_scout_player", "Sends a scout to watch a player: the scouting pool, or the scout named in `by` (`who`=true lists the scouts he can be given to). `length` is required: '1 week', '2 weeks', '1 month', '2 months', '3 months', '1 match' … '4 matches', or 'extensive'. Cancelling an assignment does not take effect from here.",
			"""{"type":"object","properties":{"player":{"type":"string"},"length":{"type":"string"},"by":{"type":"string"},"who":{"type":"boolean"}},"required":["player"]}""",
			a => a.TryGetProperty("who", out var who) && who.ValueKind == JsonValueKind.True ? ["scout", "who", Str(a, "player") ?? "", "--json"]
				: ["scout", "player", Str(a, "player") ?? "", .. Opt2(a, "by", "--by"), .. Opt2(a, "length", "--for")]),
		new("fm_shortlist_change", "Puts a player on the shortlist for `length` ('1 month', '3 months', '6 months', '1 year', 'indefinitely'), or with `remove` takes him off it.",
			"""{"type":"object","properties":{"player":{"type":"string"},"length":{"type":"string"},"remove":{"type":"boolean"}},"required":["player"]}""",
			a => a.TryGetProperty("remove", out var remove) && remove.ValueKind == JsonValueKind.True ? ["shortlist", "remove", Str(a, "player") ?? ""]
				: ["shortlist", "add", Str(a, "player") ?? "", .. Opt2(a, "length", "--for")]),
		new("fm_approach", "A move towards a player short of an offer. `move`: 'interest' (with `as`: top, transfer or loan), 'enquire' (asks his club what it would take; the answer comes to the inbox), 'trial' (with `weeks` 1-4). The game asks before interest and trial are sent; `confirm` true answers yes, otherwise the question is shown and declined.",
			"""{"type":"object","properties":{"player":{"type":"string"},"move":{"type":"string","enum":["interest","enquire","trial"]},"as":{"type":"string","enum":["top","transfer","loan"]},"weeks":{"type":"string"},"confirm":{"type":"boolean"}},"required":["player","move"]}""",
			a => [Str(a, "move") ?? "interest", Str(a, "player") ?? "", .. (Str(a, "as") is { Length: > 0 } how ? new[] { "--" + how } : []), .. Opt2(a, "weeks", "--for"), .. Flag(a, "confirm", "--yes")]),
		new("fm_saves", "The save files on disk, newest first. With `delete`, removes that save (the loaded one is refused); `confirm` must be true.",
			"""{"type":"object","properties":{"delete":{"type":"string"},"confirm":{"type":"boolean"}}}""",
			a => Str(a, "delete") is { Length: > 0 } gone ? ["saves", "delete", gone, .. Flag(a, "confirm", "--yes")] : ["saves"]),
		new("fm_load", "Load a save by name (see fm_saves). The game only loads from its main menu, so a game already in a save is closed and started again, which takes a minute or two; `save` says whether the current progress is saved first.",
			"""{"type":"object","required":["name","save"],"properties":{"name":{"type":"string"},"save":{"type":"boolean"}}}""",
			a => ["load", Str(a, "name"), a.TryGetProperty("save", out var save) && save.ValueKind == JsonValueKind.True ? "--save" : "--no-save"]),
		new("fm_stop", "Close the game. Anything not saved is lost, so `save` must say which: true saves over the current file first, false closes without saving.",
			"""{"type":"object","required":["save"],"properties":{"save":{"type":"boolean"}}}""",
			a => ["stop", a.TryGetProperty("save", out var save) && save.ValueKind == JsonValueKind.True ? "--save" : "--no-save"]),
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

			JsonNode? result = null;
			JsonNode? error = null;

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
						["instructions"] = "Plays Football Manager 26 through the running game. Start with fm_status; it says what the game is waiting for and which tool deals with it. Command output is in the language the game is set to (Turkish words in a Turkish game, English otherwise).",
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
