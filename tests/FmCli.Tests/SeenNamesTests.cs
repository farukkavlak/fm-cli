using FmCli.Nl;
using Xunit;

namespace FmCli.Tests;

/// <summary>Names read on a page a sentence cannot open (the shortlist), kept per club and save.</summary>
public sealed class SeenNamesTests : IDisposable
{
	private readonly string _file = Path.Combine(Path.GetTempPath(), $"fm-seen-{Guid.NewGuid():N}.json");

	public SeenNamesTests() => Environment.SetEnvironmentVariable("FM_SEEN_NAMES", _file);

	public void Dispose()
	{
		Environment.SetEnvironmentVariable("FM_SEEN_NAMES", null);
		File.Delete(_file);
	}

	[Fact]
	public void Each_save_keeps_its_own_and_a_new_reading_replaces_the_old()
	{
		SeenNames.Keep("shortlist", "1|a", [new(SaveName.Player, "9", "Hilterman"), new(SaveName.Club, "5", "Vitesse")]);
		SeenNames.Keep("shortlist", "1|b", [new(SaveName.Club, "6", "Köln")]);
		Assert.Equal(["Hilterman", "Vitesse"], SeenNames.At("1|a").Select(n => n.Name));
		Assert.Equal(["Köln"], SeenNames.At("1|b").Select(n => n.Name));

		SeenNames.Keep("shortlist", "1|a", []);
		Assert.Empty(SeenNames.At("1|a"));
		Assert.Single(SeenNames.At("1|b"));
	}

	[Fact]
	public void Nothing_without_a_club_and_save()
	{
		SeenNames.Keep("shortlist", "", [new(SaveName.Player, "9", "Hilterman")]);
		Assert.Empty(SeenNames.At(""));
		Assert.False(File.Exists(_file));
	}
}
