using System.Text.Encodings.Web;
using System.Text.Json;

namespace FmCli;

/// <summary>
/// What --json prints: tuples keep their fields and Turkish letters stay letters.
/// </summary>
internal static class JsonOut
{
	private static readonly JsonSerializerOptions Options = new()
	{
		IncludeFields = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	public static string Of(object value) => JsonSerializer.Serialize(value, Options);
}
