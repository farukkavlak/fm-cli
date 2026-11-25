using System.Text.Json.Nodes;
using FmCli.Nl;
using Xunit;

namespace FmCli.Tests;

/// <summary>T210: notes kept per save, numbers kept for good, nothing kept without a yes.</summary>
public class MemoryTests
{
	[Fact]
	public void ANoteKeepsItsNumberWhenAnotherGoes()
	{
		var book = new Memory.Book();
		Memory.Write(book, "play the young ones", "wish", "2035-08-01", "user");
		var second = Memory.Write(book, "sign a left back in January", "plan", "2035-08-01", "user");
		book.Notes.RemoveAt(0);
		var third = Memory.Write(book, "never sell the captain", "rule", "2035-08-02", "user");
		Assert.Equal(2, second.N);
		Assert.Equal(3, third.N);

		var put = Memory.Write(book, "sign a left back in the summer", "plan", "2035-08-03", "user", replaces: 2);
		Assert.Equal(2, put.N);
		Assert.Equal(2, book.Notes.Count);
		Assert.Equal("sign a left back in the summer", book.Notes.Single(n => n.N == 2).Text);
	}

	[Fact]
	public void AKindNotKnownIsAWish() =>
		Assert.Equal("wish", Memory.Write(new Memory.Book(), "x", "fact", "", "assistant").Kind);

	[Theory]
	[InlineData("", null, "empty")]
	[InlineData("   ", null, "empty")]
	[InlineData("ok", 9, "no note #9")]
	[InlineData("ok", null, null)]
	public void ANoteIsChecked(string text, int? replaces, string? fault)
	{
		var book = new Memory.Book();
		Memory.Write(book, "one", "wish", "", "user");
		var said = Memory.NotANote(text, book, replaces);
		if (fault == null) Assert.Null(said);
		else Assert.Contains(fault, said);
		Assert.NotNull(Memory.NotANote(new string('a', Memory.MaxLength + 1), book));
	}

	[Fact]
	public void TheBookHasRoom()
	{
		var book = new Memory.Book();
		for (var i = 0; i < Memory.MaxNotes; i++) Memory.Write(book, $"n{i}", "wish", "", "user");
		Assert.Contains("full", Memory.NotANote("one more", book));
		Assert.Null(Memory.NotANote("in place of one", book, replaces: 1));
	}

	[Fact]
	public void OnlyNotesTheBookHoldsAreCited()
	{
		var book = new Memory.Book();
		Memory.Write(book, "a", "wish", "", "user");
		Memory.Write(book, "b", "wish", "", "user");
		Assert.Equal([2], Memory.Cited("We keep him (#2); not #7, and #2 again.", book).Select(n => n.N));
		Assert.Empty(Memory.Cited("nothing", book));
	}

	[Fact]
	public void TheModelIsToldNotesAreNotFacts()
	{
		Assert.Equal("", Memory.ForModel(new Memory.Book()));
		var book = new Memory.Book();
		Memory.Write(book, "Rinaldi stays", "rule", "2035-08-01", "user");
		var told = Memory.ForModel(book);
		Assert.Contains("#1 [rule, 2035-08-01] Rinaldi stays", told);
		Assert.Contains("not facts about the game", told);
	}

	[Fact]
	public void BooksAreKeptPerSave()
	{
		var file = Path.Combine(Path.GetTempPath(), $"fm-memory-{Guid.NewGuid()}.json");
		Environment.SetEnvironmentVariable("FM_MEMORY", file);
		try
		{
			var a = new Memory.Book();
			Memory.Write(a, "in save a", "wish", "", "user");
			Assert.True(Memory.Keep("1|a", a));
			var b = new Memory.Book();
			Memory.Write(b, "in save b", "wish", "", "user");
			Assert.True(Memory.Keep("1|b", b));
			Assert.Equal("in save a", Memory.Of("1|a").Notes.Single().Text);
			Assert.Equal("in save b", Memory.Of("1|b").Notes.Single().Text);
			Assert.Empty(Memory.Of("2|a").Notes);
			Assert.False(Memory.Keep("", a));

			a.Notes.Clear();
			Assert.True(Memory.Keep("1|a", a));
			Assert.Empty(Memory.Of("1|a").Notes);
			Assert.Single(Memory.Of("1|b").Notes);
		}
		finally
		{
			Environment.SetEnvironmentVariable("FM_MEMORY", null);
			File.Delete(file);
		}
	}

