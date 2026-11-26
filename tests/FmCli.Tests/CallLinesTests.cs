using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FmCli;
using FmCli.Nl;
using Xunit;

namespace FmCli.Tests;

public class CallLinesTests
{
	private static Dictionary<string, string> Catalog()
	{
		using var stream = typeof(Labels).Assembly.GetManifestResourceStream("labels-turkish.tsv")!;
		using var reader = new StreamReader(stream);
		var catalog = new Dictionary<string, string>();
		while (reader.ReadLine() is { } line)
			if (line.Split('\t') is [var key, var value]) catalog[key] = value;
		return catalog;
	}

	private static string Holes(string line) =>
		string.Join(" ", Regex.Matches(line, @"\{([a-z_]+)\}").Select(m => m.Groups[1].Value).Order());

	// A line left out of the catalog stays English in a Turkish game; a hole lost in it prints no value.
	[Fact]
	public void EveryLineIsTranslatedWithTheSameHoles()
	{
		var catalog = Catalog();
		var wrong = CallLines.All.Where(l => !catalog.TryGetValue(l.Line, out var tr) || Holes(tr) != Holes(l.Line)).Select(l => l.Line).ToList();
		Assert.Empty(wrong);
	}

	// Every key names a real tool and its own arguments, and every hole is an argument the key asks for.
	[Fact]
	public void EveryKeyHoldsARealToolAndItsArguments()
	{
		var wrong = new List<string>();
		foreach (var (key, line) in CallLines.All)
		{
			var parts = key.Split(' ');
			if (Mcp.Card(parts[0]) is not { } card) { wrong.Add(key); continue; }
			var props = JsonNode.Parse(card.Schema)?["properties"] as JsonObject ?? [];
			var named = parts.Skip(1).Select(p => p.Split('=')[0]).ToHashSet();
			if (named.Any(n => !props.ContainsKey(n)) || Holes(line).Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(h => !named.Contains(h))) wrong.Add(key);
		}
		Assert.Empty(wrong);
	}

	// The line is in the CLI's language, whichever this machine is set to.
	private static string As(string line, params (string Hole, string Value)[] values) =>
		values.Aggregate(Labels.Named(line), (text, v) => text.Replace($"{{{v.Hole}}}", v.Value));

	[Fact]
	public void TheMostSpecificLineWins()
	{
		string Choice(string arg, string value) => value;
		Assert.Equal(As("Makes the {position} slot's role {role}", ("position", "DL"), ("role", "Wing-Back")),
			CallLines.Of("fm_tactic_role", new JsonObject { ["position"] = "DL", ["role"] = "Wing-Back" }, Choice));
		Assert.Equal(As("Lists the roles for the {position} slot", ("position", "DL")), CallLines.Of("fm_tactic_role", new JsonObject { ["position"] = "DL" }, Choice));
		Assert.Equal(As("Goes on to match day"), CallLines.Of("fm_next", new JsonObject { ["until"] = "match" }, Choice));
		Assert.Equal(As("Goes on one step"), CallLines.Of("fm_next", new JsonObject { ["skip"] = false }, Choice));
		Assert.Null(CallLines.Of("fm_nothing", [], Choice));
	}

	[Fact]
	public void ATypedCommandIsItsLongestTool()
	{
		Assert.Equal("fm_tactic_role", Mcp.ToolForTyped(["tactic", "role", "DL"]));
		Assert.Equal("fm_tactic", Mcp.ToolForTyped(["tactic"]));
		Assert.Null(Mcp.ToolForTyped(["learned"]));
	}
}
