using System.Text.Json;

namespace FmCli;

/// <summary>
/// Asking the board for something. FM27 puts every request in one list on the board's page (Club › Club Vision,
/// "Make Board Request"): plain entries (expanding the stadium) and kinds with their own entries (Facilities,
/// Finance, Networking, Staff, Personal), some a level deeper (a youth team). Choosing one puts the request to the
/// board, which answers in a line; the decision itself comes later as a message.
///
/// Held by identity: the list by the element it sits in (`#MakeBoardRequestButton`), each request by its place in
/// the game's own menu (the path of indices, read afresh each time), named as the game names it.
/// </summary>
internal static partial class Program
{
	/// <param name="Path">The entry's place in the game's menu ("2,3"), what /select takes.</param>
	/// <param name="Kind">The kind it is under, as the game names it; "" for a plain entry.</param>
	/// <param name="Shown">The entry with the levels above it below the kind ("Physios Allowed › U19s").</param>
	private sealed record BoardRequest(string Path, string Kind, string Shown, string List);

	private static async Task<int> BoardRequests(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.Board);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		List<BoardRequest> requests = [];
		await Until(async () => (requests = await BoardRequestList()).Count > 0, seconds: 8, every: 0.4);
		if (requests.Count == 0)
		{
			// The list is there with nothing to choose, or not there, while requests wait for the board's answer.
			var waiting = Field(await Fm.Pull("Human.Club", "ActiveBoardRequestsCount"), "ActiveBoardRequestsCount");
			Console.Error.WriteLine(waiting is { Length: > 0 } and not "0"
				? L("The board is not taking requests now: the card has no request option (no new one opens while requests await an answer).")
				: L("The board's request list is not on the page."));
			return 1;
		}

		var typed = string.Join(" ", Words(args).Skip(1));
		string Named(BoardRequest r) => r.Kind.Length == 0 ? r.Shown : $"{r.Kind} › {r.Shown}";
		if (typed.Length == 0)
		{
			if (json) Console.WriteLine(JsonOut.Of(requests.Select((r, i) => new { No = i + 1, r.Kind, Request = r.Shown })));
			else
			{
				Table.Write(["#", L("TYPE"), L("REQUEST")], requests.Select((r, i) => new[] { (i + 1).ToString(), r.Kind, r.Shown }).ToList());
				Console.WriteLine(L("\nTo make a request: board request <#> --yes"));
			}
			return 0;
		}

		// By its number in the list just read, or by words of its name as the game writes it.
		var wanted = Fold(typed);
		var hits = int.TryParse(typed, out var no) && no >= 1 && no <= requests.Count ? [requests[no - 1]]
			: requests.Where(r => Fold(r.Shown) == wanted || Fold(Named(r)) == wanted).ToList();
		if (hits.Count == 0) hits = requests.Where(r => wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(Fold(Named(r)).Contains)).ToList();
		if (hits.Count != 1)
		{
			Console.Error.WriteLine(hits.Count == 0 ? L($"No request called '{typed}' (all of them: board request).") : L($"'{typed}' matches more than one request:"));
			foreach (var hit in hits) Console.Error.WriteLine($"  {Named(hit)}");
			return 64;
		}
		var request = hits[0];
		var shown = Named(request);

		if (!args.Contains("--yes"))
		{
			Console.Error.WriteLine(L($"To ask the board for: {shown}. To confirm: board request {requests.IndexOf(request) + 1} --yes"));
			return 1;
		}

