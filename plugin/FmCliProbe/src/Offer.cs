using System.Collections.Generic;
using System.Linq;
using System.Text;
using FM.UI.Widgets;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// The transfer offer on screen, as its terms are laid out (FINDINGS §8.42).
///
/// The offer is a column of term rows (<c>#frames-contract-terms</c>) under three headings:
/// Base Details, Additional Fees and Additional Clauses. Each row has its name in
/// <c>#primary-text</c> and its value in one of three forms: an amount box (FMCombobox, set
/// with /setnumber), a dropdown of choices (set with /select), or plain text. Above the rows
/// are the offer-wide choices (negotiable or not, a deadline for the answer) and below them
/// what it costs. Each heading but the first has an "Add" dropdown that puts another row in.
/// </summary>
internal static class Offer
{
	public static string Current()
	{
		var root = Root();
		if (root == null) return "{}\n";

		var rows = new List<string>();
		var adds = new List<string>();
		var options = new List<string>();
		var section = "";

		Visit(root);

		string Plain(string name)
		{
			try { return Text.Plain(root.Q<TextElement>(name)?.text ?? ""); } catch { return ""; }
		}

		return "{"
			+ $"\"Club\": \"{Report.Escape(Plain("club-player-text"))}\", "
			+ $"\"Player\": \"{Report.Escape(Plain("player-text").Trim('(', ')'))}\", "
			+ $"\"BudgetCost\": \"{Report.Escape(Plain("Text_TransferCostValue"))}\", "
			+ $"\"TotalCost\": \"{Report.Escape(Plain("Text_TotalCostValue"))}\", "
			+ "\"Options\": [" + string.Join(", ", options) + "], "
			+ "\"Rows\": [" + string.Join(", ", rows) + "], "
			+ "\"Adds\": [" + string.Join(", ", adds) + "]}\n";

		void Visit(VisualElement element)
		{
			string name;
			try
			{
				if (element.resolvedStyle.display == DisplayStyle.None) return;
				name = element.name ?? "";
			}
			catch { return; }

			// A heading is known by its text: the base terms and the players to exchange share
			// the element name.
			switch (name)
			{
				case "players-to-exchange-title" or "fees-title" or "clauses-title":
					try { section = Text.Plain(element.TryCast<TextElement>()?.text ?? "") ; } catch { }
					break;
				case "frames-contract-terms":
					if (Row(element, section) is { } row) rows.Add(row);
					return;
			}

			SIDropdown dropdown = null;
			try { dropdown = element.TryCast<SIDropdown>(); } catch { }
			if (dropdown != null && Shown(dropdown))
			{
				var label = Buttons.LabelOf(dropdown);
				if (label.StartsWith("Add", System.StringComparison.Ordinal) && section.Length > 0) adds.Add($"{{\"Section\": \"{section}\", {Choices(dropdown)}}}");
				else if (section.Length == 0) options.Add($"{{\"Label\": \"{Report.Escape(label)}\", {Choices(dropdown)}}}");
				return;
			}

			try
			{
				var hierarchy = element.hierarchy;
				for (var i = 0; i < hierarchy.childCount; i++) Visit(hierarchy.ElementAt(i));
			}
			catch { }
		}
	}

