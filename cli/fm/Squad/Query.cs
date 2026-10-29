using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// A free query over our own players: conditions joined by "ve", each a field, a comparison
/// and a value ("age&lt;23 and potential&gt;140 and contract&lt;1year"). Everything comes from
/// one reading of the squad's data, so no screen is opened.
/// </summary>
internal static partial class Program
{
	private enum QueryKind { Number, Text, Money, Months, Flag }

	private sealed record QueryField(string Head, string Property, QueryKind Kind, string Means, string[] Words)
	{
		/// <summary>The field as it is written in a query, with its Turkish letters.</summary>
		public string Say => Words[0] switch { "yas" => L("age"), "ulke" => L("nation"), "maas" => L("wages"), "sozlesme" => L("contract"), "statu" => L("status"), "mac" => L("apps"), var word => word };
	}

	private static readonly QueryField[] QueryFields =
	[
		new(L("NAME"), "Name", QueryKind.Text, L("their name"), ["ad", "isim", "name"]),
		new(L("AGE"), "Age", QueryKind.Number, L("their age"), ["yas", "age"]),
		new(L("POSITION"), "PositionCombinedString", QueryKind.Text, L("the positions they play (ST, D (C), AM…)"), ["mevki", "pozisyon", "position", "pos"]),
		new(L("NO"), "SquadNumber", QueryKind.Number, L("squad number"), ["no", "numara", "number"]),
		new(L("NATION"), "Nation.Name", QueryKind.Text, L("their nation (by the game's name: Brazil)"), ["ulke", "nation", "milliyet"]),
		new(L("HEIGHT"), "Height", QueryKind.Number, L("their height, cm"), ["boy", "height"]),
		new(L("CONDITION"), "Condition", QueryKind.Number, L("their condition, %"), ["kondisyon", "condition"]),
		new(L("MATCH SHARPNESS"), "Sharpness", QueryKind.Number, L("match sharpness, %"), ["form", "sharpness", "macformu"]),
		new(L("INJURED"), "IsInjured", QueryKind.Flag, L("whether injured (injured / !injured)"), ["sakat", "injured"]),
		new(L("POTENTIAL"), "PerceivedPotentialAbility", QueryKind.Number, L("potential as the club sees it, out of 200"), ["potansiyel", "potential", "pa"]),
		// The wage's sort value is the weekly wage in pounds, whatever period and currency the game shows.
		new(L("WAGE"), "FullContract.Wage.SortValue", QueryKind.Money, L("weekly wage, pounds (50K, 1.2M)"), ["maas", "wage"]),
		new(L("CONTRACT"), "FullContract.EndDate", QueryKind.Months, L("time left on their contract (1year, 18months)"), ["sozlesme", "contract", "kalan"]),
		new(L("STATUS"), "FullContract.AgreedPlayingTime.DisplayValue", QueryKind.Text, L("promised playing time (Star Player, Squad Player…)"), ["statu", "status", "sure"]),
		new(L("APPS"), "ClubAppearancesThisSeason", QueryKind.Number, L("apps this season"), ["mac", "apps", "matches"]),
		new(L("GOALS"), "ClubGoalsThisSeason", QueryKind.Number, L("goals this season"), ["gol", "goals"]),
		new(L("ASSISTS"), "ClubAssistsThisSeason", QueryKind.Number, L("assists this season"), ["asist", "assists"]),
		new(L("RATING"), "ClubAverageRatingThisSeason", QueryKind.Number, L("average rating this season"), ["puan", "rating", "ortalama"]),
	];

	private sealed record QueryTerm(QueryField Field, string Op, string Raw, double Number);

