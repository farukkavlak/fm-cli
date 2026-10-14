using System.Collections.Generic;
using System.Collections.Specialized;
using UnityEngine.UIElements;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// The dropdowns of a table that repeats one per row, told apart by what they are for and whose
/// row they sit in rather than by any text or element name: what they are for is the simulation
/// event they send (EventFmxPlayerTrainingFocusChanged), whose row is the table's own item for
/// it, put at a path so its properties can be read (a player's UniqueId).
///
/// Only the rows laid out are found. The individual training table lays out all of its rows,
/// also those the page has scrolled out of view (FINDINGS §8.186).
/// </summary>
internal static class RowControls
{
	public static string Read(NameValueCollection q)
	{
		var wanted = q["event"];
		if (string.IsNullOrEmpty(wanted)) return "error: ?event=<simulation event id> required (see /buttons)";

		var found = new List<(SIDropdown Dropdown, StreamedListView Table, int Index)>();
		foreach (var element in Buttons.Elements())
		{
			SIDropdown dropdown;
			try
			{
				dropdown = element.TryCast<SIDropdown>();
				if (dropdown == null || dropdown.panel == null || dropdown.worldBound.width <= 0) continue;
				if (!Sends(dropdown, wanted)) continue;
			}
			catch { continue; }
			if (RowOf(dropdown) is { } at) found.Add((dropdown, at.Table, at.Index));
		}

		// Each reading gets rows of its own (?gen=), as /pull's lists do: a row left from an
		// earlier reading would answer with the item it held then.
		var under = $"fmcli.rowctl.g{q["gen"] ?? "0"}";
		var rows = new List<string>();
		for (var k = 0; k < found.Count; k++)
		{
			var (dropdown, table, index) = found[k];
			var path = "";
			try
			{
				if (table.GetItemFromSource(index)?.TryCast<SI.Core.TypedValue>() is { IsNull: false } item && RawPath.Set($"{under}.{k}", item) == null)
					path = $"{under}.{k}";
			}
			catch { }

			var choices = new List<string>();
			var list = dropdown.m_choices;
			for (var i = 0; i < (list?.Count ?? 0); i++) choices.Add($"\"{Report.Escape(Buttons.ChoiceText(list[i]) ?? "")}\"");
			rows.Add($"{{\"Item\": \"{path}\", \"Index\": {index}, \"Pointer\": \"0x{dropdown.Pointer.ToInt64():x}\", \"Value\": \"{Report.Escape(Buttons.LabelOf(dropdown))}\", \"Choices\": [{string.Join(", ", choices)}]}}");
		}
		return "[" + string.Join(",\n ", rows) + "]\n";
	}

	private static bool Sends(SIDropdown dropdown, string wanted)
	{
		var events = dropdown.SelectionEvents;
		for (var i = 0; i < (events?.Events?.Length ?? 0); i++)
			if (events.Events[i] is { } context && Buttons.EventId(context) == wanted) return true;
		return false;
	}

	/// <summary>
	/// The table a control sits in, and which of its items the row holding the control is for:
	/// the table keeps the index of the item each row in view is for.
	/// </summary>
	private static (StreamedListView Table, int Index)? RowOf(VisualElement control)
	{
		var passed = new List<VisualElement>();
		for (var at = control; at != null; at = at.parent)
		{
			StreamedListView table = null;
			try { table = at.TryCast<StreamedListView>(); } catch { }
			if (table == null)
			{
				passed.Add(at);
				continue;
			}
			try
			{
				var rows = table.m_boundElements;
				foreach (var element in passed)
					if (rows.ContainsKey(element)) return (table, rows[element]);
			}
			catch { }
			return null;
		}
		return null;
	}
}
