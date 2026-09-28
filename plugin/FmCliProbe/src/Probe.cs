using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Il2CppInterop.Runtime;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// Reconnaissance run, triggered with F10.
///
/// The first probe established that a live Bindings instance is reachable. This one answers the
/// question that decides the whole architecture: what is actually *in* the binding tree, and can
/// we address it by name?
/// </summary>
internal sealed class Probe
{
	private const string ReportName = "fm-cli-probe.txt";

	/// <summary>Dumping the whole tree could be enormous; stop before we hang the game.</summary>
	private const int MaxNodes = 20000;
	private const int MaxDepth = 6;

	private bool _ran;
	private readonly StringBuilder _report = new();
	private int _nodes;

	public void Tick()
	{
		try
		{
			// FM26 is on the new Input System; the legacy Input class is not wired up.
			if (Keyboard.current == null) return;
			if (!((ButtonControl)Keyboard.current.f10Key).wasPressedThisFrame) return;
		}
		catch
		{
			return; // Keyboard is not up yet during early boot.
		}

		if (_ran)
		{
			Plugin.Log.LogInfo("[probe] already ran; restart the game to re-probe.");
			return;
		}

		_ran = true;
		try { Run(); }
		catch (Exception ex) { Say($"!! probe threw {ex.GetType().Name}: {ex}"); Flush(); }
	}

	private void Run()
	{
		Say("=== fm-cli probe v2 ===");
		Say($"time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

		var bindings = GetBindings();
		if (bindings == null)
		{
			Say("!! no Bindings instance");
			Flush();
			return;
		}

		Say("++ Bindings instance acquired");

		// How big is the tree, and is it global or just the open screen?
		var rootKey = Bindings.RootKey;
		try { Say($"++ GetDescendantCount(RootKey) = {bindings.GetDescendantCount(ref rootKey)}"); }
		catch (Exception ex) { Say($"-- GetDescendantCount threw {ex.GetType().Name}: {ex.Message}"); }

		// Does a plain string path work, now that we can build a span?
		foreach (var path in new[] { "Condition", "Fitness", "TransferListed", "Squad", "Player", "Game" })
			TryPath(bindings, path);

		// The real payload: walk the tree and write down what exists.
		WalkTree(bindings);

		Flush();
	}

	private static Bindings GetBindings()
	{
		// The dump showed no global accessor; this ScriptableObject holds one and the first
		// probe confirmed the route works.
		var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<FM.UI.NewGameHandlerModule>());
		foreach (var o in all)
		{
			var module = o.TryCast<FM.UI.NewGameHandlerModule>();
			if (module?.m_bindings != null)
				return module.m_bindings;
		}
		return null;
	}

	private void TryPath(Bindings b, string path)
	{
		try
		{
			var span = Interop.Span(path);
			var exists = b.Exists(ref span);
			if (!exists)
			{
				Say($"   Exists(\"{path}\") = false");
				return;
			}

			var span2 = Interop.Span(path);
			var value = b.Get(span2);
			Say($"   Exists(\"{path}\") = true   Get -> {Describe(value)}");
		}
		catch (Exception ex)
		{
			Say($"   \"{path}\" threw {ex.GetType().Name}: {ex.Message}");
		}
	}

	// ------------------------------------------------------------------ walk

	private void WalkTree(Bindings b)
	{
		IReadOnlyNode root;
		try
		{
			root = b.RootNode;
		}
		catch (Exception ex)
		{
			Say($"-- RootNode threw {ex.GetType().Name}: {ex.Message}");
			return;
		}

		if (root == null)
		{
			Say("-- RootNode is null");
			return;
		}

		Say("");
		Say($"=== tree walk (max depth {MaxDepth}, max {MaxNodes} nodes) ===");
		Walk(root, "", 0);
		Say($"=== walked {_nodes} node(s) ===");
	}

	private void Walk(IReadOnlyNode node, string prefix, int depth)
	{
		if (_nodes >= MaxNodes || depth > MaxDepth) return;

		string name;
		try { name = node.Name ?? "<null>"; }
		catch { name = "<name threw>"; }

		var path = prefix.Length == 0 ? name : prefix + "." + name;
		_nodes++;

		string value = "";
		try
		{
			var v = node.Value;
			if (v != null && !v.IsNull) value = " = " + Describe(v);
		}
		catch (Exception ex) { value = $" = <threw {ex.GetType().Name}>"; }

		int children;
		try { children = node.ChildCount; }
		catch { children = 0; }

		Say($"{new string(' ', depth * 2)}{name}{value}{(children > 0 ? $"  [{children}]" : "")}");

		if (children == 0) return;

		try
		{
			for (var c = node.FirstChild; c != null; c = c.NextSibling)
			{
				Walk(c, path, depth + 1);
				if (_nodes >= MaxNodes) return;
			}
		}
		catch (Exception ex)
		{
			Say($"{new string(' ', depth * 2)}  <children threw {ex.GetType().Name}>");
		}
	}

	private static string Describe(SI.Core.TypedValue v)
	{
		if (v == null) return "<null ref>";
		try
		{
			if (v.IsNull) return "<null>";
			var type = v.DataType?.Name ?? "?";
			return $"({type}) {v.AsString()}";
		}
		catch (Exception ex)
		{
			return $"<threw {ex.GetType().Name}>";
		}
	}

	// -------------------------------------------------------------- plumbing

	private void Say(string line)
	{
		Plugin.Log.LogInfo("[probe] " + line);
		_report.AppendLine(line);
	}

	private void Flush()
	{
		try
		{
			var path = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ReportName);
			File.WriteAllText(path, _report.ToString());
			Plugin.Log.LogInfo($"[probe] report written to {path} ({_report.Length} chars)");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError($"[probe] could not write report: {ex.Message}");
		}
	}
}
