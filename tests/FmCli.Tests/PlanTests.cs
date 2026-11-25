using System.Text.Json.Nodes;
using FmCli;
using FmCli.Nl;
using Xunit;

namespace FmCli.Tests;

/// <summary>T209: a plan's steps are checked by our code, and a step counts only when its check shows it.</summary>
public class PlanTests
{
	private static JsonObject Json(string text) => (JsonObject)JsonNode.Parse(text)!;

	[Theory]
	[InlineData("fm_next", "{}")]
	[InlineData("fm_continue", "{}")]
	[InlineData("fm_save", """{"confirm":true}""")]
	[InlineData("fm_talk", """{"say":"1"}""")]
	[InlineData("fm_query", """{"path":"Human"}""")]
	[InlineData("fm_squad", "{}")]
	[InlineData("fm_inbox", "{}")]
	[InlineData("fm_tactic_set", """{"setting":"mentality","value":"2","dry":true}""")]
	[InlineData("fm_tactic_set", """{"setting":"mentality","value":"2","zzz":1}""")]
	[InlineData("fm_tactic_set", """{"value":"2"}""")]
	[InlineData("fm_nothing", "{}")]
	public void NotAStep(string tool, string args) => Assert.NotNull(Mcp.NotAStep(tool, Json(args)));

	[Theory]
	[InlineData("fm_tactic_set", """{"setting":"mentality","value":"2"}""")]
	[InlineData("fm_intensity", """{"player":"Rinaldi","level":"half"}""")]
	[InlineData("fm_inbox", """{"read_all":true}""")]
	[InlineData("fm_offer", """{"name":"Rinaldi","fee":"1M","send":true}""")]
	public void AStep(string tool, string args) => Assert.Null(Mcp.NotAStep(tool, Json(args)));

	[Fact]
	public void APlanWithAStepThatIsNotOneHasAFault()
	{
		var (steps, _, faults) = Plan.Proposed(Json("""
			{"steps":[
			  {"tool":"fm_tactic_set","arguments":{"setting":"mentality","value":"2"},"why":"w","check":{"tool":"fm_tactic","arguments":{}},"expect":"mentality 2"},
			  {"tool":"fm_next","arguments":{},"why":"w","check":{"tool":"fm_status","arguments":{}},"expect":"x"},
			  {"tool":"fm_intensity","arguments":{"player":"A","level":"half"},"why":"w","check":{"tool":"fm_offer","arguments":{"name":"A"}},"expect":"x"},
			  {"tool":"fm_intensity","arguments":{"player":"A","level":"half"},"why":"w","check":{"tool":"fm_intensity","arguments":{}},"expect":""}
			]}
			"""));
		Assert.Single(steps);
		Assert.Equal(3, faults.Count);
		Assert.StartsWith("step 2", faults[0]);
		Assert.StartsWith("step 3", faults[1]);
		Assert.StartsWith("step 4", faults[2]);
	}

	/// <summary>A model that gives each verdict in turn.</summary>
	private sealed class Judge(params string[] verdicts) : IChatModel
	{
		private int next;
		public string Name => "judge";
		public Task<ChatReply> Next(JsonArray messages, JsonArray tools, double seconds, int tokens = 1200) =>
			Task.FromResult(new ChatReply(verdicts[next++], [], null, null, 0));
	}

	/// <summary>A game whose inbox reads <paramref name="unread"/> until read_all runs, and after it the second value.</summary>
	private sealed class Game(params string[] reads)
	{
		public int Acts;
		public Task<int> Run(string[] words)
		{
			if (words.Contains("--read-all")) Acts++;
			else Console.WriteLine(reads[Math.Min(Acts, reads.Length - 1)]);
			return Task.FromResult(0);
		}
	}

	private static Plan.Step ReadAll() => new("fm_inbox", Json("""{"read_all":true}"""), "", "fm_inbox", [], "nothing unread");

	[Fact]
	public async Task AStepThatChangedAndHoldsIsDone()
	{
		var game = new Game("3 unread", "0 unread");
		var (ran, left) = await Plan.Run(new Judge("""{"holds":true,"seen":"0 unread"}"""), "g", [ReadAll()], _ => true, game.Run);
		Assert.Equal(Plan.Outcome.Done, Assert.Single(ran).Outcome);
		Assert.Empty(left);
		Assert.Equal(1, game.Acts);
	}

	// The game often takes a command and does nothing: a check that holds but did not change is not "done".
	[Fact]
	public async Task AStepThatChangedNothingIsAlreadySoOrNotDone()
	{
		var game = new Game("0 unread");
		var (ran, _) = await Plan.Run(new Judge("""{"holds":true,"seen":"0"}"""), "g", [ReadAll()], _ => true, game.Run);
		Assert.Equal(Plan.Outcome.AlreadySo, ran[0].Outcome);

		(ran, _) = await Plan.Run(new Judge("no verdict"), "g", [ReadAll()], _ => true, new Game("3 unread").Run);
		Assert.Equal(Plan.Outcome.NotDone, ran[0].Outcome);
	}

	[Fact]
	public async Task AStepNotShownToHoldStopsTheRest()
	{
		var game = new Game("3 unread", "2 unread");
		var (ran, left) = await Plan.Run(new Judge("""{"holds":false,"seen":"2 unread"}"""), "g", [ReadAll(), ReadAll()], _ => true, game.Run);
		Assert.Equal(Plan.Outcome.NotDone, Assert.Single(ran).Outcome);
		Assert.Single(left);
		Assert.Equal(1, game.Acts);
	}

	[Fact]
	public async Task AStepNotAllowedRunsNothing()
	{
		var game = new Game("3 unread");
		var (ran, left) = await Plan.Run(new Judge(), "g", [ReadAll()], _ => false, game.Run);
		Assert.Empty(ran);
		Assert.Single(left);
		Assert.Equal(0, game.Acts);
	}

	// A long read was cut for the model and the row the step changed was past the cut (round80, TR).
	[Fact]
	public void TheChangedLinesAreGivenWhateverTheCut()
	{
		var rows = Enumerable.Range(0, 500).Select(i => $"player {i}: normal").ToList();
		var before = string.Join("\n", rows);
		rows[480] = "player 480: half";
		var changed = Plan.Changed(before, string.Join("\n", rows));
		Assert.Contains("player 480: normal", changed);
		Assert.Contains("player 480: half", changed);
		Assert.DoesNotContain("player 1:", changed);
		Assert.Contains("same", Plan.Changed(before, before));
	}
}
