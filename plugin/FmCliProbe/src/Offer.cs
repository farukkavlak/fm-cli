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
///
/// The contract talks are built from the same parts (FINDINGS §8.44): rows named
/// <c>#frames-contract terms</c>, a lock beside each, "Add Promise" / "Add Bonus" / "Add Clause"
/// lists, and a lock for the entire offer. They come in two stages, promises and then terms,
/// each its own block.
/// </summary>
internal static class Offer
{
	public static string Current() => Read(Root(), contract: false);

	/// <summary>The contract talks on screen: the promises stage or the terms stage.</summary>
	public static string Contract() => Read(ContractRoot(out var stage), contract: true, stage);

	private static string Read(VisualElement root, bool contract, string stage = "")
	{
		if (root == null) return "{}\n";

		var rows = new List<string>();
		var adds = new List<string>();
		var options = new List<string>();
		var section = "";
		var lastText = "";
		var texts = new List<string>();

		Visit(root);

		// Beside the terms, in panels of their own: what the wage does to the budget, and the
		// contract he has now.
		if (contract)
		{
			texts.Clear();
			foreach (var element in Buttons.Elements())
			{
				try
				{
					if (element.name != "text" || !Shown(element)) continue;
					texts.Add(Text.Plain(element.TryCast<TextElement>()?.text ?? ""));
				}
				catch { }
			}
		}
		// Labels can repeat elsewhere on screen ("Wage" on a page beneath), so a value is only
		// taken when it looks like one.
		var facts = new List<string>();
		foreach (var (label, looks) in new (string, System.Func<string, bool>)[]
		{
			("Cost", v => v.Contains("p/w")), ("Current", v => v.Contains("p/w")), ("Avg.", v => v.Contains("p/w")),
			("Remaining", v => v.Contains("p/w") || v == "N/A"), ("Wage", v => v.Contains("p/w")), ("Contract Expires", v => v.Contains('/')),
		})
		{
			for (var at = texts.IndexOf(label); at >= 0; at = texts.IndexOf(label, at + 1))
			{
				// His contract is read from the talks' player report only, where the wage follows
				// the transfer value; a page beneath can carry another player's.
				if (label == "Wage" && (at == 0 || texts[at - 1] != "Transfer Value")) continue;
				if (label == "Contract Expires" && !texts.Skip(System.Math.Max(0, at - 3)).Take(3).Contains("Wage")) continue;
				var value = texts.Skip(at + 1).Take(3).FirstOrDefault(looks);
				if (value == null) continue;
				facts.Add($"\"{label}\": \"{Report.Escape(value)}\"");
				break;
			}
		}

		string Plain(string name)
		{
			try { return Text.Plain(root.Q<TextElement>(name)?.text ?? ""); } catch { return ""; }
		}

		return "{"
			+ (contract
				? $"\"Stage\": \"{stage}\", \"Club\": \"{Report.Escape(Plain("club-subtitle-text"))}\", \"Player\": \"{Report.Escape(Plain("club-player-text").Trim('(', ')'))}\", "
				: $"\"Club\": \"{Report.Escape(Plain("club-player-text"))}\", \"Player\": \"{Report.Escape(Plain("player-text").Trim('(', ')'))}\", ")
			+ $"\"BudgetCost\": \"{Report.Escape(Plain("Text_TransferCostValue"))}\", "
			+ $"\"TotalCost\": \"{Report.Escape(Plain("Text_TotalCostValue"))}\", "
			+ $"\"TotalCostTitle\": \"{Report.Escape(Plain("Text_TotalCostTitle"))}\", "
			+ "\"Options\": [" + string.Join(", ", options) + "], "
			+ "\"Rows\": [" + string.Join(", ", rows) + "], "
			+ "\"Facts\": {" + string.Join(", ", facts) + "}, "
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
				// A loan's headings have names of their own: options and clauses of the loan, then
				// the fees and clauses should it become permanent.
				case "players-to-exchange-title" or "fees-title" or "clauses-title"
					or "clause-area-title" or "ltb-fees-title" or "ltb-additional-fees-title" or "ltb-clauses-title":
					try { section = Text.Plain(element.TryCast<TextElement>()?.text ?? "") ; } catch { }
					break;
				case "frames-contract-terms" or "frames-contract terms":
					if (Row(element, section) is { } row) rows.Add(row);
					return;
				// A player put into the deal: his name, his position, transfer or loan, and a way out.
				case "exchange-player-row-enabled":
					if (Row(element, section, exchange: true) is { } player) rows.Add(player);
					return;
				// The contract's headings are plain text: "Bonuses & Clauses", "Additional
				// Promises", and "Entire offer" before its lock.
				case "text" when contract:
					try { lastText = Text.Plain(element.TryCast<TextElement>()?.text ?? ""); } catch { }
					texts.Add(lastText);
					if (lastText is "Bonuses & Clauses" or "Additional Promises") section = lastText;
					break;
			}

