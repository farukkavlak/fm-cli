using System.Text.Json;
using Spectre.Console;

namespace FmCli;

internal static partial class Program
{
	private static SettingNow Locate(Setting setting, List<JsonElement> form)
	{
		var kind = setting.Kind.ToString().ToLowerInvariant();

		// A list is taken by the block it sits in — the template's own name, the same in every
		// language — and only failing that by the heading above it, which is the game's words.
		var control = setting.Block != null
			? form.FirstOrDefault(i => Field(i, "Kind") == kind && Inside(i, setting.Block))
			: default;
		if (control.ValueKind != JsonValueKind.Object)
		{
			var at = form.FindIndex(i => Field(i, "Kind") == "text" && Field(i, "Text") == setting.Heading);
			if (at < 0) return null;
			control = form.Skip(at + 1).FirstOrDefault(i => Field(i, "Kind") == kind
				&& (setting.Offers == null || i.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == setting.Offers)));
			if (control.ValueKind != JsonValueKind.Object) return null;
		}

		var enabled = Field(control, "Enabled") == "True";
		switch (setting.Kind)
		{
			case SettingKind.Slider:
				var steps = setting.Steps;
				var high = (int)double.Parse(Field(control, "High"), System.Globalization.CultureInfo.InvariantCulture);
				// Should the game ever change the steps, the numbers are still honest.
				var choices = steps != null && steps.Length == high + 1 ? steps.ToList() : Enumerable.Range(0, high + 1).Select(i => i.ToString()).ToList();
				return new(setting, Field(control, "Pointer"), choices, (int)double.Parse(Field(control, "Value"), System.Globalization.CultureInfo.InvariantCulture), enabled);
			case SettingKind.Checkbox:
				return new(setting, Field(control, "Pointer"), [L("Off"), L("On")], Field(control, "Value") == "True" ? 1 : 0, enabled);
			default:
				var list = control.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
				// Some lists leave their index unset; the label still says what is in force.
				var current = int.TryParse(Field(control, "Selected"), out var selected) && selected >= 0 ? selected : list.IndexOf(Field(control, "Label"));
				return new(setting, Field(control, "Pointer"), list, current, enabled);
		}
	}

	private static string ValueOf(SettingNow now) => now.Current >= 0 && now.Current < now.Choices.Count ? now.Choices[now.Current] : "?";

	/// <summary>Whether a control sits inside the named block: the chain of element names around it.</summary>
	private static bool Inside(JsonElement item, string block)
	{
		if (!item.TryGetProperty("Within", out var within) || within.ValueKind != JsonValueKind.Array) return false;
		foreach (var name in within.EnumerateArray())
			if (name.ValueKind == JsonValueKind.String && name.GetString() == block) return true;
		return false;
	}

	private static string Shown(Setting setting, string value) =>
		setting.Turkish != null && setting.Turkish.TryGetValue(value, out var turkish) ? turkish : value;

	/// <summary>A choice by its Turkish or English name, whole first and then by its start; or the number of a step.</summary>
	private static int MatchChoice(SettingNow now, string value)
	{
		var wanted = Fold(value);
		var names = now.Choices.Select(c => (Game: Fold(c), Ours: Fold(Shown(now.Setting, c)))).ToList();

		var exact = names.FindIndex(n => n.Game == wanted || n.Ours == wanted);
		if (exact >= 0) return exact;
		if (now.Setting.Kind == SettingKind.Checkbox)
			return wanted is "ac" or "acik" or "on" or "evet" or "1" ? 1 : wanted is "kapat" or "kapali" or "off" or "hayir" or "0" ? 0 : -1;
		// "2" for 2x, "kilometre" for Kilometres.
		if (now.Setting.Kind == SettingKind.Slider && names.FindIndex(n => n.Game == wanted + "x") is >= 0 and var step) return step;

		var starts = Enumerable.Range(0, names.Count).Where(i => names[i].Game.StartsWith(wanted, StringComparison.Ordinal) || names[i].Ours.StartsWith(wanted, StringComparison.Ordinal)).ToList();
		if (starts.Count == 1) return starts[0];

		// "euro" for "€ - Euro", "uk pound" for "£ - UK Pound Sterling".
		var inside = Enumerable.Range(0, names.Count).Where(i => names[i].Game.Contains(wanted, StringComparison.Ordinal) || names[i].Ours.Contains(wanted, StringComparison.Ordinal)).ToList();
		return inside.Count == 1 ? inside[0] : -1;
	}

	private static void DrawSettings(List<SettingNow> found)
	{
		if (!Ui.Rich)
		{
			foreach (var group in found.GroupBy(f => f.Setting.Group))
			{
				Console.WriteLine(group.Key);
				foreach (var f in group) Console.WriteLine($"  {f.Setting.Name}: {Shown(f.Setting, ValueOf(f))}");
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
				table.AddRow("  " + Ui.Safe(f.Setting.Name), $"[yellow]{Ui.Safe(Shown(f.Setting, ValueOf(f)))}[/]", $"[dim]{Ui.Safe(f.Setting.Hint ?? "")}[/]");
		}
		Ui.Out.Write(table);
	}
}
