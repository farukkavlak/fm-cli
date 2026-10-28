namespace FmCli.Nl;

/// <summary>
/// Value words and the readers too long to sit in the rule table. A value in the game's own
/// words is written in the language of the phrase that names it ("savunma çizgisi" gives
/// "Defansif Çizgi", "defensive line" gives "Defensive Line"); the tool lists the choices
/// when a value is off.
/// </summary>
internal static partial class Router
{
	private static readonly Lex TalkTones = new(
		("sakin", "sakin"), ("gurur", "gurur"), ("daha fazla", "daha fazla"), ("hayal kirikligi", "hayal kırıklığı"), ("calm", "calm"), ("proud", "proud"),
		("disappointed", "disappointed"), ("well done", "well done"), ("aferin", "aferin"));

	private static readonly Lex ReplyAnswers = new(
		("reddet", "reddet"), ("kabul et", "kabul"), ("accept", "accept"), ("reject", "reject"), ("decline", "decline"));

	private static readonly Lex PenaltyMoves = new(
		("ekle", "add"), ("listesine", "add"), ("add", "add"), ("put", "add"), ("atmasin", "remove"), ("atmayacak", "remove"), ("cikar", "remove"), ("listeden", "remove"),
		("remove", "remove"), ("take off", "remove"), ("drop", "remove"));

	private static readonly Lex DialogAnswers = new(
		("evet", "Yes"), ("yes", "Yes"), ("hayir", "No"), ("no$", "No"), ("tamam", "OK"), ("ok$", "OK"), ("iptal", "Cancel"), ("cancel", "Cancel"));

	private static readonly Lex Shouts = new(
		("cesaretlendir", "Cesaretlendir"), ("sakinlestir", "Sakinleştir"), ("daha fazlasini iste", "Daha Fazlasını İste"), ("ov$", "Öv"), ("odaklan", "Odaklan"),
		("encourage", "Encourage"), ("fire them up", "Fire Up"), ("fire up", "Fire Up"), ("demand more", "Demand More"), ("praise", "Praise"), ("no pressure", "No Pressure"),
		("calm down", "Calm Down"), ("focus", "Focus"), ("baski yok", "Baskı Yok"),
		("gaza getir", "Ateşle"), ("atesle", "Ateşle"), ("firca", "Azarla"), ("azarla", "Azarla"), ("tebrik", "Öv"), ("aferin", "Öv"), ("rahatla", "Baskı Yok"),
		("rahat oyna", "Baskı Yok"), ("sakin", "Sakinleştir"), ("odaklan*", "Odaklan"), ("konsantre", "Odaklan"), ("daha fazla", "Daha Fazlasını İste"),
		("cesaret", "Cesaretlendir"), ("get them going", "Fire Up"), ("pump them up", "Fire Up"), ("well done", "Praise"), ("good job", "Praise"),
		("concentrate", "Focus"), ("relax", "No Pressure"), ("settle down", "Calm Down"), ("calm", "Calm Down"), ("want more", "Demand More"));

	private static readonly Lex OppositionSets = new(
		("baski", "Pressing=Always"), ("press$", "Pressing=Always"), ("sert", "Tackling=Hard"), ("tackle hard", "Tackling=Hard"), ("sag ayag", "Foot=Right"),
		("right foot", "Foot=Right"), ("sol ayag", "Foot=Left"), ("left foot", "Foot=Left"), ("tight mark", "Marking=Tight"));

	private static readonly Lex PrepFocus = new(
		("ilerleme", "Progression"), ("orta blok", "Mid Block"), ("savunma kornerleri", "Defending Corners"), ("hucum kornerleri", "Attacking Corners"),
		("progression", "Progression"), ("mid block", "Mid Block"), ("defending corners", "Defending Corners"),
		("hucum serbest vurus", "Attacking Free Kicks"), ("hucum frikik", "Attacking Free Kicks"), ("attacking free kick*", "Attacking Free Kicks"),
		("savunma serbest vurus", "Defending Free Kicks"), ("savunma frikik", "Defending Free Kicks"), ("defending free kick*", "Defending Free Kicks"),
		("attacking corners", "Attacking Corners"), ("alcak blok", "Low Block"), ("dusuk blok", "Low Block"), ("low block", "Low Block"), ("yuksek blok", "High Block"),
		("high block", "High Block"));

