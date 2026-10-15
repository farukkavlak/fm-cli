using System.Text.Json;

namespace FmCli;

/// <summary>
/// Teaching a player a trait, or unlearning one (FINDINGS §8.75). His training page, reached from
/// the "Training Rating" tile on his page and then its "Traits" tile, talks to the assistant:
/// "Start New Trait Training" is a list of categories with the traits in submenus, "Remove Trait"
/// lists the ones he has, "Ask For Trait Training Suggestion" gets a sentence back. Starting one
/// can bring up the assistant's misgivings as a dialog ("Too Old To Change Game", "Really
/// Unsuitable To Add") with "Start training" / "Don't start training". While a trait is being
/// worked on, the only choice is "Cancel Current Trait Training".
/// </summary>
internal static partial class Program
{
	private static async Task<int> TraitTraining(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var wanted = string.Join(" ", Words(args));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(L("kullanım: training <oyuncu> --trait [\"<alışkanlık>\"] | --trait-remove \"<alışkanlık>\" | --trait-cancel | --trait-suggest"));
			return 64;
		}
		if (await OpenPlayer(wanted) is not { } card) return 1;
		var name = Field(card.GetProperty("Player"), "Name");

		foreach (var tile in new[] { "Training Rating", "Traits" })
		{
			var opened = await Arriving("press", [("text", tile), ("name", "layout-tile-interaction-button"), ("any", "1")]);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Task.Delay(500);
		}

		// The last answer stays up until it is moved past; then the choices come back.
		JsonElement adding = default, removing = default;
		var busy = false;
		async Task<bool> Ready()
		{
			var lists = (await Fm.GetJson("dropdowns", ("json", "1"), ("deep", "1"))).EnumerateArray().ToList();
			adding = lists.FirstOrDefault(d => Field(d, "Label") == "Start New Trait Training");
			removing = lists.FirstOrDefault(d => Field(d, "Label") == "Remove Trait");
			busy = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Any(c => Field(c, "Label") == "Cancel Current Trait Training");
			if (adding.ValueKind == JsonValueKind.Object || busy) return true;
			await Acknowledge();
			return false;
		}
		if (!await Until(Ready, seconds: 10, every: 0.5))
		{
			Console.Error.WriteLine(L($"{name} için alışkanlık eğitimi açılmadı."));
			return 1;
		}

		// What he has is on his card; the "Remove Trait" list is gone while one is being worked on.
		var has = card.TryGetProperty("Traits", out var known) && known.ValueKind == JsonValueKind.Array
			? known.EnumerateArray().Select(c => c.GetString() ?? "").ToList()
			: removing.ValueKind == JsonValueKind.Object
				? removing.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList()
				: [];

		// Each category's traits are a submenu; one without a "›" is a trait of its own.
		var addable = new List<(string Category, string Trait)>();
		if (adding.ValueKind == JsonValueKind.Object)
		{
			var categories = adding.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
			adding.TryGetProperty("Submenus", out var submenus);
			for (var i = 0; i < categories.Count; i++)
			{
				var category = categories[i].Replace(" ›", "");
				if (submenus.ValueKind == JsonValueKind.Object && submenus.TryGetProperty(i.ToString(), out var traits))
					addable.AddRange(traits.EnumerateArray().Select(t => (category, t.GetString() ?? "")));
				else addable.Add(("", category));
			}
		}

		// The assistant's answer, right after what was asked in the conversation.
		async Task<string> Answer(string asked)
		{
			var texts = (await Fm.GetJson("texts", ("anchor", "Assistant Manager"), ("up", "30"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
			var at = texts.IndexOf(asked);
			return at >= 0 && at + 1 < texts.Count ? texts[at + 1] : "";
		}

		// Starting, removing or cancelling can be met by the assistant's misgivings in a dialog;
		// at a terminal they are asked about, otherwise --yes goes ahead and anything else does not.
		async Task<bool> Confirmed()
		{
			JsonElement dialogs = default;
			if (!await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 3, every: 0.5)) return true;
			var dialog = dialogs[0];
			var buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
			var go = buttons.FirstOrDefault(b => Field(b, "Name").Contains("primary", StringComparison.Ordinal));
			var stop = buttons.FirstOrDefault(b => Field(b, "Name").Contains("secondary", StringComparison.Ordinal));
			Console.WriteLine($"{Field(dialog, "Title")}\n  {Field(dialog, "Description")}");
			bool yes;
			if (args.Contains("--yes")) yes = true;
			else if (Ui.Interactive) yes = Ui.Ask(L("Ne yapalım?"), [Field(go, "Label"), Field(stop, "Label")]) == 0;
			else
			{
				yes = false;
				Console.WriteLine(L("Onaylamak için aynı komutu --yes ile çalıştır."));
			}
			var chosen = yes ? go : stop;
			if (chosen.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(chosen, "Pointer")));
			return yes;
		}

		async Task<int> Done(string said)
		{
			await Acknowledge();
			Console.WriteLine(said);
			return 0;
		}

		if (args.Contains("--trait-suggest"))
		{
			if (busy) { Console.WriteLine(L($"{name} şu an bir alışkanlık üzerinde çalışıyor; öneri için önce iptal: --trait-cancel")); return 0; }
			var asked = await Arriving("press", [("text", "Ask For Trait Training Suggestion"), ("any", "1")]);
			if (asked.StartsWith("error", StringComparison.Ordinal)) return Report(asked);
			// A suggestion comes as a dialog offering to start on it; no suggestion is a sentence.
			string said = "";
			await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() > 0
			                        || (said = await Answer("Ask For Trait Training Suggestion")).Length > 0, seconds: 6, every: 0.5);
			if (said.Length > 0) return await Done(said);
			return await Confirmed() ? await Done(L($"{name}: önerilen alışkanlığın eğitimi başladı.")) : 0;
		}

