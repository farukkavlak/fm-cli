using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// A dropdown's submenus, as the game builds them: opening the list lays out a row per choice
/// (<c>DropdownRowNavigatable</c>), and a row with a submenu carries a child dropdown of its own
/// whose choices are the submenu (FINDINGS §8.45). This opens a list and reports its rows.
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
	/// listener wants a real pointer, FINDINGS §8.43). how=click goes through the shared
	/// click-or-submit handler instead.
	/// </summary>
	public static string Submit(NameValueCollection q)
	{
		if (!Buttons.TryPointer(q["ptr"] ?? "", out var pointer)) return "error: ?ptr=<0x…> required";
		foreach (var element in Buttons.Elements())
		{
			if (element.Pointer != pointer) continue;
			var row = element.TryCast<DropdownRowNavigatable>();
			if (row == null) return "error: not a dropdown row";
			var evt = NavigationEventBase<NavigationSubmitEvent>.GetPooled(UnityEngine.EventModifiers.None);
			if (q["how"] == "click") row.OnClickedOrSubmitted(evt);
			else row.OnNavigationSubmit(evt);
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
			if (!Buttons.ChoiceDisabled(level[i])) names.Add($"\"{Report.Escape(Buttons.ChoiceText(level[i]) ?? "")}\"");
		return "[" + string.Join(", ", names) + "]\n";
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

	/// <summary>Every dropdown row laid out now, with its child dropdown when it has one.</summary>
	public static string Rows()
	{
		var sb = new StringBuilder();
		foreach (var element in Buttons.Elements())
		{
			DropdownRowNavigatable row;
			try { row = element.TryCast<DropdownRowNavigatable>(); } catch { continue; }
			if (row == null) continue;

			string label, shown, child = "";
			try { label = Buttons.LabelOf(row); } catch { label = "?"; }
			try { shown = row.panel != null && row.worldBound.width > 0 ? "" : " HIDDEN"; } catch { shown = " ?"; }
			try
			{
				var inner = row.m_childDropdown;
				if (inner != null)
				{
					var names = new List<string>();
					var choices = inner.m_choices;
					for (var i = 0; i < (choices?.Count ?? 0) && i < 40; i++) names.Add(Buttons.ChoiceText(choices[i]) ?? "");
					child = $"  child=0x{inner.Pointer.ToInt64():x} [{string.Join(" · ", names)}]";
				}
			}
			catch { }
			int index;
			try { index = row.Index; } catch { index = -1; }
			sb.AppendLine($"0x{row.Pointer.ToInt64():x}  #{index}  \"{label}\"{shown}{child}");
		}
		return sb.Length > 0 ? sb.ToString() : "no dropdown rows laid out\n";
	}
}
