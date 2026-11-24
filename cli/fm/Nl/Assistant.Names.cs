using System.Text.Json;
using System.Text.Json.Nodes;
using FmCli.Nl;

namespace FmCli;

/// <summary>
/// The names a sentence gives, held by the game's own ids: before a call runs, a player named
/// in it is looked for among the save's people ("rinaldiye", "dadukun", "kalaftı"), and the call
/// gets his full name. Two who fit as well are asked between; one who fits nobody keeps the
/// words typed, and the command looks for him in the game as it would for a typed command. A
/// club named ahead of him ("frosinonenin rinaldi") is not taken for part of his name.
/// </summary>
internal static partial class Program
{
	/// <summary>The arguments that name a player, by tool.</summary>
	private static readonly Dictionary<string, string[]> PlayerArgs = new()
	{
		["fm_player"] = ["name"], ["fm_offer"] = ["name"], ["fm_contract"] = ["name"], ["fm_compare"] = ["a", "b"],
		["fm_numbers"] = ["player"], ["fm_tactic_player"] = ["player"], ["fm_training"] = ["player"], ["fm_intensity"] = ["player"],
		["fm_release"] = ["player"], ["fm_propose"] = ["player"], ["fm_penalties"] = ["player"], ["fm_takers"] = ["player"],
		["fm_happiness"] = ["player"], ["fm_captains"] = ["captain", "vice", "player"], ["fm_tweak"] = ["player"], ["fm_sub"] = ["on", "off"],
	};

	/// <summary>The tools whose command opens the player's page, and so takes "Name #id" for one of two with a name.</summary>
	private static readonly HashSet<string> OpensPlayerPage = ["fm_player", "fm_offer", "fm_contract", "fm_release"];

	private static (string Key, List<SaveName> Names)? saveNames;

	/// <summary>
	/// Our players (the youth sides too), our club and the next opponent, by their ids. Kept while
	/// the game's date and our club stay the same. Empty when the game cannot be asked.
	/// </summary>
	private static async Task<List<SaveName>> SaveNames()
	{
		try
		{
			var state = await Fm.GetJson("state");
			var club = await Fm.Pull("Human.Club", "Name,UniqueId");
			var key = $"{Field(state, "Date")}|{Field(club, "UniqueId")}";
			if (saveNames is { } kept && kept.Key == key) return kept.Names;

			var names = new List<SaveName>();
			void Add(string kind, JsonElement r)
			{
				if (Field(r, "UniqueId") is { Length: > 0 } id && Field(r, "Name") is { Length: > 0 } name && names.All(n => n.Id != id || n.Kind != kind))
					names.Add(new SaveName(kind, id, name));
			}
			// Just after a load the squad can read as ids without names: waited for, the seconds only an upper bound.
			List<JsonElement> players = [];
			await Until(async () =>
			{
				players = (await Fm.Pull("Human.Team.Players", "Name,UniqueId", list: true, seconds: 5)).EnumerateArray().ToList();
				return players.Count > 0 && players.All(p => Field(p, "Name").Length > 0);
			}, seconds: 10, every: 0.1);
			foreach (var p in players.Concat(await YouthPlayers())) Add(SaveName.Player, p);
			Add(SaveName.Club, club);
			Add(SaveName.Club, await Fm.Pull("Human.Team.NextMatchOpponent", "Name,UniqueId"));
			// Every club we play this season, from our own fixture list (no page opened): a league's whole field.
			if (await FixtureList(100, 100, "HomeTeam.Name,HomeTeam.UniqueId,AwayTeam.Name,AwayTeam.UniqueId") is { ValueKind: JsonValueKind.Array } fixtures)
				foreach (var f in fixtures.EnumerateArray())
					foreach (var side in new[] { "HomeTeam", "AwayTeam" })
						if (Field(f, $"{side}.UniqueId") is { Length: > 0 } id && Field(f, $"{side}.Name") is { Length: > 0 } name && names.All(n => n.Id != id))
							names.Add(new SaveName(SaveName.Club, id, name));
			// The shortlist as `shortlist` last read it here (it is read only with its page open).
			foreach (var s in SeenNames.At(await SeenKey())) if (names.All(n => n.Id != s.Id || n.Kind != s.Kind)) names.Add(s);
			// The players and clubs of the transfer activity: offers made and received, by their ids.
			foreach (var p in await TransferActivityNames()) if (names.All(n => n.Id != p.Id || n.Kind != p.Kind)) names.Add(p);
			saveNames = (key, names);
			return names;
		}
		catch (Exception e) when (e is FmError or HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException)
		{
			return [];
		}
	}

	/// <summary>The shortlist's players and their clubs, by their ids, kept for later sentences.</summary>
	private static async Task KeepShortlistNames(List<JsonElement> players)
	{
		var names = new List<SaveName>();
		foreach (var p in players)
		{
			if (Field(p, "UniqueId") is { Length: > 0 } id) names.Add(new SaveName(SaveName.Player, id, Field(p, "Name")));
			if (Field(p, "Club.UniqueId") is { Length: > 0 } cid && Field(p, "Club.Name") is { Length: > 0 } cname && names.All(n => n.Id != cid))
				names.Add(new SaveName(SaveName.Club, cid, cname));
		}
		SeenNames.Keep("shortlist", await SeenKey(), names);
	}

