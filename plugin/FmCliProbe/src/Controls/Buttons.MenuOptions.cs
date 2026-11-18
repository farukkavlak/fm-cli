using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace FmCliProbe;

internal static partial class Buttons
{
	/// <summary>
	/// The FM menu's rows by what each one does. FM27's FM menu is a list of bare numbers
	/// (FMMenuOptionQuantiseAndMap): each is the key of a row of a data table whose MenuOptionID
	/// names the action (FMMenuOptionEvent.FMMenuOption: Preferences, SaveGame…), the same in every
	/// language, and whose PanelID is the page it opens. Read off the list's own event.
	/// </summary>
	public static string MenuOptions()
	{
		var rows = new List<string>();
		foreach (var control in Controls(out _))
		{
			foreach (var (_, list) in control.Lists)
			{
				var contexts = list.Events;
				for (var i = 0; i < contexts.Length; i++)
				{
					var context = contexts[i];
					if (context == null) continue;
					string type;
					try { type = context.GetIl2CppType().FullName; } catch { continue; }
					if (!type.EndsWith("FMMenuOptionQuantiseAndMapEventContext", StringComparison.Ordinal)) continue;

					var typed = context.TryCast<FMMenuOptionQuantiseAndMapEventContext>();
					var optionColumn = typed?.m_menuOptionIDColumn;
					var panelColumn = typed?.m_panelIDColumn;
					// The list loads its table the first time it is used; until then it is loaded here.
					var table = typed?.m_loadedDataCollection ?? Loaded(typed?.m_dataCollection?.AssetGUID);
					var pointer = $"0x{control.Element.Pointer.ToInt64():x}";
					if (table == null || string.IsNullOrEmpty(optionColumn))
					{
						rows.Add(Report.Json(new Dictionary<string, string> { ["Pointer"] = pointer, ["Error"] = "the menu's table could not be loaded" }));
						continue;
					}

					var keyColumn = table.KeyColumn?.Name;
					for (var row = 0; row < table.Rows; row++)
					{
						string key = row.ToString(), option = "", panel = "";
						try { if (keyColumn != null) key = Text.Of(table.Get(keyColumn, row)); } catch { }
						try { option = OptionName(table.Get(optionColumn, row)); } catch { }
						try { if (!string.IsNullOrEmpty(panelColumn)) panel = Text.Of(table.Get(panelColumn, row)); } catch { }
						rows.Add(Report.Json(new Dictionary<string, string>
						{
							["Pointer"] = pointer,
							["Name"] = NameOf(control.Element),
							// The list's own value for the row: what choosing it writes.
							["Key"] = key,
							["Option"] = option,
							["Panel"] = panel,
							// The page's own name (CreditsCard): what its data is bound under once it is up.
							["PanelName"] = PanelName(panel),
						}));
					}
				}
			}
		}
		return "[\n  " + string.Join(",\n  ", rows) + "\n]\n";
	}

	/// <summary>
	/// Does what choosing an FM menu row does, by the row's option (?option=Preferences): the menu's
	/// own list, on the row whose key the menu's table gives for that option, with the list's own
	/// events sent. Writing the list's value alone (as /select does) opens nothing.
	/// </summary>
	public static string MenuOption(NameValueCollection q)
	{
		var type = typeof(FM.UI.FMMenuOptionEvent.FMMenuOption);
		if (string.IsNullOrEmpty(q["option"]) || !Enum.TryParse(type, q["option"], ignoreCase: true, out var option))
			return $"error: ?option= one of {string.Join(", ", Enum.GetNames(type))}";

		foreach (var control in Controls(out _))
			foreach (var (_, list) in control.Lists)
			{
				var contexts = list.Events;
				for (var i = 0; i < contexts.Length; i++)
				{
					var typed = contexts[i]?.TryCast<FMMenuOptionQuantiseAndMapEventContext>();
					if (typed == null) continue;
					var table = typed.m_loadedDataCollection ?? Loaded(typed.m_dataCollection?.AssetGUID);
					if (table == null) return "error: the menu's table could not be loaded";

					// The list sends the chosen row's key, and the game maps it to the row's option
					// and page itself. An FMMenuOptionEvent sent straight is no way in: the game takes
					// its option for a row key and opens another row's page (Preferences, 7, is the
					// key of Add New Manager).
					int? key = null;
					var keyColumn = table.KeyColumn?.Name;
					for (var row = 0; row < table.Rows && key == null; row++)
						try
						{
							if (OptionName(table.Get(typed.m_menuOptionIDColumn, row)) == option.ToString())
								key = int.Parse(keyColumn == null ? row.ToString() : Text.Of(table.Get(keyColumn, row)));
						}
						catch { }
					if (key == null) return $"error: the FM menu has no {option} row here";

					// Chosen on the menu list itself and its own events sent, as a click on the row
					// does: the game fills the event from its own table. An event built here with the
					// table loaded here opened the page once; the second time it held the game up.
					var dropdown = control.Element.TryCast<SI.Bindable.SIDropdown>();
					var choices = dropdown?.m_choices;
					var index = -1;
					for (var c = 0; c < (choices?.Count ?? 0) && index < 0; c++)
						if (Text.Of(choices[c]) == key.Value.ToString()) index = c;
					if (index < 0) return $"error: the FM menu list does not offer its {option} row";
					var queue = Game.UiEvents;
					var events = dropdown.SelectionEvents;
					if (queue == null || events == null || events.Count == 0) return "error: the FM menu list has no events to send";
					dropdown.OnSelectionChangedInternal(index);
					queue.QueueEvents(events);
					return $"sent {option}";
				}
			}
		return "error: no FM menu on this screen";
	}

	private static readonly Dictionary<string, string> PanelNames = new();

	/// <summary>
	/// A page's name by its Guid: the page asset at that address, through a handle of its own (as
	/// <see cref="Loaded"/>), once per Guid. Empty when there is no such page.
	/// </summary>
	private static string PanelName(string guid)
	{
		if (string.IsNullOrEmpty(guid)) return "";
		if (PanelNames.TryGetValue(guid, out var known)) return known;
		var name = "";
		try
		{
			var key = new Il2CppSystem.Object(Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(guid));
			name = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<SI.Bindable.PanelID>(key).WaitForCompletion()?.name ?? "";
		}
		catch { }
		PanelNames[guid] = name;
		return name;
	}

	private static readonly Dictionary<string, SI.Core.DataCollection> Tables = new();

	/// <summary>
	/// The table asset by its address, through a handle of its own: the list's own reference is
	/// left alone, since loading that one twice is an error when the list comes to load it.
	/// </summary>
	private static SI.Core.DataCollection Loaded(string guid)
	{
		if (string.IsNullOrEmpty(guid)) return null;
		if (Tables.TryGetValue(guid, out var known) && known != null && !known.WasCollected && known.Rows > 0) return known;
		try
		{
			var key = new Il2CppSystem.Object(Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(guid));
			var table = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<SI.Core.DataCollection>(key).WaitForCompletion();
			Tables[guid] = table;
			return table;
		}
		catch { return null; }
	}

	/// <summary>The option a table cell holds, by its name in the game's enum (its number when it has none).</summary>
	private static string OptionName(SI.Core.TypedValue cell)
	{
		var shown = Text.Of(cell);
		var type = typeof(FM.UI.FMMenuOptionEvent.FMMenuOption);
		return int.TryParse(shown, out var number) && Enum.GetName(type, Enum.ToObject(type, number)) is { } name ? name : shown;
	}
}