		if (args.Contains("--trait-cancel"))
		{
			if (!busy) { Console.WriteLine(L($"{name} şu an bir alışkanlık üzerinde çalışmıyor.")); return 0; }
			var cancelled = await Arriving("press", [("text", "Cancel Current Trait Training"), ("any", "1")]);
			if (cancelled.StartsWith("error", StringComparison.Ordinal)) return Report(cancelled);
			if (!await Confirmed()) return 0;
			return await Done(L($"{name}: alışkanlık eğitimi iptal edildi."));
		}

		if (Option(args, "--trait-remove") is { } unlearn)
		{
			if (busy) { Console.WriteLine(L($"{name} şu an bir alışkanlık üzerinde çalışıyor; önce iptal: --trait-cancel")); return 0; }
			if (Choose(has, unlearn, L("Alışkanlık")) is not { } trait) return 1;
			var sent = await Fm.Get("select", ("ptr", Field(removing, "Pointer")), ("choice", trait), ("fire", "1"));
			if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
			if (!await Confirmed()) return 0;
			return await Done(L($"{name}: \"{trait}\" bırakılmaya çalışılacak."));
		}

		var learn = Option(args, "--trait") is { } v && !v.StartsWith("--", StringComparison.Ordinal) ? v : null;
		if (learn == null && (busy || !Ui.Interactive))
		{
			Console.WriteLine(L($"{name} — alışkanlıkları: {(has.Count == 0 ? L("yok") : string.Join(", ", has))}"));
			if (busy)
			{
				Console.WriteLine(L("Şu an bir alışkanlık üzerinde çalışıyor (oyun hangisi olduğunu bu ekranda yazmıyor)."));
				Console.WriteLine(L($"İptal için: training \"{name}\" --trait-cancel"));
				return 0;
			}
			Console.WriteLine(L("Öğretilebilecekler:"));
			foreach (var group in addable.GroupBy(a => a.Category))
				Console.WriteLine($"  {(group.Key.Length > 0 ? group.Key : L("Diğer"))}: {string.Join(", ", group.Select(a => a.Trait))}");
			Console.WriteLine(L($"\nÖğretmek için: training \"{name}\" --trait \"<alışkanlık>\"  ·  bırakmak: --trait-remove \"<alışkanlık>\"  ·  öneri: --trait-suggest"));
			return 0;
		}

		string path;
		if (learn != null)
		{
			var fits = Matching(addable, a => a.Trait, learn);
			var exact = fits.Where(a => Fold(a.Trait) == Fold(learn)).ToList();
			if (exact.Count == 1) fits = exact;
			if (fits.Count != 1)
			{
				Console.Error.WriteLine(fits.Count == 0 ? L($"Öğretilebilecekler arasında '{learn}' yok.") : L($"'{learn}' {fits.Count} alışkanlığa uyuyor:"));
				foreach (var a in fits) Console.Error.WriteLine($"  {a.Trait}");
				return 1;
			}
			path = fits[0].Category.Length > 0 ? $"{fits[0].Category}>{fits[0].Trait}" : fits[0].Trait;
		}
		else
		{
			Console.WriteLine(L($"{name} — alışkanlıkları: {(has.Count == 0 ? L("yok") : string.Join(", ", has))}"));
			var labels = addable.Select(a => a.Category.Length > 0 ? $"{a.Category} › {a.Trait}" : a.Trait).ToList();
			if (Ui.Ask(L($"{name}: hangi alışkanlığı öğrensin?"), labels) is not { } chosen) return 0;
			var a = addable[chosen];
			path = a.Category.Length > 0 ? $"{a.Category}>{a.Trait}" : a.Trait;
		}

		var started = path.Contains('>')
			? await Fm.Get("select", ("ptr", Field(adding, "Pointer")), ("names", path), ("fire", "1"))
			: await Fm.Get("select", ("ptr", Field(adding, "Pointer")), ("choice", path), ("fire", "1"));
		if (started.StartsWith("error", StringComparison.Ordinal)) return Report(started);
		if (!await Confirmed()) return 0;
		return await Done(L($"{name}: \"{path.Split('>')[^1]}\" eğitimi başladı."));
	}
}
