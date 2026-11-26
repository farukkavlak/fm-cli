using FmCli;
using Xunit;

namespace FmCli.Tests;

/// <summary>A stop's answers (T214): which commands count as answering it.</summary>
public sealed class StopTests
{
	private static Program.HeldStop Offer() => new("offer", [],
	[
		("see", ["offers", "1", "view"]),
		("accept", ["offers", "1", "accept"]),
		("reject", ["offers", "1", "reject"]),
	]);

	[Fact]
	public void One_of_its_answers_answers_it() =>
		Assert.True(Program.Answers(Offer(), ["offers", "1", "reject"]));

	[Fact]
	public void The_same_command_with_something_else_said_answers_it() =>
		Assert.True(Program.Answers(Offer(), ["offers", "2", "stall"]));

	[Fact]
	public void The_bare_list_is_a_look_not_an_answer() =>
		Assert.False(Program.Answers(Offer(), ["offers"]));

	[Fact]
	public void A_question_is_not_an_answer() =>
		Assert.False(Program.Answers(Offer(), ["squad"]));

	[Fact]
	public void A_one_word_answer_is_answered_by_that_word()
	{
		var talk = new Program.HeldStop("talk", [], [("go on", ["talk"])]);
		Assert.True(Program.Answers(talk, ["talk"]));
		Assert.False(Program.Answers(talk, ["table"]));
	}
}

/// <summary>
/// A sentence at a stop held to its answers (T215), without the model: by place and by the game's own words.
/// What is to be done is the model's to read; it is scored by `fm __stops tests/nl/stops/cases.tsv`.
/// </summary>
public sealed class StopAnswerTests
{
	static StopAnswerTests() => Environment.SetEnvironmentVariable("FM_NL_MODEL", "/nonexistent/fm-nl-model");

	private static Program.HeldStop Offers() => new("offer", [],
	[
		("Fenerbahçe → Mauro Rinaldi · €2.5M", ["offers", "2"]),
		("Galatasaray → Mauro Rinaldi · €3M", ["offers", "3"]),
		("Trabzonspor → Ante Sušak · €1M", ["offers", "4"]),
	]);

	private static Program.HeldStop OneOffer() => new("offer", ["Fenerbahçe → Mauro Rinaldi (€2.5M)"],
	[
		("see the offer", ["offers", "2", "view"]),
		("accept it", ["offers", "2", "accept"]),
		("stall", ["offers", "2", "stall"]),
		("reject it", ["offers", "2", "reject"]),
	]);

	private static Program.HeldStop Messages() => new("message", [],
	[
		("Board Confidence Update", ["reply", "id:11"]),
		("Youth Intake Preview", ["reply", "id:12"]),
	]);

	private static List<int>? Said(Program.HeldStop stop, string sentence) => Program.AnswersSaid(stop, sentence)?.Places;

	[Theory]
	[InlineData("ikincisi", 1)]
	[InlineData("the last one", 3)]
	[InlineData("3", 2)]
	[InlineData("4'ü", 3)]
	public void A_place_in_the_list(string sentence, int answer) =>
		Assert.Equal([answer], Said(OneOffer(), sentence));

	[Fact]
	public void A_number_past_the_end_is_no_place() =>
		Assert.Null(Said(OneOffer(), "ilk 11 i göster"));

	[Theory]
	[InlineData("trabzonun teklifi", 2)]
	[InlineData("galatasaray", 1)]
	[InlineData("fenerin teklifini kabul et", 0)]
	[InlineData("sušak", 2)]
	[InlineData("susak", 2)]
	[InlineData("ilkini aç", 0)]
	public void An_offer_is_said_by_its_club_or_player(string sentence, int answer) =>
		Assert.Equal([answer], Said(Offers(), sentence));

	[Fact]
	public void One_player_two_clubs_is_asked_between() =>
		Assert.Equal([0, 1], Said(Offers(), "rinaldi için gelen teklif"));

	[Theory]
	[InlineData("youth intake mesajını aç", 1)]
	[InlineData("board mesajı", 0)]
	[InlineData("ikinci mesajı oku", 1)]
	public void A_message_is_said_by_its_subject_or_place(string sentence, int answer) =>
		Assert.Equal([answer], Said(Messages(), sentence));

	[Theory]
	[InlineData("who is injured?")]
	[InlineData("rinaldiyi sat")]
	[InlineData("kadroyu göster")]
	[InlineData("oyunu kapat")]
	public void A_question_or_something_else_is_no_answer(string sentence) =>
		Assert.Null(Said(Messages(), sentence));
}
