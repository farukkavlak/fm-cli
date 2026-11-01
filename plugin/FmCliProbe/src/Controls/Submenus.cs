using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// A dropdown's submenus, as the game builds them: opening the list lays out a row per choice
/// (<c>DropdownRow</c>), and a row with a submenu carries a child dropdown of its own
/// whose choices are the submenu. This opens a list and reports its rows.
/// </summary>
internal static class Submenus
{
	/// <summary>Opens (or with close=1 closes) the dropdown at ptr, then lists the rows on screen.</summary>
	public static string Open(NameValueCollection q)
	{
		if (!Buttons.TryPointer(q["ptr"] ?? "", out var pointer)) return "error: ?ptr=<0x…> required";
		var dropdown = Buttons.AllDropdowns().FirstOrDefault(d => d.Pointer == pointer);
		if (dropdown == null) return $"error: no dropdown at {q["ptr"]}";

		if (q["close"] == "1") { dropdown.CloseSelfAndAllChildren(); return "closed"; }
		if (q["show"] != "0") dropdown.ShowDropdown();
		return Rows();
	}

	/// <summary>
	/// A row of an open list, chosen the way the keyboard chooses it: a submit event handed to
	/// the row's own handler (not sent through the panel, where the crash reporter's click
	/// listener wants a real pointer). how=click goes through the shared
	/// click-or-submit handler instead.
	/// </summary>
	public static string Submit(NameValueCollection q)
	{
		if (!Buttons.TryPointer(q["ptr"] ?? "", out var pointer)) return "error: ?ptr=<0x…> required";
		foreach (var element in Buttons.Elements())
		{
			if (element.Pointer != pointer) continue;
			var row = element.TryCast<DropdownRow>();
			if (row == null) return "error: not a dropdown row";
			var evt = NavigationEventBase<NavigationSubmitEvent>.GetPooled(UnityEngine.EventModifiers.None);
			// FM27 dropped OnNavigationSubmit; the shared click-or-submit handler is what is left.
			row.OnClickedOrSubmitted(evt);
			return "submitted\n" + Rows();
		}
		return $"error: no element at {q["ptr"]}";
	}

	/// <summary>
	/// One level of a dropdown's menu as JSON names: its top level, or with names=a>b the
	/// submenu under that path. A name ending in " ›" opens a further submenu.
	/// </summary>
	public static string Menu(NameValueCollection q)
	{
		if (!Buttons.TryPointer(q["ptr"] ?? "", out var pointer)) return "error: ?ptr=<0x…> required";
		var dropdown = Buttons.AllDropdowns().FirstOrDefault(d => d.Pointer == pointer);
		if (dropdown == null) return $"error: no dropdown at {q["ptr"]}";

		if (q["tree"] == "1") return Tree(dropdown);

		var level = dropdown.m_choices;
		foreach (var part in (q["names"] ?? "").Split('>', System.StringSplitOptions.RemoveEmptyEntries))
		{
			var wanted = part.Trim();
			Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue> next = null;
			for (var i = 0; i < (level?.Count ?? 0); i++)
			{
				var text = (Buttons.ChoiceText(level[i]) ?? "").Replace(" ›", "");
				if (!string.Equals(text, wanted, System.StringComparison.OrdinalIgnoreCase)) continue;
				next = Buttons.Children(level[i]);
				break;
			}
			if (next == null) return $"error: no submenu '{wanted}'";
			level = next;
		}

		var names = new List<string>();
		// A greyed-out row is left out: the game would not take it.
		for (var i = 0; i < (level?.Count ?? 0) && i < 200; i++)
		{
			if (Buttons.ChoiceDisabled(level[i])) continue;
			var text = Report.Escape(Buttons.ChoiceText(level[i]) ?? "");
			// With ?ids=1, each row's own id and the name the game registers it under ("Agility" for
			// 'pf20'): the same in every language, where the text is not.
			if (q["ids"] == "1")
			{
				var (id, name) = RowId(level[i]);
				var (value, _) = RowId(level[i], "PropertyValue");
				names.Add($"{{\"Text\": \"{text}\", \"Id\": \"{Code(id)}\", \"Name\": \"{Report.Escape(name ?? "")}\", \"Value\": \"{Code(value)}\"}}");
			}
			else names.Add($"\"{text}\"");
		}
		return "[" + string.Join(", ", names) + "]\n";
	}

