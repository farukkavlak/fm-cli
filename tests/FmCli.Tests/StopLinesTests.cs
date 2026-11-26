using FmCli;
using Xunit;

namespace FmCli.Tests;

public class StopLinesTests
{
	// A line is held whole and by its clauses, split at punctuation only (no words of any language).
	[Fact]
	public void ALineIsHeldWholeAndByItsClauses() =>
		Assert.Equal(
			["Benden vazgeçmelerini anlıyorum, hiçbir şekilde kin beslemem", "Benden vazgeçmelerini anlıyorum", "hiçbir şekilde kin beslemem"],
			Program.Clauses("Benden vazgeçmelerini anlıyorum, hiçbir şekilde kin beslemem"));

	[Fact]
	public void AShortPieceIsNoClause() =>
		Assert.Equal(["Yes, of course."], Program.Clauses("Yes, of course."));
}
