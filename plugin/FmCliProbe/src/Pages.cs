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
					if (!string.Equals(Read(choices[i])?.Guid, page.Guid, StringComparison.OrdinalIgnoreCase)) continue;
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
