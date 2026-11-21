using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// Commands print what to type next ("To sign: contract --sign --yes"). Through MCP that is no
/// help: the caller sends tool arguments, not flags. So the hints are put in the tools' terms
/// ("To sign: fm_contract sign=true"), learnt from the tools themselves: each tool is asked what
/// words every argument becomes, and a hint is read back through that. A hint that does not read
/// back whole (a flag no tool has, more words than the command takes) is left as it was.
/// </summary>
internal static partial class Mcp
{
	/// <summary>
	/// What a tool's words mean: the command words it starts with (and the arguments those stand
	/// for, as "scout new" stands for action=new), the words of each argument (flags, a flag with
	/// a value, a word of a choice), and the bare values in order.
	/// </summary>
	private sealed record Reading(string Tool, string[] Verb, string[] Fixed, List<(string[] Flags, string Arg, string? Value, bool TakesValue)> Options, List<string> Positional);

	private static readonly Lazy<List<Reading>> Readings = new(() => [.. Tools.SelectMany(Learn).OrderByDescending(r => r.Verb.Length)]);

	private static string Mark(int n) => $"\u0001{n}";

	/// <summary>
	/// Asks the tool what it types for its arguments, alone and together: every value argument
	/// gets a marker, every flag and choice is tried in turn on top. "--json", which the tools add
	/// for themselves, is not part of what a user types.
	/// </summary>
	private static IEnumerable<Reading> Learn(Tool tool)
	{
		var props = JsonDocument.Parse(tool.Schema).RootElement.TryGetProperty("properties", out var p) ? p.EnumerateObject().ToList() : [];
		List<string>? Words(Dictionary<string, object> args)
		{
			try { return [.. tool.Words(JsonSerializer.SerializeToElement(args)).Where(w => w.Length > 0 && w != "--json")]; }
			catch { return null; }
		}
		string Type(JsonProperty prop) => prop.Value.TryGetProperty("type", out var t) ? t.GetString() ?? "string" : "string";
		List<object> Choices(JsonProperty prop) => Type(prop) == "boolean" ? [true, false]
			: prop.Value.TryGetProperty("enum", out var e) ? [.. e.EnumerateArray().Select(v => (object)v.GetString()!)] : [];
		// A free value with named examples (fm_squad view=fitness): each example's own words are learnt as well.
		List<object> Examples(JsonProperty prop) =>
			prop.Value.TryGetProperty("examples", out var x) ? [.. x.EnumerateArray().Select(v => (object)v.GetString()!)] : [];
		var values = props.Where(x => Choices(x).Count == 0).Select((x, i) => (x.Name, Value: Type(x) == "array" ? (object)new[] { Mark(i) } : Type(x) == "integer" ? 700 + i : Mark(i))).ToList();
		string? ValueArg(string w) => values.FirstOrDefault(v => w == (v.Value is string[] a ? a[0] : v.Value.ToString())).Name;
		string Say(object choice) => choice is bool b ? (b ? "true" : "false") : $"\"{choice}\"";

		// the starting points: nothing, each value alone, all values, and each choice that changes the command words
		var bases = new List<Dictionary<string, object>> { new() };
		bases.AddRange(values.Select(v => new Dictionary<string, object> { [v.Name] = v.Value }));
		if (values.Count > 1) bases.Add(values.ToDictionary(v => v.Name, v => v.Value));
		foreach (var prop in props)
			foreach (var c in Choices(prop))
			{
				bases.Add(new() { [prop.Name] = c });
				if (values.Count > 0) bases.Add(new(values.ToDictionary(v => v.Name, v => v.Value)) { [prop.Name] = c });
			}

		var learnt = new Dictionary<string, Reading>();
		var bareVerb = Words([])?.TakeWhile(w => !w.StartsWith('-')).ToArray() ?? [];
		foreach (var b in bases)
		{
			var words = Words(b);
			if (words is not { Count: > 0 }) continue;
			var verb = words.TakeWhile(w => !w.StartsWith('-') && ValueArg(w) == null).ToArray();
			// a choice that makes its own command words ("scout new") is said by them
			var fixedArgs = b.Where(kv => Choices(props.First(x => x.Name == kv.Key)).Count > 0 && !verb.SequenceEqual(bareVerb))
				.Select(kv => $"{kv.Key}={Say(kv.Value)}").ToArray();
			if (verb.Length == 0) continue;
			var key = string.Join(' ', verb);
			if (!learnt.TryGetValue(key, out var r))
				learnt[key] = r = new Reading(tool.Name, verb, fixedArgs, [], []);
			var positional = words.Skip(verb.Length).Select((w, i) => (w, i)).Where(x => ValueArg(x.w) != null && (x.i == 0 || !words[verb.Length + x.i - 1].StartsWith("--")))
				.Select(x => ValueArg(x.w)!).ToList();
			if (positional.Count > r.Positional.Count) { r.Positional.Clear(); r.Positional.AddRange(positional); }
			for (var i = verb.Length + 1; i < words.Count; i++)
				if (ValueArg(words[i]) is { } arg && words[i - 1].StartsWith("--"))
					r.Options.Add(([words[i - 1]], arg, null, true));
			foreach (var prop in props.Where(x => !b.ContainsKey(x.Name)))
			{
				var tried = Choices(prop).Concat(Examples(prop)).Select(c => (Choice: c, Words: Words(new(b) { [prop.Name] = c }))).Where(x => x.Words != null && x.Words.Take(verb.Length).SequenceEqual(verb)).ToList();
				// what every choice of this argument types is the command's, not the choice's ("match --play")
				var shared = tried.Count > 1 ? tried.Select(x => (IEnumerable<string>)x.Words!).Aggregate((x, y) => x.Intersect(y)).ToHashSet() : [];
				// set against the other choices only from a plain start, and when every choice keeps the command words
				var compare = !b.Keys.Any(k => Choices(props.First(x => x.Name == k)).Count > 0) && tried.Count == Choices(prop).Count;
				foreach (var (choice, said) in tried)
				{
					var added = said!.ToList();
					foreach (var w in words) added.Remove(w);
					var own = !compare ? [] : said!.Skip(verb.Length).Select((w, i) => (w, i))
						.Where(x => !shared.Contains(x.w) && ValueArg(x.w) == null && !(verb.Length + x.i + 1 < said!.Count && ValueArg(said[verb.Length + x.i + 1]) != null))
						.Select(x => x.w).ToList();
					foreach (var f in new[] { added, own }.Where(f => f.Count > 0 && f.All(w => ValueArg(w) == null)))
						r.Options.Add(([.. f], prop.Name, Say(choice), false));
				}
			}
		}
		foreach (var r in learnt.Values)
		{
			// a flag that is a choice of its own does not take the value after it ("contract --cancel <name>")
			var alone = r.Options.Where(o => !o.TakesValue && o.Flags.Length == 1).Select(o => o.Flags[0]).ToHashSet();
			r.Options.RemoveAll(o => o.TakesValue && alone.Contains(o.Flags[0]));
			var distinct = r.Options.DistinctBy(o => string.Join(' ', o.Flags) + "|" + o.Arg + "|" + o.Value + "|" + o.TakesValue).OrderByDescending(o => o.Flags.Length).ToList();
			r.Options.Clear();
			r.Options.AddRange(distinct);
			yield return r;
		}
	}

