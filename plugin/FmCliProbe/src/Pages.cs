using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// The game's pages, as its own navigation menus list them, and a way to open one by name.
///
/// Each main tab keeps a menu of its pages whether or not any of them is open, and every row
/// says which panel it stands for: the asset's GUID and the panel's id. Most of those panels
/// are not loaded until first visited, so a page cannot be opened by handing the game a panel
/// object. The bookmark event takes asset references instead, and the game loads what it
/// needs.
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
		public bool OpensCard;
	}

	public static string Catalog()
	{
		var rows = All().Select(p => Report.Json(new Dictionary<string, string>
		{
			["Tab"] = p.Tab,
			["Page"] = p.Name,
			["PanelID"] = p.PanelId,
			["Guid"] = p.Guid,
			["OpensCard"] = p.OpensCard ? "True" : "False",
		}));
		return "[\n  " + string.Join(",\n  ", rows) + "\n]\n";
	}

	public static string Open(NameValueCollection q)
	{
		var name = q["page"];
		if (string.IsNullOrEmpty(name)) return "error: ?page=<name>[&tab=<tab>] required (see /pages)";

		var tab = q["tab"];
		var matches = All()
			.Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
			.Where(p => string.IsNullOrEmpty(tab) || string.Equals(p.Tab, tab, StringComparison.OrdinalIgnoreCase))
			.ToList();

		if (matches.Count == 0) return $"error: no page named '{name}'" + (string.IsNullOrEmpty(tab) ? "" : $" under '{tab}'");
		if (matches.Count > 1)
			return $"error: {matches.Count} pages are named '{name}'; narrow it with &tab=\n"
			       + string.Join("\n", matches.Select(p => $"  {p.Tab}"));

		var page = matches[0];
		var described = $"{page.Tab} › {page.Name} (panel {page.PanelId}{(page.OpensCard ? ", card" : "")})";
		if (q["dry"] == "1") return $"dry run, NOT opened: {described}";

		var events = Game.Events;
		if (events == null) return "error: event subsystem unavailable";

		events.SendEvent(new FM.UI.OpenBookmarkEvent
		{
			BookmarkPanelID = new UnityEngine.AddressableAssets.AssetReferenceT<PanelID>(page.Guid),
			MainTabPanelID = new UnityEngine.AddressableAssets.AssetReferenceT<PanelID>(page.TabGuid),
		});
		return $"opened {described}";
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
				if (page != null) rows.Add(page);
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
		entries.TryGetValue(name, out var value) && value != null && !value.IsNull ? Text.Clean(value.AsString()) : "";
}
