using FmCli;
using Xunit;

namespace FmCli.Tests;

public class MoneyFormatTests
{
	[Theory]
	[InlineData("£2.5M", 2_500_000)]
	[InlineData("£2,5M", 2_500_000)]
	[InlineData("673B £", 673_000_000_000)]
	[InlineData("£92,122", 92_122)]
	[InlineData("-£1.5K", -1_500)]
	public void ParsesTheDefaultMoneyForms(string text, long expected) =>
		Assert.Equal(expected, MoneyFormat.Parse(text));

	[Theory]
	[InlineData("")]
	[InlineData("hello")]
	[InlineData("£")]
	[InlineData("£1.2.3M")]
	public void RejectsNonMoney(string text) =>
		Assert.Null(MoneyFormat.Parse(text));

	[Fact]
	public void FindsAnAmountInsideASentence()
	{
		Assert.Equal("10,5B £", MoneyFormat.Find("Vergi sonrası 10,5B £ aylık"));
		Assert.Null(MoneyFormat.Find("no amount here"));
	}

	[Fact]
	public void ShortAndExactUseTheDefaultTemplates()
	{
		Assert.Equal("£1.5M", MoneyFormat.Short(1_500_000));
		Assert.Equal("£673B", MoneyFormat.Short(673_000_000_000));
		Assert.Equal("£92,122", MoneyFormat.Exact(92_122));
	}
}