	/// <summary>
	/// The whole menu at once, one row a line: its path of indices (what /select?path= takes), the
	/// text as shown, whether it opens a submenu, and the game's database record it stands for
	/// (DatabaseReferenceField: its top byte says the kind, a nation or a continent), which no
	/// language changes. A scouting focus's area list is told apart by those records alone.
	/// </summary>
	private static string Tree(SI.Bindable.SIDropdown dropdown)
	{
		var rows = new List<string>();
		void Walk(Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue> level, string at, int depth)
		{
			for (var i = 0; i < (level?.Count ?? 0) && rows.Count < 20000; i++)
			{
				var path = at.Length == 0 ? i.ToString() : $"{at},{i}";
				var children = Buttons.Children(level[i]);
				var text = Report.Escape((Buttons.ChoiceShown(level[i]) ?? "").Replace(" ›", ""));
				rows.Add($"{{\"Path\": \"{path}\", \"Text\": \"{text}\", \"Folder\": {(children != null ? "true" : "false")}, \"Record\": \"{RecordOf(level[i])}\"}}");
				if (children != null && depth < 6) Walk(children, path, depth + 1);
			}
		}
		Walk(dropdown.m_choices, "", 0);
		return "[\n  " + string.Join(",\n  ", rows) + "\n]\n";
	}

	/// <summary>A menu row's database record (its PropertyValue's DatabaseReferenceField), or "".</summary>
	private static string RecordOf(SI.Core.TypedValue row)
	{
		try
		{
			var record = row?.Get()?.TryCast<DynamicReference>();
			if (record == null) return "";
			var entries = Send.Entries(record);
			if (entries.TryGetValue("RowData", out var inner) && inner?.Get()?.TryCast<DynamicReference>() is { } data) entries = Send.Entries(data);
			if (!entries.TryGetValue("PropertyValue", out var value) || value?.Get()?.TryCast<DynamicReference>() is not { } held) return "";
			return Send.Entries(held).TryGetValue("DatabaseReferenceField", out var id) && id != null && !id.IsNull ? Text.Of(id) : "";
		}
		catch { return ""; }
	}

	/// <summary>A four-character id as its characters ("pf20"), or the number when they are not letters.</summary>
	internal static string Code(uint id)
	{
		if (id == 0) return "";
		var chars = new[] { (char)(id >> 24), (char)((id >> 16) & 0xFF), (char)((id >> 8) & 0xFF), (char)(id & 0xFF) };
		return chars.All(c => c is >= ' ' and <= '~' and not '"' and not '\\') ? new string(chars) : id.ToString();
	}

	/// <summary>
	/// A menu row's id ('pf20') and its registered name, through the row's RowData when it has one; with
	/// a field, the id of the record held there (an attribute's own, 'Pagi', in its PropertyValue).
	/// </summary>
	internal static (uint Id, string Name) RowId(SI.Core.TypedValue row, string field = null)
	{
		try
		{
			var record = row?.Get()?.TryCast<DynamicReference>();
			if (record == null) return (0, null);
			var entries = Send.Entries(record);
			if (entries.TryGetValue("RowData", out var inner) && inner != null && !inner.IsNull) return RowId(inner, field);
			if (field != null)
				return entries.TryGetValue(field, out var held) && held != null && !held.IsNull ? RowId(held) : (0, null);
			if (!entries.TryGetValue("ID", out var value) || value == null || value.IsNull) return (0, null);
			if (!uint.TryParse(Text.Of(value), out var id)) return (0, null);
			return (id, Schema.PlainNameOf(new SI.Bindable.Reference.Core.PropertyID(id)));
		}
		catch { return (0, null); }
	}

	/// <summary>The record behind a menu row reached by a path of indices, entry by entry, one level down.</summary>
	public static string Dump(NameValueCollection q)
	{
		if (!Buttons.TryPointer(q["ptr"] ?? "", out var pointer)) return "error: ?ptr=<0x…> required";
		var dropdown = Buttons.AllDropdowns().FirstOrDefault(d => d.Pointer == pointer);
		if (dropdown == null) return $"error: no dropdown at {q["ptr"]}";
		var rows = dropdown.m_choices;
		SI.Core.TypedValue chosen = null;
		foreach (var part in (q["path"] ?? "0").Split(','))
		{
			if (!int.TryParse(part, out var i) || rows == null || i < 0 || i >= rows.Count) return $"error: bad path at {part}";
			chosen = rows[i];
			rows = Buttons.Children(chosen);
		}
		var sb = new StringBuilder();
		DumpRecord(chosen, "", 0, sb);
		return sb.ToString();
	}

