using System.Collections.Generic;
using System.Linq;
using System.Text;
using FM.UI.Widgets;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// The transfer offer on screen, as its terms are laid out.
///
/// The offer is a column of term rows (<c>#frames-contract-terms</c>) under three headings:
/// Base Details, Additional Fees and Additional Clauses. Each row has its name in
/// <c>#primary-text</c> and its value in one of three forms: an amount box (FMCombobox, set
/// with /setnumber), a dropdown of choices (set with /select), or plain text. Above the rows
/// are the offer-wide choices (negotiable or not, a deadline for the answer) and below them
/// what it costs. Each heading but the first has an "Add" dropdown that puts another row in.
///
/// The contract talks are built from the same parts: each of the contract's own terms is a template
/// named for it (wage-term, playing-time-promise), added bonuses and clauses are the items of
/// add-on-list, with a lock beside each and one for the entire offer. They come in two stages,
/// promises and then terms.
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
		var offerType = "";
		var lockWrites = "";

		Visit(root);

		// The contract talks' record and the player they are with, as paths: what the wage does to
		// the budget and the contract he has now are read from them (ContractNegotiation…, FullContract).
		var session = "";
		var person = "";
		if (contract && lockWrites.Length > 0)
		{
			session = lockWrites[..System.Math.Max(0, lockWrites.LastIndexOf('.'))].TrimStart('.');
			person = Above(session, "Player");
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
			+ $"\"OfferType\": \"{offerType}\", "
			+ "\"Options\": [" + string.Join(", ", options) + "], "
			+ "\"Rows\": [" + string.Join(", ", rows) + "], "
			+ $"\"Session\": \"{Report.Escape(session)}\", \"Person\": \"{Report.Escape(person)}\", "
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

			switch (name)
			{
				// The headings: base terms, fees and clauses; a loan's options and clauses, then the
				// fees and clauses should it become permanent. Their text is only shown.
				case "players-to-exchange-title" or "fees-title" or "clauses-title"
					or "clause-area-title" or "ltb-fees-title" or "ltb-additional-fees-title" or "ltb-clauses-title":
					try { section = Text.Plain(element.TryCast<TextElement>()?.text ?? "") ; } catch { }
					break;
				case "frames-contract-terms":
					if (Row(element, section) is { } row) rows.Add(row);
					return;
				// A player put into the deal: his name, his position, transfer or loan, and a way out.
				case "exchange-player-row-enabled":
					if (Row(element, section, exchange: true) is { } player) rows.Add(player);
					return;
				// The contract's own terms each have a template of their own, named for what they are
				// (wage-term, agent-fee-term, playing-time-promise); added bonuses, clauses and promises
				// are the items of add-on-list.
				case not null when contract && (name.EndsWith("-term", System.StringComparison.Ordinal) || name.EndsWith("-promise", System.StringComparison.Ordinal)):
					if (Row(element, "", key: name) is { } term) rows.Add(term);
					return;
				case "add-on-list" when contract:
					section = name;
					break;
				case "item-enabled" when contract && section.Length > 0:
					if (Row(element, section) is { } addOn) rows.Add(addOn);
					return;
				// FM27's promise items in the list have no "item-enabled": each is a "binding_expect" frame holding
				// the promise's name, its note, its value, its lock and its remove button (T132: three promises
				// in the deal were read only as three unnamed locks).
				case "binding_expect" when contract && section.Length > 0 && Within(element, "add-on-list"):
					if (Row(element, section) is { } promise) { rows.Add(promise); return; }
					break;
				// The text before the entire offer's lock is its name, shown only.
				case "text" when contract:
					try { lastText = Text.Plain(element.TryCast<TextElement>()?.text ?? ""); } catch { }
					break;
			}

			SIDropdown dropdown = null;
			try { dropdown = element.TryCast<SIDropdown>(); } catch { }
			if (dropdown != null && Shown(dropdown))
			{
				var label = Buttons.LabelOf(dropdown);
				var writes = "";
				try { writes = dropdown.SelectionValueBinding.Path ?? ""; } catch { }
				// An "Add" list sits in an element named for it: fees-add-dropdown, add-bonus-dropdown.
				if (AddHolder(dropdown) is { } holder)
					adds.Add($"{{\"Section\": \"{Report.Escape(contract ? label : section)}\", \"Key\": \"{holder}\", {Choices(dropdown)}}}");
				else if (contract && IsLock(dropdown))
				{
					if (lockWrites.Length == 0) lockWrites = Writes(dropdown);
					options.Add($"{{\"Label\": \"{Report.Escape(lastText)}\", \"Key\": \"{Report.Escape(writes[(writes.LastIndexOf('.') + 1)..])}\", \"Lock\": {LockOf(dropdown)}}}");
				}
				else if (section.Length == 0)
				{
					// Which offer-wide choice it is, by the field of the offer it writes
					// (NegotiableOptionListValue, DeadlineDateOptionValue); the offer's kind sits beside it.
					if (offerType.Length == 0) offerType = Beside(dropdown, "TransferOfferType");
					options.Add($"{{\"Label\": \"{Report.Escape(label)}\", \"Key\": \"{Report.Escape(writes[(writes.LastIndexOf('.') + 1)..])}\", {Choices(dropdown)}}}");
				}
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

	/// <summary>Whether an element sits inside the one of that name.</summary>
	private static bool Within(VisualElement element, string name)
	{
		try
		{
			for (var up = element.hierarchy.parent; up != null; up = up.hierarchy.parent)
				if (up.name == name) return true;
		}
		catch { }
		return false;
	}

	/// <summary>One term: its name, what it is set to, and the control that changes it.</summary>
	private static string Row(VisualElement frame, string section, bool exchange = false, string key = "")
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
			// the term is held in the talks.
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
		sb.Append($"\"Section\": \"{Report.Escape(section)}\", \"Label\": \"{Report.Escape(label)}\", ");
		if (key.Length > 0) sb.Append($"\"Key\": \"{key}\", ");
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
	/// A field of the record a dropdown writes into, read where the dropdown's own field is bound:
	/// the offer's TransferOfferType (1 transfer, 3 loan) beside its negotiable choice. Empty when not bound.
	/// </summary>
	private static string Beside(SIDropdown dropdown, string field)
	{
		try
		{
			var b = Game.Bindings;
			var written = dropdown.m_selectionValueBindingKey;
			if (!b.Exists(ref written)) return "";
			var path = b.GetPathDebug(ref written) ?? "";
			var cut = path.LastIndexOf('.');
			if (cut <= 0) return "";
			var key = PathKey.Of($"{path[..cut]}.{field}");
			return b.Exists(ref key) && b.Get(ref key) is { IsNull: false } held ? Text.Of(held) : "";
		}
		catch { return ""; }
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
		var codes = new List<string>();
		var choices = dropdown.m_choices;
		for (var i = 0; i < (choices?.Count ?? 0) && i < 60; i++)
		{
			var name = $"\"{Report.Escape(Buttons.ChoiceText(choices[i]) ?? "")}\"";
			names.Add(name);
			if (Buttons.ChoiceDisabled(choices[i])) disabled.Add(name);
			codes.Add($"\"{Code(Buttons.ValueOf(choices[i]))}\"");
		}
		var enabled = false;
		try { enabled = dropdown.enabledInHierarchy; } catch { }
		return $"\"Pointer\": \"0x{dropdown.Pointer.ToInt64():x}\", \"Value\": \"{Report.Escape(Buttons.LabelOf(dropdown))}\", "
			+ $"\"Enabled\": \"{enabled}\", \"Choices\": [{string.Join(", ", names)}], \"Disabled\": [{string.Join(", ", disabled)}], "
			+ $"\"Codes\": [{string.Join(", ", codes)}]";
	}

	/// <summary>
	/// What an "Add" list's row stands for in the game: the kind of fee or clause, a four-letter code packed in
	/// a number ("nspc", a percentage of the next sale), the same in every language. A number that is not four
	/// letters is given as it is; none is "".
	/// </summary>
	private static string Code(int? value)
	{
		if (value is not { } v || v == 0) return "";
		var bytes = new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
		return bytes.All(b => b is >= (byte)'0' and <= (byte)'9' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z')
			? System.Text.Encoding.ASCII.GetString(bytes)
			: v.ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	private static bool Shown(VisualElement element)
	{
		try { return element.panel != null && element.worldBound.width > 0; } catch { return false; }
	}

	/// <summary>
	/// The contract talks' block: the BlockRemapper above a shown term (a template named *-term
	/// or *-promise). The promises stage has only promises; the terms stage has terms.
	/// </summary>
	private static VisualElement ContractRoot(out string stage)
	{
		stage = "";
		foreach (var element in Buttons.Elements())
		{
			string name;
			try
			{
				name = element.name ?? "";
				if (!(name.EndsWith("-term", System.StringComparison.Ordinal) || name.EndsWith("-promise", System.StringComparison.Ordinal)) || !Shown(element)) continue;
			}
			catch { continue; }

			VisualElement root = element;
			for (var climbed = 0; climbed < 30; climbed++)
			{
				try { root = root.hierarchy.parent; } catch { break; }
				if (root == null) break;
				try { if (root.name != "BlockRemapper") continue; } catch { continue; }
				stage = name.EndsWith("-promise", System.StringComparison.Ordinal) ? "Promises" : "Terms";
				return root;
			}
		}
		return null;
	}

	/// <summary>The name of the element an "Add" list sits in (fees-add-dropdown, add-bonus-dropdown), or null.</summary>
	private static string AddHolder(VisualElement dropdown)
	{
		var at = dropdown;
		for (var up = 0; up < 5 && at != null; up++)
		{
			try
			{
				var name = at.name ?? "";
				if (name.EndsWith("-dropdown", System.StringComparison.Ordinal) && (name.StartsWith("add-", System.StringComparison.Ordinal) || name.Contains("-add-")))
					return name;
				at = at.hierarchy.parent;
			}
			catch { return null; }
		}
		return null;
	}

	/// <summary>The full data path a dropdown writes its choice to.</summary>
	private static string Writes(SIDropdown dropdown)
	{
		try
		{
			var b = Game.Bindings;
			var written = dropdown.m_selectionValueBindingKey;
			return b.Exists(ref written) ? b.GetPathDebug(ref written) ?? "" : "";
		}
		catch { return ""; }
	}

	/// <summary>The nearest path above <paramref name="path"/> that has <paramref name="field"/> bound under it, as that field's path.</summary>
	private static string Above(string path, string field)
	{
		try
		{
			var b = Game.Bindings;
			for (var cut = path.LastIndexOf('.'); cut > 0; cut = path.LastIndexOf('.', cut - 1))
			{
				var key = PathKey.Of($"{path[..cut]}.{field}");
				if (b.Exists(ref key) && b.Get(ref key) is { IsNull: false }) return $"{path[..cut]}.{field}";
			}
		}
		catch { }
		return "";
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
						// The other club's answer to a loan has no exchange heading; its header is enough.
						if (root.Q<VisualElement>("club-player-text") != null && (root.Q<VisualElement>("players-to-exchange-title") != null || root.Q<VisualElement>("player-text") != null)) return root;
					}
					catch { }
				}
			}
		}
		return null;
	}
}
