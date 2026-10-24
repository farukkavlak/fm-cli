using System.Text.Json;
using FmCli;
using Xunit;

namespace FmCli.Tests;

public class JsonReadTests
{
	private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

	[Fact]
	public void TextReturnsTheStringOrEmpty()
	{
		var row = Parse("""{"Name":"Empoli","Number":3,"Missing":null}""");
		Assert.Equal("Empoli", JsonRead.Text(row, "Name"));
		Assert.Equal("", JsonRead.Text(row, "Number"));
		Assert.Equal("", JsonRead.Text(row, "Missing"));
		Assert.Equal("", JsonRead.Text(row, "Nope"));
	}

	[Fact]
	public void RawTextKeepsNumbersAndBooleans()
	{
		var row = Parse("""{"Number":3,"Ready":true}""");
		Assert.Equal("3", JsonRead.RawText(row, "Number"));
		Assert.Equal("True", JsonRead.RawText(row, "Ready"));
	}

	[Fact]
	public void NullableTextSeparatesAbsentFromEmpty()
	{
		var row = Parse("""{"Empty":"","Value":"x"}""");
		Assert.Null(JsonRead.NullableText(row, "Nope"));
		Assert.Equal("", JsonRead.NullableText(row, "Empty"));
		Assert.Equal("x", JsonRead.NullableText(row, "Value"));
	}
}
