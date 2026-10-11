using System.Text.Json;

namespace FmCli;

/// <summary>
/// What the game lets be done about a person — the menu a right click on his name raises
/// (FINDINGS §8.118): for a player scouting, shortlisting, declaring interest; for staff
/// "Approach To Sign", and for our own "Terminate Contract", "Send On Coaching Course". The
/// name is found with the game's search, whose results carry that menu, so it works for anyone
/// the game knows. Hiring and letting staff go are two entries of it with their follow-up: the
/// contract talks, and the game's own question about the compensation.
/// </summary>
internal static partial class Program
{
	/// <summary>
	/// Finds the person with the game's search and raises his menu. Null having said why not.
	/// </summary>
	private static async Task<(string Name, List<JsonElement> Entries)?> PersonMenu(string wanted, string kind = null)
	{
		var found = (await SearchFor(wanted)).Where(r => Field(r, "Name").Length > 0 && (kind == null || Field(r, "Kind") == kind)).ToList();
		var exact = found.Where(r => Fold(Field(r, "Name")) == Fold(wanted)).ToList();
		if (exact.Count > 0) found = exact;
		if (found.Count == 0)
		{
			Console.Error.WriteLine(kind == "Staff" ? $"'{wanted}' adında bir personel bulunamadı. Aramak için: staff find" : $"'{wanted}' bulunamadı.");
			return null;
		}
		if (found.Count > 1)
		{
			var labels = found.Select(r => $"{Field(r, "Name")} ({Field(r, "Kind")}{(Field(r, "Club").Length > 0 ? ", " + Field(r, "Club") : "")})").ToList();
			if (Ui.Ask($"'{wanted}' birden çok kişiye uyuyor:", labels) is { } picked) found = [found[picked]];
			else
			{
				if (!Ui.Interactive) Console.Error.WriteLine($"'{wanted}' birden çok kişiye uyuyor: {string.Join(" · ", labels)}. Adı tam yaz.");
				return null;
			}
		}
		var name = Field(found[0], "Name");

		await Fm.Get("actions", ("close", "1"));
		var opened = await Arriving("actions", [("open", "1"), ("text", name)]);
		if (opened.StartsWith("error", StringComparison.Ordinal))
		{
			Console.Error.WriteLine($"{name} için oyun bir aksiyon menüsü açmadı.");
			return null;
		}
		// The game fills the menu a moment after it opens; two readings alike are taken as whole.
		List<JsonElement> entries = [];
		var last = "";
		await Until(async () =>
		{
			var now = await Fm.GetJson("actions");
			var settled = now.GetArrayLength() > 0 && now.ToString() == last;
			last = now.ToString();
			entries = now.EnumerateArray().ToList();
			return settled;
		}, seconds: 8, every: 0.4);
		// The first entry is the person himself (it opens his page), not something done to him.
		if (entries.Count > 0 && Fold(Field(entries[0], "Name")) == Fold(name)) entries.RemoveAt(0);
		if (entries.Count == 0)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine($"{name} için yapılabilecek bir şey yok.");
			return null;
		}
		return (name, entries);
	}

	private static bool IsFolder(JsonElement entry) => entry.TryGetProperty("Folder", out var f) && f.ValueKind == JsonValueKind.True;
	private static bool IsEnabled(JsonElement entry) => entry.TryGetProperty("Enabled", out var e) && e.ValueKind == JsonValueKind.True;

	/// <summary>The entry a typed path means: the path whole, or its last words when only one entry ends so.</summary>
	private static JsonElement? MenuEntry(List<JsonElement> entries, string typed)
	{
		var items = entries.Where(e => !IsFolder(e)).ToList();
		var want = Fold(typed.Replace(" > ", ">").Replace("›", ">"));
		var whole = items.Where(e => Fold(Field(e, "Path")) == want).ToList();
		if (whole.Count == 1) return whole[0];
		var ending = items.Where(e => Fold(Field(e, "Path")).EndsWith(">" + want, StringComparison.Ordinal) || Fold(Field(e, "Name")) == want).ToList();
		if (ending.Count == 1) return ending[0];
		var holding = items.Where(e => Fold(Field(e, "Path")).Contains(want, StringComparison.Ordinal)).ToList();
		if (holding.Count == 1) return holding[0];
		var near = ending.Count > 1 ? ending : holding;
		Console.Error.WriteLine(near.Count > 1
			? $"'{typed}' birden çok seçeneğe uyuyor:\n" + string.Join("\n", near.Take(12).Select(e => "  " + Field(e, "Path").Replace(">", " › ")))
			: $"Menüde '{typed}' yok.");
		return null;
	}

	/// <summary>What came of a choice: the game's question, a conversation, contract talks — or nothing to see.</summary>
	private static async Task<int> AfterAction(string name, string path)
	{
		JsonElement dialogs = default;
		OfferScreen talks = null;
		var said = false;
		await Until(async () =>
		{
			if ((dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0) return true;
			if ((talks = await OfferScreen.Read(Fm, "contract")) != null) return true;
			return said = (await TalkOptions()).Count > 0;
		}, seconds: 3, every: 0.5);

		if (dialogs.ValueKind == JsonValueKind.Array && dialogs.GetArrayLength() > 0)
		{
			var dialog = dialogs[0];
			Console.WriteLine($"{Field(dialog, "Title")}\n  {Field(dialog, "Description").Replace("\n\n", "\n  ")}");
			Console.WriteLine($"\nCevaplamak için: dialog <{string.Join(" | ", dialog.GetProperty("Buttons").EnumerateArray().Select(b => Field(b, "Label")))}>");
			return 0;
		}
		if (talks != null) return await Contract(["contract"]);
		if (said) return await TalkTo(false, ["talk"]);
		Console.WriteLine($"{name}: {path.Replace(">", " › ")} — yapıldı.");
		return 0;
	}

	private static async Task<int> Actions(bool json, string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine("kullanım: actions <kişi> [\"<seçenek>\"]   (oyunun o kişi için sağ tık menüsü; seçenek yazılırsa yapılır)");
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		if (await PersonMenu(words[0]) is not { } menu) return 1;
		var (name, entries) = menu;

		if (words.Count > 1)
		{
			if (MenuEntry(entries, string.Join(" ", words.Skip(1))) is not { } entry)
			{
				await Fm.Get("actions", ("close", "1"));
				return 1;
			}
			return await ChooseAction(name, entry);
		}

		if (json)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.WriteLine(JsonOut.Of(new { Name = name, Actions = entries.Where(e => !IsFolder(e)).Select(e => new { Path = Field(e, "Path"), Enabled = IsEnabled(e) }) }));
			return 0;
		}

		if (Ui.Interactive)
		{
			// Level by level, the way the menu itself unfolds.
			var at = "";
			while (true)
			{
				var level = entries.Where(e => Parent(Field(e, "Path")) == at && IsEnabled(e)).ToList();
				var labels = level.Select(e => Field(e, "Name") + (IsFolder(e) ? " ›" : "")).ToList();
				if (Ui.Ask(at.Length == 0 ? $"{name}: ne yapalım?" : $"{name} › {at.Replace(">", " › ")}", labels, cancel: at.Length == 0 ? "Hiçbir şey" : "Geri") is not { } picked)
				{
					if (at.Length == 0) { await Fm.Get("actions", ("close", "1")); return 0; }
					at = Parent(at);
					continue;
				}
				if (!IsFolder(level[picked])) return await ChooseAction(name, level[picked]);
				at = Field(level[picked], "Path");
			}
		}

		await Fm.Get("actions", ("close", "1"));
		Console.WriteLine(name);
		// A folder inside another with a long list under it (every scout, every squad player) is named with its count.
		var all = args.Contains("--all");
		string folded = null;
		foreach (var entry in entries)
		{
			var path = Field(entry, "Path");
			if (folded != null && path.StartsWith(folded + ">", StringComparison.Ordinal)) continue;
			folded = null;
			var depth = path.Count(c => c == '>');
			var under = entries.Count(e => Field(e, "Path").StartsWith(path + ">", StringComparison.Ordinal));
			var line = new string(' ', 2 + depth * 2) + Field(entry, "Name");
			if (IsFolder(entry))
			{
				if (!all && under > 12 && depth > 0) { folded = path; line += $" ›  ({under} seçenek; hepsi: --all)"; }
				else line += " ›";
			}
			if (!IsEnabled(entry)) line += "  (kapalı)";
			Console.WriteLine(line);
		}
		Console.WriteLine($"\nYapmak için: actions \"{name}\" \"<seçenek>\"   ör. actions \"{name}\" \"{Field(entries.First(e => !IsFolder(e)), "Path").Replace(">", " > ")}\"");
		return 0;

		static string Parent(string path) => path.LastIndexOf('>') is var cut && cut >= 0 ? path[..cut] : "";
	}

	private static async Task<int> ChooseAction(string name, JsonElement entry)
	{
		var path = Field(entry, "Path");
		if (!IsEnabled(entry))
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine($"{name}: '{path.Replace(">", " › ")}' şu an kapalı.");
			return 1;
		}
		var chosen = await Fm.Get("actions", ("choose", path));
		if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
		return await AfterAction(name, path);
	}

	/// <summary>
	/// Approaching someone to join the staff: "Approach To Sign" opens the contract talks the
	/// players have too — job, wage, length — and `contract` carries them on from there.
	/// </summary>
	private static async Task<int> StaffHire(string[] args)
	{
		var wanted = string.Join(" ", Words(args).Skip(1));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine("kullanım: staff hire <ad> [--wage <tutar>] [--term \"Şart=değer\"] [--sign --yes]   (aday bulmak: staff find --role <görev> --free)");
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		// What is said about the terms goes on to the talks as it is.
		var passed = new List<string> { "contract" };
		for (var i = 2; i < args.Length; i++)
		{
			if (args[i] is "--wage" or "--term" && i + 1 < args.Length) { passed.Add(args[i]); passed.Add(args[++i]); }
			else if (args[i].StartsWith("--", StringComparison.Ordinal)) passed.Add(args[i]);
		}
		if (await OfferScreen.Read(Fm, "contract") is { } open)
		{
			if (!Fold(open.Player).Contains(Fold(wanted), StringComparison.Ordinal))
			{
				Console.Error.WriteLine($"Açık görüşme {open.Player} ile. Önce onu bitir ya da bırak: contract --cancel");
				return 1;
			}
			return await Contract([.. passed]);
		}

		if (await PersonMenu(wanted, "Staff") is not { } menu) return 1;
		var (name, entries) = menu;
		var approach = entries.FirstOrDefault(e => Field(e, "Path") == "Approach To Sign");
		if (approach.ValueKind != JsonValueKind.Object || !IsEnabled(approach))
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(entries.Any(e => Field(e, "Path") == "Contract>Terminate Contract")
				? $"{name} zaten bizim personelimiz."
				: entries.Any(e => Field(e, "Path") == "Contract>Withdraw Contract Offer")
				? $"{name} için teklifimiz zaten masada; cevabı gelen kutusuna düşer. Geri çekmek: actions \"{name}\" \"Withdraw Contract Offer\" · değiştirmek: actions \"{name}\" \"Change Contract Offer\""
				: $"{name} için oyun 'Approach To Sign' sunmuyor (görüşme sürüyor ya da gelmek istemiyor olabilir). Menüsü: actions \"{name}\"");
			return 1;
		}
		var chosen = await Fm.Get("actions", ("choose", "Approach To Sign"));
		if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);

		JsonElement dialogs = default;
		OfferScreen talks = null;
		await Until(async () => (talks = await OfferScreen.Read(Fm, "contract")) != null || (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 10, every: 0.5);
		if (talks == null)
		{
			if (dialogs.ValueKind == JsonValueKind.Array && dialogs.GetArrayLength() > 0)
			{
				Console.Error.WriteLine($"{Field(dialogs[0], "Title")}\n  {Field(dialogs[0], "Description").Replace("\n\n", "\n  ")}\nCevaplamak için: dialog");
				return 1;
			}
			Console.Error.WriteLine("Sözleşme görüşmesi açılmadı.");
			return 1;
		}
		return await Contract([.. passed]);
	}

	/// <summary>
	/// Letting one of our staff go: "Terminate Contract" is asked about by the game, naming the
	/// compensation; "Mutual Termination" is not (as with players, FINDINGS §8.76), so it is
	/// asked about here.
	/// </summary>
	private static async Task<int> StaffFire(string[] args)
	{
		var wanted = string.Join(" ", Words(args).Skip(1));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine("kullanım: staff fire <ad> [--mutual] [--yes]");
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		if (await PersonMenu(wanted, "Staff") is not { } menu) return 1;
		var (name, entries) = menu;

		const string terminate = "Contract>Terminate Contract", mutual = "Contract>Mutual Termination";
		var way = args.Contains("--mutual") ? mutual : terminate;
		var entry = entries.FirstOrDefault(e => Field(e, "Path") == way);
		if (entry.ValueKind != JsonValueKind.Object || !IsEnabled(entry))
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(entries.Any(e => Field(e, "Path") == "Approach To Sign")
				? $"{name} bizim personelimiz değil."
				: $"{name} için oyun '{way.Split('>')[1]}' sunmuyor. Menüsü: actions \"{name}\"");
			return 1;
		}

		if (way == mutual && !args.Contains("--yes"))
		{
			if (!Ui.Interactive || Ui.Ask($"{name} ile sözleşmeyi karşılıklı feshetmeyi teklif edelim mi?", ["Evet", "Hayır"]) != 0)
			{
				await Fm.Get("actions", ("close", "1"));
				if (!Ui.Interactive) Console.WriteLine($"Karşılıklı fesih oyunda onaysız işliyor. Göndermek için: staff fire \"{name}\" --mutual --yes");
				return 0;
			}
		}

		var chosen = await Fm.Get("actions", ("choose", way));
		if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
		if (way == mutual) return await AfterAction(name, way);

		JsonElement dialogs = default;
		if (!await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 6, every: 0.5))
		{
			Console.Error.WriteLine("Onay penceresi gelmedi; ne olduğunu `staff` ile gör.");
			return 1;
		}
		var dialog = dialogs[0];
		var buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
		Console.WriteLine($"{Field(dialog, "Title")}\n  {Field(dialog, "Description").Replace("\n\n", "\n  ")}");
		var go = args.Contains("--yes") || (Ui.Interactive && Ui.Ask("Sözleşmesini feshedelim mi?", ["Evet", "Hayır"]) == 0);
		var answer = buttons.FirstOrDefault(b => Field(b, "Label") == (go ? "Yes" : "No"));
		if (answer.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(answer, "Pointer")));
		if (!go)
		{
			if (!Ui.Interactive) Console.WriteLine($"\nVazgeçildi. Feshetmek için: staff fire \"{name}\" --yes");
			return 0;
		}
		Console.WriteLine($"\n{name} ile yollar ayrıldı.");
		return 0;
	}
}
