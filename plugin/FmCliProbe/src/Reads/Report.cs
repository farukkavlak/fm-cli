using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// Shaped answers for the CLI: the squad and the inbox as records rather than tree dumps.
///
/// The screens that hold this data sit under GUID-keyed panel paths that differ between runs,
/// so nothing is addressed by a fixed path. Instead the tree is searched for the shape we want —
/// a reference that has the fields filled in — which survives a relayout and a game update.
/// </summary>
internal static class Report
{
	private const int MaxNodes = 40000;

	public static string Squad() => People(squadOnly: true, teamless: false);

	/// <summary>
	/// Any table of people on screen — the player database, a shortlist — read the way the
	/// squad is, but without insisting that the named record itself carries a position: such
	/// tables keep even that in a neighbouring cell.
	/// </summary>
	/// <param name="teamless">Only rows of a list that no team is bound to (a search's results): a squad page
	/// opened before stays bound beside the search, and its rows would be read as found.</param>
	public static string People(bool teamless) => People(squadOnly: false, teamless);

	private static string People(bool squadOnly, bool teamless)
	{
		var players = new List<Dictionary<string, string>>();
		var byName = new Dictionary<string, Dictionary<string, string>>();

		foreach (var (path, node) in Walk())
		{
			// A squad row is a person whose name has been bound; the skeletal person objects
			// under MainTeam.Players never have one.
			if (node.Value?.DataType?.Name != "PersonReference") continue;

			// What /pull has put at nodes of its own is not a row of the screen: a squad read
			// that way stays bound and would turn up in every list of people after it (§8.92).
			if (path.StartsWith("fmcli.", StringComparison.Ordinal) || path.Contains(".fmcli.", StringComparison.Ordinal)) continue;

			var fields = Fields(node);
			if (!fields.TryGetValue("Name", out var name) || string.IsNullOrEmpty(name)) continue;

			// Which fields mark a row depends on the table's view: most carry the position, the
			// dynamics view only the shirt number. A name alone is not enough, since staff and
			// opponents are bound as named people too.
			if (!fields.ContainsKey("PositionCombinedString") &&
			    !fields.ContainsKey("SquadNumberBlankIfInvalid") &&
			    !(!squadOnly && (fields.ContainsKey("Age") || IsTableRow(node))) &&
			    !byName.ContainsKey(name)) continue;

			// Both squads hang under the same tab container, so the path cannot tell them apart;
			// the team the row list is bound to can.
			var team = TeamOf(node);
			if (team != null && teamless) continue;
			if (team != null) fields["Team"] = team;

			// A table spreads one player over many cells, each binding the same person for a
			// field or two, so the rest of the row is gathered from around the named node.
			if (node.Parent != null) Merge(fields, node.Parent);

			var nation = Child(node, "Nation");
			if (nation != null && Fields(nation).TryGetValue("Name", out var nationName))
				fields["Nation"] = nationName;

			// The same player can be bound by more than one list at once — the overview keeps
			// its tiles while a table is up — and each knows different things about him. One
			// list can hold a field still empty that another has filled.
			if (byName.TryGetValue(name, out var known))
			{
				foreach (var (key, value) in fields)
					if (!known.TryGetValue(key, out var had) || (string.IsNullOrEmpty(had) && !string.IsNullOrEmpty(value)))
						known[key] = value;
				continue;
			}

			byName[name] = fields;
			players.Add(fields);
		}

		var sb = new StringBuilder();
		sb.AppendLine("[");
		for (var i = 0; i < players.Count; i++)
			sb.AppendLine("  " + Json(players[i]) + (i < players.Count - 1 ? "," : ""));
		sb.AppendLine("]");
		return sb.ToString();
	}

