using System;

namespace FmCliProbe;

internal static class Text
{
	/// <summary>
	/// Strips the game's embedded markup from a string.
	///
	/// Bound strings carry a base64 blob of click-target data ahead of the text, and it is not
	/// separated by anything reliable — padding only appears when the blob's length calls for
	/// it. So the game's own parser does the work rather than a guess at the delimiter.
	/// </summary>
	public static string Clean(string value)
	{
		if (string.IsNullOrEmpty(value)) return value;

		try
		{
			return SI.Translation.EmbeddedDataUtilities.ContainsEmbeddedData(value)
				? SI.Translation.EmbeddedDataUtilities.RemoveEmbeddedData(value)
				: value;
		}
		catch { return value; }
	}

	/// <summary>
	/// A label as a user reads it. Text shown on a control also carries rich-text tags for
	/// colour and links, which are presentation and would keep a label from being matched.
	/// </summary>
	public static string Plain(string value)
	{
		var cleaned = Clean(value);
		return string.IsNullOrEmpty(cleaned)
			? cleaned
			: System.Text.RegularExpressions.Regex.Replace(cleaned, "<[^<>]+>", "").Trim();
	}
}
