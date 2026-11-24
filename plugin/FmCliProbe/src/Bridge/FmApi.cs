using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using Il2CppInterop.Runtime;
using SI.Bindable;
using SI.Core;

namespace FmCliProbe;

/// <summary>
/// The HTTP surface. Deliberately exploratory for now: these routes exist so the binding tree
/// can be inspected from a terminal without rebuilding the plugin for every question.
/// </summary>
internal static partial class FmApi
{
	private const int DefaultDepth = 3;
	private const int MaxNodes = 20000;

	public static string Handle(string route, NameValueCollection q) => route switch
	{
		"" or "health" => Health(),
		"tree" => Tree(q["path"], Int(q["depth"], DefaultDepth), q["shown"]),
		"get" => Get(q["path"]),
		"exists" => Exists(q["path"]),
		"props" => Props(q["path"]),
		"subsystems" => Game.SubsystemReport(),
		"scan" => Game.ScanReport(q["q"]),
		"find" => Find(q["name"], Int(q["limit"], 10)),
		"dugoutcard" => DugoutCard(),
		"loadlast" => LoadLast(),
		"next" => Next(),
		"refprops" => RefProps(q["path"]),
		"bind" => Bind(q["path"]),
		"pathexists" => RawPath.Exists(q["path"]),
		"pathbind" => RawPath.Bind(q["path"]),
		"pathlist" => RawPath.List(q["path"]),
		"contexts" => RawPath.Contexts(q["path"]),
		"pathset" => RawPath.Put(q["to"], q["from"], q["int"], q["flags"]),
		"pull" => RawPath.Pull(q["path"], q["props"], q["list"] == "1", q["gen"], q["shown"], q["nulls"] == "1"),
		"pullbound" => RawPath.PullBound(q["name"], q["under"], q["props"], q["shown"], q["gen"]),
		"rawexists" => RawExists(q["path"]),
		"keys" => Keys(Int(q["depth"], 4)),
		"schema" => Schema.Catalog(q["filter"]),
		"send" => Send.Event(q),
		"openperson" => Send.Person(q["path"]),
		"dyn" => Dynamic(q["path"]),
		"panels" => Panels.Catalog(q["filter"]),
		"squad" => Report.Squad(),
		"people" => Report.People(teamless: q["teamless"] == "1"),
		"search" => Report.SearchResults(),
		"searchopen" => Report.OpenSearchResult(q["name"], q["path"], q["dry"] == "1"),
		"inbox" => Report.Inbox(q["paths"] == "1"),
		"inboxlist" => Report.InboxList(),
		"tour" => Report.Tour(),
		"player" => PlayerCard.Current(q["name"]),
		"table" => Report.Table(q["groups"] == "1"),
		"fixtures" => Report.Fixtures(),
		"save" => Save(),
		"dialog" => Dialogs.Open(q),
		"translate" => Translations.Open(q),
		"money" => Translations.Money(q["values"]),
		"shot" => Shot(q),
		"radar" => Radar(),
		"moments" => Moments(q["from"]),
		"repaint" => Repaint(q["how"]),
		"visible" => Visible(q["text"]),
		"layers" => Layers(q["name"] ?? "PanelManager-container", Int(q["depth"], 3)),
		// The game's own way out: killing it can leave it stuck in the kernel holding the port.
		"quit" => Quit(),
		"offer" => Offer.Current(),
		"contract" => Offer.Contract(),
		"submenu" => Submenus.Open(q),
		"dropdownrows" => Submenus.Rows(q["shown"] == "1"),
		"lazymenus" => Submenus.Lazy(),
		"rowsubmit" => Submenus.Submit(q),
		"choicedump" => Submenus.Dump(q),
		"rowput" => Submenus.Put(q),
		"menu" => Submenus.Menu(q),
		"menukinds" => Submenus.Kinds(q),
		"menuoptions" => Buttons.MenuOptions(),
		"menuoption" => Buttons.MenuOption(q),
		"instructions" => Instructions.Current(),
		"tiles" => Instructions.Tiles(),
		"texts" => Texts.Around(q),
		"attachment" => q["state"] == "1" ? Attachment.State() : Attachment.Read(),
		"shown" => Texts.OnScreen(q),
		"scroll" => Texts.Scroll(q),
		"form" => Form.Read(q),
		"setcontrol" => Form.Set(q),
		"rowmove" => TableRows.Move(q),
		"rowremove" => TableRows.Remove(q),
		"rowselect" => TableRows.Select(q),
		"checkrows" => TableRows.Checkable(q),
		"rowtexts" => TableRows.Shown(q),
		"rowlinks" => TableRows.Links(q),
		"actions" => ContextMenus.Handle(q),
		"scoped" => Scoped.Handle(q),
		"opposition" => Opposition.Current(),
		"namedrows" => NamedRows.Read(q),
		"rowcontrols" => RowControls.Read(q),
		"talk" => Talk.Current(),
		"coachareas" => CoachAreas.Current(),
		"tweaks" => Tweaks.Handle(q),
		"critiques" => Report.Critiques(),
		"state" => State.Current(),
		"continueflags" => State.ContinueFlags(),
		"tactic" => Tactics.Current(),
		"registration" => Registration.Current(),
		"regtoggle" => Registration.Toggle(q),
		"regscroll" => Registration.ScrollTo(q),
		"pitchtiles" => PitchTiles.Handle(q),
		"pickplayer" => Tactics.Pick(q),
		"pickable" => Tactics.Pickable(q),
		"bindprop" => Materialise.Property(q["path"], q["name"]),
		"buttons" => Buttons.Catalog(q),
		"press" => Buttons.Press(q),
		"pluginevent" => Buttons.PluginEvent(q),
		"click" => Buttons.Click(q),
		"popup" => Popups.Open(q),
		"textboxes" => TextBoxes.Catalog(q),
		"type" => TextBoxes.Type(q),
		"setnumber" => NumberBoxes.Set(q),
		"dropdowns" => Buttons.Dropdowns(q),
		"select" => Buttons.Select(q),
		"queue" => Buttons.Queue(q),
		"whatis" => WhatIs.Handle(q),
		"elements" => Elements.Around(q),
		"pages" => Pages.Catalog(),
		"open" => Pages.Open(q),
		"pageparent" => Pages.Parent(q),
		"barpages" => Pages.BarPages(q),
		"closeintro" => Pages.CloseIntro(),
		"folders" => Folders(),
		_ => null, // 404
	};

