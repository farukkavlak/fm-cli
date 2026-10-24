using System.Text.Json;

namespace FmCli;

/// <summary>
/// The staff database (Recruitment › Staff Database, FINDINGS §8.116): the coaches, scouts and
/// physios the game knows, as its page lists them — job, contract, wage — with the page's own
/// ready filters (staff interested in joining) and views (coaching, medical, scouting ratings).
/// </summary>
internal static partial class Program
{
	private static async Task<int> StaffFind(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.StaffDatabase);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		JsonElement? views = null, filters = null;
		await Until(async () =>
		{
			var lists = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().ToList();
			views = lists.Where(d => Field(d, "Name") == "ViewAltDropdown").Cast<JsonElement?>().FirstOrDefault();
			filters = lists.Where(d => Field(d, "Name") == "inputs-combobox-default-default").Cast<JsonElement?>().FirstOrDefault();
			return views != null && filters != null;
		}, seconds: 12);
		if (views == null || filters == null)
		{
			Console.Error.WriteLine(L("Personel veritabanı sayfası beklenen listeleri göstermedi."));
			return 1;
		}
		var viewNames = views.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		var filterNames = filters.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		if (args.Contains("--filters")) { foreach (var name in filterNames) Console.WriteLine(name); return 0; }
		if (args.Contains("--views")) { foreach (var name in viewNames) Console.WriteLine(name); return 0; }

		var before = (await Fm.GetJson("people")).ToString();
		var asked = false;
		// The page's own Clear takes off what an earlier search left on.
		if (args.Contains("--clear"))
		{
			var clear = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Events").Contains("EventFmxResetFilter", StringComparison.Ordinal));
			if (clear.ValueKind == JsonValueKind.Object && Field(clear, "Enabled") == "True") await Fm.Get("click", ("ptr", Field(clear, "Pointer")));
			asked = true;
		}

