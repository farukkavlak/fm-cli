using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
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

		// An entry is chosen by the game action it runs (?action=PersonMakeOfferForPlayer), the same in
		// every language, or by its path of shown words.
		if (!string.IsNullOrEmpty(q["action"]))
		{
			if (description == null) return "error: no menu is open";
			// With ?name= the entry of that action with that name (a trait under PersonTrainingNewPreferredMove),
			// as the plugin gives it or as the game shows it.
			var entry = Find(description.m_root, q["action"], 0, q["name"]);
			if (entry == null) return $"error: the menu has no entry for action '{q["action"]}'";
			if ((entry.Flags & MenuDescription.EntryFlags.Enabled) == 0) return $"error: '{q["action"]}' is disabled";
			if (q["dry"] == "1") return $"dry run, NOT chosen: {q["action"]}";
			entry.HandleInteraction.Invoke();
			if (q["keep"] != "1") subsystem.CloseContextMenu();
			return $"chose {q["action"]}";
		}

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
				+ $"\"Action\": \"{Report.Escape(ActionOf(child) ?? "")}\", "
				+ $"\"Folder\": {(folder ? "true" : "false")}, "
				+ $"\"Enabled\": {((flags & MenuDescription.EntryFlags.Enabled) != 0 ? "true" : "false")}, "
				+ $"\"Selected\": {((flags & MenuDescription.EntryFlags.Selected) != 0 ? "true" : "false")}}}");
			if (folder) List(child, at, rows, depth + 1);
		}
	}

	private static PluginContextMenuBindings Inside(VisualElement element)
	{
		for (var i = 0; i < element.hierarchy.childCount; i++)
		{
			var child = element.hierarchy[i];
			if (child.TryCast<PluginContextMenuBindings>() is { } menu) return menu;
			if (Inside(child) is { } deeper) return deeper;
		}
		return null;
	}

	private static MenuDescription.Entry Find(MenuDescription.Entry parent, string action, int depth, string name = null)
	{
		if (parent == null || depth > 4) return null;
		for (var child = parent.FirstChild; child != null; child = child.Next)
		{
			if ((child.Flags & MenuDescription.EntryFlags.Visible) == 0) continue;
			if (child.Kind == MenuDescription.EntryKind.Item && ActionOf(child) == action
			    && (string.IsNullOrEmpty(name) || Named(child, name))) return child;
			if (child.Kind == MenuDescription.EntryKind.Folder && Find(child, action, depth + 1, name) is { } found) return found;
		}
		return null;
	}

	private static bool Named(MenuDescription.Entry entry, string name)
	{
		var raw = entry.Name ?? "";
		return string.Equals(Text.Plain(raw), name, StringComparison.OrdinalIgnoreCase)
		       || string.Equals(Text.Shown(raw), name, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// The game action an entry runs (ActionID's name, "PersonMakeOfferForPlayer"), or null. The game's
	/// entries run a closure holding the action's id; it is found by its type, not by the closure's
	/// generated name, which a game update can renumber.
	/// </summary>
	private static string ActionOf(MenuDescription.Entry entry)
	{
		try
		{
			var target = entry.HandleInteraction?.Target;
			if (target == null) return null;
			var name = target.GetIl2CppType().FullName ?? "";
			var shape = Buttons.ShapeOf(name.Replace("<>", "__"));
			if (shape.Type == null) shape = Buttons.ShapeOf(name);
			var property = shape.Type?.GetProperties().FirstOrDefault(p => p.PropertyType == typeof(FM.GamePlugin.ActionID));
			return property?.GetValue(shape.Wrap(target.Pointer))?.ToString();
		}
		catch { return null; }
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
					// The element named, or the menu inside it (a page's "#FrameActions" holds its actions menu).
					menu = element.TryCast<PluginContextMenuBindings>() ?? Inside(element);
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