	public static string Inbox(bool paths = false)
	{
		var messages = new List<Dictionary<string, string>>();
		var seen = new HashSet<string>();

		var lists = new HashSet<string>();
		foreach (var (path, found) in Walk())
		{
			if (!IsMessage(found)) continue;
			// FM27 binds the whole inbox, and its newest rows lie past the walk's node limit when
			// another page is still bound beside it (19 of 41, the oldest). The list a row sits
			// in (item › cell › message) is read whole instead.
			var list = found.Parent?.Parent?.Parent;
			var listPath = Up(path, 3);
			if (list != null && listPath != null && lists.Add(listPath))
				foreach (var (at, node) in Messages(list, listPath, depth: 3)) Message(at, node);
			else Message(path, found);
		}

		void Message(string path, IReadOnlyNode node)
		{
			var fields = Fields(node);
			if (!fields.TryGetValue("UniqueId", out var id) || !seen.Add(id)) return;
			// The title as the game shows it: a translated one ("Fitness tests") is otherwise put back into English.
			for (var c = node.FirstChild; c != null; c = c.NextSibling)
				if (c.Name == "NewsTitleString" && Text.ShownOf(c.Value) is { Length: > 0 } shown) fields["NewsTitleString"] = shown;

			// The sender lives beside the news item, not inside it, so it is picked up from the
			// surrounding node rather than the reference.
			var sender = Sibling(node, "Person") ?? Sibling(node, "Human");
			if (sender != null && Fields(sender).TryGetValue("Name", out var who)) fields["Sender"] = who;

			if (paths) fields["Path"] = path;
			messages.Add(fields);
		}

		var sb = new StringBuilder();
		sb.AppendLine("[");
		for (var i = 0; i < messages.Count; i++)
			// A title or a name is read to the user and pressed by (press?has=): as the game shows it.
			sb.AppendLine("  " + Json(messages[i], ["NewsTitleString", "Sender"]) + (i < messages.Count - 1 ? "," : ""));
		sb.AppendLine("]");
		return sb.ToString();
	}

	/// <summary>
	/// A message of the inbox list. The news site and a message left open bind news items of
	/// their own, and they are not messages waiting here: a row of the list is the one that knows
	/// whether it has been read (FM27 no longer names the list in the path).
	/// </summary>
	private static bool IsMessage(IReadOnlyNode node)
	{
		if (node.Value?.DataType?.Name != "NewsItemReference") return false;
		var fields = Fields(node);
		return fields.ContainsKey("NewsItemHasBeenRead")
			&& fields.TryGetValue("NewsTitleString", out var title) && !string.IsNullOrEmpty(title);
	}

	/// <summary>The messages under a node, down to the given depth, with their paths.</summary>
	private static IEnumerable<(string Path, IReadOnlyNode Node)> Messages(IReadOnlyNode node, string path, int depth)
	{
		if (IsMessage(node)) yield return (path, node);
		if (depth == 0) yield break;
		for (var c = node.FirstChild; c != null; c = c.NextSibling)
			foreach (var found in Messages(c, path + "." + c.Name, depth - 1)) yield return found;
	}

	/// <summary>A path with its last parts taken off, or null when it is not that deep.</summary>
	private static string Up(string path, int parts)
	{
		for (var i = 0; i < parts; i++)
		{
			var dot = path.LastIndexOf('.');
			if (dot < 0) return null;
			path = path[..dot];
		}
		return path;
	}

	/// <summary>
	/// The streamed list the Messages page draws, for scrolling it: by its element name, or by its type when it
	/// has no name (FM27's Messages page, 29 Sep 2029: unnamed, so the inbox read only the ten rows drawn).
	/// </summary>
	public static string InboxList()
	{
		foreach (var e in Buttons.Elements())
		{
			try
			{
				var name = e.name ?? "";
				if (name.Contains("MessagesStreamedObjectList", StringComparison.Ordinal) || e.GetIl2CppType().Name == "MessagesStreamedObjectList")
					// The list holds its scroll view; `scroll` looks for one above what it is given, so it is given
					// the scroll view's own content, which is inside it.
					return $"0x{(ScrollContent(e) ?? e).Pointer.ToInt64():x}";
			}
			catch { }
		}
		return "error: no inbox list";
	}

	/// <summary>The first `#unity-content-container` under an element: a Unity scroll view's own content.</summary>
	private static UnityEngine.UIElements.VisualElement ScrollContent(UnityEngine.UIElements.VisualElement root)
	{
		var queue = new Queue<UnityEngine.UIElements.VisualElement>();
		queue.Enqueue(root);
		while (queue.Count > 0)
		{
			var at = queue.Dequeue();
			if (at != root && at.name == "unity-content-container") return at;
			var hierarchy = at.hierarchy;
			for (var i = 0; i < hierarchy.childCount; i++) queue.Enqueue(hierarchy.ElementAt(i));
		}
		return null;
	}

