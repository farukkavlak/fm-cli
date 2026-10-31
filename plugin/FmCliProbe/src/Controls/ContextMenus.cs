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
		if (q["menus"] == "1") return Menus();

		MenuDescription description = null;
		try { description = subsystem.m_popupContext?.Description; } catch { }

		// An entry is chosen by the game action it runs (?action=PersonMakeOfferForPlayer), the same in
		// every language, or by its path of shown words.
		if (!string.IsNullOrEmpty(q["action"]))
		{
			if (description == null) return "error: no menu is open";
			// With ?name= the entry of that action with that name (a trait under PersonTrainingNewPreferredMove),
			// as the plugin gives it or as the game shows it. With ?who= and ?data=<property>:<value>,… the one
			// handing its action that record and those numbers (a scout and a length of time), as listed.
			var wanted = new List<(string Key, string Value)>();
			foreach (var pair in (q["data"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
				if (pair.Split(':') is { Length: 2 } kv) wanted.Add((kv[0], kv[1]));
			var entry = Find(description.m_root, q["action"], 0, q["name"], q["who"], wanted);
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
		if (description != null) List(description.m_root, "", "", rows, 0);
		return "[" + string.Join(", ", rows) + "]\n";
	}

	/// <remarks>`Path` and `Name` are put into English for matching; `ShownPath` and `Shown` are the words as
	/// the menu shows them, for showing: one English word can stand for several of another language's.</remarks>
	private static void List(MenuDescription.Entry parent, string path, string shownPath, List<string> rows, int depth)
	{
		if (parent == null || depth > 4) return;
		for (var child = parent.FirstChild; child != null && rows.Count < 400; child = child.Next)
		{
			var flags = child.Flags;
			if ((flags & MenuDescription.EntryFlags.Visible) == 0) continue;
			var name = Text.Plain(child.Name ?? "");
			var at = path.Length > 0 ? $"{path}>{name}" : name;
			var shown = Text.Shown(child.Name ?? "");
			var shownAt = shownPath.Length > 0 ? $"{shownPath}>{shown}" : shown;
			var folder = child.Kind == MenuDescription.EntryKind.Folder;
			// The entry's icon names what it does whatever language its words are in.
			var icon = "";
			try { icon = child.Icon?.StyleOrPath ?? ""; } catch { }
			rows.Add("{"
				+ $"\"Path\": \"{Report.Escape(at)}\", \"Name\": \"{Report.Escape(name)}\", "
				+ $"\"ShownPath\": \"{Report.Escape(shownAt)}\", \"Shown\": \"{Report.Escape(shown)}\", \"Icon\": \"{Report.Escape(icon)}\", "
				+ $"\"Action\": \"{Report.Escape(ActionOf(child) ?? "")}\", "
				+ DataOf(child)
				+ $"\"Folder\": {(folder ? "true" : "false")}, "
				+ $"\"Enabled\": {((flags & MenuDescription.EntryFlags.Enabled) != 0 ? "true" : "false")}, "
				+ $"\"Selected\": {((flags & MenuDescription.EntryFlags.Selected) != 0 ? "true" : "false")}}}");
			if (folder) List(child, at, shownAt, rows, depth + 1);
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

	private static MenuDescription.Entry Find(MenuDescription.Entry parent, string action, int depth, string name = null,
		string who = null, List<(string Key, string Value)> data = null)
	{
		if (parent == null || depth > 4) return null;
		for (var child = parent.FirstChild; child != null; child = child.Next)
		{
			if ((child.Flags & MenuDescription.EntryFlags.Visible) == 0) continue;
			if (child.Kind == MenuDescription.EntryKind.Item && ActionOf(child) == action
			    && (string.IsNullOrEmpty(name) || Named(child, name))
			    && ((string.IsNullOrEmpty(who) && (data == null || data.Count == 0)) || Hands(child, who, data))) return child;
			if (child.Kind == MenuDescription.EntryKind.Folder && Find(child, action, depth + 1, name, who, data) is { } found) return found;
		}
		return null;
	}

	/// <summary>Whether the entry hands its action that record and those numbers, as <see cref="DataOf"/> lists them.</summary>
	private static bool Hands(MenuDescription.Entry entry, string who, List<(string Key, string Value)> data)
	{
		var listed = DataOf(entry);
		if (!string.IsNullOrEmpty(who) && !listed.Contains($"\"Who\": \"{Report.Escape(who)}\"", StringComparison.Ordinal)) return false;
		return data == null || data.All(d => listed.Contains($"\"{d.Key}\": \"{Report.Escape(d.Value)}\"", StringComparison.Ordinal));
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
	private static string ActionOf(MenuDescription.Entry entry) =>
		Closure(entry, typeof(FM.GamePlugin.ActionID))?.ToString();

	/// <summary>
	/// What the entry hands its action: the record it names (`Who`: the scout an assignment goes to)
	/// and its numbers and flags by property (`Data`). Among entries of one action, a length of time
	/// or a kind of interest is the number that differs, where their words differ by language.
	/// </summary>
	private static string DataOf(MenuDescription.Entry entry)
	{
		var who = "";
		var fields = new List<string>();
		try
		{
			if (Closure(entry, typeof(SI.Core.TypedValue)) is SI.Core.TypedValue value && !value.IsNull
			    && value.Get()?.TryCast<SI.Bindable.DynamicReference>() is { } record)
			{
				var keys = new Il2CppSystem.Collections.Generic.List<SI.Bindable.Reference.Core.PropertyID>();
				record.GetProperties(keys);
				foreach (var key in keys)
				{
					SI.Core.TypedValue v = null;
					try { v = record[key.ID]; } catch { }
					if (v == null || v.IsNull) continue;
					var name = Schema.NameOf(key);
					var inner = v.Get()?.TryCast<SI.Bindable.DynamicReference>();
					if (name == "DatabaseReferenceField" && inner != null)
					{
						try { who = Text.Of(inner[key.ID]); } catch { }
						continue;
					}
					if (inner != null || v.DataType?.Name is not ("DynamicNumber" or "Boolean")) continue;
					fields.Add($"\"{key.ID}\": \"{Report.Escape(Text.Of(v))}\"");
				}
			}
		}
		catch { }
		return $"\"Who\": \"{Report.Escape(who)}\", \"Data\": {{{string.Join(", ", fields)}}}, ";
	}

	/// <summary>The value of the given type the entry's closure holds, found by its type, not by the closure's generated name.</summary>
	private static object Closure(MenuDescription.Entry entry, Type type)
	{
		try
		{
			var target = entry.HandleInteraction?.Target;
			if (target == null) return null;
			var name = target.GetIl2CppType().FullName ?? "";
			var shape = Buttons.ShapeOf(name.Replace("<>", "__"));
			if (shape.Type == null) shape = Buttons.ShapeOf(name);
			var property = shape.Type?.GetProperties().FirstOrDefault(p => p.PropertyType == type);
			return property?.GetValue(shape.Wrap(target.Pointer));
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
		else if (!string.IsNullOrEmpty(q["path"]))
		{
			// The menu bound to that record or to something beneath it: a search row's menu holds the
			// row's person, where a name on screen can be someone else's of the same name.
			var node = FmApi.NodeAt(q["path"], out var error);
			if (node == null) return error;
			var keys = new HashSet<ulong>();
			var queue = new Queue<SI.Bindable.IReadOnlyNode>();
			queue.Enqueue(node);
			while (queue.Count > 0 && keys.Count < 600)
			{
				var current = queue.Dequeue();
				keys.Add(current.Key.m_key);
				for (var c = current.FirstChild; c != null; c = c.NextSibling) queue.Enqueue(c);
			}
			var fitting = new List<PluginContextMenuBindings>();
			foreach (var element in Buttons.Elements())
			{
				try
				{
					if (element.panel == null || element.worldBound.width <= 0) continue;
					if (element.TryCast<PluginContextMenuBindings>() is not { } bound || bound.m_manipulator == null) continue;
					if (KeysOf(bound).Any(keys.Contains)) fitting.Add(bound);
				}
				catch { }
			}
			if (fitting.Count == 0) return $"error: no menu showing is bound to '{q["path"]}'";
			// Several over one row (its name and its picture) raise the same menu; menus of other records are not among them.
			menu = fitting[0];
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
		else return "error: ?ptr=<0x…>, ?path=<record path>, ?text=<shown text> or ?name=<element name> required";

		var manipulator = menu.m_manipulator;
		if (manipulator == null) return "error: that menu has nothing to open it";
		if (q["dry"] == "1") return $"dry run, NOT opened: 0x{menu.Pointer.ToInt64():x} name={menu.name}";

		var evt = NavigationEventBase<NavigationSubmitEvent>.GetPooled(UnityEngine.EventModifiers.None);
		evt.elementTarget = menu;
		if (q["how"] == "submit") ContextMenuDescriptionManipulator.OnSubmit(evt, manipulator);
		else manipulator.OpenOnShortcutEvent(evt);
		return $"opened 0x{menu.Pointer.ToInt64():x} name={menu.name}";
	}

	private static IEnumerable<ulong> KeysOf(PluginContextMenuBindings menu)
	{
		var keys = menu.m_bindingKeys;
		if (keys == null) yield break;
		for (var i = 0; i < keys.Count; i++) yield return keys[i].m_key;
	}

	/// <summary>
	/// Every menu showing, with the paths and keys it is bound to, and "At": the full paths of the
	/// records those keys are, so whose menu it is can be read from the record (a person page's
	/// actions menu, bound to the page's person).
	/// </summary>
	private static string Menus()
	{
		var found = new List<(PluginContextMenuBindings Menu, List<string> Paths, List<ulong> Keys)>();
		foreach (var element in Buttons.Elements())
		{
			try
			{
				if (element.panel == null || element.worldBound.width <= 0) continue;
				if (element.TryCast<PluginContextMenuBindings>() is not { } bound) continue;
				var paths = new List<string>();
				var bp = bound.BindingPaths;
				for (var i = 0; bp != null && i < bp.Count; i++) paths.Add(bp[i].Path ?? "");
				found.Add((bound, paths, KeysOf(bound).ToList()));
			}
			catch { }
		}
		var wanted = new HashSet<ulong>(found.SelectMany(f => f.Keys));
		var at = new Dictionary<ulong, string>();
		if (wanted.Count > 0)
			foreach (var (path, node) in Report.Walk())
			{
				var key = node.Key.m_key;
				if (wanted.Contains(key) && !at.ContainsKey(key)) at[key] = path;
				if (at.Count == wanted.Count) break;
			}
		var rows = found.Select(f =>
			$"{{\"Ptr\": \"0x{f.Menu.Pointer.ToInt64():x}\", \"Name\": \"{Report.Escape(f.Menu.name ?? "")}\", "
			+ $"\"Paths\": \"{Report.Escape(string.Join(" | ", f.Paths))}\", \"Keys\": \"{string.Join(",", f.Keys)}\", "
			+ $"\"At\": \"{Report.Escape(string.Join(" | ", f.Keys.Where(at.ContainsKey).Select(k => at[k])))}\"}}");
		return "[" + string.Join(", ", rows) + "]\n";
	}
}
