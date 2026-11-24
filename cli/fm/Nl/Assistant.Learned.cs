using FmCli.Nl;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// `learned`: what the router learned from the user's choices, numbered; `learned delete N`
	/// (or `all`) forgets it. Nothing in the game is touched.
	/// </summary>
	private static int LearnedCommand(string[] args)
	{
		var store = Learned.Load();
		var rows = new List<(string Shown, Action Forget)>();
		foreach (var s in store.Sentences)
			rows.Add(($"\"{s.Typed}\" → {s.Shown}", () => store.Sentences.Remove(s)));
		if (store.Pending is { } p)
			rows.Add(($"\"{p.Sentence.Typed}\" → {p.Sentence.Shown}  {Dim(L("(kept at the next command, unless it puts this right)"))}", () => store.Pending = null));
		foreach (var n in store.Names)
			rows.Add(($"\"{n.Typed}\" → {n.Full}", () => store.Names.Remove(n)));

		var words = Words(args).ToList();
		if (words.Count > 0 && Nl.Text.Fold(words[0]) is "delete" or "sil" or "unut" or "forget")
		{
			if (words.Count < 2)
			{
				Console.Error.WriteLine(L("Which one: learned delete <number> (or all)"));
				return 64;
			}
			if (Nl.Text.Fold(words[1]) is "all" or "hepsi" or "tumu")
			{
				Learned.Save(new Learned.Store());
				Console.WriteLine(L($"Forgot all {rows.Count}."));
				return 0;
			}
			if (!int.TryParse(words[1], out var n) || n < 1 || n > rows.Count)
			{
				Console.Error.WriteLine(L($"No such number: {words[1]} (1-{rows.Count})"));
				return 64;
			}
			var gone = rows[n - 1];
			gone.Forget();
			Learned.Save(store);
			Console.WriteLine(L($"Forgot: {gone.Shown}"));
			return 0;
		}

		if (rows.Count == 0)
		{
			Console.WriteLine(L("Nothing learned yet: a command picked from the list offered for a sentence is learned."));
			return 0;
		}
		Console.WriteLine(L("Learned from your choices:"));
		for (var i = 0; i < rows.Count; i++) Console.WriteLine($"  {i + 1,2}. {rows[i].Shown}");
		Console.WriteLine(Dim(L("Forget one: learned delete <number> · all: learned delete all")));
		return 0;
	}
}
