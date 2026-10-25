using System.Text.Json;

namespace FmCli;

/// <summary>Read-only views: the league table and the fixture list.</summary>
internal static partial class Program
{
	/// <summary>
	/// Reads a shaped endpoint, opening the page that binds it when nothing is bound yet, and
	/// waiting for the rows rather than for a fixed delay.
	/// </summary>
	private static async Task<JsonElement> Shown(string route, string screen)
	{
		var data = await Fm.GetJson(route);
		if (data.GetArrayLength() > 0) return data;

		var opened = await OpenPage(screen);
		if (opened.StartsWith("error", StringComparison.Ordinal)) throw new FmError(opened);

		await Until(async () => (data = await Fm.GetJson(route)).GetArrayLength() > 0);
		return data;
	}

	/// <summary>Pages are not opened over a match or the results page after it; null when it is safe to read.</summary>
	private static async Task<int?> NotNow()
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		var state = await Fm.GetJson("state");
		if (Field(state, "DuringMatch") == "True")
		{
			Console.Error.WriteLine(L("No other page opens during a match. For the match: match"));
			return 1;
		}
		// After a match the game stands on its results and news page until it is passed. A page opened
		// from there leaves that step undone: Continue then did nothing at all, for the rest of the
		// session, and only loading the save again brought time back (Stevenage, 18 Nisan 2026).
		if (Field(state, "State") == "Results")
		{
			Console.Error.WriteLine(L("The game is on the post-match results screen; go past it before opening another page: next"));
			return 1;
		}
		return null;
	}

	/// <summary>
	/// What tells people of one name apart, the way the game's own lists do: age, positions,
	/// nation and club, one line per search result ("22 · AM (RL), ST (C) · MLI · Club").
	/// The row binds only the name, so each person is put at a node of ours and read there; the
	/// game fills the values on the second reading.
	/// </summary>
	private static async Task<List<string>> Who(List<JsonElement> results)
	{
		var read = new Dictionary<string, string>[results.Count];
		// A member of staff is told by his job, which the game words for a team and a manager beside him.
		try
		{
			await Fm.Get("pathset", ("to", "fmcli.who.team"), ("from", "Human.Team"));
			await Fm.Get("pathset", ("to", "fmcli.who.human"), ("from", "Human"));
		}
		catch (FmError) { }
		for (var i = 0; i < results.Count; i++)
			if (Field(results[i], "Path") is { Length: > 0 } path)
				try { await Fm.Get("pathset", ("to", $"fmcli.who.p{i}"), ("from", path)); } catch (FmError) { }
		await Until(async () =>
		{
			for (var i = 0; i < results.Count; i++)
			{
				if (Field(results[i], "Path").Length == 0 || read[i]?.GetValueOrDefault("Age") is { Length: > 0 }) continue;
				try
				{
					var person = await Fm.GetJson("pull", ("path", $"fmcli.who.p{i}"), ("props", "Age,PositionCombinedString,JobString"));
					var nation = await Fm.GetJson("pull", ("path", $"fmcli.who.p{i}.Nation"), ("props", "ThreeLetterName"));
					read[i] = new() { ["Age"] = Field(person, "Age"), ["Positions"] = Field(person, "PositionCombinedString"), ["Job"] = Field(person, "JobString"), ["Nation"] = Field(nation, "ThreeLetterName") };
				}
				catch (FmError) { }
			}
			return read.Select((r, i) => Field(results[i], "Path").Length == 0 || r?.GetValueOrDefault("Age") is { Length: > 0 }).All(x => x);
		}, seconds: 4, every: 0.5);

		return results.Select((r, i) => string.Join(" · ", new[]
		{
			read[i]?.GetValueOrDefault("Age") is { Length: > 0 } age ? L($"aged {age}") : "",
			// A coach's positions are from his playing days and would read as his job.
			Field(r, "Kind") == "Player" ? read[i]?.GetValueOrDefault("Positions") ?? "" : read[i]?.GetValueOrDefault("Job") ?? "",
			read[i]?.GetValueOrDefault("Nation") ?? "",
			Field(r, "Club"),
		}.Where(s => s.Length > 0))).ToList();
	}

	/// <summary>
	/// Types a name into the game's search and returns what it found. The search box only takes
	/// text typed into it, and its results arrive a moment after.
	/// </summary>
	private static async Task<List<JsonElement>> SearchFor(string text)
	{
		var boxes = (await Fm.GetJson("textboxes")).EnumerateArray().ToList();
		if (!boxes.Any(b => Field(b, "Type") == "SearchBox"))
		{
			// The button is addressed by the panel it opens and its template name, not by the
			// word it shows: the label is the game's own string and differs by language.
			var opened = await Arriving("press", [("event", "OpenPanel"), ("name", "dashboard-hud-search-button")]);
			if (opened.StartsWith("error", StringComparison.Ordinal)) throw new FmError(opened);
			if (!await Until(async () => (boxes = (await Fm.GetJson("textboxes")).EnumerateArray().ToList())
				    .Any(b => Field(b, "Type") == "SearchBox"), seconds: 10, every: 0.25))
				boxes = [];
		}
		// The words the box already holds are what the results on screen answer. The game searches
		// as they are typed, so asking for the same words again needs no new answer.
		var words = Field(boxes.FirstOrDefault(b => Field(b, "Type") == "SearchBox"), "Text");

		// An earlier search's results stay up, marked finished, until the game starts on the new
		// text. A box just opened binds them a moment late, so the list is read until two readings
		// agree — the game's answer rather than a pause. A box with no search yet answers nothing
		// and has no flag: the two equal readings are enough then.
		var before = "?";
		await Until(async () =>
		{
			var now = (await Fm.GetJson("search")).ToString();
			var settled = now == before;
			before = now;
			return settled && await Found("SearchIsFinished") != "False";
		}, seconds: 10, every: 0.25);

		var typed = await Fm.Get("type", ("name", "SearchBox"), ("text", text));
		if (typed.StartsWith("error", StringComparison.Ordinal)) throw new FmError(typed);

		// Results for the words before can still be showing: the game says a new search has started
		// by clearing the list and dropping "finished", and says it is done when the flag returns
		// with the list no longer changing. Searching for the words the box already had means the
		// list is already their answer, so no new search is waited on.
		var sameWords = Fold(words) == Fold(text);
		List<JsonElement> results = [];
		var last = "?";
		var started = false;
		var typedAt = DateTime.UtcNow;
		await Until(async () =>
		{
			var found = await Fm.GetJson("search");
			var now = found.ToString();
			var finished = await Found("SearchIsFinished") != "False";
			if (!finished || found.GetArrayLength() == 0) started = true;
			// The flag returns before the rows are drawn, so an empty list says nothing: only a
			// list with rows is an answer, and an empty one is believed after a generous while.
			var answer = finished && now == last
			             && (found.GetArrayLength() > 0 && (sameWords || started || now != before)
			                 || DateTime.UtcNow - typedAt > TimeSpan.FromSeconds(3));
			last = now;
			results = found.EnumerateArray().ToList();
			return answer;
		}, seconds: 12, every: 0.15);
		return results;
	}

	private static async Task<int> Search(bool json, string[] args)
	{
		var text = string.Join(" ", Words(args));
		if (text.Length == 0)
		{
			Console.Error.WriteLine(L("usage: search <text>"));
			return 64;
		}
		if (await NotNow() is { } refused) return refused;

		var results = await SearchFor(text);
		if (json)
		{
			Console.WriteLine("[" + string.Join(",", results.Select(r => r.ToString())) + "]");
			return 0;
		}

		var who = await Who(results);
		Table.Write([L("TYPE"), L("NAME"), L("WHO")], results.Select((r, i) => new[]
		{
			Field(r, "Kind"),
			Field(r, "Name") is { Length: > 0 } person ? person : Field(r, "Club") + Field(r, "Competition") + Field(r, "Nation"),
			Field(r, "Name").Length > 0 ? who[i] : "",
		}).ToList());
		return 0;
	}

	/// <summary>
	/// What can be done about a player, as his page offers it behind his value: shortlist,
	/// scout, enquire, bid, and the rest. At a terminal one is chosen with the arrows; a bid
	/// asks for the fee and goes the way `offer` goes.
	/// </summary>
	private static async Task<int> PlayerActions(JsonElement player)
	{
		var name = Field(player, "Name");
		if ((await TalkOptions()).Count == 0)
		{
			var opened = await Arriving("press", [("text", Field(player, "Value")), ("name", "SIButton"), ("any", "1")]);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => (await TalkOptions()).Count > 0, seconds: 8);
		}

		var options = await TalkOptions();
		if (options.Count == 0)
		{
			Console.Error.WriteLine(L($"No actions found for {name}."));
			return 1;
		}

		var labels = options.Select(o => o.Heading is { Length: > 0 } heading ? $"{heading} › {o.Label}" : o.Label).ToList();
		if (!Ui.Interactive)
		{
			for (var i = 0; i < labels.Count; i++) Console.WriteLine($"  {i + 1,2}  {labels[i]}");
			Console.WriteLine(L("\nTo do it: talk <number>   (for an offer: offer <player> <fee>)"));
			return 0;
		}

		if (Ui.Ask(L($"{name}: what shall we do?"), labels, cancel: L("Nothing")) is not { } chosen) return 0;

		// The offer opens on its card, where the fee is set with the other terms.
		if (options[chosen].Label == "Make An Offer") return await Offer(["offer", name]);

		return await TalkTo(false, ["talk", (chosen + 1).ToString()]);
	}

	/// <summary>
	/// Brings up a player's page and returns what it binds, or null having said why not.
	///
	/// Every page is reached with the game's search, whose player results each open that
	/// player's page; the squad's names are read from the game's data, with no screen opened,
	/// so a part of one of ours is completed before searching. The page is opened the way a
	/// user opens it, so its actions are on screen too.
	/// </summary>
	private static async Task<JsonElement?> OpenPlayer(string wanted)
	{
		var card = await Fm.GetJson("player");
		if (card.TryGetProperty("Player", out var open) && Fold(Field(open, "Name")).Contains(Fold(wanted), StringComparison.Ordinal))
			return await Named(card);

		var folded = Fold(wanted);
		var squad = (await Fm.Pull("Human.Team.Players", "Name", list: true)).EnumerateArray()
			.Select(p => Field(p, "Name")).Where(n => n.Length > 0).Distinct().ToList();
		var own = squad.Where(n => Fold(n).Contains(folded, StringComparison.Ordinal)).ToList();
		// A typo of one of ours — unless someone elsewhere has the name as it was typed.
		if (own.Count == 0 && Matching(squad, n => n, wanted) is { Count: > 0 } typo
		    && !(await SearchFor(wanted)).Any(r => Field(r, "Kind") == "Player" && Fold(Field(r, "Name")).Contains(folded, StringComparison.Ordinal)))
			own = typo;

		string full;
		var fromSquad = false;
		if (own.Count > 0)
		{
			if (Choose(own, wanted, L("Player")) is not { } mine) return null;
			full = mine;
			if (!(await SearchFor(full)).Any(r => Field(r, "Kind") == "Player" && Fold(Field(r, "Name")) == Fold(full)))
			{
				// The game's search lists only its first few hits, and a short name like "Fred"
				// can be left out; a player of ours is then opened from the squad's own table, where
				// his name can show twice (the page and the one beneath), both opening him.
				var opened = await OpenPage(Screen.Squad);
				var pressedName = opened.StartsWith("error", StringComparison.Ordinal) ? opened
					: await Arriving("press", [("text", full), ("name", "NameButton"), ("any", "1")]);
				if (pressedName.StartsWith("error", StringComparison.Ordinal))
				{
					Console.Error.WriteLine(L($"{full} is in the squad, but could be opened neither in search nor in the squad table."));
					return null;
				}
				fromSquad = true;
			}
		}
		else
		{
			// The game's search is loose — "icardi" also finds a Vicente — so its results are
			// narrowed to the names that really contain what was typed, or are a typo away.
			// It finds nothing for a typo, though, so then the name's start is searched for.
			var found = (await SearchFor(wanted)).Where(r => Field(r, "Kind") == "Player").ToList();
			var close = Matching(found, r => Field(r, "Name"), wanted);
			for (var start = 4; close.Count == 0 && start >= 3 && wanted.Trim().Length > start; start--)
			{
				var wider = (await SearchFor(wanted.Trim()[..start])).Where(r => Field(r, "Kind") == "Player").ToList();
				if ((close = Matching(wider, r => Field(r, "Name"), wanted)).Count > 0) found = wider;
			}
			// A whole name, or else one whose words start the way it was typed, outranks a match
			// inside a word.
			var exact = close.Where(r => Fold(Field(r, "Name")) == folded).ToList();
			var starting = close.Where(r => (" " + Fold(Field(r, "Name"))).Contains(" " + folded, StringComparison.Ordinal)).ToList();
			if (exact.Count == 1) close = exact;
			else if (starting.Count > 0) close = starting;
			if (close.Count != 1)
			{
				Console.Error.WriteLine(found.Count == 0
					? L($"No player called '{wanted}' in the squad or in search.")
					: close.Count == 0
						? L($"'{wanted}' did not come up exactly in search; the closest:")
						: L($"'{wanted}' matches {close.Count} players in search; type a bit more of the name:"));
				var hits = close.Count == 0 ? found : close;
				var who = await Who(hits);
				for (var i = 0; i < hits.Count; i++) Console.Error.WriteLine($"  {Field(hits[i], "Name")}  ({who[i]})");
				return null;
			}
			full = Field(close[0], "Name");
		}

		// Each result has a button of its own; the plugin finds the one fed from this result's
		// row, since an earlier search's buttons can still be loaded.
		if (!fromSquad)
		{
			var pressed = await Arriving("searchopen", [("name", full)]);
			if (pressed.StartsWith("error", StringComparison.Ordinal)) { Report(pressed); return null; }
		}

		// Pages opened earlier stay loaded beneath, so his is asked for by name.
		if (!await Until(async () => (card = await Fm.GetJson("player", ("name", full))).TryGetProperty("Player", out _), seconds: 15, every: 0.25))
		{
			Console.Error.WriteLine(L($"The player page did not open for {full}."));
			return null;
		}

		// The name arrives before the rest: the attributes, the contract and the career bind a
		// moment later, the attributes a few at a time, so their count has to hold still.
		var counted = -1;
		await Until(async () =>
		{
			var now = await Fm.GetJson("player", ("name", full));
			if (now.TryGetProperty("Player", out _)) card = now;
			var count = card.GetProperty("Attributes").GetArrayLength();
			var still = count == counted;
			counted = count;
			return count > 0 && still && Field(card.GetProperty("Player"), "TotalCareerLeagueAppearances").Length > 0;
		}, seconds: 5, every: 0.25);
		return await Named(card);

		// The names are the game's English ones, picked by the attribute's number rather than by the
		// word the page happens to bind (a Turkish page can bind another attribute's word); the game
		// also has its own words for them, and those are what it shows. Learn them once per run.
		static async Task<JsonElement> Named(JsonElement card)
		{
			card = WithAttributeNames(card);
			if (card.TryGetProperty("Attributes", out var attributes))
				await GameWords.Ask(Fm, [.. attributes.EnumerateArray().Select(a => Field(a, "Name")).Where(n => n.Length > 0).Distinct()]);
			return card;
		}
	}
}
