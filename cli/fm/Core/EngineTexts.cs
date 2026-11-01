using System.Buffers.Binary;
using System.Text;

namespace FmCli;

/// <summary>
/// The engine's own texts: what the simulation writes — a person's menu, a training
/// session's name, a personality, a scout's verdict — is not in the UI's translation table the plugin
/// carries. The game keeps them in languages.fmf: zstd frames of 4 KB, and inside, for each language a
/// block ("ctl.", its name, a pool of texts, then an index of id → place in the pool). An id is the same
/// in every language, so the English of a text shown in another language is the English block's text
/// under the same id. The table is written once per language to ~/.config/fm-cli/engine/&lt;language&gt;.tsv,
/// where the plugin adds it to its own.
/// </summary>
internal static class EngineTexts
{
	/// <summary>The game's language setting and the name its block goes by in the file.</summary>
	// The block a language's texts are in: FM26 named some in English ("Turkish"), FM27 names them all
	// in their own language ("Türkçe"); either is taken.
	private static readonly Dictionary<string, string[]> BlockNames = new(StringComparer.OrdinalIgnoreCase)
	{
		["turkish"] = ["Turkish", "Türkçe"], ["german"] = ["German", "Deutsch"], ["french"] = ["French", "Français"],
		["spanish"] = ["Spanish", "Español"], ["italian"] = ["Italiano"], ["portuguese"] = ["Português"], ["dutch"] = ["Nederlands"],
		["polish"] = ["Polski"], ["russian"] = ["Русский"], ["danish"] = ["Dansk"], ["norwegian"] = ["Norsk"], ["swedish"] = ["Svenska"],
		["greek"] = ["Ελληνικά"], ["korean"] = ["한국어"], ["japanese"] = ["日本語"], ["arabic"] = ["اَلْعَرَبِيَّةُ"],
		["chinese"] = ["简体中文"], ["simplified chinese"] = ["简体中文"], ["latin american spanish"] = ["Español latinoamericano"],
		["indonesian"] = ["Bahasa Indonesia"], ["english (us)"] = ["English (US)"], ["american english"] = ["English (US)"],
	};

	private static string Field(System.Text.Json.JsonElement element, string name) =>
		JsonRead.RawText(element, name);

	public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli", "engine");

	/// <summary>
	/// Writes this language's table if it is not there yet and has the plugin load it. Quiet and cheap
	/// when there is nothing to do: an English game, or the table written already.
	/// </summary>
	public static async Task Ensure(Bridge fm)
	{
		System.Text.Json.JsonElement engine;
		try { engine = await fm.GetJson("translate", ("engine", "1")); }
		catch { return; }
		var language = Field(engine, "Language");
		if (language.Length == 0 || language.StartsWith("english", StringComparison.OrdinalIgnoreCase) && language.Length <= 7) return;
		var target = Path.Combine(Folder, language + ".tsv");
		var file = Field(engine, "File");
		// The table is the game file's: one written from another game or version (FM26's, before an
		// update) is written again. Which file it came from is kept beside it.
		var stamp = target + ".stamp";
		var from = file.Length > 0 && File.Exists(file) ? $"{new FileInfo(file).Length}:{File.GetLastWriteTimeUtc(file).Ticks}" : "";
		if (File.Exists(target) && (from.Length == 0 || File.Exists(stamp) && File.ReadAllText(stamp).Trim() == from))
		{
			if (Field(engine, "Engine") == "0") await fm.Get("translate", ("engine", "1"), ("reload", "1"));
			return;
		}
		if (file.Length == 0 || !BlockNames.TryGetValue(language, out var blocks)) return;

		Console.Error.WriteLine(L($"The game engine's texts ({blocks[^1]}) are being extracted, once…"));
		Dictionary<string, string> pairs;
		try { pairs = Build(file, blocks); }
		catch (Exception e)
		{
			// The texts are a help, never a reason for the command to fail; nor to try again on every one.
			Console.Error.WriteLine(L($"The engine's texts could not be extracted: {e.Message}"));
			pairs = [];
		}
		Directory.CreateDirectory(Folder);
		if (pairs.Count == 0)
		{
			await File.WriteAllTextAsync(stamp, from);
			return;
		}
		var temp = target + ".tmp";
		await File.WriteAllLinesAsync(temp, pairs.Select(p => $"{p.Key}\t{p.Value}"));
		File.Move(temp, target, overwrite: true);
		await File.WriteAllTextAsync(stamp, from);
		await fm.Get("translate", ("engine", "1"), ("reload", "1"));
	}

