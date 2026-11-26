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

/// <summary>A sentence at a stop held to its answers (T215): by place, by what is done, by the game's own words.</summary>
public sealed class StopAnswerTests
{
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

	private static Program.HeldStop Match() => new("match", [],
		[("play the match", ["match", "--play"]), ("straight to the result", ["match", "--instant"])]);

	private static Program.HeldStop Messages() => new("message", [],
	[
		("Board Confidence Update", ["reply", "id:11"]),
		("Youth Intake Preview", ["reply", "id:12"]),
	]);

	private static List<int>? Said(Program.HeldStop stop, string sentence) => Program.AnswersSaid(stop, sentence)?.Places;

	[Theory]
	[InlineData("kabul et", 1)]
	[InlineData("tamam kabul", 1)]
	[InlineData("reddet", 3)]
	[InlineData("hayır istemiyorum", 3)]
	[InlineData("beklet şimdilik", 2)]
	[InlineData("teklife bakalım", 0)]
	[InlineData("accept it", 1)]
	[InlineData("turn it down", 3)]
	[InlineData("let it wait", 2)]
	[InlineData("ikincisi", 1)]
	[InlineData("the last one", 3)]
	public void What_is_done_picks_the_answer_that_does_it(string sentence, int answer) =>
		Assert.Equal([answer], Said(OneOffer(), sentence));

	[Theory]
	[InlineData("maçı canlı oynayalım", 0)]
	[InlineData("izle", 0)]
	[InlineData("direkt sonuç", 1)]
	[InlineData("hızlı geç sonuca", 1)]
	[InlineData("play it", 0)]
	[InlineData("just the result", 1)]
	public void Match_day_by_what_is_done(string sentence, int answer) =>
		Assert.Equal([answer], Said(Match(), sentence));

	[Theory]
	[InlineData("trabzonun teklifi", 2)]
	[InlineData("galatasaray", 1)]
	[InlineData("fenerin teklifini kabul et", 0)]
	[InlineData("sušak", 2)]
	[InlineData("ilkini aç", 0)]
	public void An_offer_is_said_by_its_club_or_player(string sentence, int answer) =>
		Assert.Equal([answer], Said(Offers(), sentence));

	[Fact]
	public void A_name_is_no_word_for_doing() =>
		Assert.Equal(["stall"], Program.AnswersSaid(new Program.HeldStop("offer", [], [("Başakşehir FK → Baray Yenilmez · €34K", ["offers", "6"])]),
			"Başakşehir FK Baray Yenilmez teklifini beklet")!.Done);

	[Fact]
	public void One_player_two_clubs_is_asked_between() =>
		Assert.Equal([0, 1], Said(Offers(), "rinaldi için gelen teklif"));

	[Fact]
	public void What_to_do_is_kept_for_the_offer_it_leads_to() =>
		Assert.Contains("accept", Program.AnswersSaid(Offers(), "fenerin teklifini kabul et")!.Done);

	[Theory]
	[InlineData("kim yorgun")]
	[InlineData("kabul edelim mi")]
	[InlineData("who is injured?")]
	[InlineData("rinaldiyi sat")]
	[InlineData("kadroyu göster")]
	public void A_question_or_something_else_goes_to_the_router(string sentence) =>
		Assert.Null(Said(Messages(), sentence));

	[Theory]
	[InlineData("oyunu kapat")]
	[InlineData("maçı oyna")]
	[InlineData("kabul et")]
	public void Nothing_said_of_the_messages_is_no_answer(string sentence) =>
		Assert.Null(Said(Messages(), sentence));

	[Theory]
	[InlineData("youth intake mesajını aç", 1)]
	[InlineData("board mesajı", 0)]
	[InlineData("ikinci mesajı oku", 1)]
	public void A_message_is_said_by_its_subject_or_place(string sentence, int answer) =>
		Assert.Equal([answer], Said(Messages(), sentence));

	[Fact]
	public void Opening_with_several_messages_asks_which() =>
		Assert.Equal([0, 1], Said(Messages(), "mesajı aç"));

	[Fact]
	public void A_kind_of_answer_the_stop_has_not_is_no_answer() =>
		Assert.Null(Said(Match(), "reddet"));
}
