using System;
using System.Collections.Generic;
using System.Linq;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// The conversation on screen — a squad meeting, a chat with a player, a press conference:
/// what has been said, how the listeners are taking it, and what each answer on offer is.
///
/// The answers themselves are buttons and are pressed as such; this is what surrounds them.
/// Answers come grouped under a heading that names the approach ("Convince"), and that
/// grouping only exists in the data, not on the buttons.
/// </summary>
internal static class Talk
{
	public static string Current()
	{
		var says = new SortedDictionary<int, string>();
		var moods = new Dictionary<string, int>();
		var answers = new List<Dictionary<string, string>>();
		var seen = new HashSet<string>();

		foreach (var (_, node) in Report.Walk())
		{
			switch (node.Name)
			{
				case "bubble" when int.TryParse(node.Parent?.Name, out var index):
					var bubble = Report.Fields(node);
					if (bubble.TryGetValue("DisplayString", out var line) && !says.ContainsKey(index))
						says[index] = (bubble.TryGetValue("IsHuman", out var human) && human == "True" ? "» " : "") + line;
					break;

				case "InteractionBodyLanguageString":
					var mood = Text.Of(node.Value);
					if (mood.Length > 0) moods[mood] = moods.TryGetValue(mood, out var count) ? count + 1 : 1;
					break;

				case "Action":
					var action = Report.Fields(node);
					if (!action.TryGetValue("ActionTitle", out var title) || title.Length == 0) break;
					if (action.TryGetValue("IsHeader", out var header) && header == "True") break;
					if (!seen.Add(title)) break;

					var answer = new Dictionary<string, string> { ["Label"] = title };
					var group = Heading(node);
					if (group != null) answer["Group"] = group;
					if (action.TryGetValue("Hint", out var hint) && hint.Length > 0) answer["Hint"] = hint;
					if (action.TryGetValue("ActionSubTitle", out var sub) && sub.Length > 0) answer["Detail"] = sub;
					if (action.TryGetValue("Recommended", out var recommended) && recommended == "True") answer["Recommended"] = "True";
					if (action.TryGetValue("IsDisabled", out var disabled) && disabled == "True") answer["Disabled"] = "True";
					answers.Add(answer);
					break;
			}
		}

		var counts = new Dictionary<string, string>();
		foreach (var (mood, count) in moods) counts[mood] = count.ToString();

		return "{\"Says\": [" + string.Join(", ", says.Values.Select(Quote)) + "], "
		       + "\"Mood\": " + Report.Json(counts) + ", "
		       + "\"Answers\": [" + string.Join(", ", answers.Select(Report.Json)) + "]}\n";
	}

	/// <summary>The heading an answer sits under: the nearest ancestor that holds a header action.</summary>
	private static string Heading(IReadOnlyNode answer)
	{
		for (var ancestor = answer.Parent; ancestor != null; ancestor = ancestor.Parent)
		{
			for (var c = ancestor.FirstChild; c != null; c = c.NextSibling)
			{
				if (c.Name != "Action" || c.Key.m_key == answer.Key.m_key) continue;

				var fields = Report.Fields(c);
				if (fields.TryGetValue("IsHeader", out var header) && header == "True")
					return fields.TryGetValue("ActionTitle", out var title) ? title : null;
			}
		}
		return null;
	}

	private static string Quote(string text) => $"\"{Report.Escape(text)}\"";
}
