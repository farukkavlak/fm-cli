using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>One team tweak as the game offers it, with what it is now and what it will be.</summary>
	private sealed record Tweak(string Id, string Name, List<string> Conflicts, bool On, bool WillBeOn);

	/// <summary>
	/// The team tweaks of FM27's match tactics ("Active Tactical Tweaks"): which are on, and which
	/// change when the match goes on.
	///
	/// Held by the game's own ids: a tweak is its FM.UI.TacticalTweak value (TakeABreather…); its name
	/// is the game's translation of it, shown as is and matched only against what was typed. What is
	/// on is the match tactic's TacticActiveTweaks; a change waits in TacticPendingTweakOps (1 apply,
	/// 2 remove, in order) until play goes on.
	/// </summary>
	private static async Task<List<Tweak>> ReadTweaks()
	{
		// The plugin binds the game's list when it is not yet in the tree; it is there a frame later.
		JsonElement read = default;
		await Until(async () =>
		{
			var said = await Fm.Get("tweaks", ("gen", $"t{Environment.TickCount64}"));
			if (said.StartsWith("error: binding", StringComparison.Ordinal)) return false;
			if (said.StartsWith("error", StringComparison.Ordinal)) return true;
			read = JsonDocument.Parse(said).RootElement;
			// A row of the active list bound just now has its fields a frame later.
			return read.GetProperty("Active").EnumerateArray().All(a => Field(a, "TacticTweakID").Length > 0);
		}, seconds: 5, every: 0.1);
		if (read.ValueKind != JsonValueKind.Object) return [];
		var on = read.GetProperty("Active").EnumerateArray().Select(a => Field(a, "TacticTweakID"))
			.Where(n => n.Length > 0).ToHashSet();
		var team = read.GetProperty("Team").EnumerateArray().ToList();
		var byNumber = team.ToDictionary(t => t.GetProperty("Number").ToString(), t => Field(t, "Id"));

		// The pending operations in order: the last one for a tweak is what it will be.
		var will = on.Select(n => byNumber.GetValueOrDefault(n, "")).ToHashSet();
		var now = new HashSet<string>(will);
		foreach (var op in read.GetProperty("Pending").EnumerateArray())
		{
			if (Field(op, "TacticTweakTargetPlayerIndex") is { Length: > 0 } player && player != "-1") continue;
			if (!byNumber.TryGetValue(Field(op, "TacticTweakID"), out var id)) continue;
			if (Field(op, "TacticTweakPendingOpKind") == "1") will.Add(id);
			else if (Field(op, "TacticTweakPendingOpKind") == "2") will.Remove(id);
		}

		return team.Select(t => new Tweak(Field(t, "Id"), Field(t, "Name"),
			t.GetProperty("Conflicts").EnumerateArray().Select(c => c.GetString() ?? "").ToList(),
			now.Contains(Field(t, "Id")), will.Contains(Field(t, "Id")))).ToList();
	}

	/// <summary>The periods a tweak is taken in: both halves and half time (measured); extra time is taken to be alike, not measured.</summary>
	private static readonly HashSet<string> TweakPeriods = ["FirstHalf", "HalfTime", "SecondHalf", "ExtraTimeFirstHalf", "ExtraTimeHalfTime", "ExtraTimeSecondHalf"];

	private static async Task<int> Tweaks(bool json, string[] args)
	{
		var state = await Fm.GetJson("state");
		if (Field(state, "DuringMatch") != "True")
		{
			Console.Error.WriteLine(L("Tactical tweaks are only during a match."));
			return 1;
		}
		// Before kick-off a tweak is taken and dropped at kick-off (measured three times, Apr 2028, with
		// and without the assistant's advice applied). The period is the game's SI.Match.MatchPeriod as
		// the match screen's component keeps it: the scoreboard's is not bound on a talk screen.
		var period = (await Fm.Get("tweaks", ("period", "1"))).Split('\t')[0].Trim();
		if (!TweakPeriods.Contains(period))
		{
			Console.Error.WriteLine(period is "" or "KickOff"
				? L("Tactical tweaks can be made once the match has kicked off.")
				: L("Tactical tweaks are made while the match is played; it is over."));
			return 1;
		}

		if (Option(args, "--player") is { } playerName) return await PlayerTweaks(json, args, playerName);
		if (args.Contains("--reset")) return await ResetTweaks(args);

		var tweaks = await ReadTweaks();
		if (tweaks.Count == 0)
		{
			Console.Error.WriteLine(L("The game offers no tactical tweaks now."));
			return 1;
		}
		if (json && Words(args).FirstOrDefault() is null)
		{
			Console.WriteLine(JsonOut.Of(tweaks));
			return 0;
		}

		string Mark(Tweak t) => (t.On, t.WillBeOn) switch
		{
			(true, true) => L("on"),
			(false, true) => L("on when play goes on"),
			(true, false) => L("off when play goes on"),
			_ => L("off"),
		};
		string Line(Tweak t) => t.On || t.WillBeOn ? $"{t.Name}  ({Mark(t)})" : t.Name;

		var words = Words(args).ToList();
		var off = args.Contains("--off");
		if (words.Count == 0 && Ui.Interactive && Ui.Ask(L("Tactical tweak"), tweaks.Select(Line).ToList(), cancel: L("Cancel")) is { } asked)
		{
			words.Add(tweaks[asked].Id);
			off = tweaks[asked].WillBeOn;
		}
		if (words.Count == 0)
		{
			foreach (var t in tweaks) Console.WriteLine($"  {(t.WillBeOn ? "✓" : " ")} {Line(t)}");
			Console.WriteLine(L("\nPut one on: tweak <name>   ·   take it off: tweak <name> --off"));
			return 0;
		}

		// The game's own id (TakeABreather) or its name in the game's language.
		var wanted = string.Join(" ", words);
		var names = tweaks.Select(t => t.Name).ToList();
		var hit = tweaks.FirstOrDefault(t => string.Equals(t.Id, wanted, StringComparison.OrdinalIgnoreCase));
		if (hit == null && Choose(names, wanted, L("Tactical tweak")) is { } named) hit = tweaks[names.IndexOf(named)];
		if (hit == null) return 1;

		if (hit.WillBeOn != off)
		{
			Console.WriteLine(L($"{hit.Name}: already {Mark(hit)}"));
			return 0;
		}
		if (args.Contains("--dry"))
		{
			Console.WriteLine(L($"(dry run) {hit.Name}: {(off ? L("off") : L("on"))}"));
			return 0;
		}

		var said = await Fm.Get("tweaks", ("toggle", hit.Id), ("off", off ? "1" : "0"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		// Read back: the game answers by adding the change to its pending list.
		Tweak? after = null;
		List<Tweak> now = [];
		var taken = await Until(async () =>
		{
			now = await ReadTweaks();
			after = now.FirstOrDefault(t => t.Id == hit.Id);
			return after != null && after.WillBeOn == !off;
		}, seconds: 5, every: 0.25);
		if (!taken || after == null)
		{
			Console.Error.WriteLine(L($"{hit.Name}: sent, but the game did not take the change."));
			return 1;
		}

		// With the match's tactics panel open the change waits for the panel's Confirm (its close
		// event with the confirm flag, not its label). Otherwise, while the ball is rolling, the game
		// makes it by itself within seconds; at a stop it waits for play to go on.
		var panelOpen = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Any(c => ClosesTactics(c, "Confirm") && Field(c, "Visible") == "True");
		if (panelOpen)
		{
			Console.WriteLine($"{hit.Name}: {Mark(after)}");
			Console.WriteLine(L("The match's tactics panel is open: the change is made when it is closed with Confirm Changes."));
			return 0;
		}
		if (Field(await Fm.GetJson("state"), "Playing") == "True")
			await Until(async () =>
			{
				now = await ReadTweaks();
				after = now.FirstOrDefault(t => t.Id == hit.Id) ?? after;
				return after.On == after.WillBeOn;
			}, seconds: 60, every: 0.5);
		Console.WriteLine($"{hit.Name}: {Mark(after)}");
		// The game takes off by itself what does not go with the new one (Go Long and Play Out of Defence).
		var dropped = now.Where(t => t.Id != hit.Id && !t.WillBeOn && tweaks.Any(b => b.Id == t.Id && b.WillBeOn)).Select(t => t.Name).ToList();
		if (dropped.Count > 0) Console.WriteLine(L($"The game takes off what does not go with it: {string.Join(", ", dropped)}"));
		return 0;
	}

	/// <summary>
	/// Puts up the match tactics' tweaks popup (the panel by its MatchOpenTactics event, the popup by its
	/// element tweaks-button) on one of its tabs (TeamTab, IndividualTab). True when it is up; whether the
	/// panel was opened here, so it is closed again with Confirm.
	/// </summary>
	private static async Task<(bool Up, bool Opened)> TweaksPopup(string tab)
	{
		var opened = false;
		var controls = await Fm.GetJson("buttons", ("json", "1"));
		if (!controls.EnumerateArray().Any(c => ClosesTactics(c, "Confirm") && Field(c, "Visible") == "True"))
		{
			// The match screen's own Tactics button (element "tactic"); other controls send the same event.
			await Fm.Get("press", ("event", "MatchOpenTactics"), ("name", "tactic"));
			opened = true;
		}
		async Task<bool> Tab() => !(await Fm.Get("click", ("within", "TabButtons"), ("name", tab))).StartsWith("error", StringComparison.Ordinal);
		// The popup's own click opens it and shuts it again: it is clicked only while its tabs are not up.
		if (await Tab()) return (true, opened);
		if (!await Until(async () => !(await Fm.Get("popup", ("name", "tweaks-button"))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.4))
			return (false, opened);
		var clicked = false;
		await Until(async () => clicked = await Tab(), seconds: 4, every: 0.3);
		return (clicked, opened);
	}

	/// <summary>Closes the match tactics panel with its Confirm (the close event's confirm flag), which makes the changes.</summary>
	private static async Task ConfirmTactics()
	{
		var confirm = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(c => ClosesTactics(c, "Confirm"));
		if (confirm.ValueKind == JsonValueKind.Object) await Fm.Get("press", ("ptr", Field(confirm, "Pointer")));
	}

	/// <summary>
	/// One player's individual tweaks (the popup's Individual tab): the ones for his position, which is on
	/// (the tactic's TacticActiveTweaks at his team selection index) and with a tweak named, put on or off.
	/// Held by the tweaks' ids and the player's UniqueId; names only shown and matched against what was typed.
	/// </summary>
	private static async Task<int> PlayerTweaks(bool json, string[] args, string playerName)
	{
		var (up, opened) = await TweaksPopup("IndividualTab");
		if (!up)
		{
			Console.Error.WriteLine(L("The tactical tweaks could not be opened."));
			return 1;
		}
		try
		{
			JsonElement read = default;
			await Until(async () =>
			{
				var said = await Fm.Get("tweaks", ("individual", "1"));
				if (said.StartsWith("error", StringComparison.Ordinal)) return false;
				read = JsonDocument.Parse(said).RootElement;
				return read.GetProperty("Players").GetArrayLength() >= 11;
			}, seconds: 8, every: 0.4);
			if (read.ValueKind != JsonValueKind.Object)
			{
				Console.Error.WriteLine(L("The individual tweaks could not be read."));
				return 1;
			}
			var players = read.GetProperty("Players").EnumerateArray().ToList();
			var hits = Matching(players, p => Field(p, "Name"), playerName);
			if (hits.Count != 1)
			{
				Console.Error.WriteLine(hits.Count == 0 ? L($"'{playerName}' is not on the pitch. On it: {string.Join(", ", players.Select(p => Field(p, "Name")))}")
					: L($"'{playerName}' matches more than one player: {string.Join(", ", hits.Select(p => Field(p, "Name")))}"));
				return 1;
			}
			var player = hits[0];
			// The tweaks for his position: each entry's ValidPositions mask holds the row's PlayerPosition.
			ulong.TryParse(Field(player, "Position"), out var position);
			var options = read.GetProperty("Options").EnumerateArray()
				.Where(o => position == 0 || !ulong.TryParse(Field(o, "Positions"), out var mask) || mask == 0 || (mask & position) != 0).ToList();

			async Task<HashSet<string>> OnFor()
			{
				var now = JsonDocument.Parse(await Fm.Get("tweaks", ("gen", $"p{Environment.TickCount64}"))).RootElement;
				var on = now.GetProperty("Active").EnumerateArray().Where(a => Field(a, "TacticTweakTargetPlayerIndex") == Field(player, "Index"))
					.Select(a => Field(a, "TacticTweakID")).ToHashSet();
				foreach (var op in now.GetProperty("Pending").EnumerateArray().Where(o => Field(o, "UniqueId") == Field(player, "Id")))
				{
					if (Field(op, "TacticTweakPendingOpKind") == "1") on.Add(Field(op, "TacticTweakID"));
					else on.Remove(Field(op, "TacticTweakID"));
				}
				return on;
			}
			var active = await OnFor();
			var words = Words(args).ToList();
			if (words.Count == 0)
			{
				if (json)
				{
					Console.WriteLine(JsonOut.Of(options.Select(o => new { Id = Field(o, "Id"), Name = Field(o, "Name"), On = active.Contains(Field(o, "Number")) })));
					return 0;
				}
				Console.WriteLine(Field(player, "Name"));
				foreach (var o in options) Console.WriteLine($"  {(active.Contains(Field(o, "Number")) ? "✓" : " ")} {Field(o, "Name")}");
				Console.WriteLine(L($"\nPut one on: tweak <name> --player \"{Field(player, "Name")}\"   ·   take it off: … --off"));
				return 0;
			}

			var wanted = string.Join(" ", words);
			var names = options.Select(o => Field(o, "Name")).ToList();
			var hit = options.Cast<JsonElement?>().FirstOrDefault(o => string.Equals(Field(o!.Value, "Id"), wanted, StringComparison.OrdinalIgnoreCase));
			if (hit == null && Choose(names, wanted, L("Tactical tweak")) is { } named) hit = options[names.IndexOf(named)];
			if (hit is not { } tweak) return 1;
			var off = args.Contains("--off");
			if (active.Contains(Field(tweak, "Number")) != off)
			{
				Console.WriteLine(L($"{Field(player, "Name")} · {Field(tweak, "Name")}: already {(off ? L("off") : L("on"))}"));
				return 0;
			}
			var said = await Fm.Get("tweaks", ("toggle", Field(tweak, "Id")), ("player", Field(player, "Id")), ("off", off ? "1" : "0"));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
			if (!await Until(async () => (await OnFor()).Contains(Field(tweak, "Number")) != off, seconds: 5, every: 0.25))
			{
				Console.Error.WriteLine(L($"{Field(tweak, "Name")}: sent, but the game did not take the change."));
				return 1;
			}
			// The change is made when the panel is closed with Confirm.
			await ConfirmTactics();
			opened = false;
			var made = await Until(async () =>
			{
				var now = JsonDocument.Parse(await Fm.Get("tweaks", ("gen", $"c{Environment.TickCount64}"))).RootElement;
				var isOn = now.GetProperty("Active").EnumerateArray().Any(a => Field(a, "TacticTweakTargetPlayerIndex") == Field(player, "Index") && Field(a, "TacticTweakID") == Field(tweak, "Number"));
				return isOn != off;
			}, seconds: 60, every: 0.5);
			Console.WriteLine(made ? $"{Field(player, "Name")} · {Field(tweak, "Name")}: {(off ? L("off") : L("on"))}"
				: L($"{Field(player, "Name")} · {Field(tweak, "Name")}: confirmed; the game makes it when play goes on."));
			return 0;
		}
		finally
		{
			if (opened) await ConfirmTactics();
		}
	}

	/// <summary>The popup's Reset, by its list's own values (resetscope: 0 team, 1 individual, 2 all), then confirmed.</summary>
	private static async Task<int> ResetTweaks(string[] args)
	{
		var scope = Words(args).FirstOrDefault() switch { "team" or "takim" => 0, "individual" or "bireysel" => 1, _ => 2 };
		var (up, _) = await TweaksPopup("TeamTab");
		if (!up)
		{
			Console.Error.WriteLine(L("The tactical tweaks could not be opened."));
			await ConfirmTactics();
			return 1;
		}
		var reset = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Binds") == "resetscope");
		if (reset.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L("The tweaks popup has no Reset."));
			await ConfirmTactics();
			return 1;
		}
		var said = await Fm.Get("select", ("ptr", Field(reset, "Pointer")), ("value", scope.ToString()), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) { await ConfirmTactics(); return Report(said); }
		await ConfirmTactics();
		bool Left(JsonElement now) => now.GetProperty("Active").EnumerateArray().Any(a => scope switch
		{
			0 => Field(a, "TacticTweakTargetPlayerIndex") is "" or "-1",
			1 => Field(a, "TacticTweakTargetPlayerIndex") is not ("" or "-1"),
			_ => true,
		});
		var cleared = await Until(async () => !Left(JsonDocument.Parse(await Fm.Get("tweaks", ("gen", $"r{Environment.TickCount64}"))).RootElement), seconds: 60, every: 0.5);
		Console.WriteLine(cleared ? L("Tactical tweaks reset.") : L("Reset sent; the game makes it when play goes on."));
		return 0;
	}
}
