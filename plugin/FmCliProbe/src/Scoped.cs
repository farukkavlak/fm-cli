using System.Collections.Specialized;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// A page's own variables: values a BindingSetter keeps under its key for the elements inside
/// it, such as which phase the tactics page shows (`inpossession`). Read, or written with a
/// value copied from another path (`const.False`), the way a SetDatastoreValue event copies
/// its source to its destination (FINDINGS §8.64).
/// </summary>
internal static class Scoped
{
	public static string Handle(NameValueCollection q)
	{
		if (!Buttons.TryPointer(q["ptr"] ?? "", out var pointer)) return "error: ?ptr=<0x…> required (a BindingSetter)";
		var path = q["path"];
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";

		BindingSetter setter = null;
		foreach (var e in Buttons.Elements())
		{
			if (e.Pointer != pointer) continue;
			try { setter = e.TryCast<BindingSetter>(); } catch { }
			break;
		}
		if (setter == null) return $"error: no BindingSetter at {q["ptr"]}";

		var b = Game.Bindings;
		var target = PathKey.Of(setter.Key, path);
		var before = b.Exists(ref target) ? FmApi.Describe(b.Get(ref target)) : "<none>";
		var from = q["from"];
		if (string.IsNullOrEmpty(from)) return $"{path} = {before}";

		var source = PathKey.Of(from);
		if (!b.Exists(ref source)) RawPath.Bind(from);
		if (!b.Exists(ref source)) return $"error: '{from}' has no value to copy";

		b.Set(ref target, b.Get(ref source), Bindings.SetFlags.Default);
		return $"{path}: {before} -> {FmApi.Describe(b.Get(ref target))}";
	}
}