	// ------------------------------------------------------------------ routes

	/// <summary>
	/// Where this copy of the game keeps its files, as the game itself has them: the user's folder
	/// (saves under "games", tactics under "tactics") and the install. The store and the platform
	/// decide these; the CLI asks rather than guesses.
	/// </summary>
	private static string Folders() =>
		$"{{\"User\": \"{Report.Escape(UnityEngine.Application.persistentDataPath)}\", " +
		$"\"Install\": \"{Report.Escape(UnityEngine.Application.dataPath)}\", " +
		$"\"Platform\": \"{UnityEngine.Application.platform}\", \"Version\": \"{Report.Escape(UnityEngine.Application.version)}\"}}\n";

	/// <summary>
	/// The tree's node count, stopping at <paramref name="cap"/>: FM27 dropped Bindings.GetDescendantCount,
	/// and what callers ask is only "is the UI built yet" (more than ~150 nodes at the main menu).
	/// </summary>
	private static int Descendants(Bindings b, int cap)
	{
		var n = 0;
		var stack = new Stack<IReadOnlyNode>();
		stack.Push(b.RootNode);
		while (stack.Count > 0 && n < cap)
		{
			var node = stack.Pop();
			for (var c = node?.FirstChild; c != null && n < cap; c = c.NextSibling)
			{
				n++;
				stack.Push(c);
			}
		}
		return n;
	}

	private static string Health()
	{
		var b = Game.Bindings;
		if (b == null) return "bindings: unavailable (is a game loaded?)";

		var game = "";
		try { game = UnityEngine.Application.version ?? ""; } catch { }
		return $"bindings: ok\nplugin: {Plugin.Version}\ngame: {game}\ndescendants: {Descendants(b, 1000)}\nruns from: {Plugin.RunsFrom}\nhidden: {(WindowHider.Wanted ? WindowHider.Hidden.ToString() : "off")}";
	}

	/// <summary>Read a value by path, using a locally computed key rather than a span.</summary>
	private static string Get(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";

		var b = Game.Bindings;
		if (b == null) return "error: bindings unavailable";

		var key = PathKey.Of(path);
		if (!b.Exists(ref key)) return $"error: '{path}' does not exist";

		return Describe(b.Get(ref key)) ?? "<no value>";
	}

