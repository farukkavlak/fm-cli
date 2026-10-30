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
/// also those the page has scrolled out of view.
///
/// A control outside any row (FM27's individual training: one player's lists beside the table)
/// is for what its page keeps in a variable of the scope it writes into (?var=person); that
/// value is its item.
/// </summary>
internal static class RowControls
{
	public static string Read(NameValueCollection q)
	{
		var wanted = q["event"];
		if (string.IsNullOrEmpty(wanted)) return "error: ?event=<simulation event id> required (see /buttons)";
		if (q["buttons"] == "1") return RowButtons(wanted, q["name"], q["gen"]);

		var variable = q["var"];
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
			else if (!string.IsNullOrEmpty(variable)) found.Add((dropdown, null, -1));
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
#if FM26
				var held = table?.GetItemFromSource(index);
#else
				// FM27's table keeps each row by its place in view (TableRows.Laid).
				var held = table?.GetItemFromView(index);
#endif
				var item = table != null ? held?.TryCast<SI.Core.TypedValue>() : Kept(dropdown, variable);
				if (item is { IsNull: false } && RawPath.Set($"{under}.{k}", item) == null)
					path = $"{under}.{k}";
			}
			catch { }

			string page = null;
			try
			{
				var events = dropdown.SelectionEvents?.Events;
				for (var i = 0; page == null && i < (events?.Length ?? 0); i++)
					if (events[i] != null) page = Buttons.PageOf(events[i]);
			}
			catch { }

			var choices = new List<string>();
			var list = dropdown.m_choices;
			for (var i = 0; i < (list?.Count ?? 0); i++) choices.Add($"\"{Report.Escape(Buttons.ChoiceText(list[i]) ?? "")}\"");
			rows.Add($"{{\"Item\": \"{path}\", \"Index\": {index}, \"Page\": \"{page}\", \"Pointer\": \"0x{dropdown.Pointer.ToInt64():x}\", \"Enabled\": \"{(dropdown.enabledInHierarchy ? "True" : "False")}\", \"Writes\": \"{Report.Escape(table == null ? Writes(dropdown) : "")}\", \"Value\": \"{Report.Escape(Buttons.LabelOf(dropdown))}\", \"Choices\": [{string.Join(", ", choices)}]}}");
		}
		return "[" + string.Join(",\n ", rows) + "]\n";
	}

	/// <summary>
	/// The buttons that send this event (?event=EventToPlugin:EventFmxTransferCentreResponseAccept&amp;buttons=1),
	/// each with the table item of the row it sits in, put at a path: one answer per offer row, told
	/// apart by the offer's own record rather than by its place among the buttons. &amp;name= narrows
	/// an event many buttons send (OpenPanel) to one element name (transfer-link-button).
	/// </summary>
	private static string RowButtons(string wanted, string name, string generation)
	{
		var under = $"fmcli.rowbtn.g{generation ?? "0"}";
		var rows = new List<string>();
		var k = 0;
		foreach (var (button, label) in Buttons.Sending(wanted))
		{
			var path = "";
			var index = -1;
			var enabled = false;
			try
			{
				if (button.worldBound.width <= 0) continue;
				// A kind of event many buttons send (OpenPanel) is narrowed by the element's name.
				if (!string.IsNullOrEmpty(name) && button.name != name) continue;
				enabled = button.enabledInHierarchy;
				if (RowOf(button) is { } at)
				{
					index = at.Index;
#if FM26
					var held = at.Table.GetItemFromSource(at.Index);
#else
					var held = at.Table.GetItemFromView(at.Index);
#endif
					if (held?.TryCast<SI.Core.TypedValue>() is { IsNull: false } item && RawPath.Set($"{under}.{k}", item) == null)
						path = $"{under}.{k}";
				}
			}
			catch { }
			rows.Add($"{{\"Item\": \"{path}\", \"Index\": {index}, \"Pointer\": \"0x{button.Pointer.ToInt64():x}\", \"Enabled\": \"{(enabled ? "True" : "False")}\", \"Label\": \"{Report.Escape(label ?? "")}\"}}");
			k++;
		}
		return "[" + string.Join(",\n ", rows) + "]\n";
	}

	/// <summary>
	/// The value kept under this variable in the scope the control writes into: the path the
	/// list writes to is climbed one name at a time, and the first place that has the variable
	/// beside it holds it (the individual training panel: "….person").
	/// </summary>
	private static SI.Core.TypedValue Kept(SIDropdown control, string variable)
	{
		var b = Game.Bindings;
		var written = control.m_selectionValueBindingKey;
		if (!b.Exists(ref written)) return null;
		var path = b.GetPathDebug(ref written) ?? "";
		for (var cut = path.LastIndexOf('.'); cut > 0; cut = path.LastIndexOf('.', cut - 1))
		{
			var key = PathKey.Of($"{path[..cut]}.{variable}");
			if (b.Exists(ref key) && b.Get(ref key) is { IsNull: false } held) return held;
		}
		return null;
	}

	/// <summary>The path a control writes to, for finding where its scope lives.</summary>
	private static string Writes(SIDropdown control)
	{
		try
		{
			var b = Game.Bindings;
			var written = control.m_selectionValueBindingKey;
			return b.Exists(ref written) ? b.GetPathDebug(ref written) ?? "" : "";
		}
		catch { return ""; }
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