		// The entry is chosen into the list's binding, then the list sends its own event (SpeakToDropdownAction) with it.
		var chosen = await Fm.Get("select", ("ptr", request.List), ("path", request.Path));
		if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
		var sent = await Fm.Get("press", ("ptr", request.List));
		if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);

		// What the board says back: in FM27 a dialog of its own kind (`board_request_ok`), answered by its one
		// button; a conversation (a line, maybe with choices) as before. The request is read back by its entry
		// leaving the list: one waiting for the board's decision is not offered again.
		JsonElement talk = default;
		JsonElement dialog = default;
		var answered = await Until(async () =>
			(dialog = (await Fm.GetJson("dialog")).EnumerateArray().FirstOrDefault(d => Field(d, "Type") == "board_request_ok")).ValueKind == JsonValueKind.Object
			|| ((talk = await Fm.GetJson("talk")).TryGetProperty("Says", out var says) && says.GetArrayLength() > 0), seconds: 8, every: 0.3);
		if (!answered)
		{
			Console.Error.WriteLine(L($"{shown}: asked, but no answer came from the board."));
			return 1;
		}
		if (dialog.ValueKind == JsonValueKind.Object)
		{
			var said = Field(dialog, "ShownDescription") is { Length: > 0 } words ? words : Field(dialog, "Description");
			var closed = await Fm.Get("dialog", ("press", "primary"));
			if (closed.StartsWith("error", StringComparison.Ordinal)) return Report(closed);
			var gone = await Until(async () => !(await BoardRequestList()).Any(r => r.Kind == request.Kind && r.Shown == request.Shown), seconds: 5, every: 0.3);
			if (json) Console.WriteLine(JsonOut.Of(new { Request = shown, Says = new[] { said }, Answers = Array.Empty<string>(), Pending = gone }));
			else
			{
				Console.WriteLine(L($"Asked: {shown}"));
				Console.WriteLine(L($"  Board: {said}"));
				if (!gone) Console.WriteLine(L("  (the request is still offered in the list: check the board's page)"));
			}
			return gone ? 0 : 1;
		}
		var lines = talk.GetProperty("Says").EnumerateArray().Select(s => s.GetString() ?? "").Where(s => !s.StartsWith("»", StringComparison.Ordinal)).ToList();
		var answers = talk.GetProperty("Answers").EnumerateArray().Select(a => Field(a, "Label")).ToList();
		if (json) Console.WriteLine(JsonOut.Of(new { Request = shown, Says = lines, Answers = answers }));
		else
		{
			Console.WriteLine(L($"Asked: {shown}"));
			foreach (var line in lines) Console.WriteLine(L($"  Board: {line}"));
		}

		if (answers.Count == 1 && await AcknowledgeButton() is not null)
		{
			await Acknowledge();
			return 0;
		}
		if (!json) Console.WriteLine(L($"The board is waiting for an answer ({string.Join(" / ", answers)}) — to answer: talk"));
		return 0;
	}

	/// <summary>Every request the board's list offers, read from the game's menu as it stands.</summary>
	private static async Task<List<BoardRequest>> BoardRequestList()
	{
		var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(d => d.TryGetProperty("Within", out var within) && within.EnumerateArray().Any(n => n.GetString() == "MakeBoardRequestButton"));
		if (list.ValueKind != JsonValueKind.Object || Field(list, "Enabled") != "True") return [];
		var pointer = Field(list, "Pointer");
		var tree = await Fm.GetJson("menu", ("ptr", pointer), ("tree", "1"));
		if (tree.ValueKind != JsonValueKind.Array) return [];
		var rows = tree.EnumerateArray().Select(r => (Path: Field(r, "Path"), Text: Field(r, "Text"), Folder: r.GetProperty("Folder").GetBoolean())).ToList();
		var names = rows.ToDictionary(r => r.Path, r => r.Text);
		return rows.Where(r => !r.Folder).Select(r =>
		{
			// The levels above it, by their own paths: "4,4,0" is under "4" (the kind) and "4,4".
			var parts = r.Path.Split(',');
			var above = Enumerable.Range(1, parts.Length - 1).Select(n => names.GetValueOrDefault(string.Join(",", parts[..n]), "")).ToList();
			var kind = above.Count > 0 ? above[0] : "";
			var shown = string.Join(" › ", above.Skip(1).Append(r.Text));
			return new BoardRequest(r.Path, kind, shown, pointer);
		}).ToList();
	}
}
