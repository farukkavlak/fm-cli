using System;
using System.Collections.Generic;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// Where the game stands: the date, whether it is busy, and what its Continue button is waiting
/// for. Unlike a screen's data these are bound under fixed names at the root — "game" and
/// "continue" — whatever page is up, so they are read by path rather than searched for.
/// </summary>
internal static class State
{
	public static string Current()
	{
		var bindings = Game.Bindings;
		if (bindings == null) return "error: bindings unavailable";

		var fields = new Dictionary<string, string>();

		// ContinueFlowState: CanContinue, NeedAction, PressConference, Tactics, TeamNews,
		// TeamTalk, TunnelInterview, Processing, Match, Waiting, Saving, OnHoliday…
		Put(fields, "State", Read(bindings, "continue.State"));
		Put(fields, "Button", Read(bindings, "continue.ContinueButtonMainString"));
		// What kind of thing waits for an answer, by number: the same in every language, where the button's words are not.
		Put(fields, "Respond", Read(bindings, "continue.ContinueButtonMustRespondGrouping"));
		Put(fields, "Processing", Read(bindings, "game.GameProcessing"));
		Put(fields, "Saving", Read(bindings, "game.GameSaving"));
		Put(fields, "Waiting", Read(bindings, "game.GameWaiting"));
		Put(fields, "CanSave", Read(bindings, "game.CanSave"));
		Put(fields, "DuringMatch", Read(bindings, "game.IsDuringMatch"));

		// The language the game is set to: what the reverse table and the CLI's caches are kept by.
		try { fields["Language"] = SI.Translation.TranslationManager.CurrentLanguage ?? ""; } catch { }

		// The packed date as the game gives it, not as a day: its clock bits are wanted too.
		var date = Date(Read(bindings, "game.CurrentGameDate", raw: true));
		if (date != null)
		{
			fields["Date"] = date.Value.Day.ToString("yyyy-MM-dd");
			fields["Clock"] = date.Value.Clock.ToString();
		}

		// A match keeps its scoreboard under the match screen's own panel, so it is looked for
		// by name, and only while there is a match to look in.
		if (fields.TryGetValue("DuringMatch", out var during) && during == "True") Scoreboard(fields);

		// The commentary is only read to the user: it stays in the game's words.
		return Report.Json(fields, ["Commentary"]) + "\n";
	}

	private static FM.UI.ContinueManagerModule continueManager;

	/// <summary>
	/// The Continue button's own module, as it holds the flow: its flags (what the game is in:
	/// Processing, Waiting, Messages…), its flow state, whether it is processing, an instant result
	/// being worked out, and whether its processing card's timer runs. Read only; for telling when a
	/// press has been dealt with.
	/// </summary>
	public static string ContinueFlags()
	{
		try
		{
			if (continueManager == null)
			{
				var found = UnityEngine.Resources.FindObjectsOfTypeAll<FM.UI.ContinueManagerModule>();
				for (var i = 0; i < found.Length && continueManager == null; i++)
					if (found[i] != null && found[i].isActiveAndEnabled) continueManager = found[i];
			}
			var m = continueManager;
			if (m == null) return "error: no continue manager";
			var flags = m.m_gameStateFlags;
			var named = new List<string>();
			foreach (FM.UI.ContinueManagerModule.ContinueFlowFlags f in Enum.GetValues(typeof(FM.UI.ContinueManagerModule.ContinueFlowFlags)))
				if (f != 0 && (flags & f) == f) named.Add(f.ToString());
			var fields = new Dictionary<string, string>
			{
				["Flags"] = string.Join(",", named),
				["FlagBits"] = ((long)flags).ToString(),
				["Flow"] = m.m_continueState?.Value.ToString() ?? "",
				["IsProcessing"] = m.IsProcessing.ToString(),
				["InstantResult"] = m.m_isProcessingInstantResult.ToString(),
				["ProcessingCard"] = (m.m_processingCardTimerCoroutine != null).ToString(),
				["Action"] = (m.m_currentAction != null).ToString(),
			};
			return Report.Json(fields) + "\n";
		}
		catch (Exception e) { continueManager = null; return $"error: {e.GetType().Name}: {e.Message}"; }
	}

	/// <summary>
	/// Whether a save is loaded: the in-game Continue button's flow is bound only then. (The game
	/// date is not enough: match-day screens bind none.)
	/// </summary>
	public static bool InSave() => Game.Bindings is { } bindings && Read(bindings, "continue.State") != null;

	private static readonly Dictionary<string, string> MatchFields = new()
	{
		["MatchTime"] = "MatchTime",
		["MatchHomeScore"] = "HomeScore",
		["MatchAwayScore"] = "AwayScore",
		["MatchStatePeriod"] = "Period",
		["IsHomeTeamHuman"] = "HumanIsHome",
		["IsPlaying"] = "Playing",
		["MatchHighlightCommentary"] = "Commentary",
	};

	private static void Scoreboard(Dictionary<string, string> fields)
	{
		var missing = MatchFields.Count;
		foreach (var (_, node) in Report.Walk())
		{
			var name = node.Name;
			if (name == null || !MatchFields.TryGetValue(name, out var field) || fields.ContainsKey(field)) continue;

			string text;
			try
			{
				var value = node.Value;
				if (value == null || value.IsNull) continue;
				text = field == "Commentary" ? Text.ShownOf(value) : Text.Of(value);
			}
			catch { continue; }

			if (string.IsNullOrEmpty(text)) continue;
			fields[field] = text;
			if (--missing == 0) return;
		}
	}

	private static void Put(Dictionary<string, string> fields, string name, string value)
	{
		if (!string.IsNullOrEmpty(value)) fields[name] = value;
	}

	private static string Read(Bindings bindings, string path, bool raw = false)
	{
		try
		{
			var key = PathKey.Of(path);
			if (!bindings.Exists(ref key)) return null;

			var value = bindings.Get(ref key);
			return value == null || value.IsNull ? null : raw ? value.AsString() : Text.Of(value);
		}
		catch { return null; }
	}

	/// <summary>
	/// The game packs a date into one number: the year above bit 17, then the day of the year
	/// (from 1) in nine bits, then eight bits of clock whose unit is not yet known.
	/// </summary>
	internal static (DateTime Day, int Clock)? Date(string text)
	{
		if (text == null) return null;

		var at = text.IndexOf("Data1 = ", StringComparison.Ordinal);
		if (at < 0) return null;

		var digits = "";
		for (var i = at + 8; i < text.Length && char.IsDigit(text[i]); i++) digits += text[i];
		if (!long.TryParse(digits, out var packed)) return null;

		var year = (int)(packed >> 17);
		var dayOfYear = (int)((packed >> 8) & 0x1FF);
		if (year < 1900 || year > 2200 || dayOfYear < 1 || dayOfYear > 366) return null;

		return (new DateTime(year, 1, 1).AddDays(dayOfYear - 1), (int)(packed & 0xFF));
	}
}
