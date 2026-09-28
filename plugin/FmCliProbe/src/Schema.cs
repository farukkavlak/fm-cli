using System;
using System.Text;
using Il2CppSystem.Collections.Generic;
using SI.Bindable.Reference.Core;

namespace FmCliProbe;

/// <summary>
/// References describe themselves: each knows which properties it accepts and can name them.
/// That means the data model is discovered from the running game rather than guessed from a
/// decompiled dump, which also makes it survive game updates.
/// </summary>
internal static class Schema
{
	/// <summary>Property ids and names a reference value accepts, or null if it is not one.</summary>
	public static string Describe(Il2CppSystem.Object value)
	{
		if (value == null) return null;

		var properties = new List<PropertyID>();
		Func<uint, string> name;

		if (value.TryCast<FM.UI.PersonReference>() is { } person)
		{
			person.GetProperties(properties);
			name = FM.UI.PersonReference.GetPropertyDescriptionInternal;
		}
		else if (value.TryCast<FM.UI.TeamReference>() is { } team)
		{
			team.GetProperties(properties);
			name = FM.UI.TeamReference.GetPropertyDescriptionInternal;
		}
		else if (value.TryCast<FM.UI.ClubReference>() is { } club)
		{
			club.GetProperties(properties);
			name = FM.UI.ClubReference.GetPropertyDescriptionInternal;
		}
		else if (value.TryCast<FM.UI.CompReference>() is { } comp)
		{
			comp.GetProperties(properties);
			name = FM.UI.CompReference.GetPropertyDescriptionInternal;
		}
		else
		{
			return null;
		}

		var sb = new StringBuilder();
		sb.AppendLine($"{properties.Count} propert(ies):");
		foreach (var p in properties)
		{
			string described;
			try { described = name(p.ID); }
			catch (Exception ex) { described = $"<threw {ex.GetType().Name}>"; }
			sb.AppendLine($"  {p.ID,12}  {described}");
		}
		return sb.ToString();
	}
}