	private static readonly Lex SquadViews = new(
		("kondisyon", "fitness"), ("mac formu", "fitness"), ("yorgun", "fitness"), ("fitness", "fitness"), ("sharpness", "fitness"),
		("moral", "morale"), ("morale", "morale"), ("mood", "morale"), ("keyif", "morale"), ("keskinlik", "fitness"), ("yorgunluk", "fitness"), ("condition", "fitness"),
		("tired", "fitness"), ("gol$", "stats"), ("goller", "stats"), ("goals", "stats"), ("appearances", "stats"), ("ratings", "stats"), ("cards", "stats"),
		("sozlesme", "Contract"), ("contracts", "Contract"), ("sakatlik", "Injuries"), ("injur*", "Injuries"),
		("kac gol", "stats"), ("istatistik", "stats"), ("golcusu", "stats"), ("kac mac", "stats"), ("reyting", "stats"), ("kart cezasi", "stats"), ("stats", "stats"), ("most goals", "stats"),
		("altyapi takim", "youth"), ("youth squads", "youth"), ("genc takim", "youth"), ("youth team", "youth"), ("u18", "youth"), ("u21", "youth"), ("u23", "youth"),
		("asist", "stats"), ("assists", "stats"),
		("sozlesme gorunum", "Contract"), ("contract view", "Contract"), ("transfer gorunum", "Transfer"), ("oynama sure", "Playing Time"), ("gorunumleri neler", "list"));

	private static readonly Lex Styles = new(
		("gegenpress", "Gegenpress"), ("kontra", "Kontra"), ("tiki taka", "Tiki-Taka"), ("counter", "Counter"), ("tiki-taka", "Tiki-Taka"));

	private static readonly Lex Mentalities = new(
		("otobus", "Çok Defansif"), ("cok defansif", "Çok Defansif"), ("defansif", "Defansif"), ("kapan", "Defansif"), ("skoru koru", "Defansif"),
		("dengeli", "Dengeli"), ("pozitif", "Pozitif"), ("hucum", "Hücum"), ("saldir", "Hücum"), ("cok hucum", "Çok Hücum"), ("temkinli", "Temkinli"),
		("dikkatli", "Temkinli"), ("savunmaci", "Defansif"), ("geride kal", "Defansif"), ("cautious", "Cautious"), ("sit back", "Defensive"), ("defend the lead", "Defensive"),
		("go for it", "Very Attacking"),
		("very attacking", "Very Attacking"), ("all out attack", "Very Attacking"), ("attacking", "Attacking"), ("balanced", "Balanced"), ("positive", "Positive"),
		("see out the game", "Defensive"), ("defensive", "Defensive"), ("very defensive", "Very Defensive"), ("park the bus", "Very Defensive"));

