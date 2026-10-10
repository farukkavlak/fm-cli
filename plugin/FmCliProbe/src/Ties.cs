using System.Collections.Generic;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// The ties of a knockout round as the Stages page draws them (FINDINGS §8.105): one #MainPart
/// each, with the home side in #LeftSide, the away side in #RightSide and between them #Versus —
/// the score, or "V" before it is played, and beside it how it was settled ("p" penalties, "e"
/// extra time) on the side that went through. The text alone does not say which side a division
/// badge or a "p" belongs to; where it sits does.
/// </summary>
internal static class Ties
{
	public static string Current()
	{
		var rows = new List<string>();
		foreach (var element in Buttons.Elements())
		{
			VisualElement left, right, versus;
			try
			{
				if (element.name != "MainPart" || element.panel == null || element.worldBound.width <= 0) continue;
				left = element.Q<VisualElement>("LeftSide");
				right = element.Q<VisualElement>("RightSide");
				versus = element.Q<VisualElement>("Versus");
			}
			catch { continue; }
			if (left == null || right == null || versus == null) continue;

			var home = Side(left);
			var away = Side(right);
			var middle = new List<(string Name, string Text, string Under)>();
			Collect(versus, "", middle, 0);

			var row = new Dictionary<string, string>
			{
				["Home"] = home.Team,
				["HomeExtra"] = home.Extra,
				["Score"] = string.Join(" ", middle.FindAll(t => t.Under == "").ConvertAll(t => t.Text)),
				["HomeNote"] = string.Join(" ", middle.FindAll(t => t.Under == "HomeResultTypeVisible").ConvertAll(t => t.Text)),
				["AwayNote"] = string.Join(" ", middle.FindAll(t => t.Under == "AwayResultTypeVisible").ConvertAll(t => t.Text)),
				["Away"] = away.Team,
				["AwayExtra"] = away.Extra,
			};
			rows.Add(Report.Json(row));
		}
		return "[\n  " + string.Join(",\n  ", rows) + "\n]\n";
	}

	/// <summary>A side's team — or whatever stands in for one not yet known — and the small text beside it: its division in a domestic cup, its aggregate score in a two-legged tie.</summary>
	private static (string Team, string Extra) Side(VisualElement side)
	{
		var texts = new List<(string Name, string Text, string Under)>();
		Collect(side, "", texts, 0);
		var names = texts.FindAll(t => t.Name != "SIText").ConvertAll(t => t.Text);
		var badges = texts.FindAll(t => t.Name == "SIText").ConvertAll(t => t.Text);
		return (string.Join(" ", names), string.Join(" ", badges));
	}

	/// <summary>The texts that are showing under an element, each with which result-type holder it is in.</summary>
	private static void Collect(VisualElement element, string under, List<(string Name, string Text, string Under)> into, int depth)
	{
		if (depth > 16) return;
		try
		{
			if (element.resolvedStyle.display == DisplayStyle.None) return;
			var name = element.name ?? "";
			if (name is "HomeResultTypeVisible" or "AwayResultTypeVisible") under = name;

			// A button is a text element too, an empty one with the real text inside it.
			var text = element.TryCast<TextElement>();
			if (text != null)
			{
				var shown = Text.Plain(text.text ?? "").Trim();
				if (shown.Length > 0) into.Add((name, shown, under));
			}
			var count = element.hierarchy.childCount;
			for (var i = 0; i < count; i++) Collect(element.hierarchy[i], under, into, depth + 1);
		}
		catch { }
	}
}
