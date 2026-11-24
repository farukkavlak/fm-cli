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
			saveNames = (key, names);
			return names;
		}
		catch (Exception e) when (e is FmError or HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException)
		{
			return [];
		}
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
			if (!Ui.Interactive) return string.Join(' ', words);
			if (Ui.Ask(L($"Which player: '{typed}'?"), [.. players.Select(p => p.Name)], cancel: L("None")) is not { } at) return null;
			who = players[at];
		}
		// Two of ours with one name are told apart by the id, as `player` reads it.
		return byId && known.Count(k => k.Kind == SaveName.Player && k.Name == who.Name) > 1 ? $"{who.Name} #{who.Id}" : who.Name;
	}
}
