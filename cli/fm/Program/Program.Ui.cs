using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// What can be pressed on the screen the game is showing. Unlabelled controls are icons and
	/// layout, and outnumber the rest, so they are left out unless asked for.
	/// </summary>
	private static async Task<int> Buttons(bool json, string[] args)
	{
		var controls = await Fm.GetJson("buttons", ("json", "1"));
		if (json)
		{
			Console.WriteLine(controls.ToString());
			return 0;
		}

		var all = args.Contains("--all");
		var filter = Words(args).FirstOrDefault();

		var rows = controls.EnumerateArray()
			.Where(c => all || Field(c, "Label") != "")
			.Select(c => new[]
			{
				Field(c, "Label"),
				Field(c, "Name"),
				Field(c, "Events") + (Field(c, "Enabled") == "True" ? "" : L("  (closed)")),
			})
			.Where(r => filter == null || r.Any(cell => Fold(cell).Contains(Fold(filter)) || Fold(GameWords.Of(cell)).Contains(Fold(filter))))
			.ToList();

		Table.Write(new[] { L("LABEL"), L("NAME"), L("WHAT IT SENDS") }, rows);
		return 0;
	}

	/// <summary>Every page the game has, read from its own menus, so none has to be open to be listed.</summary>
	private static async Task<int> PageList(bool json, string[] args)
	{
		var pages = await Fm.GetJson("pages");
		if (json)
		{
			Console.WriteLine(pages.ToString());
			return 0;
		}

		var filter = Words(args).FirstOrDefault();
		var rows = pages.EnumerateArray()
			.Select(p => new[] { Field(p, "Tab"), Field(p, "Page") })
			.Where(r => filter == null || r.Any(cell => Fold(cell).Contains(Fold(filter)) || Fold(GameWords.Of(cell)).Contains(Fold(filter))))
			.ToList();

		Table.Write(new[] { L("TAB"), L("PAGE") }, rows);
		return 0;
	}

	private static async Task<int> Open(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("usage: open \"<page>\" [--tab <tab>] [--dry]   (for the pages: `pages`)"));
			return 64;
		}

		if (args.Contains("--dry"))
		{
			var query = new List<(string, string)> { ("page", words[0]), ("dry", "1") };
			if (Option(args, "--tab") is { } tab) query.Add(("tab", tab));
			return Report(await Arriving("open", query.ToArray()));
		}

		return Report(await OpenNamed(words[0], Option(args, "--tab")));
	}

	/// <summary>
	/// Opens a page by its GUID (<see cref="Screen"/>); a page about a team (a squad list) is
	/// told which by the team's unique id, the club's own team when none is given. A page in two
	/// tabs' menus is opened under the one whose first page is <paramref name="under"/>.
	///
	/// The bridge chooses the page's row in its tab's menu, as a click does, which leaves a page
	/// that needs to know what it is about (the tactics planner) bound, and needs no name. A row
	/// with a submenu (Training ›) goes by bookmark.
	///
	/// Such a page is one panel in both places, and a panel already up keeps the form it was
	/// opened in: the tactics planner's team report, chosen again under Match Day, stays the team
	/// report and never shows the next-match heading. Another page is opened
	/// first so that the panel is built anew.
	/// </summary>
	internal static async Task<string> OpenPage(string screen, string? team = null, string? under = null)
	{
		// FM27's first-visit intro over a tab keeps a chosen page from opening.
		await Fm.Get("closeintro");
		var query = new List<(string, string)> { ("guid", screen) };
		if (team != null) query.Add(("object", team));
		if (under != null)
		{
			query.Add(("under", under));
			if (screen != Screen.Home && under != Screen.Home) await OpenWith([("guid", Screen.Home)]);
		}
		// FM27 keeps some pages only in a tab menu's submenu (Squad Registration under First Team) or
		// on another page's bar (Stages): unless that bar is up already, the page it belongs to is
		// opened, then the page chosen there.
		if (team == null && under == null && (await OnBar(screen)).StartsWith("error", StringComparison.Ordinal))
		{
			// "<parent guid>\t<its team>\t<name>": a youth team's page sits under that team's squad.
			var parent = await Fm.Get("pageparent", ("guid", screen));
			if (!parent.StartsWith("error", StringComparison.Ordinal) && parent.Split('\t') is [var guid, var owner, ..])
				return await OpenSubpage(guid, screen, () => Task.FromResult(true), owner == "-1" ? null : owner);
			if (Screen.OnBarOf.TryGetValue(screen, out var page))
				return await OpenSubpage(page, screen, () => Task.FromResult(true));
		}
		return await OpenWith(query.ToArray());
	}

	/// <summary>
	/// A page listed in another page's subtab menu (Squad Dynamics › Happiness), by its GUID: the
	/// page whose bar lists it is opened, then the row chosen on that bar. The first page can finish
	/// opening after the second and cover it, so the row is chosen again until the bar itself stands
	/// on the page and the page shows what <paramref name="shown"/> looks for.
	///
	/// The bar answers for the page only once the parent is built: a row chosen before that is lost
	/// (the press comes back "no page with guid …"), so the bar is waited on rather than paused on
	/// (measured: the bar answers 0.16–0.46 s after the parent is asked for; §8.246). What the bar
	/// stands on is the game's own word for the page being the one shown (?check=1). The content
	/// followed in 0.88–1.51 s (TR + EN, §8.302), so the upper bound is 8 s rather than 3 s.
	/// </summary>
	internal static async Task<string> OpenSubpage(string parent, string screen, Func<Task<bool>> shown, string? team = null, bool bar = false)
	{
		var opened = await OpenPage(parent, team);
		// A submenu's own first page (Portal › Competitions › Overview) can keep answering "open its
		// tab first" without the tab coming up (seen after the transfer search): the portal's own home
		// is opened by its page, and the page is chosen on its bar below.
		if (opened.StartsWith("error", StringComparison.Ordinal) && opened.Contains("ask again", StringComparison.Ordinal))
		{
			await OpenPage(Screen.Home);
			opened = await OpenPage(parent, team);
			if (opened.Contains("ask again", StringComparison.Ordinal)) opened = "";
		}
		if (opened.StartsWith("error", StringComparison.Ordinal)) return opened;
		var answer = "";
		for (var attempt = 0; attempt < 4; attempt++)
		{
			// The parent's own intro may have come up over it since.
			await Fm.Get("closeintro");
			// "shown"/"not shown" once the bar is there, an error while the parent is still coming up.
			await Until(async () => !(await OnBar(screen)).StartsWith("error", StringComparison.Ordinal), seconds: 6, every: 0.25);
			// With bar, the row is chosen on the parent's bar though a tab menu lists the page too (that
			// one is the first team's: the youth team's Individual Training, FM27).
			await Until(async () => !(answer = await (bar ? Fm.Get("open", ("guid", screen), ("bar", "1")) : Fm.Get("open", ("guid", screen))))
				.StartsWith("error", StringComparison.Ordinal), seconds: 6, every: 0.5);
			// Now and then the parent comes up without the bar ever answering for the page (Stages from
			// the squad page, 2 in 16): it is opened again rather than given up on.
			if (answer.StartsWith("error", StringComparison.Ordinal))
			{
				if (attempt == 3) return answer;
				// The parent can say it opened without its tab coming up (Portal › Competitions after the
				// transfer search): the portal's home is opened by its own page first.
				await OpenPage(Screen.Home);
				opened = await OpenPage(parent, team);
				if (opened.StartsWith("error", StringComparison.Ordinal) && !opened.Contains("ask again", StringComparison.Ordinal)) return opened;
				continue;
			}
			// The page under this one answers even while the parent draws over it, so it is taken only
			// when the bar stands on it and the content is there, held over two readings in a row.
			// A page with a bar of its own (a competition's page) puts that bar in place of the one that
			// listed it: the listing bar gone after the page answered "opened" is the page up, as long as
			// what it should show is there (a page further down checks its own bar).
			var standing = false;
			if (await Until(async () =>
			{
				var bar = await OnBar(screen);
				var on = (bar.StartsWith("shown", StringComparison.Ordinal) || bar.StartsWith("error", StringComparison.Ordinal) && answer.StartsWith("opened", StringComparison.Ordinal))
				         && await shown();
				var settled = on && standing;
				standing = on;
				return settled;
			}, seconds: 8, every: 0.3)) return answer;
		}
		return L($"error: {answer}, but the page did not show what was expected");
	}

	/// <summary>
	/// The row the subtab bar on screen stands on: "shown" when it is the given page, "not shown"
	/// otherwise, and an error while the page whose bar lists it is not up yet. Nothing is chosen.
	/// </summary>
	private static Task<string> OnBar(string screen) => Fm.Get("open", ("guid", screen), ("check", "1"));

	/// <summary>A page as the game's own menus name it, in the game's language: what someone types after `fm open`.</summary>
	private static async Task<string> OpenNamed(string page, string? tab = null)
	{
		// The plugin knows the pages by their English names; one typed as the game shows it is put back.
		static bool Is(string english, string typed) => Fold(english) == Fold(typed) || Fold(GameWords.Of(english)) == Fold(typed);
		if ((await Fm.GetJson("pages")).EnumerateArray().FirstOrDefault(p => Is(Field(p, "Page"), page) && (tab == null || Is(Field(p, "Tab"), tab))) is { ValueKind: JsonValueKind.Object } known)
			(page, tab) = (Field(known, "Page"), tab == null ? null : Field(known, "Tab"));
		return await OpenWith(tab == null ? [("page", page)] : [("page", page), ("tab", tab)]);
	}

	/// <summary>
	/// Why no page can be opened: a screen of its own (the round's results after a European night, a press
	/// conference) takes the menus away until it is moved past; in a match, its talks too, the pages wait for it to end.
	/// </summary>
	internal static async Task<string> PagesClosed()
	{
		var state = await Fm.GetJson("state");
		return Field(state, "DuringMatch") == "True"
			? L("Pages cannot be opened while a match is on. In a match: sub, shout, dugout, tactic role, talk; to go on: match")
			: Lf("The game is on a screen without a menu ({0}); pages cannot be opened. To go past it: next", Field(state, "State"));
	}

	/// <summary>
	/// Right after a match the navigation is rebuilt and the page list is empty for a while
	///; it is waited for rather than reported as no such page. Now and then the
	/// game is laying out its screen when the page is asked for, and refuses to add the panel
	/// ("VisualElements cannot be added to an active visual tree…"); a moment
	/// later the same request goes through.
	/// </summary>
	private static async Task<string> OpenWith((string, string)[] query)
	{
		if (!await Until(async () => (await Fm.GetJson("pages")).GetArrayLength() > 0, seconds: 5, every: 0.5))
		{
			return "error: " + await PagesClosed();
		}

		var answer = "";
		for (var attempt = 0; attempt < 3; attempt++)
		{
			answer = await Arriving("open", query);
			if (!answer.Contains("active visual tree", StringComparison.Ordinal)) return answer;
			await Task.Delay(500);
		}
		return answer.Split('\n')[0];
	}
}