	/// <summary>
	/// Where names seen on a page hold: our club's id and the save's name, as the game gives them
	/// ("" when either cannot be read). Another save of the same club keeps its own.
	/// </summary>
	private static async Task<string> SeenKey()
	{
		try
		{
			var club = Field(await Fm.Pull("Human.Club", "UniqueId"), "UniqueId");
			var save = Field(await Fm.Pull("app.LastSaveSummary", "GameSummaryGameName"), "GameSummaryGameName");
			return club.Length > 0 && save.Length > 0 ? $"{club}|{save}" : "";
		}
		catch (Exception e) when (e is FmError or FmUnavailable or FmBusy) { return ""; }
	}

	private static async Task<int> PrintSaveNames()
	{
		foreach (var n in await SaveNames()) Console.WriteLine($"{n.Kind}\t{n.Id}\t{n.Name}");
		return 0;
	}

	/// <summary>
	/// The players with offers in the club's transfer activity (ours and to us) and the clubs on
	/// the other side of each, by their ids. Empty when there is none or it cannot be read.
	/// </summary>
	private static async Task<List<SaveName>> TransferActivityNames()
	{
		var found = new List<SaveName>();
		try
		{
			var players = await Fm.Pull(Activity, "Person.Name,Person.UniqueId", list: true);
			if (players.ValueKind != JsonValueKind.Array) return found;
			foreach (var player in players.EnumerateArray())
			{
				if (Field(player, "Person.UniqueId") is { Length: > 0 } pid && Field(player, "Person.Name") is { Length: > 0 } pname)
					found.Add(new SaveName(SaveName.Player, pid, pname));
				var offers = await Fm.Pull($"{Field(player, "Row")}.TransferOffers", "BuyingClub.Name,BuyingClub.UniqueId,SellingClub.Name,SellingClub.UniqueId", list: true);
				if (offers.ValueKind != JsonValueKind.Array) continue;
				foreach (var o in offers.EnumerateArray())
					foreach (var side in new[] { "BuyingClub", "SellingClub" })
						if (Field(o, $"{side}.UniqueId") is { Length: > 0 } cid && Field(o, $"{side}.Name") is { Length: > 0 } cname)
							found.Add(new SaveName(SaveName.Club, cid, cname));
			}
		}
		catch (Exception e) when (e is FmError or FmUnavailable or FmBusy) { }
		return found;
	}

	/// <summary>
	/// The players the call names, put as the save knows them. False when a choice between two
	/// was asked and left unanswered.
	/// </summary>
	private static async Task<bool> NamePlayers(Router.Call call)
	{
		if (!PlayerArgs.TryGetValue(call.Tool, out var args) || !args.Any(a => call.Args[a] is JsonValue)) return true;
		var known = await SaveNames();
		foreach (var arg in args)
		{
			if (call.Args[arg] is not JsonValue v || !v.TryGetValue<string>(out var typed) || typed.Length == 0) continue;
			if (Named(typed, known, OpensPlayerPage.Contains(call.Tool)) is not { } name) return false;
			call.Args[arg] = name;
		}
		return true;
	}

	/// <summary>The name to give the command for what was typed; null when a choice was left unanswered.</summary>
	internal static string? Named(string typed, List<SaveName> known, bool byId = false)
	{
		var words = new Nl.Text(typed).Words.ToList();
		if (words.Count == 0) return typed;
		var players = NameFit.Who(words, known, SaveName.Player);
		// "frosinonenin rinaldi": the club's words are not his name.
		if (players.Count == 0 && NameFit.ClubFirst(words, known) is { } split)
		{
			words = [.. split.Left];
			players = NameFit.Who(words, known, SaveName.Player);
		}
		// "salifounun bilgilerini": a word the reader took into the name that is no part of one
		if (players.Count == 0 && NameFit.Within(words, known, SaveName.Player) is { } part) (words, players) = part;
		if (players.Count == 0) return string.Join(' ', words);
		var who = players[0];
		if (players.Count > 1)
		{
			// the one the user picked for these words before, while he is still in the save
			var key = string.Join(' ', words);
			if (Learned.IdFor(key, SaveName.Player, players.Select(p => p.Id)) is { } id) who = players.First(p => p.Id == id);
			else
			{
				if (!Ui.Interactive) return key;
				if (Ui.Ask(L($"Which player: '{typed}'?"), [.. players.Select(p => p.Name)], cancel: L("None")) is not { } at) return null;
				who = players[at];
				Learned.Picked(key, SaveName.Player, who.Id, who.Name);
			}
		}
		// Two of ours with one name are told apart by the id, as `player` reads it.
		return byId && known.Count(k => k.Kind == SaveName.Player && k.Name == who.Name) > 1 ? $"{who.Name} #{who.Id}" : who.Name;
	}
}
