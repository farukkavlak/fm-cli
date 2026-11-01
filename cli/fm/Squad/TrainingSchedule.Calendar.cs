using System.Globalization;

namespace FmCli;

internal static partial class Program
{
	private static async Task<DateTime> GameDate() =>
		DateTime.TryParseExact(Field(await Fm.GetJson("state"), "Date"), "yyyy-MM-dd", null, DateTimeStyles.None, out var d) ? d : DateTime.Today;

	/// <summary>
	/// A day as typed: "2025-10-14", "14" (the next 14th from today), "14 ekim" / "14 oct",
	/// "today", "tomorrow", or a weekday ("friday": the next Friday, today included). Says how many words it took.
	/// </summary>
	private static DateTime? ScheduleDate(List<string> words, DateTime today, out int used)
	{
		used = 1;
		var first = Fold(words[0]);
		if (DateTime.TryParseExact(words[0], "yyyy-MM-dd", null, DateTimeStyles.None, out var iso)) return iso;
		if (first is "bugun" or "today") return today;
		if (first is "yarin" or "tomorrow") return today.AddDays(1);
		var weekday = Array.IndexOf(WeekdayWords, first);
		if (weekday < 0) weekday = Array.FindIndex(Enum.GetNames<DayOfWeek>(), n => Fold(n) == first);
		if (weekday >= 0) return today.AddDays(((weekday - (int)today.DayOfWeek) + 7) % 7);
		if (!int.TryParse(words[0], out var n) || n < 1 || n > 31) return null;

		if (words.Count > 1)
		{
			var m = Fold(words[1]);
			var month = Months.Select(Fold).ToList().FindIndex(x => x.StartsWith(m, StringComparison.Ordinal) && m.Length >= 3) + 1;
			if (month == 0) month = Array.FindIndex(MonthNames, x => Fold(x) == (m.Length >= 3 ? m[..3] : m)) + 1;
			if (month > 0)
			{
				used = 2;
				var year = month < today.Month - 6 ? today.Year + 1 : month > today.Month + 6 ? today.Year - 1 : today.Year;
				return n <= DateTime.DaysInMonth(year, month) ? new DateTime(year, month, n) : null;
			}
		}
		for (var ahead = 0; ahead < 3; ahead++)
		{
			var at = today.AddMonths(ahead);
			if (n > DateTime.DaysInMonth(at.Year, at.Month)) continue;
			var date = new DateTime(at.Year, at.Month, n);
			if (date >= today) return date;
		}
		return null;
	}

	private static string DayName(DateTime d) => $"{d.Day} {Months[d.Month - 1]} {Days[(int)d.DayOfWeek]}";

	private static string WeekName(DateTime d)
	{
		var monday = d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
		return L($"week of {monday.Day} {Months[monday.Month - 1]}");
	}
}