	/// <summary>
	/// What the game's search found: each result's kind, and for a person the name and club.
	/// </summary>
	public static string SearchResults()
	{
		var results = new List<Dictionary<string, string>>();

		foreach (var (path, node) in Walk())
		{
			if (node.Name != "searchresult" || node.Parent == null) continue;

			var fields = new Dictionary<string, string>();
			// The kind as the game shows it ("Spieler" in a German game): for the eye only. Which
			// kind a result is goes by the record it carries, below.
			for (var c = node.FirstChild; c != null; c = c.NextSibling)
				if (c.Name == "SearchRecordType" && c.Value is { IsNull: false } kind) fields["Shown"] = kind.AsString() ?? "";

			// The result's record sits beside the row that shows it: a person with a club, or
			// just a named club, competition or nation.
			var queue = new Queue<(IReadOnlyNode Node, string Path)>();
			queue.Enqueue((node.Parent, path[..path.LastIndexOf('.')]));
			var seen = 0;
			while (queue.Count > 0 && seen++ < 300)
			{
				var (current, at) = queue.Dequeue();
				for (var c = current.FirstChild; c != null; c = c.NextSibling) queue.Enqueue((c, at + "." + c.Name));

				if (current.Name is not ("Person" or "Club" or "Competition" or "Nation")) continue;
				if (!Fields(current).TryGetValue("Name", out var name) || name.Length == 0) continue;
				// The person's path lets a caller pull what the row does not show (age, positions,
				// nation) to tell two of the same name apart.
				if (current.Name == "Person" && fields.TryAdd("Name", name)) fields["Path"] = at;
				// A club, competition or nation result's record path too: its own right-click menu is bound to it.
				else if (fields.TryAdd(current.Name, name)) fields[current.Name + "Path"] = at;
			}

			// A person (player or staff: the caller asks his IsPlayer by Path), else the record found.
			fields["Kind"] = fields.ContainsKey("Name") ? "Person"
				: fields.ContainsKey("Competition") ? "Competition" : fields.ContainsKey("Nation") ? "Nation" : "Club";
			if (fields.Count > 2) results.Add(fields);
			// A row the game has drawn before its record's name is bound: said, so a caller waits for its
			// name rather than take a list without it for the answer (it was read as "not found").
			else results.Add(new Dictionary<string, string> { ["Shown"] = fields.GetValueOrDefault("Shown", ""), ["Kind"] = "Pending" });
		}

		return "[\n  " + string.Join(",\n  ", results.Select(r => Json(r, ["Shown"]))) + "\n]\n";
	}

	/// <summary>
	/// Open the page of a person the search found, by pressing the button of that result's own
	/// row. Results from an earlier search can still be loaded with buttons of their own, so
	/// the button is matched to the row by where it takes its data from. The result is named by
	/// its person's path (?path=, from /search), which tells two people of one name apart, or by
	/// the name.
	/// </summary>
	public static string OpenSearchResult(string name, string person, bool dry)
	{
		if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(person))
			return "error: ?path=<result's Path> or ?name=<person's full name> required (see /search)";

		foreach (var (path, node) in Walk())
		{
			if (node.Name != "searchresult" || node.Parent == null) continue;

			var keys = new HashSet<ulong>();
			var matches = false;

			var queue = new Queue<(IReadOnlyNode Node, string Path)>();
			queue.Enqueue((node.Parent, path[..path.LastIndexOf('.')]));
			while (queue.Count > 0 && keys.Count < 600)
			{
				var (current, at) = queue.Dequeue();
				keys.Add(current.Key.m_key);
				for (var c = current.FirstChild; c != null; c = c.NextSibling) queue.Enqueue((c, at + "." + c.Name));

				if (current.Name != "Person") continue;
				if (!string.IsNullOrEmpty(person) ? at == person
				    : Fields(current).TryGetValue("Name", out var who) && who == name) matches = true;
			}
			if (!matches) continue;

			// The row is the result's parent, and the button takes its data from inside it
			// (…items.N.searchresult beside …items.N.1.PersonRemapper…).
			var pressed = Buttons.PressFedBy("PlayerSearchButton", keys, dry);
			if (!pressed.StartsWith("error: no control", StringComparison.Ordinal)) return pressed;
		}

