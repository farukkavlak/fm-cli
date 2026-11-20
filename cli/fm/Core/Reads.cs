using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>Read-only views: the league table and the fixture list.</summary>
internal static partial class Program
{
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
		// session, and only loading the save again brought time back (Stevenage, 18 April 2026).
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
	/// Shuts the game's search window when it is up, by its own close button (the window's page, the ClosePanel
	/// it sends); done when its box is gone. A command that found someone with it and then stopped leaves it over
	/// the page otherwise (15 Nov: `actions` with an entry it had not, the window stayed up).
	/// </summary>
	internal static async Task CloseSearch()
	{
		static bool Box(JsonElement b) => Field(b, "Type") == "SearchBox";
		if (!(await Fm.GetJson("textboxes")).EnumerateArray().Any(Box)) return;
		var close = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Page") == Screen.Search && Field(b, "Type") == "SIButton" && Field(b, "Visible") == "True"
				&& Field(b, "Events").Split(", ").Contains("ClosePanel"));
		if (close.ValueKind != JsonValueKind.Object) return;
		await Fm.Get("press", ("ptr", Field(close, "Pointer")));
		await Until(async () => !(await Fm.GetJson("textboxes")).EnumerateArray().Any(Box), seconds: 5, every: 0.25);
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
			// A screen without the menu (the news pages after a match) has no search button and no pages:
			// that is said, with the way past it, rather than the button not being found.
			if (opened.StartsWith("error", StringComparison.Ordinal))
				throw new FmError((await Fm.GetJson("pages")).GetArrayLength() == 0 ? await PagesClosed() : opened);
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

		// Typed into the search window's own box by its pointer: a page can hold a box of the same element
		// name (the shortlist's filter), and the name alone then fits both.
		var box = Field(boxes.FirstOrDefault(b => Field(b, "Type") == "SearchBox"), "Pointer");
		var typed = await Fm.Get("type", box.Length > 0 ? ("ptr", box) : ("name", "SearchBox"), ("text", text));
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
		var reading = Guid.NewGuid().ToString("N")[..8];
		await Until(async () =>
		{
			var found = await Fm.GetJson("search");
			var now = found.ToString();
			var finished = await Found("SearchIsFinished") != "False";
			// How many results the game's search holds (game.Search.Results): the rows on screen are drawn
			// from it a moment later, and the window shows only the first few.
			var held = finished ? await SearchResultsHeld(reading) : -1;
			if (!finished || found.GetArrayLength() == 0) started = true;
			// Only the game's sign that the new search has run (it started, or the list is another) makes
			// a list the answer: the words before's list stays up, marked finished, until the game starts on
			// the new text, and after a load that can take seconds (it was taken for the answer: "not found").
			// The flag returns before the rows are drawn, so an empty list from the new search is believed
			// only after a generous while.
			// A row whose name the game has not bound yet (Kind Pending) is not an answer: its name is waited for.
			var pending = found.EnumerateArray().Any(r => Field(r, "Kind") == "Pending");
			// Rows are the answer once the game has drawn them; no rows, only when its search holds none.
			var answer = finished && now == last && !pending && (sameWords || started || now != before)
			             && (found.GetArrayLength() > 0 ? held != 0 : held == 0);
			last = now;
			results = found.EnumerateArray().ToList();
			if (Environment.GetEnvironmentVariable("FM_SEARCHTRACE") == "1")
				Console.Error.WriteLine($"SEARCH t={(DateTime.UtcNow - typedAt).TotalSeconds:0.00} rows={found.GetArrayLength()} finished={finished} started={started} same={sameWords} changed={now != before} held={held} answer={answer} names=[{string.Join("|", results.Select(r => Field(r, "Name") + Field(r, "Club") + Field(r, "Competition") + Field(r, "Nation")))}]");
			return answer;
		}, seconds: 12, every: 0.15);
		if (Environment.GetEnvironmentVariable("FM_SEARCHTRACE") == "1")
			Console.Error.WriteLine($"SEARCH typed='{text}' box-before='{words}' typed-answer={typed.Trim()} before={before}");

