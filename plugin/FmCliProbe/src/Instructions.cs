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
}
