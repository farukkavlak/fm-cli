using System.Text.Json.Nodes;

namespace FmCli.Nl;

/// <summary>
/// The cues of every tool and the reader that fills its arguments. A cue list is
/// "word; two words=2; ..." — words in this order (not necessarily side by side), default weight
/// one; see <see cref="Text.Matches"/> for how one word matches. Words are written folded (no
/// Turkish letters). "(none)" collects what no tool does, so it can win over a stray cue.
/// </summary>
internal static partial class Router
{
	public sealed record Rule(string Tool, Cue[] Cues, Action<Ctx>? Args);

	public const string NoTool = "(none)";

	private static Rule R(string tool, string cues, Action<Ctx>? args = null) => new(tool,
		[.. cues.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(c =>
		{
			var eq = c.LastIndexOf('=');
			var w = 1.0;
			var weighted = eq > 0 && double.TryParse(c[(eq + 1)..], System.Globalization.CultureInfo.InvariantCulture, out w);
			return new Cue((weighted ? c[..eq] : c).Split(' ', StringSplitOptions.RemoveEmptyEntries), weighted ? w : 1.0);
		})], args);

	/// <summary>Arguments a tool cannot run without; when the sentence leaves one out it is asked.</summary>
	private static readonly Dictionary<string, string[]> Required = new()
	{
		["fm_actions"] = ["person"], ["fm_approach"] = ["player", "move"], ["fm_compare"] = ["a", "b"], ["fm_load"] = ["name", "save"],
		["fm_match"] = ["mode"], ["fm_offer"] = ["name"], ["fm_open"] = ["page"], ["fm_player"] = ["name"], ["fm_press"] = ["label"], ["fm_type"] = ["text"],
		["fm_propose"] = ["player"], ["fm_listing"] = ["player"], ["fm_query"] = ["where"], ["fm_release"] = ["player"], ["fm_reply"] = ["message"],
		["fm_scout_focus"] = ["action"], ["fm_scout_player"] = ["player"], ["fm_search"] = ["text"], ["fm_select"] = ["list", "choice"],
		["fm_setpieces"] = ["kind"], ["fm_shortlist_change"] = ["player"], ["fm_staff_fire"] = ["name"], ["fm_staff_hire"] = ["name"],
		["fm_stop"] = ["save"], ["fm_tactic_pi"] = ["position"], ["fm_tactic_player"] = ["position", "player"], ["fm_tactic_role"] = ["position"],
		["fm_tactic_set"] = ["setting"],
	};

	/// <summary>Every argument the router can ask for by table (the readers ask a few more on their own).</summary>
	internal static IEnumerable<(string Tool, string Arg)> Askable() =>
		Required.SelectMany(r => r.Value.Select(a => (r.Key, a))).Concat(Needs.Select(n => (n.Tool, n.Need)));

	/// <summary>
	/// What an action needs once it is asked for: with the first argument given (and the second
	/// not), the third is asked if the sentence leaves it out. A watch is for a length, a sub
	/// takes one off for another, an application is to a club.
	/// </summary>
	private static readonly (string Tool, string When, string? Unless, string Need)[] Needs =
	[
		("fm_scout_player", "player", "who", "length"), ("fm_shortlist_change", "player", "remove", "length"),
		("fm_sub", "off", null, "on"), ("fm_sub", "on", null, "off"), ("fm_jobs", "action", null, "club"),
	];

	private static JsonArray Arr(IEnumerable<string> items) => new([.. items.Select(i => (JsonNode)i)]);

