using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static async Task<int> Tactic(bool json, string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count > 0 && words[0] == "new") return await TacticNew(words.Skip(1).ToList(), args.Contains("--dry"));

		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("Kayıt yüklü değil. `fm continue` ile yükle."));
			return 69;
		}

		var dry = args.Contains("--dry");
		var wanted = words.Count > 1 ? words[1] : null;

		// During a match the planner's pages are gone; the match screen has its own list.
		if (words.Count > 0 && words[0] == "mentality" && Field(await Fm.GetJson("state"), "DuringMatch") == "True")
			return await MatchList(L("Mentalite"), d => d.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == "Very Attacking"), wanted, dry, events: false);
		if (words.Count > 0 && words[0] == "role" && Field(await Fm.GetJson("state"), "DuringMatch") == "True")
			return await MatchRole(words.Skip(1).ToList(), args.Contains("--oop"), dry);

		switch (words.Count > 0 ? words[0] : "")
		{
			case "": break;
			// Each setting is a dropdown on the planner, known by something only it offers.
			case "mentality": return await TacticSetting(L("Mentalite"), "MentalityString", "Very Defensive", wanted, dry);
			case "style": return await TacticSetting(L("Stil"), "Style", "Gegenpress", wanted, dry);
			case "pick": return await TacticPick(dry);
			case "role": return await TacticRole(words.Skip(1).ToList(), args.Contains("--oop"), dry);
			case "player": return await TacticPlayer(words.Skip(1).ToList(), dry);
			case "slot": return await TacticSlot(wanted, dry);
			case "copy": return await TacticCopy(wanted, dry);
			case "clear": return await TacticClear(wanted, args.Contains("--yes"), dry);
			case "save": return await TacticSave(words.Skip(1).ToList(), Array.IndexOf(args, "--note") is >= 0 and var n && n + 1 < args.Length ? args[n + 1] : null, args.Contains("--yes"), dry);
			case "load": return await TacticLoad(words.Skip(1).ToList(), args.Contains("--yes"), dry);
			case "saved": return TacticSaved(words.Skip(1).ToList(), args.Contains("--yes"));
			case "pi": return await PlayerInstructions(words.Skip(1).ToList(), dry);
			case "instructions" or "instruction": return await TacticInstructions(words.Skip(1).ToList(), args.Contains("--oop"), dry, args.Contains("--refresh"));
			default:
				Console.Error.WriteLine(L("kullanım: tactic | tactic new … | tactic slot [<n>] | tactic copy [<taktik>] | tactic clear [<n>] --yes | tactic pi <mevki> [<talimat> [<değer>]] | tactic instructions [<talimat> [<değer>]] | tactic mentality [<x>] | tactic style [<x>] | tactic pick | tactic role <mevki> [<rol>] [--oop] | tactic player <mevki> <oyuncu>   [--dry]"));
				return 64;
		}

		if (await Planner() is not { } tactic)
		{
			Console.Error.WriteLine(L("Takımın taktiği yok. Oluşturmak için: tactic new"));
			return 1;
		}
		// The squad table under the pitch, where each player's familiarity is, fills a moment
		// after the planner itself.
		if (!Familiar(tactic))
			await Until(async () =>
			{
				var now = await Fm.GetJson("tactic");
				if (now.TryGetProperty("Tactic", out _)) tactic = now;
				return Familiar(tactic);
			}, seconds: 4, every: 0.25);
		var header = tactic.GetProperty("Tactic");

		if (json)
		{
			Console.WriteLine(tactic.ToString());
			return 0;
		}

		Console.WriteLine(Field(header, "Name"));
		Console.WriteLine(L($"Stil: {Field(header, "Style")} · Mentalite: {Field(header, "MentalityString")}")
		                  + L($" · Yoğunluk: {Field(header, "TacticalIntensity")} · Akıcılık: {Familiarity(header, "TacticalFamiliarity")}\n"));

		if (Ui.Rich)
		{
			Pitch.Draw(tactic);
			if (Bench(tactic) is { Count: > 0 } benched) Console.WriteLine(L($"\nYedekler: {string.Join(", ", benched)}"));
			if (Unfamiliar(tactic) is { Count: > 0 } learning) Console.WriteLine(L($"Taktiğe tam alışamayanlar: {string.Join(", ", learning)}"));
			return 0;
		}

		var rows = tactic.GetProperty("Slots").EnumerateArray().Select(slot => new[]
		{
			Spot(Field(slot, "IPPosition")),
			RoleOf(slot, withoutBall: false),
			Spot(Field(slot, "OOPPosition")),
			RoleOf(slot, withoutBall: true),
			Field(slot, "Player") is { Length: > 0 } player ? player : "-",
			Field(slot, "Familiarity") is { Length: > 0 } known ? GameNumbers.Percent(known) : "-",
		}).ToList();

		Table.Write([L("TOPLA"), L("ROL"), L("TOPSUZ"), L("ROL"), L("OYUNCU"), L("AKICILIK")], rows);

		if (Bench(tactic) is { Count: > 0 } bench) Console.WriteLine(L($"\nYedekler: {string.Join(", ", bench)}"));
		return 0;
	}

	/// <summary>"AMR   Asensio · Inside Forward": a slot as it reads in a list to choose from.</summary>
	private static List<string> SlotLabels(List<JsonElement> slots, string roleField) =>
		slots.Select(s => $"{Spot(Field(s, "IPPosition")),-4}  {(Field(s, "Player") is { Length: > 0 } p ? p : "—")} · {RoleOf(s, roleField == "OOPRoleName")}").ToList();

	/// <summary>After a change, the pitch with what changed marked, for someone at a terminal.</summary>
	private static async Task ShowChange(HashSet<(int, bool)> changed)
	{
		if (!Ui.Rich) return;
		var tactic = await Fm.GetJson("tactic");
		if (!tactic.TryGetProperty("Slots", out _)) return;
		Console.WriteLine();
		Pitch.Draw(tactic, changed);
	}

	/// <summary>The team's familiarity, which the game keeps out of 10000, as a percentage.</summary>
	private static string Familiarity(JsonElement header, string field) =>
		int.TryParse(Field(header, field), out var tenThousandths) ? GameNumbers.Percent((tenThousandths + 50) / 100) : "-";

	private static bool Familiar(JsonElement tactic) =>
		tactic.TryGetProperty("Slots", out var slots) && slots.EnumerateArray().Any(s => Field(s, "Familiarity").Length > 0);

	/// <summary>"Oosterwolde 71%": the eleven who do not know the tactic fully yet, least first.</summary>
	private static List<string> Unfamiliar(JsonElement tactic) =>
		tactic.GetProperty("Slots").EnumerateArray()
			.Select(s => (Name: Field(s, "Player"), Known: int.TryParse(Field(s, "Familiarity"), out var k) ? k : 100))
			.Where(p => p.Known < 100 && p.Name.Length > 0)
			.OrderBy(p => p.Known)
			.Select(p => $"{p.Name} {GameNumbers.Percent(p.Known)}")
			.ToList();

	/// <summary>The match squad lists the eleven first; whoever comes after is on the bench.</summary>
	private static List<string> Bench(JsonElement tactic)
	{
		if (!tactic.TryGetProperty("Squad", out var squad)) return [];
		var eleven = tactic.GetProperty("Slots").GetArrayLength();
		return squad.EnumerateArray()
			.Where(e => int.TryParse(Field(e, "Index"), out var place) && place >= eleven && Field(e, "PlayerIndex") != "-1")
			.Select(e => Field(e, "Familiarity") is { Length: > 0 } known && known != "100" ? $"{Field(e, "Name")} {GameNumbers.Percent(known)}" : Field(e, "Name"))
			.ToList();
	}

	/// <summary>
	/// Puts a substitute into a slot of the eleven. Whoever was there changes places with him,
	/// as it does when the same is chosen from the slot's list on the planner. Only the bench
	/// is bound with names there, so only the bench can be named.
	/// </summary>
	private static async Task<int> TacticPlayer(List<string> words, bool dry)
	{
		if (await Planner() is not { } tactic)
		{
			Console.Error.WriteLine(L("Takımın taktiği yok. Oluşturmak için: tactic new"));
			return 1;
		}

		var slots = tactic.GetProperty("Slots").EnumerateArray().ToList();
		var spots = slots.Select(s => Spot(Field(s, "IPPosition"))).ToList();
		var squad = tactic.TryGetProperty("Squad", out var listed) ? listed.EnumerateArray().Where(e => Field(e, "PlayerIndex") != "-1").ToList() : [];

		if (words.Count < 2 && Ui.Interactive && squad.Count > 0)
		{
			if (words.Count == 0)
			{
				if (Ui.Ask(L("Hangi mevki?"), SlotLabels(slots, "IPRoleName")) is not { } slotAsked) return 0;
				words.Add(spots[slotAsked]);
			}
			var benchNames = squad.Select(e => Field(e, "Name")).ToList();
			if (Ui.Ask(L($"{words[0]} mevkisine kim?"), benchNames) is not { } asked) return 0;
			words.Add(benchNames[asked]);
		}

		if (words.Count < 2)
		{
			Console.Error.WriteLine(L($"kullanım: tactic player <mevki> <oyuncu>   mevkiler: {string.Join(" ", spots)}"));
			if (squad.Count > 0) Console.Error.WriteLine(L($"yedekler: {string.Join(", ", squad.Select(e => Field(e, "Name")))}"));
			return 64;
		}

		if (Choose(spots, words[0], L("Mevki")) is not { } spot) return 1;
		var at = spots.IndexOf(spot);

		var names = squad.Select(e => Field(e, "Name")).ToList();
		if (names.Count == 0)
		{
			Console.Error.WriteLine(L("Maç kadrosu boş; önce: tactic pick"));
			return 1;
		}
		if (Choose(names, words[1], L("Oyuncu")) is not { } name) return 1;

		var player = Field(squad[names.IndexOf(name)], "PlayerIndex");
		var slot = Field(slots[at], "TeamSelectionIndex");
		var current = Field(slots[at], "Player");
		if (Field(slots[at], "PlayerIndex") == player)
		{
			Console.WriteLine(L($"{spot} zaten {name}."));
			return 0;
		}

		var query = new List<(string, string)> { ("slot", slot), ("player", player) };
		if (dry) query.Add(("dry", "1"));

		var picked = await Arriving("pickplayer", query.ToArray());
		if (picked.StartsWith("error", StringComparison.Ordinal)) return Report(picked);
		if (dry)
		{
			Console.WriteLine(L($"(kuru) {spot}: {current} → {name} yapılacaktı."));
			return 0;
		}

		var took = await Until(async () =>
			(await Fm.GetJson("tactic")).TryGetProperty("Slots", out var now) && now.GetArrayLength() > at && Field(now[at], "PlayerIndex") == player, seconds: 8);
		Console.WriteLine(took ? $"{spot}: {(current.Length > 0 ? current : L("boş"))} → {name}" : L($"{name} seçildi ama {spot} hâlâ {current} gösteriyor."));
		// The one taken off goes to the newcomer's place on the bench; an injured man there still keeps the match from starting.
		if (took)
			try
			{
				var injured = (await Fm.Pull("Human.Team.Players", "Name,IsInjured", list: true)).EnumerateArray()
					.Where(p => Field(p, "IsInjured") == "True").Select(p => Field(p, "Name")).ToList();
				bool Hurt(string shown) => shown.Length > 0 && injured.Any(n => n.EndsWith(shown, StringComparison.Ordinal));
				if (Hurt(name)) Console.WriteLine(L($"Dikkat: {name} sakat; maç onunla başlamaz."));
				if (Hurt(current)) Console.WriteLine(L($"Dikkat: {current} sakat ve şimdi yedeklerde; sakat yedekle maç başlamaz. Yedekleri yardımcıya seçtirmek için: tactic pick"));
			}
			catch (FmError) { }
		if (took) await ShowChange([(at, false), (at, true)]);
		return took ? 0 : 1;
	}

	/// <summary>The tactic as the planner binds it, opening the planner if it is not up; null without a tactic.</summary>
	private static async Task<JsonElement?> Planner()
	{
		var tactic = await Fm.GetJson("tactic");
		if (!tactic.TryGetProperty("Tactic", out _))
		{
			var opened = await OpenPage(Screen.Tactics);
			if (opened.StartsWith("error", StringComparison.Ordinal)) throw new FmError(opened);

			await Until(async () => (tactic = await Fm.GetJson("tactic")).TryGetProperty("Slots", out var s) && s.GetArrayLength() >= 11, seconds: 10);
		}
		// The pitch shows the eleven slots only in its combined view; after "In / Out of
		// Possession" or "Both" it lays out no slots (FINDINGS §8.55), so it is put back.
		if (tactic.TryGetProperty("Tactic", out _) && (!tactic.TryGetProperty("Slots", out var slots) || slots.GetArrayLength() == 0)
			&& await Labelled("Combined") is { } combined)
		{
			await Fm.Get("press", ("ptr", Field(combined, "Pointer")));
			await Until(async () => (tactic = await Fm.GetJson("tactic")).TryGetProperty("Slots", out var s) && s.GetArrayLength() >= 11, seconds: 6, every: 0.25);
		}
		// The team instructions take the pitch's place; the "Team Shape" button in their group puts
		// it back, and it answers only a real click. It is found by the group the game lays it out
		// in rather than by its translated label (FINDINGS §8.313).
		if (tactic.TryGetProperty("Tactic", out _) && (!tactic.TryGetProperty("Slots", out var shown) || shown.GetArrayLength() == 0)
			&& !(await Fm.Get("click", ("within", "FMShapeAndInstructionsButtons"), ("name", "team-shape"))).StartsWith("error", StringComparison.Ordinal))
			await Until(async () => (tactic = await Fm.GetJson("tactic")).TryGetProperty("Slots", out var s) && s.GetArrayLength() >= 11, seconds: 6, every: 0.25);
		if (!tactic.TryGetProperty("Tactic", out _)) return null;
		await KnowRoles(tactic);
		return tactic;
	}
}
