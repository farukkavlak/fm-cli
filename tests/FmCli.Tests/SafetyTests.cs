using System.Text.Json;
using System.Text.Json.Nodes;
using FmCli;
using Xunit;

namespace FmCli.Tests;

/// <summary>
/// T153: nothing that changes the game for good runs without a yes. One table (Mcp.Impact,
/// next to each tool) is what MCP tells its host and what the sentence layer asks about.
/// </summary>
public class SafetyTests
{
	private static readonly string[] Gates = ["confirm", "yes", "send", "sign"];

	public static IEnumerable<object[]> WeightyTools() =>
		Mcp.Impacts().Where(t => t.Impact.Effect == Mcp.Effect.Weighty).Select(t => new object[] { t.Name });

	private static (string Name, Mcp.Impact Impact, JsonObject Schema) Tool(string name) => Mcp.Impacts().Single(t => t.Name == name);

	/// <summary>
	/// A high-impact tool does its work only when a call says so: an argument that brings it
	/// about, a yes of its own (confirm, yes, send, sign), or an argument it cannot go without.
	/// A bare call then only shows, or is refused.
	/// </summary>
	[Theory]
	[MemberData(nameof(WeightyTools))]
	public void AHighImpactToolNeedsToBeToldTo(string name)
	{
		var (_, impact, schema) = Tool(name);
		var properties = (schema["properties"] as JsonObject)?.Select(p => p.Key).ToHashSet() ?? [];
		var required = (schema["required"] as JsonArray)?.Count ?? 0;
		Assert.True(impact.When.Length > 0 || properties.Overlaps(Gates) || required > 0, $"{name}: a bare call would act");
	}

	[Theory]
	[MemberData(nameof(WeightyTools))]
	public void TheArgumentsThatBringItAboutExist(string name)
	{
		var (_, impact, schema) = Tool(name);
		foreach (var when in impact.When)
			Assert.NotNull(schema["properties"]?[when.Split('=')[0]]);
	}

	[Fact]
	public void EveryToolTellsTheHostWhatItDoes()
	{
		foreach (var (name, impact, _) in Mcp.Impacts())
		{
			var hints = Mcp.Annotations(impact);
			Assert.Equal(impact.Effect == Mcp.Effect.Read, hints["readOnlyHint"]!.GetValue<bool>());
			Assert.Equal(impact.Effect == Mcp.Effect.Weighty, hints["destructiveHint"]!.GetValue<bool>());
		}
	}

	// The sentence layer asks before a high-impact call and only then.
	[Theory]
	[InlineData("fm_offer", """{"name":"Rinaldi","fee":"2M"}""", true)]
	[InlineData("fm_release", """{"player":"Rinaldi","free":true}""", true)]
	[InlineData("fm_stop", """{"save":true}""", true)]
	[InlineData("fm_jobs", """{"club":"Ajax","action":"apply"}""", true)]
	[InlineData("fm_jobs", """{"club":"Ajax"}""", false)]
	[InlineData("fm_reply", """{"message":"3"}""", false)]
	[InlineData("fm_reply", """{"message":"3","answer":"1"}""", true)]
	[InlineData("fm_offers", """{"offer":"1","answer":"view"}""", false)]
	[InlineData("fm_offers", """{"offer":"1","answer":"accept"}""", true)]
	[InlineData("fm_transferroom", """{"action":"wanted"}""", false)]
	[InlineData("fm_transferroom", """{"action":"offer","advert":"2"}""", true)]
	[InlineData("fm_saves", """{}""", false)]
	[InlineData("fm_saves", """{"delete":"old"}""", true)]
	[InlineData("fm_tactic_set", """{"setting":"mentality","value":"Positive"}""", false)]
	[InlineData("fm_next", """{}""", false)]
	[InlineData("fm_match", """{"mode":"instant"}""", false)]
	[InlineData("fm_vision", """{}""", false)]
	[InlineData("fm_vision", """{"action":"confirm"}""", true)]
	public void TheSentenceLayerAsksBeforeHighImpactCalls(string tool, string arguments, bool asks) =>
		Assert.Equal(asks, Program.IsWeighty(new Nl.Router.Call(tool, JsonNode.Parse(arguments)!.AsObject(), [], 1)));

	[Theory]
	[InlineData("fm_status", """{}""", "Read")]
	[InlineData("fm_tactic_set", """{"setting":"mentality"}""", "Read")]
	[InlineData("fm_tactic_set", """{"setting":"mentality","value":"Positive"}""", "Change")]
	[InlineData("fm_talk", """{}""", "Read")]
	[InlineData("fm_talk", """{"say":"2"}""", "Play")]
	[InlineData("fm_numbers", """{"auto":false}""", "Read")]
	[InlineData("fm_numbers", """{"auto":true}""", "Weighty")]
	[InlineData("fm_nothing", """{}""", "Weighty")]
	public void ACallsEffectFollowsItsArguments(string tool, string arguments, string effect) =>
		Assert.Equal(effect, Mcp.EffectOf(tool, JsonNode.Parse(arguments)!.AsObject()).ToString());

	// Without `save`, fm_stop ran `stop --no-save`: the game closed and the progress was lost.
	[Theory]
	[InlineData("fm_stop", """{}""")]
	[InlineData("fm_load", """{"name":"career"}""")]
	[InlineData("fm_holiday", """{}""")]
	[InlineData("fm_press", """{"label":""}""")]
	public async Task AToolCallWithoutWhatItRequiresRunsNothing(string tool, string arguments)
	{
		var ran = new List<string[]>();
		var call = JsonDocument.Parse($$"""{"name":"{{tool}}","arguments":{{arguments}}}""").RootElement;
		var result = await Mcp.Call(call, words => { ran.Add(words); return Task.FromResult(0); });
		Assert.Empty(ran);
		Assert.True(result!["isError"]!.GetValue<bool>());
	}

	[Fact]
	public async Task AToolCallWithWhatItRequiresRuns()
	{
		var ran = new List<string[]>();
		var call = JsonDocument.Parse("""{"name":"fm_stop","arguments":{"save":true}}""").RootElement;
		await Mcp.Call(call, words => { ran.Add(words); return Task.FromResult(0); });
		Assert.Equal(["stop", "--save"], Assert.Single(ran));
	}
}
