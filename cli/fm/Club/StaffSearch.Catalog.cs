using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static readonly Dictionary<string, string> PositionNames = new(StringComparer.OrdinalIgnoreCase)
	{
		["GK"] = "Goalkeeper", ["DR"] = "Defender (Right)", ["DL"] = "Defender (Left)", ["DC"] = L("DC"),
		["WBR"] = "Wing-Back (Right)", ["WBL"] = "Wing-Back (Left)", ["DM"] = L("DM"),
		["MR"] = L("MR"), ["ML"] = L("ML"), ["MC"] = L("MC"),
		["AMR"] = L("AMR"), ["AML"] = L("AML"), ["AMC"] = L("AMC"),
		["ST"] = L("ST"), ["STC"] = L("ST"),
	};

	/// <summary>
	/// The attributes by their English names (folded) and the game's own id for each, which the search
	/// panel's rows carry in every language.
	/// </summary>
	private static readonly Dictionary<string, string> AttributeCodes = new()
	{
		["finishing"] = "Pfin", ["dribbling"] = "Pdri", ["firsttouch"] = "Pfrs", ["heading"] = "Phea", ["corners"] = "Pcor",
		["marking"] = "Pmar", ["crossing"] = "Pcro", ["passing"] = "Ppas", ["penaltytaking"] = "Ppen", ["freekicks"] = "Pfrk",
		["freekicktaking"] = "Pfrk", ["technique"] = "Ptec", ["tackling"] = "Ptac", ["longshots"] = "Plsh", ["longthrows"] = "Plth",
		["aggression"] = "Pagg", ["bravery"] = "Pbra", ["workrate"] = "Pwkr", ["decisions"] = "Pdec", ["determination"] = "Pdet",
		["concentration"] = "Pcnt", ["leadership"] = "Pinf", ["anticipation"] = "Pant", ["flair"] = "Pfla", ["positioning"] = "Ppsn",
		["composure"] = "Pcmp", ["teamwork"] = "Ptwr", ["offtheball"] = "Potb", ["vision"] = "Pcre",
		["agility"] = "Pagi", ["stamina"] = "Psta", ["balance"] = "Pbal", ["strength"] = "Pstr", ["pace"] = "Ppac",
		["acceleration"] = "Pacc", ["naturalfitness"] = "Pnft", ["jumpingreach"] = "Pjum",
		["rushingout"] = "Prou", ["rushingouttendency"] = "Prou", ["oneonones"] = "Pooo", ["commandofarea"] = "Pcoa", ["kicking"] = "Pkic",
		["eccentricity"] = "Pecc", ["handling"] = "Phan", ["throwing"] = "Pthr", ["aerialreach"] = "Paab", ["communication"] = "Pcom",
		["reflexes"] = "Pref", ["punching"] = "Pttp", ["punchingtendency"] = "Pttp",
	};

	/// <summary>
	/// Each attribute's number on a player's card (the game's attribute type: 42 is Pace) and its name as
	/// the game writes it in English.
	/// </summary>
	private static readonly Dictionary<string, string> AttributeNames = new()
	{
		["0"] = "Aerial Reach", ["1"] = "Command Of Area", ["2"] = "Communication", ["3"] = "Eccentricity",
		["4"] = "Handling", ["5"] = "Kicking", ["6"] = "Reflexes", ["7"] = "Rushing Out (Tendency)",
		["8"] = "Punching (Tendency)", ["9"] = "Throwing", ["10"] = "One On Ones", ["11"] = "Crossing",
		["12"] = "Dribbling", ["13"] = "Finishing", ["14"] = "Heading", ["15"] = "Long Shots", ["16"] = "Long Throws",
		["17"] = "Marking", ["18"] = "Passing", ["19"] = "Penalty Taking", ["20"] = "Free Kick Taking",
		["21"] = "Tackling", ["22"] = "Technique", ["23"] = "First Touch", ["24"] = "Corners", ["25"] = "Aggression",
		["26"] = "Anticipation", ["27"] = "Bravery", ["28"] = "Vision", ["29"] = "Decisions", ["30"] = "Determination",
		["31"] = "Flair", ["32"] = "Leadership", ["33"] = "Off The Ball", ["34"] = "Positioning", ["35"] = "Teamwork",
		["36"] = "Work Rate", ["37"] = "Composure", ["38"] = "Concentration", ["39"] = "Acceleration", ["40"] = "Agility",
		["41"] = "Balance", ["42"] = "Pace", ["43"] = "Stamina", ["44"] = "Strength", ["45"] = "Jumping Reach",
		["46"] = "Natural Fitness"
	};

	/// <summary>An attribute as the page shows it; the English name put into the game's words where the page gave none.</summary>
	internal static string AttributeShown(JsonElement attribute) =>
		Field(attribute, "Shown") is { Length: > 0 } shown ? shown : GameWords.Of(Field(attribute, "Name"));

	/// <summary>
	/// A player's card with each attribute named by its id: in another language the name read back
	/// from the words can be another attribute's (Pace comes back as Speed in a Turkish game).
	/// </summary>
	private static JsonElement WithAttributeNames(JsonElement card)
	{
		if (!card.TryGetProperty("Attributes", out var list) || !list.EnumerateArray().Any(a => AttributeNames.ContainsKey(Field(a, "Id"))))
			return card;
		var node = System.Text.Json.Nodes.JsonNode.Parse(card.GetRawText())!;
		foreach (var attribute in node["Attributes"]!.AsArray())
			if (attribute?["Id"]?.GetValue<string>() is { } id && AttributeNames.TryGetValue(id, out var english))
				attribute["Name"] = english;
		return JsonDocument.Parse(node.ToJsonString()).RootElement;
	}

	/// <summary>
	/// The search panel's Apply (true) and Cancel (false) as the game itself tells them apart: the
	/// panel's two buttons stand for the same event, and what differs is the payload each sends —
	/// true for keeping what was set, false for dropping it — which does not change with the
	/// language. Null when the panel is not open.
	/// </summary>
	private static async Task<string> PanelSent(bool apply)
	{
		var sent = (await Fm.GetJson("buttons", ("json", "1"), ("gen", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains("EventFmxFilterRestoreFilter", StringComparison.Ordinal)
				&& Field(b, "SendsProps") == (apply ? "True" : "False"));
		return sent!.ValueKind == JsonValueKind.Object ? Field(sent, "Pointer") : null;
	}

	// The player search panel, read by the data paths the game binds its controls to instead of by
	// the words on it, which another language changes: the panel itself by a heading it binds
	// ("Position"), each rule by the path its own control writes, and an attribute row's name and
	// its two controls ("rule.…") among the panel's items.
	private const string SearchPanel = "filter.FilterRulePlayerPosition.Name";
	private const string SearchCompare = "rule.selectedcompareflag";
	private const string SearchValue = "rule.minvalue";
	private const string SearchMaxValue = "rule.maxvalue";
	// A search rule's row picked by the game's own value for it, not its words: "id:" then the value.
	private const string ById = "id:";
	// The comparison list's flags (rule.selectedcompareflag): "Is At Least", "Is At Most".
	private const string CompareAtLeast = "4";
	private const string CompareAtMost = "8";
	// Status flags in the search's lists: on the transfer list (transfer status), on the loan list (loan
	// status); a contract run out (contract status).
	private const string TransferListed = ById + "1";
	private const string LoanListed = ById + "2";
	private const string ContractExpired = ById + "2";
}
