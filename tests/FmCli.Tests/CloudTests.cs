using System.Text.Json.Nodes;
using FmCli.Nl;
using Xunit;

namespace FmCli.Tests;

/// <summary>Smart mode (T207): what our code takes from a cloud model's answer.</summary>
public class CloudTests
{
	private static Router.Call Call(string tool, List<string>? ask = null) => new(tool, [], ask ?? [], 0);

	[Fact]
	public void The_answer_is_read_from_json_around_it_and_only_an_offered_tool_counts()
	{
		HashSet<string> offered = ["fm_squad", "fm_table"];
		Assert.Equal("fm_squad", Cloud.Read("```json\n{\"tool\": \"fm_squad\", \"args\": {}}\n```", offered)!.Tool);
		Assert.Null(Cloud.Read("{\"tool\": null}", offered)!.Tool);
		Assert.Null(Cloud.Read("{\"tool\": \"fm_release\"}", offered));
		Assert.Null(Cloud.Read("no idea", offered));
	}

	[Fact]
	public void A_name_is_taken_only_when_the_sentence_gives_it()
	{
		var said = Call("fm_player", ["name"]);
		Cloud.Merge(said, new JsonObject { ["name"] = "rinaldi" }, "rinaldiye bak");
		Assert.Equal("rinaldi", said.Args["name"]!.GetValue<string>());
		Assert.Empty(said.Ask);

		var made = Call("fm_player", ["name"]);
		Cloud.Merge(made, new JsonObject { ["name"] = "hrelja" }, "bu kim");
		Assert.False(made.Args.ContainsKey("name"));
		Assert.Equal(["name"], made.Ask);
	}

	[Fact]
	public void A_choice_that_changes_the_game_is_asked_not_taken()
	{
		var match = Call("fm_match", ["mode"]);
		Cloud.Merge(match, new JsonObject { ["mode"] = "instant" }, "maca cik");
		Assert.False(match.Args.ContainsKey("mode"));
		Assert.Equal(["mode"], match.Ask);
	}

	[Fact]
	public void The_yes_is_never_the_models()
	{
		var call = Call("fm_saves");
		Cloud.Merge(call, new JsonObject { ["delete"] = "scn-calm", ["confirm"] = true }, "scn-calm kaydini sil");
		Assert.Equal("scn-calm", call.Args["delete"]!.GetValue<string>());
		Assert.False(call.Args.ContainsKey("confirm"));
	}

	[Fact]
	public void A_value_that_turns_a_read_into_a_weighty_act_needs_the_sentence()
	{
		var call = Call("fm_saves");
		Cloud.Merge(call, new JsonObject { ["delete"] = "scn-calm" }, "kayitlari goster");
		Assert.False(call.Args.ContainsKey("delete"));
	}

	[Fact]
	public void A_value_of_the_wrong_kind_is_dropped()
	{
		var call = Call("fm_saves");
		Cloud.Merge(call, new JsonObject { ["unknown"] = "x", ["delete"] = 5 }, "5 sil");
		Assert.Empty(call.Args);
	}
}
