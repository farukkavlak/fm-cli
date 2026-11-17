using System.Text.Json;

namespace FmCli;

/// <summary>
/// A page of a club that is not ours, a nation, a national team or a competition: `read --of "&lt;name&gt;" [&lt;page&gt;]`.
/// The record is found with the game's search (its row's kind is the game's type for it), its page opened by that row's
/// own button, and a page on its bar chosen by GUID. What is shown is read as `read` reads any page.
/// </summary>
internal static partial class Program
{
	private static readonly string[] RecordKinds = ["Club", "Competition", "Nation", "NationalTeam"];

	private static async Task<int> ReadOf(bool json, string wanted, string page)
	{
		if (await NotNow() is { } refused) return refused;
		var typedOf = wanted;

		// "Galatasaray (Süper Lig)": what is in brackets tells apart records of one name, by kind, league or nation.
		string? qualifier = null;
		if (System.Text.RegularExpressions.Regex.Match(wanted, @"^(.+?)\s*\((.+)\)\s*$") is { Success: true } q)
			(wanted, qualifier) = (q.Groups[1].Value, q.Groups[2].Value);

		var found = (await SearchFor(wanted)).Where(r => RecordKinds.Contains(Field(r, "Kind"))).ToList();
		string NameOf(JsonElement r) => Field(r, Field(r, "Kind"));
		string PathOf(JsonElement r) => Field(r, Field(r, "Kind") + "Path");
		var exact = found.Where(r => Fold(NameOf(r)) == Fold(wanted)).ToList();
		var worded = found.Where(r => Fold(wanted).Split(' ').All(w => Fold(NameOf(r)).Split(' ', '-').Contains(w))).ToList();
		if (exact.Count > 0) found = exact;
		else if (worded.Count > 0) found = worded;
		if (found.Count == 0)
		{
			await CloseSearch();
			Console.Error.WriteLine(L($"No club, nation or competition called '{wanted}' was found."));
			return 1;
		}
		if (found.Count > 1)
		{
			var about = await RecordDetails(found.Select(PathOf).ToList(), found.Select(r => Field(r, "Kind")).ToList());
			var labels = found.Select((r, i) => $"{NameOf(r)} ({string.Join(", ", new[] { Field(r, "Shown"), about[i] }.Where(s => s.Length > 0))})").ToList();
			var pickedOne = -1;
			if (qualifier != null)
			{
				var words = System.Text.RegularExpressions.Regex.Split(Fold(qualifier), @"[^\p{L}\p{N}]+").Where(w => w.Length > 0).ToArray();
				// Whole words first ("nation" is not "national", "men" not "women's"), then the start of one.
				static string[] Of(string label) => System.Text.RegularExpressions.Regex.Split(Fold(label), @"[^\p{L}\p{N}]+");
				var fits = labels.Select((l, i) => (l, i)).Where(x => words.All(w => Of(x.l).Contains(w))).Select(x => x.i).ToList();
				if (fits.Count == 0) fits = labels.Select((l, i) => (l, i)).Where(x => words.All(w => Of(x.l).Any(o => o.StartsWith(w, StringComparison.Ordinal)))).Select(x => x.i).ToList();
				if (fits.Count == 1) pickedOne = fits[0];
				else if (fits.Count == 0) Console.Error.WriteLine(L($"'({qualifier})' matches none of them."));
				else (found, labels) = (fits.Select(i => found[i]).ToList(), fits.Select(i => labels[i]).ToList());
			}
			if (pickedOne < 0 && Ui.Ask(L($"'{wanted}' matches more than one:"), labels) is { } picked) pickedOne = picked;
			if (pickedOne < 0)
			{
				await CloseSearch();
				if (!Ui.Interactive) Console.Error.WriteLine(L($"'{wanted}' matches more than one; tell them apart in brackets, e.g. \"{wanted} ({(about[^1].Length > 0 ? about[^1] : Field(found[^1], "Shown"))})\":\n") + string.Join("\n", labels.Select(l => "  " + l)));
				return 1;
			}
			found = [found[pickedOne]];
		}
		var record = found[0];

		// Its page is up once what is on screen (its texts and its bar: a nation's page has none) is not what was
		// before and reads the same twice. A page opened again is a new bar.
		async Task<string> Screen_() => await Fm.Get("barpages") + "\n##\n" + string.Join("\n", (await ShownTexts()).Select(t => Field(t, "Text")));
		var screenBefore = await Screen_();
		var before = await ShownTexts();
		var pressed = await Arriving("searchopen", [("path", PathOf(record))]);
		if (pressed.StartsWith("error", StringComparison.Ordinal))
		{
			await CloseSearch();
			return Report(pressed);
		}
		var last = "?";
		await Until(async () =>
		{
			var now = await Screen_();
			var settled = now == last && now != screenBefore;
			last = now;
			return settled;
		}, seconds: 10, every: 0.25);
		var bars = last.Split("\n##\n")[0];
		await CloseSearch();
		await Fm.Get("closeintro");

		// The pages on its bar: the GUID once (a nation's bar lists each of its teams' squad), with the bar's name.
		var onBar = bars.Split('\n').Select(l => l.Split('\t')).Where(p => p.Length >= 2 && p[1].Length == 32)
			.GroupBy(p => p[1]).Select(g => (Name: g.First()[0], Guid: g.Key)).ToList();
		string Shown((string Name, string Guid) p) => GameWords.Of(p.Name);
		// What a page is typed as: `read`'s key for its GUID; a name only one page on the bar has; else its GUID's first eight.
		string KeyOf((string Name, string Guid) p) =>
			ReadablePages.FirstOrDefault(r => r.Guid == p.Guid && r.Tab == null)?.Key
			?? (onBar.Count(o => Fold(Shown(o)) == Fold(Shown(p))) > 1 ? p.Guid[..8] : "");

		var title = $"{NameOf(record)} · {Field(record, "Shown")}";
		if (page.Length == 0)
		{
			var code = await PrintPage(json, new ReadablePage("of", title, ""), before, opening: false);
			if (code == 0 && !json && onBar.Count > 0)
			{
				Console.WriteLine();
				Console.WriteLine(L("Its pages:"));
				foreach (var p in onBar) Console.WriteLine("  " + (KeyOf(p) is { Length: > 0 } key ? $"{key} · {Shown(p)}" : Shown(p)));
				Console.WriteLine(L($"\nTo read one: read <page> --of \"{typedOf}\""));
			}
			return code;
		}

		// The page typed: a key of `read`'s list (by its GUID) or its name on this bar, as the game shows it or in English.
		var chosen = onBar.Where(p => string.Equals(KeyOf(p), page, StringComparison.OrdinalIgnoreCase)).ToList();
		if (chosen.Count == 0) chosen = onBar.Where(p => Fold(Shown(p)) == Fold(page) || Fold(p.Name) == Fold(page)).ToList();
		if (chosen.Count == 0) chosen = onBar.Where(p => Fold(Shown(p)).StartsWith(Fold(page), StringComparison.Ordinal) || Fold(p.Name).StartsWith(Fold(page), StringComparison.Ordinal)).ToList();
		// A word of its name: "Kadro" for the club's own "Galatasaray Kadrosu".
		if (chosen.Count == 0) chosen = onBar.Where(p => Fold(Shown(p)).Split(' ').Any(w => w.StartsWith(Fold(page), StringComparison.Ordinal))).ToList();
		if (chosen.Count != 1)
		{
			Console.Error.WriteLine(chosen.Count == 0
				? L($"{NameOf(record)} has no page '{page}'. Its pages: read --of \"{typedOf}\"")
				: L($"'{page}' fits several pages: {string.Join(", ", chosen.Select(p => KeyOf(p) is { Length: > 0 } k ? k : Shown(p)))}"));
			return 1;
		}
		before = await ShownTexts();
		var opened = await OpenWith([("guid", chosen[0].Guid), ("bar", "1")]);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		return await PrintPage(json, new ReadablePage("of", $"{title} › {Shown(chosen[0])}", ""), before, opening: false);
	}

