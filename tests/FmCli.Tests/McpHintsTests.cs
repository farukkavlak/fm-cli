using FmCli;
using Xunit;

namespace FmCli.Tests;

public class McpHintsTests
{
	[Theory]
	[InlineData("To sign: contract --sign --yes", "To sign: fm_contract sign=true")]
	[InlineData("Finish or leave it first: contract --cancel", "Finish or leave it first: fm_contract cancel=true")]
	[InlineData("To send it: staff fire \"Rossi\" --mutual --yes", "To send it: fm_staff_fire name=\"Rossi\" mutual=true confirm=true")]
	[InlineData("Waiting in a team talk. To speak: talk   (to skip the talk: match --no-talk)", "Waiting in a team talk. To speak: fm_talk   (to skip the talk: fm_match mode=\"play_no_talk\")")]
	[InlineData("To answer: talk   ·   to go on without answering: next --skip", "To answer: fm_talk   ·   to go on without answering: fm_next skip=true")]
	[InlineData("Under which name?  save --as \"<name>\" --yes", "Under which name?  fm_save as=\"<name>\" confirm=true")]
	[InlineData("Which one:  stop --save   or   stop --no-save", "Which one:  fm_stop save=true   or   fm_stop save=false")]
	public void PutsHintsInToolTerms(string printed, string expected) =>
		Assert.Equal(expected, Mcp.InToolTerms(printed));

	[Theory]
	[InlineData("To change: captains --captain <player> · --vice <player>", "fm_captains", "To change: fm_captains captain=<player> · vice=<player>")]
	[InlineData("An age range takes two numbers:  --age 18-23", "fm_players", "An age range takes two numbers:  age=\"18-23\"")]
	[InlineData("Not among the promises: {0} — to confirm the promises and go on, add --next.", "fm_contract", "Not among the promises: {0} — to confirm the promises and go on, add next=true.")]
	public void ReadsALoneFlagAsTheToolsOwn(string printed, string tool, string expected) =>
		Assert.Equal(expected, Mcp.InToolTerms(printed, tool));

	[Theory]
	[InlineData("'{0}' sends the player away; the suggested way: release <player>", "'{0}' sends the player away; the suggested way: fm_release player=<player>")]
	[InlineData("The squad numbers screen opened. To see them: numbers", "The squad numbers screen opened. To see them: fm_numbers")]
	public void TakesNoProseForAValue(string printed, string expected) =>
		Assert.Equal(expected, Mcp.InToolTerms(printed));

	[Theory]
	[InlineData("The game carries out a mutual termination without asking.")]
	[InlineData("for all of them: fm players --json or a wider terminal")]
	[InlineData("Not sent. To send it: the same command, with --yes at the end")]
	public void LeavesWhatDoesNotReadBack(string printed) =>
		Assert.Equal(printed, Mcp.InToolTerms(printed));
}
