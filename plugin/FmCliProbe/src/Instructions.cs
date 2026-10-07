using System.Collections.Generic;
using System.Text;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// The team instructions on the tactics page (FINDINGS §8.48): under each phase heading
/// (<c>#PhaseText</c>: Overview, Final Third, Progression, Buildup…) a cell per instruction, its
/// name in <c>#text</c> and its setting in <c>#text-second</c>. Pressing a cell opens the
/// setting's choices as a list named <c>options-list</c>.
/// </summary>
internal static class Instructions
{
	public static string Current()
	{
		var rows = new List<string>();
		var phase = "";
		foreach (var element in Buttons.Elements())
		{
			string name;
			try
			{
				if (element.panel == null || element.worldBound.width <= 0) continue;
				name = element.name ?? "";
			}
			catch { continue; }

			if (name == "PhaseText")
			{
				try { phase = Text.Plain(element.TryCast<TextElement>()?.text ?? ""); } catch { }
				continue;
			}
			if (name != "CellButton" || phase.Length == 0) continue;

			string label, value;
			try
			{
				label = Text.Plain(element.Q<TextElement>("text")?.text ?? "");
				value = Text.Plain(element.Q<TextElement>("text-second")?.text ?? "");
			}
			catch { continue; }
			if (label.Length == 0 || value.Length == 0) continue;
			rows.Add($"{{\"Phase\": \"{Report.Escape(phase)}\", \"Label\": \"{Report.Escape(label)}\", \"Value\": \"{Report.Escape(value)}\", \"Pointer\": \"0x{element.Pointer.ToInt64():x}\"}}");
		}
		return "[" + string.Join(", ", rows) + "]\n";
	}

	/// <summary>
	/// The tiles of a card such as a player's instructions (FINDINGS §8.53): each a
	/// <c>layout-tile-interaction-button</c> holding its name in <c>#title-text</c> and its setting
	/// in <c>#name</c>. Tiles with no setting (links to other pages) are left out.
	/// </summary>
	public static string Tiles()
	{
		var rows = new List<string>();
		foreach (var element in Buttons.Elements())
		{
			try
			{
				if (element.name != "layout-tile-interaction-button" || element.panel == null || element.worldBound.width <= 0) continue;
				var title = Text.Plain(element.Q<TextElement>("title-text")?.text ?? "");
				var value = Text.Plain(element.Q<TextElement>("name")?.text ?? "");
				if (title.Length == 0 || value.Length == 0) continue;
				rows.Add($"{{\"Label\": \"{Report.Escape(title)}\", \"Value\": \"{Report.Escape(value)}\", \"Pointer\": \"0x{element.Pointer.ToInt64():x}\"}}");
			}
			catch { }
		}
		return "[" + string.Join(", ", rows) + "]\n";
	}
}
