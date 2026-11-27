using FmCli;
using FmCli.Nl;
using Xunit;

namespace FmCli.Tests;

public class NameFitTests
{
	private static readonly List<SaveName> Save =
	[
		new(SaveName.Player, "1", "Emir Dadük"),
		new(SaveName.Player, "2", "Kerem Kalafat"),
		new(SaveName.Player, "3", "Mustafa Şengül"),
		new(SaveName.Player, "4", "Emirhan Topçu"),
		new(SaveName.Player, "5", "Ali Yılmaz"),
		new(SaveName.Player, "6", "Ali Yılmaz"),
		new(SaveName.Player, "7", "Kerem Aktürkoğlu"),
		new(SaveName.Club, "10", "Pendikspor"),
		new(SaveName.Club, "11", "Frosinone"),
	];

	[Theory]
	[InlineData("dadukun", "Emir Dadük")]       // a Turkish ending
	[InlineData("sengule", "Mustafa Şengül")]
	[InlineData("kalaftı", "Kerem Kalafat")]    // a typo with an ending
	[InlineData("kalafat", "Kerem Kalafat")]
	[InlineData("emir", "Emir Dadük")]          // every word of his name typed beats a start of Emirhan's
	[InlineData("emirhan", "Emirhan Topçu")]
	[InlineData("kerem kalafat", "Kerem Kalafat")]
	[InlineData("pendikspor kalafat", "Kerem Kalafat")]   // the club's word is not his name
	[InlineData("frosinonenin rinaldi", "rinaldi")]       // not ours: left for the game's search, without the club
	[InlineData("kalafatin bilgilerini", "Kerem Kalafat")]   // a word the reader took in that is no name
	[InlineData("zzzz", "zzzz")]
	public void PutsTheNameAsTheSaveHasIt(string typed, string want) =>
		Assert.Equal(want, Program.Named(typed, Save));

	[Fact]
	public void TwoAsGoodAreNotGuessedWithoutAPerson() =>
		// "kerem" fits two; with nobody to ask, the words stay as typed and the command asks in its own way
		Assert.Equal("kerem", Program.Named("kerem", Save));

	[Fact]
	public void TwoOfOneNameAreToldApartByTheIdWhereThePageOpens()
	{
		Assert.Equal(["5", "6"], NameFit.Who(["yilmaz"], Save, SaveName.Player).Select(k => k.Id));
	}

	[Fact]
	public void TheFirstLetterHolds() => Assert.Equal(0, NameFit.Fit("final", "rinaldi"));

	[Fact]
	public void AStartOfThreeLettersFits() => Assert.True(NameFit.Fit("ali", "alioski") > 0);

	[Theory]
	[InlineData("rizesporunkine", "rizespor", true)]
	[InlineData("samsunsporunkini", "samsunspor", true)]
	[InlineData("rinaldininki", "rinaldi", true)]
	[InlineData("rizesporki", "rizespor", false)]
	[InlineData("rizesporxyz", "rizespor", false)]
	public void EndingsStack(string typed, string word, bool fits) => Assert.Equal(fits, NameFit.Fit(typed, word) == 3);

	[Theory]
	[InlineData("dadukun mutlulugu", "rinaldi mutlulugu")]
	[InlineData("pendikspor kalafat hakkinda bilgi", "rinaldi hakkinda bilgi")]
	[InlineData("sengul ile kalafati karsilastir", "rinaldi ile tosto karsilastir")]
	[InlineData("ali ne durumda", null)]          // only the start of a name
	[InlineData("kadroyu goster", null)]
	[InlineData("emir ver", null)]               // a common word on its own
	[InlineData("emir daduk nasil", "rinaldi nasil")]
	public void TheModelSeesANameItWasTaught(string typed, string? seen) =>
		Assert.Equal(seen, NameFit.StandIn(typed, Save));
}
