using System.Text.Json;
using System.Text.Json.Serialization;

namespace FmCli.Nl;

/// <summary>
/// What the router learned from the user (T206), kept in ~/.config/fm-cli/learned.json:
/// <list type="bullet">
/// <item>sentences and the tool the user chose for them: a pick from the offered list, or a yes
/// to the one shown. Never what the router ran on its own. Only the tool is learned; the
/// arguments are read from the words every time.</item>
/// <item>names: a typed word and the one of the save's people the user picked between two who
/// fit, by the game's id.</item>
/// </list>
/// A choice waits as pending until the next command: when that command is one of the others
/// that were offered, the choice was a mistake put right and is forgotten.
/// </summary>
internal static class Learned
{
	public sealed record Sentence(string Text, string Typed, string Tool, string Shown);

	public sealed record Name(string Typed, string Kind, string Id, string Full);

	public sealed record Pending(Sentence Sentence, List<string> Rivals);

	public sealed class Store
	{
		public List<Sentence> Sentences { get; set; } = [];
		public List<Name> Names { get; set; } = [];
		public Pending? Pending { get; set; }
	}

	private static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	/// <summary>FM_LEARNED names another file (tests and tools/nl-learn.py).</summary>
	public static string File =>
		Environment.GetEnvironmentVariable("FM_LEARNED") is { Length: > 0 } other ? other
		: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli", "learned.json");

	public static Store Load(string? file = null)
	{
		try { return JsonSerializer.Deserialize<Store>(System.IO.File.ReadAllText(file ?? File), Options) ?? new(); }
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
	}

	public static void Save(Store store)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(File)!);
			System.IO.File.WriteAllText(File, JsonSerializer.Serialize(store, Options));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
	}

	/// <summary>The learned sentences as examples for the nearest-sentence vote.</summary>
	public static List<(string Text, string Call)> Examples(Store store) =>
		[.. store.Sentences.Select(s => (s.Text, JsonSerializer.Serialize(s.Tool)))];

	/// <summary>
	/// The user chose <paramref name="sentence"/>'s tool; it is kept once the next command is not
	/// one of <paramref name="rivals"/> (the commands of the other choices).
	/// </summary>
	public static void Chose(Sentence sentence, IEnumerable<string> rivals)
	{
		var store = Load();
		Keep(store);
		store.Pending = new Pending(sentence, [.. rivals.Distinct()]);
		Save(store);
	}

	/// <summary>
	/// The next command is <paramref name="command"/> (its name, as `help` lists it): a pending
	/// choice is forgotten when it is one of the other choices, kept otherwise.
	/// </summary>
	public static void Next(string? command)
	{
		// looking at what was learned is no answer to it
		if (command == "learned" || !System.IO.File.Exists(File)) return;
		var store = Load();
		if (store.Pending is not { } pending) return;
		if (command != null && pending.Rivals.Contains(command)) store.Pending = null;
		else Keep(store);
		Save(store);
	}

	private static void Keep(Store store)
	{
		if (store.Pending is not { } pending) return;
		store.Sentences.RemoveAll(s => s.Text == pending.Sentence.Text);
		store.Sentences.Add(pending.Sentence);
		store.Pending = null;
	}

	/// <summary>The user picked <paramref name="full"/> for the words <paramref name="typed"/>.</summary>
	public static void Picked(string typed, string kind, string id, string full)
	{
		var store = Load();
		store.Names.RemoveAll(n => n.Typed == typed && n.Kind == kind);
		store.Names.Add(new Name(typed, kind, id, full));
		Save(store);
	}

	/// <summary>The id learned for these words, among the ids the save has now.</summary>
	public static string? IdFor(string typed, string kind, IEnumerable<string> ids)
	{
		var name = Load().Names.LastOrDefault(n => n.Typed == typed && n.Kind == kind);
		return name != null && ids.Contains(name.Id) ? name.Id : null;
	}
}
