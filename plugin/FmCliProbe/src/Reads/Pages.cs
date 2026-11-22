using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// The game's pages, as its own navigation menus list them, and a way to open one.
///
/// Each main tab keeps a menu of its pages whether or not any of them is open, and every row
/// says which panel it stands for: the asset's GUID, the panel's id and, for a page about a
/// team, that team's unique id. The GUID comes from the game's files, so it is the same in
/// every language and every save; the name is only what the row shows.
///
/// A page is opened by choosing its row in that menu, the way a click does. The row carries
/// the team, so First Team, Under 21s and Under 18s — one panel — each open their own squad,
/// and a page that has to be told what it is about (the tactics planner) comes up bound.
///
/// A row with a submenu (Training ›) does not open its page when chosen, and the bookmark event
/// opens such a page empty. The page has a button of its own in the bar under the tabs, and a
/// click on it sends the page's GUID: that button is found by the GUID of its first row, not by
/// its label. A page kept in the bar's "More" menu has no button, and opens whole by bookmark.
/// The bar is only there while the page's tab is up, so the tab is opened first and the caller
/// asks again.
/// </summary>
internal static class Pages
{
	private sealed class Page
	{
		public string Tab;
		public string Name;
		public string Guid;
		public string TabGuid;
		public string PanelId;
		public string ObjectId;
		public bool OpensCard;
		public SIDropdown Menu;
		public int Row;
		public bool HasSubmenu;
	}

	/// <summary>
	/// The page a submenu's page is reached from (FM27 moved Squad Registration, Stages… into
	/// the tab menus' submenus): "&lt;parent guid&gt;\t&lt;parent team&gt;\t&lt;name&gt;". That parent's subtab
	/// bar lists the page, so it is opened first and the page chosen there.
	/// </summary>
	public static string Parent(NameValueCollection q)
	{
		var guid = q["guid"];
		if (string.IsNullOrEmpty(guid)) return "error: ?guid= required";
		// A page a tab menu lists itself is opened from there, though a submenu may list it too
		// (Tactics is under First Team as well).
		if (All().Any(p => string.Equals(p.Guid, guid, StringComparison.OrdinalIgnoreCase))) return $"error: a tab menu lists guid {guid}";
		foreach (var menu in Buttons.AllDropdowns())
		{
			try
			{
				if (menu.name != "NavTabDropdown") continue;
				var choices = menu.m_choices;
				for (var i = 0; i < (choices?.Count ?? 0); i++)
				{
					var children = Buttons.Children(choices[i]);
					for (var c = 0; c < (children?.Count ?? 0); c++)
					{
						var child = Read(children[c]);
						if (!string.Equals(child?.Guid, guid, StringComparison.OrdinalIgnoreCase)) continue;
						var parent = Read(choices[i]);
						if (parent == null || string.Equals(parent.Guid, guid, StringComparison.OrdinalIgnoreCase)) continue;
						return $"{parent.Guid}\t{parent.ObjectId}\t{child.Name}";
					}
				}
			}
			catch { }
		}
		return $"error: no submenu lists guid {guid}";
	}

	/// <summary>
	/// The pages the bars on screen list (not the tab menus), submenus included: "name\tguid\tdropdown".
	/// With ?all=1 every dropdown, tab menus and hidden bars too.
	/// </summary>
	public static string BarPages(NameValueCollection q)
	{
		var all = q["all"] == "1";
		var sb = new System.Text.StringBuilder();
		void Walk(Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue> rows, string prefix, SIDropdown dropdown)
		{
			for (var i = 0; i < (rows?.Count ?? 0); i++)
			{
				var row = Read(rows[i]);
				if (row != null) sb.AppendLine($"{prefix}{row.Name}\t{row.Guid}\t0x{dropdown.Pointer.ToInt64():x}");
				Walk(Buttons.Children(rows[i]), prefix + (row?.Name ?? "?") + " › ", dropdown);
			}
		}
		foreach (var dropdown in Buttons.AllDropdowns())
		{
			try
			{
				if (!all && (dropdown.name == "NavTabDropdown" || !Shown(dropdown))) continue;
				Walk(dropdown.m_choices, "", dropdown);
			}
			catch { }
		}
		return sb.ToString();
	}

