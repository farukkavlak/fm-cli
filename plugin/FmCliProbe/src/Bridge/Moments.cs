using System;
using System.Globalization;
using System.Text;

namespace FmCliProbe;

/// <summary>
/// What the match has said so far, each line with the game's own kind of event: the match keeps
/// every commentary event in its frame with an id, and the match screen sorts those ids into the
/// moments it puts up as a banner (goal, card, substitution, injury, VAR, a goal ruled out). That
/// sorting is the game's, so a key moment is known by it rather than by words in the line.
/// </summary>
internal static partial class FmApi
{
	/// <summary>
	/// The commentary events from index <paramref name="from"/> up to the last one played: id, kind,
	/// importance, team, time slice (a quarter of a second) and the line. The list is worked out ahead
	/// of the playback (202 events with 135 played), so the rest would tell what is still to come.
	/// "Now" is the frame's own time slice, so a caller can place them.
	/// </summary>
	private static string Moments(string from)
	{
		var frame = LiveFrame();
		if (frame == null) return "error: no match frame (a match is not on)";

		Il2CppSystem.Collections.Generic.List<FM.Match.MatchCommentaryEvent> events;
		try { events = frame.CommentaryEvents; }
		catch (Exception ex) { return $"error: {ex.GetType().Name}: {ex.Message}"; }
		var start = int.TryParse(from, NumberStyles.Integer, CultureInfo.InvariantCulture, out var f) ? Math.Max(0, f) : 0;

		var sb = new StringBuilder();
		sb.Append("{\"Now\":").Append(frame.CurrentTimeSlice.ToString(CultureInfo.InvariantCulture));
		sb.Append(",\"Time\":").Append(frame.CurrentTime.ToString("0.##", CultureInfo.InvariantCulture));
		sb.Append(",\"Last\":").Append(frame.CurrentLastEventIndex.ToString(CultureInfo.InvariantCulture));
		var count = Math.Min(events?.Count ?? 0, frame.CurrentLastEventIndex + 1);
		sb.Append(",\"Count\":").Append(count.ToString(CultureInfo.InvariantCulture)).Append(",\"Events\":[");
		var first = true;
		for (var i = start; i < count; i++)
		{
			FM.Match.MatchCommentaryEvent e;
			try { e = events[i]; }
			catch { continue; }
			if (e == null) continue;

			string kind;
			try { kind = FM.Match.FMMatchUIComponent.CommentaryEventToLowerThirdType(e.CommentaryEventID).ToString(); }
			catch { kind = ""; }
			string line;
			try { line = e.TextPresent is { Length: > 0 } text ? string.Join(" ", text) : ""; }
			catch { line = ""; }

			if (!first) sb.Append(',');
			first = false;
			sb.Append("{\"Index\":").Append(i.ToString(CultureInfo.InvariantCulture));
			sb.Append(",\"Id\":").Append(((int)e.CommentaryEventID).ToString(CultureInfo.InvariantCulture));
			sb.Append(",\"Event\":\"").Append(e.CommentaryEventID.ToString()).Append('"');
			sb.Append(",\"Kind\":\"").Append(kind).Append('"');
			sb.Append(",\"Importance\":\"").Append(e.CommentaryImportance.ToString()).Append('"');
			sb.Append(",\"Team\":\"").Append(e.Team.ToString()).Append('"');
			sb.Append(",\"Slice\":").Append(e.TimeSlice.ToString(CultureInfo.InvariantCulture));
			sb.Append(",\"Line\":\"").Append(Report.Escape(line)).Append("\"}");
		}
		sb.Append("]}\n");
		return sb.ToString();
	}

	/// <summary>The match's own frame: kept by the match MonoBehaviour, else by the match screen.</summary>
	private static FM.Match.MatchFrame LiveFrame()
	{
		try
		{
			var matches = UnityEngine.Resources.FindObjectsOfTypeAll<FM.Match.Match>();
			for (var i = 0; i < matches.Length; i++)
			{
				FM.Match.MatchFrame frame;
				try { frame = matches[i]?.m_matchFrame; }
				catch { continue; }
				if (frame?.CommentaryEvents != null) return frame;
			}
			var screens = UnityEngine.Resources.FindObjectsOfTypeAll<FM.Match.FMMatchUIComponent>();
			for (var i = 0; i < screens.Length; i++)
			{
				FM.Match.MatchFrame frame;
				try { frame = screens[i]?.m_currentMatchFrame; }
				catch { continue; }
				if (frame?.CommentaryEvents != null) return frame;
			}
		}
		catch { }
		return null;
	}
}