	/// <summary>
	/// Every row of a dropdown, submenus included, by what the game says it is rather than what it
	/// reads: the row's own kind (<c>RowData.Type</c>; a match player's menu numbers them as
	/// <c>FM.UI.PlayerMenuOption</c>: 1 his name, 7 a substitute for him, a folder 0 with the kind it
	/// holds as its value), its value, whether it is greyed out, and its text as shown.
	/// A row standing for a person is put at ?put=&lt;prefix&gt;.&lt;n&gt; ("At") so his fields can be read.
	/// A menu FM27 fills only when opened (<c>ShouldLazyBindChoices</c>, the match screen's player
	/// menus) is opened with ?load=1, and again with ?load=1&amp;fresh=1 since it keeps what it was first
	/// given (a substitute in, the old name still there). It fills on a later frame, with a new list
	/// ("List"), and ?close=1 shuts it then.
	/// </summary>
	public static string Kinds(NameValueCollection q)
	{
		if (!Buttons.TryPointer(q["ptr"] ?? "", out var pointer)) return "error: ?ptr=<0x…> required";
		var dropdown = Buttons.AllDropdowns().FirstOrDefault(d => d.Pointer == pointer);
		if (dropdown == null) return $"error: no dropdown at {q["ptr"]}";

		if (q["load"] == "1" && (q["fresh"] == "1" || (dropdown.m_choices?.Count ?? 0) == 0)) dropdown.ShowDropdown();
		if (q["close"] == "1") dropdown.CloseSelfAndAllChildren();

		var rows = new List<string>();
		var people = 0;
		void Add(SI.Core.TypedValue choice, string path)
		{
			var entries = choice?.Get()?.TryCast<DynamicReference>() is { } record ? Send.Entries(record) : null;
			Dictionary<string, SI.Core.TypedValue> data = null;
			if (entries != null && entries.TryGetValue("RowData", out var row) && row?.Get()?.TryCast<DynamicReference>() is { } inner)
				data = Send.Entries(inner);
			string Field(Dictionary<string, SI.Core.TypedValue> from, string name) =>
				from != null && from.TryGetValue(name, out var v) && v != null && !v.IsNull ? Text.Of(v) : "";

			var value = data != null && data.TryGetValue("PropertyValue", out var held) && held != null && !held.IsNull ? held : null;
			var at = "";
			if (value?.DataType?.Name == "PersonReference" && !string.IsNullOrEmpty(q["put"]))
			{
				var to = $"{q["put"]}.{people++}";
				if (RawPath.Set(to, value) == null) at = to;
			}
			var disabled = Field(entries, "Disabled") == "True" || Field(data, "Disabled") == "True";
			var children = Buttons.Children(choice);
			// The text alone: whether it opens a submenu is in the paths under it.
			var shown = Buttons.ChoiceShown(choice) ?? "";
			if (children != null && shown.EndsWith(" ›", System.StringComparison.Ordinal)) shown = shown[..^2];
			rows.Add($"{{\"Path\": \"{path}\", \"Type\": \"{Report.Escape(Field(data, "Type"))}\", "
			         + $"\"Value\": \"{Report.Escape(value?.DataType?.Name == "PersonReference" ? "" : Field(data, "PropertyValue"))}\", "
			         + $"\"Disabled\": {(disabled ? "true" : "false")}, \"At\": \"{at}\", "
			         + $"\"Shown\": \"{Report.Escape(shown)}\"}}");

			for (var c = 0; c < (children?.Count ?? 0) && c < 60; c++) Add(children[c], $"{path},{c}");
		}

		var top = dropdown.m_choices;
		for (var i = 0; i < (top?.Count ?? 0) && i < 60; i++) Add(top[i], i.ToString());
		// Each filling hands the menu a new list: a list other than the one read before says it was filled again.
		var list = top == null ? "" : $"0x{top.Pointer.ToInt64():x}";
		return $"{{\"List\": \"{list}\", \"Rows\": [\n  " + string.Join(",\n  ", rows) + "\n]}\n";
	}