		results.RemoveAll(r => Field(r, "Kind") == "Pending");
		// A person is a player or staff by his record's IsPlayer, the same in every language (the
		// kind the row shows is the game's word for it).
		var people = results.Where(r => Field(r, "Kind") == "Person" && Field(r, "Path").Length > 0).ToList();
		if (people.Count == 0) return results;
		var players = await PullEach(people.Select(r => (Field(r, "Path"), "IsPlayer")).ToList(), r => Field(r, "IsPlayer").Length > 0);
		var isPlayer = people.Zip(players).ToDictionary(p => Field(p.First, "Path"), p => Field(p.Second, "IsPlayer"));
		return results.Select(r =>
		{
			if (!isPlayer.TryGetValue(Field(r, "Path"), out var player) || player.Length == 0) return r;
			var node = System.Text.Json.Nodes.JsonNode.Parse(r.GetRawText())!.AsObject();
			node["Kind"] = player == "True" ? "Player" : "Staff";
			return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
		}).ToList();
	}

	/// <summary>The number of results the game's search holds, read from a row of ours (its own path is too long to pull under); -1 unread.</summary>
	private static async Task<int> SearchResultsHeld(string reading)
	{
		var found = await Fm.Get("find", ("name", "SearchIsFinished"), ("limit", "1"));
		var line = found.Split('\n')[0].Trim();
		if (!line.EndsWith(".SearchIsFinished", StringComparison.Ordinal)) return -1;
		var row = $"fmcli.srch{reading}";
		if ((await Fm.Get("pathset", ("to", row), ("from", line[..^".SearchIsFinished".Length]))).StartsWith("error", StringComparison.Ordinal)) return -1;
		var list = await Fm.GetJson("pull", ("path", row + ".Results"), ("props", "SearchRecordType"), ("list", "1"), ("gen", reading));
		return list.ValueKind == JsonValueKind.Array ? list.GetArrayLength() : -1;
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
			Field(r, "Shown"),
			// Anything else under its kind: Club, Competition, Nation, NationalTeam, Award.
			Field(r, "Name") is { Length: > 0 } person ? person : Field(r, Field(r, "Kind")),
			Field(r, "Name").Length > 0 ? who[i] : "",
		}).ToList());
		return 0;
	}

	/// <summary>
	/// The game's id of the person at each path ("" where none is read). A page's or a search
	/// row's person cannot have more bound beneath it where it sits, so each is put at a node
	/// of ours and read there; the game fills the value on a later reading.
	/// </summary>
	private static async Task<List<string>> RecordIds(List<string> paths)
	{
		var ids = paths.Select(_ => "").ToList();
		for (var i = 0; i < paths.Count; i++)
			if (paths[i].Length > 0)
				try { await Fm.Get("pathset", ("to", $"fmcli.record.p{i}"), ("from", paths[i])); } catch (FmError) { }
		await Until(async () =>
		{
			for (var i = 0; i < paths.Count; i++)
				if (paths[i].Length > 0 && ids[i].Length == 0)
					try { ids[i] = Field(await Fm.GetJson("pull", ("path", $"fmcli.record.p{i}"), ("props", "UniqueId")), "UniqueId"); }
					catch (FmError) { }
			return ids.Select((v, i) => paths[i].Length == 0 || v.Length > 0).All(x => x);
		}, seconds: 4, every: 0.25);
		return ids;
	}

	/// <summary>
	/// Brings up a player's page and returns what it binds, or null having said why not.
	///
	/// Every page is reached with the game's search, whose player results each open that
	/// player's page; the squad's names are read from the game's data, with no screen opened,
	/// so a part of one of ours is completed before searching. The page is opened the way a
	/// user opens it, so its actions are on screen too.
	/// </summary>
	/// <summary>
	/// The players of the club's youth sides, by the club's own references to them (YouthTeam, HighestLevelYouthTeam):
	/// each is set at a node of ours and its list read there, once every row has a name.
	/// </summary>
	private static async Task<List<JsonElement>> YouthPlayers()
	{
		var found = new List<JsonElement>();
		foreach (var (team, i) in new[] { "Human.Club.YouthTeam", "Human.Club.HighestLevelYouthTeam" }.Select((t, i) => (t, i)))
		{
			try
			{
				if (Field(await Fm.Pull(team, "UniqueId"), "UniqueId").Length == 0) continue;
				await Fm.Get("pathset", ("to", $"fmcli.youth{i}"), ("from", team));
				List<JsonElement> rows = [];
				await Until(async () =>
				{
					var read = await Fm.GetJson("pull", ("path", $"fmcli.youth{i}.Players"), ("props", "Name,UniqueId"), ("list", "1"));
					rows = read.ValueKind == JsonValueKind.Array ? read.EnumerateArray().ToList() : [];
					return rows.Count > 0 && rows.All(r => Field(r, "Name").Length > 0 && Field(r, "UniqueId").Length > 0);
				}, seconds: 4, every: 0.25);
				found.AddRange(rows.Where(r => Field(r, "Name").Length > 0 && found.All(f => Field(f, "UniqueId") != Field(r, "UniqueId"))));
			}
			catch (FmError) { }
		}
		return found;
	}

	private static async Task<JsonElement?> OpenPlayer(string wanted)
	{
		// "Charlie Jones #12345": the game's own id picks one of several with the same name, as the list of
		// them prints it; nothing else (club, age, order) tells them apart in every save.
		var idMark = Regex.Match(wanted, @"\s*#(\d+)\s*$");
		var wantedId = idMark.Success ? idMark.Groups[1].Value : "";
		if (idMark.Success) wanted = wanted[..idMark.Index];
		var folded = Fold(wanted);
		// Just after a load the squad can read as no one, or as ids without names (24 Jun 2032): the player was then
		// looked for in search alone, which found nobody, or someone else of a like name. A club always has a
		// squad: it is waited for until there is one with every name; one reading keeps its rows while it waits
		// for the names. The seconds are only upper bounds.
		List<JsonElement> all = [];
		await Until(async () =>
		{
			all = (await Fm.Pull("Human.Team.Players", "Name,UniqueId", list: true, seconds: 5)).EnumerateArray().ToList();
			return all.Count > 0 && all.All(p => Field(p, "Name").Length > 0);
		}, seconds: 15, every: 0.25);
		var records = all.Where(p => Field(p, "Name").Length > 0).ToList();
		// Ours in the youth sides too: "Yunus" is ours when the under-19s have him, and search alone found two others.
		records.AddRange((await YouthPlayers()).Where(y => records.All(r => Field(r, "UniqueId") != Field(y, "UniqueId"))));
		var squad = records.Select(p => Field(p, "Name")).Distinct().ToList();
		var own = squad.Where(n => Fold(n).Contains(folded, StringComparison.Ordinal)).ToList();

		// A page already open is his when it shows the record asked for: a player of ours is known
		// by his id, as someone elsewhere can have his name ("Ederson").
		var card = await Fm.GetJson("player");
		if (card.TryGetProperty("Player", out var open) && Fold(Field(open, "Name")).Contains(folded, StringComparison.Ordinal)
		    && (wantedId.Length == 0 || (await RecordIds([Field(open, "Path")]))[0] == wantedId))
		{
			var ours = records.Where(p => Fold(Field(p, "Name")) == Fold(Field(open, "Name"))).Select(p => Field(p, "UniqueId")).ToList();
			if (own.Count == 0 || ours.Contains((await RecordIds([Field(open, "Path")]))[0])) return await Named(card);
		}
		// A typo of one of ours — unless someone elsewhere has the name as it was typed.
		if (own.Count == 0 && Matching(squad, n => n, wanted) is { Count: > 0 } typo
		    && !(await SearchFor(wanted)).Any(r => Field(r, "Kind") == "Player" && Fold(Field(r, "Name")).Contains(folded, StringComparison.Ordinal)))
		{
			// One of ours only a typo away is offered, not opened, as one in search is (T141): two of a like name
			// can be in the squad, and the one typed may be neither. Several are listed by Choose below.
			var typed = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (typo is [var only] && !typed.All(w => Fold(only).Split(' ', '-', '\'').Any(x => x.StartsWith(w, StringComparison.Ordinal))))
			{
				if (!(Ui.Interactive && Ui.Ask(L($"No player called '{wanted}'. Did you mean {only} (our squad)?"), [L($"Open {only}")], cancel: L("No")) == 0))
				{
					Console.Error.WriteLine(L($"No player called '{wanted}'. The closest name in our squad: {only}; to open him: player \"{only}\""));
					return null;
				}
			}
			own = typo;
		}

		string full;
		// The search result to open, by its person's path, and for one of ours his id.
		var result = "";
		var id = "";
		var fromSquad = false;
		if (own.Count > 0)
		{
			if (Choose(own, wanted, L("Player")) is not { } mine) return null;
			full = mine;
			id = Field(records.First(p => Field(p, "Name") == full), "UniqueId");
			var named = (await SearchFor(full)).Where(r => Field(r, "Kind") == "Player" && Fold(Field(r, "Name")) == Fold(full)).ToList();
			var ids = await RecordIds([.. named.Select(r => Field(r, "Path"))]);
			result = named.Zip(ids).Where(p => p.Second == id).Select(p => Field(p.First, "Path")).FirstOrDefault() ?? "";
			if (result.Length == 0)
			{
				// The game's search lists only its first few hits, and a short name like "Fred"
				// can be left out; a player of ours is then opened from the squad's own table: the
				// name link in the row whose item is his record (his name links twice there).
				var opened = await OpenPage(Screen.Squad);
				var link = "";
				if (!opened.StartsWith("error", StringComparison.Ordinal))
					await Until(async () =>
					{
						var links = await Fm.GetJson("rowlinks", ("gen", Environment.TickCount64.ToString()));
						foreach (var row in links.EnumerateArray())
							if (Field(row, "Item") is { Length: > 0 } item && Field(await Fm.Pull(item, "UniqueId"), "UniqueId") == id)
							{
								link = Field(row, "Pointer");
								return true;
							}
						return false;
					}, seconds: 8, every: 0.4);
				var pressedName = link.Length == 0 ? "error: no row of the squad table is his"
					: await Fm.Get("rowlinks", ("ptr", link));
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
			// narrowed to the names that contain what was typed, or are a typo away.
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
			List<string> closeIds = [];
			if (close.Count > 1)
			{
				closeIds = await RecordIds([.. close.Select(r => Field(r, "Path"))]);
				if (wantedId.Length > 0)
				{
					var byId = close.Where((_, i) => closeIds[i] == wantedId).ToList();
					if (byId.Count == 0)
					{
						Console.Error.WriteLine(L($"No player called '{wanted}' has the id {wantedId}."));
						wantedId = "";
					}
					else close = byId;
				}
				// The same name more than once: typing more of it cannot tell them apart.
				else if (Ui.Interactive && close.Select(r => Fold(Field(r, "Name"))).Distinct().Count() == 1)
				{
					var whom = await Who(close);
					if (Ui.Ask(L($"'{wanted}' is the name of {close.Count} players:"), [.. close.Select((r, i) => $"{Field(r, "Name")}  ({whom[i]})")], cancel: L("None")) is not { } picked)
						return null;
					close = [close[picked]];
				}
			}
			// One name only a typo away from what was typed is someone else as often as a typo: "Yunus Eker", ours,
			// came up as another club's Yunus Emre Konak while the squad had not been read yet (T141). It is
			// offered, not opened: asked in a terminal, said otherwise.
			var typedWords = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			bool Holds(JsonElement r)
			{
				var name = Fold(Field(r, "Name"));
				var words = name.Split(' ', '-', '\'');
				return name.Contains(folded, StringComparison.Ordinal)
				       || typedWords.All(w => words.Any(x => x.StartsWith(w, StringComparison.Ordinal)));
			}
			if (close.Count == 1 && !Holds(close[0]))
			{
				var guess = Field(close[0], "Name");
				var whose = (await Who(close))[0];
				if (!(Ui.Interactive && Ui.Ask(L($"No player called '{wanted}'. Did you mean {guess} ({whose})?"), [L($"Open {guess}")], cancel: L("No")) == 0))
				{
					Console.Error.WriteLine(L($"No player called '{wanted}'. The closest name in search: {guess} ({whose}); to open him: player \"{guess}\""));
					return null;
				}
			}
			if (close.Count != 1)
			{
				Console.Error.WriteLine(found.Count == 0
					? L($"No player called '{wanted}' in the squad or in search.")
					: close.Count == 0
						? L($"'{wanted}' did not come up exactly in search; the closest:")
						: L($"'{wanted}' matches {close.Count} players in search; type a bit more of the name, or add the id:"));
				var hits = close.Count == 0 ? found : close;
				var who = await Who(hits);
				// Each with the game's id, which is what picks one when names are the same.
				var hitIds = close.Count > 1 && closeIds.Count == close.Count ? closeIds : [];
				for (var i = 0; i < hits.Count; i++)
					Console.Error.WriteLine($"  {Field(hits[i], "Name")}{(i < hitIds.Count && hitIds[i].Length > 0 ? $" #{hitIds[i]}" : "")}  ({who[i]})");
				if (hitIds.Count > 0) Console.Error.WriteLine(L($"One of them by its id: player \"{Field(hits[0], "Name")} #{hitIds[0]}\""));
				return null;
			}
			full = Field(close[0], "Name");
			result = Field(close[0], "Path");
			// One of several with this name: his page is believed only once it shows his record, as for one of ours.
			if (closeIds.Count > 0) id = (await RecordIds([result]))[0];
		}

		// Each result has a button of its own; the plugin finds the one fed from this result's
		// row, since an earlier search's buttons can still be loaded.
		if (!fromSquad)
		{
			var pressed = await Arriving("searchopen", [("path", result)]);
			if (pressed.StartsWith("error", StringComparison.Ordinal)) { Report(pressed); return null; }
		}

		// Pages opened earlier stay loaded beneath, so his is asked for by name, and one of ours
		// is believed only once the page shows his record.
		var shown = "";
		if (!await Until(async () => (card = await Fm.GetJson("player", ("name", full))).TryGetProperty("Player", out var page)
		                             && (id.Length == 0 || (shown = (await RecordIds([Field(page, "Path")]))[0]) == id), seconds: 15, every: 0.25))
		{
			Console.Error.WriteLine(shown.Length > 0
				? L($"Another {full}'s page opened, not the one in the squad.")
				: L($"The player page did not open for {full}."));
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
		// word the page binds put back into English (a Turkish "Teknik" comes back "Technical"). What is
		// shown is the page's own word ("Shown"); the English is put into the game's words only where
		// the page gave none, since one English word can stand for several (German "Balance").
		static async Task<JsonElement> Named(JsonElement card)
		{
			card = WithAttributeNames(card);
			if (card.TryGetProperty("Attributes", out var attributes))
				await GameWords.Ask(Fm, [.. attributes.EnumerateArray().Where(a => Field(a, "Shown").Length == 0)
					.Select(a => Field(a, "Name")).Where(n => n.Length > 0).Distinct()]);
			return card;
		}
	}
}
