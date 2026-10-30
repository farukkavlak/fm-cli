using System.Text.Json;
using Spectre.Console;

namespace FmCli;

internal static partial class Program
{
	/// <summary>Sliders whose labels did not match the game's own label for the step in force: shown by number.</summary>
	private static readonly HashSet<string> stepsUnsure = [];

	/// <summary>A setting's control on the read page: the first laid out for its preference key.</summary>
	private static SettingNow Locate(Setting setting, List<JsonElement> form)
	{
		var kind = setting.Kind.ToString().ToLowerInvariant();
		var control = form.FirstOrDefault(i => Field(i, "Kind") == kind && string.Equals(Field(i, "Pref"), setting.Pref, StringComparison.OrdinalIgnoreCase));
		if (control.ValueKind != JsonValueKind.Object) return null;

		var enabled = Field(control, "Enabled") == "True";
		var pointer = Field(control, "Pointer");
		switch (setting.Kind)
		{
			case SettingKind.Slider:
				var steps = setting.Steps;
				var high = (int)double.Parse(Field(control, "High"), System.Globalization.CultureInfo.InvariantCulture);
				// Should the game ever change the steps, the numbers are still honest.
				var choices = steps != null && steps.Length == high + 1 && !stepsUnsure.Contains(setting.Pref)
					? steps.ToList() : Enumerable.Range(0, high + 1).Select(i => i.ToString()).ToList();
				return new(setting, pointer, choices, choices, (int)double.Parse(Field(control, "Value"), System.Globalization.CultureInfo.InvariantCulture), enabled);
			case SettingKind.Checkbox:
				return new(setting, pointer, [L("Off"), L("On")], ["Off", "On"], Field(control, "Value") == "True" ? 1 : 0, enabled);
			default:
				var source = Strings(control, "Choices");
				var shown = Strings(control, "Shown");
				if (shown.Count != source.Count) shown = source;
				var current = int.TryParse(Field(control, "Selected"), out var selected) ? selected : -1;
				return new(setting, pointer, shown, source, current, enabled);
		}

		static List<string> Strings(JsonElement item, string name) =>
			item.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(c => c.GetString() ?? "").ToList() : [];
	}

	/// <summary>
	/// Whether a slider's labels are the game's: the label of the step in force, which the game keeps
	/// beside the step ("1" for 1x), against ours for that step. Asked once per read of the page.
	/// </summary>
	private static async Task CheckSteps(List<JsonElement> form)
	{
		foreach (var setting in Settings.Where(s => s.Steps != null && s.StepShown != null))
		{
			var now = Locate(setting, form);
			if (now == null || now.Current < 0 || now.Current >= setting.Steps!.Length) continue;
			var held = await Fm.Get("get", ("path", setting.StepShown!));
			var label = held.Contains(')') ? held[(held.IndexOf(')') + 1)..].Trim() : held.Trim();
			if (label.StartsWith("error", StringComparison.Ordinal)) continue;
			if (setting.Steps[now.Current].TrimEnd('x') != label) stepsUnsure.Add(setting.Pref);
		}
	}

	private static string ValueOf(SettingNow now) => now.Current >= 0 && now.Current < now.Choices.Count ? now.Choices[now.Current] : "?";

	/// <summary>A choice by its name as the game shows it or in English, whole first and then by its start; or the number of a step.</summary>
	private static int MatchChoice(SettingNow now, string value)
	{
		var wanted = Fold(value);
		var names = now.Choices.Select((c, i) => (Game: Fold(c), Source: Fold(i < now.Source.Count ? now.Source[i] : c))).ToList();

		var exact = names.FindIndex(n => n.Game == wanted || n.Source == wanted);
		if (exact >= 0) return exact;
		if (now.Setting.Kind == SettingKind.Checkbox)
			return wanted is "ac" or "acik" or "on" or "evet" or "1" ? 1 : wanted is "kapat" or "kapali" or "off" or "hayir" or "0" ? 0 : -1;
		// "2" for 2x.
		if (now.Setting.Kind == SettingKind.Slider && names.FindIndex(n => n.Game == wanted + "x") is >= 0 and var step) return step;

		var starts = Enumerable.Range(0, names.Count).Where(i => names[i].Game.StartsWith(wanted, StringComparison.Ordinal) || names[i].Source.StartsWith(wanted, StringComparison.Ordinal)).ToList();
		if (starts.Count == 1) return starts[0];

		// "euro" for "€ - Euro", "uk pound" for "£ - UK Pound Sterling".
		var inside = Enumerable.Range(0, names.Count).Where(i => names[i].Game.Contains(wanted, StringComparison.Ordinal) || names[i].Source.Contains(wanted, StringComparison.Ordinal)).ToList();
		return inside.Count == 1 ? inside[0] : -1;
	}

	private static void DrawSettings(List<SettingNow> found)
	{
		if (!Ui.Rich)
		{
			foreach (var group in found.GroupBy(f => f.Setting.Group))
			{
				Console.WriteLine(group.Key);
				foreach (var f in group) Console.WriteLine($"  {f.Setting.Name}: {ValueOf(f)}");
			}
			return;
		}

		var table = new Spectre.Console.Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).HideHeaders()
			.AddColumn(L("setting")).AddColumn(L("value")).AddColumn("not");
		var first = true;
		foreach (var group in found.GroupBy(f => f.Setting.Group))
		{
			if (!first) table.AddEmptyRow();
			first = false;
			table.AddRow($"[bold]{Ui.Safe(group.Key)}[/]", "", "");
			foreach (var f in group)
				table.AddRow("  " + Ui.Safe(f.Setting.Name), $"[yellow]{Ui.Safe(ValueOf(f))}[/]", $"[dim]{Ui.Safe(f.Setting.Hint ?? "")}[/]");
		}
		Ui.Out.Write(table);
	}
}
