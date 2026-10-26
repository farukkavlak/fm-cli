using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// The league's transfer window: open with so many days left, or closed.
	/// The window's record is bound on the first read, so it is read until it answers.
	/// </summary>
	private static async Task<string> TransferWindow()
	{
		try
		{
			var window = await Fm.Pull("Human.Team.Division.TransferWindowData", "TransferWindowStatus,TransferWindowDaysRemaining");
			var status = Field(window, "TransferWindowStatus");
			if (status.Length == 0) return null;
			if (status != "1") return L("window closed");
			return int.TryParse(Field(window, "TransferWindowDaysRemaining"), out var days) ? L($"window open, {days} days left") : L("window open");
		}
		catch (FmError) { return null; }
	}

	/// <summary>
	/// "Club · neutral ground · Friendly · Monday 7 July 14:00 (7 days)", read from
	/// the team's next fixture without opening a screen. The fixture's own date is a packed
	/// value, so the day is counted on from today by the distance the game gives.
	/// </summary>
	private static async Task<string> FixtureVenue()
	{
		try { return Field(await Fm.Pull("Human.Team.NextFixture", "MatchVenueShort"), "MatchVenueShort"); }
		catch (FmError) { return ""; }
	}

	private static async Task<string> NextMatch(string today)
	{
		try
		{
			var fixture = await Fm.Pull("Human.Team.NextFixture", "KickoffTime,MatchVenueShort,FixtureDateDistanceInDays,IsFutureMatch");
			if (Field(fixture, "IsFutureMatch") != "True") return null;

			var opponent = Field(await Fm.Pull("Human.Team.NextFixture.TeamOpponent", "Name"), "Name");
			var competition = Field(await Fm.Pull("Human.Team.NextFixture.Competition", "Name"), "Name");
			var venue = Field(fixture, "MatchVenueShort") switch { "H" => L("home"), "A" => L("away"), "N" => L("neutral ground"), var other => other };

			var days = int.TryParse(Field(fixture, "FixtureDateDistanceInDays"), out var d) ? d : -1;
			var when = days switch { 0 => L("today"), 1 => L("tomorrow"), > 1 => L($"in {days} days"), _ => "" };
			var day = DateTime.TryParseExact(today, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var now) && days >= 0
				? $"{now.AddDays(days).Day} {Months[now.AddDays(days).Month - 1]} {Days[(int)now.AddDays(days).DayOfWeek]} "
				: "";

			var parts = new[] { opponent, venue, competition, $"{day}{Field(fixture, "KickoffTime")}".Trim() }.Where(p => p.Length > 0);
			return string.Join(" · ", parts) + (when.Length > 0 ? $" ({when})" : "");
		}
		catch (FmError)
		{
			// An older plugin has no /pull; the status is still worth giving without it.
			return null;
		}
	}

	/// <summary>
	/// The team's last results, oldest first, as the game keeps them: points per match, three
	/// for a win, one for a draw. Drawn as coloured letters.
	/// </summary>
	private static async Task<string> Form()
	{
		try
		{
			var points = await Fm.Numbers("Human.Team.RecentFormValuesAllComps");
			return string.Join(" ", points.Select(p => p switch
			{
				3 => "[black on green1] G [/]",
				1 => "[black on grey] B [/]",
				0 => "[white on red] M [/]",
				_ => $"[dim]{p}[/]",
			}));
		}
		catch (FmError)
		{
			return "";
		}
	}

	/// <summary>"2025-07-01" → "Tuesday 1 July 2025".</summary>
	private static string Pretty(string date) =>
		DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day)
			? $"{day.Day} {Months[day.Month - 1]} {day.Year} {Days[(int)day.DayOfWeek]}"
			: date;

	/// <summary>
	/// The time of day from the game's clock byte. The unit is a quarter of an hour counted from
	/// 05:45 — a fit to four stops whose time was known from the messages around them (9 at
	/// 08:00, 13 at 09:00, 17 at 10:00, 30 at 13:15), not something read from the game.
	/// </summary>
	private static string Time(JsonElement state) =>
		int.TryParse(Field(state, "Clock"), out var clock)
			? TimeSpan.FromMinutes(5 * 60 + 45 + clock * 15).ToString(@"hh\:mm")
			: "";

	/// <summary>
	/// The injured among the eleven and the bench, as the tactics screen of a match day has them
	/// (by surname); null when the screen is not up, so no page is opened for it.
	/// </summary>
	private static async Task<List<string>?> HurtInSide()
	{
		try
		{
			var tactic = await Fm.GetJson("tactic");
			if (!tactic.TryGetProperty("Slots", out var slots)) return null;
			var picked = slots.EnumerateArray().Select(s => Field(s, "Player")).Concat(Bench(tactic)).Where(n => n.Length > 0).ToList();
			var injured = await InjuredSquad();
			return picked.Where(n => injured.Any(full => full == n || full.EndsWith(" " + n, StringComparison.Ordinal))).ToList();
		}
		catch (FmError) { return null; }
	}

	/// <summary>The injured in the squad, from its data; no page opened.</summary>
	private static async Task<List<string>> InjuredSquad()
	{
		try
		{
			return (await Fm.Pull("Human.Team.Players", "Name,IsInjured", list: true)).EnumerateArray()
				.Where(p => Field(p, "IsInjured") == "True").Select(p => Field(p, "Name")).Where(n => n.Length > 0).ToList();
		}
		catch (FmError) { return []; }
	}

	/// <summary>Days to the next fixture, or null when there is none.</summary>
	private static async Task<int?> DaysToNextMatch()
	{
		try
		{
			var fixture = await Fm.Pull("Human.Team.NextFixture", "FixtureDateDistanceInDays,IsFutureMatch");
			return Field(fixture, "IsFutureMatch") == "True" && int.TryParse(Field(fixture, "FixtureDateDistanceInDays"), out var days) ? days : null;
		}
		catch (FmError) { return null; }
	}

	/// <summary>Whether the next fixture is a friendly, which has no opposition report.</summary>
	private static async Task<bool> NextIsFriendly()
	{
		try { return Field(await Fm.Pull("Human.Team.NextFixture.Competition", "IsFriendly"), "IsFriendly") == "True"; }
		catch (FmError) { return false; }
	}

	/// <summary>
	/// What a conversation waiting for an answer last said, or null when there is none. A card that
	/// keeps a menu of the director's beside it (a viewed offer, a player's transfer status) offers
	/// lines too, but nobody has said anything there.
	/// </summary>
	private static async Task<string> OpenQuestion()
	{
		if ((await TalkOptions()).Count == 0) return null;
		var said = (await Fm.GetJson("talk")).TryGetProperty("Says", out var says) ? says.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : [];
		return said.Count > 0 ? said[^1] : null;
	}
}
