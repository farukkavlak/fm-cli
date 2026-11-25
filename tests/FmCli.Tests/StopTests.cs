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
