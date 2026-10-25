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
	private static string? learned;   // the game's language, as last seen
	private static string? shown;     // the language the CLI prints in
	private static string? choice;    // the user's choice; "" follows the game
	private static Dictionary<string, string>? english;
	private static Dictionary<string, string>? source;

	// The language the game was last seen in, for what is printed before the game has answered.
	private static string GameCacheFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli", "language");

	// The language the user chose; absent means follow the game.
	private static string ChoiceFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli", "language-choice");

	/// <summary>The language the CLI prints in ("turkish", "english", or "" before a game is seen).</summary>
	public static string Language
	{
		get
		{
			if (shown != null) return shown;
			if (Choice is { Length: > 0 } chosen) return shown = chosen;
			return shown = GameLanguage;
		}
	}

	/// <summary>The language the user chose ("turkish", "english"), or "" to follow the game.</summary>
	public static string Choice
	{
		get
		{
			if (choice != null) return choice;
			var env = Environment.GetEnvironmentVariable("FM_LANG");
			if (!string.IsNullOrEmpty(env) && Parse(env) is { } fromEnv) return choice = fromEnv;
			try { return choice = File.Exists(ChoiceFile) ? Parse(File.ReadAllText(ChoiceFile)) ?? "" : ""; }
			catch { return choice = ""; }
		}
	}

	/// <summary>The game's own language as last seen ("turkish", "english", or "").</summary>
	public static string GameLanguage
	{
		get
		{
			if (learned != null) return learned;
			try { learned = File.Exists(GameCacheFile) ? File.ReadAllText(GameCacheFile).Trim() : ""; }
			catch { learned = ""; }
			return learned;
		}
	}

	/// <summary>Notes the language a bridge answer came in; a chosen language does not change.</summary>
	public static void Heard(string? now)
	{
		if (string.IsNullOrEmpty(now)) return;
		var before = learned;
		learned = now;
		if (before != now)
			try { Directory.CreateDirectory(Path.GetDirectoryName(GameCacheFile)!); File.WriteAllText(GameCacheFile, now); } catch { }
		if (Choice.Length == 0) shown = now;
	}

	/// <summary>Chooses the CLI's language, or "" to follow the game.</summary>
	public static void SetChoice(string? chosen)
	{
		chosen ??= "";
		try
		{
			if (chosen.Length == 0)
			{
				if (File.Exists(ChoiceFile)) File.Delete(ChoiceFile);
			}
			else
			{
				Directory.CreateDirectory(Path.GetDirectoryName(ChoiceFile)!);
				File.WriteAllText(ChoiceFile, chosen);
			}
		}
		catch { }
		choice = null;
		shown = null;
	}

	/// <summary>"turkish", "english", "" for auto, or null when the word is not a language.</summary>
	public static string? Parse(string text) => text.Trim().ToLowerInvariant() switch
	{
		"" or "auto" or "game" or "oyun" => "",
		"tr" or "turkish" or "turkce" => "turkish",
		"en" or "english" or "ingilizce" => "english",
		_ => null,
	};

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

	/// <summary>The English source of a translated label, or the label itself when it is not one.</summary>
	public static string Source(string shown)
	{
		var table = Labels.english ??= Load();
		if (table.Count == 0) return shown;
		if (source == null)
		{
			source = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var (key, value) in table) source.TryAdd(value, key);
		}
		return source.TryGetValue(shown, out var sourceKey) ? sourceKey : shown;
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
