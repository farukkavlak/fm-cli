using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FmCli.Nl;

/// <summary>
/// What the user asked to keep for a save (T210): wishes, rules for the club, plans, things about
/// themselves. Kept in ~/.config/fm-cli/memory.json, one book per club and save (the key `ask` keeps
/// its talk under). The assistant (`ask`, `plan`) is given the book; it may offer a note, kept only on
/// the user's yes. A note is the user's word, never a fact about the game: what it says of the game is
/// read again. Each note keeps its number for good, so "#3" means the same note after another is deleted.
/// </summary>
internal static class Memory
{
	/// <summary>A note: its number, the user's words, its kind, the game day and real time it was kept, and who wrote it.</summary>
	public sealed record Note(int N, string Text, string Kind, string Day, DateTime At, string By);

	public sealed class Book
	{
		public string Club { get; set; } = "";
		public int Next { get; set; } = 1;
		public List<Note> Notes { get; set; } = [];
	}

	public static readonly string[] Kinds = ["wish", "rule", "plan", "about"];

	public const int MaxNotes = 60;
	public const int MaxLength = 300;

	private static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	/// <summary>FM_MEMORY names another file (tests).</summary>
	public static string File =>
		Environment.GetEnvironmentVariable("FM_MEMORY") is { Length: > 0 } other ? other
		: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli", "memory.json");

	private static Dictionary<string, Book> ReadAll()
	{
		try { return JsonSerializer.Deserialize<Dictionary<string, Book>>(System.IO.File.ReadAllText(File), Options) ?? []; }
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
	}

	/// <summary>The book of <paramref name="save"/> (empty when there is none).</summary>
	public static Book Of(string save) =>
		save.Length > 0 && ReadAll().TryGetValue(save, out var book) ? book : new Book();

	/// <summary>Writes the book of <paramref name="save"/>; false when it could not be written.</summary>
	public static bool Keep(string save, Book book)
	{
		if (save.Length == 0) return false;
		var all = ReadAll();
		if (book.Notes.Count == 0) all.Remove(save);
		else all[save] = book;
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(File)!);
			System.IO.File.WriteAllText(File, JsonSerializer.Serialize(all, Options));
			if (!OperatingSystem.IsWindows()) System.IO.File.SetUnixFileMode(File, UnixFileMode.UserRead | UnixFileMode.UserWrite);
			return true;
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
	}

	/// <summary>What is wrong with <paramref name="text"/> as a note, or null.</summary>
	public static string? NotANote(string text, Book book, int? replaces = null) =>
		text.Trim().Length == 0 ? "empty"
		: text.Length > MaxLength ? $"longer than {MaxLength} characters"
		: replaces is { } r && book.Notes.All(n => n.N != r) ? $"there is no note #{r}"
		: replaces == null && book.Notes.Count >= MaxNotes ? $"the book is full ({MaxNotes} notes): delete one first"
		: null;

	/// <summary>Adds a note, or puts one in place of note <paramref name="replaces"/> (it keeps its number); the note as kept.</summary>
	public static Note Write(Book book, string text, string kind, string day, string by, int? replaces = null)
	{
		kind = Kinds.Contains(kind) ? kind : "wish";
		if (replaces is { } r && book.Notes.FindIndex(n => n.N == r) is var i and >= 0)
		{
			var put = new Note(r, text.Trim(), kind, day, DateTime.UtcNow, by);
			book.Notes[i] = put;
			return put;
		}
		var note = new Note(book.Next++, text.Trim(), kind, day, DateTime.UtcNow, by);
		book.Notes.Add(note);
		return note;
	}

	/// <summary>The notes as the model is given them; empty when there are none.</summary>
	public static string ForModel(Book book) =>
		book.Notes.Count == 0 ? ""
		: "\n\nNotes the user asked you to keep for this save (#number, kind, game day kept: the user's words):\n"
		  + string.Join("\n", book.Notes.Select(n => $"#{n.N} [{n.Kind}, {n.Day}] {n.Text}"))
		  + "\nThese are the user's wishes, rules and plans, not facts about the game: a figure or a state in one may be out of date, so read it again; when a read differs, the read holds and you say the note may be old. Keep to them when they bear on what is asked, and cite one you stand on as (#N).";

	/// <summary>The notes an answer cites as (#N), among those the book holds.</summary>
	public static List<Note> Cited(string answer, Book book) =>
		[.. Regex.Matches(answer, @"#(\d+)").Select(m => int.Parse(m.Groups[1].Value)).Distinct()
			.Select(n => book.Notes.FirstOrDefault(x => x.N == n)).OfType<Note>()];
}