	public static string Catalog()
	{
		var rows = All().Select(p => Report.Json(new Dictionary<string, string>
		{
			["Tab"] = p.Tab,
			["Page"] = p.Name,
			["PanelID"] = p.PanelId,
			["Guid"] = p.Guid,
			["Object"] = p.ObjectId,
			["OpensCard"] = p.OpensCard ? "True" : "False",
			["Submenu"] = p.HasSubmenu ? "True" : "False",
		}));
		return "[\n  " + string.Join(",\n  ", rows) + "\n]\n";
	}

	public static string Open(NameValueCollection q)
	{
		var guid = q["guid"];
		var name = q["page"];
		if (string.IsNullOrEmpty(guid) && string.IsNullOrEmpty(name))
			return "error: ?guid=<page guid>[&object=<team id>] or ?page=<name>[&tab=<tab>] required (see /pages)";

		// Only a question, never an opening: is this page the one its subtab bar stands on?
		if (q["check"] == "1")
			return string.IsNullOrEmpty(guid) ? "error: ?check=1 needs ?guid=" : Subpage(guid, false, true, team: q["object"]);

		// Only from the bar on screen, though a tab menu lists the page too: the youth team's
		// Individual Training is the first team's under the Training tab (FM27).
		if (q["bar"] == "1")
			return string.IsNullOrEmpty(guid) ? "error: ?bar=1 needs ?guid=" : Subpage(guid, q["dry"] == "1", barOnly: true, team: q["object"]);

		Page page;
		if (!string.IsNullOrEmpty(guid))
		{
			// One page can sit in two menus, and the tab it is opened under changes what it shows:
			// the opposition report under Match Day is titled for the next match. ?under= names the
			// tab by the GUID of its first page. One panel can also stand for several teams; without
			// ?object= the first row is taken, which is the club's own team.
			var obj = q["object"];
			var under = q["under"];
			page = All().FirstOrDefault(p => string.Equals(p.Guid, guid, StringComparison.OrdinalIgnoreCase)
			                                 && (string.IsNullOrEmpty(obj) || p.ObjectId == obj)
			                                 && (string.IsNullOrEmpty(under) || string.Equals(p.TabGuid, under, StringComparison.OrdinalIgnoreCase)));
			// A page under a subtab's menu (Squad Dynamics › Social Groups) is not in the tab menus;
			// it is chosen from the subtab bar on screen, once its page is open.
			if (page == null && string.IsNullOrEmpty(obj) && string.IsNullOrEmpty(under))
				return Subpage(guid, q["dry"] == "1", q["check"] == "1");
			if (page == null)
				return $"error: no page with guid {guid}" + (string.IsNullOrEmpty(obj) ? "" : $" for object {obj}")
				       + (string.IsNullOrEmpty(under) ? "" : $" under tab {under}");
		}
		else
		{
			var tab = q["tab"];
			var matches = All()
				.Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
				.Where(p => string.IsNullOrEmpty(tab) || string.Equals(p.Tab, tab, StringComparison.OrdinalIgnoreCase))
				.ToList();

			if (matches.Count == 0) return $"error: no page named '{name}'" + (string.IsNullOrEmpty(tab) ? "" : $" under '{tab}'");
			if (matches.Count > 1)
				return $"error: {matches.Count} pages are named '{name}'; narrow it with &tab=\n"
				       + string.Join("\n", matches.Select(p => $"  {p.Tab}"));
			page = matches[0];
		}

		var described = $"{page.Tab} › {page.Name} (panel {page.PanelId}{(page.OpensCard ? ", card" : "")})";
		if (q["dry"] == "1") return $"dry run, NOT opened: {described}";

		if (q["how"] != "bookmark")
		{
			if (page.HasSubmenu)
			{
				switch (Click(page))
				{
					case Bar.Clicked: return $"opened {described}";
					case Bar.Absent when page.Menu != null && Choose(Home(page)):
						return $"error: no subtab bar for {described} yet; opened {page.Tab} first, ask again";
				}
			}
			else if (Choose(page)) return $"opened {described}";
		}

		var events = Game.Events;
		if (events == null) return "error: event subsystem unavailable";

		events.SendEvent(new FM.UI.OpenBookmarkEvent
		{
			BookmarkPanelID = new UnityEngine.AddressableAssets.AssetReferenceT<PanelID>(page.Guid),
			MainTabPanelID = new UnityEngine.AddressableAssets.AssetReferenceT<PanelID>(page.TabGuid),
		});
		return $"opened {described} (bookmark)";
	}