	private static readonly Dictionary<string, int> CountWords = new()
	{
		["iki"] = 2, ["uc"] = 3, ["dort"] = 4, ["bes"] = 5, ["alti"] = 6, ["yedi"] = 7, ["sekiz"] = 8, ["dokuz"] = 9, ["on"] = 10,
		["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10,
		["twice"] = 2, ["thrice"] = 3,
	};

	/// <summary>
	/// How many times, as said: "3 kere", "x5", "beş gün", "twice", "bi hafta" (7), "2 weeks" (14),
	/// "devam et devam et" (2). English "on" is a word, not ten.
	/// </summary>
	private static int Times(Ctx c)
	{
		var n = 0;
		for (var i = 0; i < c.W.Length && n == 0; i++)
		{
			var w = c.W[i];
			var m = System.Text.RegularExpressions.Regex.Match(w, @"^x?(\d{1,2})x?$");
			if (m.Success) n = int.Parse(m.Groups[1].Value);
			else if (CountWords.TryGetValue(w, out var v) && !(w == "on" && c.Has("go on", "on$ off", "turn on", "on the"))) n = v;
			else if (w is "bi" or "bir" or "a" or "one" && i + 1 < c.W.Length && Text.Matches(c.W[i + 1], "hafta") | Text.Matches(c.W[i + 1], "week")) n = 1;
			if (n > 0 && i + 1 < c.W.Length && (Text.Matches(c.W[i + 1], "hafta") || Text.Matches(c.W[i + 1], "week"))) n *= 7;
		}
		if (n == 0 && c.Has("hafta", "week")) n = 7;
		if (n == 0)
		{
			var again = c.W.Count(w => w is "devam" or "continue" or "ilerle" or "ilerlet");
			if (again >= 2) n = again;
		}
		return n;
	}

	/// <summary>A player named, or else a position: tactic tools take either.</summary>
	private static string? NameOrPos(Ctx c) => c.Name() ?? c.Pos();

	private static readonly Lex Months = new(
		("ocak", "ocak"), ("subat", "subat"), ("mart", "mart"), ("nisan", "nisan"), ("mayis", "mayis"), ("haziran", "haziran"), ("temmuz", "temmuz"),
		("agustos", "agustos"), ("eylul", "eylul"), ("ekim", "ekim"), ("kasim", "kasim"), ("aralik", "aralik"),
		("january", "january"), ("february", "february"), ("march$", "march"), ("april", "april"), ("may$", "may"), ("june", "june"), ("july", "july"),
		("august", "august"), ("september", "september"), ("october", "october"), ("november", "november"), ("december", "december"));

	private static readonly string[] MonthOrder = ["ocak", "subat", "mart", "nisan", "mayis", "haziran", "temmuz", "agustos", "eylul", "ekim", "kasim", "aralik"];

	private static readonly Lex Competitions = new(
		("avrupa ligi", "UEFA Europa League"), ("europa league", "UEFA Europa League"), ("uel$", "UEFA Europa League"),
		("sampiyonlar ligi", "UEFA Champions League"), ("champions league", "UEFA Champions League"), ("ucl$", "UEFA Champions League"),
		("konferans ligi", "UEFA Conference League"), ("conference league", "UEFA Conference League"),
		("la liga", "LALIGA"), ("serie b$", "Serie B"), ("serie a$", "Serie A"), ("premier league", "Premier League"), ("premier lig", "Premier League"),
		("italya kupasi", "Coppa Italia"), ("coppa italia", "Coppa Italia"), ("dunya kupasi", "World Cup"), ("world cup", "World Cup"),
		("super lig", "Super League"), ("bundesliga", "Bundesliga"));

	private static readonly Lex Stages = new(
		("ceyrek final", "Quarter-Final"), ("quarter final", "Quarter-Final"), ("yari final", "Semi-Final"), ("semi final", "Semi-Final"),
		("play off", "Play-Off"), ("playoff", "Play-Off"), ("son 16", "Round of 16"), ("last 16", "Round of 16"));

	private static readonly Lex MatchStats = new(
		("gol", "Goals"), ("top scorer", "Goals"), ("top scorers", "Goals"), ("golden boot", "Goals"), ("asist", "Assists"), ("assists", "Assists"),
		("reyting", "Average Rating"), ("rating", "Average Rating"), ("rated", "Average Rating"), ("goals", "Goals"), ("scorer*", "Goals"), ("scores", "Goals"), ("kurtaris", "Saves"), ("saves$", "Saves"), ("form$", "Form"), ("formda", "Form"),
		("seyirci", "Average Attendance"), ("attendance", "Average Attendance"), ("xg$", "Expected Goals"), ("clean sheet", "Clean Sheets"), ("gol yememe", "Clean Sheets"));

	public static readonly Rule[] Rules =
	[
		R(NoTool, "hava nasil=3; weather=3; saka=3; joke=3; pizza=3; kahve=3; hile=3; referee=3; hakem=3; kariyer baslat=4; new career=4; forma tasarla=4; kit$=3; " +
			"chairman=3; baskani=3; oyuncusunu sakatla=4; bilgisayari=4; naber=3; selam=3; tesekkur=3; sagol=3; hi$=3; thanks=3; brb=3; fm27=3; seni kim=3; " +
			"replay=4; milyar=3; billion=4; who won=3; asdf=3; hmm$=3; bi dk=3; nasil yaparim=3"),

		// ---- time and saves
		R("fm_status", "durum$=2; durumu ne=1; durumday=3; ne bekli=3; tarih$=3; date$=3; where are we=4; nerdeyiz=3; devam edebilir=3; ilerlemiyo=3; tutuyo=3; " +
			"saat kac=3; takildi=3; napiyoz=3; ne yapmam=3; sira kimde=3; status=3; cant continue=4; score$=2; what do i do=3; wat do=3; su an=1; macta durum=3"),
		R("fm_next", "devam$=1.5; devam et=1.5; ilerlet=2; ilerle$=2; gunu gec=3; sonraki gune=3; gec gec=3; continue=1.5; cont$=2; next day=3; advance=2; go on=2; " +
			"skip ahead=3; press continue=3; on kere=2; cevaplamadan=2; move on=1.5; soruyu=1; soru atla=2; mac* kadar=2; mesaj gel*=1.5; until the match=2", c =>
			{
				// "maça kadar devam et", "skip ahead to the match": as far as match day, or a message
				if (c.Has("mac* kadar", "mac gun* kadar", "until the match", "until match*", "till the match", "till match*", "to the match", "to match day", "to matchday", "up to the match"))
					c.Set("until", "match");
				else if (c.Has("mesaj gel*", "mesaj dus*", "until a message", "until the next message", "until new message*", "till a message", "until someone writes"))
					c.Set("until", "message");
				var times = Times(c);
				if (times >= 2 && !c.Args.ContainsKey("until")) c.Set("times", Math.Min(times, 30));
				if (c.Has("soru", "cevaplamadan", "question", "ignore", "post match")) c.Set("skip", true);
			}),
		R("fm_continue", "son kayd=3; son kayit=3; last save=4; resume last=4; ana menu=2"),
		R("fm_save", "kaydet=1.5; kayit al=3; kaydt=1.5; save$=1.5; save the game=3; sav$=2; bi save=3; farkli kaydet=3; ayri kayit=3; separate save=3", c =>
			{
				var at = c.Find("adiyla");
				if (at < 0) at = c.Find("diye");
				if (at > 0) c.Set("as", c.Between(c.Find("kaydet") is var k && k >= 0 && k < at ? k + 1 : 0, at).Replace("farkli", "").Trim());
				var called = c.Find("called");
				if (called >= 0) c.Set("as", c.Between(called + 1, c.W.Length));
			}),
		R("fm_saves", "kayitlarim=4; kayit dosya=4; hangi kayitlar=4; kayitlari listele=4; list saves=4; saves$=3; kaydini sil=4; delete save=4", c =>
			{
				var at = c.Find("kaydini");
				if (at > 0) c.Set("delete", c.W[at - 1]);
			}),
		R("fm_load", "kayit yukle=3; eski kayda=3; load$=2; yukle$=2; kayda don=3; sonra #name ac$=3", c =>
			{
				var at = c.Find("load");
				var next = at >= 0 ? c.W.Skip(at + 1).FirstOrDefault(w => !Ctx.IsStop(w)) : null;
				if (next != null && !next.StartsWith("my") && next is not ("save" or "game" or "file")) c.Set("name", Ctx.Strip(next));
				else if (c.Name() is string n && c.Has("ac$")) c.Set("name", n.TrimEnd('i'));
				if (c.Has("kaydet")) c.Set("save", true);
			}),
		R("fm_stop", "oyunu kapat=3; kapat$=2; fm i kapat=3; kaydedip kapat=4; kaydet cik=3; kaydetmeden=3; kayitsiz cik=4; oyundan cik=3; close the game=4; " +
			"save and quit=4; quit without=4; save n close=4; done for today=3; kapat oyunu=2; cik$=0.5", c =>
			{
				if (c.Has("kaydetmeden", "kayitsiz", "without")) c.Set("save", false);
				else if (c.Has("kaydedip", "kaydet", "save")) c.Set("save", true);
			}),
		R("fm_world", "hangi kayit acik=4; ne zaman kaydettim=4; kac saattir=4; kac saat=3; oyun dunyasi=4; time played=4; otomatik kayit acik=4; kariyer kac saat=4"),
		R("fm_diff", "ne degisti=4; neler degisti=4; what changed=4; show changes=4; farki sifirla=4; beri ne oldu=4; changes=1.5", c =>
			{
				if (c.Has("sifirla", "reset")) c.Set("reset", true);
				if (c.Has("keep")) c.Set("keep", true);
			}),

		// ---- inbox and talks
		R("fm_inbox", "mesajlar=2.5; mesaj$=2; mesajlarimi=2.5; gelen kutu=3; mail=2; okunmamis=3; msj=2; posta=3; inbox=3; messages=2.5; msgs=2.5; unread=3; " +
			"need an answer=2; bekleyen mesaj=2"),
		R("fm_reply", "mesaji=2.5; mesaja=3; mesajina=3; mesajini=3; message #num=3; open message=3; msg #num=3; reply=2; answer the=2; cevap ver=2; #num oku=3; " +
			"#num numaraya=3; ne cevap=3; what can i answer=3; read message=3; teklifi mesaj=2; message about=3; msg say=2", c =>
			{
				var nums = c.Nums();
				var msgWord = new[] { "mesaj", "mail", "email", "eposta", "message", "msg" }.Select(w => c.Find(w)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
				int? message = null, answer = null;
				var toMsg = Math.Max(c.Find("message"), c.Find("msg"));
				if (c.Has("en ustteki", "ilk mesaj", "first msg", "first message")) message = 1;
				if (nums.Count >= 2)
				{
					var afterMsg = nums.FirstOrDefault(n => n.At == toMsg + 1);
					if (afterMsg != default) { message = afterMsg.Value; answer = nums.First(n => n != afterMsg).Value; }
					else { message = nums[0].Value; answer = nums[1].Value; }
				}
				else if (nums.Count == 1)
				{
					if (message == 1 && c.Has("option", "secenek")) answer = nums[0].Value;
					else message = nums[0].Value;
				}
				if (message is int m) c.Set("message", m.ToString());
				else if (msgWord > 0)
				{
					var before = c.W[..msgWord].Where(w => !w.StartsWith("teklif") && w is not ("gelen" or "gelmis" or "son") && !Ctx.IsStop(w)).ToList();
					var about = c.Find("about");
					if (about >= 0) c.Set("message", c.Between(about + 1, c.W.Length).Replace("registration", "").Trim());
					else if (before.Count > 0) c.Set("message", string.Join(' ', before.TakeLast(c.Has("raporu") ? 2 : 1)));
				}
				if (answer is int a) c.Set("answer", a.ToString());
				else if (c.Has("ilk secenek")) c.Set("answer", "1");
				else if (c.Find("diye") is var d && d > 0 && c.Find("cevap") > d) c.Set("answer", c.W[d - 1]);
				else if (c.LexValue(ReplyAnswers) is string verdict) c.Set("answer", verdict);
				else if (c.Find("with") is var with && with > msgWord && msgWord >= 0 && with + 1 < c.W.Length) c.Set("answer", c.Between(with + 1, c.W.Length));
				if (c.Has("gondermeden", "without sending")) c.Set("dry", true);
				if (c.Has("olumlu", "positive")) c.Ask("answer");
			}),
		R("fm_talk", "konusma=1.5; acik konusma=3; gazeteci=3; basin toplanti=3; presser=3; press conference=3; team talk=3; tepki=3; react=3; #num numarayi soyle=4; " +
			"say #num=4; #num sec=2; takima=2; oyunculara=2; de$=1; terk et=3; walk out=3; leave the conference=4; kalk cik=3; bonservis=2; no comment=4; tell them=3; " +
			"tell the lads=4; calm down=3; confirm the fee=4; my options=2; options in the=2; takima bagir=3", c =>
			{
				if (c.Has("terk", "walk out", "leave the", "kalk cik", "ayril", "storm out", "walk away")) c.Set("say", "leave");
				else if (c.Has("bonservis") && c.Money() is string fee && !c.Has("onayla")) c.Set("say", "fee " + fee);
				else if (c.Has("onayla", "confirm")) c.Set("say", "confirm");
				else if (c.Has("no comment")) c.Set("say", "no comment");
				else if (c.LexValue(TalkTones) is string tone) c.Set("say", tone);
				else if (c.Num() is int n) c.Set("say", n.ToString());
				// "takıma çok iyi gidiyorsunuz de": what comes before the closing "de", past whom it is said to
				else if (c.W.Length >= 3 && c.W[^1] is "de" or "diye")
				{
					var from = Array.FindIndex(c.W, w => w is "takima" or "oyunculara" or "muhabire" or "gazeteciye" or "basina" or "medyaya") + 1;
					if (from < c.W.Length - 1) c.Set("say", string.Join(' ', c.W[from..^1]));
				}
			}),
		R("fm_tour", "tanitim=3; rehber=3; turu gec=4; tur mur=4; tour=3; tutorial=3; turunu=3", c =>
			{
				if (c.Has("atla", "gec", "skip")) c.Set("skip", true);
			}),
		R("fm_dialog", "pencere=3; popup=3; dialog=3; confirmation=3; evet de=4; hayir bas=4; tamama bas=3; press ok=3; onay penceresi=4", c =>
			{
				var a = c.LexValue(DialogAnswers);
				if (a != null) c.Set("answer", a);
			}),
		R("fm_news", "haber=3; medya=3; manset=3; dedikodu=3; papers=3; headlines=3; rumours=3; rumors=3; news=3; haberlr=3", c =>
			{
				var first = c.Nums().Select(n => (int?)n.Value).FirstOrDefault();
				if (first is int n) c.Set("item", n.ToString());
				else if (c.Find("hakkindaki") is var at && at > 0) c.Set("item", Ctx.Strip(c.W[at - 1]));
			}),

		// ---- match day
		R("fm_match", "maci oyna=4; mac oyna=4; maci izle=4; maci simule=4; maci gec=4; maci hizli=4; maci birak=4; maca cik=4; maca devam=4; mac basla=4; hadi maca=4; " +
			"maci yardimciya=4; sonucu ver=3; sonucu soyle=3; aninda sonuc=4; izlemeyecem=3; konusmasiz=4; ikinci yari=4; oyna$=1.5; play the match=4; play the game=4; " +
			"kick off=4; sim the=4; simulate=3; give me the result=4; handle the game=4; play no talk=4; resume the match=4; play match=4", c =>
			{
				var talk = c.Has("konusma", "talk");
				var noTalk = c.Has("konusmasiz", "no talk") || (talk && c.Has("yapma", "atla", "gec", "skip", "without"));
				if (noTalk) c.Set("mode", "play_no_talk");
				else if (talk || c.Has("ikinci yari")) c.Set("mode", "play");
				else if (c.Has("hizli", "simule", "sonuc", "aninda", "maci gec", "birak", "izlemeyecem", "izlemeden", "bitir", "sim$", "simulate", "result", "handle", "yardimciya", "score")) c.Set("mode", "instant");
				else if (c.Has("canli", "izle", "anlatim", "live", "watch", "commentary")) c.Set("mode", "play");
			}),
		R("fm_sub", "degisiklik=3; yerine=3; cik gir=3; girsin=2; gir$=1.5; cikar$=1.5; yedek kulubesi=4; bench=3; subs$=3; bring on=4; on off=3; sahada kim=4; " +
			"kondisyonlar macta=4; tired out there=4; yoruldu=2", c =>
			{
				var names = c.Names();
				var instead = c.Find("yerine");
				var bring = c.Find("bring on");
				if (instead > 0 && names.Any(n => n.At < instead) && names.Any(n => n.At > instead)) { c.Set("off", names.Last(n => n.At < instead).Name); c.Set("on", names.First(n => n.At > instead).Name); }
				else if (bring >= 0 && names.Count >= 2) { c.Set("on", names[0].Name); c.Set("off", names[1].Name); }
				else if (names.Count >= 2)
				{
					var offFirst = c.W.Length > names[0].At + names[0].Len && c.W[names[0].At + names[0].Len] is "off" or "cik" or "cikar" or "out";
					var onFirst = c.W.Length > names[0].At + names[0].Len && c.W[names[0].At + names[0].Len] is "on" or "gir" or "girsin" or "in";
					c.Set("off", onFirst && !offFirst ? names[1].Name : names[0].Name);
					c.Set("on", onFirst && !offFirst ? names[0].Name : names[1].Name);
				}
				else if (names.Count == 1 && c.Has("cikar", "cik$", "ciksin", "oyundan", "off$", "yoruldu")) { c.Set("off", names[0].Name); c.Ask("on"); }
				else if (names.Count == 1 && c.Has("gir", "girsin", "oyuna al", "oyuna sok", "bring on", "on$", "come on")) { c.Set("on", names[0].Name); c.Ask("off"); }
				if (c.Has("once ne olacag", "first show", "dry")) c.Set("dry", true);
				if (c.Has("degisiklik yap") && names.Count == 0) { c.Ask("off"); c.Ask("on"); }
			}),
		R("fm_pause", "durdur$=3; maci durdur=5; duraklat=4; mola ver$=2; pause=4; devam ettir=3; maci surdur=5; resume=4; play on=4; tekrari gec=5; tekrari atla=5; skip the replay=5; skip replay=5", c =>
			{
				c.Set("do", c.Has("tekrar", "replay") ? "skip" : c.Has("devam ettir", "surdur", "resume", "play on") ? "resume" : "pause");
			}),
		R("fm_tweak", "ince ayar=5; incayar=5; taktik ince=4; tweak=5; tactical tweak=6; soluklan=4; take a breather=6; lock it down=6; kitchen sink=6; go long=3", c =>
			{
				if (c.Has("sifirla", "reset")) c.Set("reset", c.Has("bireysel", "individual") ? "individual" : c.Has("hepsi", "all") ? "all" : "team");
				if (c.Has("kapat", "kaldir", "turn off", " off$")) c.Set("off", true);
			}),
		R("fm_read", "kulup tarihi=5; kupalarimiz=4; kulup rekor=5; tarihce=3; club history=6; honours=4; past winners=5; gecmis sampiyon=5; turnuva kural=5; competition rules=6; veri merkezi=5; data hub=6; takim analizi=5; gol analizi=5; duran top analizi=5; transfer gecmisi=5; transfer history=6; sezon onizleme=5; ceza alanlar=3; suspensions=4; manager movements=6; transfer soylenti=5; rumours=4", c =>
			{
				foreach (var (cue, key) in new[] { ("kulup tarihi", "club-history"), ("club history", "club-history"), ("kupalarimiz", "club-honours"), ("honours", "club-honours"), ("kulup rekor", "club-records"), ("past winners", "past-winners"), ("gecmis sampiyon", "past-winners"), ("turnuva kural", "comp-rules"), ("competition rules", "comp-rules"), ("veri merkezi", "data-hub"), ("data hub", "data-hub"), ("takim analizi", "team-analysis"), ("gol analizi", "goal-analysis"), ("duran top analizi", "set-piece-analysis"), ("transfer gecmisi", "transfer-history"), ("transfer history", "transfer-history"), ("sezon onizleme", "season-preview"), ("suspensions", "suspensions"), ("manager movements", "manager-movements"), ("transfer soylenti", "rumours"), ("rumours", "rumours") })
					if (c.Has(cue)) { c.Set("page", key); break; }
			}),
		R("fm_transferroom", "transferroom=7; transfer room=7; transfer odasi=6; menajer teklifleri=5; agent offers=5", c => { }),
		R("fm_planner", "squad planner=7; planner=5; kadro plani=6; kadro planlayici=6", c => { }),
		R("fm_meeting", "takim toplantisi=6; toplanti yap=5; team meeting=6; hold a meeting=6", c => { }),
		R("fm_draw", "kura cek=6; kurayi cek=6; kura=3; run the draw=6; cup draw=5; do the draw=6", c => { }),
		R("fm_schedule_week", "antrenman haftasini=5; haftayi kopyala=6; haftayi yapistir=6; haftayi temizle=6; copy the week=6; paste the week=6; clear the week=6", c =>
			{
				c.Set("do", c.Has("kopyala", "copy") ? "copy" : c.Has("yapistir", "paste") ? "paste" : "clear");
			}),
		R("fm_manager", "menajer kariyer=5; menajer istatistik=5; kariyerim=4; kariyer istatistik=5; menajerlik kariyer=5; my career=4; my record=4; manager record=5; manager stats=5; career stats=4", c => { }),
		R("fm_tactic_advice", "taktik onerisi=5; taktik tavsiye=5; yardimci taktik=4; taktik degerlendir=5; tactic advice=6; tactical advice=6; assistant think of the tactic=6; evaluate my tactic=6; evaluate the tactic=6", c =>
			{
				if (c.Has("kadro", "ilk 11", "on bir", "eleven", "xi")) c.Set("what", "xi");
				else if (c.Has("rakip", "opposition", "opponent")) c.Set("what", "opposition");
			}),
		R("fm_holiday", "tatile cik=6; tatil=4; holiday=5; go on holiday=6; yardimci yonetsin=3", c =>
			{
				if (c.Has("hafta", "week")) c.Set("until", "week");
				else if (c.Has("ay", "ay$", "month")) c.Set("until", "month");
				else if (c.Has("mac", "match")) c.Set("until", "match");
				else c.Ask("until");
			}),
		R("fm_shout", "bagir=3; shout=3; cesaretlendir=4; sakinlestir=4; daha fazlasini iste=4; ov$=4; encourage=4; fire them up=4; fire up=4; demand more=4; praise=4; " +
			"no pressure=4; kenardan=3", c =>
			{
				var s = c.LexValue(Shouts);
				if (s != null) c.Set("shout", s);
			}),
		R("fm_dugout", "oneri=3; tavsiye=3; asistan ne=4; dugout=3; assistant suggest=4; suggestion=3; dedigini yap=4; what the assistant says=4; yardimci ne=3; " +
			"yardimcinin #num=3", c =>
			{
				if (c.Has("uygula", "apply", "yap$", "do what")) c.Set("action", "apply");
				else if (c.Has("bosver", "dismiss", "ignore", "gec$")) c.Set("action", "dismiss");
				if (c.Num() is int n) c.Set("number", n);
				else if (c.Args.ContainsKey("action")) c.Ask("number");
			}),
		R("fm_report", "son mac=3; mac raporu=3; gecen mac=3; macin adami=4; xg=2; kac sut=3; kac bitti=4; yenildik=3; mac ozeti=4; last game=3; match report=4; " +
			"who scored=4; potm=4; dunku mac=3; last match=3"),
		R("fm_opponent", "rakip=2; rakib=2; rakp=1.5; karsi takim=3; opp$=3; opponent=3; their top scorer=4; how good are they=4; rakibi analiz=3; analyse the opponent=4"),
		R("fm_opposition", "rakip talimat=5; opposition=3.5; baski yap=2; surekli baski=3; adam adama=3; ayagina zorla=4; always press=4; tight mark=4; onto his=4; " +
			"sert mudahale #name=1; #name sert mudahale=4; rakibin yildizina=3", c =>
			{
				if (c.Has("uygula", "apply")) c.Set("advice", true);
				else if (c.Has("temizle", "sil$", "clear")) c.Set("clear", true);
				else
				{
					if (c.Name() is string p) c.Set("player", p); else c.Ask("player");
					var sets = c.LexAll(OppositionSets).Select(s => s.Value).Distinct().ToList();
					if (sets.Count > 0 && c.Args.ContainsKey("player")) c.Set("set", Arr(sets));
				}
			}),
		// The staff's advice board; a single "tavsiye/öneri" stays with the dugout (the match assistant).
		R("fm_advice", "tavsiyeler=5; tavsiyeleri=5; tavsiyelerin=5; ekibin tavsiye=6; ekip tavsiye=6; staff advice=6; staff recommendation=6; backroom=5; recommendations=5; advice list=6"),
		// "hazırlık maçı" (a friendly) shares its words with "maç hazırlığı" (match prep): told apart by the whole phrase.
		R("fm_friendly", "hazirlik maci=9; ozel mac=6; dostluk maci=6; friendly=6; friendlies=6", c =>
			{
				if (c.Has("deplasman", "away")) c.Set("away", true);
			}),
		R("fm_prep", "mac hazirligi=4; hazirlik odagi=4; topla oyunda odak=4; topsuz odak=4; duran top odagi=4; match prep=4; preparation focus=4", c =>
			{
				if (c.Has("topsuz", "out of possession", "without the ball")) c.Set("slot", "topsuz");
				else if (c.Has("topla", "in possession", "with the ball")) c.Set("slot", "topla");
				else if (c.Has("duran top", "set piece")) c.Set("slot", c.Has("ikinci", "second") ? "duran2" : "duran1");
				var f = c.LexValue(PrepFocus);
				if (f != null) c.Set("focus", f);
			}),

		// ---- fixtures and competitions
		R("fm_fixtures", "fikstur=4; fiksture=4; sonraki mac=3; siradaki mac=3; onumuzdeki mac=3; mac ne zaman=4; mac var mi=3; deplasman=3; sonraki maclar=3; mac programi=4; " +
			"tum maclari=4; butun sezonun=4; fixtures=4; next game=3; next match=3; who do we play=4; upcoming=3; season schedule=4; whens the next=3", c =>
			{
				if (c.Has("butun", "tum", "all", "full season", "sezonun")) c.Set("all", true);
			}),
		R("fm_calendar", "takvim=4; transfer donemi=4; milli ara=4; gelecek ay=3; bu ay=2; calendar=4; transfer window=4; international break=4; whats on in=3; " +
			"neler var=1", c =>
			{
				var m = c.Lex(Months);
				if (m is { } month)
				{
					var year = c.Nums().FirstOrDefault(n => n.Value >= 2000).Value;
					var i = Array.IndexOf(MonthOrder, month.Value);
					c.Set("month", year > 0 && i >= 0 ? $"{year}-{i + 1:00}" : month.Value);
				}
				else if (c.Has("gelecek ay*", "onumuzdeki ay*", "sonraki ay*", "next month", "coming month")) c.Ask("month");
				if (c.Has("genc", "youth")) c.Set("youth", true);
				if (!c.Args.ContainsKey("month") && m is null && c.Has("ekimde", "eylulde")) { }
			}),
		R("fm_table", "puan durum=4; puan durmu=4; lig tablo=4; kacinci=4; puan tablo=4; tablo$=3; lider kim=3; kume dus=4; avrupaya kac=4; siralama=3; table=3; " +
			"standings=4; whos top=4; grubunda durum=3", c =>
			{
				var comp = c.LexValue(Competitions);
				if (comp != null) c.Set("competition", comp);
			}),
		R("fm_stages", "la liga=4; serie b=4; premier league=4; coppa italia=4; italya kupasi=4; dunya kupasi=4; world cup=4; kupada=2.5; esles=3; turday=3; " +
			"ceyrek final=3.5; yari final=3.5; semi final=3.5; quarter final=3.5; play off=3; ties$=3; all rounds=4; grup=1; lider$=1; looking=1", c =>
			{
				if (c.Has("hangi turnuva", "turnuvalar", "which competitions", "what competitions", "list the competitions")) { c.Set("list", true); return; }
				var comp = c.LexValue(Competitions);
				if (comp != null) c.Set("competition", comp); else c.Ask("competition");
				var stage = c.LexValue(Stages);
				if (stage != null) c.Set("stage", stage);
				if (c.Has("all rounds", "tum turlar")) c.Set("all", true);
			}),
		R("fm_league", "gol kral=5; asist kral=5; ligin=2; reytingli=2; kurtaris=3; lig istatistik=5; en formda takim=5; en yuksek takim=4; hangi istatistik=5; " +
			"top scorers=5; golden boot=5; league stats=5; stats can i rank=5; rank by=4; xg leaders=5; leaders=2; seyirci ortalamasi en=3", c =>
			{
				if (c.Has("hangi istatistik", "which stats")) { c.Set("stats", true); return; }
				var s = c.LexValue(MatchStats);
				if (s != null) c.Set("stat", s);
				if (s is "Form" or "Average Attendance") c.Set("teams", true);
				var top = c.Find("ilk");
				if (top < 0) top = c.Find("top");
				var n = c.Nums().FirstOrDefault(x => x.At == top + 1);
				if (top >= 0 && n != default) c.Set("top", n.Value);
				else if (c.Has("en cok", "en iyi", "most", "best") && c.Nums().FirstOrDefault(x => x.Value is >= 3 and <= 50) is { At: > 0 } m) c.Set("top", m.Value);
				if (c.Has("takimlar", "takimi", "teams", "team$", "hangi takim")) c.Set("teams", true);
				if (c.LexValue(Competitions) is string comp) c.Set("competition", comp);
				else if (c.Has("kupa", "cup")) c.Ask("competition");
			}),

		// ---- squad and players
		R("fm_squad", "kadro=2; squad=2; sqaud=2; oyuncularim=3; takimi goster=3; a takim=3; kimin maasi=3; degerleri=3; kondisyon=1.5; mac formu=2; yorgun=1; " +
			"moral=1.5; kac gol=3; istatistik=1.5; golcusu=1.5; kac mac oyna=3; reytingler=3; kart cezasi=3; altyapi takim=4; gorunum=2; oynama sure=4; " +
			"fitness=2; sharpness=2; morale=2; most goals=3; youth squads=4; contract view=4; show me the squad=4", c =>
			{
				var v = c.LexValue(SquadViews);
				if (v != null) c.Set("view", v);
			}),
		R("fm_player", "#name=1.2; nasil oyuncu=3; ozellik=3; kac yasinda=4; hangi mevkilerde=3; hizi kac=3; sakatlik gecmisi=4; kisilig=4; profil=3; kac para=3; " +
			"kariyer=2.5; gozlemci raporu=3; rapor ne diyo=3; ne diyo=1; tell me about=4; how old=4; attributes=3; profile=3; career history=4; scout report on=4; " +
			"what do the scouts think=4; show #name=2; #name bak$=1.5; ne alemde=1; sozlesmesi ne zaman=2; bizim kaleci=3", c =>
			{
				if (c.Name() is string n) c.Set("name", n);
				if (c.Has("kariyer", "career", "hangi takimlarda", "nerelerde oynadi", "eski takim", "played for", "former clubs", "history")) c.Set("career", true);
				if (c.Has("rapor", "report", "scoutlar", "scouts think")) c.Set("report", true);
			}),
		R("fm_compare", "karsilastir=4; compare=4; #name mu #name mi=3; daha iyi=1.5; yan yana=4; side by side=4; whos better=4", c =>
			{
				if (c.Name(0) is string a) c.Set("a", a);
				if (c.Name(1) is string b) c.Set("b", b);
			}),
		R("fm_query", "yas alti=2; yasindan kucuk=4; potansiyeli=2; altinda olan=3; ustu olan=3; olanlar=1.5; fazla olan=3; italyan=3; boyu=3; sirala=3; asisti olan=4; " +
			"en ucuz=3; stoperlerimiz=4; hic maca cikmamis=4; yedekte kalan=4; players under=4; below #num=3; earns more than=4; sorted by=3; taller than=4; " +
			"zero apps=4; our centre backs=4; italian players=4; formu dusuk=2; sakat olanlar=2; kalanlar=1; az kalan=2", Query),
		R("fm_depth", "hangi mevkide kac=5; kanatlarda yedek=4; mevki mevki=4; pozisyonlara gore=4; squad depth=5; who can play=4; per position=4; positions are thin=4; depth$=3"),
		R("fm_health", "sakatlik=2.5; sakatl=1.5; sakatlr=2; ne zaman doner=4; doner=1; yorgun olanlar=2.5; mac eksigi=3; sakatlanma riski=4; revir=4; injured=3; injury=3; " +
			"dinlendirmeli=4; should i rest=4"),
		R("fm_doctor", "sorun=3; genel kontrol=4; cezali=3; kimler yok=4; eksiklerimiz=4; kullanamam=4; doktor=4; tek bakista=4; whats wrong=4; suspended=4"),
		R("fm_progress", "gelisiyo=3; gelisim=3; gelisi=1.5; geriliyor=3; improving=4; declining=4; progress=3; gelisiyor=3"),
		R("fm_happiness", "mutsuz=3.5; mutlu$=3; mutlu mu=3; mutluluk=4; derdi ne=4; surat=4; sikayet=4; gitmek isti=4; ilgilenen kulup=4; morali dusuk=4; unhappy=4; " +
			"happiness=4; problem=3; wants to leave=4", c =>
			{
				if (c.Name() is string p) c.Set("player", p);
				else if (c.Has("neden", "why", "nedenleri", "nedenini")) c.Set("why", true);
			}),
		R("fm_dynamics", "soyunma odasi=4; hiyerarsi=4; hierarchy=4; destekli=3; dinamik=4; dressing room=4; dynamics=4; team leaders=4"),
		R("fm_internationals", "milli takim=4; milli mac=4; milliler=4; internationals=4; for their country=4; caps$=4"),
		R("fm_contracts", "sozlesmeler=4; kontrati bit=4; sozlesmesi bit=3; contracts=4; expiring=3; leave on a free=4; bitecekler=4"),
		R("fm_captains", "kaptan=3; captain=3; kaptanlig=3", c =>
			{
				var vice = new[] { "ikinci", "yardimci", "yardimcisi", "vice" };
				foreach (var (name, at, len) in c.Names())
				{
					var near = (at > 0 ? c.W[at - 1] : "") + " " + (at + len < c.W.Length ? c.W[at + len] : "");
					var isVice = vice.Any(v => near.Split(' ').Any(w => Text.Matches(w, v)));
					var key = isVice ? "vice" : "captain";
					if (!c.Args.ContainsKey(key)) c.Set(key, name);
				}
				if (c.Names().Count == 1 && c.Args.ContainsKey("captain") && c.Has("vice", "yardimci kaptan", "ikinci kaptan")) { c.Args.Remove("captain"); c.Set("vice", c.Names()[0].Name); }
				if (c.Names().Count == 0 && c.Has("degistir", "baskasi", "yeni kaptan", "change", "new captain", "someone else")) c.Ask("captain");
			}),

		// ---- tactics
		R("fm_tactic", "taktik$=2.5; taktigi goster=4; ilk 11=2; ilk on bir=4; kim oynuyo=4; dizilis ne=4; hangi formasyon=4; kimin rolu=4; mentalite ne=4; yedekler kim=4; " +
			"starting xi=4; what formation=4; whos starting=4; familiarity=4; taktigi$=1.5; tactic$=2"),
		R("fm_tactic_set", "mentalite=3; hucumcu=4; otobus=4; kapan=3; skoru koru=4; defansif=2.5; dengeli oyna=4; pozitif=3; oyun stili=4; oyun tarzi=4; stili=3; " +
			"kontra atak=4; tiki taka=4; gol lazim=3; saldir=3; go attacking=4; balanced$=4; all out attack=4; see out the game=4; style to=4; counter attack=4", c =>
			{
				var style = c.Lex(Styles);
				var mentality = c.Lex(Mentalities);
				var styleAsked = c.Has("stil", "tarz", "style");
				if (style is { } s && !(mentality is { } m0 && m0.Len > s.Len)) { c.Set("setting", "style"); c.Set("value", s.Value); }
				else if (mentality is { } m) { c.Set("setting", "mentality"); c.Set("value", m.Value); }
				else c.Set("setting", styleAsked ? "style" : "mentality");
			}),
		R("fm_tactic_instructions", "takim talimat=4; team instructions=5; topsuz talimat=4; savunma cizgi=4; daha cok pres=4; pres yap=2; sert mudahale=3; ofsayt=4; tempo=4; " +
			"zaman gecir=4; kontra pres=4; kanatlardan oyna=4; pres hatti=4; kaleci topu=4; press more=4; get stuck in=4; line of engagement=4", c =>
			{
				if (c.Lex(TeamInstructions) is { } ti)
				{
					var parts = ti.Value.Split('=');
					c.Set("name", parts[0]);
					if (parts.Length > 1 && parts[1].Length > 0 && !c.Has("secenek", "choices", "options")) c.Set("value", Steer(c, parts[1]));
				}
				if (c.Has("topsuz talimat", "without the ball")) c.Set("without_ball", true);
			}),
		R("fm_tactic_pi", "oyuncu talimat=4; daha cok sut=4; sut atsin=3; bindirme=4; calim=4; prese ciksin=4; tutsun=3; sut secenek=4; shoot more=4; dribble=4; " +
			"fewer risks=4; man mark=4; ileri ciksin=4", c =>
			{
				if (NameOrPos(c) is string p) c.Set("position", p);
				if (c.Lex(PlayerInstructions) is { } pi)
				{
					var parts = pi.Value.Split('=');
					c.Set("name", parts[0]);
					if (parts.Length > 1 && parts[1].Length > 0 && !c.Has("secenek")) c.Set("value", Steer(c, parts[1]));
				}
			}),
		R("fm_tactic_pick", "sen sec=4; hizli secim=4; otomatik diz=4; auto pick=5; best xi=4; choose the lineup=4; otomatik sec=4"),
		R("fm_tactic_player", "#pos #name oynasin=1; oynasin=2.5; gecsin=1.5; baslasin=2.5; ilk 11e girsin=4; #pos #name=2.2; #name #pos=2.2; yedeklerde=3; swap sub=4; slot$=1; " +
			"in the #pos=1; koy$=1.5; gelsin=1.5", c =>
			{
				var names = c.Names();
				if (c.Has("yedeklerde", "swap sub") && names.Count >= 2) { c.Set("position", names[0].Name); c.Set("player", names[1].Name); }
				else
				{
					if (c.Pos() is string p) c.Set("position", p);
					if (c.Name() is string n) c.Set("player", n);
				}
				if (c.Has("once goster", "first show", "dry")) c.Set("dry", true);
			}),
		R("fm_tactic_role", "rolu=3; rol$=2; rolleri=3; role$=3; hedef adam=3; pres forvet=4; derin oyun kurucu=4; ice kat eden=4; golge forvet=4; target forward=4; " +
			"deep lying=4; sweeper keeper=4; inside forward=4; #pos wing back=3; #pos kanat bek=3; playmaker=3", c =>
			{
				var role = c.Lex(Roles);
				var pos = c.Positions().Where(p => role is not { } r || p.At < r.At || p.At >= r.At + r.Len).Select(p => p.Value).FirstOrDefault();
				if (c.Name() is string n && pos == null) c.Set("position", n);
				else if (pos != null) c.Set("position", pos);
				if (role is { } rr) c.Set("role", rr.Value);
				if (c.Has("topsuz", "out of possession", "without the ball")) c.Set("without_ball", true);
			}),
		// "kişisel rol" would go to the role rule by its "rol"; the personalised-role tool is told by the whole phrase.
		R("fm_tactic_personal", "kisisel rol=6; kisisel rolu=6; kisisel rolleri=6; kisisellestir=6; kisisellestirilmis=6; ozel rol=5; " +
			"personalised role=6; personalized role=6; personalise=6; personalize=6; personalised roles=6", c =>
			{
				var pos = c.Positions().Select(p => p.Value).FirstOrDefault();
				if (pos != null) c.Set("position", pos);
				else if (c.Name() is string n) c.Set("position", n);
				if (c.Args.ContainsKey("position")) c.Set("on", !c.Has("kapat", "kaldir", "off", "remove", "turn off"));
			}),
		R("fm_tactic_new", "yeni taktik=4; dizilisi degistir=4; formasyon degistir=4; #formation=2.5; hangi dizilisler=4; new tactic=4; change formation=4; back three=2", c =>
			{
				var f = c.Formations();
				if (f.Count > 0) c.Set("in_possession", f[0].Value);
				if (f.Count > 1) c.Set("out_of_possession", f[1].Value);
				if (c.LexValue(Styles) is string s && c.Has("gegenpress", "create")) c.Set("style", s);
			}),
		R("fm_tactic_slot", "taktik slot=4; hangi taktik aktif=5; kac taktigim=4; taktige gec=4; taktigi aktif=4; taktige don=4; taktik #num=3; tactic slots=5; " +
			"switch to tactic=5; which tactic is active=5; back to slot=5", c =>
			{
				if (c.Num() is int n) c.Set("slot", n.ToString());
				else if (c.Has("yedek taktig", "diger taktig", "obur taktig", "other tactic", "another tactic", "backup tactic")) c.Ask("slot");
			}),
		R("fm_tactic_clear", "slotu temizle=5; bosalt=4; delete the #num tactic=5; delete the third=5; remove this tactic=5; temizle=1", c =>
			{
				if (c.Num() is int n) c.Set("slot", n.ToString());
			}),
		R("fm_tactic_copy", "kopya=5; cogalt=5; copy=5", c =>
			{
				if (c.Formations().FirstOrDefault().Value is string f) c.Set("tactic", f);
				else if (c.Find("taktiginin") is var at && at > 0) c.Set("tactic", c.W[at - 1]);
			}),
		R("fm_tactic_file", "kayitli taktik=5; dosyadaki taktik=5; taktigi kaydet=4; taktigi #name diye kaydet=2; ustune yaz=5; saved tactics=5; save this tactic=5; " +
			"tactic file=5; overwrite=5; taktik diye kaydet=2; diye kaydet=1", c =>
			{
				var diye = c.Find("diye");
				var adiyla = c.Find("adiyla");
				var asAt = c.Find("as$");
				var ustune = c.Find("ustune");
				if (ustune > 0) { c.Set("save", Ctx.Strip(c.W[ustune - 1]).TrimEnd('u', 'n')); c.Set("overwrite", true); }
				else if (c.Has("overwrite")) { c.Set("save", c.W[c.Find("overwrite") + 1]); c.Set("overwrite", true); }
				else if (diye > 0) c.Set("save", c.Between(c.Find("taktig") + 1, diye));
				else if (adiyla > 0) c.Set("save", c.Between(c.Find("kaydet") + 1, adiyla));
				else if (asAt >= 0 && asAt + 1 < c.W.Length) c.Set("save", c.W[asAt + 1]);
				else if (c.Find("load") is var l && l >= 0) c.Set("load", c.W.Skip(l + 1).First(w => w != "the"));
				else if (c.Has("yukle", "ac$", "kullan") && c.Find("taktig") is var t && t > 0) c.Set("load", c.Between(0, t).Replace("kayitli", "").Trim());
				else if (c.Find("delete") is var d && d >= 0) c.Set("delete", c.W[^1]);
				else if (c.Has("kaydet", "save")) c.Ask("save");
				if (c.Find("not olarak") is var n && n >= 0) c.Set("note", c.Between(n + 2, c.W.Length));
			}),

		// ---- set pieces
		R("fm_takers", "kim kullan=3; kim ati=3; kim atar=3; kornerci=4; frikikci=4; takers=4; who takes=4; duran toplari kim=4; kullansin=2.5; ilk siraya=3; listesine=1; " +
			"first$=1; add #name to=2; on right corners=2; on left corners=2", c =>
			{
				if (c.LexValue(SetPieceKinds) is string k) c.Set("kind", k);
				if (c.Has("sag", "right")) c.Set("side", "right");
				else if (c.Has("sol", "left")) c.Set("side", "left");
				if (c.Name() is string p)
				{
					c.Set("player", p);
					if (c.Has("ekle", "add")) c.Set("action", "add");
					else if (c.Has("cikar", "remove")) c.Set("action", "remove");
					else { c.Set("action", "move"); c.Set("place", 1); }
					if (!c.Args.ContainsKey("side")) c.Ask("side");
				}
			}),
		R("fm_penalties", "penalti=4; pens$=4; penalty=4", c =>
			{
				var rule = c.Find("kural");
				if (rule < 0) rule = c.Find("rule");
				if (rule >= 0)
				{
					if (c.Num() is int n) c.Set("rule", n);
					c.Set("on", !c.Has("kapat", "off"));
					return;
				}
				if (c.Name() is string p)
				{
					c.Set("player", p);
					if (c.LexValue(PenaltyMoves) is string act)
					{
						c.Set("action", act);
						if (act == "add" && c.Num() is int at) c.Set("place", at);
					}
					else { c.Set("action", "move"); c.Set("place", c.Num() ?? 1); }
				}
			}),
		R("fm_setpieces", "korner taktigi=4; rutin=4; routine=4; disa donuk=4; inswing=4; outswing=4; kornerlerde=2; hedefle=3; aim$=3; gorevi=3; mark the keeper=4; " +
			"kaleciyi rahatsiz=4; uzak frikikte=3; kornerleri degistir=4; near post=3; far post=3; kornerde=2; frikikte=2", c =>
			{
				if (c.LexValue(SetPieceKinds) is string k) c.Set("kind", k);
				if (c.Has("disa", "outswing")) c.Set("delivery", "out");
				else if (c.Has("ice donuk", "inswing")) c.Set("delivery", "in");
				if (c.LexValue(SetPieceAims) is string a) c.Set("aim", a);
				var slot = c.W.FirstOrDefault(w => System.Text.RegularExpressions.Regex.IsMatch(w, "^[a-d][1-9]$"));
				if (slot != null) c.Set("slot", slot.ToUpperInvariant());
				if (c.LexValue(SetPieceTasks) is string t) c.Set("task", t);
			}),
		R("fm_setpieces_new", "sihirbaz=5; wizard=5; bastan kur=4", c =>
			{
				var nums = c.Nums();
				if (c.Has("answers") && nums.Count > 0) c.Set("answers", new JsonArray([.. nums.Select(n => (JsonNode)n.Value)]));
			}),

		// ---- training
		R("fm_training", "bireysel=4; antrenman odak=3; kim neye calisiyo=4; calissin=3; individual training=5; train$=3; focus on=3; antrenman raporu=3; ogrenebilir=4; " +
			"ogrendigi=4; hareket=2; ppm=4; teach=4; unlearn=4; trait=4; training report on=4; antrenman yapsin=4; calisiyor=2", Training),
		R("fm_intensity", "yogunluk=4; dinlensin=4; dinlendir=2.5; tatil=4; rest #name=3; rest$=2; holiday=4; training intensity=5; yukunu=3; otomatige=2; fitnes hoca=3; " +
			"cift yogunluk=1; yarim yogunluk=1; normal antrenman=3", c =>
			{
				if (c.Has("otomatig", "ayarlasin", "auto", "yorgun olanlari")) { c.Set("auto", true); return; }
				if (c.Name() is string p) c.Set("player", p);
				var week = c.Has("hafta", "week");
				var days = c.Nums().Select(n => (int?)n.Value).FirstOrDefault();
				if (c.Has("cift", "agir", "double", "heavy")) c.Set("level", "double");
				else if (c.Has("yarim", "hafif", "half", "light")) c.Set("level", "half");
				else if (c.Has("normal")) c.Set("level", "normal");
				else if (c.Has("tatil", "holiday")) c.Set("level", $"holiday {days ?? 1}");
				else if (c.Has("dinlen", "rest")) c.Set("level", week ? $"rest {7 * (days ?? 1)}" : $"rest {days ?? 3}");
			}),
		R("fm_coaches", "antrenorler=4; antrenor gorev=4; koc atama=4; antrenor atama=4; koclari=3; kalecilere baksin=4; fiziksel antrenman=3; is yuku=4; coaches$=4; " +
			"coaching assignments=5; put #name on=3; assign coaches=5; baksin=1.5", c =>
			{
				if (c.Has("yardimci yapsin", "otomatik", "assistant assign", "auto")) { c.Set("auto", true); return; }
				if (c.Name() is string n) c.Set("coach", n);
				if (c.LexValue(CoachAreas) is string a) c.Set("area", a);
				if (c.Has("ciksin", "cikar", "birak", "take off", "remove", "stop")) c.Set("off", true);
			}),
		R("fm_schedule", "bu haftanin antrenman=4; haftalik program=4; seans=4; session=4; sezon oncesi program=4; antrenman stili=4; program tur=4; schedule types=5; " +
			"this weeks training=5; mac oncesi gun=3", c =>
			{
				if (c.LexValue(ScheduleStyles) is string st) { c.Set("style", st); return; }
				if (c.Has("program tur", "schedule types")) { c.Set("style", "?"); return; }
				var nums = c.Nums();
				var month = c.Lex(Months);
				var day = nums.FirstOrDefault(n => n.Value is >= 1 and <= 31 && OrdinalWordAt(c, n.At) == null && !(c.W[n.At].Length > 0 && c.W.Length > n.At + 1 && Text.Matches(c.W[n.At + 1], "seans")));
				var slot = nums.FirstOrDefault(n => n != day);
				if (day != default) c.Set("day", month is { } m && m.At == day.At + 1 ? $"{day.Value} {m.Value}" : day.Value.ToString());
				if (slot != default) c.Set("slot", slot.Value);
				if (c.LexValue(Sessions) is string s) c.Set("session", s);
				if (!c.Args.ContainsKey("day") && c.Has("mac oncesi gun")) c.Ask("day");
			}),
		R("fm_mentoring", "mentor=4; abilik=4; grup kur=4; grubu=1.5; mentoring=4; group #name=4; grubunu boz=4", c =>
			{
				if (c.Has("yardimci kursun", "otomatik", "auto")) { c.Set("auto", true); return; }
				if (c.Has("olabil", "could be", "can mentor", "who can")) { c.Set("candidates", true); return; }
				var remove = c.Find("grubunu");
				if (remove < 0) remove = c.Find("group");
				if (remove >= 0 && c.Has("dagit", "boz", "kaldir", "sil", "remove", "break up", "delete")) { c.Set("remove", c.Num()?.ToString() ?? c.W[Math.Max(0, remove - 1)]); return; }
				var named = c.Find("adi");
				var players = c.NameWords().Where(n => named < 0 || c.Find(n) < named).ToList();
				if (players.Count >= 2) c.Set("players", Arr(players));
				if (named >= 0 && named + 1 < c.W.Length) c.Set("name", c.W[named + 1]);
			}),
		R("fm_youth", "altyapi=2.5; gencler=2; u19=4; youth academy=5; youth intake=5; intake=4; wonderkid=4; gencler ne zaman=3", c =>
			{
				if (c.Has("antrenman", "training")) c.Set("training", true);
				else if (c.Has("geldi mi", "alimin", "wonderkid", "kim var", "aday", "gelecek mi", "kimler geliyor", "prospects", "candidates", "who is coming")) c.Set("candidates", true);
			}),

		// ---- buying and scouting
		R("fm_players", "bonservissiz=5; serbest oyuncu=5; satis listesi=5; transfer listesinde=5; kiralik listesi=5; kiralanabilecek=5; diger takimlarda=4; diger kulup=4; " +
			"kulubu olmayan=5; yas arasi=3; #money kadar #pos=3; gozlemci notu=4; filtreyi kaldir=5; oyuncu arama=4; sozlesme sutun=5; kafa topu=3; players with=3; " +
			"months left=4; young strikers=4; grade players=4; player search=5; quick filters=5; with pace=3; ucuz=2", Players),
		R("fm_targets", "hangi mevkiye transfer=5; takviye=4; hedefleri=2.5; butceme uygun=4; lazim=2; transfer targets=5; where do we need=5; within budget=4; up to=1.5; " +
			"strengthen=4; young right winger=3; #pos lazim=1", c =>
			{
				if (c.Pos() is string p) c.Set("position", p);
				if (c.Money() is string m) c.Set("max_value", m);
				if (c.AgeRange() is string a) c.Set("age", a);
			}),
		R("fm_search", "ara$=3; #name bul$=2.5; kulubunu ara=4; diye kim var=4; nerde calisiyo=4; diye biri=4; search for=4; find #name=2.5; look up=4; #name diye=1", c =>
			{
				var diye = c.Find("diye");
				if (diye > 0) c.Set("text", Ctx.Strip(c.W[diye - 1]));
				else if (c.Name() is string n) c.Set("text", n);
			}),
		R("fm_shortlist", "kisa liste=3; izledigim oyuncu=5; tracking=4; shortlist$=3"),
		R("fm_shortlist_change", "kisa listeye ekle=5; takip et=4; takibe al=5; kisa listeden cikar=5; shortlist #name=4; listeye ekle=3; listeden cikar=2", c =>
			{
				if (c.Name() is string p) c.Set("player", p);
				if (c.Has("cikar", "kaldir", "sil", "remove", "off the", "drop")) c.Set("remove", true);
				else if (c.Has("suresiz", "indefinitely")) c.Set("length", "indefinitely");
				else if (Length(c) is string l) c.Set("length", l);
			}),
		R("fm_offer", "teklif=1.5; teklif ver=3; teklif yap=2.5; teklif hazirla=4; at$=1; bid$=3; make an offer=4; loan offer=4; throw=2; #money=1.5; teklif at=3; " +
			"teklifini #money=2; raise the=2; kiralamak icin teklif=3", c =>
			{
				if (c.Name() is string n) c.Set("name", n);
				if (c.Money() is string fee) c.Set("fee", fee);
				if (c.Has("kirala", "loan")) c.Set("loan", true);
				var terms = new List<string>();
				var sellOn = c.W.Select((w, i) => (w, i)).FirstOrDefault(x => x.w.EndsWith('%') || (x.i > 0 && c.W[x.i - 1] == "yuzde")).w;
				if (sellOn != null) terms.Add($"Percentage Of Next Sale={sellOn.TrimEnd('%')}%");
				if (c.Has("sezon sonunda", "end of season")) terms.Add("Transfer Date=End Of Season");
				var games = c.Find("mac sonra");
				if (games > 0 && c.Money(1) is string bonus) terms.Add($"After League Appearances={c.W[games - 1]} games,{bonus}");
				if (terms.Count > 0) c.Set("terms", Arr(terms));
			}),
		R("fm_offers", "teklifler=3; bekleyen=2; teklif gelmis=4; tekliflr=3; kim kime teklif=5; teklifin detayi=4; teklifini goster=4; kabul et=3; kabl et=3; reddet=3; " +
			"hayir$=1.5; bekletelim=4; view offer=5; accept the=4; stall=5; turn down=5; gelen teklif=4", c =>
			{
				string? answer = c.Has("kabul", "kabl", "accept") ? "accept" : c.Has("reddet", "hayir", "turn down", "reject") ? "reject"
					: c.Has("beklet", "stall") ? "stall" : c.Has("detay", "goster", "view", "show") ? "view" : null;
				if (answer == null) return;
				if (c.Has("hepsi", "all")) { c.Ask("offer"); return; }
				var offer = c.Num()?.ToString() ?? c.Name();
				if (offer != null) { c.Set("offer", offer); c.Set("answer", answer); }
				else c.Ask("offer");
			}),
		R("fm_approach", "ilgimizi belirt=5; ilgi=2; kac para isti=5; denemeye al=5; ana hedef=5; transfer ilgisi=5; declare interest=4; enquire=5; trial=5; top target=5", c =>
			{
				if (c.Name() is string p) c.Set("player", p);
				if (c.Has("dene", "trial")) { c.Set("move", "trial"); if (c.Num() is int w) c.Set("weeks", w.ToString()); }
				else if (c.Has("kac para", "fiyat", "ne ister", "enquire", "price", "ask about", "what it")) c.Set("move", "enquire");
				else c.Set("move", "interest");
				if (c.Has("ana hedef", "top target")) c.Set("as", "top");
				else if (c.Has("transfer ilgisi")) c.Set("as", "transfer");
			}),
		R("fm_scout", "gozlemciler ne=4; scout odak=4; izleme gorev=5; kimi buldu=5; ne buldu=5; odaginda=3; from focus=5; near matches=5; in progress on focus=5", c =>
			{
				if (c.Has("neredeyse", "near")) c.Set("show", "near");
				else if (c.Has("in progress", "devam eden")) c.Set("show", "progress");
				if (c.Num() is int n) c.Set("focus", n.ToString());
				else if (c.Find("odaginda") is var at && at > 0) c.Set("focus", c.Between(0, at));
				else if (c.Find("focus") is var f && f > 0 && c.Find("the") is var t && t >= 0 && t < f) c.Set("focus", c.Between(t + 1, f));
			}),
		R("fm_scout_focus", "gozlem odagi=5; arat=4; taratsin=4; odaga=4; odaginin=4; odagini iptal=5; hangi bolgeler=5; bolgeler=4; gozlemcilerim=5; scout kadrom=5; " +
			"yeni odak=5; new scouting focus=5; to focus=4; stop focus=5; restart focus=5; how good are my scouts=5; scouting areas=5; kaleci ara=3", ScoutFocus),
		R("fm_scout_player", "izlet=5; izlesin=4; gozlemci gonder=5; send a scout=5; scouting report on=4; watch #name=3; which scouts can i send=5", c =>
			{
				var names = c.Names();
				if (c.Has("which scouts")) { c.Set("player", names.LastOrDefault().Name); c.Set("who", true); return; }
				if (names.Count >= 2 && !c.Has("gozlemle", "izlet")) { c.Set("by", names[0].Name); c.Set("player", names[1].Name); }
				else if (names.Count >= 2) c.Set("player", names[0].Name);
				else if (names.Count == 1) c.Set("player", names[0].Name);
				if (c.Has("full scouting", "extensive", "detayli", "kapsamli", "full report")) c.Set("length", "extensive");
				else if (Length(c) is string l) c.Set("length", l);
				else c.Ask("length");
			}),

		// ---- selling and contracts
		R("fm_propose", "satisa cikar=5; kuluplere oner=5; kiraya gonder=5; gitsin=2.5; pazarla=5; sat$=3; to clubs=4; loan out=5; send #name on loan=5; get rid of=5", c =>
			{
				if (c.Name() is string p) c.Set("player", p);
				if (c.Money() is string f) c.Set("fee", f);
				if (c.Has("kira", "loan")) c.Set("loan", true);
			}),
		R("fm_listing", "satis listesine=5; satis listesinden=5; transfer listesine=5; transfer listesinden=5; satis listesi=3; transfer list=5; transfer listed=5; " +
			"on the transfer list=5; off the transfer list=5; list him for sale=4; take him off the list=3", c =>
			{
				if (c.Name() is string p) c.Set("player", p);
				if (c.Has("listesinden", "off the", "cikar", "kaldir", "remove", "take off", "take him off", "unlist")) c.Set("off", true);
			}),
		R("fm_release", "fesh=5; serbest birak=5; karsilikli anlas=3; yollarimizi=2; bedavaya gonder=5; kovalim=3; release$=5; terminate=5; mutual consent=2", c =>
			{
				if (c.Name() is string p) c.Set("player", p);
				if (c.Has("karsilikli", "mutual")) c.Set("mutual", true);
				else if (c.Has("serbest", "bedava", "release")) c.Set("free", true);
			}),
		R("fm_contract", "sozlesme gorusme=5; sozlesmesini uzat=5; uzat=3; maasi #money=3; haftalik #money=4; yil yap=4; primi=4; maddelere gec=5; menajere sun=5; " +
			"gorusmeyi iptal=5; gorusmesinden cik=5; imzala=2; menajer ucret=5; serbest kalma madde=5; contract talks=5; renew=5; appearance fee=5; " +
			"move on to the terms=5; sign it=5; walk away=5; release clause=5; maas #money=3; kontratini uzat=5", Contract),
		R("fm_register", "kadro kaydi=5; kadro listesi=4; lige bildir=5; kadro listesini gonder=5; listeden cikar=3; kayit kural=5; avrupa kadrosu=5; kadro belirlenecek=5; " +
			"registration=5; unregister=5; register=4; submit=4; homegrown=5", c =>
			{
				if (c.Has("hallet", "otomatik", "auto")) c.Set("auto", true);
				if (c.Has("bildir", "gonder", "submit", "hallet")) c.Set("send", true);
				var names = c.NameWords();
				if (names.Count > 0 && c.Has("cikar", "unregister", "remove")) c.Set("remove", Arr(names));
				else if (names.Count > 0 && c.Has("koy", "ekle", "add")) c.Set("add", Arr(names));
			}),
		R("fm_numbers", "numarayi giysin=5; numara ver=4; giyiyo=4; numaralari=4; kac numara=4; squad numbers=5; the number=4; numbers are free=5; numara istedi=4", c =>
			{
				if (c.Has("yardimci", "auto")) { c.Set("auto", true); return; }
				if (c.Name() is string p) c.Set("player", p);
				if (c.Num() is int n && c.Args.ContainsKey("player")) c.Set("number", n);
			}),

		// ---- club and board
		R("fm_club", "kulup durumu=5; kulubun hedefleri=5; sezon hedef=5; kulup vizyon=5; yonetimin notu=5; kulup ozet=5; club overview=5; objectives=4; season target=5"),
		R("fm_vision", "hedefi cikar=5; yonetime oner=5; geri al=4; toplantiyi bitir=5; remove objective=5; accept the vision=5; undo=4", c =>
			{
				if (c.Has("cikar", "remove")) { if (c.Num() is int n) c.Set("remove", n.ToString()); }
				else if (c.Has("oner", "sun$", "suggest", "propose", "submit")) c.Set("action", "suggest");
				else if (c.Has("geri al", "undo", "revert")) c.Set("action", "undo");
				else if (c.Has("sifirla", "bastan", "reset", "start over")) c.Set("action", "reset");
				else if (c.Has("bitir", "kapat", "end", "finish", "close")) c.Set("action", "end");
				else if (c.Has("accept", "kabul", "onayla", "approve", "confirm")) c.Set("action", "confirm");
			}),
		R("fm_board", "yonetim=2; seviyo mu=3; kovulacak=5; guven=4; hakkimda=4; isim garanti=5; board confidence=5; getting sacked=5; job safe=5; board think=5; destekliyo=1"),
		R("fm_board_request", "yonetimden=4; isteyebilirim=3; talep=4; tesislerini gelistir=4; stadyum=4; arttirsinlar=4; board requests=5; ask the board=5; request=4; " +
			"ask for a new=4", c =>
			{
				if (c.LexValue(BoardRequests) is string r) c.Set("request", r);
			}),
		R("fm_budget", "butce=3; ne kadar param=5; kac para harcayabilirim=5; scout butcesi=5; kaydirici=5; budget=3.5; how much can i spend=5; wage budget=5; slider=5", c =>
			{
				if (c.Has("kaydir", "aktar", "dengele", "ayarla", "ayarini", "dengesini", "slider", "rebalance", "shift", "move", "adjust"))
				{
					c.Set("adjust", true);
					if (c.Num() is int n) c.Set("step", n);
				}
			}),
		R("fm_finances", "parasi ne durumda=5; finansal=4; fair play=4; maas yuku=5; gelir=4; para nereye=5; batiyo=5; finans=4; finances=5; bank balance=5; ffp=5; " +
			"income=4; spend last month=5; what did we spend=5", c =>
			{
				if (c.Has("gelir", "income")) c.Set("breakdown", "income");
				else if (c.Has("nereye", "spend", "gider", "expenditure")) c.Set("breakdown", "expenditure");
				if (c.Has("last month", "gecen ay")) c.Set("period", "last month");
				else if (c.Has("last season", "gecen sezon")) c.Set("period", "last season");
				else if (c.Has("this season", "bu sezon")) c.Set("period", "season");
			}),
		R("fm_facilities", "tesisler=4; stadin adi=5; facilities=4; stadium capacity=5; training facilities=1"),
		R("fm_supporters", "taraftar=4; seyirci ortalamamiz=5; supporters=5; fans=4"),
		R("fm_staff", "personel=2.5; teknik ekip=5; calisanlarimiz=5; kac scoutumuz=5; personel listesi=5; how many scouts=5"),
		R("fm_staff_find", "#pos bul$=1; bul$=2; lazim=1.5; personel aramas=5; fizyoterapist=3; yardimci antrenor=3; gelmek isteyen personel=5; koçluk ozellik=5; " +
			"kocluk ozellik=5; sifirla=1.5; find a free=5; goalkeeping coaches=5; staff interested=5; fitnes hocasi=2; physio=3", c =>
			{
				if (c.Has("sifirla", "clear")) { c.Set("clear", true); return; }
				if (c.Has("gelmek isteyen", "interested")) { c.Set("filter", "Interested In Joining First Team"); return; }
				if (c.Find("aramasinda") is var at && at >= 0 && c.Name() is string n) { c.Set("text", n); return; }
				if (c.LexValue(StaffRoles) is string r) c.Set("role", r);
				if (c.Has("serbest", "bedava", "bosta", "issiz", "free", "unemployed", "out of work")) c.Set("free", true);
				if (c.Has("cok iyi", "very good")) c.Set("reputation", "Very Good");
				else if (c.Has("iyi bir", "good")) c.Set("reputation", "Good");
				if (c.Has("kocluk ozellik", "coaching attributes")) c.Set("view", "Coaching");
			}),
		R("fm_staff_hire", "ise al=5; gorus=1.5; alalim=2.5; isteyen=1; hire=5", c =>
			{
				if (c.Name() is string n) c.Set("name", n);
				if (c.Money() is string w) c.Set("wage", w);
				if (c.Has("imzala", "sign")) c.Set("sign", true);
			}),
		R("fm_staff_fire", "kov$=4; isine son=5; sack #name=5; fire #name=4; let #name go=4; yollarimizi ayir=2.5; karsilikli anlasmayla gonder=4", c =>
			{
				if (c.Name() is string n) c.Set("name", n);
				if (c.Has("karsilikli", "mutual")) c.Set("mutual", true);
			}),
		R("fm_responsibilities", "sorumluluk=5; gorev dagilimi=5; kim yapiyo=3; devret=5; sportif direktor=3; kime verilebilir=4; ben gitmeyeyim=4; responsibilities=5; " +
			"who handles=5; delegate=5; take back=5; give hiring=5; dof$=2", c =>
			{
				var duty = c.LexValue(Duties);
				if (c.Has("kim yapiyo", "who handles")) { if (duty != null) c.Set("filter", duty); return; }
				if (c.Has("devret", "delegate", "gitmeyeyim")) { if (duty != null) c.Set("delegate", duty); return; }
				if (c.Has("take back", "geri al")) { if (duty != null) c.Set("take", duty); return; }
				if (duty != null) c.Set("give", duty);
				if (c.Has("sportif direktor", "dof", "director of football")) c.Set("to", "director of football");
			}),
		R("fm_jobs", "is ilan=5; teknik direktorluk=4; hoca ariyo=5; is bul=5; isinin detayi=5; ise bak=5; basvur=5; isiyle ilgilen=5; job$=4; apply for=5", c =>
			{
				if (c.Num() is int n) c.Set("club", n.ToString());
				else if (c.Name() is string club) c.Set("club", club);
				if (c.Has("basvur", "apply")) c.Set("action", "apply");
				else if (c.Has("ilgilen", "interest")) c.Set("action", "interest");
			}),
		R("fm_resign", "istifa=5; biraktim=4; istfa=5; resign=5; i quit=5", c =>
			{
				if (c.Has("sezon sonu", "sezon bitince", "end of the season")) c.Set("end_of_season", true);
			}),

		// ---- screens and settings
		R("fm_settings", "ayarlar=5; vurgu hizi=5; otomatik kayd=4; otomatik kaydetsin=5; para birimi=5; dilini=4; dili$=4; settings=5; highlight speed=5; autosave=5; " +
			"currency=5; key highlights=5; match speed=5", c =>
			{
				if (c.Lex(SettingNames) is { } n) c.Set("name", n.Value);
				if (c.LexValue(SettingValues) is string v) c.Set("value", v);
				if (c.Has("her gun", "every day", "daily")) { c.Set("name", "save interval"); c.Set("value", "Day"); }
				else if (c.Has("haftada bir", "haftalik", "once a week", "every week", "weekly")) { c.Set("name", "save interval"); c.Set("value", "Week"); }
				if (!c.Args.ContainsKey("name") && c.Args["value"]?.GetValue<string>() is string value)
					c.Set("name", value switch { "English" or "Türkçe" => "language", "Euro" or "Turkish Lira" or "Pound" or "Dollar" => "currency", "Key" => "highlights", _ => null });
			}),
		R("fm_pages", "sayfalar=4; sayfalari=4; list pages=5; sekmesindeki=3", c =>
			{
				var at = Math.Max(c.Find("sayfa"), c.Find("sekmesindeki"));
				if (at > 0 && c.W[at - 1] != "oyunun") c.Set("filter", c.W[at - 1].Replace("sekmesindeki", ""));
			}),
		R("fm_actions", "sag tik=5; neler yapabilirim=5; ne yapabilirim=4; what can i do with=5; right click=5; actions for=4; options for=3; coaching course=4; " +
			"antrenorluk kurs=4; squad planner=3; kadro planlayici=3", c =>
			{
				if (c.Name() is string p) c.Set("person", p);
				if (c.LexValue(PersonActions) is string a) c.Set("choose", a);
			}),
		R("fm_open", "planlayiciyi ac=5; merkezini ac=5; merkezine git=5; ana sayfaya don=5; ekrani ac=5; open the=3; #name ac$=1", c =>
			{
				if (c.LexValue(Pages) is string p) c.Set("page", p);
				else if (c.T.Raw.Contains(':')) c.Set("page", Text.Fold(c.T.Raw[(c.T.Raw.LastIndexOf(':') + 1)..]).Trim());
				// "dinamikler sayfasını aç", "open the finances screen", "go to tactics": the page by the words around it
				else if (new[] { "sayfa", "ekran", "page", "screen" }.Select(w => c.Find(w)).Where(i => i > 0).DefaultIfEmpty(-1).Min() is var end && end > 0)
				{
					var start = Math.Max(c.Find("open"), c.Find("to")) + 1;
					if (c.Between(start, end) is { Length: > 0 } words) c.Set("page", words);
				}
				else if (Math.Max(c.Find("open"), c.Find("go to") >= 0 ? c.Find("go to") + 1 : -1) is var open && open >= 0 && c.Between(open + 1, c.W.Length) is { Length: > 0 } rest)
					c.Set("page", rest);
			}),
		R("fm_buttons", "butonu nerde=5; dugmeler=5; buttons=5", c =>
			{
				if (c.Find("butonu") is var at && at > 0) c.Set("filter", c.W[at - 1]);
			}),
		R("fm_press", "butonuna bas=5; dugmesine tikla=5; tusuna bas=5; kaydete bas=5; suna bas=5; press confirm=5; hit back=5; press$=1; bas$=1.5", c =>
			{
				var at = new[] { "butonuna", "dugmesine", "tusuna" }.Select(w => c.Find(w)).Where(i => i > 0).DefaultIfEmpty(-1).Max();
				if (at > 0) c.Set("label", Ctx.Title(c.W[at - 1]));
				else if (c.T.Raw.Contains(':')) c.Set("label", Ctx.Title(Text.Fold(c.T.Raw[(c.T.Raw.IndexOf(':') + 1)..]).Trim()));
				else if (c.Find("kaydete") >= 0) c.Set("label", "Kaydet");
				else if (Math.Max(c.Find("press"), c.Find("hit")) is var p && p >= 0 && p + 1 < c.W.Length) c.Set("label", Ctx.Title(c.W[p + 1]));
				if (c.Has("sadece ne olacag", "dry")) c.Set("dry", true);
			}),
		R("fm_type", "kutuya yaz=5; lakap olarak yaz=5; diye yaz=4; type in=4; in the box=4; yaz$=1.5", c =>
			{
				// As the user wrote it: the words are folded (lower case, no accents), a nickname is not.
				string Shown(string folded) => c.T.Raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(r => Text.Fold(r).Trim() == folded) ?? folded;
				if (c.T.Raw.IndexOf('"') is var q && q >= 0 && c.T.Raw.IndexOf('"', q + 1) is var e && e > q)
					c.Set("text", c.T.Raw[(q + 1)..e]);
				else if (c.Find("diye") is var diye && diye > 0) c.Set("text", Shown(c.W[diye - 1]));
				else if (c.Find("yaz") is var yaz && yaz > 0 && c.W[yaz - 1] is not ("kutuya" or "olarak")) c.Set("text", Shown(c.W[yaz - 1]));
				else if (c.Find("type") is var t && t >= 0 && t + 1 < c.W.Length && c.W[t + 1] != "in") c.Set("text", Shown(c.W[t + 1]));
				if (c.Has("sadece ne olacag", "dry")) c.Set("dry", true);
			}),
		R("fm_dropdowns", "acilir menu=5; secim kutulari=5; menusunde neler=5; dropdowns=5", c =>
			{
				if (c.Find("menusunde") is var at && at > 0) c.Set("filter", c.W[at - 1]);
			}),
		R("fm_select", "kutusundan=5; menuden=4; from the #name list=4; in the radio=5; pick #num=1", c =>
			{
				var box = c.Find("kutusundan");
				if (box > 0) { c.Set("list", Ctx.Title(c.W[box - 1])); c.Set("choice", Ctx.Title(c.Between(box + 1, c.W.Length - 1).Replace("ayi", "ay"))); return; }
				var from = c.Find("from the");
				if (from > 0) { c.Set("list", Ctx.Title(c.W[from + 2])); c.Set("choice", Ctx.Title(c.Between(1, from))); return; }
				if (c.Find("choose") is var ch && ch >= 0 && ch + 1 < c.W.Length) c.Set("choice", Ctx.Title(c.W[ch + 1]));
			}),
	];

	private static string? OrdinalWordAt(Ctx c, int at) => Ctx.OrdinalOf(c.W[at]) is int ? c.W[at] : null;

	/// <summary>"2 hafta", "3 maç", "1 month", "6 months", "1 yıl" as the game's lengths.</summary>
	private static string? Length(Ctx c)
	{
		foreach (var (n, at) in c.Nums())
		{
			if (at + 1 >= c.W.Length) continue;
			var unit = c.W[at + 1];
			string? word = Text.Matches(unit, "hafta") || Text.Matches(unit, "week*") ? "week" : Text.Matches(unit, "mac$") || Text.Matches(unit, "match*") ? "match"
				: Text.Matches(unit, "ay$") || Text.Matches(unit, "aylik") || Text.Matches(unit, "month*") ? "month" : Text.Matches(unit, "yil") || Text.Matches(unit, "year*") ? "year" : null;
			if (word != null) return $"{n} {word}{(n > 1 ? (word == "match" ? "es" : "s") : "")}";
		}
		// "for a month", "bir haftalığına"
		if (c.LexValue(Ctx.Durations) is string one && one != "day" && (c.Has("a week", "a month", "a year") || c.Has("bir", "bi"))) return $"1 {one}";
		return null;
	}
}
