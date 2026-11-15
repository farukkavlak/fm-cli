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
		// A filter left on (Tasks, by `inbox --tasks` or a reply that looked there) shows only its own rows:
		// the inbox read empty while a message waited (30 Aug 2030).
		await Filter("AllMessagesTutorial");
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

	/// <summary>
	/// The message the inbox last opened, as the game keeps it (`Human.HumanPortalLastSelectedMessageReference`),
	/// with the fields an inbox row has. Opening a message unbinds the list that carries it, so the one on screen
	/// can be missing from every list read (T128, T131). Null when none is kept.
	/// </summary>
	internal static async Task<JsonElement?> LastOpenedMessage()
	{
		const string at = "HumanPortalLastSelectedMessageReference.";
		string[] fields = ["UniqueId", "NewsTitleString", "CanRespond", "NewsHasResponded", "NewsItemIsPriority", "NewsItemTimeSent", "NewsItemHasBeenRead", "NewsItemId"];
		JsonElement row;
		try { row = await Fm.GetJson("pull", ("path", "Human"), ("props", string.Join(",", fields.Select(f => at + f)))); }
		catch (FmError) { return null; }
		if (Field(row, at + "UniqueId") is not { Length: > 0 } id || id == "0") return null;
		return JsonSerializer.SerializeToElement(fields.ToDictionary(f => f, f => Field(row, at + f)));
	}

	/// <summary>The messages read, with the last opened one added when it holds the game up and no list had it.</summary>
	internal static async Task<List<JsonElement>> WithLastOpened(List<JsonElement> blocking)
	{
		if (await LastOpenedMessage() is { } open && Blocking(open) && !blocking.Any(m => Field(m, "UniqueId") == Field(open, "UniqueId")))
			blocking.Add(open);
		return blocking;
	}

	/// <summary>The inbox back on all messages, after `TaskInbox(stay: true)`.</summary>
	internal static Task AllMessages() => Filter("AllMessagesTutorial");

	/// <summary>The inbox filter on show, by the game's own number for it: 0 all messages, 2 tasks.</summary>
	private static async Task<string> FilterOn() =>
		Field(await Fm.GetJson("pull", ("path", "data"), ("props", "PortalMessagesSelectedTab")), "PortalMessagesSelectedTab");

	private static readonly Dictionary<string, string> FilterTab = new() { ["AllMessagesTutorial"] = "0", ["TaskMessagesTutorial"] = "2" };

	/// <summary>An inbox filter by its button's element name, read back by the filter the game says is on.</summary>
	private static async Task Filter(string name)
	{
		if (await FilterOn() == FilterTab[name]) return;
		var controls = await Fm.GetJson("buttons", ("json", "1"));
		var button = controls.EnumerateArray().FirstOrDefault(c => Field(c, "Name") == name);
		if (button.ValueKind != JsonValueKind.Object) return;
		await Fm.Get("press", ("ptr", Field(button, "Pointer")));
		await Until(async () => await FilterOn() == FilterTab[name], seconds: 3, every: 0.1);
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
		// The list draws a window of rows; scrolled by a fixed share, a long inbox (149 messages) was read with gaps
		// between windows and a waiting message was missed. Each window must share a row with the one before:
		// when it does not, the step is halved and the gap read again.
		async Task<HashSet<string>> WindowAt(double at)
		{
			var to = at.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
			var before = string.Join("|", (await Fm.GetJson("inbox")).EnumerateArray().Select(m => Field(m, "UniqueId")));
			if ((await Fm.Get("scroll", ("ptr", list), ("to", to))).StartsWith("error", StringComparison.Ordinal)) return [];
			await Until(async () => string.Join("|", (await Fm.GetJson("inbox")).EnumerateArray().Select(m => Field(m, "UniqueId"))) != before, seconds: 1, every: 0.1);
			var ids = new HashSet<string>();
			foreach (var m in (await Fm.GetJson("inbox")).EnumerateArray())
			{
				var id = Field(m, "UniqueId");
				if (id.Length == 0) continue;
				ids.Add(id);
				if (seen.Add(id)) rows.Add(m.Clone());
			}
			return ids;
		}
		var last = rows.Select(m => Field(m, "UniqueId")).ToHashSet();
		double done = 0, share = 0.1;
		while (done < 1)
		{
			var next = Math.Min(1, done + share);
			var window = await WindowAt(next);
			if (window.Count == 0) break;
			if (!window.Overlaps(last) && share > 0.01)
			{
				share /= 2;
				continue;
			}
			last = window;
			done = next;
		}
		await Fm.Get("scroll", ("ptr", list), ("to", "0"));
		// In the order they came, as `inbox` lists its window: the order the scrolled pages handed rows
		// out in is not the same twice (a message was 15th, then 23rd).
		return rows.OrderBy(m => Id(m) is var id and >= 0 ? id : long.MaxValue).ToList();
	}

	/// <summary>
	/// Scrolls the inbox's own list until the row of this message (by its id) is drawn, so it can be pressed:
	/// the list draws only the rows in view, and one `inbox --all` listed may lie far down. False when no step
	/// of the list draws it.
	/// </summary>
	internal static async Task<bool> DrawMessage(string id)
	{
		bool Drawn(JsonElement rows) => rows.ValueKind == JsonValueKind.Array && rows.EnumerateArray().Any(m => Field(m, "UniqueId") == id);
		if (Drawn(await Fm.GetJson("inbox"))) return true;
		var list = (await Fm.Get("inboxlist")).Trim();
		if (list.StartsWith("error", StringComparison.Ordinal)) return false;
		for (var step = 0; step <= 20; step++)
		{
			var to = (step / 20.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
			if ((await Fm.Get("scroll", ("ptr", list), ("to", to))).StartsWith("error", StringComparison.Ordinal)) return false;
			// The rows are drawn again a moment after the list moves: the read that holds it is waited for.
			if (await Until(async () => Drawn(await Fm.GetJson("inbox")), seconds: 1, every: 0.1)) return true;
		}
		return false;
	}

	/// <summary>
	/// The row of a drawn message to press, by the binding the row is drawn from: the row's button is bound one level
	/// above the message (…items1.N.1 for …items1.N.1.NewsItem). Not by its title: two messages can share one.
	/// </summary>
	internal static async Task<string?> MessageRow(string id)
	{
		var path = (await Fm.GetJson("inbox", ("paths", "1"))).EnumerateArray()
			.Where(m => Field(m, "UniqueId") == id).Select(m => Field(m, "Path"))
			.FirstOrDefault(p => p.EndsWith(".NewsItem", StringComparison.Ordinal));
		if (path == null) return null;
		var source = path.TrimStart('.')[..^".NewsItem".Length];
		var row = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Name") == "NewsItemButton" && Field(b, "Source") == source);
		return row.ValueKind == JsonValueKind.Object ? Field(row, "Pointer") : null;
	}

	/// <summary>The bound inbox, its list scrolled to the newest first.</summary>
	internal static async Task<JsonElement> TopInbox()
	{
		await Filter("AllMessagesTutorial");
		return await AtTop(await Bound("inbox", "PortalScreen"));
	}

	private static async Task<JsonElement> AtTop(JsonElement data)
	{
		// The list binds only the rows drawn, and the portal keeps where it was scrolled to: left
		// halfway down, the newest messages are not among them. The top is the newest.
		if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) return Oldest(data);
		try
		{
			// The list itself, by its element (not by a message's title, which two messages can share).
			var list = (await Fm.Get("inboxlist")).Trim();
			if (list.StartsWith("error", StringComparison.Ordinal)) return Oldest(data);
			// Already at the top, the rows bound are the newest.
			if ((await Fm.Get("scroll", ("ptr", list))).StartsWith("0 of")) return Oldest(data);
			var moved = await Fm.Get("scroll", ("ptr", list), ("to", "0"));
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

			// The subtab event still waiting after a while: a popup holds the game's queue, and nothing will come.
			if (DateTime.UtcNow > deadline - TimeSpan.FromSeconds(17) && (await Fm.Get("queue")).Split(' ')[0] != "0") break;
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

		// `--open`: the message last opened, with its kind, the game's own number for what the message is
		// (`NewsItemId`: 125 "contracts set to expire", player's or staff's). The list's rows do not bind it.
		// The open message's attachment as the game draws it, once drawn (`reply` shows the same): blocks of texts.
		if (args.Contains("--attachment"))
		{
			var blocks = await ReadyAttachment();
			if (blocks.ValueKind != JsonValueKind.Array)
			{
				Console.Error.WriteLine(L("The open message has no attachment."));
				return 1;
			}
			if (json) Console.WriteLine(JsonOut.Of(blocks.EnumerateArray().Select(b => new
			{
				Kind = Field(b, "Kind"),
				Texts = b.TryGetProperty("Texts", out var t) ? t.EnumerateArray().Select(x => GameWords.Of(Field(x, "Text"))).ToList() : [],
			})));
			else foreach (var b in blocks.EnumerateArray())
				if (b.TryGetProperty("Texts", out var t)) Console.WriteLine("  " + string.Join(" · ", t.EnumerateArray().Select(x => GameWords.Of(Field(x, "Text")))));
			return 0;
		}

		if (args.Contains("--open"))
		{
			if (await LastOpenedMessage() is not { } open)
			{
				Console.Error.WriteLine(L("No message has been opened."));
				return 1;
			}
			if (json) Console.WriteLine(open.ToString());
			else Console.WriteLine(L($"{Field(open, "NewsTitleString")}   (kind {Field(open, "NewsItemId")})"));
			return 0;
		}

		// Offer messages can sit behind the inbox's own Tasks filter, where the default list
		// does not show them and `offers` has no button to answer. Pressing
		// that filter by its stable element name brings them up.
		// Every message marked read with the inbox's own Mark All Read (EventFmxMarkAllHumanMessagesAsRead).
		if (args.Contains("--read-all"))
		{
			static int Unread(JsonElement list) => list.EnumerateArray().Count(m => Field(m, "NewsItemHasBeenRead") != "True");
			var before = Unread(await FreshInbox());
			if (before == 0)
			{
				Console.WriteLine(L("Every message is read already."));
				return 0;
			}
			var mark = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(c => Field(c, "Events").Contains("EventFmxMarkAllHumanMessagesAsRead", StringComparison.Ordinal) && Field(c, "Enabled") == "True");
			if (mark.ValueKind != JsonValueKind.Object)
			{
				await OpenPage(Screen.Inbox);
				await Until(async () => (mark = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
					.FirstOrDefault(c => Field(c, "Events").Contains("EventFmxMarkAllHumanMessagesAsRead", StringComparison.Ordinal) && Field(c, "Enabled") == "True")).ValueKind == JsonValueKind.Object, seconds: 6, every: 0.3);
			}
			if (mark.ValueKind != JsonValueKind.Object)
			{
				Console.Error.WriteLine(L("The inbox has no Mark All Read button now."));
				return 1;
			}
			await Fm.Get("press", ("ptr", Field(mark, "Pointer")));
			var left = before;
			await Until(async () => (left = Unread(await FreshInbox())) < before, seconds: 5, every: 0.3);
			await Until(async () => (left = Unread(await FreshInbox())) == 0, seconds: 2, every: 0.3);
			if (left == before)
			{
				Console.Error.WriteLine(L("Told to mark every message read, but none changed."));
				return 1;
			}
			Console.WriteLine(left == 0 ? L($"{before} messages marked read.") : L($"{before - left} of {before} messages marked read; {left} still unread."));
			return 0;
		}

		// `--all` reads every message the inbox holds, scrolling its streamed list; the bound
		// inbox only carries the ones the current page has loaded (ARCHIVED offers, for one).
		// `--tasks` reads the list under the inbox's own Tasks filter, then puts the filter back: left on, every
		// later read of the inbox saw only its rows (empty while a message waited, 30 Aug 2030).
		var inbox = args.Contains("--all")
			? System.Text.Json.JsonSerializer.SerializeToElement(await AllInbox())
			: args.Contains("--tasks")
				? System.Text.Json.JsonSerializer.SerializeToElement(await FreshInbox() is var _ ? await TaskInbox() : [])
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