	/// <summary>
	/// The pitch positions' codes as the engine writes them ("AMC"), by their ids: the same ids in every
	/// language, so a position comes out as the game writes it in its own ("OOSM" in Turkish, "OMZ" in German).
	/// AML stands apart from the other thirteen in FM27's file.
	/// </summary>
	private static readonly (string Code, ulong Id)[] PositionCodes =
	[
		("GK", 269119505), ("DR", 269119504), ("DL", 269119502), ("DC", 269119503), ("WBR", 269119501), ("WBL", 269119499),
		("DM", 269119500), ("MR", 269119498), ("ML", 269119496), ("MC", 269119497), ("AMR", 269119495), ("AML", 268868848),
		("AMC", 269119493), ("ST", 269119494),
	];

	private static Dictionary<string, string>? positions;

	/// <summary>The codes <see cref="Positions"/> has read in this run; empty until then, and in an English game.</summary>
	public static IReadOnlyDictionary<string, string> KnownPositions => positions ?? [];

	/// <summary>
	/// The game's codes for the pitch positions in its language, by the English ones ("AMC" → "OOSM"); empty in
	/// an English game. A code whose id no longer holds that code in the English block (a game update moved it)
	/// is left out, and the English code is shown. Read from the game file once per language and kept.
	/// </summary>
	public static async Task<IReadOnlyDictionary<string, string>> Positions(Bridge fm)
	{
		if (positions != null) return positions;
		positions = [];
		System.Text.Json.JsonElement engine;
		try { engine = await fm.GetJson("translate", ("engine", "1")); }
		catch { return positions; }
		var language = Field(engine, "Language");
		var file = Field(engine, "File");
		if (language.Length == 0 || language.StartsWith("english", StringComparison.OrdinalIgnoreCase) && language.Length <= 7) return positions;
		if (!BlockNames.TryGetValue(language, out var blocks) || file.Length == 0 || !File.Exists(file)) return positions;

		var target = Path.Combine(Folder, language + ".positions.tsv");
		var stamp = target + ".stamp";
		var from = $"{new FileInfo(file).Length}:{File.GetLastWriteTimeUtc(file).Ticks}";
		if (File.Exists(target) && File.Exists(stamp) && File.ReadAllText(stamp).Trim() == from)
		{
			foreach (var line in File.ReadAllLines(target))
				if (line.Split('\t') is [var code, var shown]) positions[code] = shown;
			GameWords.Keep(positions.Values);
			return positions;
		}
		try
		{
			var english = new Dictionary<ulong, string>();
			var shownIn = new Dictionary<ulong, string>();
			foreach (var (name, first, table) in Blocks(Unpack(File.ReadAllBytes(file))))
			{
				var into = name == "English" ? english : blocks.Contains(name) ? shownIn : null;
				if (into == null) continue;
				foreach (var (id, text) in table) into[((ulong)(first == 0 ? 1 : 0) << 32) | id] = text;
			}
			foreach (var (code, id) in PositionCodes)
				if (english.GetValueOrDefault(id) == code && shownIn.GetValueOrDefault(id) is { Length: > 0 } shown) positions[code] = shown;
			Directory.CreateDirectory(Folder);
			await File.WriteAllLinesAsync(target, positions.Select(p => $"{p.Key}\t{p.Value}"));
			await File.WriteAllTextAsync(stamp, from);
		}
		catch (Exception) { positions.Clear(); }
		GameWords.Keep(positions.Values);
		return positions;
	}