	/// <summary>
	/// Puts a dropdown's row at a path (?ptr=&amp;i=&amp;to=), so a row that is only a reference — a
	/// list of people with no text, as who can take a duty — can be read by /pull.
	/// </summary>
	public static string Put(NameValueCollection q)
	{
		if (!Buttons.TryPointer(q["ptr"] ?? "", out var pointer)) return "error: ?ptr=<0x…> required";
		if (string.IsNullOrEmpty(q["to"])) return "error: ?to=<path> required";
		var dropdown = Buttons.AllDropdowns().FirstOrDefault(d => d.Pointer == pointer);
		if (dropdown == null) return $"error: no dropdown at {q["ptr"]}";
		var rows = dropdown.m_choices;
		if (!int.TryParse(q["i"], out var i) || rows == null || i < 0 || i >= rows.Count) return $"error: ?i= must be 0..{(rows?.Count ?? 0) - 1}";
		return RawPath.Set(q["to"], rows[i]) ?? $"{q["to"]} = {FmApi.Describe(rows[i])}";
	}

	private static void DumpRecord(SI.Core.TypedValue value, string indent, int depth, StringBuilder sb)
	{
		var record = value?.Get()?.TryCast<DynamicReference>();
		if (record == null) { sb.AppendLine($"{indent}{FmApi.Describe(value)}"); return; }
		var keys = new Il2CppSystem.Collections.Generic.List<SI.Bindable.Reference.Core.PropertyID>();
		record.GetProperties(keys);
		foreach (var key in keys)
		{
			SI.Core.TypedValue entry = null;
			try { entry = record[key.ID]; } catch { }
			var name = Schema.NameOf(key);
			sb.AppendLine($"{indent}{key.ID,12}  {name,-32} {FmApi.Describe(entry)}");
			if (depth < 2 && name != "ChildList" && entry?.Get()?.TryCast<DynamicReference>() != null) DumpRecord(entry, indent + "    ", depth + 1, sb);
		}
	}

	/// <summary>
	/// Which menus fill a submenu only when it is asked for (FM27): per dropdown, whether it binds its
	/// choices lazily and the rows the game says have children while their child list is still empty.
	/// </summary>
	public static string Lazy()
	{
		var sb = new StringBuilder();
		foreach (var dropdown in Buttons.AllDropdowns())
		{
			try
			{
				var choices = dropdown.m_choices;
				var count = choices?.Count ?? 0;
				var waiting = new List<string>();
				for (var i = 0; i < count; i++)
				{
					var record = choices[i]?.Get()?.TryCast<DynamicReference>();
					if (record == null || !SIDropdown.HasLazyChildren(record) || Buttons.Children(choices[i]) != null) continue;
					waiting.Add(Buttons.ChoiceText(choices[i]) ?? "?");
				}
				var children = dropdown.m_childrenDropdowns?.Count ?? 0;
				if (!dropdown.ShouldLazyBindChoices && waiting.Count == 0 && children == 0) continue;
				sb.AppendLine($"0x{dropdown.Pointer.ToInt64():x}  {dropdown.name}  rows={count} lazyBind={dropdown.ShouldLazyBindChoices} requested={dropdown.m_lazyChildrenRequested} childDropdowns={children}"
				              + (waiting.Count > 0 ? $"  waiting: {string.Join(" | ", waiting.Take(8))}" : ""));
			}
			catch (System.Exception e) { sb.AppendLine($"? {e.GetType().Name}"); }
		}
		return sb.Length > 0 ? sb.ToString() : "no lazy menus\n";
	}

	/// <summary>Every dropdown row laid out now, with its child dropdown when it has one.</summary>
	public static string Rows()
	{
		var sb = new StringBuilder();
		foreach (var element in Buttons.Elements())
		{
			DropdownRow row;
			try { row = element.TryCast<DropdownRow>(); } catch { continue; }
			if (row == null) continue;

			string label, shown, child = "";
			try { label = Buttons.LabelOf(row); } catch { label = "?"; }
			try { shown = row.panel != null && row.worldBound.width > 0 ? "" : " HIDDEN"; } catch { shown = " ?"; }
			// FM27 dropped DropdownRow.Index: the row's place among its siblings.
			int index;
			try { index = row.hierarchy.parent?.IndexOf(row) ?? -1; } catch { index = -1; }
			sb.AppendLine($"0x{row.Pointer.ToInt64():x}  #{index}  \"{label}\"{shown}{child}");
		}
		return sb.Length > 0 ? sb.ToString() : "no dropdown rows laid out\n";
	}
}
