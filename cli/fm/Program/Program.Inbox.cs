using System.Text.Json;

namespace FmCli;

internal static partial class Program
{

	/// <summary>
	/// The inbox as it is now. An inbox loaded earlier can stay bound beneath other pages and go
	/// on answering with what it held then — after four matches it still listed the week before
	/// while the board's meeting request waited unseen. Opening the portal again
	/// binds the live one.
	/// </summary>
	internal static async Task<JsonElement> FreshInbox()
	{
		var before = (await Fm.GetJson("inbox")).ToString();
		var sentAt = DateTime.UtcNow;
		await Fm.Get("send",
			("event", "OpenSubtabEvent"),
			("SubtabPanelID", "PortalScreen"),
			("MainTabPanelID", "PortalScreen"),
			("ObjectUniqueID", "0"),
			("SecondaryTab", "false"));
		JsonElement data = default;
		// Changed, or the same for a while: the inbox already open was the live one.
		await Until(async () => (data = await Fm.GetJson("inbox")).GetArrayLength() > 0
			&& (data.ToString() != before || DateTime.UtcNow - sentAt > TimeSpan.FromSeconds(2)), seconds: 8, every: 0.5);
		if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) data = await Bound("inbox", "PortalScreen");
		return await AtTop(data);
	}

	/// <summary>
	/// The inbox under its own Tasks filter, then back to all messages. A waiting task can be
	/// missing from the default list and its scrolled pages (an illness, 3 Şubat 2029) while the
	/// filter shows it.
	/// </summary>
	internal static async Task<List<JsonElement>> TaskInbox(bool stay = false)
	{
		var before = (await Fm.GetJson("inbox")).ToString();
		await Filter("TaskMessagesTutorial");
		JsonElement data = default;
		await Until(async () => (data = await Fm.GetJson("inbox")).ToString() != before, seconds: 2, every: 0.2);
		var rows = data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().Select(m => m.Clone()).ToList() : [];
		if (!stay) await Filter("AllMessagesTutorial");
		return rows;
	}

	/// <summary>The inbox back on all messages, after `TaskInbox(stay: true)`.</summary>
	internal static Task AllMessages() => Filter("AllMessagesTutorial");

	private static async Task Filter(string name)
	{
		var controls = await Fm.GetJson("buttons", ("json", "1"));
		var button = controls.EnumerateArray().FirstOrDefault(c => Field(c, "Name") == name);
		if (button.ValueKind == JsonValueKind.Object) await Fm.Get("press", ("ptr", Field(button, "Pointer")));
	}

	/// <summary>
	/// Every message the inbox holds, read by scrolling its own streamed list with the list's
	/// pointer (so the anchor never goes off screen). Used when the first window does not have
	/// the message — a subject a script names, or the one the game is waiting on.
	/// </summary>
	internal static async Task<List<JsonElement>> AllInbox()
	{
		var top = await FreshInbox();
		var rows = top.EnumerateArray().Select(m => m.Clone()).ToList();
		var seen = rows.Select(m => Field(m, "UniqueId")).Where(id => id.Length > 0).ToHashSet();
		var list = (await Fm.Get("inboxlist")).Trim();
		if (list.StartsWith("error", StringComparison.Ordinal)) return rows;
		for (var step = 1; step <= 10; step++)
		{
			var to = (step / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
			var moved = await Fm.Get("scroll", ("ptr", list), ("to", to));
			if (moved.StartsWith("error", StringComparison.Ordinal)) break;
			var before = string.Join("|", (await Fm.GetJson("inbox")).EnumerateArray().Select(m => Field(m, "UniqueId")));
			await Until(async () => string.Join("|", (await Fm.GetJson("inbox")).EnumerateArray().Select(m => Field(m, "UniqueId"))) != before, seconds: 1, every: 0.1);
			foreach (var m in (await Fm.GetJson("inbox")).EnumerateArray())
			{
				var id = Field(m, "UniqueId");
				if (id.Length > 0 && seen.Add(id)) rows.Add(m.Clone());
			}
		}
		await Fm.Get("scroll", ("ptr", list), ("to", "0"));
		return rows;
	}

	/// <summary>The bound inbox, its list scrolled to the newest first.</summary>
	internal static async Task<JsonElement> TopInbox() => await AtTop(await Bound("inbox", "PortalScreen"));

	private static async Task<JsonElement> AtTop(JsonElement data)
	{
		// The list binds only the rows drawn, and the portal keeps where it was scrolled to: left
		// halfway down, the newest messages are not among them. The top is the newest.
		var title = data.EnumerateArray().Select(m => Field(m, "NewsTitleString")).FirstOrDefault(t => t != "");
		if (title == null) return Oldest(data);
		try
		{
			// Already at the top, the rows bound are the newest.
			if ((await Fm.Get("scroll", ("anchor", title))).StartsWith("0 of")) return Oldest(data);
			var moved = await Fm.Get("scroll", ("anchor", title), ("to", "0"));
			if (moved.StartsWith("0 of") && data.ToString() is var held)
			{
				// The rows are drawn again a few at a time: the read is the one that has stopped changing.
				JsonElement top = default;
				var last = held;
				var at = DateTime.UtcNow;
				await Until(async () =>
				{
					top = await Fm.GetJson("inbox");
					var now = top.ToString();
					var settled = top.GetArrayLength() > 0 && now == last && (now != held || DateTime.UtcNow - at > TimeSpan.FromSeconds(1.5));
					last = now;
					return settled;
				}, seconds: 5, every: 0.3);
				if (top.ValueKind == JsonValueKind.Array && top.GetArrayLength() > 0) data = top;
			}
		}
		catch (FmError) { }
		return Oldest(data);
	}

	/// <summary>
	/// The rows in the order they came, the newest last: a list's rows are bound in the order its
	/// pool hands them out, and a message's id grows with the day it arrived.
	/// </summary>
	private static JsonElement Oldest(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Array) return data;
		var rows = data.EnumerateArray().OrderBy(m => Id(m) is var id and >= 0 ? id : long.MaxValue).Select(m => m.GetRawText());
		return JsonDocument.Parse("[" + string.Join(",", rows) + "]").RootElement;
	}

	/// <summary>A message's id, or -1. Ids are the game's own and grow with the day a message arrived.</summary>
	private static long Id(JsonElement message) => long.TryParse(Field(message, "UniqueId"), out var id) ? id : -1;

	private static async Task<JsonElement> Bound(string route, string panel)
	{
		var data = await Fm.GetJson(route);
		if (data.GetArrayLength() > 0) return data;

		await Fm.Get("send",
			("event", "OpenSubtabEvent"),
			("SubtabPanelID", panel),
			("MainTabPanelID", panel),
			("ObjectUniqueID", "0"),
			("SecondaryTab", "false"));

		// Binding lands a moment later when the panel has to be built, and the game says so by
		// answering with rows: it is asked until it does rather than after a fixed wait (measured
		// 0.3–0.5 s after the subtab event; the first request was two seconds late).
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
		while (DateTime.UtcNow < deadline)
		{
			await Task.Delay(TimeSpan.FromMilliseconds(250));

			data = await Fm.GetJson(route);
			if (data.GetArrayLength() > 0) return data;
		}

		return data;
	}

	private static async Task<int> Inbox(bool json, string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		// Reading the inbox means bringing the portal up, and during a match that would be
		// opened over the match itself.
		if (Field(await Fm.GetJson("state"), "DuringMatch") == "True")
		{
			Console.Error.WriteLine(L("The inbox is not read during a match. For the match: match"));
			return 1;
		}

		// Offer messages can sit behind the inbox's own Tasks filter, where the default list
		// does not show them and `offers` has no button to answer. Pressing
		// that filter by its stable element name brings them up.
		if (args.Contains("--tasks"))
		{
			var controls = await Fm.GetJson("buttons", ("json", "1"));
			var task = controls.EnumerateArray().FirstOrDefault(c => Field(c, "Name") == "TaskMessagesTutorial");
			if (task.ValueKind == JsonValueKind.Object)
			{
				await Fm.Get("press", ("ptr", Field(task, "Pointer")));
				await Task.Delay(500);
			}
		}

		// `--all` reads every message the inbox holds, scrolling its streamed list; the bound
		// inbox only carries the ones the current page has loaded (ARCHIVED offers, for one).
		var inbox = args.Contains("--all")
			? System.Text.Json.JsonSerializer.SerializeToElement(await AllInbox())
			: await FreshInbox();
		RememberNumbers(inbox);
		if (json)
		{
			Console.WriteLine(inbox.ToString());
			return 0;
		}

		if (Ui.Rich) InboxView.Draw(inbox);
		else
		{
		var rows = inbox.EnumerateArray()
			.Select((m, i) => new[]
			{
				(i + 1).ToString(),
				Field(m, "NewsItemTimeSent"),
				Field(m, "Sender"),
				Field(m, "NewsTitleString"),
				Flags(m),
			})
			.ToList();

		Table.Write(new[] { "#", L("TIME"), L("FROM"), L("SUBJECT"), "" }, rows);
		}

		var waiting = inbox.EnumerateArray().Count(Blocking);
		if (waiting > 0)
			Console.WriteLine(L($"\n{waiting} messages await an answer — they block `fm next`. To answer: reply <#>"));

		return 0;
	}
}
