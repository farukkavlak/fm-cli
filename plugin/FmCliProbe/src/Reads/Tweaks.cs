using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using SI.Bindable;
using SI.Bindable.Reference.Core;

namespace FmCliProbe;

/// <summary>
/// FM27's in-match tactical tweaks ("Take a Breather", "Lock It Down"…): the team's list, which
/// of them are on, and the changes waiting for the match screen's Confirm.
///
/// Everything is held by the game's own ids: a tweak by its FM.UI.TacticalTweak value (TweakID),
/// the list by its data path (data.TacticalTweakTeamOptions), the match's tactic by its type
/// (FM.UI.MatchTacticReference): the node holding it is named afresh each match (MatchUI2C,
/// MatchUI2F…), so it is found by what it holds, nearest the root first. Names come from each tweak's own translation id, so they are in the game's
/// language and are never matched against.
///
/// A row's click sends ToggleTacticalTweakEvent; ?toggle= sends the same event, so no row has to
/// be on screen. The game does not change the tactic at once: it adds an operation to
/// TacticPendingTweakOps (1 apply, 2 remove, in order) and works them through when the tactics
/// panel is closed with Confirm.
/// </summary>
internal static class Tweaks
{
	private const string Options = "data.TacticalTweakTeamOptions";

	/// <summary>The path of the node nearest the root that holds the match's tactic; null when none does.</summary>
	private static string Tactic()
	{
		foreach (var (path, node) in Report.Walk())
		{
			try
			{
				if (node.Value is { IsNull: false } held && held.Get()?.TryCast<FM.UI.MatchTacticReference>() != null)
					return path.TrimStart('.');
			}
			catch { }
		}
		return null;
	}

	/// <summary>
	/// The match's period as the match screen's own component keeps it (SI.Match.MatchPeriod): there
	/// on a talk screen too, where the scoreboard's bound fields are not. Empty without a match.
	/// </summary>
	internal static (string State, string Match) Period()
	{
		try
		{
			foreach (var component in UnityEngine.Resources.FindObjectsOfTypeAll<FM.Match.FMMatchUIComponent>())
			{
				var state = component?.m_matchUIState;
				if (state == null) continue;
				return (state.m_matchStatePeriod.Value.ToString(), state.m_matchPeriod.Value.ToString());
			}
		}
		catch (Exception e) { return ($"<{e.GetType().Name}: {e.Message}>", ""); }
		return ("", "");
	}

	/// <summary>Every node holding a match tactic, with the object it holds: a check that they are one.</summary>
	private static string Where()
	{
		var sb = new System.Text.StringBuilder();
		foreach (var (path, node) in Report.Walk())
		{
			try
			{
				if (node.Value is { IsNull: false } held && held.Get()?.TryCast<FM.UI.MatchTacticReference>() is { } tactic)
					sb.AppendLine($"{path}\t0x{tactic.Pointer.ToInt64():x}");
			}
			catch { }
		}
		return sb.Length > 0 ? sb.ToString() : "none\n";
	}

	public static string Handle(NameValueCollection q)
	{
		if (q["where"] == "1") return Where();
		if (q["period"] == "1") { var (state, match) = Period(); return $"{state}\t{match}\n"; }
		if (!string.IsNullOrEmpty(q["toggle"])) return Toggle(q["toggle"], q["off"] == "1", q["dry"] == "1");
		return Current(q["gen"]);
	}

	/// <summary>The team tweaks the game offers, in its own order.</summary>
	private static List<FM.UI.TacticalTweakEntry> Team(out string error)
	{
		var list = new List<FM.UI.TacticalTweakEntry>();
		var value = FmApi.ValueAt(Options, out error);
		// The list is in the tree only once the tactics panel has bound it; binding it here makes it
		// arrive on the next frame, so the caller asks again.
		if (value == null)
		{
			RawPath.Bind(Options);
			error = $"error: binding {Options}; ask again";
			return list;
		}
		var entries = value.TryCast<Il2CppSystem.Collections.Generic.List<FM.UI.TacticalTweakEntry>>();
		if (entries == null)
		{
			error = $"error: {Options} is not a list of tweaks ({value.GetIl2CppType()?.Name})";
			return list;
		}
		for (var i = 0; i < entries.Count; i++)
			if (entries[i] != null) list.Add(entries[i]);
		return list;
	}

