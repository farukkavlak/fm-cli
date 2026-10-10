using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// A free query over our own players: conditions joined by "ve", each a field, a comparison
/// and a value ("yaş&lt;23 ve potansiyel&gt;140 ve sözleşme&lt;1yıl"). Everything comes from
/// one reading of the squad's data, so no screen is opened.
/// </summary>
internal static partial class Program
{
	private enum QueryKind { Number, Text, Money, Months, Flag }

	private sealed record QueryField(string Head, string Property, QueryKind Kind, string Means, string[] Words)
	{
		/// <summary>The field as it is written in a query, with its Turkish letters.</summary>
		public string Say => Words[0] switch { "yas" => "yaş", "ulke" => "ülke", "maas" => "maaş", "sozlesme" => "sözleşme", "statu" => "statü", "mac" => "maç", var word => word };
	}

	private static readonly QueryField[] QueryFields =
	[
		new("AD", "Name", QueryKind.Text, "adı", ["ad", "isim", "name"]),
		new("YAŞ", "Age", QueryKind.Number, "yaşı", ["yas", "age"]),
		new("MEVKİ", "PositionCombinedString", QueryKind.Text, "oynadığı mevkiler (ST, D (C), AM…)", ["mevki", "pozisyon", "position", "pos"]),
		new("NO", "SquadNumber", QueryKind.Number, "forma numarası", ["no", "numara", "number"]),
		new("ÜLKE", "Nation.Name", QueryKind.Text, "ülkesi (oyundaki adıyla: Brazil)", ["ulke", "nation", "milliyet"]),
		new("BOY", "Height", QueryKind.Number, "boyu, cm", ["boy", "height"]),
		new("KONDİSYON", "Condition", QueryKind.Number, "kondisyonu, %", ["kondisyon", "condition"]),
		new("MAÇ FORMU", "Sharpness", QueryKind.Number, "maç formu, %", ["form", "sharpness", "macformu"]),
		new("SAKAT", "IsInjured", QueryKind.Flag, "sakat mı (sakat / !sakat)", ["sakat", "injured"]),
		new("POTANSİYEL", "PerceivedPotentialAbility", QueryKind.Number, "kulübün gördüğü potansiyel, 200 üzerinden", ["potansiyel", "potential", "pa"]),
		new("MAAŞ", "FullContract.Wage.DisplayValue", QueryKind.Money, "haftalık maaşı (50K, 1.2M)", ["maas", "wage"]),
		new("SÖZLEŞME", "FullContract.EndDateDescription", QueryKind.Months, "sözleşmesinin kalan süresi (1yıl, 18ay)", ["sozlesme", "contract", "kalan"]),
		new("STATÜ", "FullContract.AgreedPlayingTime.DisplayValue", QueryKind.Text, "söz verilen oynama süresi (Star Player, Squad Player…)", ["statu", "status", "sure"]),
		new("MAÇ", "ClubAppearancesThisSeason", QueryKind.Number, "bu sezon maç", ["mac", "apps", "matches"]),
		new("GOL", "ClubGoalsThisSeason", QueryKind.Number, "bu sezon gol", ["gol", "goals"]),
		new("ASİST", "ClubAssistsThisSeason", QueryKind.Number, "bu sezon asist", ["asist", "assists"]),
		new("PUAN", "ClubAverageRatingThisSeason", QueryKind.Number, "bu sezon ortalama puan", ["puan", "rating", "ortalama"]),
	];

	private sealed record QueryTerm(QueryField Field, string Op, string Raw, double Number);

