using System;
using System.Linq;
using System.Reflection;
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

		// GetProperties is virtual on the base, so every reference type answers it. Only the
		// naming function is per-type and static, which is why it is looked up by reflection.
		var reference = value.TryCast<SI.Interop.InteropReference>();
		if (reference == null) return null;

		var properties = new List<PropertyID>();
		reference.GetProperties(properties);

		var typeName = value.GetIl2CppType()?.FullName;
		var name = DescriptionFunction(typeName);

		var sb = new StringBuilder();
		sb.AppendLine($"{typeName}  —  {properties.Count} propert(ies):");
		foreach (var p in properties)
		{
			string described;
			try { described = name?.Invoke(p.ID) ?? "<no describer>"; }
			catch (Exception ex) { described = $"<threw {ex.GetType().Name}>"; }
			sb.AppendLine($"  {p.ID,12}  {described}");
		}
		return sb.ToString();
	}

	/// <summary>
	/// The interop wrapper for an il2cpp reference type carries a static
	/// GetPropertyDescriptionInternal. Finding it by name keeps this working for reference types
	/// we have never seen, which is the whole point of discovering the model at runtime.
	/// </summary>
	private static Func<uint, string> DescriptionFunction(string il2cppTypeName)
	{
		if (string.IsNullOrEmpty(il2cppTypeName)) return null;

		var type = AppDomain.CurrentDomain.GetAssemblies()
			.Select(a => { try { return a.GetType(il2cppTypeName, false); } catch { return null; } })
			.FirstOrDefault(t => t != null);

		var method = type?.GetMethod("GetPropertyDescriptionInternal",
			BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(uint) }, null);
		if (method == null) return null;

		return id => (string)method.Invoke(null, new object[] { id });
	}

	/// <summary>
	/// Every reference type's full property list, named — the whole data model in one pass.
	///
	/// This needs no live data and no navigation: GetPropertiesInternal and
	/// GetPropertyDescriptionInternal are static, so a type answers for itself even when no
	/// instance of it has ever been bound. Discovering the model by walking the tree only ever
	/// showed the properties a screen happened to have populated.
	/// </summary>
	public static string Catalog(string filter)
	{
		var types = AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(TypesOf)
			// Not every reference type derives from InteropReference — several sit directly on
			// Il2CppSystem.Object — so the static enumerator is the criterion, not the base type.
			.Where(t => Enumerator(t) != null)
			.Where(t => filter == null || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
			.OrderBy(t => t.FullName, StringComparer.Ordinal);

		var sb = new StringBuilder();
		foreach (var type in types)
		{
			var enumerate = Enumerator(type);
			var describe = type.GetMethod("GetPropertyDescriptionInternal",
				BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(uint) }, null);
			var kind = type.GetMethod("GetPropertyTypeInternal",
				BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(uint) }, null);
			if (enumerate == null) continue;

			var properties = new List<PropertyID>();
			try { enumerate.Invoke(null, new object[] { properties }); }
			catch (Exception ex) { sb.AppendLine($"{type.FullName}  <enumerate threw {ex.InnerException?.GetType().Name ?? ex.GetType().Name}>"); continue; }

			sb.AppendLine($"{type.FullName}  ({properties.Count})");
			foreach (var p in properties)
			{
				sb.AppendLine($"    {p.ID,12}  {KindOf(kind, p.ID),-28}  {NameOf(p)}");
			}
		}
		return sb.ToString();
	}

	/// <summary>
	/// A property's real name, from the registry the game keeps for itself.
	///
	/// GetPropertyDescriptionInternal is stripped in the release build and returns empty, which
	/// is why the ids looked like meaningless four-character codes. But every id is registered
	/// in PropertyIdentifierSet with its name, display name and owning type, so the names are
	/// right there in the running process.
	/// </summary>
	/// <summary>The registered name alone, for callers that match on it rather than print it.</summary>
	public static string PlainNameOf(PropertyID property)
	{
		try { return property.GetIdentifierSet()?.GetInfo(property)?.Name; }
		catch { return null; }
	}

	public static string NameOf(PropertyID property)
	{
		try
		{
			var info = property.GetIdentifierSet()?.GetInfo(property);
			if (info == null) return "<unregistered>";

			// FM27 dropped IdentifierInfo.DisplayName.
			return info.Name;
		}
		catch (Exception ex) { return $"<threw {ex.GetType().Name}>"; }
	}

	private static MethodInfo Enumerator(Type type)
	{
		try
		{
			return type.GetMethod("GetPropertiesInternal", BindingFlags.Public | BindingFlags.Static,
				null, new[] { typeof(List<PropertyID>) }, null);
		}
		catch { return null; }
	}

	private static Type[] TypesOf(Assembly assembly)
	{
		try { return assembly.GetTypes(); }
		catch { return new Type[0]; }
	}

	/// <summary>
	/// A property's value kind, and for a reference-valued one the type it points at — which is
	/// what turns a flat property list into a navigable graph.
	/// </summary>
	private static string KindOf(MethodInfo describeKind, uint id)
	{
		if (describeKind == null) return "?";
		try
		{
			var kind = (BindingKind)describeKind.Invoke(null, new object[] { id });
			var target = kind.Reference;
			var targetName = target.GetIdentifierSet()?.GetInfo(target)?.Name;

			return string.IsNullOrEmpty(targetName) || targetName == "Invalid"
				? kind.Kind.ToString()
				: $"{kind.Kind} -> {targetName}";
		}
		catch (Exception ex) { return $"<{ex.InnerException?.GetType().Name ?? ex.GetType().Name}>"; }
	}
}
