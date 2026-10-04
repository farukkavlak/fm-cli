using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime;
using SI.Bindable;
using UnityEngine;

namespace FmCliProbe;

/// <summary>
/// The game's screens, which are ScriptableObjects and so can be listed and addressed by name.
///
/// Navigation events take a PanelID rather than a string, which would normally mean knowing the
/// asset up front. But every PanelID is a loaded ScriptableObject carrying its own name, so the
/// whole screen inventory is enumerable — the same trick as the property registry, one layer up.
/// </summary>
internal static class Panels
{
	public static string Catalog(string filter)
	{
		var found = new List<string>();
		foreach (var panel in All())
		{
			string name;
			try { name = panel.name; }
			catch { continue; }

			if (!string.IsNullOrEmpty(filter) &&
			    name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

			found.Add($"  {panel.ID,12}  {name}");
		}

		found.Sort(StringComparer.OrdinalIgnoreCase);
		var sb = new StringBuilder();
		sb.AppendLine($"{found.Count} panel(s):");
		foreach (var line in found) sb.AppendLine(line);
		return sb.ToString();
	}

	/// <summary>The panel with this name, for callers that address a screen the way a user would.</summary>
	public static PanelID ByName(string name)
	{
		foreach (var panel in All())
		{
			try
			{
				if (string.Equals(panel.name, name, StringComparison.OrdinalIgnoreCase)) return panel;
			}
			catch { }
		}
		return null;
	}

	private static IEnumerable<PanelID> All()
	{
		foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PanelID>()))
		{
			var panel = o.TryCast<PanelID>();
			if (panel != null) yield return panel;
		}
	}
}
