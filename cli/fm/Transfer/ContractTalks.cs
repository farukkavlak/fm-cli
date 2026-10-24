using System.Text.Json;

namespace FmCli;

/// <summary>
/// The contract talks, drawn as a card and changed term by term like the transfer offer
/// (FINDINGS §8.44). They open after a club accepts a bid ("Enter Contract Negotiations" on the
/// message) or, for one of our own players, from his page: a renewal. They come in two stages,
/// promises and then terms; each is a card of its own.
/// </summary>
internal static partial class Program
{
	// Only the talks' own buttons: the game's Continue is also labelled "Negotiate Contract"
	// during the talks, and pressing it walks away from the deal (FINDINGS §8.42).
	private const string TalksButton = "StandardButton";

	/// <summary>What the way out of the talks sends (InteropEventIDs.EventFmxExitNegotiations).</summary>
	private const string ExitNegotiations = "42";

	/// <summary>
	/// What walking away from the terms sends (InteropEventIDs.EventFmxWithdrawContractNegotiationProcess):
	/// the button the game labels "Cancel Negotiations". Its label is one of the game's own strings and
	/// this language's table may give several English texts the same word, so the event is what tells it
	/// apart (FINDINGS §8.296).
	/// </summary>
	private const string WithdrawNegotiation = "811955833";

	private static async Task<int> Contract(string[] args)
	{
		if (args.Contains("--agent")) return await AskAgent(args);
		var wanted = string.Join(" ", Words(args));
		var screen = await OfferScreen.Read(Fm, "contract");
		if (screen != null) screen = await TalksSettled();

		if (screen == null)
		{
			if (wanted.Length == 0)
			{
				Console.Error.WriteLine(L("Açık bir sözleşme görüşmesi yok.\n")
					+ L("  Kendi oyuncumuzla yenilemek için: contract <oyuncu>\n")
					+ L("  Transferde: teklif kabul edilince reply \"<oyuncu> offer accepted\" → Enter Contract Negotiations"));
				return 1;
			}
			if (await NotNow() is { } refused) return refused;
			if (await OpenRenewal(wanted) is { } failed) return failed;
			if ((screen = await TalksSettled()) == null)
			{
				Console.Error.WriteLine(L("Sözleşme ekranı açılmadı."));
				return 1;
			}
		}
		else if (wanted.Length > 0 && !Fold(screen.Player).Contains(Fold(wanted), StringComparison.Ordinal))
		{
			Console.Error.WriteLine(L($"Açık görüşme {screen.Player} ile. Önce onu bitir ya da bırak (contract ile aç, 'Görüşmeyi iptal et')."));
			return 1;
		}

		if (args.Contains("--cancel"))
		{
			var failed = await CallOff();
			// The talks were opened from a card (his transfer status with the director beside it), which
			// comes back up and reads as a question waiting; the portal is opened over it.
			if (failed == null) await OpenPage(Screen.Home);
			Console.WriteLine(failed ?? L("Görüşme iptal edildi."));
			return failed == null ? 0 : 1;
		}

		var terms = new List<string>();
		for (var i = 0; i < args.Length - 1; i++)
			if (args[i] == "--term") terms.Add(args[i + 1]);
		if (Option(args, "--wage") is { } wage) terms.Add($"Wage={wage}");

		if (Ui.Interactive && !args.Contains("--sign")) return await HoldTalks(terms);

		// A script: the asked-for terms go to whichever stage has them. The promises are only
		// taken, and the terms only put to the agent, when asked: --next, --sign --yes.
		var left = await ApplyTerms(terms);
		if (left == null) return 1;
		var onward = args.Contains("--next") || args.Contains("--sign");
		if (onward && await OfferScreen.Read(Fm, "contract") is { Stage: "Promises" })
		{
			// Changed promises are put to the agent first; he takes them or answers with his own,
			// and only promises he has taken are finalised.
			if (await Labelled("Suggest Promises", TalksButton) != null && await Propose() == Outcome.Failed) return 1;
			if (await Labelled("Finalise Promises", TalksButton) != null)
			{
				if (await PastPromises() is { } stuck) { Console.Error.WriteLine(stuck); return 1; }
				left = await ApplyTerms(left);
				if (left == null) return 1;
			}
		}

		var shown = await TalksSettled();
		var json = args.Contains("--json");
		if (shown != null && json) Console.WriteLine(JsonOut.Of((await shown.WithReasons(Fm)).Summary()));
		else if (shown != null) (await shown.WithReasons(Fm)).Draw();
		if (left.Count > 0)
		{
			Console.Error.WriteLine(shown is { Stage: "Promises" }
				? L($"Vaatlerde yok, şartlarda denenecek: {string.Join(", ", left)} — vaatleri onaylayıp geçmek için --next ekle.")
				: L($"Ekranda bulunamadı: {string.Join(", ", left)}"));
			return 1;
		}
		if (json && !args.Contains("--sign")) return 0;
		if (shown is { Stage: "Promises" })
		{
			// A card whose promises offer nothing to choose is one the game has locked (seen with
			// a 34-year-old in his last year, FINDINGS §8.94): --next does nothing on it.
			if (shown.Rows.Count > 0 && shown.Rows.All(r => r.Choice is { Value: "None Selected", Choices.Count: 0 }))
			{
				Console.WriteLine(L("\nOyun bu kartı kilitli açtı: vaatlerde seçilecek bir şey yok, görüşme ilerlemiyor. Çıkmak için: contract --cancel   (menajere sormak: contract <oyuncu> --agent)"));
				return 1;
			}
			Console.WriteLine(L("\nVaatleri onaylayıp şartlara geçmek için: contract --next   (vaat değiştirmek: --term \"Vaat=değer\")"));
			return 0;
		}
		if (!args.Contains("--sign"))
		{
			Console.WriteLine(L("\nMenajere sunmak / imzalamak için: contract --sign --yes   (şart değiştirmek: --wage <tutar>, --term \"Şart=değer\")"));
			return 0;
		}
		if (!args.Contains("--yes"))
		{
			Console.Error.WriteLine(L("Sözleşme imzalanınca geri alınamaz. Bitirmek için --yes ekle."));
			return 1;
		}
		// Terms we changed go to the agent first; taken as they stand, --yes has already said to sign them.
		var put = await Propose();
		if (put == Outcome.Accepted) put = await Propose();
		return put == Outcome.Failed ? 1 : 0;
	}

