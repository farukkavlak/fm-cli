using System.Text.Json;

namespace FmCli;

/// <summary>
/// Asking the board for something. The board's card — Club › Board, the "N
/// board member" tile — has one list a kind of request (Facilities, Finance, Networking, Staff)
/// and two plain buttons (Build New Stadium, New Contract). Choosing one puts the request to the
/// board, which answers in a line; the decision itself comes later as a message.
/// </summary>
internal static partial class Program
{
	private sealed record BoardRequest(string Kind, string Name, string? List, string? Parent);

	private static async Task<int> BoardRequests(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.Board);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// The card is behind the board's tile, known by the panel it is laid out in (BindingRemapper
		// #Board): its words count the members in the game's language.
		// A card left open by an earlier call covers the tile, so it is read as it is.
		var requests = await BoardRequestList();
		for (var attempt = 0; attempt < 3 && requests.Count == 0; attempt++)
		{
			var clicked = "";
			await Until(async () => !(clicked = await Fm.Get("click", ("within", "Board"), ("name", "layout-tile-interaction-button"))).StartsWith("error", StringComparison.Ordinal), seconds: 6, every: 0.4);
			if (clicked.StartsWith("error", StringComparison.Ordinal)) break;
			await Until(async () => (requests = await BoardRequestList()).Count > 0, seconds: 5, every: 0.5);
		}
		if (requests.Count == 0)
		{
			// The card without its request lists: the board takes no more while some are waiting
			// for its answer (seen after three in one day).
			var card = (await Fm.GetJson("texts", ("anchor", "Outcome"), ("up", "0"))).GetArrayLength() > 0;
			Console.Error.WriteLine(card
				? L("The board is not taking requests now: the card has no request option (no new one opens while requests await an answer).")
				: L("The board card did not open."));
			return 1;
		}

		var typed = string.Join(" ", Words(args).Skip(1));
		if (typed.Length == 0)
		{
			if (json) Console.WriteLine(JsonOut.Of(requests.Select(r => new { Kind = GameWords.Of(r.Kind), Request = r.Parent == null ? GameWords.Of(r.Name) : $"{GameWords.Of(r.Parent)} › {GameWords.Of(r.Name)}" })));
			else
			{
				Table.Write([L("TYPE"), L("REQUEST")], requests.Select(r => new[] { r.Kind, r.Parent == null ? r.Name : $"{r.Parent} › {r.Name}" }).ToList());
				Console.WriteLine(L("\nTo make a request: board request \"<request>\" --yes"));
			}
			return 0;
		}

		string Full(BoardRequest r) => r.Parent == null ? r.Name : $"{r.Parent} {r.Name}";
		// Typed as the game reads it or as the table shows it.
		string[] Forms(BoardRequest r) => [Fold(Full(r)), Fold(r.Parent == null ? GameWords.Of(r.Name) : $"{GameWords.Of(r.Parent)} {GameWords.Of(r.Name)}")];
		var wanted = Fold(typed);
		var hits = requests.Where(r => Forms(r).Contains(wanted) || Fold(r.Name) == wanted || Fold(GameWords.Of(r.Name)) == wanted).ToList();
		if (hits.Count == 0) hits = requests.Where(r => Forms(r).Any(f => f.Contains(wanted))).ToList();
		if (hits.Count == 0) hits = requests.Where(r => Forms(r).Any(f => wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(f.Contains))).ToList();
		if (hits.Count != 1)
		{
			Console.Error.WriteLine(hits.Count == 0 ? L($"No request called '{typed}' (all of them: board request).") : L($"'{typed}' matches more than one request:"));
			foreach (var hit in hits) Console.Error.WriteLine($"  {(hit.Parent == null ? GameWords.Of(hit.Name) : $"{GameWords.Of(hit.Parent)} › {GameWords.Of(hit.Name)}")}   ({GameWords.Of(hit.Kind)})");
			return 64;
		}
		var request = hits[0];
		var shown = request.Parent == null ? GameWords.Of(request.Name) : $"{GameWords.Of(request.Parent)} › {GameWords.Of(request.Name)}";

		if (!args.Contains("--yes"))
		{
			Console.Error.WriteLine(L($"To ask the board for: {shown}  ({GameWords.Of(request.Kind)}). To confirm: board request \"{Full(request)}\" --yes"));
			return 1;
		}

		var sent = request.List == null
			? await Fm.Get("click", ("text", request.Name))
			: request.Parent == null
				? await Fm.Get("select", ("ptr", request.List), ("choice", request.Name), ("fire", "1"))
				: await Fm.Get("select", ("ptr", request.List), ("names", $"{request.Parent}>{request.Name}"), ("fire", "1"));
		if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);

		// What the board says back; a line with nothing to choose is acknowledged, anything
		// with a choice in it is left for `talk`.
		JsonElement talk = default;
		var answered = await Until(async () => (talk = await Fm.GetJson("talk")).TryGetProperty("Says", out var says) && says.GetArrayLength() > 0, seconds: 8, every: 0.5);
		if (!answered)
		{
			Console.Error.WriteLine(L($"{shown}: asked, but no answer came from the board."));
			return 1;
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

	/// <summary>Every request the card offers: the lists' choices (a submenu's under their parent) and the two buttons.</summary>
	private static async Task<List<BoardRequest>> BoardRequestList()
	{
		var requests = new List<BoardRequest>();
		var seen = new HashSet<string>();
		foreach (var list in (await Fm.GetJson("dropdowns", ("json", "1"), ("deep", "1"))).EnumerateArray().Where(d => Field(d, "Name") == "GenericDropdown"))
		{
			var kind = Field(list, "Label");
			var choices = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
			// A list can sit on the card twice, the second copy without its label.
			if (!seen.Add(string.Join("\n", choices))) continue;
			var submenus = list.TryGetProperty("Submenus", out var subs) && subs.ValueKind == JsonValueKind.Object ? subs : default;
			for (var i = 0; i < choices.Count; i++)
			{
				if (!choices[i]!.EndsWith(" ›", StringComparison.Ordinal))
				{
					requests.Add(new(kind, choices[i], Field(list, "Pointer"), null));
					continue;
				}
				if (submenus.ValueKind != JsonValueKind.Object || !submenus.TryGetProperty(i.ToString(), out var children)) continue;
				foreach (var child in children.EnumerateArray())
					requests.Add(new(kind, child.GetString() ?? "", Field(list, "Pointer"), choices[i]![..^2]));
			}
		}
		if (requests.Count == 0) return requests;

		foreach (var button in (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			         .Where(b => Field(b, "Name") == "StandardButton" && Field(b, "Events") == "SpeakToAction" && Field(b, "Label") is "Build New Stadium" or "New Contract"))
			requests.Add(new(Field(button, "Label"), Field(button, "Label"), null, null));
		return requests;
	}
}
