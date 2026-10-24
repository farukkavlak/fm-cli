using System.Text.Json;

namespace FmCli;

/// <summary>
/// The two ways a JSON field is read in the command layer: a string field as a string (empty when
/// missing), and any scalar as the text the game sent. Keeping the rules here stops each reader
/// from growing its own slightly different copy.
/// </summary>
internal static class JsonRead
{
	public static string Text(JsonElement element, string name) =>
		element!.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
			&& value.ValueKind == JsonValueKind.String
			? value.GetString() ?? ""
			: "";

	public static string RawText(JsonElement element, string name) =>
		element!.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
			? value.ToString()
			: "";

	public static string? NullableText(JsonElement element, string name) =>
		element!.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
			&& value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}
