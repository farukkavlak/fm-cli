using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// The confirmation dialogs on screen: "Proceed to match?" and its kind.
///
/// A dialog stops whatever opened it until it is answered, and nothing else says it is there:
/// the game's state does not change and the button that raised it stays where it was. It is
/// recognised by the fixed names of its title and description, and its buttons are whatever
/// buttons share the nearest ancestor that has any. They are clicked rather than queued: a
/// dialog's button is wired in code and carries no event list.
/// </summary>
internal static class Dialogs
{
	/// <summary>
	/// What the dialog on screen is made of, as the game holds it: its type and, for each of its
	/// buttons, the event it stands for, whom it goes to and what it carries.
	/// </summary>
	private static string Data()
	{
		var manager = SI.Bindable.DialogManager.Instance;
		if (manager == null) return "error: no dialog manager";
		var data = manager.m_currentDialog?.Value;
		if (data is null) return "error: no current dialog";
		var lines = new List<string> { $"Type={data.Type}  Title={Text.Plain(data.Title ?? "")}  visible={manager.m_isDialogVisible}  queued={manager.m_dialogs?.Count}" };
		foreach (var (slot, button) in new[] { ("primary", data.PrimaryButton), ("secondary", data.SecondaryButton), ("tertiary", data.TertiaryButton), ("quaternary", data.QuaternaryButton) })
		{
			if (button is null) continue;
			string Say(SI.Core.TypedValue value) { try { return value == null ? "null" : FmApi.Describe(value); } catch (System.Exception e) { return $"<{e.GetType().Name}>"; } }
			lines.Add($"{slot}: Text={Text.Plain(button.Text ?? "")}  EventID={button.EventID}  Visible={button.IsVisible}  Callback={(button.Callback != null ? "yes" : "no")}"
				+ $"  DontClose={button.DontCloseOnClick}  Target={Say(button.Target)}  Payload={Say(button.Payload)}");
		}
		var handler = FM.UI.FMDialogHandler.Instance;
		if (handler?.m_eventToPluginHandlerList is { } plugin)
			foreach (var entry in plugin)
				if (entry != null && entry.Type == data.Type) lines.Add($"to plugin: {entry.Type} -> {entry.EventID}");
		if (handler?.m_dialogActionHandlerList is { } actions)
			foreach (var entry in actions)
				if (entry != null && entry.Type == data.Type) lines.Add($"action: {entry.Type} -> {entry.ActionID}");
		return string.Join("\n", lines) + "\n";
	}

	/// <summary>
	/// Answers the dialog the way the game's own handler does when its button is pressed. A
	/// simulated click on the button closes some dialogs without their answer ever being sent.
	/// </summary>
	private static string Answer(string slot)
	{
		var handler = FM.UI.FMDialogHandler.Instance;
		if (handler == null) return "error: no dialog handler";
		if (SI.Bindable.DialogManager.Instance?.m_isDialogVisible != true) return "error: no dialog is showing";
		switch (slot)
		{
			case "primary": handler.PressPrimaryButton(null); break;
			case "secondary": handler.PressSecondaryButton(null); break;
			case "tertiary": handler.PressTertiaryButton(null); break;
			case "quaternary": handler.PressQuaternaryButton(null); break;
			default: return "error: ?press=primary|secondary|tertiary|quaternary";
		}
		return $"pressed {slot}";
	}

	/// <summary>
	/// The dialog the game is showing, as its manager holds it: its type, a name that does not
	/// change with the language, and each button's slot and event, found by the text it shows.
	/// </summary>
	private static (string Type, Dictionary<string, (string Slot, string Event)> Buttons)? Current()
	{
		try
		{
			var manager = SI.Bindable.DialogManager.Instance;
			if (manager == null || manager.m_isDialogVisible != true) return null;
			var data = manager.m_currentDialog?.Value;
			if (data is null) return null;
			var buttons = new Dictionary<string, (string, string)>();
			foreach (var (slot, button) in new[] { ("primary", data.PrimaryButton), ("secondary", data.SecondaryButton), ("tertiary", data.TertiaryButton), ("quaternary", data.QuaternaryButton) })
			{
				if (button is null) continue;
				var text = Text.Plain(button.Text ?? "");
				if (text.Length > 0) buttons.TryAdd(text, (slot, button.EventID ?? ""));
			}
			return (data.Type ?? "", buttons);
		}
		catch { return null; }
	}

