using System.Reflection;
using System.Text.Json;
using FmCli;
using Xunit;

namespace FmCli.Tests;

public class PromptStatusTests
{
	private static void Look(string date, int? days, params (string Id, bool Priority, bool CanRespond, bool Responded)[] unread)
	{
		var program = typeof(FmCli.Program);
		var record = program.GetNestedType("NewsLook", BindingFlags.NonPublic)!;
		var messages = unread.ToDictionary(m => m.Id, m => JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, string>
		{
			["UniqueId"] = m.Id, ["NewsItemIsPriority"] = m.Priority.ToString(), ["CanRespond"] = m.CanRespond.ToString(),
			["NewsHasResponded"] = m.Responded.ToString(),
		})).RootElement.Clone());
		var look = Activator.CreateInstance(record, "team", date, "60", messages, new Dictionary<string, JsonElement>(), days);
		program.GetField("newsBefore", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, look);
	}

	private static string Status() => FmCli.Program.PromptStatus();

	[Fact]
	public void DateAndDaysToTheMatch()
	{
		Look("2028-10-22", 3);
		Assert.StartsWith("22 ", Status());
		Assert.Contains("3", Status().Split(" · ")[1]);
		Assert.Equal(2, Status().Split(" · ").Length);
	}

	[Fact]
	public void OnlyPriorityUnansweredMessagesWait()
	{
		Look("2028-10-22", null, ("1", true, true, false), ("2", false, true, false), ("3", true, true, true), ("4", true, false, false));
		var parts = Status().Split(" · ");
		Assert.Equal(2, parts.Length);
		Assert.StartsWith("1 ", parts[1]);
	}

	[Fact]
	public void NothingWithoutASave()
	{
		typeof(FmCli.Program).GetField("newsBefore", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
		Assert.Equal("", Status());
	}
}
