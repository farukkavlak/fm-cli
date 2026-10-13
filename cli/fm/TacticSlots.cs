using System.Text.Json;

namespace FmCli;

/// <summary>
/// Copying a tactic into a slot of its own and clearing a slot (FINDINGS §8.77). The tactics page
/// has two menus without labels: one for the slot's tactic ("Edit Tactic", "Presets… ›", "Save",
/// "Load…", "Manage Tactics…", "Clear Slot") and one to fill a slot ("Create New Tactic", "Load
/// Tactic", "Copy Tactic ›" with the tactics to copy in its submenu).
/// </summary>
internal static partial class Program
{
	/// <summary>The tactics page's menu that offers this choice, once the page is up.</summary>
	private static async Task<JsonElement?> TacticMenu(string choice)
	{
		var opened = await OpenPage(Screen.Tactics);
		if (opened.StartsWith("error", StringComparison.Ordinal)) { Report(opened); return null; }
		JsonElement menu = default;
		await Until(async () => (menu = (await Fm.GetJson("dropdowns", ("json", "1"), ("deep", "1"))).EnumerateArray()
			.FirstOrDefault(d => d.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == choice))).ValueKind == JsonValueKind.Object, seconds: 8, every: 0.5);
		return menu.ValueKind == JsonValueKind.Object ? menu : null;
	}

	private static async Task<int> TacticCopy(string wanted, bool dry)
	{
		if (await TacticMenu("Copy Tactic ›") is not { } menu)
		{
			Console.Error.WriteLine("Kopyalanacak yer yok: üç taktik yuvası dolu olabilir (yuvalar için: tactic slot).");
			return 1;
		}
		var at = menu.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList().IndexOf("Copy Tactic ›");
		var tactics = menu.TryGetProperty("Submenus", out var subs) && subs.TryGetProperty(at.ToString(), out var list)
			? list.EnumerateArray().Select(t => t.GetString() ?? "").ToList()
			: [];
		if (tactics.Count == 0)
		{
			Console.Error.WriteLine("Kopyalanacak taktik yok.");
			return 1;
		}

		string tactic;
		if (wanted != null)
		{
			if (Choose(tactics, wanted, "Taktik") is not { } named) return 1;
			tactic = named;
		}
		else if (tactics.Count == 1) tactic = tactics[0];
		else if (Ui.Interactive && Ui.Ask("Hangi taktik kopyalansın?", tactics) is { } chosen) tactic = tactics[chosen];
		else
		{
			foreach (var t in tactics) Console.WriteLine($"  {t}");
			Console.WriteLine("\nKopyalamak için: tactic copy \"<taktik>\"");
			return 0;
		}

		if (dry)
		{
			Console.WriteLine($"(kuru) '{tactic}' yeni bir yuvaya kopyalanacak.");
			return 0;
		}
		var before = int.TryParse(Field(await TacticInfo(), "TacticCount"), out var c0) ? c0 : 0;
		var sent = await Fm.Get("select", ("ptr", Field(menu, "Pointer")), ("names", $"Copy Tactic>{tactic}"), ("fire", "1"));
		if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
		JsonElement info = default;
		var copied = await Until(async () => int.TryParse(Field(info = await TacticInfo(), "TacticCount"), out var c) && c > before, seconds: 6, every: 0.25);
		Console.WriteLine(copied
			? $"Kopyalandı. Etkin: {(int.TryParse(Field(info, "CurrentTacticSlot"), out var s) ? s + 1 : 0)}. yuva, {Field(info, "Name")}. Yuvalar: tactic slot"
			: "Kopya görünmedi; yuvalara bak: tactic slot");
		return copied ? 0 : 1;
	}

	private static async Task<int> TacticClear(string wanted, bool yes, bool dry)
	{
		var info = await TacticInfo();
		if (Field(info, "TacticCount") == "1")
		{
			Console.Error.WriteLine("Tek taktik var; boşaltılmaz.");
			return 1;
		}
		// The menu clears the slot that is in use, so the one asked for is made so first.
		var active = int.TryParse(Field(info, "CurrentTacticSlot"), out var a) ? (a + 1).ToString() : "";
		if (wanted != null && wanted != active)
		{
			if (dry) { Console.WriteLine($"(kuru) {wanted}. yuvaya geçilip boşaltılacak."); return 0; }
			var switched = await TacticSlot(wanted, false);
			if (switched != 0) return switched;
			info = await TacticInfo();
		}
		var slot = int.TryParse(Field(info, "CurrentTacticSlot"), out var b) ? b + 1 : 0;
		if (!yes || dry)
		{
			Console.WriteLine($"{slot}. yuva ({Field(info, "Name")}) boşaltılacak. Onay için: tactic clear {slot} --yes");
			return 0;
		}
		if (await TacticMenu("Clear Slot") is not { } menu)
		{
			Console.Error.WriteLine("Taktik menüsünde 'Clear Slot' yok.");
			return 1;
		}
		var before = int.TryParse(Field(info, "TacticCount"), out var c0) ? c0 : 0;
		var sent = await Fm.Get("select", ("ptr", Field(menu, "Pointer")), ("choice", "Clear Slot"), ("fire", "1"));
		if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);

		// The game may ask first; the answer that goes ahead is its primary button.
		JsonElement dialogs = default;
		if (await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 3, every: 0.5))
		{
			var go = dialogs[0].GetProperty("Buttons").EnumerateArray().FirstOrDefault(x => Field(x, "Name").Contains("primary", StringComparison.Ordinal));
			Console.WriteLine($"{Field(dialogs[0], "Title")} → {Field(go, "Label")}");
			if (go.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(go, "Pointer")));
		}
		var cleared = await Until(async () => int.TryParse(Field(info = await TacticInfo(), "TacticCount"), out var c) && c < before, seconds: 6, every: 0.25);
		Console.WriteLine(cleared ? $"{slot}. yuva boşaltıldı. Etkin: {Field(info, "Name")}" : "Yuva boşalmadı; bak: tactic slot");
		return cleared ? 0 : 1;
	}
}
