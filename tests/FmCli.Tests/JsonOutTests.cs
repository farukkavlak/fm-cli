using FmCli;
using Xunit;

namespace FmCli.Tests;

public class JsonOutTests
{
	[Fact]
	public void KeepsTurkishLettersAndFieldNames()
	{
		var json = JsonOut.Of(new { Name = "Ağrı", Score = 3 });
		Assert.Contains("\"Name\":\"Ağrı\"", json);
		Assert.Contains("\"Score\":3", json);
	}
}
