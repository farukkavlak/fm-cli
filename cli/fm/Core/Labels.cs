global using static FmCli.Labels;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace FmCli;

/// <summary>
/// The CLI's own words in the game's language. The CLI is written in English; a text it prints goes
/// through <c>L()</c>, whose holes make a template ("{0} {1} players") that is looked up in the
/// Turkish catalog when the game is set to Turkish. Any other language is shown as written.
/// </summary>
internal static class Labels
{
	private static string? language;
	private static Dictionary<string, string>? english;

	// The language the game was last seen in, for what is printed before the game has answered.
	private static string CacheFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli", "language");

	/// <summary>The language the game is set to, as the plugin names it ("turkish", "english").</summary>
	public static string Language
	{
		get
		{
			if (language != null) return language;
			try { language = File.Exists(CacheFile) ? File.ReadAllText(CacheFile).Trim() : ""; }
			catch { language = ""; }
			return language;
		}
	}

	/// <summary>Notes the language a bridge answer came in.</summary>
	public static void Heard(string? now)
	{
		if (string.IsNullOrEmpty(now) || now == language) return;
		var before = Language;
		language = now;
		if (before == now) return;
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
			File.WriteAllText(CacheFile, now);
		}
		catch { }
	}

	/// <summary>Whether the CLI's words are shown in Turkish.</summary>
	public static bool Turkish => Language.StartsWith("turkish", StringComparison.OrdinalIgnoreCase);

	/// <summary>Whether the text is shown as written: English, or no game seen yet.</summary>
	private static bool AsWritten => !Turkish;

	/// <summary>A text the CLI prints, in the game's language.</summary>
	public static string L(string text) => AsWritten ? text : Lookup(text.Replace("{", "{{").Replace("}", "}}")) is { } shown ? string.Format(CultureInfo.CurrentCulture, shown) : text;

	/// <summary>
	/// A text with its template written out ("{0} …"), in the game's language: for values that need an
	/// await, which an interpolated <see cref="L(ref Template)"/> cannot hold.
	/// </summary>
	public static string Lf(string template, params object?[] values) =>
		string.Format(CultureInfo.CurrentCulture, AsWritten ? template : Lookup(template) ?? template, values);

	/// <summary>A text the CLI prints with values in it, in the game's language.</summary>
	public static string L(ref Template text) => text.Text();

	// FM_LABELS_MISSING=<file> collects the templates no catalog holds, to see what is left to translate.
	private static readonly string? MissingFile = Environment.GetEnvironmentVariable("FM_LABELS_MISSING");

	private static string? Lookup(string key)
	{
		english ??= Load();
		if (english.TryGetValue(key, out var shown)) return shown;
		if (MissingFile != null)
			try { File.AppendAllText(MissingFile, key.Replace("\t", "\\t").Replace("\n", "\\n") + "\n"); } catch { }
		return null;
	}

	private static Dictionary<string, string> Load()
	{
		var table = new Dictionary<string, string>(StringComparer.Ordinal);
		try
		{
			using var stream = typeof(Labels).Assembly.GetManifestResourceStream("labels-turkish.tsv");
			if (stream == null) return table;
			using var reader = new StreamReader(stream);
			while (reader.ReadLine() is { } line)
			{
				if (line.Length == 0 || line[0] == '#') continue;
				var tab = line.IndexOf('\t');
				if (tab <= 0) continue;
				table[Unescape(line[..tab])] = Unescape(line[(tab + 1)..]);
			}
		}
		catch { }
		return table;
	}

	private static string Unescape(string s) => s.Replace("\\t", "\t").Replace("\\n", "\n");

	/// <summary>
	/// An interpolated text taken apart: its literal parts with a numbered hole for each value, the
	/// key the catalog is read by, and the values to put back into whichever template is shown.
	/// </summary>
	[InterpolatedStringHandler]
	public ref struct Template
	{
		private readonly StringBuilder key;
		private readonly List<object?> values;

		public Template(int literalLength, int formattedCount)
		{
			key = new StringBuilder(literalLength + formattedCount * 4);
			values = new List<object?>(formattedCount);
		}

		public void AppendLiteral(string s) => key.Append(s.Replace("{", "{{").Replace("}", "}}"));

		public void AppendFormatted<T>(T value) => Hole(value, null, null);
		public void AppendFormatted<T>(T value, int alignment) => Hole(value, alignment, null);
		public void AppendFormatted<T>(T value, string? format) => Hole(value, null, format);
		public void AppendFormatted<T>(T value, int alignment, string? format) => Hole(value, alignment, format);

		private void Hole(object? value, int? alignment, string? format)
		{
			key.Append('{').Append(values.Count);
			if (alignment is { } a) key.Append(',').Append(a);
			if (!string.IsNullOrEmpty(format)) key.Append(':').Append(format);
			key.Append('}');
			values.Add(value);
		}

		public readonly string Text()
		{
			var written = key.ToString();
			var shown = AsWritten ? written : Lookup(written) ?? written;
			return string.Format(CultureInfo.CurrentCulture, shown, values.ToArray());
		}
	}
}