	/// <summary>
	/// Chooses the page's row in its tab's menu and sends what the row sends. A row with a
	/// submenu only opens that submenu, and none of the ways of choosing its first entry opened
	/// the page either (see <see cref="Click"/>).
	/// </summary>
	private static bool Choose(Page page)
	{
		var queue = Game.UiEvents;
		if (page.Menu == null || queue == null) return false;
		try
		{
			page.Menu.OnSelectionChangedInternal(page.Row);
			var events = page.Menu.SelectionEvents;
			if (events == null || events.Count == 0) return false;
			queue.QueueEvents(events);
			return true;
		}
		catch { return false; }
	}

	/// <summary>
	/// Opens a page listed in a subtab's menu on the bar shown (FM27 shows a tab's own menu as its bar): its row there
	/// carries the page's GUID as the tab menus' rows do. The row is chosen and its events sent. The
	/// page whose bar it is may still be opening and cover it; the caller checks and asks again.
	/// With <paramref name="check"/> nothing is chosen: it only says whether the page is the one the bar
	/// has chosen ("shown" / "not shown").
	/// </summary>
	/// <param name="team">?object=: only a row for that team (each youth team's tab menu lists its own Training).</param>
	private static string Subpage(string guid, bool dry, bool check = false, bool barOnly = false, string team = null)
	{
		var queue = Game.UiEvents;
		if (queue == null) return "error: UIEventQueue unavailable";
		// The bars first. A tab's own menu is the bar of an FM27 tab (Training › Coach Assignments), but
		// every tab's menu counts as shown, and the youth team's bar lists pages the Training tab does too.
		var dropdowns = Buttons.AllDropdowns().ToList();
		var ordered = dropdowns.Where(d => d.name != "NavTabDropdown").Concat(barOnly ? [] : dropdowns.Where(d => d.name == "NavTabDropdown"));
		string failed = null;
		foreach (var dropdown in ordered)
		{
			try
			{
				if (!Shown(dropdown)) continue;
				var choices = dropdown.m_choices;
				var count = choices?.Count ?? 0;
				for (var i = 0; i < count; i++)
				{
					var row = Read(choices[i]);
					if (!string.Equals(row?.Guid, guid, StringComparison.OrdinalIgnoreCase)) continue;
					if (!string.IsNullOrEmpty(team) && row.ObjectId != team) continue;
					if (check)
					{
						var on = dropdown.SelectedIndex;
						var at = on >= 0 && on < count ? Read(choices[on]) : null;
						return $"{(on == i ? "shown" : "not shown")} {row.Name} (bar on row {on}: {at?.Name} {at?.Guid}; first row {Read(choices[0])?.Guid})";
					}
					if (dry) return $"dry run, NOT opened: {row.Name} (subtab row {i})";
					// Another list can hold the page too, so a row that fails does not end the search.
					try
					{
						dropdown.OnSelectionChangedInternal(i);
						var events = dropdown.SelectionEvents;
						if (events == null || events.Count == 0) return $"error: the subtab row for {row.Name} sends nothing";
						queue.QueueEvents(events);
						return $"opened {row.Name} (subtab, panel {row.PanelId})";
					}
					catch (Exception ex) { failed = $"error: the subtab row for {row.Name} could not be chosen: {ex.Message}"; }
				}
			}
			catch { }
		}
		return failed ?? $"error: no page with guid {guid} (a subtab's page: open the page whose bar lists it first)";
	}

	/// <summary>
	/// Closes the intro FM27 lays over a tab the first time it is visited ("Got It"): while it is up, a
	/// page chosen in a menu does not open. Known by its button's name, #EndTutorialButton, in every language.
	/// </summary>
	public static string CloseIntro()
	{
		foreach (var element in Buttons.Elements())
		{
			try
			{
				if (element.name != "EndTutorialButton" || !Shown(element)) continue;
				var button = element.TryCast<SIButton>();
				if (button == null) continue;
				button.SimulateButtonClick(false);
				return "closed";
			}
			catch { }
		}
		return "none";
	}

	/// <summary>The first page of a page's tab: the tab's own screen.</summary>
	private static Page Home(Page page) => new() { Menu = page.Menu, Row = 0 };

	private enum Bar { Clicked, InMore, Absent }

