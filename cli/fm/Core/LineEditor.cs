namespace FmCli;

/// <summary>
/// The session's line: arrows move and go back through the lines typed before (kept between
/// sessions), tab completes a command, its options or a player's name. Console.ReadLine on
/// macOS has neither. Without a terminal on both sides it is Console.ReadLine.
/// </summary>
internal sealed class LineEditor(Func<string[], string, Task<IReadOnlyList<string>>> complete)
{
	private const int Kept = 500;

	private static string HistoryFile =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli", "history");

	private readonly List<string> history = Load();

	public static bool Usable => !Console.IsInputRedirected && !Console.IsOutputRedirected;

	/// <summary>The line typed, or null for ctrl-D on an empty line. Ctrl-C drops the line and asks again.</summary>
	public async Task<string?> ReadLine(string prompt, int promptWidth)
	{
		if (!Usable)
		{
			Console.Write(prompt);
			return Console.ReadLine();
		}

		var line = new System.Text.StringBuilder();
		var cursor = 0;
		var shownRows = 0; // rows the line took when last drawn, above the cursor's row
		var back = history.Count; // the history line shown; Count is the line being typed
		var typed = ""; // what was being typed before going back through history
		var tabbedAgain = false;

		void Draw()
		{
			var width = Width();
			if (shownRows > 0) Console.Write($"\u001b[{shownRows}A");
			Console.Write($"\r{prompt}{line}\u001b[J");

			// Where the terminal left the cursor (after the line's end) and where it belongs.
			var end = promptWidth + line.Length;
			var at = promptWidth + cursor;
			var endRow = end / width;
			if (end > 0 && end % width == 0) Console.Write("\r\n"); // the terminal waits to wrap; make it
			var up = endRow - at / width;
			if (up > 0) Console.Write($"\u001b[{up}A");
			Console.Write($"\r{(at % width > 0 ? $"\u001b[{at % width}C" : "")}");
			shownRows = at / width;
		}

		void Set(string text)
		{
			line.Clear().Append(text);
			cursor = line.Length;
		}

		var controlC = Console.TreatControlCAsInput;
		Console.TreatControlCAsInput = true;
		try
		{
			Console.Write(prompt);
			while (true)
			{
				var key = Console.ReadKey(true);
				var ctrl = key.Modifiers.HasFlag(ConsoleModifiers.Control);
				var alt = key.Modifiers.HasFlag(ConsoleModifiers.Alt);
				if (key.Key != ConsoleKey.Tab) tabbedAgain = false;

				switch (key.Key)
				{
					case ConsoleKey.Enter:
						Console.Write("\r\n");
						var done = line.ToString();
						Remember(done);
						return done;

					case ConsoleKey.C when ctrl:
						Console.Write("^C\r\n");
						line.Clear();
						cursor = 0;
						shownRows = 0;
						back = history.Count;
						Console.Write(prompt);
						continue;

					case ConsoleKey.D when ctrl:
						if (line.Length == 0)
						{
							Console.Write("\r\n");
							return null;
						}
						if (cursor < line.Length) line.Remove(cursor, 1);
						break;

					case ConsoleKey.R when ctrl:
						if (shownRows > 0) Console.Write($"\u001b[{shownRows}A");
						Console.Write("\r\u001b[J");
						shownRows = 0;
						var (found, run) = Search(line.ToString());
						if (found != null) Set(found);
						if (run)
						{
							Console.Write($"\r{prompt}{line}\u001b[J\r\n");
							Remember(line.ToString());
							return line.ToString();
						}
						Console.Write("\r\u001b[J");
						Console.Write(prompt);
						break;

					case ConsoleKey.L when ctrl:
						Console.Write("\u001b[2J\u001b[H");
						shownRows = 0;
						break;

					case ConsoleKey.UpArrow:
					case ConsoleKey.P when ctrl:
						if (back == 0) continue;
						if (back == history.Count) typed = line.ToString();
						Set(history[--back]);
						break;

					case ConsoleKey.DownArrow:
					case ConsoleKey.N when ctrl:
						if (back == history.Count) continue;
						back++;
						Set(back == history.Count ? typed : history[back]);
						break;

					case ConsoleKey.LeftArrow when ctrl || alt:
					case ConsoleKey.B when alt:
						cursor = WordStart(line, cursor);
						break;

					case ConsoleKey.RightArrow when ctrl || alt:
					case ConsoleKey.F when alt:
						cursor = WordEnd(line, cursor);
						break;

					case ConsoleKey.LeftArrow:
					case ConsoleKey.B when ctrl:
						if (cursor > 0) cursor--;
						break;

					case ConsoleKey.RightArrow:
					case ConsoleKey.F when ctrl:
						if (cursor < line.Length) cursor++;
						break;

					case ConsoleKey.Home:
					case ConsoleKey.A when ctrl:
						cursor = 0;
						break;

					case ConsoleKey.End:
					case ConsoleKey.E when ctrl:
						cursor = line.Length;
						break;

					case ConsoleKey.Backspace when alt:
					case ConsoleKey.W when ctrl:
						var from = WordStart(line, cursor);
						line.Remove(from, cursor - from);
						cursor = from;
						break;

					case ConsoleKey.Backspace:
					case ConsoleKey.H when ctrl:
						if (cursor == 0) continue;
						line.Remove(--cursor, 1);
						break;

					case ConsoleKey.Delete:
						if (cursor < line.Length) line.Remove(cursor, 1);
						break;

					case ConsoleKey.U when ctrl:
						line.Remove(0, cursor);
						cursor = 0;
						break;

					case ConsoleKey.K when ctrl:
						line.Remove(cursor, line.Length - cursor);
						break;

					case ConsoleKey.Tab:
						var listed = await Tab(line, cursor, tabbedAgain);
						if (listed)
						{
							// The choices were written below; the line starts again under them.
							shownRows = 0;
							Console.Write(prompt);
						}
						else tabbedAgain = true;
						cursor = TabCursor ?? Math.Min(cursor, line.Length);
						break;

					default:
						if (key.KeyChar == '\0' || char.IsControl(key.KeyChar)) continue;
						line.Insert(cursor++, key.KeyChar);
						break;
				}

				Draw();
			}
		}
		finally
		{
			Console.TreatControlCAsInput = controlC;
		}
	}

	private int? TabCursor;

	/// <summary>
	/// Ctrl-R: goes back through the lines typed before for one holding what is typed (case and
	/// accents aside). Ctrl-R again finds an older one; enter runs it; esc, an arrow or another
	/// key keeps it on the line to change; ctrl-C or ctrl-G gives back the line as it was. The
	/// search is drawn on one row; the caller has cleared it.
	/// </summary>
	private (string? Line, bool Run) Search(string was)
	{
		var query = new System.Text.StringBuilder();
		var at = history.Count; // the line found; Count is none
		var failed = false;

		int Find(int before)
		{
			for (var i = Math.Min(before, history.Count) - 1; i >= 0; i--)
				if (Nl.Text.Fold(history[i]).Contains(Nl.Text.Fold(query.ToString()), StringComparison.Ordinal)) return i;
			return -1;
		}

		void Draw()
		{
			var head = failed ? L($"no match") : L($"search");
			var shown = $"({head}) '{query}': {(at < history.Count ? history[at] : "")}";
			var width = Width();
			if (shown.Length >= width) shown = shown[..(width - 2)] + "…";
			Console.Write($"\r{shown}\u001b[K");
		}

		Draw();
		while (true)
		{
			var key = Console.ReadKey(true);
			var ctrl = key.Modifiers.HasFlag(ConsoleModifiers.Control);
			switch (key.Key)
			{
				case ConsoleKey.Enter:
					return at < history.Count ? (history[at], true) : (null, false);
				case ConsoleKey.C when ctrl:
				case ConsoleKey.G when ctrl:
					return (was, false);
				case ConsoleKey.R when ctrl:
					if (query.Length == 0) break;
					var older = Find(at);
					failed = older < 0;
					if (older >= 0) at = older;
					break;
				case ConsoleKey.Backspace:
				case ConsoleKey.H when ctrl:
					if (query.Length > 0) query.Length--;
					var again = query.Length == 0 ? history.Count : Find(history.Count);
					failed = again < 0 && query.Length > 0;
					at = again < 0 ? history.Count : again;
					break;
				default:
					if (key.KeyChar == '\0' || char.IsControl(key.KeyChar) || ctrl)
						return (at < history.Count ? history[at] : was, false);
					query.Append(key.KeyChar);
					var match = Find(history.Count);
					failed = match < 0;
					if (match >= 0) at = match;
					break;
			}
			Draw();
		}
	}

	/// <summary>
	/// Completes the word before the cursor. One choice is put in whole; several put in what they
	/// share, and a second tab lists them. True when the list was written.
	/// </summary>
	private async Task<bool> Tab(System.Text.StringBuilder line, int cursor, bool again)
	{
		TabCursor = null;
		var text = line.ToString();
		var start = CurrentWordStart(text, cursor);
		var before = Session.Split(text[..start]);
		var word = text[start..cursor].TrimStart('"');

		IReadOnlyList<string> choices;
		try { choices = await complete(before, word); }
		catch { return false; }
		if (choices.Count == 0) return false;

		if (choices.Count == 1)
		{
			var whole = choices[0].Contains(' ') ? $"\"{choices[0]}\" " : $"{choices[0]} ";
			line.Remove(start, cursor - start).Insert(start, whole);
			TabCursor = start + whole.Length;
			return false;
		}

		var shared = Shared(choices);
		if (shared.Length > word.Length)
		{
			var put = choices.Any(c => c.Contains(' ')) && shared.Contains(' ') ? $"\"{shared}" : shared;
			line.Remove(start, cursor - start).Insert(start, put);
			TabCursor = start + put.Length;
			return false;
		}

		if (!again) return false;

		Console.Write("\r\n");
		var width = Width();
		var column = choices.Max(c => c.Length) + 2;
		var across = Math.Max(1, width / column);
		for (var i = 0; i < choices.Count; i++)
		{
			Console.Write(choices[i].PadRight(column));
			if ((i + 1) % across == 0 || i == choices.Count - 1) Console.Write("\r\n");
		}
		return true;
	}

	/// <summary>Where the word being typed starts; a quoted name counts as one word.</summary>
	private static int CurrentWordStart(string text, int cursor)
	{
		var quoted = false;
		var start = 0;
		for (var i = 0; i < cursor; i++)
		{
			if (text[i] == '"') quoted = !quoted;
			else if (text[i] == ' ' && !quoted) start = i + 1;
		}
		return start;
	}

	/// <summary>The start the choices share, in the case of the first: the names are matched without case or accents.</summary>
	private static string Shared(IReadOnlyList<string> choices)
	{
		var first = choices[0];
		var length = first.Length;
		foreach (var other in choices.Skip(1))
		{
			var i = 0;
			while (i < length && i < other.Length && char.ToLowerInvariant(first[i]) == char.ToLowerInvariant(other[i])) i++;
			length = i;
		}
		return first[..length];
	}

	// A terminal that does not say its size reads 0; drawn at width 0 every letter would wrap.
	private static int Width()
	{
		try { return Console.WindowWidth >= 20 ? Console.WindowWidth : 80; }
		catch (IOException) { return 80; }
	}

	private static int WordStart(System.Text.StringBuilder line, int cursor)
	{
		var i = cursor;
		while (i > 0 && line[i - 1] == ' ') i--;
		while (i > 0 && line[i - 1] != ' ') i--;
		return i;
	}

	private static int WordEnd(System.Text.StringBuilder line, int cursor)
	{
		var i = cursor;
		while (i < line.Length && line[i] == ' ') i++;
		while (i < line.Length && line[i] != ' ') i++;
		return i;
	}

	private void Remember(string line)
	{
		if (string.IsNullOrWhiteSpace(line) || (history.Count > 0 && history[^1] == line)) return;
		history.Add(line);
		if (history.Count > Kept) history.RemoveRange(0, history.Count - Kept);
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(HistoryFile)!);
			File.WriteAllLines(HistoryFile, history);
		}
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
	}

	private static List<string> Load()
	{
		try { return File.Exists(HistoryFile) ? File.ReadAllLines(HistoryFile).Where(l => l.Length > 0).TakeLast(Kept).ToList() : []; }
		catch (IOException) { return []; }
		catch (UnauthorizedAccessException) { return []; }
	}
}
