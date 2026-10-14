using System.Text.Json;

namespace FmCli;

/// <summary>
/// Transfer targets in two steps. Bare, it says what there is to spend and where the squad is
/// thin; which position to look at is the manager's to say. With a position, it searches the
/// player database for that position within the transfer budget — the same search as
/// `players --position … --max-value …`, whose other options it passes on.
/// </summary>
internal static partial class Program
{
	private static readonly Dictionary<string, string> PositionTurkish = new()
	{
		["kaleci"] = "GK", ["sol bek"] = "DL", ["sag bek"] = "DR", ["stoper"] = "DC", ["sol kanat bek"] = "WBL", ["sag kanat bek"] = "WBR",
		["on libero"] = "DM", ["defansif orta saha"] = "DM", ["orta saha"] = "MC", ["sol orta saha"] = "ML", ["sag orta saha"] = "MR",
		["on numara"] = "AMC", ["ofansif orta saha"] = "AMC", ["sol kanat"] = "AML", ["sag kanat"] = "AMR", ["forvet"] = "ST", ["santrfor"] = "ST",
	};

	private static async Task<int> Targets(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		var opened = await OpenPage(Screen.RecruitmentBudgets);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		var budgets = await BudgetOverview();
		if (budgets == null)
		{
			Console.Error.WriteLine("Bütçe sayfası okunamadı.");
			return 1;
		}
		// "£11.1M (bu sezon harcanan …)" and "2.123 £ boşta (haftalık) · …": the figure comes first.
		var transfer = budgets.Count > 0 ? MoneyFormat.Find(budgets[0].Value) ?? "" : "";
		var wage = budgets.Count > 1 ? budgets[1].Value.Split(" · ")[0] : "";

		var words = Words(args).ToList();
		if (words.Count == 0) return await ThinPositions(json, transfer, wage);

		var typed = string.Join(" ", words);
		var slot = PositionTurkish.GetValueOrDefault(Fold(typed), typed);
		if (!PositionNames.ContainsKey(new string(slot.Where(char.IsLetter).ToArray())))
		{
			Console.Error.WriteLine($"'{typed}' diye bir mevki yok. Mevkiler: {string.Join(" ", DepthRows.SelectMany(r => r))}  (ya da: {string.Join(", ", PositionTurkish.Keys)})");
			return 64;
		}

		// Everything else typed is the search's own: --age, --grade, --listed, --attr…
		var passed = args.Skip(1).Where(a => !words.Contains(a)).ToList();
		var limited = passed.Contains("--max-value");
		if (!limited && Money(transfer) is not > 0)
		{
			Console.Error.WriteLine($"Transfer bütçesi okunamadı ({transfer}); sınırı kendin ver: targets {typed} --max-value <para>");
			return 1;
		}
		if (!json)
		{
			Console.WriteLine($"{slot.ToUpperInvariant()} — değeri {(limited ? Option(args, "--max-value") : transfer)} ve altı{(limited ? "" : " (transfer bütçen; değiştirmek için --max-value <para>)")}");
			Console.WriteLine($"Maaş bütçesi: {wage}");
		}
		var found = await Players(json, ["players", "--position", slot, .. (limited ? [] : new[] { "--max-value", transfer }), .. passed]);
		if (found == 0 && !json) Console.WriteLine("Daralt: --age 18-23 · --grade B · --listed · --loan-listed · --attr \"pace>=14\"   (arama oyunda açık kalır: players --clear)");
		return found;
	}

	/// <summary>What there is to spend, and the positions with one fit player or none.</summary>
	private static async Task<int> ThinPositions(bool json, string transfer, string wage)
	{
		var (team, _) = await Fm.OwnTeam();
		var squad = (await Fm.Pull("Human.Team.Players", "Name,PositionCombined,IsInjured,LoanClub.Name", list: true)).EnumerateArray()
			.Where(p => Field(p, "Name").Length > 0)
			// Players out on loan are not here to cover; one loaned to us has us as his loan club.
			.Where(p => Field(p, "LoanClub.Name") is not { Length: > 0 } club || club == team)
			.Select(p => (Name: Field(p, "Name"), Injured: Field(p, "IsInjured") == "True", Slots: Positions(Field(p, "PositionCombined"))))
			.ToList();
		var depth = DepthRows.SelectMany(r => r)
			.Select(slot => (Slot: slot, Fit: squad.Where(p => p.Slots.Contains(slot) && !p.Injured).Select(p => p.Name).ToList(), Injured: squad.Where(p => p.Slots.Contains(slot) && p.Injured).Select(p => p.Name).ToList()))
			.ToList();
		var thin = depth.Where(d => d.Fit.Count <= 1).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				TransferBudget = transfer,
				WageBudget = wage,
				Thin = thin.Select(d => new { d.Slot, d.Fit, d.Injured }),
				Depth = depth.ToDictionary(d => d.Slot, d => d.Fit.Count),
			}));
			return 0;
		}

		Console.WriteLine($"Bütçe: transfer {transfer} · maaş {wage}");
		Console.WriteLine(thin.Count == 0 ? "\nKadroda ince mevki yok: her mevkide en az iki sağlam oyuncu var." : "\nKadroda ince mevkiler (sağlam oyuncu bir ya da hiç):");
		foreach (var (slot, fit, injured) in thin)
		{
			var who = fit.Count == 0 ? "kimse yok" : $"yalnızca {fit[0]}";
			var out_ = injured.Count > 0 ? $"  (sakat: {string.Join(", ", injured)})" : "";
			if (Ui.Rich) Ui.Line($"  [bold]{slot,-4}[/] [red]{Ui.Safe(who)}[/][dim]{Ui.Safe(out_)}[/]");
			else Console.WriteLine($"  {slot,-4} {who}{out_}");
		}
		Console.WriteLine($"\nÖbür mevkiler: {string.Join(" · ", depth.Where(d => d.Fit.Count > 1).Select(d => $"{d.Slot} {d.Fit.Count}"))}");
		Console.WriteLine("\nAramak için: targets <mevki>   (ör. targets DL, targets \"sol bek\")");
		return 0;
	}
}