	private static readonly Lex TeamInstructions = new(
		("savunma cizgi", "Defansif Çizgi=Daha Yüksek"), ("defans cizgi", "Defansif Çizgi=Daha Yüksek"), ("daha cok pres", "Pres Sıklığı=Daha Fazla"),
		("daha fazla pres", "Pres Sıklığı=Daha Fazla"), ("pres yap*", "Pres Sıklığı=Daha Fazla"), ("sert mudahale", "Müdahale=Sert"),
		("ofsayt", "Ofsayt Taktiği=Uygula"), ("tempo", "Tempo=Daha Yüksek"), ("zaman gecir", "Zaman Geçirme=Daha Fazla"), ("kontra pres", "Karşı Pres="),
		("kanatlardan", "Hücum Genişliği=Daha Geniş"), ("pres hatti", "Pres Hattı="), ("kaleci topu", "Kaleci Dağıtımı="),
		("press more", "Pressing Intensity=More"), ("get stuck in", "Tackling=Harder"), ("slow the tempo", "Tempo=Lower"), ("line of engagement", "Line of Engagement="),
		("defensive line", "Defensive Line=Higher"), ("waste time", "Time Wasting=More"),
		("defans hatt", "Defansif Çizgi=Daha Yüksek"), ("savunma hatt", "Defansif Çizgi=Daha Yüksek"), ("pres*", "Pres Sıklığı=Daha Fazla"), ("presi azalt", "Pres Sıklığı=Daha Az"),
		("hizli oyna", "Tempo=Daha Yüksek"), ("yavas oyna", "Tempo=Daha Düşük"), ("oyunu yavaslat", "Tempo=Daha Düşük"), ("pas mesafe", "Pas Mesafesi=Daha Kısa"),
		("kisa pas", "Pas Mesafesi=Daha Kısa"), ("uzun pas", "Pas Mesafesi=Daha Uzun"), ("uzun top", "Pas Mesafesi=Daha Uzun"), ("genis oyna", "Hücum Genişliği=Daha Geniş"),
		("hucum genislig", "Hücum Genişliği=Daha Geniş"), ("dar oyna", "Hücum Genişliği=Daha Dar"), ("mudahale", "Müdahale=Sert"), ("zaman kazan", "Zaman Geçirme=Daha Fazla"),
		("back line", "Defensive Line=Higher"), ("drop deeper", "Defensive Line=Lower"), ("press*", "Pressing Intensity=More"), ("tempo", "Tempo=Higher"),
		("slow down", "Tempo=Lower"), ("play faster", "Tempo=Higher"), ("time wasting", "Time Wasting=More"), ("tackle harder", "Tackling=Harder"));

	private static readonly Lex PlayerInstructions = new(
		("sut", "Şut=Daha Sık"), ("bindirme", "İleri Koşular=Daha Sık"), ("ileri cik", "İleri Koşular=Daha Sık"), ("calim", "Top Sürme=Daha Sık"),
		("prese cik", "Pres Tetikleme=Daha Fazla"), ("tutsun", "Markaj Hedefi="), ("shoot", "Shooting=More Often"), ("dribble", "Dribbling Frequency=More Often"),
		("fewer risks", "Passing Risk=Less"), ("man mark", "Marking Target="),
		("sut cek*", "Şut=Daha Sık"), ("ileri kos*", "İleri Koşular=Daha Sık"), ("hucuma cik*", "İleri Koşular=Daha Sık"), ("bindir*", "İleri Koşular=Daha Sık"),
		("top sur*", "Top Sürme=Daha Sık"), ("riskli pas", "Pas Riski=Daha Fazla"), ("pas riski", "Pas Riski=Daha Fazla"), ("risk al*", "Pas Riski=Daha Fazla"),
		("pres*", "Pres Tetikleme=Daha Fazla"), ("markaj", "Markaj Hedefi="), ("adam adama", "Markaj Hedefi="),
		("get forward", "Forward Runs=More Often"), ("forward runs", "Forward Runs=More Often"), ("overlap*", "Forward Runs=More Often"), ("stay back", "Forward Runs=Less Often"),
		("run with the ball", "Dribbling Frequency=More Often"), ("risky passes", "Passing Risk=More"), ("take risks", "Passing Risk=More"), ("safer passes", "Passing Risk=Less"),
		("mark their", "Marking Target="));

	private static readonly (string Up, string Down)[] Directions =
	[
		("Daha Yüksek", "Daha Düşük"), ("Daha Fazla", "Daha Az"), ("Daha Geniş", "Daha Dar"), ("Daha Uzun", "Daha Kısa"), ("Daha Sık", "Daha Seyrek"),
		("Higher", "Lower"), ("More Often", "Less Often"), ("More", "Less"), ("Wider", "Narrower"), ("Longer", "Shorter"),
	];

	private static readonly string[] UpWords = ["yukselt", "yukari", "artir", "arttir", "daha cok", "daha fazla", "more", "higher", "up$", "push", "increase", "raise", "faster"];
	private static readonly string[] DownWords = ["dusur", "geri", "azalt", "asagi", "daha az", "less", "lower", "drop", "deeper", "decrease", "reduce", "fewer", "slow", "yavas", "seyrek"];

	[System.Text.RegularExpressions.GeneratedRegex("^[a-z]{2,}(masin|mesin|masinlar|mesinler|mayin|meyin)$")]
	private static partial System.Text.RegularExpressions.Regex TurkishDont();

