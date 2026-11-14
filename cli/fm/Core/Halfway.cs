namespace FmCli;

/// <summary>
/// A run of several changes that stopped part way (T153): what was done and what was not,
/// so the user is not left to guess what the game now holds.
/// </summary>
internal static class Halfway
{
	/// <summary>
	/// Says it when something was already done, or always for steps the user asked for one by
	/// one (a sentence's commands); otherwise a run that failed at its first step says nothing
	/// more than its error.
	/// </summary>
	public static int Stop(IReadOnlyCollection<string> done, IEnumerable<string> left, int code, bool always = false)
	{
		var notDone = left.ToList();
		if (done.Count == 0 && !always) return code;
		if (done.Count > 0) Console.Error.WriteLine(L($"Done: {string.Join(", ", done)}"));
		if (notDone.Count > 0) Console.Error.WriteLine(L($"Not run: {string.Join(", ", notDone)}"));
		return code;
	}
}