	/// <summary>
	/// The name of the panel a window is drawn in ("InstantResultPanel", "TeamSelectionInfoDialog"):
	/// the windows the game opens as panels have no type at the dialog manager, and their panel's
	/// name is the same in every language. Empty when there is none.
	/// </summary>
	private static string PanelOf(VisualElement element)
	{
		try
		{
			for (var at = element.hierarchy.parent; at != null; at = at.hierarchy.parent)
				if (at.GetIl2CppType().Name == "Panel") return at.name ?? "";
		}
		catch { }
		return "";
	}

	public static string Open(System.Collections.Specialized.NameValueCollection q)
	{
		if (q["data"] == "1") return Data();
		if (!string.IsNullOrEmpty(q["press"])) return Answer(q["press"]);
		var rows = new List<string>();
		var current = Current();

		foreach (var element in Buttons.Elements())
		{
			string name;
			try { name = element.name; } catch { continue; }
			if (name != "dialog-title") continue;

			string title;
			try
			{
				title = element.TryCast<TextElement>()?.text;
				// A dismissed dialog can linger unattached; one that takes no room is not showing.
				if (element.panel == null || element.worldBound.width <= 0) continue;
			}
			catch { continue; }

			VisualElement root = element;
			List<(string Pointer, string Label, string Name, string Shown)> buttons = new();
			for (var climbed = 0; climbed < 8 && buttons.Count == 0; climbed++)
			{
				VisualElement parent;
				try { parent = root.hierarchy.parent; } catch { break; }
				if (parent == null) break;
				root = parent;
				buttons = Buttons.ButtonsUnder(root);
			}

			var description = "";
			try { description = root.Q<TextElement>("dialog-description")?.text ?? ""; } catch { }
			var panel = PanelOf(element);

			// The manager holds one dialog; it is this one when its buttons are these.
			var mine = current is { } c && buttons.Any(b => c.Buttons.ContainsKey(b.Label)) ? current : null;
			string Slot(string label) => mine is { } m && m.Buttons.TryGetValue(label, out var at)
				? $", \"Slot\": \"{at.Slot}\", \"EventID\": \"{Report.Escape(at.Event)}\"" : "";

			// Title, Description and Label are put into English for matching; the Shown ones are the
			// words as the window shows them, for showing.
			rows.Add("{"
				+ $"\"Title\": \"{Report.Escape(Text.Plain(title))}\", "
				+ $"\"ShownTitle\": \"{Report.Escape(Text.Shown(title))}\", "
				+ $"\"Description\": \"{Report.Escape(Text.Plain(description))}\", "
				+ $"\"ShownDescription\": \"{Report.Escape(Text.Shown(description))}\", "
				+ (mine is { } d ? $"\"Type\": \"{Report.Escape(d.Type)}\", " : "")
				+ (panel.Length > 0 ? $"\"Panel\": \"{Report.Escape(panel)}\", " : "")
				+ "\"Buttons\": [" + string.Join(", ", buttons.Select(b =>
					$"{{\"Label\": \"{Report.Escape(b.Label)}\", \"ShownLabel\": \"{Report.Escape(b.Shown)}\", \"Pointer\": \"{b.Pointer}\", \"Name\": \"{Report.Escape(b.Name)}\"{Slot(b.Label)}}}")) + "]}");
		}

		return "[" + string.Join(", ", rows) + "]\n";
	}
}