	private static async Task<int> Query(bool json, string[] args)
	{
		var sortBy = Option(args, "--sort");
		var words = Words(args).Where(w => w != sortBy).ToList();
		if (words.Count == 0)
		{
			Console.WriteLine(L("usage: query \"<condition> and <condition> …\" [--sort <field>] [--asc]"));
			Console.WriteLine(L("example:  query \"age<23 and potential>140\"     query \"contract<1year\"     query \"position=ST and goals>2\"     query \"condition<70 and !injured\""));
			Console.WriteLine(L("Comparison: <  <=  >  >=  =  !=   (for text, = and ~ mean \"contains\")\n"));
			Table.Write([L("FIELD"), L("WHAT")], QueryFields.Select(f => new[] { f.Say, f.Means }).ToList());
			return 0;
		}
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		static QueryField Named(string word) => QueryFields.FirstOrDefault(f => f.Words.Contains(Fold(word).Replace(" ", "")));

		var terms = new List<QueryTerm>();
		foreach (var part in Regex.Split(string.Join(" ", words), @"\s+(?:ve|and)\s+|\s*&&?\s*|\s*,\s*", RegexOptions.IgnoreCase).Where(p => p.Trim().Length > 0))
		{
			var m = Regex.Match(part.Trim(), @"^(!)?\s*([\p{L} ]+?)\s*(?:(<=|>=|!=|<|>|=|~)\s*(.+))?$");
			var field = m.Success ? Named(m.Groups[2].Value) : null;
			if (field == null)
			{
				Console.Error.WriteLine(L($"'{part.Trim()}' not understood. Fields: {string.Join(", ", QueryFields.Select(f => f.Say))}   (for the list: query)"));
				return 64;
			}
			var op = m.Groups[3].Value;
			var raw = m.Groups[4].Value.Trim().Trim('"', '\'');
			if (field.Kind == QueryKind.Flag)
			{
				// "injured", "!injured", "injured=yes", "injured=no".
				var yes = op.Length == 0 || Fold(raw) is "evet" or "true" or "1" or "yes";
				terms.Add(new(field, yes != (m.Groups[1].Success || op == "!=") ? "=" : "!=", "True", 0));
				continue;
			}
			if (op.Length == 0 || raw.Length == 0 || m.Groups[1].Success)
			{
				Console.Error.WriteLine(L($"'{part.Trim()}': a comparison and a value are needed (example: {field.Say}{(field.Kind == QueryKind.Text ? "=…" : "<…")})."));
				return 64;
			}
			double number = 0;
			var understood = field.Kind switch
			{
				QueryKind.Text => true,
				QueryKind.Money => Money(raw) is { } money && (number = money) >= 0,
				QueryKind.Months => Span(raw) is { } months && (number = months) >= 0,
				_ => double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number),
			};
			if (!understood || (field.Kind == QueryKind.Text && op is "<" or ">" or "<=" or ">="))
			{
				Console.Error.WriteLine(L($"'{part.Trim()}': the value for {field.Say} {field.Kind switch
				{
					QueryKind.Text => L("text; only with =, ~ or !="),
					QueryKind.Money => L("must be money (50K, 1.2M)"),
					QueryKind.Months => L("must be a length of time (1year, 18months)"),
					_ => L("must be a number"),
				}}."));
				return 64;
			}
			terms.Add(new(field, op, raw, number));
		}

		QueryField sort = terms[0].Field;
		if (sortBy != null && (sort = Named(sortBy)) == null)
		{
			Console.Error.WriteLine(L($"There is no field called '{sortBy}'. Fields: {string.Join(", ", QueryFields.Select(f => f.Say))}"));
			return 64;
		}

		// The name, age and position are always shown; then whatever was asked about.
		var shown = QueryFields.Where(f => f.Property is "Name" or "Age" or "PositionCombinedString" || terms.Any(t => t.Field == f) || f == sort).ToList();
		var players = (await Fm.Pull("Human.Team.Players", string.Join(",", shown.Select(f => f.Property)), list: true)).EnumerateArray()
			.Where(p => Field(p, "Name").Length > 0).ToList();
		var today = DateTime.TryParseExact(Field(await Fm.GetJson("state"), "Date"), "yyyy-MM-dd", null, DateTimeStyles.None, out var d) ? d : DateTime.Today;

		// What a field is worth as a number, for comparing and sorting; null when the game has none.
		double? Value(JsonElement p, QueryField f)
		{
			var text = Field(p, f.Property);
			switch (f.Kind)
			{
				case QueryKind.Money:
					if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pounds)) return pounds;
					// "£230K p/w"; a wage shown by the month or year is brought to the week.
					var wage = Regex.Match(text, @"^(.+?)\s*p/([wma])$");
					return wage.Success && Money(wage.Groups[1].Value) is { } money
						? money / (wage.Groups[2].Value switch { "m" => 52 / 12.0, "a" => 52, _ => 1 }) : null;
				case QueryKind.Months:
					return GameDay(text) is { } ends
						? (ends.Year - today.Year) * 12 + ends.Month - today.Month + (ends.Day >= today.Day ? 0 : -1) : null;
				case QueryKind.Flag:
					return text == "True" ? 1 : 0;
				default:
					return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
			}
		}

		bool Holds(JsonElement p, QueryTerm t)
		{
			if (t.Field.Kind == QueryKind.Text)
			{
				var has = Fold(Field(p, t.Field.Property)).Contains(Fold(t.Raw), StringComparison.Ordinal);
				return t.Op == "!=" ? !has : has;
			}
			if (t.Field.Kind == QueryKind.Flag) return (Field(p, t.Field.Property) == "True") == (t.Op == "=");
			if (Value(p, t.Field) is not { } v) return false;
			return t.Op switch { "<" => v < t.Number, "<=" => v <= t.Number, ">" => v > t.Number, ">=" => v >= t.Number, "!=" => v != t.Number, _ => v == t.Number };
		}

		var found = players.Where(p => terms.All(t => Holds(p, t))).ToList();
		// Smallest first when the question was "less than", else largest first; --asc says smallest.
		var ascending = args.Contains("--asc") || (sortBy == null && terms[0].Op is "<" or "<=");
		found = sort.Kind == QueryKind.Text
			? found.OrderBy(p => Fold(Field(p, sort.Property)), StringComparer.Ordinal).ToList()
			: ascending ? found.OrderBy(p => Value(p, sort) ?? double.MaxValue).ToList() : found.OrderByDescending(p => Value(p, sort) ?? double.MinValue).ToList();

		string Shown(JsonElement p, QueryField f)
		{
			var text = Field(p, f.Property);
			return f.Kind switch
			{
				QueryKind.Flag => text == "True" ? L("injured") : "",
				QueryKind.Months => Value(p, f) is { } months && GameDay(text) is { } ends
					? $"{ends.Day} {Months[ends.Month - 1]} {ends.Year} · {Left((int)months)}" : text,
				QueryKind.Money => Value(p, f) is { } money ? L($"{MoneyFormat.Exact(MoneyFormat.Shown(money))}/week") : text,
				_ when f.Property == "ClubAverageRatingThisSeason" => Value(p, f) is { } r and > 0 ? GameNumbers.Of(r, "0.00") : "-",
				_ => text,
			};
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(found.Select(p => shown.ToDictionary(f => f.Property, f => Field(p, f.Property)))));
			return 0;
		}
		if (found.Count == 0)
		{
			Console.WriteLine(L($"No player meets the conditions ({players.Count} players looked at)."));
			return 0;
		}
		Table.Write(shown.Select(f => f.Property == "Name" ? L("PLAYER") : f.Head).ToArray(), found.Select(p => shown.Select(f => Shown(p, f)).ToArray()).ToList());
		Console.WriteLine(L($"\n{found.Count} players (out of {players.Count})"));
		return 0;

		static string Left(int months) => months < 0 ? L("expired") : months < 12 ? L($"{months} months") : months % 12 == 0 ? L($"{months / 12} yrs") : L($"{months / 12} yrs {months % 12} mths");

		// "1year", "2 years", "18months", "6": a length of time in months.
		static double? Span(string text)
		{
			var m = Regex.Match(Fold(text).Replace(" ", ""), @"^(\d+(?:[.,]\d+)?)(yil|y|year|years|ay|a|month|months|m)?$");
			if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return null;
			return m.Groups[2].Value is "yil" or "y" or "year" or "years" ? n * 12 : n;
		}
	}
}
