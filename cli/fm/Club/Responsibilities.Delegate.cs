using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// Hands a duty to someone in particular: the duty's list of who can take it (FINDINGS §8.135)
	/// has only people in it, no text, so each row is put where its name can be read.
	/// </summary>
	private static async Task<int> GiveDuty(List<string> words, bool yes)
	{
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("kullanım: responsibilities give \"<görev>\" [<kişi>]"));
			return 64;
		}
		if (await FindDuty(words[0]) is not { } duty) return 64;

		// Back up the list to where the duty is laid out, its list with it.
		await ToTop();
		var now = (await Duties(stopAt: duty.Title)).FirstOrDefault(d => d.Group == duty.Group && d.Title == duty.Title);
		if (now == null || now.Staff.Length == 0)
		{
			Console.Error.WriteLine(L($"{duty.Title}: sayfada bulunamadı."));
			return 1;
		}
		if (!now.CanTake && !now.CanDelegate)
		{
			Console.Error.WriteLine(L($"{duty.Title}: kilitli, oyun başkasına verdirmiyor ({now.Holder})."));
			return 1;
		}

		var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Pointer") == now.Staff);
		var count = list!.ValueKind == JsonValueKind.Object ? list.GetProperty("Choices").GetArrayLength() : 0;
		var scope = $"fmcli.duty{DateTime.UtcNow.Ticks % 100000000}";
		var readings = new List<(string Path, string Props)>();
		for (var i = 0; i < count; i++)
		{
			var put = await Fm.Get("rowput", ("ptr", now.Staff), ("i", i.ToString()), ("to", $"{scope}.r{i}"));
			if (put.StartsWith("error", StringComparison.Ordinal)) return Report(put);
			readings.Add(($"{scope}.r{i}", "Name"));
		}
		var people = (await PullEach(readings)).Select(r => Field(r, "Name")).ToList();
		if (people.Count == 0 || people.Any(p => p.Length == 0))
		{
			Console.Error.WriteLine(L($"{duty.Title}: kimlere verilebileceği okunamadı."));
			return 1;
		}

		string person;
		if (words.Count > 1)
		{
			if (Choose(people, string.Join(" ", words.Skip(1)), duty.Title) is not { } picked) return 1;
			person = picked;
		}
		else if (Ui.Interactive)
		{
			if (Ui.Ask(duty.Title, people, current: people.IndexOf(now.Holder), cancel: L("Vazgeç")) is not { } picked) return 0;
			person = people[picked];
		}
		else
		{
			Console.WriteLine(L($"{duty.Title}: {now.Holder}\nVerilebilecekler: {string.Join(" · ", people)}"));
			return 0;
		}
		if (person == now.Holder)
		{
			Console.WriteLine(L($"{duty.Title}: zaten {person}."));
			return 0;
		}

		var said = await Fm.Get("select", ("ptr", now.Staff), ("index", people.IndexOf(person).ToString()));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		Duty after = null;
		var changed = await Until(async () =>
		{
			var dialogs = await Fm.GetJson("dialog");
			if (dialogs.GetArrayLength() > 0 && yes)
			{
				var go = dialogs[0].GetProperty("Buttons").EnumerateArray().FirstOrDefault(x => Field(x, "Name").Contains("primary", StringComparison.Ordinal));
				if (go.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(go, "Pointer")));
			}
			else if (dialogs.GetArrayLength() > 0) return true;
			var anchor = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Where(b => Field(b, "Name") is "TakeControlAll" or "DelegateAll").Select(b => Field(b, "Label")).FirstOrDefault();
			if (anchor == null) return false;
			after = ParseDuties((await Fm.GetJson("form", ("anchor", anchor), ("up", "12"), ("buttons", "1"))).EnumerateArray().ToList())
				.FirstOrDefault(d => d.Group == duty.Group && d.Title == duty.Title);
			return after?.Holder == person;
		}, seconds: 6, every: 0.5);

		var asked = await Fm.GetJson("dialog");
		if (asked.GetArrayLength() > 0)
		{
			Console.Error.WriteLine(L($"Oyun soruyor: {Field(asked[0], "Title")} — {Field(asked[0], "Description")}   (cevap: dialog, ya da komutu --yes ile yinele)"));
			return 1;
		}
		if (!changed)
		{
			Console.Error.WriteLine(L($"{duty.Title}: {person} seçildi ama görev hâlâ {after?.Holder ?? now.Holder}."));
			return 1;
		}
		Console.WriteLine($"{duty.Title}: {now.Holder} → {person}");
		return 0;
	}

	/// <summary>Takes a duty back or hands it on, with the page's own button for it.</summary>
	private static async Task<int> SetDuty(bool take, string typed, bool yes)
	{
		if (typed.Length == 0)
		{
			Console.Error.WriteLine(L($"Hangi görev?  responsibilities {(take ? "take" : "delegate")} \"<görev>\""));
			return 64;
		}
		if (await FindDuty(typed) is not { } duty) return 64;
		if (duty.Mine == take)
		{
			Console.WriteLine(L($"{duty.Title}: zaten {(take ? L("sende") : L($"devredilmiş ({duty.Holder})"))}."));
			return 0;
		}

		// Back up the list to where the duty is laid out, its buttons with it.
		await ToTop();
		var now = (await Duties(stopAt: duty.Title)).FirstOrDefault(d => d.Group == duty.Group && d.Title == duty.Title);
		if (now == null)
		{
			Console.Error.WriteLine(L($"{duty.Title}: sayfada bulunamadı."));
			return 1;
		}
		if (take ? !now.CanTake : !now.CanDelegate)
		{
			Console.Error.WriteLine(L($"{duty.Title}: oyun bunu {(take ? L("geri aldırmıyor") : L("devrettirmiyor"))} (buton kapalı)."));
			return 1;
		}

		var clicked = await Fm.Get("click", ("ptr", take ? now.Take : now.Delegate));
		if (clicked.StartsWith("error", StringComparison.Ordinal)) return Report(clicked);

		Duty after = null;
		var changed = await Until(async () =>
		{
			var dialogs = await Fm.GetJson("dialog");
			if (dialogs.GetArrayLength() > 0 && yes)
			{
				var go = dialogs[0].GetProperty("Buttons").EnumerateArray().FirstOrDefault(x => Field(x, "Name").Contains("primary", StringComparison.Ordinal));
				if (go.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(go, "Pointer")));
			}
			else if (dialogs.GetArrayLength() > 0) return true;
			var anchor = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Where(b => Field(b, "Name") is "TakeControlAll" or "DelegateAll").Select(b => Field(b, "Label")).FirstOrDefault();
			if (anchor == null) return false;
			after = ParseDuties((await Fm.GetJson("form", ("anchor", anchor), ("up", "12"), ("buttons", "1"))).EnumerateArray().ToList())
				.FirstOrDefault(d => d.Group == duty.Group && d.Title == duty.Title);
			// The holder's name follows the buttons by a moment.
			return after != null && after.Mine == take && after.Holder != duty.Holder;
		}, seconds: 6, every: 0.5) || after?.Mine == take;

		var asked = await Fm.GetJson("dialog");
		if (asked.GetArrayLength() > 0)
		{
			Console.Error.WriteLine(L($"Oyun soruyor: {Field(asked[0], "Title")} — {Field(asked[0], "Description")}   (cevap: dialog, ya da komutu --yes ile yinele)"));
			return 1;
		}
		if (!changed || after == null)
		{
			Console.Error.WriteLine(L($"{duty.Title}: butona basıldı ama durum değişmedi."));
			return 1;
		}
		Console.WriteLine(take ? L($"{duty.Title}: artık sende.") : L($"{duty.Title}: devredildi → {after.Holder}"));
		return 0;
	}
}