	/// <summary>
	/// A value with a direction turned the way the sentence says: "defans hattını geri çek" lowers
	/// the line, "sol bek ileri çıkmasın" makes the runs rarer ("-masın", "don't" turn it round).
	/// </summary>
	private static string Steer(Ctx c, string value)
	{
		var pair = Directions.FirstOrDefault(d => d.Up == value || d.Down == value);
		if (pair == default) return value;
		var (up, down) = (c.Has(UpWords), c.Has(DownWords));
		var low = value == pair.Down;
		if (down && !up) low = true;
		else if (up && !down) low = false;
		if (c.W.Any(w => TurkishDont().IsMatch(w)) || c.Has("dont", "do not", "stop", "no more", "never")) low = !low;
		return low ? pair.Down : pair.Up;
	}

	private static readonly Lex Roles = new(
		("hedef adam", "Target Forward"), ("target forward", "Target Forward"), ("pres forvet", "Pressing Forward"), ("pressing forward", "Pressing Forward"),
		("kanat bek", "Wing-Back"), ("wing back", "Wing-Back"), ("derin oyun kurucu", "Deep-Lying Playmaker"), ("deep lying playmaker", "Deep-Lying Playmaker"),
		("ice kat eden forvet", "Inside Forward"), ("inside forward", "Inside Forward"), ("golge forvet", "Shadow Striker"), ("shadow striker", "Shadow Striker"),
		("sweeper keeper", "Sweeper Keeper"), ("libero kaleci", "Sweeper Keeper"), ("playmaker", "Playmaker"), ("box to box", "Box-To-Box Midfielder"));

	private static readonly Lex SetPieceKinds = new(
		("uzak mesafe frikik", "deep"), ("uzak frikik", "deep"), ("deep", "deep"), ("kanattan frikik", "wide"), ("wide free kick", "wide"),
		("korner", "corner"), ("corner", "corner"), ("frikik", "freekick"), ("free kick", "freekick"), ("free kicks", "freekick"),
		("tac", "throw"), ("throw", "throw"), ("throw ins", "throw"), ("long throw", "throw"));

	private static readonly Lex SetPieceAims = new(
		("arka direk", "Arka Direk"), ("on direk", "Ön Direk"), ("ceza sahasina", "Ceza Sahası"), ("near post", "Near Post"), ("far post", "Far Post"));

	private static readonly Lex SetPieceTasks = new(
		("kaleciyi rahatsiz", "Kaleciyi Rahatsız Et"), ("mark the keeper", "Distract Keeper"), ("distract the keeper", "Distract Keeper"));

	private static readonly Lex CoachAreas = new(
		("kaleci", "kaleci"), ("fiziksel", "fitness"), ("fizik", "fitness"), ("kondisyon", "fitness"), ("goalkeeping", "goalkeeping"), ("goalkeepers", "goalkeeping"),
		("keepers", "goalkeeping"), ("fitness", "fitness"), ("physical", "fitness"), ("savunma", "savunma"), ("defans", "savunma"), ("hucum", "hücum"), ("defence", "defending"),
		("defense", "defending"), ("attack", "attacking"), ("duran top", "duran top"), ("set piece*", "set pieces"), ("topla taktik", "topla taktik"),
		("topsuz taktik", "topsuz taktik"), ("possession tactical", "Poss Tact"),
		("defending", "defending"), ("attacking", "attacking"), ("teknik", "teknik"), ("technical", "technical"));

	private static readonly Lex ScheduleStyles = new(
		("hafif sezon oncesi", "Pre-Season Early/Light"), ("dengeli antrenman", "Training Style/Balanced"), ("balanced training", "Training Style/Balanced"));

	private static readonly Lex Sessions = new(
		("cabukluk", "Quickness"), ("quickness", "Quickness"), ("dinlenme", "Rest"), ("rest$", "Rest"), ("toparlanma", "Physical/Recovery"), ("recovery", "Physical/Recovery"));