	/// <summary>
	/// What tells records of one name apart: a club's league (two clubs of one name and nation, one with no league),
	/// a competition's nation. Each is put at a node of ours to be read ("" where none).
	/// </summary>
	private static async Task<List<string>> RecordDetails(List<string> paths, List<string> kinds)
	{
		var details = paths.Select(_ => "").ToList();
		var read = paths.Select(_ => false).ToList();
		var tag = Guid.NewGuid().ToString("N")[..8];
		for (var i = 0; i < paths.Count; i++)
			if (kinds[i] == "Club" && paths[i].Length > 0)
				try { await Fm.Get("pathset", ("to", $"fmcli.rec{tag}.c{i}"), ("from", paths[i])); } catch (FmError) { }
		await Until(async () =>
		{
			for (var i = 0; i < paths.Count; i++)
			{
				if (read[i] || kinds[i] != "Club") { read[i] = true; continue; }
				try
				{
					var club = await Fm.GetJson("pull", ("path", $"fmcli.rec{tag}.c{i}"), ("props", "UniqueId,Division"));
					// The league comes a reading after the rest: none is said only once the game gives it as no record.
					if (Field(club, "UniqueId").Length == 0 || Field(club, "Division").Length == 0) continue;
					read[i] = true;
					if (Field(club, "Division").Contains("CompReference", StringComparison.Ordinal))
					{
						details[i] = Field(await Fm.GetJson("pull", ("path", $"fmcli.rec{tag}.c{i}.Division"), ("props", "Name")), "Name");
						if (details[i].Length == 0) { read[i] = false; continue; }
					}
					else details[i] = L("no league");
				}
				catch (FmError) { }
			}
			return read.All(r => r);
		}, seconds: 4, every: 0.5);
		return details;
	}
}