	/// <summary>A model that calls keep_note first, then answers.</summary>
	private sealed class Keeper(string call, string args, string answer) : IChatModel
	{
		public JsonArray? Told;
		private int turn;
		public string Name => "keeper";
		public Task<ChatReply> Next(JsonArray messages, JsonArray tools, double seconds, int tokens = 1200)
		{
			Told = messages;
			return Task.FromResult(turn++ == 0
				? new ChatReply("", [new ToolCall("c1", call, (JsonObject)JsonNode.Parse(args)!)], new JsonObject { ["role"] = "assistant" }, null, 0)
				: new ChatReply(answer, [], null, null, 0));
		}
	}

	private static async Task<(Ask.Result Result, JsonArray Told)> Talk(Keeper model, Memory.Book book, Func<Ask.Offer, Ask.Offered>? offer)
	{
		var file = Path.Combine(Path.GetTempPath(), $"fm-ask-{Guid.NewGuid()}.json");
		Environment.SetEnvironmentVariable("FM_ASK_FILE", file);
		try
		{
			var result = await Ask.Answer(model, "remember: Rinaldi stays", "1|a", "", true, _ => Task.FromResult(0), book: book, offer: offer);
			return (result, model.Told!);
		}
		finally
		{
			Environment.SetEnvironmentVariable("FM_ASK_FILE", null);
			File.Delete(file);
		}
	}

	private static string ToolSaid(JsonArray told) => told.Last(m => m?["role"]?.GetValue<string>() == "tool")!["content"]!.GetValue<string>();

	[Fact]
	public async Task ANoteOfferedIsKeptOnlyOnAYes()
	{
		var book = new Memory.Book();
		var (no, told) = await Talk(new Keeper("keep_note", """{"text":"Rinaldi stays","kind":"rule"}""", "ok"), book,
			o => new Ask.Offered(o, true, false, null));
		Assert.Empty(book.Notes);
		Assert.Contains("said no", ToolSaid(told));
		Assert.False(no.Offers.Single().Yes);

		var (notAsked, told2) = await Talk(new Keeper("keep_note", """{"text":"Rinaldi stays","kind":"rule"}""", "ok"), book,
			o => new Ask.Offered(o, false, false, null));
		Assert.Empty(book.Notes);
		Assert.Contains("memory add", ToolSaid(told2));

		var (_, told3) = await Talk(new Keeper("keep_note", """{"text":"Rinaldi stays","kind":"rule"}""", "ok"), book, null);
		Assert.Contains("cannot be kept", ToolSaid(told3));

		var (yes, told4) = await Talk(new Keeper("keep_note", """{"text":"Rinaldi stays","kind":"rule"}""", "Kept (#1)."), book,
			o => new Ask.Offered(o, true, true, Memory.Write(book, o.Text, o.Kind, "", "assistant")));
		Assert.Equal("Rinaldi stays", book.Notes.Single().Text);
		Assert.Contains("#1", ToolSaid(told4));
		Assert.Empty(yes.Cited); // the note just kept is told as kept, not as one the answer stands on
	}

	[Fact]
	public async Task AFaultyOfferIsNotPutToTheUser()
	{
		var book = new Memory.Book();
		var asked = 0;
		var (_, told) = await Talk(new Keeper("forget_note", """{"number":4}""", "ok"), book, o => { asked++; return new Ask.Offered(o, true, true, null); });
		Assert.Equal(0, asked);
		Assert.Contains("no note #4", ToolSaid(told));

		var (_, told2) = await Talk(new Keeper("keep_note", """{"text":"","kind":"wish"}""", "ok"), book, o => { asked++; return new Ask.Offered(o, true, true, null); });
		Assert.Equal(0, asked);
		Assert.Contains("empty", ToolSaid(told2));
	}

	[Fact]
	public async Task TheNotesGoToTheModel()
	{
		var book = new Memory.Book();
		Memory.Write(book, "Rinaldi stays", "rule", "2035-08-01", "user");
		var model = new Keeper("forget_note", """{"number":"#1"}""", "ok");
		var (result, told) = await Talk(model, book, o => new Ask.Offered(o, true, false, null));
		Assert.Contains("#1 [rule, 2035-08-01] Rinaldi stays", told[0]!["content"]!.GetValue<string>());
		Assert.Equal(1, result.Offers.Single().Offer.Forget);
	}
}