	private static readonly Lex StaffRoles = new(
		("yardimci antrenor", "Assistant Manager"), ("assistant manager", "Assistant Manager"), ("fizyoterapist", "Physio"), ("physio", "Physio"),
		("fitnes hocasi", "Fitness Coach"), ("fitness coach", "Fitness Coach"), ("kaleci antrenoru", "Goalkeeping Coach"), ("goalkeeping coaches", "Goalkeeping Coach"),
		("goalkeeping coach", "Goalkeeping Coach"), ("kaleci hoca", "Goalkeeping Coach"), ("kaleci koc", "Goalkeeping Coach"), ("scout", "Scout"), ("gozlemci", "Scout"), ("antrenor", "Coach"), ("coach", "Coach"));

	private static readonly Lex Duties = new(
		("sozlesme uzatma", "contract"), ("contract renewals", "contract"), ("hazirlik maci", "friendlies"), ("friendlies", "friendlies"),
		("basin toplanti", "press conference"), ("press conferences", "press conference"), ("press conference", "press conference"),
		("personel alimi", "hiring"), ("hiring", "hiring"), ("antrenorluk kurs", "coaching courses"), ("coaching courses", "coaching courses"),
		("team selection", "team selection"), ("takim secimi", "team selection"));

	private static readonly Lex BoardRequests = new(
		("transfer butcesi", "transfer budget"), ("transfer money", "transfer budget"), ("para$", "transfer budget"), ("antrenman tesis", "training facilities"),
		("training facilities", "training facilities"), ("altyapi tesis", "youth facilities"), ("youth facilities", "youth facilities"), ("stadyum", "stadium"),
		("stadium", "stadium"), ("scout", "scout"), ("sozlesme", "contract"), ("contract", "contract"), ("maas butce", "wage budget"), ("wage budget", "wage budget"));

	private static readonly Lex SettingNames = new(
		("vurgu hizi", "highlight speed"), ("highlight speed", "highlight speed"), ("otomatik kayd", "auto save"), ("otomatik kayit", "auto save"), ("autosave", "auto save"), ("auto save", "auto save"),
		("para birimi", "currency"), ("currency", "currency"), ("dil", "language"), ("dilini", "language"), ("language", "language"),
		("key highlights", "highlights"), ("match speed", "match speed"), ("mac hizi", "match speed"),
		("kayit aralig", "save interval"), ("save interval", "save interval"), ("birim", "units"), ("units", "units"), ("vurgular", "highlights"), ("highlights", "highlights"),
		("onemli an", "highlights"), ("key moments", "highlights"));

	private static readonly Lex SettingValues = new(
		("2x", "2x"), ("1x", "1x"), ("4x", "4x"), ("ac$", "On"), ("acik olsun", "On"), ("turn on", "On"), ("enable", "On"), ("on$", "On"), ("kapat", "Off"), ("turn off", "Off"),
		("disable", "Off"), ("off$", "Off"), ("english", "English"), ("turkish", "Türkçe"), ("euro", "Euro"), ("turk lirasi", "Turkish Lira"),
		("sterlin", "Pound"), ("dolar", "Dollar"), ("ingilizce", "English"), ("turkceye", "Türkçe"), ("turkce", "Türkçe"), ("key$", "Key"));

	private static readonly Lex PersonActions = new(
		("antrenorluk kurs", "Send On Coaching Course"), ("koçluk kurs", "Send On Coaching Course"), ("kocluk kurs", "Send On Coaching Course"),
		("coaching course", "Send On Coaching Course"), ("kadro planlayici", "Squad Planner"), ("kadro plan", "Squad Planner"), ("squad planner", "Squad Planner"));

	private static readonly Lex Pages = new(
		("kadro planlayici", "Kadro Planlayıcı"), ("veri merkez", "Veri Merkezi"), ("transfer merkez", "Transfer Merkezi"), ("ana sayfa", "Ana Sayfa"),
		("squad planner", "Squad Planner"), ("data hub", "Data Hub"), ("transfer centre", "Transfer Centre"), ("home page", "Home"));

	private static readonly Lex TrainingFocus = new(
		("bitiricilik", "Bitiricilik"), ("orta acma", "Orta"), ("hiz$", "Hız"), ("kafa", "Kafa"), ("pas", "Pas"), ("finishing", "Finishing"), ("crossing", "Crossing"),
		("pace", "Pace"), ("heading", "Heading"), ("passing", "Passing"));

