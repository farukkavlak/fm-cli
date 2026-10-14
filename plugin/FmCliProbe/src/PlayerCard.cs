using System;
using System.Collections.Generic;
using System.Linq;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// The player whose page is open: who he is, his contract, and his attributes.
///
/// The page sits on top of whatever it was opened from, and that screen — a squad table —
/// binds ages and names of its own. So the page is found first, by the one thing only it has,
/// an attribute list, and everything else is read from inside it.
/// </summary>
internal static class PlayerCard
{
	private static readonly HashSet<string> Wanted = new()
	{
		"ConditionString", "ConditionCommentTypeString", "InjuryDescription", "Personality", "Height",
		"RightFootednessString", "LeftFootednessString", "TotalCareerLeagueAppearances", "TotalCareerLeagueGoals",
		"ThreeLetterName", "PositionCombinedString",
	};

	/// <summary>
	/// Pages opened one after another stay loaded under each other, so there can be several
	/// players' pages at once; `name` asks for a particular one, and without it the first found
	/// is given.
	/// </summary>
	public static string Current(string name)
	{
		var pages = new List<IReadOnlyNode>();
		var seenPages = new HashSet<ulong>();
		foreach (var (_, node) in Report.Walk())
		{
			if (node.Name != "AttributeTypeInfo") continue;

			var root = node;
			while (root.Parent?.Parent != null) root = root.Parent;
			if (seenPages.Add(root.Key.m_key)) pages.Add(root);
		}

		string first = null;
		foreach (var page in pages)
		{
			var card = Read(page, out var who);
			if (card == null) continue;
			if (!string.IsNullOrEmpty(name) && string.Equals(who, name, StringComparison.OrdinalIgnoreCase)) return card;
			first ??= card;
		}

		return string.IsNullOrEmpty(name) ? first ?? "{}\n" : "{}\n";
	}

	private static string Read(IReadOnlyNode page, out string who)
	{
		who = null;

		var header = new Dictionary<string, string>();
		var attributes = new List<Dictionary<string, string>>();
		var traits = new List<string>();
		var groups = new List<ulong>();
		SI.Core.TypedValue personValue = null;

		var queue = new Queue<IReadOnlyNode>();
		queue.Enqueue(page);
		var seen = 0;

		while (queue.Count > 0 && seen++ < 20000)
		{
			var node = queue.Dequeue();
			for (var c = node.FirstChild; c != null; c = c.NextSibling) queue.Enqueue(c);

			var name = node.Name;
			if (string.IsNullOrEmpty(name)) continue;

			switch (name)
			{
				case "Person" when !header.ContainsKey("Name"):
					var person = Report.Fields(node);
					if (!person.ContainsKey("Name") || !person.ContainsKey("Age")) break;
					foreach (var (key, value) in person) header.TryAdd(key, value);
					try { personValue = node.Value; } catch { }

					try
					{
						var born = State.Date(Child(node, "DateOfBirth")?.Value?.AsString());
						if (born != null) header["Born"] = born.Value.Day.ToString("yyyy-MM-dd");
					}
					catch { }

					Nested(node, "TransferValueRange", "TransferValueRangeString", "Value", header);
					var contract = Child(node, "FullContract");
					if (contract != null)
					{
						Nested(contract, "Wage", "DisplayValue", "Wage", header);
						Nested(contract, "AgreedPlayingTime", "DisplayValue", "PlayingTime", header);
						if (Report.Fields(contract).TryGetValue("EndDateDescription", out var ends)) header["ContractEnds"] = ends;
					}
					break;

				case "attribute":
					var type = Child(node, "AttributeTypeInfo");
					var values = Child(node, "AttributeValues");
					if (type == null || values == null) break;
					if (!Report.Fields(type).TryGetValue("Name", out var attribute)) break;
					if (!Report.Fields(values).TryGetValue("PropertyValue", out var rating)) break;

					// Each block of attributes is a list of its own, and the lists come in the
					// order the page shows them.
					var list = ListOf(node);
					var group = groups.IndexOf(list);
					if (group < 0) { groups.Add(list); group = groups.Count - 1; }

					// Its number (42 for Pace) names it in every language; the words read back can be another's.
					// The screen binds only its name; the id is in the record itself.
					var id = "";
					try { id = Submenus.RowId(type.Value).Id.ToString(); } catch { }
					attributes.Add(new Dictionary<string, string> { ["Name"] = attribute, ["Value"] = rating, ["Group"] = group.ToString(), ["Id"] = id });
					break;

				case "Nation" when !header.ContainsKey("Nation"):
					if (Report.Fields(node).TryGetValue("Name", out var nation)) header["Nation"] = nation;
					break;

				case "Text":
					try
					{
						var text = Text.Of(node.Value);
						if (text.Length > 0 && !traits.Contains(text)) traits.Add(text);
					}
					catch { }
					break;

				default:
					if (!Wanted.Contains(name) || header.ContainsKey(name)) break;
					try
					{
						var value = node.Value;
						if (value == null || value.IsNull) break;
						var text = Text.Of(value);
						if (text.Length > 0) header[name] = text;
					}
					catch { }
					break;
			}
		}

		if (!header.TryGetValue("Name", out who)) return null;

		// The person is also put at a path of his own, so what the page does not show (his
		// rating in each position, his injuries) can be pulled from there. One path per player:
		// a path reused for someone else would answer with the first one's values.
		if (personValue is { IsNull: false } && RawPath.Set($"fmcli.card.p{(uint)who.GetHashCode()}", personValue) == null)
			header["Path"] = $"fmcli.card.p{(uint)who.GetHashCode()}";

		return "{\"Player\": " + Report.Json(header)
		       + ", \"Traits\": [" + string.Join(", ", traits.Select(t => $"\"{Report.Escape(t)}\"")) + "]"
		       + ", \"Attributes\": [\n  " + string.Join(",\n  ", attributes.Select(Report.Json)) + "\n]}\n";
	}

	private static ulong ListOf(IReadOnlyNode node)
	{
		for (var ancestor = node.Parent; ancestor != null; ancestor = ancestor.Parent)
			if ((ancestor.Name ?? "").Contains("List", StringComparison.Ordinal)) return ancestor.Key.m_key;
		return 0;
	}

	private static void Nested(IReadOnlyNode node, string child, string field, string As, Dictionary<string, string> into)
	{
		var holder = Child(node, child);
		if (holder != null && Report.Fields(holder).TryGetValue(field, out var value)) into[As] = value;
	}

	private static IReadOnlyNode Child(IReadOnlyNode node, string name)
	{
		for (var c = node.FirstChild; c != null; c = c.NextSibling)
			if (c.Name == name) return c;
		return null;
	}
}