	/// <summary>
	/// The terms asked for by name that this stage has, set on it; the rest come back to be
	/// tried on the next stage. Null when one of them failed.
	/// </summary>
	private static async Task<List<string>> ApplyTerms(List<string> terms)
	{
		var left = new List<string>();
		foreach (var asked in terms)
		{
			if (await SetTerm("contract", asked) is not { } failed) continue;
			if (failed.StartsWith(L("böyle bir şart yok"), StringComparison.Ordinal)) { left.Add(asked); continue; }
			Console.Error.WriteLine($"{asked}: {failed}");
			return null;
		}
		return left;
	}

	/// <summary>At a terminal: the card, and the arrows to change it, until it is signed or put aside.</summary>
	private static async Task<int> HoldTalks(List<string> terms)
	{
		var failed = await ApplyTerms(terms) is { Count: 0 } ? null : L("Bazı --term'ler bu aşamada yok; kartta elle değiştir.");
		var said = await AgentSays();
		var notes = new List<string>();
		while (true)
		{
			if (await OfferScreen.Read(Fm, "contract") is not { } now)
			{
				Console.WriteLine(L("Görüşme ekranı kapandı."));
				return 0;
			}
			now = await now.WithReasons(Fm);
			Ui.Out.Clear(true);
			now.Draw();
			if (notes.Count > 0) foreach (var note in notes) Ui.Line(Ui.Safe(note));
			else if (said.Length > 0) Ui.Line($"[italic]{Ui.Safe(said)}[/]");
			notes.Clear();
			if (failed != null) Ui.Line($"[red]{Ui.Safe(failed)}[/]");
			failed = null;

			var promises = now.Stage == "Promises";
			var items = new List<string>();
			var actions = new List<Func<Task<string?>>>();

			if (promises && await Labelled("Finalise Promises", TalksButton) != null)
			{
				items.Add(L("Vaatleri onayla, şartlara geç"));
				actions.Add(PastPromises);
			}
			else if (promises && await Labelled("Suggest Promises", TalksButton) != null)
			{
				items.Add(L("Vaatleri menajere öner"));
				actions.Add(async () => await Propose(notes) == Outcome.Failed ? L("Öneri gönderilemedi.") : null);
			}
			else if (promises)
			{
				items.Add(L("Vaatler gönderilemiyor: kırmızı satırları düzelt"));
				actions.Add(() => Task.FromResult<string>(null));
			}
			else if (await Labelled("Finalise Deal", TalksButton) != null)
			{
				items.Add(L("Bu şartlarla imzala"));
				actions.Add(async () => await Signs(notes) == Outcome.Signed ? "" : null);
			}
			else if (await Labelled("Suggest Terms", TalksButton) != null)
			{
				items.Add(L("Şartları menajere öner"));
				actions.Add(async () => await Propose(notes) == Outcome.Signed ? "" : null);
			}
			else
			{
				// The game takes the proposal away while a term breaks its rules; the row says which.
				items.Add(L("Öneri gönderilemiyor: kırmızı satırları düzelt"));
				actions.Add(() => Task.FromResult<string>(null));
			}

			foreach (var row in now.Rows)
			{
				items.Add(OfferScreen.MenuLine(row) + (row.Detail.Length > 0 && now.Stage == "Terms" ? $"  ({OfferScreen.SayDetail(row.Detail)})" : ""));
				actions.Add(() => EditTerm("contract", row));
			}
			foreach (var option in now.Options.Where(o => o.Lock is { Choices.Count: > 1 }))
			{
				items.Add($"{OfferScreen.Say(option.Label)}: {OfferScreen.SayLock(option.Lock.Value)}");
				actions.Add(() => PickLock("contract", option, s => s.Options.FirstOrDefault(o => o.Lock?.Pointer == option.Lock.Pointer)?.Lock));
			}
			foreach (var add in now.Adds.Where(a => a.Choice.Open.Count > 0))
			{
				items.Add(AddLine(add));
				actions.Add(() => AddTerm("contract", add));
			}
			if (!promises && await Labelled("Reset") != null)
			{
				items.Add(L("Sıfırla (menajerin istediklerine dön)"));
				actions.Add(async () => await Labelled("Reset") is { } reset ? (await Fm.Get("press", ("ptr", Field(reset, "Pointer")))).StartsWith("error", StringComparison.Ordinal) ? L("Sıfırlanamadı.") : null : null);
			}
			if (await SpeakToActionEvent(WithdrawNegotiation) != null)
			{
				items.Add(L("Görüşmeyi iptal et"));
				actions.Add(CancelTalks);
			}

			if (Ui.Ask(promises ? L("Vaatler — değiştirmek istediğini seç:") : L("Sözleşme — değiştirmek istediğini seç:"), items, cancel: L("Şimdilik bırak (görüşme açık kalır)")) is not { } picked)
				return 0;

			var before = await AgentSays();
			failed = await actions[picked]();
			// An empty answer is the end of the talks: signed, or called off.
			if (failed == "") return 0;
			var after = await AgentSays();
			said = after != before ? after : "";
			if (await OfferScreen.Read(Fm, "contract") == null) return 0;
		}

		async Task<Outcome> Signs(List<string> told)
		{
			if (Ui.Ask(L("Sözleşme imzalanınca geri alınamaz."), [L("İmzala")], cancel: L("Geri dön")) == null) return Outcome.Answered;
			var outcome = await Propose(told);
			// Signed: the card is gone, so what was said is said here.
			if (outcome == Outcome.Signed) foreach (var line in told) Console.WriteLine(line);
			return outcome;
		}

		async Task<string?> CancelTalks()
		{
			if (Ui.Ask(L("Görüşme kapanacak; menajer bundan hoşlanmayabilir."), [L("İptal et")], cancel: L("Geri dön")) == null) return null;
			if (await CallOff() is { } failed) return failed;
			await OpenPage(Screen.Home);
			Console.WriteLine(L("Görüşme iptal edildi."));
			return "";
		}
	}