	private static readonly Lex TrainingPositions = new(
		("sol bek", "Sol Bek"), ("sag bek", "Sağ Bek"), ("stoper", "Stoper"), ("forvet", "Forvet"), ("left back", "Left Back"), ("right back", "Right Back"),
		("centre back", "Centre Back"), ("striker", "Striker"));

	private static readonly Lex TrainingRoles = new(("hedef adam", "Hedef Forvet"), ("target forward", "Target Forward"), ("pres forvet", "Pres Forvet"));

	private static readonly Lex ScoutAreas = new(
		("super lig", "Competition>Turkey>Super League"), ("turkiye", "Turkey"), ("guney amerika", "South America"), ("almanya", "Germany"), ("germany", "Germany"),
		("avrupa", "Europe"), ("europe", "Europe"), ("italya", "Italy"), ("italy", "Italy"), ("ispanya", "Spain"), ("spain", "Spain"), ("brezilya", "Brazil"));

	private static readonly Lex Priorities = new(
		("yuksek", "top"), ("oncelikli", "top"), ("top priority", "top"), ("surekli", "ongoing"), ("ongoing", "ongoing"), ("standart", "standard"), ("standard", "standard"));

	private static readonly Lex Traits = new(
		("cuts inside", "Cuts Inside"), ("shoots from distance", "Shoots From Distance"), ("ice kat", "İçe Kat Eder"), ("uzaktan sut", "Uzaktan Şut Atar"));

	private static void Training(Ctx c)
	{
		if (c.Name() is string p) c.Set("player", p);
		if (c.Has("ogrenebilir", "can learn", "ppms", "traits")) { c.Set("traits", true); return; }
		if (c.Has("iptal", "cancel")) { c.Set("trait_cancel", true); return; }
		if (c.Has("unlearn")) { if (c.LexValue(Traits) is string t) c.Set("trait_remove", t); return; }
		if (c.Has("teach", "ogret")) { if (c.LexValue(Traits) is string t) c.Set("trait", t); return; }
		if (c.Has("rapor", "report")) { c.Set("report", true); return; }
		if (c.LexValue(TrainingRoles) is string r && c.Has("rol", "role")) { c.Set("role", r); return; }
		if (c.LexValue(TrainingPositions) is string pos) { c.Set("position", pos); return; }
		if (c.LexValue(TrainingFocus) is string f) c.Set("focus", f);
	}

	private static void Contract(Ctx c)
	{
		// the talks already open are the default, so a common word is not taken for a name here
		if (c.StrongName() is string n && !c.Has("maddelere", "menajere")) c.Set("name", n);
		var terms = new List<string>();
		var years = c.Nums().FirstOrDefault(x => x.At + 1 < c.W.Length && (Text.Matches(c.W[x.At + 1], "yil*") || Text.Matches(c.W[x.At + 1], "year*")));
		if (years != default) terms.Add($"Contract Length={years.Value} years");
		if (c.Has("prim", "appearance fee")) terms.Add("Appearance Fee=remove");
		var moneys = c.Moneys();
		var agentFee = c.Find("menajer ucret");
		var clause = Math.Max(c.Find("serbest kalma"), c.Find("release clause"));
		foreach (var (m, at) in moneys)
		{
			if (agentFee >= 0 && at > agentFee) terms.Add($"Agent Fee={m}");
			else if (clause >= 0) terms.Add($"Minimum Fee Release Clause={m}");
			else c.Set("wage", m);
		}
		if (terms.Count > 0) c.Set("terms", Arr(terms));
		if (c.Has("maddelere", "move on to the terms")) c.Set("next", true);
		if (c.Has("menajere sun", "imzala", "sign")) c.Set("sign", true);
		if (c.Has("iptal", "gorusmesinden cik", "vazgec", "walk away", "cancel", "call off")) c.Set("cancel", true);
		if (c.Has("agent if", "menajerine sor")) c.Set("agent", true);
	}