	private static string Exists(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";
		var b = Game.Bindings;
		if (b == null) return "error: bindings unavailable";
		var key = PathKey.Of(path);
		return b.Exists(ref key).ToString().ToLowerInvariant();
	}

	/// <summary>
	/// References describe themselves, so rather than guessing the schema we ask the game
	/// which properties a node's value accepts.
	/// </summary>
	private static string Props(string path)
	{
		var node = string.IsNullOrEmpty(path) ? Root(out var e0) : Resolve(path, out e0);
		if (e0 != null) return e0;

		var sb = new StringBuilder();
		var value = node.Value;
		sb.AppendLine($"value: {Describe(value)}");
		sb.AppendLine($"children ({node.ChildCount}):");
		for (var c = node.FirstChild; c != null; c = c.NextSibling)
			sb.AppendLine($"  {c.Name} = {Describe(c.Value)}{(c.ChildCount > 0 ? $"  [{c.ChildCount}]" : "")}");
		return sb.ToString();
	}

	/// <summary>
	/// The bound tree as text. The fields named in <paramref name="shown"/> come in the game's own
	/// words, not put into English: text only read out (a news story's age, "Az önce" came back "Just Now").
	/// </summary>
	private static string Tree(string path, int depth, string shown = null)
	{
		var inGameWords = new HashSet<string>((shown ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
		var node = string.IsNullOrEmpty(path) ? Root(out var err) : Resolve(path, out err);
		if (err != null) return err;

		var sb = new StringBuilder();
		var count = 0;
		// From the root, what /pull and the routes put at fmcli.* is left out: it is ours, not the
		// screen's, and its live values (lists of records read earlier) made a whole dump take minutes.
		Walk(node, 0, depth, sb, ref count, skipOwn: string.IsNullOrEmpty(path), inGameWords);
		sb.AppendLine($"-- {count} node(s)");
		return sb.ToString();
	}

	/// <summary>
	/// Advance the game, the way the Continue button does. The event carries no payload; the
	/// game decides what advancing means from its current state.
	/// </summary>
	private static string Next()
	{
		var events = Game.Events;
		if (events == null) return "error: event subsystem unavailable";

		events.SendEvent(new FM.UI.ContinueGameEvent());
		return "sent ContinueGameEvent";
	}

	/// <summary>
	/// Save the game over its current file, as the game's own save shortcut does. The event
	/// carries nothing; progress shows up as game.GameSaving in /state.
	/// </summary>
	private static string Save()
	{
		var events = Game.Events;
		if (events == null) return "error: event subsystem unavailable";

		events.SendEvent(new FM.UI.SaveGameEvent());
		return "sent SaveGameEvent";
	}

	/// <summary>
	/// The main menu's Continue: loads the last save. Not the in-game Continue (that is /next).
	/// Refused once a save is loaded, where it would throw away everything since the last save.
	/// </summary>
	private static string LoadLast()
	{
		var events = Game.Events;
		if (events == null) return "error: event subsystem unavailable";
		if (State.InSave()) return "error: a save is loaded; this would reload the last save over it (the in-game Continue is /next)";

		var hits = FindAll("LastSave", 1);
		if (hits.Count == 0) return "error: no LastSave node (is the main menu up?)";
		var node = hits[0].Node;

		var record = node.Value?.Get()?.TryCast<SI.Core.Record>();
		if (record == null) return "error: LastSave held no Record";

		var evt = new FM.UI.LoadSelectedGameEvent { LoadGameDataRecord = record };
		events.SendEvent(evt);
		return "sent LoadSelectedGameEvent";
	}

	/// <summary>
	/// Locate nodes by name and report their full paths, so /tree and /get can address them.
	/// Panel subtrees are keyed by GUID, so paths are impractical to guess by hand.
	/// </summary>
	private static string Find(string name, int limit)
	{
		if (string.IsNullOrEmpty(name)) return "error: ?name= required";

		var hits = FindAll(name, limit);
		if (hits.Count == 0) return $"error: no node named '{name}'";

		var sb = new StringBuilder();
		foreach (var (path, node) in hits)
			sb.AppendLine($"{path}\n    = {Describe(node.Value) ?? "<no value>"}  [{node.ChildCount}]");
		return sb.ToString();
	}

	/// <summary>The dugout panel's selected card, from one walk of the tree; nothing while the panel is hidden.</summary>
	private static string DugoutCard()
	{
		var fields = new Dictionary<string, string>();
		var wanted = new[] { "MatchDugoutSelectedEventIndex", "MatchDugoutSelectedEventType", "AnalysisFeedbackMinute", "MatchDugoutIsSelectedEventActionable",
			"MatchDugoutSelectedEventTweakId", "MatchDugoutSelectedEventTweakAction" };
		foreach (var (_, node) in Report.Walk())
		{
			var name = node.Name;
			if (name == null || fields.ContainsKey(name) || Array.IndexOf(wanted, name) < 0) continue;
			try
			{
				if (node.Value is { IsNull: false } value) fields[name] = Text.Of(value);
			}
			catch { continue; }
			if (fields.Count == wanted.Length) break;
		}
		return Report.Json(fields) + "\n";
	}

	private static List<(string Path, IReadOnlyNode Node)> FindAll(string name, int limit)
	{
		// The full walk: its limit is the larger one, and it leaves out our own fmcli.* nodes, which in a long
		// session used up the limit before the screen's nodes were reached.
		var hits = new List<(string, IReadOnlyNode)>();
		foreach (var (path, node) in Report.Walk())
		{
			if (string.Equals(node.Name ?? "", name, StringComparison.OrdinalIgnoreCase)) hits.Add((path, node));
			if (hits.Count >= limit) break;
		}

		return hits;
	}


	private static string RawExists(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";
		var b = Game.Bindings;
		if (b == null) return "error: bindings unavailable";
		try
		{
			var key = PathKey.Of(path);
			return $"'{path}': key={key.m_key} exists={b.Exists(ref key)}";
		}
		catch (Exception ex) { return $"error: {ex.GetType().Name}: {ex.Message}"; }
	}

	/// <summary>
	/// Materialise a path. The tree is lazy: a reference's properties only exist once something
	/// binds them, which is what the UI does when it opens a screen. Binding from here is the
	/// same act, minus the screen.
	/// </summary>
	private static string Bind(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";

		var b = Game.Bindings;
		if (b == null) return "error: bindings unavailable";

		try
		{
			var key = PathKey.Of(path);
			var before = b.Exists(ref key);
			b.Bind(ref key, Owner, NoOp);
			var after = b.Exists(ref key);
			var value = after ? Describe(b.Get(ref key)) : null;
			return $"'{path}': existed={before} -> exists={after}  value={value ?? "<none>"}";
		}
		catch (Exception ex)
		{
			return $"error: bind threw {ex.GetType().Name}: {ex.Message}";
		}
	}

	private static Bindings.ValueChangedCallback _noOp;

	/// <summary>
	/// The owner FM27's Bind wants beside the callback (a null one throws in SubscriptionOwnerTable);
	/// one object for the whole session, so the game's weak table keeps every binding of ours.
	/// </summary>
	internal static readonly Il2CppSystem.Object Owner = new();

	/// <summary>
	/// Binding requires a callback; we only want the side effect. It must be a real il2cpp
	/// delegate — handing the game a null function pointer crashes it when the value changes.
	/// </summary>
	internal static Bindings.ValueChangedCallback NoOp =>
		_noOp ??= DelegateSupport.ConvertDelegate<Bindings.ValueChangedCallback>(
			(Action<Bindings.Key, TypedValue>)((_, _) => { }));

	/// <summary>Ask the reference at a path which properties it accepts.</summary>
	private static string RefProps(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";

		var node = Resolve(path, out var err);
		if (err != null) return err;

		var value = node.Value?.Get();
		if (value == null) return "error: node has no value";

		return Schema.Describe(value) ?? $"error: not a reference ({node.Value.DataType?.Name})";
	}

	/// <summary>
	/// Dump (parent key, child name, child key) triples.
	///
	/// Keys are ulongs and the span-free overloads take them directly, so if the key for a child
	/// can be derived from its parent's key and its name, path addressing becomes possible again
	/// without going near ReadOnlySpan. This exists to collect enough samples to work that out.
	/// </summary>
	private static string Keys(int maxDepth)
	{
		var root = Root(out var err);
		if (err != null) return err;

		var sb = new StringBuilder();
		sb.AppendLine("parentKey\tchildKey\tname");

		var stack = new Stack<(IReadOnlyNode Node, int Depth)>();
		stack.Push((root, 0));
		var seen = 0;

		while (stack.Count > 0 && seen < MaxNodes)
		{
			var (node, depth) = stack.Pop();
			seen++;
			if (depth >= maxDepth) continue;

			ulong parentKey;
			try { parentKey = node.Key.m_key; }
			catch { continue; }

			for (var c = node.FirstChild; c != null; c = c.NextSibling)
			{
				try { sb.AppendLine($"{parentKey}\t{c.Key.m_key}\t{c.Name}"); }
				catch { continue; }
				stack.Push((c, depth + 1));
			}
		}

		return sb.ToString();
	}

	// ------------------------------------------------------------------ walking

	private static void Walk(IReadOnlyNode node, int depth, int maxDepth, StringBuilder sb, ref int count, bool skipOwn = false, HashSet<string> inGameWords = null)
	{
		if (count >= MaxNodes || depth > maxDepth) return;
		if (skipOwn && depth == 1 && node.Name == "fmcli") return;
		count++;

		var children = node.ChildCount;
		sb.Append(' ', depth * 2)
			.Append(node.Name ?? "<null>")
			.Append(inGameWords != null && node.Name != null && inGameWords.Contains(node.Name) && node.Value is { IsNull: false } v
				? $" = ({v.DataType?.Name ?? "?"}) {Text.ShownOf(v)}"
				: Suffix(node.Value))
			.AppendLine(children > 0 ? $"  [{children}]" : "");

		for (var c = node.FirstChild; c != null; c = c.NextSibling)
		{
			Walk(c, depth + 1, maxDepth, sb, ref count, skipOwn, inGameWords);
			if (count >= MaxNodes) return;
		}
	}

	/// <summary>
	/// Why a piece of text that is in the tree does not show: each of its ancestors with what
	/// would hide it — display, visibility, opacity, size and where it sits on screen.
	/// </summary>
	private static string Visible(string text)
	{
		var sb = new StringBuilder();
		var found = 0;
		foreach (var element in Buttons.Elements())
		{
			string own;
			try { own = element.TryCast<UnityEngine.UIElements.TextElement>()?.text; } catch { continue; }
			if (own == null || own.IndexOf(text ?? "", StringComparison.OrdinalIgnoreCase) < 0) continue;
			if (found++ >= 2) break;
			sb.AppendLine($"== \"{own}\"");
			for (var e = (UnityEngine.UIElements.VisualElement)element; e != null; e = e.parent)
			{
				var st = e.resolvedStyle;
				var b = e.worldBound;
				sb.AppendLine($"{e.GetType().Name} #{e.name} display={st.display} vis={st.visibility} op={st.opacity:0.##} "
					+ $"rect=({b.x:0},{b.y:0} {b.width:0}x{b.height:0}) children={e.childCount}");
			}
		}
		return found > 0 ? sb.ToString() : "error: no text matches";
	}

	/// <summary>
	/// What is stacked on screen: the children of a named element (the panel manager by default)
	/// a few levels down, with size, opacity and background, to find what covers what.
	/// </summary>
	private static string Layers(string name, int depth)
	{
		var sb = new StringBuilder();
		foreach (var d in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.UIElements.UIDocument>())
		{
			if (d == null || !d.gameObject.activeInHierarchy || d.rootVisualElement == null) continue;
			var stack = new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();
			stack.Push(d.rootVisualElement);
			while (stack.Count > 0)
			{
				var e = stack.Pop();
				if (e.name == name) { Layer(sb, e, 0, depth); break; }
				for (var i = 0; i < e.childCount; i++) stack.Push(e[i]);
			}
		}
		return sb.Length > 0 ? sb.ToString() : "error: no element by that name";
	}

	private static void Layer(StringBuilder sb, UnityEngine.UIElements.VisualElement e, int level, int depth)
	{
		var st = e.resolvedStyle;
		var b = e.worldBound;
		var bg = st.backgroundColor;
		sb.AppendLine($"{new string(' ', level * 2)}{e.GetType().Name} #{e.name} d={st.display} v={st.visibility} op={st.opacity:0.##} "
			+ $"bg={bg.r:0.##},{bg.g:0.##},{bg.b:0.##},{bg.a:0.##} rect=({b.x:0},{b.y:0} {b.width:0}x{b.height:0}) n={e.childCount}");
		if (level >= depth) return;
		for (var i = 0; i < e.childCount; i++) Layer(sb, e[i], level + 1, depth);
	}
}