		// Job, employment and reputation are set in the page's search panel ("Edit Search") and
		// applied; they stay on, like any search there, until cleared.
		var role = Option(args, "--role");
		var employed = args.Contains("--free") ? "No" : Option(args, "--employed");
		var reputation = Option(args, "--reputation");
		if (role != null || employed != null || reputation != null)
		{
			// Found by what it does: its label changes once a search is on ("Edit Search (2)").
			JsonElement edit = default;
			await Until(async () => (edit = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Events").Contains("EventFmxFilterStoreFilter", StringComparison.Ordinal))).ValueKind == JsonValueKind.Object, seconds: 6, every: 0.4);
			if (edit!.ValueKind != JsonValueKind.Object) return Report(L("error: sayfada arama paneli butonu yok"));
			var panel = await Fm.Get("click", ("ptr", Field(edit, "Pointer")));
			if (panel.StartsWith("error", StringComparison.Ordinal)) return Report(panel);
			// The panel's rules write to the club's own fields, whatever the language calls them,
			// so each is found by the path it binds to — not by a word among its choices
			// ("By Nation", "Medical ›": the game's lists are its own to rename).
			const string RoleRule = "filter.filterrulestaffrole.filtervalue";
			const string EmployedRule = "filter.filterruleemployed.filtervalue";
			const string ReputationRule = "Filter.FilterRuleWorldReputation.FilterValue";
			List<JsonElement> lists = [];
			await Until(async () =>
			{
				lists = (await Fm.GetJson("dropdowns", ("json", "1"), ("deep", "1"))).EnumerateArray().ToList();
				return lists.Any(d => Field(d, "Binds") == RoleRule)
					&& lists.Any(d => Field(d, "Binds") == EmployedRule)
					&& lists.Any(d => Field(d, "Binds") == ReputationRule);
			}, seconds: 8, every: 0.4);

			// A choice is in the rule once the rule's own list stands on it. Measured, the game
			// writes the choice as the select returns, so this waits only on a machine that lags;
			// the bound says when a choice has been lost rather than sleeping through it.
			async Task<bool> Landed(string binds, string choice) => await Until(async () =>
				Field((await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
					.FirstOrDefault(d => Field(d, "Binds") == binds), "Label") == choice, seconds: 6, every: 0.25);

			async Task<int> Cancel(int code)
			{
				// The panel's own Cancel sends the same event as Apply with "false"; the word on it
				// is this language's, so the payload decides and no word is ever pressed.
				if (await PanelSent(false) is { } cancel) await Fm.Get("click", ("ptr", cancel));
				return code;
			}

			if (role != null)
			{
				var list = lists.FirstOrDefault(d => Field(d, "Binds") == RoleRule);
				if (list!.ValueKind != JsonValueKind.Object) return await Cancel(Report(L("error: arama panelinde görev listesi yok")));
				// Jobs sit one level down, under the department they belong to.
				var top = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
				var jobs = new List<(string Department, string Job)>();
				if (list.TryGetProperty("Submenus", out var subs) && subs.ValueKind == JsonValueKind.Object)
					foreach (var sub in subs.EnumerateObject())
						if (int.TryParse(sub.Name, out var at) && at < top.Count)
							jobs.AddRange(sub!.Value.EnumerateArray().Select(j => (top[at]![..^2], j.GetString() ?? "")));
				if (Choose(jobs.Select(j => j.Job).ToList(), role, L("Görev")) is not { } job) return await Cancel(64);
				var picked = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("names", $"{jobs.First(j => j.Job == job).Department}>{job}"), ("fire", "1"));
				if (picked.StartsWith("error", StringComparison.Ordinal)) return await Cancel(Report(picked));
				if (!await Landed(RoleRule, job)) return await Cancel(Report(L($"error: '{job}' görev listesine oturmadı")));
			}
			if (employed != null)
			{
				var list = lists.FirstOrDefault(d => Field(d, "Binds") == EmployedRule);
				if (list!.ValueKind != JsonValueKind.Object) return await Cancel(Report(L("error: arama panelinde çalışma durumu listesi yok")));
				var choices = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
				if (Choose(choices, employed, L("Çalışma durumu")) is not { } choice) return await Cancel(64);
				var picked = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("choice", choice), ("fire", "1"));
				if (picked.StartsWith("error", StringComparison.Ordinal)) return await Cancel(Report(picked));
				if (!await Landed(EmployedRule, choice)) return await Cancel(Report(L($"error: '{choice}' çalışma durumu listesine oturmadı")));
			}
			if (reputation != null)
			{
				var list = lists.FirstOrDefault(d => Field(d, "Binds") == ReputationRule);
				if (list!.ValueKind != JsonValueKind.Object) return await Cancel(Report(L("error: arama panelinde itibar listesi yok")));
				var choices = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
				if (Choose(choices, reputation, L("İtibar")) is not { } choice) return await Cancel(64);
				var picked = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("choice", choice), ("fire", "1"));
				if (picked.StartsWith("error", StringComparison.Ordinal)) return await Cancel(Report(picked));
				if (!await Landed(ReputationRule, choice)) return await Cancel(Report(L($"error: '{choice}' itibar listesine oturmadı")));
			}
			var apply = await PanelSent(true);
			if (apply == null) return await Cancel(Report(L("error: arama panelinin Apply düğmesi bulunamadı")));
			var applied = await Fm.Get("click", ("ptr", apply));
			if (applied.StartsWith("error", StringComparison.Ordinal)) return await Cancel(Report(applied));
			asked = true;
		}

		if (Option(args, "--filter") is { } wantedFilter)
		{
			if (Choose(filterNames, wantedFilter, L("Süzgeç")) is not { } filter) return 1;
			var chosen = await Fm.Get("select", ("ptr", Field(filters.Value, "Pointer")), ("choice", filter), ("fire", "1"));
			if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
			asked = true;
		}
		if (Option(args, "--view") is { } wantedView)
		{
			if (Choose(viewNames, wantedView, L("Görünüm")) is not { } view) return 1;
			var chosen = await Fm.Get("select", ("ptr", Field(views.Value, "Pointer")), ("choice", view));
			if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
			asked = true;
		}

		// The rows refill a moment after a filter or view changes.
		JsonElement people = default;
		var last = "";
		var waited = 0;
		await Until(async () =>
		{
			var now = await Fm.GetJson("people");
			var text = now.ToString();
			var settled = text == last && now.GetArrayLength() > 0 && (!asked || text != before || ++waited >= 5);
			last = text;
			if (settled) people = now;
			return settled;
		}, seconds: 20);
		if (people.ValueKind != JsonValueKind.Array) people = await Fm.GetJson("people");