	/// <summary>Each text of a language's blocks against the English blocks' text under the same id.</summary>
	private static Dictionary<string, string> Build(string file, string[] language)
	{
		var data = Unpack(File.ReadAllBytes(file));
		var english = new Dictionary<ulong, string>();
		var shown = new Dictionary<ulong, string>();
		foreach (var (name, first, table) in Blocks(data))
		{
			var into = name == "English" ? english : language.Contains(name) ? shown : null;
			if (into == null) continue;
			// The two blocks of a language (first 1001 and first 0) number their texts apart.
			foreach (var (id, text) in table) into[((ulong)(first == 0 ? 1 : 0) << 32) | id] = text;
		}
		var pairs = new Dictionary<string, string>();
		var counts = new Dictionary<string, Dictionary<string, int>>();
		foreach (var (key, text) in shown)
		{
			if (!english.TryGetValue(key, out var en) || text == en) continue;
			// A text built from parts ("[%male#1] left") is never shown as it is; only plain ones are kept.
			if (text.Contains("[%", StringComparison.Ordinal) || en.Contains("[%", StringComparison.Ordinal)
			    || text.Contains('\t') || text.Contains('\n') || en.Contains('\n')) continue;
			// A code of a few letters ("D", "M", "OOS") stands for different things in different places
			// ("D" is a defender and a draw); going back from it guesses.
			if (text.Length < 4) continue;
			if (!counts.TryGetValue(text, out var seen)) counts[text] = seen = [];
			seen[en] = seen.GetValueOrDefault(en) + 1;
		}
		// One shown text can stand for several English ones ("Dengeli": Balanced, Even); the commonest is given.
		foreach (var (text, seen) in counts) pairs[text] = seen.MaxBy(s => s.Value).Key;
		return pairs;
	}

	/// <summary>
	/// The file's zstd frames after its header, one after another; a skippable frame between them (an
	/// index, magic 0x184D2A5E and its length) is passed over.
	/// </summary>
	private static unsafe byte[] Unpack(byte[] file)
	{
		var at = file.AsSpan().IndexOf((ReadOnlySpan<byte>)[0x28, 0xB5, 0x2F, 0xFD]);
		if (at < 0) return [];
		using var output = new MemoryStream();
		using var decompressor = new ZstdSharp.Decompressor();
		fixed (byte* start = file)
		{
			while (at + 8 <= file.Length)
			{
				var magic = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at));
				if ((magic & 0xFFFFFFF0) == 0x184D2A50)
				{
					at += 8 + (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 4));
					continue;
				}
				if (magic != 0xFD2FB528) break;
				var size = ZstdSharp.Unsafe.Methods.ZSTD_findFrameCompressedSize(start + at, (nuint)(file.Length - at));
				if (ZstdSharp.Unsafe.Methods.ZSTD_isError(size)) break;
				output.Write(decompressor.Unwrap(file.AsSpan(at, (int)size)));
				at += (int)size;
			}
		}
		return output.ToArray();
	}

	/// <summary>Each language block: its name, its first id, and its texts by id (the first of a record's variants).</summary>
	private static IEnumerable<(string Name, uint First, List<(uint Id, string Text)> Table)> Blocks(byte[] data)
	{
		var starts = new List<int>();
		ReadOnlySpan<byte> mark = [0x03, 0x01, (byte)'c', (byte)'t', (byte)'l', (byte)'.'];
		for (var at = data.AsSpan().IndexOf(mark); at >= 0;)
		{
			starts.Add(at);
			var next = data.AsSpan(at + 6).IndexOf(mark);
			at = next < 0 ? -1 : at + 6 + next;
		}
		starts.Add(data.Length);
		for (var b = 0; b + 1 < starts.Count; b++)
		{
			int start = starts[b], end = starts[b + 1];
			var p = start + 8;
			var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p));
			var nameLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 8));
			p += 12;
			var name = Encoding.UTF8.GetString(data, p, nameLength);
			p += nameLength;
			var first = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p));
			var pool = p + 4;
			// The pool ends where the header's size says; the index follows: a count, then id and place.
			var index = start + 8 + (int)size;
			if (index + 4 > end) continue;
			var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(index));
			if (count <= 0 || index + 4 + count * 8L > end) continue;
			var entries = new (uint Key, uint Offset)[count];
			var least = uint.MaxValue;
			for (var k = 0; k < count; k++)
			{
				var key = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(index + 4 + k * 8));
				var offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(index + 8 + k * 8));
				entries[k] = (key, offset);
				least = Math.Min(least, offset);
			}
			var table = new List<(uint, string)>(count);
			foreach (var (key, offset) in entries)
			{
				var q = pool + (long)(offset - least);
				if (q < pool || q + 5 > index) continue;
				var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)q + 1));
				if (length < 0 || q + 5 + length > index) continue;
				table.Add((key, Encoding.UTF8.GetString(data, (int)q + 5, length)));
			}
			yield return (name, first, table);
		}
	}
}