	/// <summary>
	/// Every command hint in a command's output, in the tools' terms; a flag on its own ("· --vice
	/// &lt;player&gt;") is read as the given tool's, the one that printed it.
	/// </summary>
	public static string InToolTerms(string text, string? tool = null)
	{
		text = Hint().Replace(text, m =>
		{
			var all = Token().Matches(m.Groups["cmd"].Value).Select(t => t.Value).ToList();
			// the command may start a few words in ("or stop --no-save")
			for (var from = 0; from < all.Count; from++)
			{
				var words = all[from..];
				var hasFlag = words.Any(w => w.StartsWith("--"));
				var asked = from == 0 && m.Groups["lead"].Success;
				foreach (var r in Readings.Value)
					if (Read(r, words, asked) is { } call && (hasFlag || asked || words.Count > r.Verb.Length))
						return m.Groups["pre"].Value + string.Join(' ', [.. all[..from], call]) + m.Groups["post"].Value;
			}
			return m.Value;
		});
		if (tool == null) return text;
		var own = Readings.Value.Where(r => r.Tool == tool).SelectMany(r => r.Options).OrderByDescending(o => o.Flags.Length).ToList();
		return LoneFlag().Replace(text, m =>
		{
			var words = Token().Matches(m.Value).Select(t => t.Value).ToList();
			foreach (var o in own.Where(o => o.Flags.Length == 1 && o.Flags[0] == words[0]))
			{
				if (o.TakesValue && words.Count == 2 && IsValue(words[1], true)) return $"{o.Arg}={(words[1][0] is '<' or '"' ? words[1] : $"\"{words[1]}\"")}";
				if (!o.TakesValue) return $"{o.Arg}={o.Value}" + (words.Count == 2 ? " " + words[1] : "");
			}
			return m.Value;
		});
	}

