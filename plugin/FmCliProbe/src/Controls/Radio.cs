using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// Radio groups: a set of tiles of which one is chosen.
///
/// Like a dropdown, a radio group writes its choice to a bound value instead of sending an
/// event, so it is listed and chosen from alongside the dropdowns. Choosing goes through the
/// tile's own click handler, which leaves the writing and any selection events to the game.
/// </summary>
internal static class Radio
{
	public static IEnumerable<SIRadioGroup> Groups()
	{
		foreach (var element in Buttons.Elements())
		{
			SIRadioGroup group;
			try { group = element.TryCast<SIRadioGroup>(); }
			catch { continue; }
			if (group == null) continue;

			// A page of a wizard that has been left stays loaded with its tiles emptied, and a
			// group nobody can read a choice from is not one that can be chosen from.
			List<string> choices;
			try { choices = Choices(group); } catch { continue; }
			if (choices.All(string.IsNullOrEmpty)) continue;

			yield return group;
		}
	}

	public static string Name(SIRadioGroup group)
	{
		try { return group.name ?? ""; } catch { return ""; }
	}

	public static List<string> Choices(SIRadioGroup group, bool shown = false)
	{
		var names = new List<string>();
		var buttons = group.m_radioButtons;
		var count = buttons?.Count ?? 0;
		for (var i = 0; i < count; i++) names.Add(Buttons.LabelOf(buttons[i], shown));
		return names;
	}

	public static string Describe(SIRadioGroup group)
	{
		var sb = new StringBuilder();
		var choices = Choices(group);
		var selected = group.m_selectionIndex;

		string enabled;
		try { enabled = group.enabledInHierarchy ? "" : "  DISABLED"; } catch { enabled = ""; }

		sb.AppendLine($"0x{group.Pointer.ToInt64():x}  radio  name={Name(group)}  {choices.Count} choice(s), selected {selected}{enabled}");
		for (var i = 0; i < choices.Count; i++) sb.AppendLine($"      {(i == selected ? '*' : ' ')} {i,2}  {choices[i]}");
		return sb.ToString();
	}

	public static string Json(SIRadioGroup group)
	{
		var choices = Choices(group);
		var selected = group.m_selectionIndex;
		// As a list's: the data paths it reads its items from and writes its choice to, which say whose
		// group it is in any language (a group often has no name of its own).
		string items = "", binds = "";
		try { items = group.ItemsBinding.Path ?? ""; } catch { }
		try { binds = group.SelectionBinding.Path ?? ""; } catch { }
		if (binds.Length == 0) try { binds = group.IndexBinding.Path ?? ""; } catch { }
		return "{"
			+ $"\"Pointer\": \"0x{group.Pointer.ToInt64():x}\", "
			+ $"\"Name\": \"{Report.Escape(Name(group))}\", "
			+ $"\"Items\": \"{Report.Escape(items)}\", \"Binds\": \"{Report.Escape(binds)}\", "
			+ $"\"Label\": \"{Report.Escape(selected >= 0 && selected < choices.Count ? choices[selected] : "")}\", "
			+ $"\"Enabled\": \"{(group.enabledInHierarchy ? "True" : "False")}\", "
			+ $"\"Selected\": \"{selected}\", "
			+ "\"Choices\": [" + string.Join(", ", choices.Select(n => $"\"{Report.Escape(n ?? "")}\"")) + "], "
			+ "\"Shown\": [" + string.Join(", ", Choices(group, shown: true).Select(n => $"\"{Report.Escape(n ?? "")}\"")) + "]}";
	}

	/// <summary>Null when no radio group is meant by the request, so the caller can say so its own way.</summary>
	public static string Select(NameValueCollection q)
	{
		var name = q["name"];
		var wanted = q["choice"];

		var groups = Groups().ToList();
		if (!string.IsNullOrEmpty(q["ptr"]))
		{
			if (!Buttons.TryPointer(q["ptr"], out var pointer)) return null;
			groups = groups.Where(g => g.Pointer == pointer).ToList();
		}
		else
		{
			if (string.IsNullOrEmpty(name)) return null;
			groups = groups.Where(g => string.Equals(Name(g), name, StringComparison.OrdinalIgnoreCase)).ToList();
			if (groups.Count > 1 && !string.IsNullOrEmpty(wanted))
				groups = groups.Where(g => IndexOf(Choices(g), wanted) >= 0).ToList();
		}

		if (groups.Count == 0) return null;
		if (groups.Count > 1) return $"error: {groups.Count} radio groups named '{name}' fit; the choice does not tell them apart";

		var group = groups[0];
		var choices = Choices(group);

		int index;
		if (!string.IsNullOrEmpty(wanted))
		{
			index = IndexOf(choices, wanted);
			if (index < 0) return $"error: no choice '{wanted}' in this radio group (see /dropdowns)";
		}
		else if (!int.TryParse(q["index"], out index)) return "error: ?index=<n> or ?choice=<text> required";

		if (index < 0 || index >= choices.Count) return $"error: index {index} is outside 0..{choices.Count - 1}";

		var before = group.m_selectionIndex;
		var described = $"'{choices[index]}' (index {index}, was {before})";
		if (q["dry"] == "1") return $"dry run, NOT selected: {described}";

		var button = group.m_radioButtons[index].TryCast<SIRadioButton>();
		if (button == null) return "error: that choice is not a radio button";

		button.OnClickedOrSubmitted();
		return $"selected {described}, now {group.m_selectionIndex}";
	}

	private static int IndexOf(List<string> choices, string wanted) =>
		choices.FindIndex(c => string.Equals(c, wanted, StringComparison.OrdinalIgnoreCase));
}