	private static readonly Lex QueryFields = new(
		("yas", "age"), ("yasindan", "age"), ("age", "age"), ("under", "age"), ("potansiyel", "potential"), ("potential", "potential"), ("kondisyon", "condition"),
		("condition", "condition"), ("maas", "wage"), ("earns", "wage"), ("wage", "wage"), ("sozlesme", "contract"), ("boyu", "height"), ("taller", "height"),
		("height", "height"), ("gol", "goals"), ("goals", "goals"), ("asist", "assists"), ("assists", "assists"), ("reyting", "rating"), ("rating", "rating"),
		("mac formu", "sharpness"), ("sharpness", "sharpness"));

	private static void Query(Ctx c)
	{
		var parts = new List<string>();
		string? sort = null;
		foreach (var (field, at, len) in c.LexAll(QueryFields))
		{
			if (field == "age" && c.W[at] == "under" && parts.Any(p => p.StartsWith("age"))) continue;
			var near = c.Nums().Concat(c.Moneys().Select(m => (Value: (int)MoneyValue(m.Value), m.At))).Where(n => Math.Abs(n.At - at) <= 3 && n.At != at).OrderBy(n => Math.Abs(n.At - at)).FirstOrDefault();
			var window = string.Join(' ', c.W[Math.Max(0, at - 1)..Math.Min(c.W.Length, at + len + 4)]);
			var less = new[] { "alti", "altinda", "kucuk", "under", "below", "az", "less", "dusuk" }.Any(w => window.Split(' ').Any(x => Text.Matches(x, w)));
			var more = new[] { "ustu", "fazla", "buyuk", "over", "more", "taller", "above", "yuksek" }.Any(w => window.Split(' ').Any(x => Text.Matches(x, w)));
			if (c.Has("sirala", "sorted", "sort", "baslayarak") && near == default)
			{
				sort = field;
				if (!c.Has("sorted by")) parts.Add($"{field}>0");
				continue;
			}
			if (near == default)
			{
				if (field == "sharpness" && less) parts.Add("sharpness<70");
				continue;
			}
			var v = near.Value;
			if (field == "contract" && window.Contains("yil")) v *= 12;
			if (field == "wage" && v < 1000) v *= 1000;
			parts.Add($"{field}{(less ? "<" : more || !less ? ">" : "<")}{v}");
		}
		if (c.Has("italyan", "italian")) parts.Add("nation=Italy");
		if (c.Has("sakat")) parts.Add("injured=true");
		if (c.Has("stoperlerimiz", "centre backs")) parts.Add("position=DC");
		if (c.Has("hic maca", "zero apps")) parts.Add("apps=0");
		if (c.Has("yedekte")) parts.Add("status=Backup");
		if (c.Has("sorted by") && c.Find("sorted by") is var sb && sb + 2 < c.W.Length) sort = c.LexValue(QueryFields) is string && c.W[sb + 2] == "age" ? "age" : c.W[sb + 2];
		if (parts.Count > 0) c.Set("where", string.Join(" and ", parts.Distinct()));
		if (sort != null) c.Set("sort", sort);
		if (c.Has("en ucuz", "baslayarak", "ascending")) c.Set("ascending", true);
	}

	private static double MoneyValue(string m) =>
		double.Parse(m.TrimEnd('M', 'K'), System.Globalization.CultureInfo.InvariantCulture) * (m.EndsWith('M') ? 1e6 : m.EndsWith('K') ? 1e3 : 1);

	private static readonly Lex PlayerFilters = new(
		("bonservissiz", "Contract Expired"), ("serbest oyuncu", "Contract Expired"), ("mutsuz", "Unhappy"), ("unhappy", "Unhappy"), ("free agents", "Contract Expired"),
		("kontrati bitmis", "Contract Expired"), ("sozlesmesi bitmis", "Contract Expired"), ("expired contract", "Contract Expired"), ("out of contract", "Contract Expired"));

	private static readonly Lex Attributes = new(
		("kafa topu", "heading"), ("heading", "heading"), ("pace", "pace"), ("hiz", "pace"), ("finishing", "finishing"), ("bitiricilik", "finishing"),
		("passing", "passing"), ("pas", "passing"), ("dribbling", "dribbling"));

