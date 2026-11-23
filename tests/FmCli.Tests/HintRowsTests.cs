using FmCli;
using Xunit;

namespace FmCli.Tests;

public class HintRowsTests
{
	[Fact]
	public void AShortLineStaysWhole() =>
		Assert.Equal(["Why: happiness <player>"], Ui.HintRows("Why: happiness <player>", 79));

	[Theory]
	[InlineData("Details: jobs <club|#>   ·   apply: jobs <club> apply   ·   declare interest: jobs <club> interest   ·   every job: jobs --all")]
	[InlineData("To change: training <player> --focus \"<focus>\" · --role \"<role>\" · --position \"<position>><role>\"  (a wrong value lists the options)")]
	[InlineData("Diğer mevkiler: planner --position <biri> GK = KL, DR = D (Sğ), DCR = D (M), DCL = D (M), DL = D (Sl), DMR = DOS, DML = DOS, AMR = OOS (Sğ), AMC = OOS (M)")]
	public void ALongLineFitsAndKeepsEveryWord(string line)
	{
		var rows = Ui.HintRows(line, 79).ToList();
		Assert.True(rows.Count > 1);
		Assert.All(rows, r => Assert.True(r.Length <= 79, r));
		Assert.All(rows.Skip(1), r => Assert.StartsWith("  ", r));
		var words = (string s) => s.Replace("·", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
		Assert.Equal(words(line), rows.SelectMany(words));
	}

	[Fact]
	public void PartsBreakWhereTheyMeet()
	{
		var rows = Ui.HintRows("To delegate: responsibilities delegate \"<duty>\"   ·   to someone in particular: responsibilities give \"<duty>\" <person>   ·   to take back: responsibilities take \"<duty>\"", 79).ToList();
		Assert.Equal("To delegate: responsibilities delegate \"<duty>\"", rows[0]);
		Assert.StartsWith("  to someone in particular:", rows[1]);
	}
}