	/// <summary>
	/// Clicks the subtab button that opens a page with a submenu: of the dropdowns under the
	/// tabs, the one whose click sends something and whose first row is that page. A dropdown
	/// that lists the page further down is the bar's "More" menu.
	/// </summary>
	private static Bar Click(Page page)
	{
		var queue = Game.UiEvents;
		var inMore = false;
		foreach (var dropdown in Buttons.AllDropdowns())
		{
			try
			{
				// A tab visited earlier keeps its bar, hidden; a click there does nothing.
				if (dropdown.name == "NavTabDropdown" || !Shown(dropdown)) continue;
				var choices = dropdown.m_choices;
				var count = choices?.Count ?? 0;
				for (var i = 0; i < count; i++)
				{
					// One panel stands for several teams (First Team, the B team, the U19s each open the squad
					// page): the row is told by the team it opens too, not only by the page.
					var row = Read(choices[i]);
					if (!string.Equals(row?.Guid, page.Guid, StringComparison.OrdinalIgnoreCase)) continue;
					if (!string.IsNullOrEmpty(page.ObjectId) && !string.IsNullOrEmpty(row.ObjectId) && row.ObjectId != page.ObjectId) continue;
					var events = dropdown.LeftClickEvents;
					if (i == 0 && events != null && events.Count > 0 && queue != null)
					{
						queue.QueueEvents(events);
						return Bar.Clicked;
					}
					inMore = true;
				}
			}
			catch { }
		}
		return inMore ? Bar.InMore : Bar.Absent;
	}

	/// <summary>Whether an element is on screen: attached, given room, and not hidden by itself or any parent.</summary>
	internal static bool Shown(UnityEngine.UIElements.VisualElement element)
	{
		try
		{
			if (element.panel == null || element.worldBound.width <= 0) return false;
			for (var e = element; e != null; e = e.parent)
			{
				var style = e.resolvedStyle;
				if (style.display == UnityEngine.UIElements.DisplayStyle.None
				    || style.visibility == UnityEngine.UIElements.Visibility.Hidden
				    || style.opacity <= 0) return false;
			}
			return true;
		}
		catch { return false; }
	}

	private static List<Page> All()
	{
		var pages = new List<Page>();

		foreach (var menu in Buttons.AllDropdowns())
		{
			string menuName;
			try { menuName = menu.name; } catch { continue; }
			if (menuName != "NavTabDropdown") continue;

			var choices = menu.m_choices;
			var count = choices?.Count ?? 0;
			var rows = new List<Page>();

			for (var i = 0; i < count; i++)
			{
				var page = Read(choices[i]);
				if (page == null) continue;
				page.Menu = menu;
				page.Row = i;
				page.HasSubmenu = (Buttons.Children(choices[i])?.Count ?? 0) > 0;
				rows.Add(page);
			}
			if (rows.Count == 0) continue;

			// A menu's first row is the tab's own screen, which names the tab and is the main
			// panel every other row opens under.
			var tabName = TabName(rows[0]);
			foreach (var row in rows)
			{
				row.Tab = tabName;
				row.TabGuid = rows[0].Guid;
			}
			pages.AddRange(rows);
		}

		return pages;
	}

	private static Page Read(SI.Core.TypedValue choice)
	{
		try
		{
			var record = choice?.Get()?.TryCast<DynamicReference>();
			if (record == null) return null;

			var row = Record(Send.Entries(record), "RowData");
			var panel = row == null ? null : Record(row, "PropertyValue");
			if (panel == null) return null;

			return new Page
			{
				Name = Value(panel, "SubtabText"),
				Guid = Value(panel, "Value"),
				PanelId = Value(panel, "PanelID"),
				ObjectId = Value(panel, "ObjectUniqueID"),
				OpensCard = Value(panel, "OpensCard") == "True",
			};
		}
		catch { return null; }
	}

	private static string TabName(Page first)
	{
		if (!uint.TryParse(first.PanelId, out var id)) return first.Name;

		// The home tab's screen is itself titled "Overview", which is also what every tab calls
		// its first page, so there the asset's name says more than the title does.
		var title = Panels.TitleById(id);
		if (!string.IsNullOrEmpty(title) && title != first.Name) return title;

		var asset = Panels.NameById(id) ?? first.Name;
		return asset.EndsWith("Screen", StringComparison.Ordinal) ? asset[..^"Screen".Length] : asset;
	}

	private static Dictionary<string, SI.Core.TypedValue> Record(Dictionary<string, SI.Core.TypedValue> entries, string name)
	{
		if (!entries.TryGetValue(name, out var value) || value == null || value.IsNull) return null;
		var record = value.Get()?.TryCast<DynamicReference>();
		return record == null ? null : Send.Entries(record);
	}

	private static string Value(Dictionary<string, SI.Core.TypedValue> entries, string name) =>
		entries.TryGetValue(name, out var value) && value != null && !value.IsNull ? Text.Of(value) : "";
}