	private static void Players(Ctx c)
	{
		if (c.Has("filtreyi kaldir", "temizle", "sifirla", "clear the")) { c.Set("clear", true); return; }
		if (c.Has("gorunum", "views")) { c.Set("list", "views"); return; }
		if (c.Has("quick filters", "hizli filtre")) { c.Set("list", "filters"); return; }
		if (c.Has("sutun", "columns")) { c.Set("view", "Contract"); return; }
		if (c.LexValue(PlayerFilters) is string f) c.Set("filter", f);
		if (c.Has("satis listesi", "transfer listesi", "transfer listed", "transfer list")) c.Set("listed", true);
		if (c.Has("kiralik", "kiralanabil", "kiraya", "loan listed", "for loan", "on loan")) c.Set("loan_listed", true);
		if (c.Has("kulubu olmayan", "unattached")) c.Set("contract", "Unattached");
		else if (c.Has("sozlesme", "deal", "left on")) c.Set("contract", c.Has("6 ay", "6 months") ? "Expiring (6 Months)" : "Expiring (1 Year)");
		var age = c.AgeRange();
		var strikers = c.Has("santrfor", "strikers", "forvetler");
		if (c.Has("kanat bek", "wing back")) c.Set("role", "Wing-Back");
		else if (strikers && age != null) c.Set("role", "Centre Forward");
		else if (c.Pos() is string p) c.Set("position", p);
		if (age != null) c.Set("age", age);
		if (c.Money() is string m) c.Set("max_value", m);
		else if (c.Has("ucuz", "cheap")) c.Ask("max_value");
		var grade = c.W.Select((w, i) => (w, i)).FirstOrDefault(x => x.w.Length == 1 && "abcd".Contains(x.w) && (c.Has("grade", "notu"))).w;
		if (grade != null) c.Set("grade", grade.ToUpperInvariant());
		var attrs = new List<string>();
		foreach (var (attr, at, len) in c.LexAll(Attributes))
		{
			var n = c.W.Skip(at + len).Take(2).Select(w => System.Text.RegularExpressions.Regex.Match(w, @"^(\d+)\+?$")).FirstOrDefault(x => x.Success);
			if (n != null) attrs.Add($"{attr}>={n.Groups[1].Value}");
			else if (c.Has("iyi", "good", "uzun")) attrs.Add($"{attr}>=15");
		}
		if (attrs.Count > 0) c.Set("attributes", Arr(attrs));
	}

	private static void ScoutFocus(Ctx c)
	{
		if (c.Has("bolge", "areas", "regions")) { c.Set("action", "areas"); if (c.LexValue(ScoutAreas) is string a0) c.Set("area", a0); return; }
		if (c.Has("gozlemcilerim", "scout kadrom", "my scouts", "scouts ratings")) { c.Set("action", "staff"); return; }
		var focusNum = c.Num()?.ToString();
		var named = c.Find("odaginin");
		if (named < 0) named = c.Find("odagini");
		var focusName = named > 0 ? c.Between(0, named) : null;
		var focus = focusNum ?? focusName;
		var isNew = c.Has("yeni", "new");
		if (!isNew && c.Has("iptal", "sil", "kaldir", "durdur", "stop", "delete", "remove", "cancel")) { c.Set("action", "remove"); if (focus != null) c.Set("focus", focus); return; }
		if (c.Has("restart", "yeniden")) { c.Set("action", "restart"); if (focus != null) c.Set("focus", focus); return; }
		var edit = !isNew && c.Has("odaga", "to focus", "odaginin");
		c.Set("action", edit ? "edit" : "new");
		if (edit && focus != null) c.Set("focus", focus);
		if (!edit)
		{
			if (c.Pos() is string p) c.Set("position", p);
			if (c.Find("ile") is var by && by > 0 && c.Name() is string scout) c.Set("by", scout);
		}
		if (c.LexValue(ScoutAreas) is string a) c.Set("area", a);
		if (c.AgeRange() is string age) c.Set("age", age);
		if (c.Has("potansiyel", "potential") && c.Num() is int stars) { c.Set("potential", stars.ToString()); }
		if (c.LexValue(Priorities) is string pr) c.Set("priority", pr);
		else if (!edit) c.Ask("priority");
	}
}