	private static async Task<int> Query(bool json, string[] args)
	{
		var sortBy = Option(args, "--sort");
		var words = Words(args).Where(w => w != sortBy).ToList();
		if (words.Count == 0)
		{
			Console.WriteLine("kullanım: query \"<koşul> ve <koşul> …\" [--sort <alan>] [--asc]");
			Console.WriteLine("örnek:    query \"yaş<23 ve potansiyel>140\"     query \"sözleşme<1yıl\"     query \"mevki=ST ve gol>2\"     query \"kondisyon<70 ve !sakat\"");
			Console.WriteLine("Karşılaştırma: <  <=  >  >=  =  !=   (yazıda = ve ~ \"içinde geçiyor\" demek)\n");
			Table.Write(["ALAN", "NE"], QueryFields.Select(f => new[] { f.Say, f.Means }).ToList());
			return 0;
		}
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
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
				Console.Error.WriteLine($"'{part.Trim()}' anlaşılmadı. Alanlar: {string.Join(", ", QueryFields.Select(f => f.Say))}   (liste için: query)");
				return 64;
			}
			var op = m.Groups[3].Value;
			var raw = m.Groups[4].Value.Trim().Trim('"', '\'');
			if (field.Kind == QueryKind.Flag)
			{
				// "sakat", "!sakat", "sakat=evet", "sakat=hayır".
				var yes = op.Length == 0 || Fold(raw) is "evet" or "true" or "1" or "yes";
				terms.Add(new(field, yes != (m.Groups[1].Success || op == "!=") ? "=" : "!=", "True", 0));
				continue;
			}
			if (op.Length == 0 || raw.Length == 0 || m.Groups[1].Success)
			{
				Console.Error.WriteLine($"'{part.Trim()}': karşılaştırma ve değer gerek (örnek: {field.Say}{(field.Kind == QueryKind.Text ? "=…" : "<…")}).");
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
				Console.Error.WriteLine($"'{part.Trim()}': {field.Say} için değer {field.Kind switch
				{
					QueryKind.Text => "yazı; yalnızca =, ~ ya da != ile",
					QueryKind.Money => "para olmalı (50K, 1.2M)",
					QueryKind.Months => "süre olmalı (1yıl, 18ay)",
					_ => "sayı olmalı",
				}}.");
				return 64;
			}
			terms.Add(new(field, op, raw, number));
		}

		QueryField sort = terms[0].Field;
		if (sortBy != null && (sort = Named(sortBy)) == null)
		{
			Console.Error.WriteLine($"'{sortBy}' diye bir alan yok. Alanlar: {string.Join(", ", QueryFields.Select(f => f.Say))}");
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
					// "£230K p/w"; a wage shown by the month or year is brought to the week.
					var wage = Regex.Match(text, @"^(.+?)\s*p/([wma])$");
					return wage.Success && Money(wage.Groups[1].Value) is { } money
						? money / (wage.Groups[2].Value switch { "m" => 52 / 12.0, "a" => 52, _ => 1 }) : null;
				case QueryKind.Months:
					return DateTime.TryParseExact(text, "d/M/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ends)
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
				QueryKind.Flag => text == "True" ? "sakat" : "",
				QueryKind.Months => Value(p, f) is { } months ? $"{text} · {Left((int)months)}" : text,
				_ when f.Property == "ClubAverageRatingThisSeason" => Value(p, f) is { } r and > 0 ? r.ToString("0.00", CultureInfo.InvariantCulture) : "-",
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
			Console.WriteLine($"Koşula uyan oyuncu yok ({players.Count} oyuncuya bakıldı).");
			return 0;
		}
		Table.Write(shown.Select(f => f.Property == "Name" ? "OYUNCU" : f.Head).ToArray(), found.Select(p => shown.Select(f => Shown(p, f)).ToArray()).ToList());
		Console.WriteLine($"\n{found.Count} oyuncu ({players.Count} içinden)");
		return 0;

		static string Left(int months) => months < 0 ? "bitti" : months < 12 ? $"{months} ay" : months % 12 == 0 ? $"{months / 12} yıl" : $"{months / 12} yıl {months % 12} ay";

		// "1yıl", "2 yil", "18ay", "6": a length of time in months.
		static double? Span(string text)
		{
			var m = Regex.Match(Fold(text).Replace(" ", ""), @"^(\d+(?:[.,]\d+)?)(yil|y|year|years|ay|a|month|months|m)?$");
			if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return null;
			return m.Groups[2].Value is "yil" or "y" or "year" or "years" ? n * 12 : n;
		}
	}
}
