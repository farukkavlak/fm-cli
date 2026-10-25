using System;
using System.Globalization;
using System.Text;
using FM.UI.MatchSimulation;

namespace FmCliProbe;

/// <summary>
/// Where everyone is, read from the game's own match simulation: the match screen's data holds
/// the ball and every player as an entity with a position, so nothing has to be guessed from a
/// picture. The binding tree only names the two lists, not the fields inside them, which is why
/// this goes through the interop types rather than a path.
/// </summary>
internal static partial class FmApi
{
	private static string Radar()
	{
		// The engine keeps the data itself, every tick of the match. The binding tree only holds it
		// while a screen that binds it is drawn, so that is the fallback rather than the way in.
		var simulation = Live();
		if (simulation == null)
		{
			try
			{
				var hits = FindAll("Match2DSimulationData", 8);
				for (var i = 0; i < hits.Count && simulation == null; i++)
				{
					var found = hits[i].Node.Value?.Get()?.TryCast<MatchSimulationData>();
					if (found is { Players.Count: > 0 }) simulation = found;
				}
			}
			catch { }
		}
		if (simulation == null)
		{
			try { simulation = MatchSimulationData.GetInstance(); }
			catch (Exception ex) { return $"error: {ex.GetType().Name}: {ex.Message}"; }
		}
		if (simulation == null) return "error: no match simulation (a match is not on)";

		var sb = new StringBuilder(4096);
		sb.Append("{\"ball\":");
		try
		{
			var ball = simulation.Ball;
			if (ball == null) sb.Append("null");
			else
			{
				sb.Append('{');
				Entity(sb, ball, comma: false);
				sb.Append('}');
			}
		}
		catch { sb.Append("null"); }

		sb.Append(",\"players\":[");
		var first = true;
		try
		{
			var players = simulation.Players;
			for (var i = 0; i < players.Count; i++)
			{
				PlayerEntityData player;
				try { player = players[i]; }
				catch { continue; }
				if (player == null) continue;

				if (!first) sb.Append(',');
				first = false;
				sb.Append('{');
				sb.Append("\"n\":").Append(player.ShirtNumber.ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"i\":").Append(player.PlayerIndex.ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"e\":").Append(player.EntityIndex.ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"sub\":").Append(player.IsSubstitute ? "true" : "false");
				sb.Append(",\"kit\":\"").Append(Hex(player.PrimaryColour)).Append('"');
				sb.Append(",\"txt\":\"").Append(Hex(player.TextColour)).Append('"');
				Entity(sb, player);
				sb.Append('}');
			}
		}
		catch (Exception ex)
		{
			sb.Append("],\"error\":\"").Append(ex.GetType().Name).Append("\"}");
			return sb.ToString();
		}

		sb.Append("],\"count\":").Append(simulation.Players.Count.ToString(CultureInfo.InvariantCulture)).Append('}');
		return sb.ToString();
	}

	/// <summary>
	/// The live simulation: the match screen's own component keeps the ball and the players in
	/// <c>m_data</c> and updates them on every tick. It is a MonoBehaviour, so the loaded scene can
	/// be asked for it; the ones that are not initialised yet have no data and are skipped.
	/// </summary>
	private static MatchSimulationData Live()
	{
		try
		{
			var parts = UnityEngine.Resources.FindObjectsOfTypeAll<FM.Match.Simulation.Match2DSimulation>();
			for (var i = 0; i < parts.Length; i++)
			{
				MatchSimulationData data;
				try { data = parts[i]?.m_data; }
				catch { continue; }
				if (data is { Players.Count: > 0 }) return data;
			}
		}
		catch { }
		return null;
	}

	/// <summary>A position, and the two more a body has: which way it faces and how high it is.</summary>
	private static void Entity(StringBuilder sb, MatchEntityData entity, bool comma = true)
	{
		if (entity == null) { sb.Append("null"); return; }
		var at = entity.Position;
		if (comma) sb.Append(',');
		sb.Append("\"x\":").Append(Number(at.x));
		sb.Append(",\"y\":").Append(Number(at.y));
		sb.Append(",\"rot\":").Append(Number(entity.Rotation));
		sb.Append(",\"h\":").Append(Number(entity.Height));
	}

	private static string Number(float value) =>
		float.IsNaN(value) || float.IsInfinity(value) ? "0" : value.ToString("0.##", CultureInfo.InvariantCulture);

	/// <summary>A colour as rrggbb, which a terminal can take as it is.</summary>
	private static string Hex(UnityEngine.Color colour) =>
		$"{(int)Math.Clamp(colour.r * 255, 0, 255):x2}{(int)Math.Clamp(colour.g * 255, 0, 255):x2}{(int)Math.Clamp(colour.b * 255, 0, 255):x2}";
}
