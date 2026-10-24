using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static async Task<List<string>> LabelledAttachment(List<string> shown)
	{
		List<(string Text, string Path)> texts;
		try
		{
			texts = (await Fm.GetJson("texts", ("anchor", "Attachments"), ("up", "8"), ("paths", "1"))).EnumerateArray()
				.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
		}
		catch (FmError) { return [string.Join(" · ", shown)]; }
		var start = texts.FindIndex(t => t.Text == "Attachments");
		if (start < 0 || !texts.Any(t => t.Path.Length > 0)) return [string.Join(" · ", shown)];
		// Only what the reader was given: the attachment, not the panels beside it.
		var wanted = shown.ToHashSet();
		var items = texts.Skip(start + 1).Where(t => wanted.Contains(t.Text)).ToList();
		var repeated = items.Where(t => t.Path.Length == 0).GroupBy(t => t.Text).Where(g => g.Count() > 2).Select(g => g.Key).ToHashSet();

		// Headings (a run of plain texts) and records (a run of bound ones), in order.
		items = items.Where(t => t.Path.Length > 0 || !repeated.Contains(t.Text)).ToList();
		var parts = new List<(bool Record, List<(string Text, string Path)> Items)>();
		for (var n = 0; n < items.Count; n++)
		{
			var item = items[n];
			var last = parts.Count > 0 ? parts[^1] : default;
			// A plain text inside a row (a scout's grade between the player and the report's date) is the row's.
			var inside = item.Path.Length == 0 && last.Record && n + 1 < items.Count && items[n + 1].Path is { Length: > 0 } next
				&& !last.Items.Any(t => t.Path == next) && last.Items.Any(t => Root(t.Path) == Root(next));
			// So is a bare figure right after a row (a training rating drawn without a path after the name).
			inside |= item.Path.Length == 0 && last.Record && n > 0 && items[n - 1].Path.Length > 0
				&& System.Text.RegularExpressions.Regex.IsMatch(item.Text, @"^[-+]?\d[\d.,]*%?$");
			var record = item.Path.Length > 0 || inside;
			if (parts.Count == 0 || last.Record != record || record && item.Path.Length > 0 && last.Items.Any(t => t.Path == item.Path))
				parts.Add((record, []));
			parts[^1].Items.Add(item);
		}

		static string Root(string path) => path.Split('.')[0];

		var lines = new List<string>();
		string? lastPath = null;
		for (var k = 0; k < parts.Count; k++)
		{
			var (record, part) = parts[k];
			var text = string.Join(" · ", part.Select(t => t.Text));
			if (!record)
			{
				// A row's own label drawn without a path ("Cts 11/7" before a fixture) leads the row it is on.
				// A label of words ("Training Rating") is a heading.
				if (part.Count == 1 && part[0].Text.Any(char.IsDigit) && k > 0 && parts[k - 1].Items.Count > 1 && k + 1 < parts.Count && parts[k + 1].Items.Count > 1)
				{
					parts[k + 1] = (true, [part[0], .. parts[k + 1].Items]);
					continue;
				}
				lines.Add(text);
				lastPath = null;
				continue;
			}
			// One path after another of the same kind: a list on one line.
			if (part.Count == 1 && lastPath == part[0].Path && lines.Count > 0)
			{
				lines[^1] += " · " + text;
				continue;
			}
			lines.Add("  " + text);
			lastPath = part.Count == 1 ? part[0].Path : null;
		}
		// A heading with nothing under it (the actions', shown as the answers) says nothing.
		if (lines.Count > 1 && parts.Count > 0 && !parts[^1].Record && parts[^1].Items.Count == 1 && !parts[^1].Items[0].Text.Any(char.IsDigit))
			lines.RemoveAt(lines.Count - 1);
		return lines.Count > 0 ? lines : [string.Join(" · ", shown)];
	}

	/// <summary>
	/// The card of a signing to confirm, laid out by its labels: "Fee", "Wages", "Duration", then the
	/// budgets left ("(£5.25K)" in brackets is below nothing) and each staff team's vacant roles.
	/// </summary>
	private static void Signing(List<string> card, List<(string Text, string Path)> page)
	{
		// The deal and the contract by data path; the dates are the figures drawn without a path
		// between the wage and the contract's type.
		string One(string path) => page.FirstOrDefault(t => t.Path == path).Text ?? "";
		var name = One("Person.Name") is { Length: > 0 } n ? n : card[0];
		var job = One("Person.PlayerDescription") is { Length: > 0 } described ? described
			: page.FindIndex(t => t.Path == "Person.Name") is >= 0 and var named && named + 1 < page.Count && page[named + 1].Path.Length == 0 ? GameWords.Of(page[named + 1].Text) : "";
		Console.WriteLine(L($"\n  Ek: {name}{(job.Length > 0 ? $" — {job}" : "")}"));

		if (card.Contains("Offer made") && One("feestring") is { Length: > 0 } deal) Console.WriteLine(L($"    Bonservis: {deal}"));

		var wageAt = page.FindIndex(t => t.Path == "contract.Wage.DisplayValue");
		var typeAt = page.FindIndex(t => t.Path == "contract.ContractStatusWithType.DisplayValue");
		if (wageAt >= 0)
		{
			var between = page.Skip(wageAt + 1).Take((typeAt > wageAt ? typeAt : page.Count) - wageAt - 1).ToList();
			// Beside the wage: what is left after tax and its share of the wage bill, each a sentence in the game's words.
			var notes = between.Where(t => t.Path == "contract.WageAfterTax.DisplayValue" || t.Path.Length == 0 && t.Text.Contains('%')).Select(t => t.Text).ToList();
			Console.WriteLine(L($"    Ücret: {page[wageAt].Text}{(notes.Count > 0 ? $" ({string.Join(", ", notes)})" : "")}"));
			var dates = between.Where(t => t.Path.Length == 0 && t.Text.Any(char.IsDigit) && !t.Text.Contains('%')).Select(t => t.Text).ToList();
			if (dates.Count >= 2) Console.WriteLine(L($"    Süre: {dates[0]} – {dates[1]}{(typeAt >= 0 ? $" · {page[typeAt].Text}" : "")}"));
		}

		var addons = new List<string>();
		for (var i = 0; i < page.Count; i++)
			if (page[i].Path == "contractaddon.AddOnNameString")
				addons.Add(i + 1 < page.Count && page[i + 1].Path == "contractaddon.AddOnValueString" ? $"{GameWords.Of(page[i].Text)} {page[i + 1].Text}" : GameWords.Of(page[i].Text));
		if (addons.Count > 0) Console.WriteLine(L($"    Primler ve maddeler: {string.Join(" · ", addons)}"));
		var verdictAt = page.FindIndex(t => t.Path == "report.ScoutedGradeInfo.GradeString");
		if (verdictAt > 0)
		{
			var grade = page[verdictAt - 1].Path.Length == 0 && page[verdictAt - 1].Text.Length <= 2 ? page[verdictAt - 1].Text + " · " : "";
			Console.WriteLine(L($"    Gözlemci görüşü: {grade}{page[verdictAt].Text}"));
		}

		var budgets = card.IndexOf("Recruitment Budgets");
		if (budgets >= 0)
		{
			var left = new List<string>();
			foreach (var (label, said) in new[] { ("Transfer", "transfer"), ("Wage", L("maaş")), ("Scouting", "scouting") })
			{
				var at = card.IndexOf(label, budgets);
				if (at < 0 || at + 2 >= card.Count || card[at + 1] != "Available") continue;
				var amount = card[at + 2];
				left.Add(amount.StartsWith('(') ? L($"{said} {amount.Replace("(", "-").Replace(")", "")} (aşılmış)") : $"{said} {amount}");
			}
			if (left.Count > 0) Console.WriteLine(L($"    Kalan bütçe: {string.Join(" · ", left)}"));
		}

		if (AttachedStaffTeams(page) is { Count: > 0 } teams) Console.WriteLine(L($"    Ekipler: {string.Join(" · ", teams)}"));
	}

	/// <summary>
	/// The staff teams an attachment lists, each with its name, head count and what is vacant
	/// ("Team.StaffCoachingSubGroup.ShortString" beside "team.StaffCoachingCount"); the name is
	/// the plain text just before.
	/// </summary>
	private static List<string> AttachedStaffTeams(List<(string Text, string Path)> page)
	{
		var teams = new List<string>();
		for (var i = 1; i < page.Count; i++)
			if (page[i].Path is var group && group.StartsWith("Team.Staff", StringComparison.Ordinal) && group.EndsWith("SubGroup.ShortString", StringComparison.Ordinal))
			{
				var count = page.FirstOrDefault(t => t.Path == "team." + group["Team.".Length..^"SubGroup.ShortString".Length] + "Count").Text;
				teams.Add($"{GameWords.Of(page[i - 1].Text)} {count} ({GameWords.Of(page[i].Text)})");
			}
		return teams;
	}
}
