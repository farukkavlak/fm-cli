using System;
using SI.Bindable;
using SI.Bindable.Reference.Core;

namespace FmCliProbe;

/// <summary>
/// Puts a single property into the binding tree on demand.
///
/// Until now a value only appeared once a screen bound it, which meant reading the squad's
/// condition required finding the screen that shows it. The game's interop subsystem exposes the
/// two steps a screen performs — create the node, then open the channel that feeds it — and
/// neither takes a span, so both are reachable from here.
///
/// The value arrives over the following frames, like any other binding, so a read is a separate
/// call rather than a return value.
/// </summary>
internal static class Materialise
{
	public static string Property(string path, string propertyName)
	{
		if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(propertyName))
			return "error: ?path=<reference node>&name=<Property> required";

		var interop = Game.Interop;
		if (interop == null) return "error: game interop subsystem unavailable";

		var value = FmApi.ValueAt(path, out var error);
		if (error != null) return error;

		var reference = value.TryCast<SI.Interop.InteropReference>();
		if (reference == null) return $"error: '{path}' is not a reference ({value.GetIl2CppType()?.Name})";

		var property = PropertyNamed(reference, propertyName, out var accepted);
		if (property == null)
			return $"error: {value.GetIl2CppType()?.Name} has no property '{propertyName}'\n"
				+ $"       {accepted} accepted; /schema?filter=<type> lists them";

		// The tree names a property node after the property, so the key follows the same
		// convention and an ordinary path read finds it afterwards.
		var childPath = $"{path}.{propertyName}";
		var parentKey = PathKey.Of(path);
		var childKey = PathKey.Of(childPath);

		try
		{
			interop.AddNode(childKey, parentKey, property.Value);
			interop.OpenChannel(reference, property.Value, childKey);
		}
		catch (Exception ex)
		{
			return $"error: {ex.GetType().Name}: {ex.Message}";
		}

		return $"opened channel for {propertyName} at '{childPath}' (value arrives next frame)";
	}

	/// <summary>
	/// The id of a property this reference accepts, matched by registered name.
	///
	/// PropertyIdentifierSet.GetID takes a span, so a name cannot be looked up directly; the
	/// reference lists its own accepted properties instead, and each one can be named.
	/// </summary>
	private static PropertyID? PropertyNamed(
		SI.Interop.InteropReference reference, string name, out int accepted)
	{
		var properties = new Il2CppSystem.Collections.Generic.List<PropertyID>();
		reference.GetProperties(properties);
		accepted = properties.Count;

		foreach (var candidate in properties)
		{
			if (string.Equals(Schema.PlainNameOf(candidate), name, StringComparison.OrdinalIgnoreCase))
				return candidate;
		}
		return null;
	}
}