	/// <summary>A word standing for a value: a placeholder, a quote, a slot; any word when the hint is asked for.</summary>
	private static bool IsValue(string w, bool asked) =>
		!w.StartsWith('[') && !w.StartsWith("--") && (asked || w[0] is '<' or '"' or '{' || char.IsDigit(w[0]));

	private static string? Read(Reading r, List<string> words, bool asked)
	{
		var at = words.FirstOrDefault() == "fm" ? 1 : 0;
		if (words.Count - at < r.Verb.Length || !words.Skip(at).Take(r.Verb.Length).SequenceEqual(r.Verb)) return null;
		at += r.Verb.Length;
		var args = new List<string>(r.Fixed);
		var nextPositional = 0;
		while (at < words.Count)
		{
			var w = words[at];
			var hit = r.Options.FirstOrDefault(o =>
				at + o.Flags.Length + (o.TakesValue ? 1 : 0) <= words.Count && words.Skip(at).Take(o.Flags.Length).SequenceEqual(o.Flags));
			if (hit.Arg == null && w.StartsWith("--")) return null;
			if (hit.Arg != null)
			{
				at += hit.Flags.Length;
				if (hit.TakesValue)
				{
					if (!IsValue(words[at], true)) return null;
					args.Add($"{hit.Arg}={words[at++]}");
				}
				else args.Add($"{hit.Arg}={hit.Value}");
			}
			else
			{
				if (nextPositional >= r.Positional.Count || !IsValue(w, asked)) return null;
				args.Add($"{r.Positional[nextPositional++]}={w}");
				at++;
			}
		}
		return string.Join(' ', [r.Tool, .. args]);
	}

	// a command: from a known first word, words one space apart, up to a separator (two spaces,
	// a bracket, a middle dot, a semicolon or the end); "lead" when it follows a colon
	[GeneratedRegex("""(?<pre>(?<lead>:\s+)|^|(?<=[\s(]))(?<cmd>(?:fm )?[a-z][a-z_]*(?: (?:"[^"\n]*"|<[^>\n]*>|--[a-z][a-z-]*|[^\s"·();,:<>]+))*)(?<post>(?=\s{2,}|[)(·;,]|\s*$|\s+[·(—-]|\s+then\b|\s+or\b))""", RegexOptions.Multiline)]
	private static partial Regex Hint();

	// a flag no command word comes before, with the value after it if any
	[GeneratedRegex("""(?<=^|[\s(])--[a-z][a-z-]*(?: (?:"[^"\n]*"|<[^>\n]*>|[^\s"·();,|<>\[\]]+))?""")]
	private static partial Regex LoneFlag();

	[GeneratedRegex("""\"[^\"]*\"|<[^>]*>|\S+""")]
	private static partial Regex Token();
}