	private static string Current(string generation)
	{
		var team = Team(out var error);
		if (error != null) return error;

		var rows = team.Select(entry =>
		{
			string name;
			try { name = Text.Shown(entry.NameTranslationID.TranslatedString ?? ""); } catch { name = ""; }
			var conflicts = new List<string>();
			try
			{
				var set = entry.ConflictSet;
				for (var i = 0; i < (set?.Length ?? 0); i++)
					if (set[i] != null && set[i].TweakID != entry.TweakID) conflicts.Add(Quote(set[i].TweakID.ToString()));
			}
			catch { }
			return $"{{\"Id\": {Quote(entry.TweakID.ToString())}, \"Number\": \"{(int)entry.TweakID}\", \"Name\": {Quote(name)}, " +
			       $"\"Conflicts\": [{string.Join(", ", conflicts)}]}}";
		});

		var tactic = Tactic();
		if (tactic == null) return "error: no match tactic in the tree";
		var (period, _) = Period();
		return $"{{\"Period\": {Quote(period)}, \"Team\": [{string.Join(", ", rows)}], \"Active\": {Active(tactic, generation)}, \"Pending\": {Pending(tactic)}}}\n";
	}

	/// <summary>The tweaks the match's tactic has on now: TacticActiveTweaks, a list of references.</summary>
	private static string Active(string tactic, string generation)
	{
		var pulled = RawPath.Pull($"{tactic}.TacticActiveTweaks", "TacticTweakID,TacticTweakAxis,TacticTweakTargetPlayerIndex", list: true, generation ?? "tw");
		return pulled.StartsWith("error", StringComparison.Ordinal) ? "[]" : pulled.Trim();
	}

	/// <summary>The operations waiting for Confirm, in the order they were made.</summary>
	private static string Pending(string tactic)
	{
		var value = FmApi.ValueAt($"{tactic}.TacticPendingTweakOps", out var error);
		if (error != null) RawPath.Bind($"{tactic}.TacticPendingTweakOps");
		var ops = error == null ? value?.TryCast<Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>>() : null;
		var rows = new List<string>();
		var keys = new Il2CppSystem.Collections.Generic.List<PropertyID>();
		for (var i = 0; i < (ops?.Count ?? 0); i++)
		{
			var record = ops[i]?.Get()?.TryCast<DynamicReference>();
			if (record == null) continue;
			keys.Clear();
			record.GetProperties(keys);
			var fields = new List<string>();
			foreach (var key in keys)
			{
				string text;
				try { text = Text.Of(record[key.ID]); } catch { text = ""; }
				fields.Add($"{Quote(Schema.NameOf(key))}: {Quote(text)}");
			}
			rows.Add("{" + string.Join(", ", fields) + "}");
		}
		return "[" + string.Join(", ", rows) + "]";
	}

	/// <summary>Sends what a team tweak's row sends when clicked: put it on, or with ?off=1 take it off.</summary>
	private static string Toggle(string wanted, bool off, bool dry)
	{
		if (!Enum.TryParse<FM.UI.TacticalTweak>(wanted, ignoreCase: true, out var id))
			return $"error: '{wanted}' is not a FM.UI.TacticalTweak";
		var entry = Team(out var error).FirstOrDefault(e => e.TweakID == id);
		if (error != null) return error;
		if (entry == null) return $"error: {id} is not among the team tweaks the game offers now";

		var path = Tactic();
		if (path == null) return "error: no match tactic in the tree";
		var tactic = FmApi.ValueAt(path, out error)?.TryCast<FM.UI.MatchTacticReference>();
		if (tactic == null) return error ?? $"error: {path} is not the match's tactic";
		if (dry) return $"dry run, NOT sent: ToggleTacticalTweakEvent {id} off={off}\n";

		var events = Game.Events;
		if (events == null) return "error: event subsystem unavailable";
		var payload = new FM.UI.ToggleTacticalTweakEvent { Target = tactic, Tweak = entry, TurnOff = off };
		events.SendEvent(payload.TryCast<SI.Core.EventBase>());
		return $"sent ToggleTacticalTweakEvent {id} off={off}\n";
	}

	private static string Quote(string text) => $"\"{Report.Escape(text ?? "")}\"";
}
