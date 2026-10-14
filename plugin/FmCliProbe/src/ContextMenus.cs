using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using FM.UI;
using SI.UI;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// The "Actions" menu of a table whose rows are ticked, and the menu a right click raises: both
/// are a <c>PluginContextMenuBindings</c> element that asks the game what can be done about what
/// it is bound to, and shows the answer as a cascading menu. The menu on screen is the context
/// menu subsystem's <c>MenuDescription</c>: entries with a name, a kind (item or folder), flags
/// and the action a click runs. This opens one, lists what it holds and runs an entry.
/// </summary>
internal static class ContextMenus
{
	private static ContextMenuSubsystem Subsystem()
	{
		var all = SI.Core.GameSubsystems.s_subsystems;
		if (all == null) return null;
		foreach (var s in all)
		{
			if (s == null) continue;
			var typed = new Il2CppSystem.Object(s.Pointer).TryCast<ContextMenuSubsystem>();
			if (typed != null) return typed;
		}
		return null;
	}

	public static string Handle(NameValueCollection q)
	{
		var subsystem = Subsystem();
		if (subsystem == null) return "error: no context menu subsystem";

		if (q["close"] == "1")
		{
			subsystem.CloseContextMenu();
			return "closed";
		}
		if (q["open"] == "1") return Open(q);

		MenuDescription description = null;
		try { description = subsystem.m_popupContext?.Description; } catch { }

		if (!string.IsNullOrEmpty(q["choose"]))
		{
			if (description == null) return "error: no menu is open";
			var entry = description.m_root;
			foreach (var part in q["choose"].Split('>', StringSplitOptions.RemoveEmptyEntries))
			{
				var wanted = part.Trim();
				MenuDescription.Entry next = null;
				for (var child = entry?.FirstChild; child != null; child = child.Next)
					if (string.Equals(Text.Plain(child.Name ?? ""), wanted, StringComparison.OrdinalIgnoreCase)) { next = child; break; }
				if (next == null) return $"error: the menu has no '{wanted}'";
				entry = next;
			}
			if (entry.Kind != MenuDescription.EntryKind.Item) return $"error: '{q["choose"]}' opens a further menu";
			if ((entry.Flags & MenuDescription.EntryFlags.Enabled) == 0) return $"error: '{q["choose"]}' is disabled";
			var action = entry.HandleInteraction;
			if (action == null) return $"error: '{q["choose"]}' carries no action";
			if (q["dry"] == "1") return $"dry run, NOT chosen: {q["choose"]}";
			action.Invoke();
			if (q["keep"] != "1") subsystem.CloseContextMenu();
			return $"chose {q["choose"]}";
		}

		var rows = new List<string>();
		if (description != null) List(description.m_root, "", rows, 0);
		return "[" + string.Join(", ", rows) + "]\n";
	}

	private static void List(MenuDescription.Entry parent, string path, List<string> rows, int depth)
	{
		if (parent == null || depth > 4) return;
		for (var child = parent.FirstChild; child != null && rows.Count < 400; child = child.Next)
		{
			var flags = child.Flags;
			if ((flags & MenuDescription.EntryFlags.Visible) == 0) continue;
			var name = Text.Plain(child.Name ?? "");
			var at = path.Length > 0 ? $"{path}>{name}" : name;
			var folder = child.Kind == MenuDescription.EntryKind.Folder;
			// The entry's icon names what it does whatever language its words are in.
			var icon = "";
			try { icon = child.Icon?.StyleOrPath ?? ""; } catch { }
			rows.Add("{"
				+ $"\"Path\": \"{Report.Escape(at)}\", \"Name\": \"{Report.Escape(name)}\", \"Icon\": \"{Report.Escape(icon)}\", "
				+ $"\"Folder\": {(folder ? "true" : "false")}, "
				+ $"\"Enabled\": {((flags & MenuDescription.EntryFlags.Enabled) != 0 ? "true" : "false")}, "
				+ $"\"Selected\": {((flags & MenuDescription.EntryFlags.Selected) != 0 ? "true" : "false")}}}");
			if (folder) List(child, at, rows, depth + 1);
		}
	}

	/// <summary>
	/// Opens a menu the way the keyboard does: a submit event handed to its own manipulator. Which
	/// one is said by ptr, by name (a table's "ActionsDropdown", there once a row is ticked) or by
	/// text — the menu a right click on those words would raise, a person's name in a row. The
	/// game fills the menu a moment later, so it is listed with a call of its own.
	/// </summary>
	private static string Open(NameValueCollection q)
	{
		PluginContextMenuBindings menu = null;
		if (!string.IsNullOrEmpty(q["ptr"]))
		{
			if (!Buttons.TryPointer(q["ptr"], out var pointer)) return "error: ?ptr=<0x…> is not a pointer";
			foreach (var element in Buttons.Elements())
			{
				if (element.Pointer != pointer) continue;
				try { menu = element.TryCast<PluginContextMenuBindings>(); } catch { }
				if (menu == null) return "error: that element raises no menu";
				break;
			}
			if (menu == null) return $"error: no element at {q["ptr"]}";
		}
		else if (!string.IsNullOrEmpty(q["text"]))
		{
			var wanted = q["text"];
			foreach (var element in Buttons.Elements())
			{
				try
				{
					if (element.panel == null || element.worldBound.width <= 0) continue;
					if (!string.Equals(Text.Plain(element.TryCast<TextElement>()?.text ?? ""), wanted, StringComparison.OrdinalIgnoreCase)) continue;
					for (var e = element; e != null && menu == null; e = e.hierarchy.parent) menu = e.TryCast<PluginContextMenuBindings>();
				}
				catch { continue; }
				if (menu != null) break;
			}
			if (menu == null) return $"error: nothing showing '{wanted}' raises a menu";
		}
		else if (!string.IsNullOrEmpty(q["name"]))
		{
			foreach (var element in Buttons.Elements())
			{
				try
				{
					if (element.name != q["name"] || element.panel == null || element.worldBound.width <= 0) continue;
					menu = element.TryCast<PluginContextMenuBindings>();
				}
				catch { continue; }
				if (menu != null) break;
			}
			if (menu == null) return $"error: no menu named '{q["name"]}' is showing";
		}
		else return "error: ?ptr=<0x…>, ?text=<shown text> or ?name=<element name> required";

		var manipulator = menu.m_manipulator;
		if (manipulator == null) return "error: that menu has nothing to open it";
		if (q["dry"] == "1") return $"dry run, NOT opened: 0x{menu.Pointer.ToInt64():x} name={menu.name}";

		var evt = NavigationEventBase<NavigationSubmitEvent>.GetPooled(UnityEngine.EventModifiers.None);
		evt.elementTarget = menu;
		if (q["how"] == "submit") ContextMenuDescriptionManipulator.OnSubmit(evt, manipulator);
		else manipulator.OpenOnShortcutEvent(evt);
		return $"opened 0x{menu.Pointer.ToInt64():x} name={menu.name}";
	}
}