	/// <summary>The promises taken as they stand, and on to the terms. Null once the terms are open.</summary>
	private static async Task<string?> PastPromises()
	{
		async Task<bool> OnTerms() => await OfferScreen.Read(Fm, "contract") is { Stage: "Terms" };

		// Finalise Promises, then Negotiate Contract: each is waited out before the next, since
		// the screen keeps the promises a moment after the first is pressed.
		foreach (var label in new[] { "Finalise Promises", "Negotiate Contract" })
		{
			if (await OnTerms()) return null;
			if (!await Until(async () => await Labelled(label, TalksButton) != null || await OnTerms(), seconds: 8, every: 0.25)) break;
			if (await Labelled(label, TalksButton) is not { } button) continue;
			await Fm.Get("press", ("ptr", Field(button, "Pointer")));
			await Until(async () => await Labelled(label, TalksButton) == null || await OnTerms(), seconds: 8, every: 0.25);
		}
		return await TalksSettled("Terms") is { Stage: "Terms" } ? null : L("Şartlara geçilemedi; menajer başka bir şey bekliyor olabilir (talk ile bak).");
	}

	/// <summary>Accepted: the agent took what was suggested as it stood, and Finalise now closes it.</summary>
	private enum Outcome { Failed, Answered, Accepted, Signed }