		// A staff result has no searchresult node: its button is fed by the person's own remapper,
		// the parent of …PersonRemapper1.Person.
		if (!string.IsNullOrEmpty(person) && FmApi.NodeAt(person, out _) is { Parent: { } remapper })
		{
			var keys = new HashSet<ulong>();
			var queue = new Queue<IReadOnlyNode>();
			queue.Enqueue(remapper);
			while (queue.Count > 0 && keys.Count < 600)
			{
				var current = queue.Dequeue();
				keys.Add(current.Key.m_key);
				for (var c = current.FirstChild; c != null; c = c.NextSibling) queue.Enqueue(c);
			}
			var pressed = Buttons.PressFedBy("StaffSearchButton", keys, dry);
			if (!pressed.StartsWith("error: no control", StringComparison.Ordinal)) return pressed;
		}

		// A club, competition or nation result (…items.N.1.ClubRemapper1.Club): its button, named for the
		// record ("ClubSearchButton"), is fed by the remapper's parent.
		if (!string.IsNullOrEmpty(person) && FmApi.NodeAt(person, out _) is { Name: not "Person", Parent.Parent: { } holder } record)
		{
			var keys = new HashSet<ulong>();
			var queue = new Queue<IReadOnlyNode>();
			queue.Enqueue(holder);
			while (queue.Count > 0 && keys.Count < 600)
			{
				var current = queue.Dequeue();
				keys.Add(current.Key.m_key);
				for (var c = current.FirstChild; c != null; c = c.NextSibling) queue.Enqueue(c);
			}
			var pressed = Buttons.PressFedBy(record.Name + "SearchButton", keys, dry);
			if (!pressed.StartsWith("error: no control", StringComparison.Ordinal)) return pressed;
		}

