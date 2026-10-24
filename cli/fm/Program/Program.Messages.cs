using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// What a message carries under "Attachments" — the Code of Conduct it asks to confirm, say
	/// (FINDINGS §8.145). An answer such as "Confirm" agrees to it, so it is shown with the message.
	/// </summary>
	private static async Task WriteAttachment(string title, HashSet<string> answers)
	{
		// Another message's attachment can still be on screen: this one is known by its title. And its
		// own comes a moment after the message: read at once, a message with nothing to answer (the ones
		// with answers are waited on for those) showed none — the weekly recruitment update, the advice
		// messages, 30 Nisan 2026. The heading arrives first and the card binds its content in steps
		// (measured: heading ~0.09 s, first text ~0.16 s, more up to ~0.7 s), so what follows it is
		// waited for rather than read the moment the heading is there.
		var bound = await OpenMessage(title, t => t.Text == "Attachments", seconds: 3);
		var texts = bound.Select(t => t.Text).ToList();
		var start = texts.IndexOf("Attachments");
		if (start < 0) return;
		{
			// The card streams its content: its title can be the first thing to arrive and the rest
			// follows (measured: heading ~0.09 s, first text ~0.16 s, six by ~0.43 s). It is read
			// once what follows the heading has not changed for three polls in a row; a message
			// with nothing after the heading waits the cap and then shows none.
			var last = "";
			var same = 0;
			await Until(async () =>
			{
				var page = await OpenMessage(title, t => t.Text == "Attachments", seconds: 1);
				var at = page.FindIndex(t => t.Text == "Attachments");
				var now = at >= 0 ? string.Join("|", page.Skip(at + 1).Select(t => t.Text)) : "";
				same = now.Length > 0 && now == last ? same + 1 : 0;
				last = now;
				return now.Length > 0 && same >= 2;
			}, seconds: 4, every: 0.3);
			bound = await OpenMessage(title, t => t.Text == "Attachments", seconds: 1);
			texts = bound.Select(t => t.Text).ToList();
			start = texts.IndexOf("Attachments");
			if (start < 0) return;
		}
		var rest = texts.Skip(start + 1)
			.Where(t => t is not ("Reset" or "Response Required") && !answers.Contains(t))
			.ToList();
		// The panel beside the message (the club, the manager's profile) is not part of it.
		var side = rest.FindIndex(t => t is "About Club" or "My Profile");
		if (side >= 0) rest = rest.Take(side).ToList();
		if (rest.Count == 0) return;
		// The same with each text's data path (FINDINGS §8.169), for the readers that go by it.
		var page = bound.Skip(start + 1).ToList();
		if (page.FindIndex(t => t.Text is "About Club" or "My Profile") is >= 0 and var aside) page = page.Take(aside).ToList();
		string One(string path) => page.FirstOrDefault(t => t.Path == path).Text ?? "";

		// A news story attached to the message that reports it, under the message's own title: the
		// story is its first paragraph; what follows is the page around it (the league, how long ago,
		// a player's card, the club's site), which says nothing more.
		if (rest.Count > 1 && texts.Take(start).Contains(rest[0]))
		{
			Console.WriteLine(L($"\n  Haber: {rest[1].Replace("\n", "\n  ")}"));
			return;
		}

		// A press conference's: the likely talking points, then who is coming (FINDINGS §8.145).
		if (rest[0] == "Likely Talking Points")
		{
			var journalists = rest.IndexOf("Journalists in Attendance");
			var points = rest.Skip(1).Take((journalists < 0 ? rest.Count : journalists) - 1).Where(t => !t.StartsWith('(')).ToList();
			// The plugin answers a topic it knows in English; the game's own words for it are asked
			// for and put back, as with the analysts' points (a topic the table does not know is
			// left as the game wrote it).
			await GameWords.Ask(Fm, [.. points]);
			Console.WriteLine(L("\n  Olası konular:"));
			foreach (var point in points) Console.WriteLine($"    · {GameWords.Of(point)}");
			if (journalists >= 0)
			{
				var row = rest.IndexOf("Relationship", journalists);
				var people = new List<string[]>();
				// The panels beside it (the next match, the table) follow on; a journalist's influence
				// and standing are words, theirs have numbers in them.
				for (var i = row + 1; row > 0 && i + 3 < rest.Count && rest[i] != "Next Match Preview"
				     && !rest[i + 2].Any(char.IsDigit) && !rest[i + 3].Any(char.IsDigit); i += 4)
					people.Add([rest[i], rest[i + 1], rest[i + 2], rest[i + 3]]);
				// The heading counts them all; the table holds as many as it has drawn.
				var all = journalists + 1 < rest.Count && int.TryParse(rest[journalists + 1].Trim('(', ')'), out var n) ? n : people.Count;
				if (people.Count > 0)
				{
					Console.WriteLine(all > people.Count ? L($"\n  Gelecek gazeteciler ({all} kişi, ekranda görünen {people.Count}):") : L($"\n  Gelecek gazeteciler ({all}):"));
					Table.Write([L("GAZETECİ"), L("KURUM"), L("ETKİ"), L("İLİŞKİ")], people);
				}
			}
			return;
		}

		// The captaincy card: who holds each armband; the command shows the leaders and changes them.
		if (rest[0] == "Captains" && rest.Contains("Vice-Captain"))
		{
			string Holder(string label) => rest.IndexOf(label) is var i and >= 0 && i + 1 < rest.Count ? rest[i + 1] : "";
			var vice = Holder("Vice-Captain");
			Console.WriteLine(L($"\n  Ek: kaptan {Holder("Captain")}, yardımcısı {(vice.Contains("to be selected", StringComparison.Ordinal) ? L("seçilmedi") : vice)}. Görmek ve seçmek için: captains (--captain / --vice <oyuncu>)"));
			return;
		}

		// The coaching grid in miniature; the command shows it whole and changes it.
		if (rest[0] == "Coach Assignments")
		{
			Console.WriteLine(L("\n  Ek: antrenör görevleri. Görmek ve atamak için: coaches"));
			return;
		}

		// The registration widget: its counts are the numbered shirts of the list, read better by register.
		// The registration card before any are registered: the window, the list's size, the rules (the
		// sentences under it) and nothing else of note (the fixtures beside it are the calendar's).
		if (rest[0] == "Squad Registration" && One("nextsquadreg.RegistrationDaysLeft") is { Length: > 0 } left
		    && !rest.Any(t => t.StartsWith("Unregistered (", StringComparison.Ordinal)))
		{
			var sizeAt = page.FindIndex(t => t.Path.Length == 0 && System.Text.RegularExpressions.Regex.IsMatch(t.Text, @"^\d+/\d+$"));
			Console.WriteLine(L($"\n  Ek: kayıt listesi {(sizeAt >= 0 ? page[sizeAt].Text : "")} · {One("comp.Name")} · {GameWords.Of(rest.Count > 1 ? rest[1] : "")}, {left}"));
			foreach (var rule in page.Skip(sizeAt + 1).TakeWhile(t => t.Path.Length == 0 && t.Text.Length >= 24 && t.Text.Contains(" ")).Where(_ => sizeAt >= 0))
				Console.WriteLine($"    · {rule.Text}");
			Console.WriteLine(L("    Ayrıntı: register"));
			return;
		}

		if (rest.FirstOrDefault(t => t.StartsWith("Unregistered (", StringComparison.Ordinal)) is { } unregistered)
		{
			var size = rest.FirstOrDefault(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+/\d+$"));
			Console.WriteLine(L($"\n  Ek: kayıt listesi{(size != null ? $" {size}" : "")}, kayıtsız {unregistered[14..].TrimEnd(')')}. Ayrıntı: register"));
			return;
		}

		// A signing waiting to be confirmed: the deal, the contract, what is left to spend and the staff teams.
		// A coach's has no status line; his job is drawn without a path after his name.
		if (rest.Contains("Offered Contract") && page.Any(t => t.Path == "contract.Wage.DisplayValue"))
		{
			Signing(rest, page);
			return;
		}

		// The season's review: what was expected in each competition, the transfer business, what the
		// board and the supporters made of it, and where the money is headed.
		if (rest[0] == "Competitions" && rest.Contains("Board Expectation") && rest.Contains("Performance Feedback"))
		{
			int At(string label, int from = 0) => rest.FindIndex(from, t => t == label);
			var transfers = At("Transfer History");
			var feedback = At("Performance Feedback");
			var projections = At("Projections");
			Console.WriteLine(L("\n  Ek: sezon değerlendirmesi"));
			var expected = new List<string>();
			for (var i = At("Board Expectation") + 1; i + 1 < (transfers > 0 ? transfers : feedback); i += 2) expected.Add($"{rest[i]}: {rest[i + 1]}");
			if (expected.Count > 0) Console.WriteLine(L($"    Yönetimin beklentisi — {string.Join(" · ", expected)}"));
			if (transfers > 0 && feedback > transfers)
			{
				var money = rest.Skip(transfers + 1).Take(feedback - transfers - 1).ToList();
				var into = money.IndexOf(money.FirstOrDefault(t => t.StartsWith("Transfers In", StringComparison.Ordinal)) ?? "\0");
				var outOf = money.IndexOf(money.FirstOrDefault(t => t.StartsWith("Transfers Out", StringComparison.Ordinal)) ?? "\0");
				if (into >= 0 && outOf > into)
				{
					Console.WriteLine($"    {money[into]}: {string.Join(" · ", money.Skip(into + 1).Take(outOf - into - 1).Where(t => !t.EndsWith(':')))}");
					Console.WriteLine($"    {money[outOf]}: {string.Join(" · ", money.Skip(outOf + 1).Where(t => !t.EndsWith(':')))}");
				}
				else Console.WriteLine(L($"    Transfer: {string.Join(" · ", money)}"));
			}
			foreach (var (who, label) in new[] { (L("Yönetim"), "Board"), (L("Taraftar"), "Supporters") })
			{
				var from = At(label, feedback + 1);
				if (from < 0) continue;
				var to = new[] { At("Supporters", from + 1), projections, rest.Count }.Where(x => x > from).Min();
				var said = rest.Skip(from + 1).Take(to - from - 1).ToList();
				// "Highlights", "(10),", "Criticisms", "(2)", the mood, then a line of each kind.
				var counts = string.Join(" ", said.TakeWhile(t => t is "Highlights" or "Criticisms" || t.StartsWith('('))).Replace(" (", " (").Replace(",", " ·");
				var lines = said.SkipWhile(t => t is "Highlights" or "Criticisms" || t.StartsWith('(')).ToList();
				Console.WriteLine($"    {who}: {(lines.Count > 0 ? lines[0] : "")}  [{counts}]");
				foreach (var line in lines.Skip(1)) Console.WriteLine($"      {(line.StartsWith("Disappointed", StringComparison.Ordinal) || line.StartsWith("Unhappy", StringComparison.Ordinal) ? "-" : "+")} {line}");
			}
			for (var i = projections + 1; projections > 0 && i < rest.Count; i++)
			{
				if (!rest[i].StartsWith("End of", StringComparison.Ordinal)) continue;
				var pairs = new List<string>();
				for (var k = i + 1; k + 1 < rest.Count && !rest[k].StartsWith("End of", StringComparison.Ordinal); k += 2) pairs.Add($"{rest[k]} {rest[k + 1]}");
				Console.WriteLine(L($"    Öngörü, {rest[i]}: {string.Join(" · ", pairs)}"));
			}
			return;
		}

		// The budgets the board has set: what there is to spend and what has gone.
		if (rest[0] == "Recruitment Budgets" && rest.Contains("Available"))
		{
			Console.WriteLine(L("\n  Ek: bütçeler"));
			foreach (var (label, name) in new[] { ("Transfer", "Transfer"), ("Wage", L("Maaş")), ("Scouting", "Scouting") })
			{
				var at = rest.IndexOf(label);
				if (at < 0 || at + 1 >= rest.Count) continue;
				var note = at + 3 < rest.Count && rest[at + 2] == "Available" ? $"  ({rest[at + 3]})" : "";
				Console.WriteLine(L($"    {name,-9}{rest[at + 1]} kullanılabilir{note}"));
			}
			if (rest.IndexOf("Overall Balance") is > 0 and var balance) Console.WriteLine(L($"    Kasa     {rest[balance - 1]}"));
			Console.WriteLine(L("    Ayrıntı: budget · finances"));
			return;
		}

		// The club's vision: each objective with how much it matters and how it is going.
		if (rest[0] == "Club Vision" && rest.Contains("Actionable Objectives"))
		{
			Console.WriteLine(L("\n  Ek: kulüp vizyonu"));
			string[] weights = ["Required", "Favoured", "Desired", "Preferred"];
			for (var i = 1; i + 2 < rest.Count; i++)
			{
				if (rest[i] is "Actionable Objectives" or "Informational Objectives") { Console.WriteLine($"    {(rest[i] == "Actionable Objectives" ? L("Yapılacak hedefler") : L("Bilgi amaçlı hedefler"))}"); continue; }
				if (!weights.Contains(rest[i + 1])) continue;
				Console.WriteLine($"      {rest[i]} — {rest[i + 1] switch { "Required" => L("zorunlu"), "Favoured" => L("tercih"), "Desired" => L("istenen"), var w => w }} · {rest[i + 2]}");
				i += 2;
			}
			Console.WriteLine(L("    Ayrıntı: club"));
			return;
		}

		// The season's injuries: how many against what was expected, how bad, and the league's worst hit.
		if (rest[0] == "Season Summary" && rest.Count > 2 && rest[1].Contains("injuries this season", StringComparison.Ordinal))
		{
			string After(string label) => rest.IndexOf(label) is >= 0 and var at && at + 1 < rest.Count ? rest[at + 1] : "";
			Console.WriteLine(L($"\n  Ek: sezonun sakatlıkları — {rest[1]}"));
			Console.WriteLine(L($"    Beklenene göre {After("Expected injuries:")} · geçen sezona göre {After("Last Season")}"));
			Console.WriteLine(L($"    Ağırlık: {string.Join(" · ", new[] { L("Major"), L("Moderate"), L("Minor"), "Slight" }.Where(k => After(k).Length > 0).Select(k => $"{k} {After(k)}"))}"));
			var worst = rest.IndexOf("Injury Table");
			var teams = new List<string>();
			for (var i = worst + 1; worst > 0 && i + 1 < rest.Count && int.TryParse(rest[i + 1], out _); i += 2) teams.Add($"{rest[i]} {rest[i + 1]}");
			if (teams.Count > 0) Console.WriteLine(L($"    Ligde en çok sakatlık: {string.Join(" · ", teams)}"));
			return;
		}

		// An offer for one of ours: the fee, its terms as label and value, and who else is after him.
		if (One("Person.PlayerDescription") is { Length: > 0 } described && One("feestring") is { Length: > 0 } fee && rest.Contains("Additional Details"))
		{
			// Read by data path: the player and his description, the fee, the report's verdict, his
			// transfer and loan status, the clubs that have bid.
			Console.WriteLine(L($"\n  Ek: teklif — {One("Person.Name")} ({described})"));
			Console.WriteLine(L($"    Bedel: {fee}"));
			// The terms: each a term ("binding.PropertyValue") and what it says ("binding.DisplayValue").
			for (var i = 0; i + 1 < page.Count; i++)
				if (page[i].Path == "binding.PropertyValue" && page[i + 1].Path == "binding.DisplayValue")
					Console.WriteLine($"    {GameWords.Of(page[i].Text)}: {page[i + 1].Text}");
			// Our player's carries the coach's view of him; one we bid for, the scouts' ("Scout Report",
			// "Scouting Report"). The report's heading and the grade's letter are drawn without a path
			// just before the verdict.
			var verdictAt = page.FindIndex(t => t.Path == "report.ScoutedGradeInfo.GradeString");
			if (verdictAt > 0)
			{
				var lettered = page[verdictAt - 1].Path.Length == 0 && page[verdictAt - 1].Text.Length <= 2;
				var grade = lettered ? page[verdictAt - 1].Text + " · " : "";
				var heading = verdictAt - (lettered ? 2 : 1) is >= 0 and var h ? page[h].Text : "";
				Console.WriteLine(heading == "Coach Report" ? L($"    Antrenör görüşü: {grade}{page[verdictAt].Text}") : L($"    Gözlemci görüşü: {grade}{page[verdictAt].Text}"));
			}
			var listed = new[] { ("Transfer Status", "Player.TransferStatusDetails.TransferStatusDescription.Text"), ("Loan Status", "Player.LoanStatusDetails.TransferStatusDescription.Text") }
				.Where(x => One(x.Item2).Length > 0).Select(x => $"{GameWords.Of(x.Item1)}: {One(x.Item2)}").ToList();
			if (listed.Count > 0) Console.WriteLine($"    {string.Join(" · ", listed)}");
			var bidders = new List<string>();
			for (var i = 0; i < page.Count; i++)
				if (page[i].Path == "row.TransferOfferType" && i > 0 && page[i - 1].Path == "Club.Name")
					bidders.Add($"{page[i - 1].Text} ({page[i].Text}{(i + 1 < page.Count && page[i + 1].Path == "row.TotalTransferCost" ? $", {page[i + 1].Text}" : "")})");
			if (bidders.Count > 0) Console.WriteLine(L($"    Teklif yapanlar: {string.Join(" · ", bidders)}   (hepsi: offers)"));
			return;
		}

		// The loans about to end: who, where he has been and how he has played there.
		if (rest[0] == "Loans" && rest.IndexOf("Rating") is > 0 and var loanHead)
		{
			Console.WriteLine(L($"\n  Ek: kiralıklar — {rest[1]}"));
			for (var i = loanHead + 1; i + 2 < rest.Count && rest[i] != "Action message"; i += 3)
				Console.WriteLine(L($"    {rest[i],-22}{rest[i + 1],-22}ortalama {rest[i + 2]}"));
			return;
		}

		// A staff meeting's summary: what kind it was and the topics raised, each with how many items.
		if (rest[0] == "Advice Summary" && rest.IndexOf("Advice Topics") is > 0 and var topicsAt)
		{
			Console.WriteLine(L($"\n  Ek: tavsiye özeti — {(rest.IndexOf("Meeting type") is >= 0 and var kindAt && kindAt + 1 < rest.Count ? rest[kindAt + 1] : "")}"));
			var topics = rest.Skip(topicsAt + 1).TakeWhile(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"\(\d+\)$")).ToList();
			foreach (var topic in topics) Console.WriteLine($"    {GameWords.Of(topic)}");
			if (AttachedStaffTeams(page) is { Count: > 0 } teams) Console.WriteLine(L($"    Personel: {string.Join(" · ", teams)}"));
			return;
		}

		// The assistant's team for the next match: a position to a line, the man he picks, the one he
		// would replace and why ("teamselectionadviceentry.TeamSelectionReason"); the names and the
		// position are drawn without a path before the reason.
		if (page.Any(t => t.Path == "teamselectionadviceentry.TeamSelectionReason"))
		{
			Console.WriteLine(L("\n  Ek: önerilen kadro"));
			var rows = new List<string[]>();
			var from = 0;
			for (var i = 1; i < page.Count; i++)
			{
				if (page[i].Path != "teamselectionadviceentry.TeamSelectionReason") continue;
				var plain = page.Skip(from).Take(i - from).Where(t => t.Path.Length == 0).Select(t => t.Text).ToList();
				from = i + 1;
				if (plain.Count < 2) continue;
				rows.Add([GameWords.Of(plain[0]), plain[1], plain.Count > 2 ? plain[2] : "", page[i].Text]);
			}
			// The ones the assistant keeps have no reason: a position and a name between two that do.
			Table.Write([L("MEVKİ"), L("OYUNCU"), L("YERİNE"), L("NEDEN")], rows);
			return;
		}

		// The squad's dynamics: each heading with its grade, then its pluses and minuses
		// ("binding.ProConsLongString", the sign drawn before it), then the issues and happiness lines.
		if (rest[0] == "Squad Dynamics" && page.Any(t => t.Path == "binding.ProConsLongString"))
		{
			Console.WriteLine(L("\n  Ek: takım dinamikleri"));
			var stop = page.FindIndex(1, t => t.Path.Length == 0 && t.Text == "Player Report");
			var part = stop > 0 ? page.Take(stop).ToList() : page;
			for (var i = 1; i < part.Count; i++)
			{
				var (text, path) = part[i];
				if (path == "binding.ProConsLongString")
					Console.WriteLine($"      {(part[i - 1].Text == "-" ? "-" : "+")} {GameWords.Of(text)}");
				else if (path.Length == 0 && text is not ("+" or "-") && i + 1 < part.Count && part[i + 1].Path.Length == 0 && part[i + 1].Text is not ("+" or "-"))
				{
					Console.WriteLine($"    {GameWords.Of(text)}: {GameWords.Of(part[i + 1].Text)}");
					i++;
				}
				else if (path.Length == 0 && text is not ("+" or "-")) Console.WriteLine($"      {GameWords.Of(text)}");
			}
			return;
		}

		// The squad numbers' card: how many are given out; the numbers themselves are numbers' to show.
		if (One("Team.SquadNumbersAssigned") is { Length: > 0 } assigned)
		{
			Console.WriteLine(L($"\n  Ek: forma numaraları — {assigned} oyuncuya numara verilmiş. Ayrıntı ve değiştirmek: numbers"));
			return;
		}

		// A scout's report on a player (an agent's or a scout's pick): the grade and verdict, how well he is
		// known, his ability now and to come, the good and the bad said of him, then who he is and his terms.
		// By data path; the grade's letter and the contract's end are drawn without one.
		if (rest[0] == "Scout Report" && One("report.ScoutedGradeInfo.GradeString") is { Length: > 0 } scouted)
		{
			var at = page.FindIndex(t => t.Path == "report.ScoutedGradeInfo.GradeString");
			var grade = at > 0 && page[at - 1].Path.Length == 0 && page[at - 1].Text.Length <= 2 ? page[at - 1].Text + " · " : "";
			Console.WriteLine(L($"\n  Ek: gözlemci raporu — {grade}{scouted}"));
			Console.WriteLine(L($"    {One("report.ScoutingKnowledgeLevel.Text")} · şu an: {One("report.ScoutedCurrentAbilityInfo.ShortString")} · gelecek: {One("report.ScoutedPotentialAbilityInfo.ShortString")}"));
			// The good comes first, the bad after the heading that counts it ("Negatives (3)").
			var bad = page.FindIndex(t => t.Path.Length == 0 && t.Text.StartsWith("Negatives", StringComparison.Ordinal));
			for (var i = 0; i < page.Count; i++)
				if (page[i].Path == "row.ProConsLongString") Console.WriteLine($"    {(bad >= 0 && i > bad ? "-" : "+")} {page[i].Text.Replace("\n", "\n      ")}");
			var age = page.FindIndex(t => t.Path == "Person.PositionCombinedString") is >= 0 and var p && p + 1 < page.Count && page[p + 1].Path.Length == 0 ? page[p + 1].Text : "";
			Console.WriteLine("    " + string.Join(" · ", new[] { One("Person.Name"), One("Person.PositionCombinedString"), age, One("Nation.ThreeLetterName"), One("Club.ShortName") }.Where(x => x.Length > 0)));
			var expires = page.FindIndex(t => t.Path.Length == 0 && t.Text == "Contract Expires") is >= 0 and var c && c + 1 < page.Count && page[c + 1].Path.Length == 0 ? page[c + 1].Text : "";
			Console.WriteLine(L($"    Değer {One("binding.TransferValueRangeString")} · ücret {One("Person.FullContract.Wage.DisplayValue")}{(expires.Length > 0 ? L($" · sözleşme {expires}'e kadar") : "")}"));
			return;
		}

		// A player's card (one who is unhappy, say): who he is, his value and wage, how happy he is
		// and the squad's mood. By data path; the age, the contract's end and the moods are drawn
		// without one, each after its label.
		if (rest[0] == "Player Report" && One("Person.Name") is { Length: > 0 } reported)
		{
			string Next(string label) => page.FindIndex(t => t.Path.Length == 0 && t.Text == label) is >= 0 and var at && at + 1 < page.Count && page[at + 1].Path.Length == 0 ? GameWords.Of(page[at + 1].Text) : "";
			var age = page.FindIndex(t => t.Path == "Person.PositionCombinedString") is >= 0 and var p && p + 1 < page.Count && page[p + 1].Path.Length == 0 ? page[p + 1].Text : "";
			Console.WriteLine(L($"\n  Ek: oyuncu raporu — {string.Join(", ", new[] { reported, One("Person.PositionCombinedString"), age, One("Nation.ThreeLetterName"), One("Club.ShortName") }.Where(x => x.Length > 0))}"));
			Console.WriteLine(L($"    Değer {One("binding.TransferValueRangeString")} · ücret {One("Person.FullContract.Wage.DisplayValue")} · sözleşme {Next("Contract Expires")}'e kadar"));
			Console.WriteLine(L($"    Mutluluk: {Next("Happiness")} ({One("HappinessPositivesCount")} artı, {One("HappinessNegativesCount")} eksi) · takım havası: {Next("Squad Dynamics")}"));
			if (One("Team.ClubAtmosphereFeedback.CurrentDynamicSupportStringLong") is { Length: > 0 } mood) Console.WriteLine($"    {mood}");
			return;
		}

		// A player's training report: how he is getting on, the attributes that moved (by data path), his
		// training rating and unit beside it, and who he is.
		if (rest[0] == "Progress Report" && page.Any(t => t.Path == "Person.Name"))
		{
			var moved = new List<string>();
			for (var i = 0; i < page.Count; i++)
				if (page[i].Path == "row.Name")
					moved.Add(i + 1 < page.Count && page[i + 1].Path == "row.PropertyValue" ? $"{GameWords.Of(page[i].Text)} {page[i + 1].Text}" : GameWords.Of(page[i].Text));
			Console.WriteLine(L("\n  Ek: ") + rest[0] + (rest.Count > 1 && rest[1] != "Attribute" ? " — " + rest[1] : ""));
			if (moved.Count > 0) Console.WriteLine("    " + string.Join(" · ", moved));
			// The rating is the figure drawn before its label; the unit stands last on the card, before the player's.
			var rating = rest.FindIndex(t => t == "T.Rat") is > 0 and var r && rest[r - 1].Any(char.IsDigit) ? rest[r - 1] : "";
			var unit = rest.IndexOf("Player Report") is > 0 and var card && rest[card - 1] is not ("T.Rat" or "N/A") ? rest[card - 1] : "";
			if (rating.Length > 0 || unit.Length > 0)
				Console.WriteLine(L($"    Antrenman puanı {(rating.Length > 0 ? rating : "-")}{(unit.Length > 0 ? $" · {unit}" : "")}"));
			var age = page.FindIndex(t => t.Path == "Person.PositionCombinedString") is >= 0 and var p && p + 1 < page.Count && page[p + 1].Path.Length == 0 ? page[p + 1].Text : "";
			Console.WriteLine("    " + string.Join(" · ", new[] { One("Person.Name"), One("Person.PositionCombinedString"), age, One("Nation.ThreeLetterName"), One("Club.ShortName") }.Where(x => x.Length > 0)));
			return;
		}

		// The week's training: how many came on and how many went back, the best and the worst by
		// their training rating. Read by data path: a player's name starts his line, his positions
		// are on it, the rating is the figure drawn after it without a path.
		if (rest[0] == "Training Recap")
		{
			var best = new List<string>();
			var worst = new List<string>();
			var into = best;
			foreach (var (text, path) in page)
			{
				if (path.Length == 0 && text == "Worst") into = worst;
				else if (path == "Person.Name") into.Add(text);
				else if (into.Count > 0 && (path.EndsWith("PositionCombinedString", StringComparison.Ordinal)
				         || path.Length == 0 && System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+[.,]\d+$")))
					into[^1] += $" {(path.Length > 0 ? $"({text})" : text)}";
			}
			var up = One("individualtraining.PlayerImprovementCount");
			var down = One("individualtraining.PlayerRegressionCount");
			Console.WriteLine(L($"\n  Ek: antrenman özeti — {(up.Length > 0 ? up : "0")} oyuncu gelişti, {(down.Length > 0 ? down : "0")} oyuncu geriledi"));
			if (best.Count > 0) Console.WriteLine(L($"    En iyi: {string.Join(" · ", best)}"));
			if (worst.Count > 0) Console.WriteLine(L($"    En kötü: {string.Join(" · ", worst)}"));
			return;
		}

		// The medical centre's note: who is back from injury and how his fitness test went, a
		// player to a line by data path ("binding.Name", the injury bound to "binding"); the
		// test's verdict is the word drawn after them without a path.
		if (rest.Contains("Pre-match fitness tests:") && page.Any(t => t.Path == "binding.Name"))
		{
			var watched = page.TakeWhile(t => t.Text != "Pre-match fitness tests:").Skip(1).FirstOrDefault(t => t.Path.Length == 0 && t.Text.Any(char.IsDigit)).Text;
			var window = One("Team.PreMatchFitnessTestString");
			Console.WriteLine(L($"\n  Ek: {GameWords.Of(rest[0])}{(watched is { Length: > 0 } ? $" — {watched}" : "")}{(window.Length > 0 ? L($" · maç öncesi kondisyon testi: {window}") : "")}"));
			var rows = new List<string[]>();
			for (var i = 0; i < page.Count; i++)
			{
				if (page[i].Path != "binding.Name") continue;
				var injury = i + 1 < page.Count && page[i + 1].Path == "binding" ? page[i + 1].Text : "";
				var at = injury.Length > 0 ? i + 2 : i + 1;
				var test = at < page.Count && page[at].Path.Length == 0 ? GameWords.Of(page[at].Text) : "";
				rows.Add([page[i].Text, injury, test]);
			}
			if (rows.Count > 0) Table.Write([L("OYUNCU"), L("SAKATLIK"), L("KONDİSYON TESTİ")], rows);
			return;
		}

		// The report on the next opponent: where they stand, how they line up, their best men, who is
		// doubtful and how their last matches went.
		if (rest[0] == "Next Opposition Report" && rest.Count > 3)
		{
			// Read by each text's data path (FINDINGS §8.169): the formation, the manager, the team news,
			// the expected eleven and the strengths and weaknesses, two runs of pros and cons.
			Console.WriteLine(L($"\n  Ek: rakip raporu — {rest[1]} {rest[2]}{(One("Team.Description") is { Length: > 0 } d ? $" · {d}" : "")}"));
			if (One("Team.MostUsedFormation") is { Length: > 0 } shape) Console.WriteLine(L($"    Diziliş: {shape}"));
			if (One("Manager.InitialSurname") is { Length: > 0 } manager)
				Console.WriteLine(L($"    Menajer: {string.Join(" · ", new[] { manager, One("Manager.PreferredFormationString"), One("Manager.PreferredMentality") }.Where(x => x.Length > 0))}"));
			var news = page.Where(t => t.Path == "row.Text").Select(t => t.Text).ToList();
			if (news.Count > 0) Console.WriteLine(L($"    Takım haberleri: {string.Join(" · ", news)}"));
			var eleven = page.Where(t => t.Path == "Position.Player.Name").Select(t => t.Text).ToList();
			if (eleven.Count > 0) Console.WriteLine(L($"    Muhtemel 11{(One("tactic.FormationType") is { Length: > 0 } f ? $" ({f})" : "")}: {string.Join(", ", eleven)}"));
			// Pros and cons: the strengths' run, then the weaknesses', with a heading between them.
			var runs = new List<List<string>>();
			var apart = true;
			foreach (var (text, path) in page)
			{
				if (path.Length == 0) apart = true;
				if (path != "procon.ProConsShortString") continue;
				if (apart) runs.Add([]);
				runs[^1].Add(text);
				apart = false;
			}
			string[] heads = [L("Güçlü yanları"), L("Zayıf yanları")];
			for (var r = 0; r < runs.Count && r < heads.Length; r++) Console.WriteLine($"    {heads[r]}: {string.Join(" · ", runs[r])}");
			Console.WriteLine(L("    Ayrıntı: opponent"));
			return;
		}

		// Who has just become home-grown and who is about to, each with his age, nation and squad.
		// The card names itself by the game's own section heading; its rows are the players the game
		// binds ("binding.Name"), each followed by his age, nation, squad, status and date — so neither
		// the heading nor the age's sentence ("19 years old" / "19 yaşında") is read. The columns bind
		// one after another, so the read is taken again until every row has its five cells.
		if (rest.Contains("Upcoming Status Changes"))
		{
			List<(string Name, List<string> Cells)> Rows() 
			{
				var rows = new List<(string, List<string>)>();
				for (var i = 0; i < page.Count; i++)
				{
					if (page[i].Path != "binding.Name") continue;
					var cells = new List<(string Text, string Path)>();
					for (var j = i + 1; j < page.Count && page[j].Path != "binding.Name"; j++) cells.Add(page[j]);
					// The panel beside the card (the squad's own lines) follows the last row; a row is
					// cut at its third bound cell (its squad, status and date are the bound ones).
					var ended = cells.FindIndex(c => c.Path == "binding") is var first && first >= 0
						&& cells.FindIndex(first + 1, c => c.Path == "binding") is var second && second >= 0
						? cells.FindIndex(second + 1, c => c.Path == "binding") + 1 : 0;
					rows.Add((page[i].Text, ended > 0 ? cells.Take(ended).Select(c => c.Text).ToList() : []));
				}
				return rows;
			}
			var rows = Rows();
			for (var attempt = 0; attempt < 12 && (rows.Count == 0 || rows.Any(r => r.Cells.Count != 5)); attempt++)
			{
				await Task.Delay(250);
				if (await OpenMessage(title, t => t.Path == "binding.Name", seconds: 3) is { Count: > 0 } filled) page = filled;
				rows = Rows();
			}
			Console.WriteLine(L("\n  Ek: altyapı (home-grown) statüsü"));
			foreach (var (who, cells) in rows.Where(r => r.Cells.Count == 5))
			{
				var said = string.Join(" · ", cells.Skip(3).Where(t => t != "-"));
				Console.WriteLine($"      {who} ({cells[0].Split(' ')[0]}, {cells[1]}, {cells[2]}){(said.Length > 0 ? $" — {said}" : "")}");
			}
			return;
		}

		// The weekly roundup of what is delegated: how much is ours and how much the staff's, then the
		// areas with the number of items each has to report.
		if (rest[0] == "Responsibilities" && rest.Contains("Delegated to Staff"))
		{
			string Count(string path) => One(path) is { Length: > 0 } n ? n : "?";
			Console.WriteLine(L($"\n  Ek: sorumluluklar — {Count("teamsettings.ResponsibilitiesAssignedToHumanCount")} sende · {Count("teamsettings.ResponsibilitiesAssignedToStaffCount")} devredilmiş"));
			// The staff's head count and vacancies are sentences with a figure ("49 Staff Members"), up to the first area.
			var staffAt = rest.IndexOf("Staff");
			var people = rest.Skip(staffAt + 1).TakeWhile(t => t.Any(char.IsDigit) && !System.Text.RegularExpressions.Regex.IsMatch(t, @"^\(\d+\)$")).ToList();
			if (staffAt > 0 && people.Count > 0) Console.WriteLine(L($"    Personel: {string.Join(" · ", people)}"));
			var areas = new List<string>();
			for (var i = 1; i + 1 < rest.Count; i++)
				if (System.Text.RegularExpressions.Regex.IsMatch(rest[i + 1], @"^\(\d+\)$")) areas.Add($"{rest[i]} {rest[i + 1]}");
			if (areas.Count > 0) Console.WriteLine(L($"    Bildirilen: {string.Join(" · ", areas)}"));
			Console.WriteLine(L("    Ayrıntı: responsibilities"));
			return;
		}

		// The coming two weeks of training, a day to a line, then who is coming on and who is not.
		if (rest[0] == "Training" && page.Any(t => t.Path == "day.DayOfTheMonth"))
		{
			// Read by data path: a day's date, then its sessions; a match is its venue's letter.
			Console.WriteLine(L("\n  Ek: antrenman programı"));
			string[] days = [L("Pzt"), "Sal", L("Çar"), "Per", "Cum", L("Cmt"), L("Paz")];
			var grid = new List<(string Day, List<string> Sessions)>();
			foreach (var (text, path) in page)
				if (path == "day.DayOfTheMonth") grid.Add((text, []));
				else if (grid.Count > 0 && path == "Event.SessionName") grid[^1].Sessions.Add(text switch { "Rest" => L("dinlenme"), "Travel" => L("yolculuk"), _ => text });
				else if (grid.Count > 0 && path == "Event.MatchVenueShort")
					grid[^1].Sessions.Add(text switch { "H" => L("MAÇ (iç saha)"), "A" => L("MAÇ (deplasman)"), "N" => L("MAÇ (tarafsız)"), _ => L($"MAÇ ({text})") });
			// The grid starts on a Monday.
			for (var day = 0; day < grid.Count; day++)
				Console.WriteLine($"    {days[day % 7]} {grid[day].Day,-7} {string.Join(" · ", grid[day].Sessions)}");
			var parts = new List<string>();
			if (One("individualtraining.PlayerImprovementCount") is { Length: > 0 } up) parts.Add(L($"{up} gelişen"));
			if (One("individualtraining.PlayerRegressionCount") is { Length: > 0 } down) parts.Add(L($"{down} gerileyen"));
			var names = page.SkipWhile(t => t.Text != "Notable Developments").Where(t => t.Path == "Person.Name").Select(t => t.Text).ToList();
			if (names.Count > 0) parts.Add(L($"öne çıkanlar: {string.Join(", ", names)}"));
			if (parts.Count > 0) Console.WriteLine(L($"    Bireysel antrenman: {string.Join(" · ", parts)}"));
			Console.WriteLine(L("    Değiştirmek: schedule"));
			return;
		}

		// The scouts' focuses: how many, by priority, and each with where it stands.
		if (rest[0] == "Recruitment Focuses" && page.Any(t => t.Path == "focusitem.Name"))
		{
			// By data path: each priority's count with its name drawn after it ("Ongoing" can come back
			// as another English, "Obscure"), then each focus with its state and how many it found.
			var priorities = new List<string>();
			for (var i = 0; i + 1 < page.Count; i++)
				if (page[i].Path.StartsWith("Human.ScoutingInfo.RecruitmentFocuses.", StringComparison.Ordinal) && page[i].Path.EndsWith("FocusesCount", StringComparison.Ordinal))
					priorities.Add($"{page[i].Text} {GameWords.Of(page[i + 1].Text)}");
			var summary = page.Skip(1).TakeWhile(t => t.Path.Length == 0).Select(t => t.Text);
			Console.WriteLine(L($"\n  Ek: scouting odakları — {string.Join(" · ", summary)}  ({string.Join(" · ", priorities)})"));
			for (var i = 0; i < page.Count; i++)
			{
				if (page[i].Path != "focusitem.Name") continue;
				var after = page.Skip(i + 1).TakeWhile(t => t.Path.Length == 0).Take(2).Select(t => GameWords.Of(t.Text)).ToList();
				Console.WriteLine($"    {page[i].Text}{(after.Count > 0 ? $" — {string.Join(", ", after)}" : "")}");
			}
			Console.WriteLine(L("    Ayrıntı: scout"));
			return;
		}

		// A match's card: the score with who played, the scorers, the numbers and who stood out.
		if (rest[0] == "Match Report" && rest.FindIndex(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+ - \d+$")) is > 1 and var result && result + 1 < rest.Count)
		{
			Console.WriteLine(L($"\n  Ek: maç raporu — {rest[result - 1]} {rest[result]} {rest[result + 1]}  ({rest[1]}, {rest[2]})"));
			var stats = rest.IndexOf("Match Stats");
			var scorers = new List<string>();
			for (var i = result + 2; i + 1 < (stats > 0 ? stats : rest.Count) && rest[i + 1].EndsWith('\''); i += 2) scorers.Add($"{rest[i]} {rest[i + 1]}");
			if (scorers.Count > 0) Console.WriteLine(L($"    Goller: {string.Join(", ", scorers)}"));
			var numbers = new List<string>();
			static bool Number(string t) => t.Length > 0 && char.IsDigit(t[0]);
			for (var i = stats + 1; stats > 0 && i + 2 < rest.Count && !Number(rest[i]) && Number(rest[i + 1]) && Number(rest[i + 2]); i += 3)
				numbers.Add($"{rest[i] switch { "Shots" => L("Şut"), "Shots On Target" => L("İsabetli şut"), "xG" => L("Gol beklentisi"), "Corners" => L("Korner"), "Possession" => L("Topla oynama"), "Fouls" => L("Faul"), var other => other }} {rest[i + 1]} - {rest[i + 2]}");
			if (numbers.Count > 0) Console.WriteLine($"    {string.Join(" · ", numbers)}");
			var key = rest.IndexOf("Key Players");
			var stood = new List<string>();
			for (var i = key + 1; key > 0 && i + 1 < rest.Count && rest[i] != "Action message" && rest[i + 1] != "Action message"; i += 2) stood.Add($"{rest[i]} ({rest[i + 1]})");
			if (stood.Count > 0) Console.WriteLine(L($"    Öne çıkanlar: {string.Join(", ", stood)}"));
			return;
		}

		// The intake's card: how many came, and the best of them with the group each is put in.
		if (rest[0] == "Youth Candidates" && rest.IndexOf("Prospect") is > 0 and var prospect)
		{
			Console.WriteLine(L($"\n  Ek: altyapı alımı — {string.Join(" · ", rest.Skip(1).Take(prospect - 2).Reverse())}"));
			var groups = new[] { "Elite", "Top", "Good", "Decent", "Poor" };
			for (var i = prospect + 1; i + 1 < rest.Count && groups.Contains(rest[i + 1]); i += 2)
				Console.WriteLine($"    {rest[i + 1],-7}{rest[i]}");
			Console.WriteLine(L("    Hepsi: youth candidates"));
			return;
		}

		// Who is away with his country, for what and when; then the fixtures around it.
		if (rest[0] == "Internationals" && rest.Contains("Current and Recent Events"))
		{
			var events = rest.IndexOf("Current and Recent Events");
			var fixtures = rest.IndexOf("Fixture Schedule");
			var end = fixtures > events ? fixtures : rest.Count;
			Console.WriteLine(L("\n  Ek: milli takımdakiler"));
			// An event, its dates, then everyone it takes: one man or several ("FIFA World Cup™",
			// "11th Jun-19th Jul", "Kaide Gordon", "James Collins"). Read as threes, the second man of an
			// event was taken for the next event's name (30 Mayıs 2026).
			static bool Dates(string t) => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+\w* \w+-\d+\w* \w+$");
			for (var i = events + 1; i + 1 < end; )
			{
				if (!Dates(rest[i + 1])) { i++; continue; }
				var called = new List<string>();
				var k = i + 2;
				for (; k < end && !(k + 1 < end && Dates(rest[k + 1])); k++) called.Add(rest[k]);
				Console.WriteLine($"    {rest[i]} ({rest[i + 1]}): {string.Join(", ", called)}");
				i = k;
			}
			if (fixtures > 0)
			{
				// Played ones have a score; those to come, a kick-off time.
				var coming = new List<string>();
				for (var i = fixtures + 1; i + 4 < rest.Count && rest[i + 3] is "H" or "A"; i += 5)
					if (rest[i + 4].Contains(':'))
						coming.Add($"{rest[i]} {rest[i + 2]} ({(rest[i + 3] == "H" ? L("iç saha") : L("deplasman"))}) {rest[i + 4]}");
				if (coming.Count > 0) Console.WriteLine(L($"    Sıradaki maçlar: {string.Join(" · ", coming)}"));
			}
			return;
		}

		// An injury's card: what it is, how it came (in a match, in training), how he is, then what each
		// answer would mean, in the answers' order.
		if (rest[0] == "Fitness" && rest.Any(t => t.Contains("Received in", StringComparison.Ordinal)))
		{
			var parts = rest.Skip(1).SelectMany(t => t.Split('\n')).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
			var received = parts.FindIndex(t => t.StartsWith("Received in", StringComparison.Ordinal));
			var first = parts.FindIndex(t => t.StartsWith("Out for", StringComparison.Ordinal) || t.StartsWith("Available for", StringComparison.Ordinal));
			if (first < 0) first = parts.Count;
			// Sharpness and condition stand just before the outcomes.
			var states = first >= received + 3 ? parts.Skip(first - 2).Take(2).ToList() : [];
			Console.WriteLine(L($"\n  Ek: {parts.FirstOrDefault()}{(received > 1 ? $" — {parts[1]}" : "")}"));
			if (received >= 0) Console.WriteLine($"    {string.Join(" ", parts.Skip(received).Take(first - states.Count - received))}");
			if (states.Count > 0) Console.WriteLine($"    {string.Join(" · ", states)}");
			// The set keeps the answers in the order they were put in, the order they are shown.
			var said = answers.ToList();
			var outcomes = parts.Skip(first).ToList();
			for (var i = 0; i < outcomes.Count; i++) Console.WriteLine($"    {(i < said.Count ? said[i] : L($"{i + 1}. seçenek"))}: {outcomes[i]}");
			return;
		}

		// A scouting card: who he is, his fitness, the coach's grade and verdict, the clubs after him.
		if (rest[0] == "Report" && rest.Contains("Coach Report"))
		{
			string After(string label, int skip = 1) => rest.IndexOf(label) is var i and >= 0 && i + skip < rest.Count ? rest[i + skip] : "";
			var who = rest.Skip(1).TakeWhile(t => t != "Fitness").Where(t => t != "-").Select(t => t.Replace(" years old", L(" yaş"))).ToList();
			Console.WriteLine(L($"\n  Ek: {string.Join(" · ", who)}"));
			if (After("Fitness") is { Length: > 0 } fitness) Console.WriteLine(L($"    Kondisyon: {fitness}"));
			var grade = After("Coach Report");
			var verdict = After("Coach Report", 2);
			if (grade.Length > 0) Console.WriteLine(L($"    Antrenör notu: {grade}{(verdict is { Length: > 0 } and not "Current Ability" ? $" — {verdict}" : "")}"));
			if (After("Interested Clubs") is { Length: > 0 } clubs) Console.WriteLine(L($"    İlgilenen kulüp: {clubs.Trim('(', ')')}"));
			return;
		}

		// The contracts running out: a row from the player's name ("binding.Name") to the action recommended
		// ("row.DisplayValue"), his positions and wage between; the abilities are stars, not text. Only the
		// rows drawn are bound; the heading counts them all.
		if (page.Any(t => t.Path == "binding.Name") && page.Any(t => t.Path == "row.DisplayValue") && rest.Contains("Recommended Action"))
		{
			var rows = new List<string[]>();
			for (var i = 0; i < page.Count; i++)
			{
				if (page[i].Path != "binding.Name") continue;
				var row = page.Skip(i + 1).TakeWhile(t => t.Path is "binding" or "row.DisplayValue" or "").ToList();
				var cells = row.Where(t => t.Path == "binding").Select(t => t.Text).ToList();
				var action = row.FirstOrDefault(t => t.Path == "row.DisplayValue").Text ?? "";
				rows.Add([page[i].Text, cells.ElementAtOrDefault(0) ?? "", cells.ElementAtOrDefault(1) ?? "", action]);
			}
			Console.WriteLine(L($"\n  Ek: sözleşmesi bitecekler — {rest[0]}{(rows.Count > 0 ? L($", ekranda görünen {rows.Count}") : "")}"));
			Table.Write([L("OYUNCU"), L("MEVKİ"), L("MAAŞ"), L("ÖNERİLEN")], rows);
			Console.WriteLine(L("    Hepsi ve sözleşme teklifi: contracts"));
			return;
		}

		// The squad's win bonuses for the season: for the league and for the cups, low, normal or high, by
		// data path; the one chosen is the button whose row style is the active one (#low-, #middle-,
		// #high-button), each row found by its own figure.
		if (page.Any(t => t.Path == "collectivewinbonus.BonusLeagueData.BonusLowAmount"))
		{
			Console.WriteLine(L("\n  Ek: kadronun galibiyet primleri (artış ya da düşüş transfer bütçesine yansır)"));
			foreach (var (name, data) in new[] { (One("collectivewinbonus.BonusLeagueString"), "BonusLeagueData"), (GameWords.Of("Domestic Cups"), "BonusCupData") })
			{
				var levels = new[] { "BonusLowAmount", "BonusNormalAmount", "BonusHighAmount" }.Select(l => One($"collectivewinbonus.{data}.{l}")).ToList();
				if (levels.All(l => l.Length == 0)) continue;
				var chosen = -1;
				try
				{
					var tree = await Fm.Get("elements", ("text", GameWords.Of(levels[1])), ("up", "3"), ("depth", "2"), ("members", "1"));
					var styles = System.Text.RegularExpressions.Regex.Matches(tree, @"m_addedClass = ""([^""]*)""").Select(m => m.Groups[1].Value).ToList();
					chosen = styles.FindIndex(s => s.Contains("-active-", StringComparison.Ordinal));
				}
				catch (FmError) { }
				Console.WriteLine($"    {name}: {string.Join(" · ", levels.Select((l, i) => i == chosen ? $"[{GameWords.Of(l)}]" : GameWords.Of(l)))}");
			}
			return;
		}

		// The match preparation for the next game: each focus with the phase or slot it is for, by data path
		// (a "binding" heading, its sentence, then the focus); the cards beside it are the opponent's and the
		// calendar's, said elsewhere.
		if (page.Any(t => t.Path == "row.MatchFocusSessionName"))
		{
			Console.WriteLine(L($"\n  Ek: maç hazırlığı — {rest[0]} ({One("fixture.NextMatchDaysText")}, {One("comp.Name")})"));
			for (var i = 0; i < page.Count; i++)
			{
				if (page[i].Path != "row.MatchFocusSessionName") continue;
				var heading = i >= 2 && page[i - 2].Path == "binding" ? page[i - 2].Text : "";
				Console.WriteLine($"    {(heading.Length > 0 ? $"{heading}: " : "")}{page[i].Text}");
			}
			Console.WriteLine(L("    Değiştirmek: prep"));
			return;
		}

		// The analysts' look at a match: the result, the goals, the expected goals, the key findings and
		// the tactics, by data path; the xG figures are the two after their label.
		if (rest[0] == "Data Hub" && page.Any(t => t.Path == "row.AnalysisPointString"))
		{
			var crowd = One("fixture.attendance");
			Console.WriteLine(L($"\n  Ek: veri merkezi — {One("fixture.hometeam.Name")} {One("fixture.ResultLinkable")} {One("fixture.awayteam.Name")}  ({One("fixture.competition.name")}, {One("fixture.stadium.nameforcomp")}{(crowd.Length > 0 ? L($", {crowd} seyirci") : "")})"));
			var goals = new List<string>();
			for (var i = 0; i + 1 < page.Count; i++)
				if (page[i].Path == "Event.Player.InitialSurname" && page[i + 1].Path == "Event.TimeString") goals.Add($"{page[i].Text} {page[i + 1].Text}");
			if (goals.Count > 0) Console.WriteLine(L($"    Goller: {string.Join(", ", goals)}"));
			if (rest.IndexOf("XG") is > 0 and var xg && xg + 2 < rest.Count) Console.WriteLine(L($"    Gol beklentisi {rest[xg + 1]} - {rest[xg + 2]}"));
			if (One("Fixture.PlayerOfTheMatch.Name") is { Length: > 0 } best) Console.WriteLine(L($"    Maçın oyuncusu: {best}"));
			// The analysts' sentences are the game's own, which the plugin answers with in English where its
			// table knows the sentence (one with a figure in it comes through as the game wrote it). The words
			// the game showed are asked for once, so a finding is said in the game's language too — a sentence
			// the table does not know is left as it came.
			var points = page.Where(t => t.Path == "row.AnalysisPointString").Select(t => t.Text).ToList();
			await GameWords.Ask(Fm, [.. points]);
			Console.WriteLine(L("    Önemli bulgular:"));
			foreach (var point in points) Console.WriteLine($"      · {GameWords.Of(point)}");
			var tactics = page.Where(t => t.Path is "data.TacticalStylesOption.Name" or "Tactic.FormationName").Select(t => t.Text).ToList();
			if (tactics.Count > 0) Console.WriteLine(L($"    Taktikler: {string.Join(" · ", tactics)}"));
			return;
		}

		// The scouts' latest picks: each with his description, grade and date (by data path), the focuses'
		// counts and what is left of each budget, each figure after its own "Available".
		if (rest[0] == "Player Recommendations" && page.Any(t => t.Path == "row.PlayerDescription"))
		{
			Console.WriteLine(L($"\n  Ek: oyuncu tavsiyeleri — {(rest.Count > 1 ? rest[1] : "")}"));
			for (var i = 0; i < page.Count; i++)
			{
				if (page[i].Path != "row.Name") continue;
				var row = page.Skip(i + 1).TakeWhile(t => t.Path != "row.Name" && t.Text != "Recruitment Focuses").Select(t => t.Text);
				Console.WriteLine($"    {page[i].Text} · {string.Join(" · ", row)}");
			}
			var focuses = rest.IndexOf("Recruitment Focuses");
			if (focuses > 0 && focuses + 2 < rest.Count)
				Console.WriteLine(L($"    Transfer odakları: {rest[focuses + 1]} · {rest[focuses + 2]} ({One("Human.ScoutingInfo.RecruitmentFocuses.TopFocusesCount")} {GameWords.Of("Top")} · {One("Human.ScoutingInfo.RecruitmentFocuses.StandardFocusesCount")} {GameWords.Of("Standard")} · {One("Human.ScoutingInfo.RecruitmentFocuses.OngoingFocusesCount")} {GameWords.Of("Obscure")})"));
			string Left(string budget) => rest.IndexOf(budget, Math.Max(0, rest.IndexOf("Recruitment Budgets"))) is > 0 and var b && b + 2 < rest.Count && rest[b + 1] == "Available" ? rest[b + 2] : "-";
			if (rest.Contains("Recruitment Budgets"))
				Console.WriteLine(L($"    Kalan bütçe: transfer {Left("Transfer")} · maaş {Left("Wage")} · scouting {Left("Scouting")}"));
			Console.WriteLine(L("    Ayrıntı: scout"));
			return;
		}

		// Our player's card (on a bid for him, in a coach's word on his training): who he is, his terms, how
		// he is, then what the card holds of his transfer and loan status and the offers made, or his
		// training; by data path, the age, the contract's end, the rating and the counts drawn without one.
		if (rest[0] == "Report" && One("Person.Name") is { Length: > 0 } bidFor)
		{
			string Plain(string label) => page.FindIndex(t => t.Path.Length == 0 && t.Text == label) is >= 0 and var i && i + 1 < page.Count && page[i + 1].Path.Length == 0 ? page[i + 1].Text : "";
			var age = page.FindIndex(t => t.Path == "Nation.Name") is >= 0 and var n && n + 1 < page.Count && page[n + 1].Path.Length == 0 ? page[n + 1].Text : "";
			var until = page.FirstOrDefault(t => t.Path.Length == 0 && t.Text.StartsWith('(') && t.Text.Any(char.IsDigit) && t.Text.Contains('/')).Text ?? "";
			Console.WriteLine(L("\n  Ek: ") + string.Join(" · ", new[] { bidFor, One("Person.PositionCombinedString"), One("Nation.Name"), age }.Where(x => x.Length > 0)));
			Console.WriteLine(L($"    Değer {One("binding.TransferValueRangeString")} · ücret {One("Person.FullContract.Wage.DisplayValue")} {until}").TrimEnd());
			Console.WriteLine(L($"    Kondisyon: {One("Person.ConditionCommentTypeString")} · moral: {One("Person.MoraleString")} · ortalama puan {Plain("Average Rating")}"));
			if (rest.Contains("T.Rat"))
			{
				// The training card: how he is doing, the rating before its label, the workload and the unit after it.
				var at = rest.IndexOf("T.Rat");
				var how = rest.IndexOf("Training") is > 0 and var tr && tr + 1 < at ? rest[tr + 1] : "";
				var rating = at > 0 && rest[at - 1].Any(char.IsDigit) ? rest[at - 1] : "-";
				var after = rest.Skip(at + 1).TakeWhile(t => t != "Happiness" && t != "Positional Ability").ToList();
				Console.WriteLine(L($"    Antrenman: {how} · puan {rating}{(after.Count > 0 ? " · " + string.Join(" · ", after) : "")}"));
			}
			if (One("HappinessPositivesCount") is { Length: > 0 } plus)
				Console.WriteLine(L($"    Mutluluk: {(rest.LastIndexOf("Happiness") is > 0 and var h && h + 1 < rest.Count ? rest[h + 1] : "-")} ({plus} artı, {One("HappinessNegativesCount")} eksi)"));
			if (rest.IndexOf("Positional Ability") is > 0 and var pa && pa + 1 < rest.Count && !rest[pa + 1].StartsWith("Action", StringComparison.Ordinal))
				Console.WriteLine(L($"    Mevkide yeteneği: {string.Join(" · ", rest.Skip(pa + 1).TakeWhile(t => t != "Action message"))}"));
			if (!page.Any(t => t.Path == "Player.TransferStatusDetails.TransferStatusDescription.Text")) return;
			Console.WriteLine(L($"    Transfer durumu: {One("Player.TransferStatusDetails.TransferStatusDescription.Text")} · kiralık: {One("Player.LoanStatusDetails.TransferStatusDescription.Text")}"));
			var offers = new List<string>();
			for (var i = 1; i + 1 < page.Count; i++)
				if (page[i].Path == "row.TransferOfferType")
					offers.Add($"{page[i - 1].Text} ({page[i].Text}{(page[i + 1].Path == "row.TotalTransferCost" ? $", {page[i + 1].Text}" : "")})");
			if (offers.Count > 0) Console.WriteLine(L($"    Teklifler: {string.Join(" · ", offers)} · ilgilenen kulüp {Plain("Interested Clubs").Trim('(', ')')}"));
			return;
		}

		// A player's card: who he is, his contract and the club's balance.
		if (rest[0] == "Player Report" && rest.Contains("Duration") && rest.Contains("Wage"))
		{
			string After(string label, int skip = 1) => rest.IndexOf(label) is var i and >= 0 && i + skip < rest.Count ? rest[i + skip] : "";
			var who = rest.Skip(1).TakeWhile(t => t is not ("Current Ability" or "Contract")).Select(t => t.Replace(" years old", L(" yaş"))).ToList();
			Console.WriteLine(L($"\n  Ek: {string.Join(" · ", who)}"));
			Console.WriteLine(L($"    Sözleşme: {After("Duration")} – {After("Duration", 2)} · {After("Wage")}"));
			if (After("Finances") is { Length: > 0 } balance)
				Console.WriteLine(L($"    Kulübün kasası: {(balance.StartsWith('(') ? "-" + balance.Trim('(', ')') : balance)}"));
			return;
		}

		Console.WriteLine(L("\n  Ek: ") + rest[0]);
		var table = rest.IndexOf("Fine Type");
		if (table >= 0 && table + 4 < rest.Count)
		{
			// Fine type, what it is per, then the fine for the first to fourth time.
			var rows = new List<string[]>();
			var i = table + 5;
			for (; i + 5 < rest.Count && rest[i + 1].StartsWith("Issued", StringComparison.Ordinal); i += 6)
				rows.Add([rest[i], rest[i + 2], rest[i + 3], rest[i + 4], rest[i + 5]]);
			Table.Write([L("CEZA (maaştan, Wk = haftalık)"), L("1. KEZ"), L("2. KEZ"), L("3. KEZ"), L("4. KEZ")], rows);
			rest = rest.Skip(i).ToList();
		}
		else rest = rest.Skip(1).ToList();
		// A news article's words come with its page's labels (the date, the score, the scorers,
		// the ground): the article is the long text, the rest is said in it already.
		// Only when there is little else: a card of bound data with a long sentence in it is laid out whole.
		var article = rest.Where(t => t.Length >= 80).ToList();
		if (article.Count > 0 && (page.Any(t => t.Path == "newsitem.NewsBodyString") || page.Count(t => t.Path.Length > 0 && t.Text.Length < 80) <= 3))
			foreach (var paragraph in article.SelectMany(t => t.Split('\n')).Where(l => l.Trim() != ""))
				Console.WriteLine("  " + paragraph.Trim());
		else if (rest.Count > 0)
			foreach (var line in await LabelledAttachment(rest)) Console.WriteLine("  " + line);
	}

	/// <summary>
	/// An attachment no reader here knows, laid out by its data paths (FINDINGS §8.169): the texts bound
	/// to the game's data come in records — a table's row is a run of paths that starts again when its
	/// first path comes round ("binding.Name", "binding.InjuryRiskFactorReason", then "binding.Name"
	/// again) — and each record is a line; a list of one path ("day.DayOfTheWeek" seven times) is one line.
	/// Texts with nothing bound are headings, and a word drawn over and over (an icon's "Increased") is
	/// left out. The same in any language; without paths (an older plugin) the texts as they come.
	/// </summary>
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

	private static async Task<string> MessageBody(string title, string opening)
	{
		var page = await OpenMessage(title, t => t.Path == "newsitem.NewsBodyString", seconds: 3);
		return page.FirstOrDefault(t => t.Path == "newsitem.NewsBodyString").Text is { Length: > 0 } body ? body : opening;
	}

	/// <summary>
	/// The open message as the game draws it, from its sender and title down through its attachment,
	/// each text with its data path: found by the body's element name ("MessageBodyText") and known
	/// for this message by its title, since the one opened before can still be on screen. Waits a
	/// while for what <paramref name="wanted"/> asks for; empty when the message is not there.
	/// </summary>
	private static async Task<List<(string Text, string Path)>> OpenMessage(string title, Func<(string Text, string Path), bool> wanted, double seconds, int up = 5)
	{
		List<(string Text, string Path)> page = [];
		bool Ours() => page.Any(t => t.Path == "newsitem.NewsTitleString" && (t.Text == title || GameWords.Of(t.Text) == GameWords.Of(title)));
		try
		{
			await Until(async () =>
			{
				var found = await Fm.GetJson("texts", ("anchorname", "MessageBodyText"), ("up", up.ToString()), ("paths", "1"), ("most", "1"));
				if (found.ValueKind != JsonValueKind.Array) return false;
				page = found.EnumerateArray().Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
				return Ours() && page.Any(wanted);
			}, seconds: seconds, every: 0.3);
		}
		catch (FmError) { return []; }
		return Ours() ? page : [];
	}

	/// <summary>A message by its number in the inbox listing, or by a piece of its subject.</summary>
	/// <summary>
	/// Where the numbers last shown by `inbox` are kept. The inbox drops read messages and takes
	/// new ones between commands, so "reply 17" means the seventeenth as it was shown, not as
	/// it is now (PLAN, known bugs: once a reply opened the message above the one meant).
	/// </summary>
	private static string NumbersFile => CacheFile("inbox-numbers.json");

	private static void RememberNumbers(JsonElement inbox)
	{
		try
		{
			var shown = inbox.EnumerateArray().Select(m => new[] { Field(m, "UniqueId"), Field(m, "NewsTitleString") }).ToList();
			File.WriteAllText(NumbersFile, JsonSerializer.Serialize(shown));
		}
		catch { }
	}

	private static JsonElement? Pick(List<JsonElement> inbox, string wanted)
	{
		if (int.TryParse(wanted, out var number))
		{
			// The number as `inbox` showed it, found again by the message's id.
			string[][] shown = null;
			try
			{
				if (File.Exists(NumbersFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(NumbersFile) < TimeSpan.FromHours(12))
					shown = JsonSerializer.Deserialize<string[][]>(File.ReadAllText(NumbersFile));
			}
			catch { }
			if (shown != null && number >= 1 && number <= shown.Length)
			{
				var (id, title) = (shown[number - 1][0], shown[number - 1][1]);
				if (inbox.FirstOrDefault(m => Field(m, "UniqueId") == id) is { ValueKind: JsonValueKind.Object } same) return same;
				Console.Error.WriteLine(L($"{number} numaralı mesaj ('{title}') artık gelen kutusunda yok. Güncel numaralar için: inbox"));
				return null;
			}

			if (number >= 1 && number <= inbox.Count) return inbox[number - 1];

			Console.Error.WriteLine(L($"{number} numaralı mesaj yok; gelen kutusunda {inbox.Count} mesaj var."));
			return null;
		}

		var matches = inbox
			// The subject as the game shows it, or the English the plugin gives for it.
			.Where(m => Field(m, "NewsTitleString") is var subject && (subject.Contains(wanted, StringComparison.OrdinalIgnoreCase) || GameWords.Of(subject).Contains(wanted, StringComparison.OrdinalIgnoreCase)))
			.ToList();
		if (matches.Count == 1) return matches[0];

		Console.Error.WriteLine(matches.Count == 0
			? L($"Konusunda '{wanted}' geçen mesaj yok.")
			: L($"'{wanted}' {matches.Count} mesaja uyuyor; numarasıyla seç:"));
		foreach (var match in matches) Console.Error.WriteLine("  " + GameWords.Of(Field(match, "NewsTitleString")));
		return null;
	}
}
