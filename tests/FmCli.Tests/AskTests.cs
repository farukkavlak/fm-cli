using System.Text.Json.Nodes;
using FmCli;
using FmCli.Nl;
using Xunit;

namespace FmCli.Tests;

/// <summary>T208: the assistant is offered reads only, and a call that could act is refused before anything runs.</summary>
public class AskTests
{
	private static Task<int> NeverRun(string[] words) => throw new InvalidOperationException($"ran: {string.Join(" ", words)}");

	[Fact]
	public void EveryToolOfferedOnlyReadsWhateverItIsGiven()
	{
		foreach (var (name, _, schema) in Mcp.ReadTools())
		{
			// every argument shown, each set: the call still only reads
			var all = new JsonObject();
			foreach (var (arg, s) in schema["properties"] as JsonObject ?? [])
				all[arg] = s?["enum"] is JsonArray { Count: > 0 } e ? e[0]!.DeepClone() : s?["type"]?.GetValue<string>() == "boolean" ? true : "x";
			Assert.True(Mcp.EffectOf(name, all) == Mcp.Effect.Read, $"{name}: {all.ToJsonString()} does not only read");
		}
	}

	[Fact]
	public void NoToolThatActsIsOffered()
	{
		var offered = Mcp.ReadTools().Select(t => t.Name).ToHashSet();
		foreach (var name in new[] { "fm_propose", "fm_offer", "fm_next", "fm_save", "fm_release", "fm_tactic_pick", "fm_continue", "fm_pages", "fm_query" })
			Assert.DoesNotContain(name, offered);
		Assert.Contains("fm_squad", offered);
		// a tool that only acts with some arguments is offered without them
		var offers = Mcp.ReadTools().Single(t => t.Name == "fm_offers").Schema["properties"] as JsonObject;
		Assert.False(offers!.ContainsKey("answer"));
	}

	[Theory]
	[InlineData("fm_propose", """{"player":"Rinaldi"}""")]
	[InlineData("fm_next", "{}")]
	[InlineData("fm_offers", """{"answer":"accept"}""")]
	[InlineData("fm_offers", """{"answer":"negotiate"}""")]
	[InlineData("fm_inbox", """{"read_all":true}""")]
	[InlineData("fm_query", """{"path":"Human"}""")]
	[InlineData("fm_nothing", "{}")]
	public async Task ACallThatCouldActIsRefusedAndNothingRuns(string tool, string args)
	{
		var (text, ran) = await Mcp.RunRead(tool, (JsonObject)JsonNode.Parse(args)!, NeverRun);
		Assert.False(ran);
		Assert.StartsWith("Refused", text);
	}

	[Fact]
	public void ActCommandsAreWordsAUserTypes()
	{
		var acts = Mcp.ActCommands();
		Assert.Contains(acts, a => a.Words == "propose");
		Assert.DoesNotContain(acts, a => a.Words == "squad");
	}

	[Theory]
	[InlineData("En yorgun Mirko Sušak, kondisyonu %67.", "", new string[0])]
	[InlineData("En yorgun Mirko Sušak'ın sözleşmesi bitiyor.", "", new string[0])]
	[InlineData("En yorgun Mirko Sušak, yedeği Ahmet Yılmaz.", "", new[] { "Ahmet", "Yılmaz" })]
	[InlineData("Bunu `Propose` komutu yapar.", "", new string[0])]
	[InlineData("Sakat:\n- Erata 5 gün\n1. Hrelja", "", new string[0])]
	[InlineData("He is fit. Rinaldi is not.", "rinaldi", new string[0])]
	public void ANameNothingReadHoldsIsSaid(string answer, string question, string[] unread)
	{
		var seen = new[] { "Yorgun\n  Mirko Sušak %67\n  Damir Hrelja %69\nSakatlanan\n  Yusuf Erata", question };
		Assert.Equal(unread, Ask.Unread(answer, seen));
	}
}