		return $"error: no search result on screen opens '{(string.IsNullOrEmpty(person) ? name : person)}'";
	}

	/// <summary>
	/// The league table on screen, one record per row.
	///
	/// A row is a record holding the position and the points; the team's name and the rest of
	/// the columns are bound around it, and which columns there are depends on the table shown.
	/// </summary>
	public static string Table(bool groups = false)
	{
		// Several tables can be bound at once — the portal's tile and a competition's page —
		// and their rows share positions; rows are kept apart by the list they sit in (the path
		// less its last two steps). Asked for as one table, the first row at a position wins.
		var tables = new Dictionary<string, SortedDictionary<int, Dictionary<string, string>>>();
		var order = new List<string>();

		foreach (var (path, node) in Walk())
		{
			// Only a node with a LeaguePoints child is a row: its children's names are looked at before
			// any value is read (a player search left bound walks tens of thousands of nodes).
			if (!HasChild(node, "LeaguePoints")) continue;
			var fields = Fields(node);
			if (!fields.ContainsKey("LeaguePoints") || !fields.TryGetValue("TablePosition", out var raw)) continue;
			if (!int.TryParse(raw, out var position)) continue;

			var key = "";
			if (groups)
			{
				var steps = path.Split('.');
				key = steps.Length > 2 ? string.Join(".", steps, 0, steps.Length - 2) : path;
			}
			if (!tables.TryGetValue(key, out var rows))
			{
				tables[key] = rows = new SortedDictionary<int, Dictionary<string, string>>();
				order.Add(key);
			}
			if (rows.ContainsKey(position)) continue;

			if (node.Parent != null) Merge(fields, node.Parent);

			// The team's last results are bound under another cell of the same row — the form
			// column's — so they are looked for from the row's ancestors down, nearest first.
			if (!fields.ContainsKey("RecentFormValues"))
			{
				var hops = 0;
				for (var up = node.Parent; up != null && hops < 5; up = up.Parent, hops++)
				{
					if (FormUnder(up, 0) is not { } form) continue;
					fields["RecentFormValues"] = form;
					break;
				}
			}
			rows[position] = fields;
		}

		static string Rows(SortedDictionary<int, Dictionary<string, string>> rows) =>
			"[\n  " + string.Join(",\n  ", rows.Values.Select(Json)) + "\n]";

		if (!groups) return (tables.TryGetValue("", out var only) ? Rows(only) : "[\n  \n]") + "\n";
		return "[\n" + string.Join(",\n", order.Select(k => Rows(tables[k]))) + "\n]\n";
	}

	/// <summary>
	/// The fixtures on screen: when, where, against whom, in what, and the result once played.
	/// </summary>
	public static string Fixtures()
	{
		var fixtures = new List<Dictionary<string, string>>();
		var seen = new HashSet<string>();

		foreach (var (_, node) in Walk())
		{
			if (node.Name != "Fixture") continue;

			// One to come has a kick-off time, one already played a result instead.
			// Other records by this name carry neither and are only links to a fixture.
			var fields = Fields(node);
			fields.TryGetValue("ResultLinkableNoSpaces", out var result);
			if (!fields.ContainsKey("KickoffTime") && string.IsNullOrEmpty(result)) continue;
			if (!string.IsNullOrEmpty(result)) fields["Result"] = result;

			// A date is a packed struct, which the record builder leaves out as noise.
			var date = Child(node, "FixtureDate");
			string day = null;
			try { day = State.Date(date?.Value?.AsString())?.Day.ToString("yyyy-MM-dd"); } catch { }
			if (day != null) fields["Date"] = day;

			if (node.Parent != null) Merge(fields, node.Parent);

			// The same fixture is bound by every tile that shows it.
			fields.TryGetValue("ShortName", out var opponent);
			if (!seen.Add($"{day}|{opponent}")) continue;
			fixtures.Add(fields);
		}

		fixtures.Sort((a, b) => string.CompareOrdinal(a.GetValueOrDefault("Date", ""), b.GetValueOrDefault("Date", "")));
		return "[\n  " + string.Join(",\n  ", fixtures.Select(Json)) + "\n]\n";
	}

	/// <summary>The lines of the assistant's tactics evaluation (the rows of its CritiquesList), in the game's words.</summary>
	public static string Critiques()
	{
		var lines = new List<string>();
		foreach (var (path, node) in Walk())
			if (node.Name == "Value" && path.Contains("CritiquesList") && path.Contains(".items") && Text.ShownOf(node.Value) is { Length: > 0 } line)
				lines.Add($"\"{Escape(line)}\"");
		return "[" + string.Join(", ", lines) + "]\n";
	}

	/// <summary>
	/// The introduction tour on screen: which step it is on and what the guide is saying.
	///
	/// A tour is a conversation whose reference counts its steps; the speech bubbles hang beside
	/// it. Nothing bound means no tour is open, which is answered as an empty record.
	/// </summary>
	public static string Tour()
	{
		foreach (var (_, node) in Walk())
		{
			if (node.Name != "SpeakTo") continue;

			var fields = Fields(node);
			if (!fields.TryGetValue("CurrentInductionStepIndex", out var step)) continue;
			fields.TryGetValue("MaxInductionStepIndex", out var max);

			var bubbles = new SortedDictionary<int, string>();
			if (node.Parent != null) Bubbles(node.Parent, bubbles);

			return "{"
				+ $"\"Step\": \"{Escape(step)}\", \"Max\": \"{Escape(max ?? "")}\", "
				+ "\"Says\": [" + string.Join(", ", bubbles.Values.Select(b => $"\"{Escape(b)}\"")) + "]}\n";
		}
		return "{}\n";
	}

	/// <summary>The guide's lines in the order they were said; the list holds them by index.</summary>
	private static void Bubbles(IReadOnlyNode under, SortedDictionary<int, string> bubbles)
	{
		var queue = new Queue<IReadOnlyNode>();
		queue.Enqueue(under);
		var seen = 0;

		while (queue.Count > 0 && seen++ < 400)
		{
			var node = queue.Dequeue();
			for (var c = node.FirstChild; c != null; c = c.NextSibling) queue.Enqueue(c);

			if (node.Name != "bubble" || !int.TryParse(node.Parent?.Name, out var index)) continue;
			if (Spoken(node) is { } text) bubbles[index] = text;
		}
	}

	/// <summary>
	/// What a speech bubble says, in the game's own words: a line of speech is only shown, never
	/// matched, and put into English it came back half translated (the stock lines are in the table,
	/// those with names are not) and the CLI could not put it back.
	/// </summary>
	internal static string Spoken(IReadOnlyNode bubble)
	{
		for (var c = bubble.FirstChild; c != null; c = c.NextSibling)
			if (c.Name == "DisplayString")
			{
				var text = Text.ShownOf(c.Value);
				return text.Length > 0 ? text : null;
			}
		return null;
	}

	/// <summary>
	/// A table binds each row's person as "binding" beside the row's index, whatever columns
	/// the view in force puts around it.
	/// </summary>
	private static bool IsTableRow(IReadOnlyNode person) =>
		person.Name == "binding" && Sibling(person, "Index") != null;

	/// <summary>The name of the team a squad row belongs to, found by climbing to its list.</summary>
	private static string TeamOf(IReadOnlyNode node)
	{
		for (var ancestor = node.Parent; ancestor != null; ancestor = ancestor.Parent)
		{
			var team = Child(ancestor, "Team");
			if (team == null) continue;

			if (Fields(team).TryGetValue("Name", out var name) && !string.IsNullOrEmpty(name))
				return name;
		}
		return null;
	}

	/// <summary>
	/// Adds the values bound anywhere under a row, keyed by the name the game gives them.
	///
	/// Which columns exist depends on the view the table is showing, so nothing is asked for by
	/// name: whatever the row carries is taken, minus the layout's own bookkeeping, which is
	/// recognisable by its spelling.
	/// </summary>
	private static void Merge(Dictionary<string, string> fields, IReadOnlyNode row)
	{
		var queue = new Queue<IReadOnlyNode>();
		queue.Enqueue(row);
		var seen = 0;

		while (queue.Count > 0 && seen++ < 600)
		{
			var node = queue.Dequeue();
			for (var c = node.FirstChild; c != null; c = c.NextSibling) queue.Enqueue(c);

			var name = node.Name;
			if (string.IsNullOrEmpty(name)) continue;

			// A formatted value sits one level down, under the name of what it formats.
			if (name is "DisplayValue" or "PropertyValue") name = node.Parent?.Name;
			if (!IsData(name) || fields.ContainsKey(name)) continue;

			var text = Scalar(node, name);
			if (!string.IsNullOrEmpty(text)) fields[name] = text;
		}
	}

	private static bool IsData(string name)
	{
		if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0])) return false;

		foreach (var prefix in new[] { "Is", "Has", "Can", "Disable", "Use", "Dragged", "Highlight" })
			if (name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length && char.IsUpper(name[prefix.Length]))
				return false;

		return !name.EndsWith("Color", StringComparison.Ordinal) && !name.EndsWith("Index", StringComparison.Ordinal);
	}

	private static string Scalar(IReadOnlyNode node, string name)
	{
		try
		{
			var value = node.Value;
			if (value == null || value.IsNull) return null;

			var type = value.DataType?.Name ?? "";
			if (type.EndsWith("Reference", StringComparison.Ordinal) || type.StartsWith("List", StringComparison.Ordinal)
			    || type is "GameDate" or "Color32") return null;

			return Text.Field(name, value);
		}
		catch { return null; }
	}

	private static IReadOnlyNode Child(IReadOnlyNode node, string name)
	{
		for (var c = node.FirstChild; c != null; c = c.NextSibling)
			if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;

		return null;
	}

	/// <summary>A node's immediate string/number children, cleaned of embedded markup.</summary>
	private static string FormUnder(IReadOnlyNode node, int depth)
	{
		if (depth > 5) return null;
		for (var c = node.FirstChild; c != null; c = c.NextSibling)
		{
			try
			{
				if (c.Name == "RecentFormValues" && c.Value is { IsNull: false } value
				    && value.Get()?.TryCast<Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>>() is { } results)
				{
					var codes = new List<string>();
					for (var i = 0; i < results.Count; i++)
					{
						try { codes.Add(results[i] is { IsNull: false } code ? Text.Of(code) : ""); } catch { codes.Add("?"); }
					}
					return string.Join(",", codes);
				}
			}
			catch { }
			if (FormUnder(c, depth + 1) is { } found) return found;
		}
		return null;
	}

	private static bool HasChild(IReadOnlyNode node, string name)
	{
		try
		{
			for (var c = node.FirstChild; c != null; c = c.NextSibling)
				if (c.Name == name) return true;
		}
		catch { }
		return false;
	}

	internal static Dictionary<string, string> Fields(IReadOnlyNode node)
	{
		var fields = new Dictionary<string, string>();
		for (var c = node.FirstChild; c != null; c = c.NextSibling)
		{
			var name = c.Name;
			if (string.IsNullOrEmpty(name)) continue;

			string text;
			try
			{
				var value = c.Value;
				if (value == null || value.IsNull) continue;

				// A reference or a packed struct stringifies to its type name, which is noise in
				// a record; those are reached through their own fields instead.
				var type = value.DataType?.Name ?? "";
				if (type.EndsWith("Reference", StringComparison.Ordinal) || type == "GameDate") continue;

				text = Text.Field(name, value);

				// A team's recent results are a list of codes; as a string a list is only its
				// type's name, so this one is given as its entries; a message's tags (the game's category numbers) too.
				if ((name.StartsWith("RecentFormValues", StringComparison.Ordinal) || name == "TagsList")
				    && value.Get()?.TryCast<Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>>() is { } results)
				{
					var codes = new List<string>();
					for (var i = 0; i < results.Count; i++)
					{
						try { codes.Add(results[i] is { IsNull: false } code ? Text.Of(code) : ""); } catch { codes.Add("?"); }
					}
					text = string.Join(",", codes);
				}
			}
			catch { continue; }

			if (!string.IsNullOrEmpty(text)) fields[name] = text;
		}
		return fields;
	}

	private static IReadOnlyNode Sibling(IReadOnlyNode node, string name)
	{
		var parent = node.Parent;
		if (parent == null) return null;

		for (var c = parent.FirstChild; c != null; c = c.NextSibling)
			if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;

		return null;
	}

	internal static IEnumerable<(string Path, IReadOnlyNode Node)> Walk()
	{
		var bindings = Game.Bindings;
		if (bindings == null) yield break;

		var rootKey = Bindings.RootKey;
		var root = bindings.GetNode(ref rootKey);
		if (root == null) yield break;

		var queue = new Queue<(IReadOnlyNode Node, string Path)>();
		queue.Enqueue((root, ""));
		var seen = 0;

		while (queue.Count > 0 && seen++ < MaxNodes)
		{
			var (node, prefix) = queue.Dequeue();
			var path = prefix.Length == 0 ? node.Name ?? "" : prefix + "." + node.Name;

			yield return (path, node);

			// What /pull and the routes put at fmcli.* is ours, not the screen's: left out, so it does
			// not use up the node limit before the screen's own nodes are reached.
			for (var c = node.FirstChild; c != null; c = c.NextSibling)
				if (prefix.Length != 0 || c.Name != "fmcli") queue.Enqueue((c, path));
		}
	}

	internal static string Json(Dictionary<string, string> fields) => Json(fields, []);

	/// <summary>
	/// A record as JSON, its values in English where the game's table can say, except the fields named
	/// as shown and numbers: a number is not a word, and a German game's "6" is the text some English
	/// "F" is translated to (a squad number, a rating, an attribute's id came out "F").
	/// </summary>
	internal static string Json(Dictionary<string, string> fields, string[] shown) =>
		"{" + string.Join(", ", fields.Select(kv => $"\"{Escape(kv.Key)}\": \"{Escape(Array.IndexOf(shown, kv.Key) >= 0 || IsNumber(kv.Value) ? kv.Value : Localisation.English(kv.Value))}\"")) + "}";

	// Digits with what separates or ranges them ("6", "11-15", "1.250"), and nothing else.
	private static bool IsNumber(string value) =>
		!string.IsNullOrEmpty(value) && value.Any(char.IsDigit) && value.All(c => char.IsDigit(c) || c is '-' or '–' or '.' or ',' or ' ' or '/');

	internal static string Escape(string s) =>
		s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
}
