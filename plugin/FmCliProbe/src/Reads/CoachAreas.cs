using System.Collections.Generic;
using System.Linq;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// The coach assignments page's figures per training area, from the page's own record
/// (CoachAssignmentsReference): each area's workload (CoachWorkload) and its lead coach's rating
/// (LeadCoachRating), keyed by the area's record name (AttackingTacticalCategory …). The page draws
/// them in blocks under the grid whose order and headings say nothing in another language; the record
/// names each area. Question only: nothing is changed.
/// </summary>
internal static class CoachAreas
{
	public static string Current()
	{
		foreach (var (_, node) in Report.Walk())
		{
			if (node.Name != "CoachAssignments") continue;
			if (node.Value?.Get()?.TryCast<DynamicReference>() is not { } record) continue;
			var workload = Numbers(Entry(record, "CoachWorkload"));
			var lead = Numbers(Entry(record, "LeadCoachRating"));
			if (workload.Count == 0) continue;

			var areas = workload.Keys.Union(lead.Keys).Select(area =>
				$"{{\"Area\": \"{Report.Escape(area)}\", \"Workload\": \"{(workload.TryGetValue(area, out var w) ? w : "")}\", \"Lead\": \"{(lead.TryGetValue(area, out var l) ? l : "")}\"}}");
			return "[" + string.Join(", ", areas) + "]\n";
		}
		return "[]\n";
	}

	private static DynamicReference Entry(DynamicReference record, string name)
	{
		var keys = new Il2CppSystem.Collections.Generic.List<SI.Bindable.Reference.Core.PropertyID>();
		record.GetProperties(keys);
		foreach (var key in keys)
			if (Schema.NameOf(key) == name)
				try { return record[key.ID]?.Get()?.TryCast<DynamicReference>(); } catch { return null; }
		return null;
	}

	/// <summary>A record's entries that are areas (named …Category), with their numbers.</summary>
	private static Dictionary<string, string> Numbers(DynamicReference record)
	{
		var found = new Dictionary<string, string>();
		if (record == null) return found;
		var keys = new Il2CppSystem.Collections.Generic.List<SI.Bindable.Reference.Core.PropertyID>();
		record.GetProperties(keys);
		foreach (var key in keys)
		{
			var name = Schema.NameOf(key);
			if (name == null || !name.EndsWith("Category", System.StringComparison.Ordinal)) continue;
			try
			{
				var value = record[key.ID];
				if (value == null || value.IsNull) continue;
				// A rating is a range of its own (MinValue to MaxValue, the same for a known coach).
				if (value.Get()?.TryCast<DynamicReference>() is { } range)
				{
					var inner = new Il2CppSystem.Collections.Generic.List<SI.Bindable.Reference.Core.PropertyID>();
					range.GetProperties(inner);
					foreach (var part in inner)
						if (Schema.NameOf(part) == "MinValue" && range[part.ID] is { IsNull: false } min) found[name] = Text.Of(min);
				}
				else found[name] = Text.Of(value);
			}
			catch { }
		}
		return found;
	}
}