	/// <summary>One term: its name, what it is set to, and the control that changes it.</summary>
	private static string Row(VisualElement frame, string section)
	{
		if (!Shown(frame)) return null;

		string label;
		try { label = Text.Plain(frame.Q<TextElement>("primary-text")?.text ?? ""); } catch { label = ""; }
		if (label.Length == 0) return null;

		string secondary;
		try { secondary = Text.Plain(frame.Q<TextElement>("secondary-text")?.text ?? ""); } catch { secondary = ""; }

		FMCombobox box = null;
		SIDropdown choice = null;
		SIDropdown lockList = null;
		string fixedText = "";
		string remove = "";
		var stack = new Stack<VisualElement>();
		stack.Push(frame);
		var seen = 0;
		while (stack.Count > 0 && seen++ < 400)
		{
			var element = stack.Pop();
			string name;
			try
			{
				if (element.resolvedStyle.display == DisplayStyle.None) continue;
				name = element.name ?? "";
			}
			catch { continue; }

			// The lock beside a term is a dropdown of its own, not the term's value: how firmly
			// the term is held in the talks (FINDINGS §8.42).
			if (name == "lock-button-remapper")
			{
				if (lockList == null)
				{
					foreach (var inner in Buttons.AllDropdownsUnder(element))
					{
						if (!Shown(inner)) continue;
						lockList = inner;
						break;
					}
				}
				continue;
			}

			if (box == null)
			{
				try { box = element.TryCast<FMCombobox>(); } catch { }
				if (box != null) continue;
			}
			if (choice == null && box == null)
			{
				try { choice = element.TryCast<SIDropdown>(); } catch { }
				if (choice != null && !Shown(choice)) choice = null;
			}
			if (name == "fixed-text" && fixedText.Length == 0)
			{
				try { fixedText = Text.Plain(element.TryCast<TextElement>()?.text ?? ""); } catch { }
			}
			if (remove.Length == 0 && name == "remove-button-visible")
			{
				var button = Buttons.ButtonsUnder(element).FirstOrDefault();
				if (button.Pointer != null) remove = button.Pointer;
			}

			try
			{
				var hierarchy = element.hierarchy;
				for (var i = hierarchy.childCount - 1; i >= 0; i--) stack.Push(hierarchy.ElementAt(i));
			}
			catch { }
		}

		var sb = new StringBuilder("{");
		sb.Append($"\"Section\": \"{section}\", \"Label\": \"{Report.Escape(label)}\", ");
		if (secondary.Length > 0) sb.Append($"\"Detail\": \"{Report.Escape(secondary)}\", ");
		if (remove.Length > 0) sb.Append($"\"Remove\": \"{remove}\", ");

		// A term can have both: "After League Appearances" is a number of games and an amount.
		var parts = new List<string>();
		if (choice != null) parts.Add($"\"Choice\": {{{Choices(choice)}}}");
		if (box != null)
		{
			long amount = 0;
			try { amount = box.m_numericInput?.value ?? 0; } catch { }
			parts.Add($"\"Amount\": {{\"Pointer\": \"0x{box.Pointer.ToInt64():x}\", \"Value\": \"{Report.Escape(Buttons.LabelOf(box))}\", \"Number\": \"{amount}\"}}");
		}
		if (lockList != null)
		{
			var count = 0;
			try { count = lockList.m_choices?.Count ?? 0; } catch { }
			parts.Add($"\"Lock\": {{\"Pointer\": \"0x{lockList.Pointer.ToInt64():x}\", \"Index\": \"{lockList.SelectedIndex}\", \"Count\": \"{count}\"}}");
		}
		if (parts.Count == 0 || parts.Count == 1 && lockList != null) parts.Add($"\"Fixed\": \"{Report.Escape(fixedText)}\"");
		sb.Append(string.Join(", ", parts));
		return sb.Append('}').ToString();
	}

	/// <summary>A dropdown as pointer, what it shows, and what it offers.</summary>
	private static string Choices(SIDropdown dropdown)
	{
		var names = new List<string>();
		var choices = dropdown.m_choices;
		for (var i = 0; i < (choices?.Count ?? 0) && i < 60; i++) names.Add($"\"{Report.Escape(Buttons.ChoiceText(choices[i]) ?? "")}\"");
		var enabled = false;
		try { enabled = dropdown.enabledInHierarchy; } catch { }
		return $"\"Pointer\": \"0x{dropdown.Pointer.ToInt64():x}\", \"Value\": \"{Report.Escape(Buttons.LabelOf(dropdown))}\", "
			+ $"\"Enabled\": \"{enabled}\", \"Choices\": [{string.Join(", ", names)}]";
	}

	private static bool Shown(VisualElement element)
	{
		try { return element.panel != null && element.worldBound.width > 0; } catch { return false; }
	}

	/// <summary>The offer's panel: the nearest ancestor of its total that also holds its header.</summary>
	private static VisualElement Root()
	{
		foreach (var element in Buttons.Elements())
		{
			try
			{
				if (element.name != "Text_TotalCostValue" || !Shown(element)) continue;
			}
			catch { continue; }

			VisualElement root = element;
			for (var climbed = 0; climbed < 40; climbed++)
			{
				VisualElement parent;
				try { parent = root.hierarchy.parent; } catch { break; }
				if (parent == null) break;
				root = parent;
				try
				{
					if (root.Q<VisualElement>("club-player-text") != null && root.Q<VisualElement>("players-to-exchange-title") != null) return root;
				}
				catch { }
			}
		}
		return null;
	}
}
