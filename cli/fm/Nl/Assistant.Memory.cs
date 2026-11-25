using FmCli.Nl;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// The club and save being played: our club's name, the key the save's talk and notes are kept under
	/// (the club's UniqueId and the save's game name, both the game's own), and today's game day. Empty when
	/// no club is played or the game cannot be read.
	/// </summary>
	private static async Task<(string Club, string Save, string Day)> SaveNow()
	{
		string club = "", save = "", day = "";
		try
		{
			var human = await Fm.Pull("Human.Club", "Name,UniqueId");
			club = Field(human, "Name");
			if (Field(human, "UniqueId") is { Length: > 0 } id)
				save = $"{id}|{Field(await Fm.Pull("app.LastSaveSummary", "GameSummaryGameName"), "GameSummaryGameName")}";
			day = Field(await Fm.GetJson("state"), "Date");
		}
		catch (Exception e) when (e is FmError or FmBusy) { }
		return (club, save, day);
	}

	private static string KindShown(string kind) => kind switch
	{
		"rule" => L("rule"),
		"plan" => L("plan"),
		"about" => L("about you"),
		_ => L("wish"),
	};

	private static string NoteShown(Memory.Note n) =>
		$"#{n.N} {n.Text}  {Dim($"{KindShown(n.Kind)} · {(n.Day.Length > 0 ? DayHeading(n.Day) : n.At.ToLocalTime().ToString("yyyy-MM-dd"))}")}";

	/// <summary>
	/// `memory`: the notes kept for this save, by number. `memory add <note>` (also `remember <note>`),
	/// `memory fix <number> <note>`, `memory delete <number>|all`. `--plan`, `--rule`, `--about` give a
	/// note its kind (a wish otherwise). Nothing in the game is touched; the game is read only to know the save.
	/// </summary>
	private static async Task<int> MemoryCommand(string[] args, bool json, bool remember = false)
	{
		var kind = args.Contains("--plan") ? "plan" : args.Contains("--rule") ? "rule" : args.Contains("--about") ? "about" : "wish";
		var words = Words([.. args.Where(a => a is not ("--plan" or "--rule" or "--about"))]).ToList();
		var verb = remember ? "add" : words.Count == 0 ? "" : Text.Fold(words[0]) switch
		{
			"add" or "ekle" or "yaz" => "add",
			"fix" or "duzelt" or "edit" or "change" or "degistir" => "fix",
			"delete" or "sil" or "unut" or "forget" or "remove" => "delete",
			_ => "?",
		};
		if (verb == "?")
		{
			Console.Error.WriteLine(L("usage: memory · memory add <note> · memory fix <number> <note> · memory delete <number>|all"));
			return 64;
		}
		if (!remember && verb.Length > 0) words.RemoveAt(0);

		var (club, save, day) = await SaveNow();
		if (save.Length == 0)
		{
			Console.Error.WriteLine(L("Notes are kept per club and save: load a save with a club first (status)."));
			return 1;
		}
		var book = Memory.Of(save);
		book.Club = club;

		int? Number(string word) => int.TryParse(word.TrimStart('#'), out var n) && book.Notes.Any(x => x.N == n) ? n : null;
		int Wrote(Memory.Note note, bool fixedOne)
		{
			if (!Memory.Keep(save, book))
			{
				Console.Error.WriteLine(L($"Could not write {Memory.File}."));
				return 1;
			}
			if (json) Console.WriteLine(JsonOut.Of(note));
			else
			{
				var shown = NoteShown(note);
				Console.WriteLine(fixedOne ? L($"Fixed: {shown}") : L($"Kept: {shown}"));
			}
			return 0;
		}

		switch (verb)
		{
			case "add":
			{
				var text = string.Join(" ", words);
				if (Memory.NotANote(text, book) is { } fault)
				{
					Console.Error.WriteLine(text.Trim().Length == 0 ? L("usage: memory add <note>   (--plan, --rule, --about: its kind)") : L($"Not kept: {fault}."));
					return 64;
				}
				return Wrote(Memory.Write(book, text, kind, day, "user"), false);
			}
			case "fix":
			{
				if (words.Count < 2 || Number(words[0]) is not { } n)
				{
					Console.Error.WriteLine(words.Count == 0 ? L("usage: memory fix <number> <note>") : L($"No such note: {words[0]} (memory lists them)"));
					return 64;
				}
				var text = string.Join(" ", words.Skip(1));
				if (Memory.NotANote(text, book, n) is { } fault)
				{
					Console.Error.WriteLine(L($"Not kept: {fault}."));
					return 64;
				}
				var old = book.Notes.First(x => x.N == n);
				var kindNow = args.Any(a => a is "--plan" or "--rule" or "--about") ? kind : old.Kind;
				return Wrote(Memory.Write(book, text, kindNow, day, "user", n), true);
			}
			case "delete":
			{
				if (words.Count == 0)
				{
					Console.Error.WriteLine(L("Which one: memory delete <number> (or all)"));
					return 64;
				}
				if (Text.Fold(words[0]) is "all" or "hepsi" or "tumu")
				{
					var count = book.Notes.Count;
					book.Notes.Clear();
					if (!Memory.Keep(save, book)) return 1;
					Console.WriteLine(L($"Forgot all {count} notes of this save."));
					return 0;
				}
				if (Number(words[0]) is not { } n)
				{
					Console.Error.WriteLine(L($"No such note: {words[0]} (memory lists them)"));
					return 64;
				}
				var gone = book.Notes.First(x => x.N == n);
				book.Notes.Remove(gone);
				if (!Memory.Keep(save, book)) return 1;
				var goneShown = NoteShown(gone);
				Console.WriteLine(L($"Forgot: {goneShown}"));
				return 0;
			}
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Club = club, book.Notes }));
			return 0;
		}
		if (book.Notes.Count == 0)
		{
			Console.WriteLine(L("No notes for this save. Keep one: remember <note>, or tell ask (\"remember that …\")."));
			return 0;
		}
		Console.WriteLine(L($"Notes for {club}, this save:"));
		foreach (var n in book.Notes) Console.WriteLine($"  {NoteShown(n)}");
		Console.WriteLine(Dim(L("memory fix <number> <note> · memory delete <number>|all")));
		return 0;
	}

	/// <summary>
	/// A note the assistant offers, put to the user: kept or forgotten on a yes, never without one, and not
	/// asked at all away from a terminal or in --json.
	/// </summary>
	private static Ask.Offered OfferNote(Ask.Offer offer, Memory.Book book, string save, string day, bool json)
	{
		if (json || !Ui.Interactive) return new Ask.Offered(offer, false, false, null);
		Ui.Progress(null);
		var shown = offer.Forget is { } f
			? $"{L("Forget the note?")} {NoteShown(book.Notes.First(n => n.N == f))}"
			: offer.Replaces is { } r
				? $"{L("Change the note?")} #{r}: {offer.Text}  {Dim(KindShown(offer.Kind))}"
				: $"{L("Keep a note?")} {offer.Text}  {Dim(KindShown(offer.Kind))}";
		Console.Write($"{shown} {L("[y/N]")} ");
		if (!IsYes((Console.ReadLine() ?? "").Trim())) return new Ask.Offered(offer, true, false, null);
		Memory.Note? kept = null;
		if (offer.Forget is { } gone) book.Notes.RemoveAll(n => n.N == gone);
		else kept = Memory.Write(book, offer.Text, offer.Kind, day, "assistant", offer.Replaces);
		if (!Memory.Keep(save, book))
		{
			Console.Error.WriteLine(L($"Could not write {Memory.File}."));
			return new Ask.Offered(offer, true, false, null);
		}
		return new Ask.Offered(offer, true, true, kept);
	}
}
