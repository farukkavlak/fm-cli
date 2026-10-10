using System.Text.Json;

namespace FmCli;

/// <summary>
/// Asking the board for something (FINDINGS §8.108). The board's card — Club › Board, the "N
/// board member" tile — has one list a kind of request (Facilities, Finance, Networking, Staff)
/// and two plain buttons (Build New Stadium, New Contract). Choosing one puts the request to the
/// board, which answers in a line; the decision itself comes later as a message.
/// </summary>
internal static partial class Program
{
	private sealed record BoardRequest(string Kind, string Name, string List, string Parent);

	private static async Task<int> BoardRequests(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage("Board", "Club");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// The card is behind the tile that counts the board's members.
		List<BoardRequest> requests = [];
		for (var attempt = 0; attempt < 3 && requests.Count == 0; attempt++)
		{
			JsonElement tile = default;
			await Until(async () => (tile = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Label").Contains("board member", StringComparison.Ordinal))).ValueKind == JsonValueKind.Object, seconds: 6, every: 0.4);
			if (tile.ValueKind != JsonValueKind.Object) break;
			await Fm.Get("click", ("ptr", Field(tile, "Pointer")));
			await Until(async () => (requests = await BoardRequestList()).Count > 0, seconds: 5, every: 0.5);
		}
		if (requests.Count == 0)
		{
			// The card without its request lists: the board takes no more while some are waiting
			// for its answer (seen after three in one day).
			var card = (await Fm.GetJson("texts", ("anchor", "Outcome"), ("up", "0"))).GetArrayLength() > 0;
			Console.Error.WriteLine(card
				? "Yönetim şu an talep almıyor: kartta talep seçeneği yok (cevap bekleyen talepler varken yenisi açılmıyor)."
				: "Yönetim kartı açılmadı.");
			return 1;
		}

		var typed = string.Join(" ", Words(args).Skip(1));
		if (typed.Length == 0)
		{
			if (json) Console.WriteLine(JsonOut.Of(requests.Select(r => new { r.Kind, Request = r.Parent == null ? r.Name : $"{r.Parent} › {r.Name}" })));
			else
			{
				Table.Write(["TÜR", "TALEP"], requests.Select(r => new[] { r.Kind, r.Parent == null ? r.Name : $"{r.Parent} › {r.Name}" }).ToList());
				Console.WriteLine("\nTalep etmek için: board request \"<talep>\" --yes");
			}
			return 0;
		}

		string Full(BoardRequest r) => r.Parent == null ? r.Name : $"{r.Parent} {r.Name}";
		var wanted = Fold(typed);
		var hits = requests.Where(r => Fold(Full(r)) == wanted || Fold(r.Name) == wanted).ToList();
		if (hits.Count == 0) hits = requests.Where(r => Fold(Full(r)).Contains(wanted)).ToList();
		if (hits.Count == 0) hits = requests.Where(r => wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => Fold(Full(r)).Contains(word))).ToList();
		if (hits.Count != 1)
		{
			Console.Error.WriteLine(hits.Count == 0 ? $"'{typed}' diye bir talep yok (hepsi: board request)." : $"'{typed}' birden çok talebe uyuyor:");
			foreach (var hit in hits) Console.Error.WriteLine($"  {(hit.Parent == null ? hit.Name : $"{hit.Parent} › {hit.Name}")}   ({hit.Kind})");
			return 64;
		}
		var request = hits[0];
		var shown = request.Parent == null ? request.Name : $"{request.Parent} › {request.Name}";

		if (!args.Contains("--yes"))
		{
			Console.Error.WriteLine($"Yönetimden istenecek: {shown}  ({request.Kind}). Onaylamak için: board request \"{Full(request)}\" --yes");
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
			Console.Error.WriteLine($"{shown}: istendi ama yönetimden bir cevap gelmedi.");
			return 1;
		}
		var lines = talk.GetProperty("Says").EnumerateArray().Select(s => s.GetString()).Where(s => !s.StartsWith("»", StringComparison.Ordinal)).ToList();
		var answers = talk.GetProperty("Answers").EnumerateArray().Select(a => Field(a, "Label")).ToList();
		if (json) Console.WriteLine(JsonOut.Of(new { Request = shown, Says = lines, Answers = answers }));
		else
		{
			Console.WriteLine($"İstendi: {shown}");
			foreach (var line in lines) Console.WriteLine($"  Yönetim: {line}");
		}

		if (answers.Count == 1 && answers[0] == "Acknowledge and Move On")
		{
			await Fm.Get("press", ("text", "Acknowledge and Move On"), ("any", "1"));
			return 0;
		}
		if (!json) Console.WriteLine($"Yönetim cevap bekliyor ({string.Join(" / ", answers)}) — cevap: talk");
		return 0;
	}

	/// <summary>Every request the card offers: the lists' choices (a submenu's under their parent) and the two buttons.</summary>
	private static async Task<List<BoardRequest>> BoardRequestList()
	{
		var requests = new List<BoardRequest>();
		foreach (var list in (await Fm.GetJson("dropdowns", ("json", "1"), ("deep", "1"))).EnumerateArray().Where(d => Field(d, "Name") == "GenericDropdown"))
		{
			var kind = Field(list, "Label");
			var choices = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
			var submenus = list.TryGetProperty("Submenus", out var subs) && subs.ValueKind == JsonValueKind.Object ? subs : default;
			for (var i = 0; i < choices.Count; i++)
			{
				if (!choices[i].EndsWith(" ›", StringComparison.Ordinal))
				{
					requests.Add(new(kind, choices[i], Field(list, "Pointer"), null));
					continue;
				}
				if (submenus.ValueKind != JsonValueKind.Object || !submenus.TryGetProperty(i.ToString(), out var children)) continue;
				foreach (var child in children.EnumerateArray())
					requests.Add(new(kind, child.GetString(), Field(list, "Pointer"), choices[i][..^2]));
			}
		}
		if (requests.Count == 0) return requests;

		foreach (var button in (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			         .Where(b => Field(b, "Name") == "StandardButton" && Field(b, "Events") == "SpeakToAction" && Field(b, "Label") is "Build New Stadium" or "New Contract"))
			requests.Add(new(Field(button, "Label"), Field(button, "Label"), null, null));
		return requests;
	}
}
