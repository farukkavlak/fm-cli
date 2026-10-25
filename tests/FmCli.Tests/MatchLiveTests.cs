using FmCli;
using Xunit;

namespace FmCli.Tests;

public class MatchLiveTests
{
	[Theory]
	[InlineData("GOL Empoli FC!")]
	[InlineData("Goal for Pescara")]
	[InlineData("sarı kart: Tosto")]
	[InlineData("Red card for the defender")]
	[InlineData("Elia sakatlandı")]
	[InlineData("Substitution: Elia off, Korać on")]
	[InlineData("Penaltı noktası")]
	public void KeyMomentsAreKept(string line) => Assert.True(MatchLive.KeyMoment(line));

	[Theory]
	[InlineData("Kısa pas ile orta sahaya geçiyor")]
	[InlineData("Short pass in midfield")]
	[InlineData("Kale vuruşu")]
	public void OrdinaryLinesAreNot(string line) => Assert.False(MatchLive.KeyMoment(line));
}