			SIDropdown dropdown = null;
			try { dropdown = element.TryCast<SIDropdown>(); } catch { }
			if (dropdown != null && Shown(dropdown))
			{
				var label = Buttons.LabelOf(dropdown);
				if (label.StartsWith("Add", System.StringComparison.Ordinal) && (contract || section.Length > 0))
					adds.Add($"{{\"Section\": \"{(contract ? label : section)}\", {Choices(dropdown)}}}");
				else if (contract && IsLock(dropdown))
					options.Add($"{{\"Label\": \"{Report.Escape(lastText)}\", \"Lock\": {LockOf(dropdown)}}}");
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
	private static string Row(VisualElement frame, string section, bool exchange = false)
	{
		if (!Shown(frame)) return null;

		string label;
		try { label = Text.Plain(frame.Q<TextElement>(exchange ? "NameText" : "primary-text")?.text ?? ""); } catch { label = ""; }
		if (label.Length == 0) return null;

		string secondary;
		try
		{
			secondary = exchange
				? Text.Plain(frame.Q<VisualElement>("exchange-person-position-remapper")?.Q<TextElement>("text")?.text ?? "")
				: Text.Plain(frame.Q<TextElement>("secondary-text")?.text ?? "");
		}
		catch { secondary = ""; }

		FMCombobox box = null;
		SIDropdown choice = null;
		SIDropdown lockList = null;
		string fixedText = "";
		string remove = "";
		var warning = false;
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
			if (name is "lock-button-remapper" or "lock-button-enabled")
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
			// The exchange row's own remove button sits loose in the row.
			if (exchange && remove.Length == 0 && name == "buttons-controls-pc-default")
			{
				try { if (element.TryCast<SI.Bindable.SIButton>() != null && element.enabledInHierarchy) remove = $"0x{element.Pointer.ToInt64():x}"; } catch { }
			}
			if (name == "fixed-text" && fixedText.Length == 0)
			{
				try { fixedText = Text.Plain(element.TryCast<TextElement>()?.text ?? ""); } catch { }
			}
			// The warning's frame is always there; only an icon inside it is the warning.
			if (name == "warning-visible")
			{
				try { warning |= element.hierarchy.childCount > 0 && Shown(element); } catch { }
				continue;
			}
			if (remove.Length == 0 && name is "remove-button-visible" or "remove-button-enabled")
			{
				var usable = true;
				try { usable = element.enabledInHierarchy; } catch { }
				if (!usable) continue;
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
		if (warning) sb.Append("\"Warning\": \"True\", ");

		// A term can have both: "After League Appearances" is a number of games and an amount.
		var parts = new List<string>();
		if (choice != null) parts.Add($"\"Choice\": {{{Choices(choice)}}}");
		if (box != null)
		{
			long amount = 0;
			try { amount = box.m_numericInput?.value ?? 0; } catch { }
			parts.Add($"\"Amount\": {{\"Pointer\": \"0x{box.Pointer.ToInt64():x}\", \"Value\": \"{Report.Escape(Buttons.LabelOf(box))}\", \"Number\": \"{amount}\"}}");
		}
		if (lockList != null) parts.Add($"\"Lock\": {LockOf(lockList)}");
		if (parts.Count == 0 || parts.Count == 1 && lockList != null) parts.Add($"\"Fixed\": \"{Report.Escape(fixedText)}\"");
		sb.Append(string.Join(", ", parts));
		return sb.Append('}').ToString();
	}

	/// <summary>
	/// A padlock: its choices are the lock values themselves ("0 · 1 · 2", or "0 · 2" where only
	/// open and closed are offered), with the one set given by its position.
	/// </summary>
	private static string LockOf(SIDropdown dropdown)
	{
		var names = new List<string>();
		var choices = dropdown.m_choices;
		for (var i = 0; i < (choices?.Count ?? 0) && i < 8; i++) names.Add($"\"{Report.Escape(Buttons.ChoiceText(choices[i]) ?? "")}\"");
		var enabled = false;
		try { enabled = dropdown.enabledInHierarchy; } catch { }
		return $"{{\"Pointer\": \"0x{dropdown.Pointer.ToInt64():x}\", \"Index\": \"{dropdown.SelectedIndex}\", \"Enabled\": \"{enabled}\", \"Choices\": [{string.Join(", ", names)}]}}";
	}

	private static bool IsLock(SIDropdown dropdown)
	{
		var choices = dropdown.m_choices;
		var count = choices?.Count ?? 0;
		if (count == 0) return false;
		for (var i = 0; i < count; i++)
			if (!int.TryParse(Buttons.ChoiceText(choices[i]) ?? "", out _)) return false;
		return true;
	}

	/// <summary>A dropdown as pointer, what it shows, and what it offers.</summary>
	private static string Choices(SIDropdown dropdown)
	{
		var names = new List<string>();
		var disabled = new List<string>();
		var choices = dropdown.m_choices;
		for (var i = 0; i < (choices?.Count ?? 0) && i < 60; i++)
		{
			var name = $"\"{Report.Escape(Buttons.ChoiceText(choices[i]) ?? "")}\"";
			names.Add(name);
			if (Buttons.ChoiceDisabled(choices[i])) disabled.Add(name);
		}
		var enabled = false;
		try { enabled = dropdown.enabledInHierarchy; } catch { }
		return $"\"Pointer\": \"0x{dropdown.Pointer.ToInt64():x}\", \"Value\": \"{Report.Escape(Buttons.LabelOf(dropdown))}\", "
			+ $"\"Enabled\": \"{enabled}\", \"Choices\": [{string.Join(", ", names)}], \"Disabled\": [{string.Join(", ", disabled)}]";
	}

	private static bool Shown(VisualElement element)
	{
		try { return element.panel != null && element.worldBound.width > 0; } catch { return false; }
	}

	/// <summary>
	/// The contract talks' block: "Contract Negotiation Promises Block" first, then
	/// "Contract Negotiation Contents Block". Found by climbing from a shown term row.
	/// </summary>
	private static VisualElement ContractRoot(out string stage)
	{
		stage = "";
		foreach (var element in Buttons.Elements())
		{
			try
			{
				if (element.name != "frames-contract terms" || !Shown(element)) continue;
			}
			catch { continue; }

			VisualElement root = element;
			for (var climbed = 0; climbed < 60; climbed++)
			{
				try { root = root.hierarchy.parent; } catch { break; }
				if (root == null) break;
				string name;
				try { name = root.name ?? ""; } catch { continue; }
				if (!name.StartsWith("Contract Negotiation", System.StringComparison.Ordinal)) continue;
				stage = name.Contains("Promises") ? "Promises" : "Terms";
				return root;
			}
		}
		return null;
	}

	/// <summary>
	/// The offer's panel: the nearest ancestor of its total that also holds its header. A loan
	/// with no fee to buy him has no total, so a term row will do as well.
	/// </summary>
	private static VisualElement Root()
	{
		foreach (var anchor in new[] { "Text_TotalCostValue", "frames-contract-terms" })
		{
			foreach (var element in Buttons.Elements())
			{
				try
				{
					if (element.name != anchor || !Shown(element)) continue;
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
		}
		return null;
	}
}