		// What is typed after `find` narrows what the page shows: a name, a job ("scout").
		var typed = Fold(string.Join(" ", Words(args).Skip(1)));
		var rows = people.EnumerateArray().Where(p => typed.Length == 0 || Fold(Field(p, "Name") + " " + Field(p, "JobString")).Contains(typed)).ToList();

		if (json)
		{
			Console.WriteLine(JsonSerializer.Serialize(rows));
			return 0;
		}
		if (rows.Count == 0)
		{
			Console.WriteLine(L("Sayfanın gösterdikleri arasında uyan yok."));
			return 0;
		}
		Columns(rows, L("KİŞİ"));
		Console.WriteLine(L($"\n{rows.Count} kişi (sayfanın gösterdiği kadarı). Daraltmak: --role <görev> · --free · --reputation <seviye> · --view <görünüm> · sıfırlamak: --clear"));
		return 0;
	}
	/// <summary>
	/// Sets lists in a database page's search panel ("Edit Search") and applies them: each is
	/// found by the heading it stands under (the second list of a pair by the word between them,
	/// "and"), chosen in, and waited for — a choice made before the last one has landed is lost.
	/// Null when applied; otherwise what went wrong, with the panel closed again.
	/// </summary>
	// The game's position names by the short ones written on the pitch.
	private static readonly Dictionary<string, string> PositionNames = new(StringComparer.OrdinalIgnoreCase)
	{
		["GK"] = "Goalkeeper", ["DR"] = "Defender (Right)", ["DL"] = "Defender (Left)", ["DC"] = L("Defender (Centre)"),
		["WBR"] = "Wing-Back (Right)", ["WBL"] = "Wing-Back (Left)", ["DM"] = L("Defensive Midfielder"),
		["MR"] = L("Midfielder (Right)"), ["ML"] = L("Midfielder (Left)"), ["MC"] = L("Midfielder (Centre)"),
		["AMR"] = L("Attacking Midfielder (Right)"), ["AML"] = L("Attacking Midfielder (Left)"), ["AMC"] = L("Attacking Midfielder (Centre)"),
		["ST"] = L("Striker (Centre)"), ["STC"] = L("Striker (Centre)"),
	};

	/// <summary>
	/// The attributes by their English names (folded) and the game's own id for each, which the search
	/// panel's rows carry in every language (FINDINGS §8.172).
	/// </summary>
	private static readonly Dictionary<string, string> AttributeCodes = new()
	{
		["finishing"] = "Pfin", ["dribbling"] = "Pdri", ["firsttouch"] = "Pfrs", ["heading"] = "Phea", ["corners"] = "Pcor",
		["marking"] = "Pmar", ["crossing"] = "Pcro", ["passing"] = "Ppas", ["penaltytaking"] = "Ppen", ["freekicks"] = "Pfrk",
		["freekicktaking"] = "Pfrk", ["technique"] = "Ptec", ["tackling"] = "Ptac", ["longshots"] = "Plsh", ["longthrows"] = "Plth",
		["aggression"] = "Pagg", ["bravery"] = "Pbra", ["workrate"] = "Pwkr", ["decisions"] = "Pdec", ["determination"] = "Pdet",
		["concentration"] = "Pcnt", ["leadership"] = "Pinf", ["anticipation"] = "Pant", ["flair"] = "Pfla", ["positioning"] = "Ppsn",
		["composure"] = "Pcmp", ["teamwork"] = "Ptwr", ["offtheball"] = "Potb", ["vision"] = "Pcre",
		["agility"] = "Pagi", ["stamina"] = "Psta", ["balance"] = "Pbal", ["strength"] = "Pstr", ["pace"] = "Ppac",
		["acceleration"] = "Pacc", ["naturalfitness"] = "Pnft", ["jumpingreach"] = "Pjum",
		["rushingout"] = "Prou", ["rushingouttendency"] = "Prou", ["oneonones"] = "Pooo", ["commandofarea"] = "Pcoa", ["kicking"] = "Pkic",
		["eccentricity"] = "Pecc", ["handling"] = "Phan", ["throwing"] = "Pthr", ["aerialreach"] = "Paab", ["communication"] = "Pcom",
		["reflexes"] = "Pref", ["punching"] = "Pttp", ["punchingtendency"] = "Pttp",
	};

	/// <summary>
	/// Each attribute's number on a player's card (the game's attribute type: 42 is Pace) and its name as
	/// the game writes it in English (FINDINGS §8.172).
	/// </summary>
	private static readonly Dictionary<string, string> AttributeNames = new()
	{
		["0"] = "Aerial Reach", ["1"] = "Command Of Area", ["2"] = "Communication", ["3"] = "Eccentricity",
		["4"] = "Handling", ["5"] = "Kicking", ["6"] = "Reflexes", ["7"] = "Rushing Out (Tendency)",
		["8"] = "Punching (Tendency)", ["9"] = "Throwing", ["10"] = "One On Ones", ["11"] = "Crossing",
		["12"] = "Dribbling", ["13"] = "Finishing", ["14"] = "Heading", ["15"] = "Long Shots", ["16"] = "Long Throws",
		["17"] = "Marking", ["18"] = "Passing", ["19"] = "Penalty Taking", ["20"] = "Free Kick Taking",
		["21"] = "Tackling", ["22"] = "Technique", ["23"] = "First Touch", ["24"] = "Corners", ["25"] = "Aggression",
		["26"] = "Anticipation", ["27"] = "Bravery", ["28"] = "Vision", ["29"] = "Decisions", ["30"] = "Determination",
		["31"] = "Flair", ["32"] = "Leadership", ["33"] = "Off The Ball", ["34"] = "Positioning", ["35"] = "Teamwork",
		["36"] = "Work Rate", ["37"] = "Composure", ["38"] = "Concentration", ["39"] = "Acceleration", ["40"] = "Agility",
		["41"] = "Balance", ["42"] = "Pace", ["43"] = "Stamina", ["44"] = "Strength", ["45"] = "Jumping Reach",
		["46"] = "Natural Fitness"
	};

	/// <summary>
	/// A player's card with each attribute named by its id: in another language the name read back
	/// from the words can be another attribute's (Pace comes back as Speed in a Turkish game).
	/// </summary>
	private static JsonElement WithAttributeNames(JsonElement card)
	{
		if (!card.TryGetProperty("Attributes", out var list) || !list.EnumerateArray().Any(a => AttributeNames.ContainsKey(Field(a, "Id"))))
			return card;
		var node = System.Text.Json.Nodes.JsonNode.Parse(card.GetRawText())!;
		foreach (var attribute in node["Attributes"]!.AsArray())
			if (attribute?["Id"]?.GetValue<string>() is { } id && AttributeNames.TryGetValue(id, out var english))
				attribute["Name"] = english;
		return JsonDocument.Parse(node.ToJsonString()).RootElement;
	}

	/// <summary>
	/// The search panel's Apply (true) and Cancel (false) as the game itself tells them apart: the
	/// panel's two buttons stand for the same event, and what differs is the payload each sends —
	/// true for keeping what was set, false for dropping it — which does not change with the
	/// language. Null when the panel is not open.
	/// </summary>
	private static async Task<string> PanelSent(bool apply)
	{
		var sent = (await Fm.GetJson("buttons", ("json", "1"), ("gen", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains("EventFmxFilterRestoreFilter", StringComparison.Ordinal)
				&& Field(b, "SendsProps") == (apply ? "True" : "False"));
		return sent!.ValueKind == JsonValueKind.Object ? Field(sent, "Pointer") : null;
	}

	// The player search panel, read by the data paths the game binds its controls to instead of by
	// the words on it, which another language changes: the panel itself by a heading it binds
	// ("Position"), each rule by the path its own control writes, and an attribute row's name and
	// its two controls ("rule.…") among the panel's items.
	private const string SearchPanel = "filter.FilterRulePlayerPosition.Name";
	private const string SearchCompare = "rule.selectedcompareflag";
	private const string SearchValue = "rule.minvalue";
	private const string SearchMaxValue = "rule.maxvalue";

	private static async Task<string> ApplySearch(List<(string Rule, string Choice, string What)> wanted,
		List<(string Attribute, bool AtLeast, int Value)>? conditions = null, string? position = null, long? maxValue = null)
	{
		// What can be told to be wrong from the words alone is said before the panel is opened.
		string? place = null;
		if (position != null)
		{
			var squeezed = new string(position.Where(char.IsLetter).ToArray());
			place = PositionNames.TryGetValue(squeezed, out var full) ? full : Choose(PositionNames.Values.Distinct().ToList(), position, L("Mevki"));
			if (place == null) return "";
		}

		JsonElement edit = default;
		await Until(async () => (edit = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains("EventFmxFilterStoreFilter", StringComparison.Ordinal))).ValueKind == JsonValueKind.Object, seconds: 6, every: 0.4);
		if (edit!.ValueKind != JsonValueKind.Object) return L("error: sayfada arama paneli butonu yok");
		await Fm.Get("click", ("ptr", Field(edit, "Pointer")));

		async Task<JsonElement?> Under(string rule)
		{
			var items = (await Fm.GetJson("form", ("anchorpath", SearchPanel), ("up", "8"))).EnumerateArray().ToList();
			foreach (var item in items)
				if (Field(item, "Kind") == "dropdown" && string.Equals(Field(item, "Binds"), rule, StringComparison.OrdinalIgnoreCase)) return item;
			return null;
		}
		// The panel's own Apply and Cancel stand for the same event; the one that keeps what was set
		// sends "true", the one that drops it "false" — the payload the game itself sends, the same
		// in every language. No word on either button is ever pressed; if the game does not say
		// which is which the panel is left as it is rather than guessed at from its language.
		async Task<string> Close(string error)
		{
			if (await PanelSent(false) is { } cancel) await Fm.Get("click", ("ptr", cancel));
			return error;
		}

		// The list that adds a condition; the panel takes a moment to be there after it opens.
		async Task<JsonElement> Adder()
		{
			JsonElement found = default;
			await Until(async () => (found = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(d => Field(d, "Binds") == "conditiontoadd" || Field(d, "Label") == "Add Condition")).ValueKind == JsonValueKind.Object, seconds: 6, every: 0.4);
			return found;
		}

		foreach (var (rule, typed, what) in wanted)
		{
			JsonElement? list = null;
			await Until(async () => (list = await Under(rule)) != null, seconds: 6, every: 0.4);
			if (list == null) return await Close(L($"error: arama panelinde '{what}' yok"));
			var choices = list!.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
			// A number typed is the choice itself (an age), not its place in the list.
			var choice = choices.FirstOrDefault(c => string.Equals(c, typed, StringComparison.OrdinalIgnoreCase)) ?? Choose(choices, typed, what);
			if (choice == null) return await Close("");
			if (Field(list!.Value, "Label") == choice) continue;

			var picked = await Fm.Get("select", ("ptr", Field(list!.Value, "Pointer")), ("choice", choice), ("fire", "1"));
			if (picked.StartsWith("error", StringComparison.Ordinal)) return await Close(picked);
			if (!await Until(async () => await Under(rule) is { } now && Field(now, "Label") == choice, seconds: 4, every: 0.4))
				return await Close(L($"error: {what}: '{choice}' seçildi ama panel tutmadı"));
		}

		// Attribute conditions: "Add Attribute" › Attributes › group › attribute puts a row in the
		// panel — the attribute, "Is At Least" or "Is At Most", and a value set from a short list
		// (1, 5, 10, 15, 20) and nudged by one with - and +.
		foreach (var (typed, atLeast, value) in conditions ?? [])
		{
			var adder = await Adder();
			if (adder.ValueKind != JsonValueKind.Object) return await Close("error: arama panelinde 'Add Attribute' yok");
			var attributes = new List<(string Group, string Name, string Code)>();
			foreach (var group in new[] { "Technical", "Mental", "Physical", "Goalkeeping" })
			{
				var names = await Fm.GetJson("menu", ("ptr", Field(adder, "Pointer")), ("names", $"Attributes>{group}"), ("ids", "1"));
				if (names.ValueKind == JsonValueKind.Array)
					attributes.AddRange(names.EnumerateArray().Select(n => n.ValueKind == JsonValueKind.Object ? (group, Field(n, "Text"), Field(n, "Value")) : (group, n.GetString(), "")));
			}
			// By the attribute's own id first: in another language the row's words can come back as
			// another attribute's ("Hız" is Pace, and reads as Speed).
			var name = (AttributeCodes.TryGetValue(new string(Fold(typed).Where(char.IsLetter).ToArray()), out var code) ? attributes.FirstOrDefault(a => a.Code == code).Name : null)
				?? attributes.Select(a => a.Name).FirstOrDefault(n => Fold(n) == Fold(typed))
				?? Choose(attributes.Select(a => a.Name).Distinct().ToList(), typed, L("Nitelik"));
			if (name == null) return await Close("");
			var added = await Fm.Get("select", ("ptr", Field(adder, "Pointer")), ("names", $"Attributes>{attributes.First(a => a.Name == name).Group}>{name}"), ("fire", "1"));
			if (added.StartsWith("error", StringComparison.Ordinal)) return await Close(added);

			// The row just added is the last one that names the attribute; its two controls are the
			// dropdowns of that row, told by the paths they write, not by the words in them.
			async Task<(List<JsonElement> Items, int At)> Row()
			{
				var items = (await Fm.GetJson("form", ("anchorpath", SearchPanel), ("up", "8"), ("buttons", "1"))).EnumerateArray().ToList();
				return (items, items.FindLastIndex(i => Field(i, "Kind") == "text" && Field(i, "Text") == name));
			}
			bool Compare(JsonElement i) => Field(i, "Kind") == "dropdown" && string.Equals(Field(i, "Binds"), SearchCompare, StringComparison.OrdinalIgnoreCase);
			(List<JsonElement> Items, int At) row = default;
			if (!await Until(async () => (row = await Row()).At >= 0 && row.Items.Skip(row.At).Any(Compare), seconds: 5, every: 0.4))
				return await Close(L($"error: {name} koşulu panele eklenmedi"));

			JsonElement Next(Func<JsonElement, bool> fits) => row.Items.Skip(row.At + 1).FirstOrDefault(fits);
			// The value shows on a button of its own, between the comparison and the - and +.
			int Shown() => row.Items.Skip(row.At + 1).Where(i => Field(i, "Kind") is "text" or "button").Select(i => int.TryParse(Field(i, "Text"), out var n) ? n : -1).FirstOrDefault(n => n >= 0, -1);
			// Switching the comparison swaps the path the value's list writes: "Is At Least" keeps
			// rule.minvalue, "Is At Most" rule.maxvalue.
			bool Presets(JsonElement i) => Field(i, "Kind") == "dropdown"
				&& (string.Equals(Field(i, "Binds"), SearchValue, StringComparison.OrdinalIgnoreCase)
				    || string.Equals(Field(i, "Binds"), SearchMaxValue, StringComparison.OrdinalIgnoreCase));
			var how = Next(Compare);
			var wantedHow = atLeast ? "Is At Least" : "Is At Most";
			if (Field(how, "Label") != wantedHow)
			{
				var switched = await Fm.Get("select", ("ptr", Field(how, "Pointer")), ("choice", wantedHow), ("fire", "1"));
				if (switched.StartsWith("error", StringComparison.Ordinal)) return await Close(switched);
				// The comparison's word changes before the value under it is drawn: the row is taken
				// only once both are there, or the value reads as missing (FINDINGS §8.290).
				if (!await Until(async () =>
				    {
					    row = await Row();
					    return row.At >= 0 && Field(Next(Compare), "Label") == wantedHow
					           && (Shown() >= 0 || Next(Presets).ValueKind == JsonValueKind.Object);
				    }, seconds: 6, every: 0.2))
					return await Close(L($"error: {name}: '{wantedHow}' seçildi ama satır tamamlanmadı"));
			}

			// The value: the nearest of the list's, then one step at a time.
			var presets = Next(Presets);
			var nearest = new[] { 1, 5, 10, 15, 20 }.OrderBy(preset => Math.Abs(preset - value)).First();
			if (Shown() != nearest && presets.ValueKind == JsonValueKind.Object)
			{
				await Fm.Get("select", ("ptr", Field(presets, "Pointer")), ("choice", nearest.ToString()), ("fire", "1"));
				await Until(async () => (row = await Row()).At >= 0 && Shown() == nearest, seconds: 4, every: 0.4);
			}
			for (var guard = 0; guard < 6 && Shown() != value && Shown() >= 0; guard++)
			{
				var before = Shown();
				var step = Next(i => Field(i, "Kind") == "button" && Field(i, "Text") == (before < value ? "+" : "-"));
				if (step.ValueKind != JsonValueKind.Object) break;
				await Fm.Get("click", ("ptr", Field(step, "Pointer")));
				// The value under the step turns a little later than the click: measured 0.34–0.51 s in
				// both languages, so 3 s was only 6×; 8 s leaves ~16× on a slower machine. An empty
				// step is not taken as done — the guard runs out and the change is reported as failed.
				await Until(async () => (row = await Row()).At >= 0 && Shown() != before, seconds: 8, every: 0.3);
			}
			if (Shown() != value) return await Close(L($"error: {name} için değer {value} yapılamadı (panelde {Shown()})"));
		}

		// The position he is natural at: a condition of its own ("Add Attribute" › Position), a
		// row with how well he plays there and where. The pitch at the top of the panel says the
		// same but is not made of buttons.
		if (position != null)
		{
			var adder = await Adder();
			if (adder.ValueKind != JsonValueKind.Object) return await Close("error: arama panelinde 'Add Attribute' yok");
			var added = await Fm.Get("select", ("ptr", Field(adder, "Pointer")), ("choice", "Position"), ("fire", "1"));
			if (added.StartsWith("error", StringComparison.Ordinal)) return await Close(added);
			// The list of positions is the one whose rows are the game's position bits (a striker is 16384);
			// its words differ by language ("Defence (Right)" in a Turkish game's English).
			var bit = PositionBits.First(p => PositionNames[p.Slot] == place).Bit.ToString();
			async Task<(JsonElement List, string Row)> Where()
			{
				foreach (var d in (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Reverse())
				{
					if (d.GetProperty("Choices").GetArrayLength() != PositionBits.Length) continue;
					var rows = await Fm.GetJson("menu", ("ptr", Field(d, "Pointer")), ("ids", "1"));
					if (rows.ValueKind == JsonValueKind.Array && rows.EnumerateArray().FirstOrDefault(r => Field(r, "Id") == bit) is { ValueKind: JsonValueKind.Object } row)
						return (d, Field(row, "Text"));
				}
				return default;
			}
			(JsonElement List, string Row) where = default;
			if (!await Until(async () => (where = await Where()).Row != null, seconds: 5, every: 0.4))
				return await Close(L("error: mevki koşulu panele eklenmedi"));
			await Fm.Get("select", ("ptr", Field(where.List, "Pointer")), ("choice", where.Row), ("fire", "1"));
			if (!await Until(async () => Field((await Where()).List, "Label") == where.Row, seconds: 4, every: 0.4))
				return await Close(L($"error: mevki '{place}' seçildi ama panel tutmadı"));
		}

		// What he may cost at most: "Add Attribute" › Transfer Value, a row that opens on "Is At
		// Most" with the amount chosen from a list of round figures.
		if (maxValue != null)
		{
			var adder = await Adder();
			if (adder.ValueKind != JsonValueKind.Object) return await Close("error: arama panelinde 'Add Attribute' yok");
			var added = await Fm.Get("select", ("ptr", Field(adder, "Pointer")), ("choice", "Transfer Value"), ("fire", "1"));
			if (added.StartsWith("error", StringComparison.Ordinal)) return await Close(added);
			async Task<JsonElement> Amounts() => (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
				.LastOrDefault(d => d.GetProperty("Choices").EnumerateArray().Any(c => Money(c.GetString() ?? "") == 7_500_000));
			JsonElement amounts = default;
			if (!await Until(async () => (amounts = await Amounts()).ValueKind == JsonValueKind.Object, seconds: 5, every: 0.4))
				return await Close(L("error: değer koşulu panele eklenmedi"));
			// The figure as the list writes it ("£7.5M", "7,5M £").
			if (amounts.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").FirstOrDefault(c => Money(c) == maxValue) is not { } figure)
				return await Close(L($"error: değer listesinde {MoneyFormat.Short(maxValue.Value)} yok"));
			await Fm.Get("select", ("ptr", Field(amounts, "Pointer")), ("choice", figure), ("fire", "1"));
			// Not a fixed pause: the list says what it now holds. Its bound figure is the raw
			// number, whatever the money is written like, so it is read there.
			if (!await Until(async () => (await Amounts()) is { ValueKind: JsonValueKind.Object } now && Field(now, "Value") == maxValue.Value.ToString(), seconds: 4, every: 0.25))
				return await Close(L($"error: {MoneyFormat.Short(maxValue.Value)} değeri panele oturmadı"));
		}

		if (await PanelSent(true) is not { } apply) return await Close(L("error: arama panelinde Apply düğmesi bulunamadı"));
		var applied = await Fm.Get("click", ("ptr", apply));
		if (applied.StartsWith("error", StringComparison.Ordinal)) return await Close(applied);
		await Until(async () => await PanelSent(true) == null, seconds: 5, every: 0.4);
		return null;
	}
}