	/// <summary>
	/// Puts the stage to the agent: his own promises or terms are taken with Finalise, changed
	/// ones go with Suggest and he answers, either taking them or putting his own back on the card.
	/// </summary>
	private static async Task<Outcome> Propose(List<string>? notes = null)
	{
		// At a terminal the card is drawn again after this, so what is said goes beneath it.
		void Tell(string line) { if (notes != null) notes.Add(line); else Console.WriteLine(line); }

		var ours = await OfferScreen.Read(Fm, "contract");
		var promises = ours is { Stage: "Promises" };
		var (finalise, suggest) = promises ? ("Finalise Promises", "Suggest Promises") : ("Finalise Deal", "Suggest Terms");
		if ((await Labelled(finalise, TalksButton) ?? await Labelled(suggest, TalksButton)) is not { } button)
		{
			Console.Error.WriteLine(L($"'{finalise}' / '{suggest}' butonu yok: ya bir şart kurala uymuyor (kartta kırmızı), ya görüşme başka bir adımda (talk ile bak)."));
			return Outcome.Failed;
		}
		var finalising = Field(button, "Label") == finalise;
		if (promises && finalising)
		{
			if (await PastPromises() is not { } stuck) return Outcome.Answered;
			Console.Error.WriteLine(stuck);
			return Outcome.Failed;
		}
		var before = await AgentSays();
		await Fm.Get("press", ("ptr", Field(button, "Pointer")));

		var said = before;
		await Until(async () => (said = await AgentSays()) != before, seconds: 12, every: 0.25);
		if (said != before && said.Length > 0) Tell(said);

		var next = await TalkOptions();
		// What is left to say is the way out and the budget adjustment, which changes no term: the
		// agent has taken the offer away. Both are told apart by the game's own action, not by what
		// this language calls them (the way out carries IsExitButton; see TalkEnds).
		var (exit, changing) = await TalkEnds();
		if (exit is { } way && changing == 0)
		{
			await LeaveTalks(way);
			if (!await Until(async () => await OfferScreen.Read(Fm, "contract") == null, seconds: 6, every: 0.25))
			{
				Tell(L("Çıkış denendi ama sözleşme görüşmesi hâlâ açık (talk ile bak)."));
				return Outcome.Answered;
			}
			// A renewal is not signed there and then: the agent takes it away and answers in a few days.
			// That is in the agent's words, which only an English game's are known here; in another
			// language which it was is not guessed.
			var english = Field(await Fm.GetJson("state"), "Language") is "" or "english";
			var later = said.Contains("final decision", StringComparison.OrdinalIgnoreCase) || said.Contains("be in touch", StringComparison.OrdinalIgnoreCase);
			Tell(!finalising ? L("Görüşme kapandı.")
				: !english ? L("Görüşme kapandı: sözleşme ya imzalandı ya da kararı birkaç gün içinde gelen kutusuna düşer (contracts).")
				: later ? L("Teklif karşı tarafta; kararı birkaç gün içinde gelen kutusuna düşer.")
				: L("Sözleşme imzalandı; görüşme kapandı."));
			return finalising ? Outcome.Signed : Outcome.Answered;
		}

		// What he put back in place of ours is what he wants.
		var theirs = await OfferScreen.Read(Fm, "contract");
		var changed = ours == null || theirs == null ? [] : theirs.Rows
			.Select(r => (r, was: ours.Row(r)))
			.Where(p => p.was == null || p.was.Shown != p.r.Shown)
			.Select(p => $"{OfferScreen.Say(p.r.Label)}: {(p.was == null ? "" : p.was.Shown + " → ")}{p.r.Shown}")
			.Concat(ours?.Rows.Where(r => theirs?.Row(r) == null).Select(r => L($"{OfferScreen.Say(r.Label)}: çıkarıldı")) ?? [])
			.ToList();
		if (changed.Count > 0) Tell(L($"Menajerin istediği: {string.Join(" · ", changed)}"));

		if (next.Any(l => l.Label == finalise))
		{
			if (!promises && changed.Count == 0 && !finalising)
			{
				// On the card the next choice offered is to sign; from the command line --yes has said so already.
				if (notes != null) Tell(L("Menajer bu şartları kabul ediyor."));
				return Outcome.Accepted;
			}
			Tell(promises
				? changed.Count == 0 ? L("Menajer bu vaatleri kabul ediyor. Onaylayıp şartlara geçmek için: contract --next") : L("Bunları kabul edip şartlara geçmek için: contract --next · pazarlığa devam: --term")
				: changed.Count == 0 ? L("Menajer bu şartları kabul ediyor. İmzalamak için: contract --sign --yes") : L("Bunları kabul edip imzalamak için: contract --sign --yes · pazarlığa devam: --wage / --term"));
		}
		else if (next.Count > 0)
			Tell(L($"Sırada: {string.Join(" · ", next.Select(l => l.Label))}"));
		return Outcome.Answered;
	}

