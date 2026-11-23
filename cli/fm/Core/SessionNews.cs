using System.Text.Json;

namespace FmCli;

/// <summary>
/// In the session, what a command brought: after one that moved the game's clock, the messages
/// that came in and the squad's injuries, returns, arrivals and departures, in a few lines above
/// the prompt. The clock only moves when a command moves it, so a look is taken after each
/// command, not on a timer; when the date and hour stand still that is one state read and
/// nothing more. Both lists are read from the data tree, without opening a page: the game's
/// unread messages (`Human.HumanPortalUnreadMessages`) and the squad (`Human.Team.Players`),
/// told apart by their own ids.
/// </summary>
internal static partial class Program
{
	private sealed record NewsLook(string Team, string Date, string Clock, Dictionary<string, JsonElement> Unread, Dictionary<string, JsonElement> Squad);

	private const int NewsShown = 5;

	// Messages a command already named itself (`next`'s "new:" lines), not named again.
	internal static readonly HashSet<string> MessagesSaid = [];

	private static NewsLook? newsBefore;

	/// <summary>Where things stand before a command; null when there is no save to look at.</summary>
	internal static async Task<bool> NewsBaseline()
	{
		MessagesSaid.Clear();
		newsBefore = await NewsNow(null);
		return newsBefore != null;
	}

	/// <summary>Says what the command brought, if it moved the clock, and keeps the new look for the next one.</summary>
	internal static async Task SayNews()
	{
		var before = newsBefore;
		if (before == null)
		{
			await NewsBaseline();
			return;
		}

		var now = await NewsNow(before);
		newsBefore = now;
		if (now == null || ReferenceEquals(now, before)) return;
		// Another save (load, continue): a different career, not news.
		if (now.Team != before.Team) return;

		var lines = new List<string>();

		var squadBefore = before.Squad;
		if (squadBefore.Count > 0 && now.Squad.Count > 0)
		{
			foreach (var (id, p) in now.Squad)
			{
				if (!squadBefore.TryGetValue(id, out var was))
				{
					lines.Add(L($"joined: {Field(p, "Name")}"));
					continue;
				}
				var hurt = Field(p, "IsInjured") == "True";
				if (hurt && Field(was, "IsInjured") != "True")
				{
					var days = int.TryParse(Field(p, "CurrentInjury.InjuryReturnDays"), out var d) && d > 0 ? L($", {d} days") : "";
					var what = Field(p, "CurrentInjury.InjuryDescription");
					lines.Add(what.Length > 0 || days.Length > 0
						? L($"injured: {Field(p, "Name")} ({what}{days})")
						: L($"injured: {Field(p, "Name")}"));
				}
				else if (!hurt && Field(was, "IsInjured") == "True") lines.Add(L($"back from injury: {Field(p, "Name")}"));
			}
			foreach (var (id, p) in squadBefore)
				if (!now.Squad.ContainsKey(id)) lines.Add(L($"left: {Field(p, "Name")}"));
		}

		// Those waiting for an answer first, then the newest: the game's ids grow with each message.
		var arrived = now.Unread.Where(m => !before.Unread.ContainsKey(m.Key) && !MessagesSaid.Contains(m.Key))
			.Select(m => m.Value)
			.OrderByDescending(Blocking).ThenByDescending(Id)
			.ToList();
		foreach (var m in arrived.Take(NewsShown))
			lines.Add(L($"new: {Field(m, "NewsTitleString")}{(Blocking(m) ? L("  (awaiting an answer)") : "")}"));
		if (arrived.Count > NewsShown) lines.Add(L($"{arrived.Count - NewsShown} more new messages: inbox"));

		if (lines.Count == 0) return;
		foreach (var line in lines)
			if (Ui.Rich) Ui.Line($"  [dim]·[/] {Ui.Safe(line)}");
			else Console.WriteLine($"  · {line}");
	}

	/// <summary>
	/// The game as the news sees it. The same look back when the clock has not moved since
	/// <paramref name="before"/>: then nothing can have come in. Null when there is nothing to read.
	/// </summary>
	private static async Task<NewsLook?> NewsNow(NewsLook? before)
	{
		try
		{
			var state = await Fm.GetJson("state");
			var date = Field(state, "Date");
			if (date.Length == 0) return null;
			var clock = Field(state, "Clock");
			if (before != null && before.Date == date && before.Clock == clock) return before;

			var team = Field(await Fm.Pull("Human.Team", "UniqueId"), "UniqueId");
			var unread = await Fm.Pull("Human.HumanPortalUnreadMessages", "UniqueId,NewsTitleString,CanRespond,NewsHasResponded,NewsItemIsPriority", list: true);
			var squad = await Fm.Pull("Human.Team.Players", "UniqueId,Name,IsInjured,CurrentInjury.InjuryDescription,CurrentInjury.InjuryReturnDays", list: true);
			return new NewsLook(team, date, clock, ById(unread), ById(squad));
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			return null;
		}

		static Dictionary<string, JsonElement> ById(JsonElement list) =>
			list.ValueKind != JsonValueKind.Array ? []
				: list.EnumerateArray().Where(e => Field(e, "UniqueId").Length > 0)
					.GroupBy(e => Field(e, "UniqueId")).ToDictionary(g => g.Key, g => g.First().Clone());
	}
}
