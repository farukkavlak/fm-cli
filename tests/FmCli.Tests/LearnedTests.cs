using FmCli;
using FmCli.Nl;
using Xunit;

namespace FmCli.Tests;

/// <summary>The learning router's store (T206): what a choice keeps, what puts it right, names by id.</summary>
public sealed class LearnedTests : IDisposable
{
	private readonly string _file = Path.Combine(Path.GetTempPath(), $"fm-learned-{Guid.NewGuid():N}.json");

	public LearnedTests() => Environment.SetEnvironmentVariable("FM_LEARNED", _file);

	public void Dispose()
	{
		Environment.SetEnvironmentVariable("FM_LEARNED", null);
		File.Delete(_file);
	}

	private static Learned.Sentence S(string text, string tool) => new(text, text, tool, tool);

	[Fact]
	public void A_choice_is_kept_when_the_next_command_is_another_matter()
	{
		Learned.Chose(S("kim yorgun", "fm_squad"), ["health", "table"]);
		Assert.Empty(Learned.Load().Sentences);
		Learned.Next("next");
		var store = Learned.Load();
		Assert.Null(store.Pending);
		Assert.Equal("fm_squad", Assert.Single(store.Sentences).Tool);
	}

	[Fact]
	public void A_choice_put_right_by_the_next_command_is_forgotten()
	{
		Learned.Chose(S("kim yorgun", "fm_squad"), ["health", "table"]);
		Learned.Next("health");
		var store = Learned.Load();
		Assert.Null(store.Pending);
		Assert.Empty(store.Sentences);
	}

	[Fact]
	public void A_new_choice_keeps_the_one_before_and_the_same_sentence_is_held_once()
	{
		Learned.Chose(S("kim yorgun", "fm_squad"), ["health"]);
		Learned.Chose(S("tablo nasil", "fm_table"), ["league"]);
		Learned.Next(null);
		Learned.Chose(S("kim yorgun", "fm_health"), ["squad"]);
		Learned.Next("table");
		var store = Learned.Load();
		Assert.Equal(2, store.Sentences.Count);
		Assert.Equal("fm_health", store.Sentences.Single(s => s.Text == "kim yorgun").Tool);
	}

	[Fact]
	public void Looking_at_what_was_learned_settles_nothing()
	{
		Learned.Chose(S("kim yorgun", "fm_squad"), ["health"]);
		Learned.Next("learned");
		Assert.NotNull(Learned.Load().Pending);
	}

	[Fact]
	public void No_file_no_learning()
	{
		Learned.Next("squad");
		Assert.False(File.Exists(_file));
	}

	[Fact]
	public void A_name_picked_between_two_is_held_by_the_id_while_he_is_in_the_save()
	{
		Learned.Picked("ali", SaveName.Player, "6", "Ali Yılmaz");
		Assert.Equal("6", Learned.IdFor("ali", SaveName.Player, ["5", "6"]));
		Assert.Null(Learned.IdFor("ali", SaveName.Player, ["5"]));
		Assert.Null(Learned.IdFor("ali", SaveName.Club, ["6"]));
	}

	[Fact]
	public void A_learned_name_answers_the_question_between_two()
	{
		List<SaveName> save = [new(SaveName.Player, "5", "Ali Yılmaz"), new(SaveName.Player, "6", "Ali Yılmaz")];
		Learned.Picked("ali", SaveName.Player, "6", "Ali Yılmaz");
		Assert.Equal("Ali Yılmaz #6", Program.Named("ali", save, byId: true));
	}
}