	/// <summary>
	/// Calls the talks off. The game asks first ("you may not be able to immediately re-enter
	/// talks"); the question has been put already by whoever asked for this. Null once closed.
	/// </summary>
	private static async Task<string?> CallOff()
	{
		if (await SpeakToActionEvent(WithdrawNegotiation) is not { } cancel)
		{
			// The promises stage has no cancel either: nothing has been put to the agent yet, and
			// walking off the page is the way out. That button stands for the game's own Exit
			// Negotiations event (42); its label is this language's string and is not asked for
			// (FINDINGS §8.209). The press is queued; opening another page before it is handled
			// only hides the talks beneath it, so they are waited on to close first.
			var exit = false;
			for (var attempt = 0; attempt < 3; attempt++)
			{
				if (await SpeakToActionEvent(ExitNegotiations) is not { } way) break;
				exit = true;
				await Fm.Get("press", ("ptr", Field(way, "Pointer")));
				if (await Until(async () => await OfferScreen.Read(Fm, "contract") == null, seconds: 3, every: 0.25)) break;
			}
			await OpenPage(Screen.Home);
			if (await Until(async () => await OfferScreen.Read(Fm, "contract") == null, seconds: 4, every: 0.25)) return null;
			return exit ? L("Çıkış denendi ama sözleşme görüşmesi hâlâ açık.") : L("Görüşmede çıkış düğmesi bulunamadı.");
		}
		await Fm.Get("press", ("ptr", Field(cancel, "Pointer")));

		JsonElement dialog = default;
		if (await Until(async () => (dialog = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 5, every: 0.25)
			&& DialogAnswer(dialog[0], true) is { } confirm)
			await Fm.Get("click", ("ptr", Field(confirm, "Pointer")));

		// Withdrawing closes the talks itself: no "Exit Talks" step follows it, so waiting for that
		// button only burned its whole 6 s ceiling before the screen was read (FINDINGS §8.296).
		// The screen closing is the game's own signal.
		return await Until(async () => await OfferScreen.Read(Fm, "contract") == null, seconds: 6, every: 0.25)
			? null : L("İptal denildi ama görüşme ekranı hâlâ açık (talk ile bak).");
	}

	/// <summary>
	/// The talks once their terms are filled in: the screen opens with its rows empty and fills
	/// them a moment later.
	/// </summary>
	private static async Task<OfferScreen?> TalksSettled(string? stage = null)
	{
		OfferScreen? screen = null;
		string? last = null;
		await Until(async () =>
		{
			if ((screen = await OfferScreen.Read(Fm, "contract")) is not { } s || !s.Rows.Any(r => r.Shown != "—") || (stage != null && s.Stage != stage)) return false;
			if (s.Club.Length > 0 && s.Facts.ContainsKey("Wage")) return true;
			// One without a club has no wage of his own to wait for (a free agent, staff out of work):
			// two readings alike are taken as whole.
			var now = string.Join("|", s.Rows.Select(r => r.Label + "=" + r.Shown));
			var same = now == last;
			last = now;
			return same;
		}, seconds: 8, every: 0.25);
		return screen;
	}

	/// <summary>
	/// The way out of the talks and how many of their other buttons still change the deal. The way
	/// out is the button whose action is the game's exit (<c>IsExitButton</c>), and a button that
	/// changes a term is one whose action sends an event of its own; the budget adjustment sends
	/// none. Neither is asked for by its label, which is this language's string (FINDINGS §8.209).
	/// </summary>
	private static async Task<(JsonElement? Exit, int Changing)> TalkEnds()
	{
		var buttons = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Events") == "SpeakToAction" && Field(b, "Name") == TalksButton && Field(b, "Action").Length > 0 && Field(b, "Enabled") == "True")
			.ToList();
		if (buttons.Count == 0) return (null, 0);
		var actions = await PullEach(buttons.Select(b => (Field(b, "Action"), "Event,IsExitButton")).ToList());
		JsonElement? exit = null;
		var changing = 0;
		foreach (var (button, action) in buttons.Zip(actions))
		{
			if (Field(action, "IsExitButton") == "True") exit ??= button;
			else if (Field(action, "Event") is not ("" or "0")) changing++;
		}
		return (exit, changing);
	}

	/// <summary>Walking out of the talks by the game's own way out: the talks close themselves.</summary>
	private static async Task LeaveTalks(JsonElement exit) =>
		await Fm.Get("press", ("ptr", Field(exit, "Pointer")));

	/// <summary>What the agent said last, as one line; the echo of our own line ("» …") left out.</summary>
	private static async Task<string?> AgentSays()
	{
		var talk = await Fm.GetJson("talk");
		if (!talk.TryGetProperty("Says", out var says)) return "";
		return string.Join(" ", says.EnumerateArray().Select(l => l.GetString() ?? "").Where(l => !l.StartsWith("»", StringComparison.Ordinal))).Trim();
	}

	/// <summary>
	/// Opens the talks for a new contract with one of our players, the way the game does: his
	/// value opens his actions, "Enter Contract Negotiations" the talks. The game first warns
	/// that going in without asking his agent may upset them. Null once the talks are open.
	/// </summary>
	private static async Task<int?> OpenRenewal(string wanted)
	{
		if (await OpenPlayer(wanted) is not { } card) return 1;
		var player = card.GetProperty("Player");
		var name = Field(player, "Name");

		if (await Labelled("Enter Contract Negotiations", TalksButton) == null)
		{
			var opened = await Arriving("press", [("text", Field(player, "Value")), ("name", "SIButton"), ("any", "1")]);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => await Labelled("Enter Contract Negotiations", TalksButton) != null, seconds: 8, every: 0.25);
		}
		if (await Labelled("Enter Contract Negotiations", TalksButton) is not { } enter)
		{
			var ours = (await Fm.Pull("Human.Team.Players", "Name", list: true)).EnumerateArray().Any(p => Field(p, "Name") == name);
			Console.Error.WriteLine(ours
				? L($"{name} ile şu an sözleşme görüşmesine girilemiyor: oyun bu seçeneği vermiyor (yakın zamanda iptal edilen bir görüşme olabilir; birkaç gün sonra yeniden dene).")
				: L($"{name} bizim oyuncumuz değil. Başka kulübün oyuncusuyla görüşme, teklifi kabul edilince açılır: offer {name}"));
			return 1;
		}

		await Fm.Get("press", ("ptr", Field(enter, "Pointer")));
		JsonElement dialog = default;
		await Until(async () => await OfferScreen.Read(Fm, "contract") != null || (dialog = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 10, every: 0.25);
		if (dialog.ValueKind != JsonValueKind.Array || dialog.GetArrayLength() == 0) return null;

		var warning = dialog[0];
		// A notice with only "Ok" on it is the game saying no.
		if (!DialogAsks(warning))
		{
			Console.Error.WriteLine(L($"Oyun diyor ki: {Field(warning, "Title")}. {Field(warning, "Description")}"));
			return 1;
		}
		var go = !Ui.Interactive
			|| Ui.Ask(L($"{name}: menajerine sormadan görüşmeye giriliyor; oyuncu ve menajer bundan hoşlanmayabilir."), [L("Yine de gir")], cancel: L("Vazgeç")) != null;
		if (!Ui.Interactive) Console.WriteLine(L("(Oyun uyardı: menajere sormadan görüşmeye girmek oyuncuyu ve menajeri üzebilir.)"));
		await Fm.Get("click", ("ptr", Field(DialogAnswer(warning, go)!.Value, "Pointer")));
		return go ? null : 0;
	}
}
