using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// What can be pressed on the screen the game is showing. Unlabelled controls are icons and
	/// layout, and outnumber the rest, so they are left out unless asked for.
	/// </summary>
	private static async Task<int> Buttons(bool json, string[] args)
	{
		var controls = await Fm.GetJson("buttons", ("json", "1"));
		if (json)
		{
			Console.WriteLine(controls.ToString());
			return 0;
		}

		var all = args.Contains("--all");
		var filter = Words(args).FirstOrDefault();

		var rows = controls.EnumerateArray()
			.Where(c => all || Field(c, "Label") != "")
			.Select(c => new[]
			{
				Field(c, "Label"),
				Field(c, "Name"),
				Field(c, "Events") + (Field(c, "Enabled") == "True" ? "" : L("  (kapalı)")),
			})
			.Where(r => filter == null || r.Any(cell => cell.Contains(filter, StringComparison.OrdinalIgnoreCase)))
			.ToList();

		Table.Write(new[] { L("ETİKET"), L("AD"), L("NE GÖNDERİR") }, rows);
		return 0;
	}

	/// <summary>Every page the game has, read from its own menus, so none has to be open to be listed.</summary>
	private static async Task<int> PageList(bool json, string[] args)
	{
		var pages = await Fm.GetJson("pages");
		if (json)
		{
			Console.WriteLine(pages.ToString());
			return 0;
		}

		var filter = Words(args).FirstOrDefault();
		var rows = pages.EnumerateArray()
			.Select(p => new[] { Field(p, "Tab"), Field(p, "Page") })
			.Where(r => filter == null || r.Any(cell => cell.Contains(filter, StringComparison.OrdinalIgnoreCase)))
			.ToList();

		Table.Write(new[] { L("SEKME"), L("SAYFA") }, rows);
		return 0;
	}

	private static async Task<int> Open(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("kullanım: open \"<sayfa>\" [--tab <sekme>] [--dry]   (sayfalar için `pages`)"));
			return 64;
		}

		if (args.Contains("--dry"))
		{
			var query = new List<(string, string)> { ("page", words[0]), ("dry", "1") };
			if (Option(args, "--tab") is { } tab) query.Add(("tab", tab));
			return Report(await Arriving("open", query.ToArray()));
		}

		return Report(await OpenNamed(words[0], Option(args, "--tab")));
	}

	/// <summary>
	/// Opens a page by its GUID (<see cref="Screen"/>); a page about a team (a squad list) is
	/// told which by the team's unique id, the club's own team when none is given. A page in two
	/// tabs' menus is opened under the one whose first page is <paramref name="under"/>.
	///
	/// The bridge chooses the page's row in its tab's menu, as a click does, which leaves a page
	/// that needs to know what it is about (the tactics planner) bound, and needs no name. A row
	/// with a submenu (Training ›) goes by bookmark.
	///
	/// Such a page is one panel in both places, and a panel already up keeps the form it was
	/// opened in: the tactics planner's team report, chosen again under Match Day, stays the team
	/// report and never shows the next-match heading (FINDINGS §8.163). Another page is opened
	/// first so that the panel is built anew.
	/// </summary>
	internal static async Task<string> OpenPage(string screen, string team = null, string under = null)
	{
		var query = new List<(string, string)> { ("guid", screen) };
		if (team != null) query.Add(("object", team));
		if (under != null)
		{
			query.Add(("under", under));
			if (screen != Screen.Home && under != Screen.Home) await OpenWith([("guid", Screen.Home)]);
		}
		return await OpenWith(query.ToArray());
	}

	/// <summary>
	/// A page listed in another page's subtab menu (Squad Dynamics › Happiness), by its GUID: the
	/// page whose bar lists it is opened, then the row chosen on that bar. The first page can finish
	/// opening after the second and cover it, so the row is chosen again until the bar itself stands
	/// on the page and the page shows what <paramref name="shown"/> looks for.
	///
	/// The bar answers for the page only once the parent is built: a row chosen before that is lost
	/// (the press comes back "no page with guid …"), so the bar is waited on rather than paused on
	/// (measured: the bar answers 0.16–0.46 s after the parent is asked for; §8.246). What the bar
	/// stands on is the game's own word for the page being the one shown (?check=1). The content
	/// followed in 0.88–1.51 s (TR + EN, §8.302), so the upper bound is 8 s rather than 3 s.
	/// </summary>
	internal static async Task<string> OpenSubpage(string parent, string screen, Func<Task<bool>> shown)
	{
		var opened = await OpenPage(parent);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return opened;
		var answer = "";
		for (var attempt = 0; attempt < 4; attempt++)
		{
			// "shown"/"not shown" once the bar is there, an error while the parent is still coming up.
			await Until(async () => !(await OnBar(screen)).StartsWith("error", StringComparison.Ordinal), seconds: 6, every: 0.25);
			await Until(async () => !(answer = await Fm.Get("open", ("guid", screen))).StartsWith("error", StringComparison.Ordinal), seconds: 6, every: 0.5);
			if (answer.StartsWith("error", StringComparison.Ordinal)) return answer;
			// The page under this one answers even while the parent draws over it, so it is taken only
			// when the bar stands on it and the content is there, held over two readings in a row.
			var standing = false;
			if (await Until(async () =>
			{
				var on = (await OnBar(screen)).StartsWith("shown", StringComparison.Ordinal) && await shown();
				var settled = on && standing;
				standing = on;
				return settled;
			}, seconds: 8, every: 0.3)) return answer;
		}
		return L($"error: {answer}, ama sayfa beklenen içeriği göstermedi");
	}

	/// <summary>
	/// The row the subtab bar on screen stands on: "shown" when it is the given page, "not shown"
	/// otherwise, and an error while the page whose bar lists it is not up yet. Nothing is chosen.
	/// </summary>
	private static Task<string> OnBar(string screen) => Fm.Get("open", ("guid", screen), ("check", "1"));

	/// <summary>A page as the game's own menus name it, in the game's language: what someone types after `fm open`.</summary>
	private static Task<string> OpenNamed(string page, string tab = null) =>
		OpenWith(tab == null ? [("page", page)] : [("page", page), ("tab", tab)]);

	/// <summary>
	/// Right after a match the navigation is rebuilt and the page list is empty for a while
	/// (FINDINGS §8.52); it is waited for rather than reported as no such page. Now and then the
	/// game is laying out its screen when the page is asked for, and refuses to add the panel
	/// ("VisualElements cannot be added to an active visual tree…", FINDINGS §8.78); a moment
	/// later the same request goes through.
	/// </summary>
	private static async Task<string> OpenWith((string, string)[] query)
	{
		if (!await Until(async () => (await Fm.GetJson("pages")).GetArrayLength() > 0, seconds: 5, every: 0.5))
		{
			// A screen of its own — the round's results after a European night, a press
			// conference — takes the menus away until it is moved past.
			var state = await Fm.GetJson("state");
			// In a match the pages wait for it to end; what can be done meanwhile is the match's own.
			if (Field(state, "DuringMatch") == "True")
				return L("error: maç sürerken sayfalar açılamıyor. Maçta yapılabilenler: sub, shout, dugout, tactic role; sürdürmek: match");
			var flow = Field(state, "State");
			return L($"error: oyun menüsüz bir ekranda ({flow}); sayfalar açılamıyor. Geçmek için: next");
		}

		var answer = "";
		for (var attempt = 0; attempt < 3; attempt++)
		{
			answer = await Arriving("open", query);
			if (!answer.Contains("active visual tree", StringComparison.Ordinal)) return answer;
			await Task.Delay(500);
		}
		return answer.Split('\n')[0];
	}

	private static async Task<int> Dropdowns(bool json, string[] args)
	{
		var dropdowns = await Fm.GetJson("dropdowns", ("json", "1"));
		if (json)
		{
			Console.WriteLine(dropdowns.ToString());
			return 0;
		}

		var filter = Words(args).FirstOrDefault();
		var rows = dropdowns.EnumerateArray()
			.Where(d => args.Contains("--all") || Field(d, "Enabled") == "True")
			.Select(d => new[]
			{
				Field(d, "Name"),
				Field(d, "Label"),
				string.Join(" · ", d.GetProperty("Choices").EnumerateArray().Select(c => c.GetString())),
			})
			.Where(r => filter == null || r.Any(cell => cell.Contains(filter, StringComparison.OrdinalIgnoreCase)))
			.ToList();

		Table.Write(new[] { L("LİSTE"), L("SEÇİLİ"), L("SEÇENEKLER") }, rows);
		return 0;
	}

	private static async Task<int> Press(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("kullanım: press \"<etiket>\" [--name <ad>] [--dry]   (etiketler için `buttons`)"));
			return 64;
		}

		Launcher.Activate();
		var query = new List<(string, string)> { ("text", words[0]) };
		if (Option(args, "--name") is { } name) query.Add(("name", name));
		if (args.Contains("--dry")) query.Add(("dry", "1"));

		var answer = await Arriving("press", query.ToArray());
		// A control's own label is the game's English ("Recent Results") while the screen shows
		// this language's word for it ("Son Sonuçlar"); a command typed in the shown words is
		// looked up again in the table (FINDINGS §8.376).
		if (answer.Contains("no control labelled", StringComparison.Ordinal)
		    && GameWords.English(words[0]) is { } english && english != words[0])
		{
			var again = new List<(string, string)> { ("text", english) };
			if (Option(args, "--name") is { } n) again.Add(("name", n));
			if (args.Contains("--dry")) again.Add(("dry", "1"));
			answer = await Arriving("press", again.ToArray());
		}
		return Report(answer);
	}

	private static async Task<int> Select(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count < 2)
		{
			Console.Error.WriteLine(L("kullanım: select <liste> \"<seçenek>\" [--dry]   (listeler için `dropdowns`)"));
			return 64;
		}

		var query = new List<(string, string)> { ("name", words[0]), ("choice", words[1]) };
		if (args.Contains("--dry")) query.Add(("dry", "1"));

		return Report(await Arriving("select", query.ToArray()));
	}

	/// <summary>
	/// Asks again for a short while when the control is not there yet. A screen takes a moment
	/// to build after the press that opened it, and two commands in a row should not have to
	/// know how long.
	/// </summary>
	private static async Task<string> Arriving(string route, (string, string)[] query)
	{
		var answer = "";
		// Not there yet, or the game is still sending what the last press queued: a page that is
		// opening holds the queue for a moment. Either clears by itself or is reported once the
		// wait is up; a name the game's own page list does not have never will, and asking again
		// for it only spends the whole wait (measured: nine requests and nine seconds to the same
		// answer, FINDINGS §8.327).
		await Until(async () => !Unanswered(answer = await Fm.Get(route, query)), seconds: 8, every: 1);
		return answer;
	}

	/// <summary>An answer the game has yet to give: the thing asked for is not drawn yet, or the press that would draw it is still queued.</summary>
	private static bool Unanswered(string answer) =>
		(answer.StartsWith("error: no ", StringComparison.Ordinal)
		 && !answer.StartsWith("error: no page named", StringComparison.Ordinal))
		|| answer.StartsWith("error: the event queue is blocked", StringComparison.Ordinal);

	/// <summary>The bridge answers in plain text and marks a refusal by how the line starts.</summary>
	private static int Report(string answer)
	{
		var failed = answer.StartsWith("error", StringComparison.Ordinal);
		(failed ? Console.Error : Console.Out).WriteLine(answer.TrimEnd());
		return failed ? 1 : 0;
	}

	/// <summary>A command's positional arguments: everything that is neither a flag nor a flag's value.</summary>
	private static IEnumerable<string> Words(string[] args)
	{
		for (var i = 1; i < args.Length; i++)
		{
			if (args[i] is "--for" or "--by" or "--analysts" or "--ability" or "--potential" or "--area" or "--priority" or "--max-value" or "--attr" or "--age" or "--grade" or "--contract" or "--employed" or "--reputation" or "--note" or "--name" or "--tab" or "--view" or "--filter" or "--wage" or "--term" or "--focus" or "--position" or "--role" or "--captain" or "--vice" or "--coach" or "--trait" or "--trait-remove" or "--stat" or "--top") { i++; continue; }
			if (!args[i].StartsWith("--", StringComparison.Ordinal)) yield return args[i];
		}
	}

	private static string Option(string[] args, string flag)
	{
		var at = Array.IndexOf(args, flag);
		return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
	}

	/// <summary>
	/// A message holds the game up when it is a priority one still unanswered. Being answerable
	/// is not enough: some messages only offer something optional, like cancelling a camp.
	/// </summary>
	internal static bool Blocking(JsonElement message) =>
		Field(message, "NewsItemIsPriority") == "True" && Answerable(message);

	private static bool StillAnswerable(JsonElement inbox, string id) =>
		inbox.EnumerateArray().Any(m => Field(m, "UniqueId") == id && Answerable(m));

	internal static bool Answerable(JsonElement message) =>
		Field(message, "CanRespond") == "True" && Field(message, "NewsHasResponded") != "True";

	private static string Flags(JsonElement message)
	{
		var flags = new List<string>();
		if (Field(message, "NewsItemHasBeenRead") != "True") flags.Add(L("okunmadı"));
		if (Blocking(message)) flags.Add(L("cevap bekliyor"));
		else if (Answerable(message)) flags.Add(L("cevaplanabilir"));
		return string.Join(", ", flags);
	}

	/// <summary>
	/// A dialog's answer by its place rather than its words (FINDINGS §8.165): the game puts the
	/// answer that goes ahead (Yes, Confirm, Ok) in the primary slot and the one that backs out (No,
	/// Cancel) in the secondary. The bridge takes the slot from the game's dialog manager; failing
	/// that, the button's element is named for it ("buttons-button-primary-default").
	/// </summary>
	internal static JsonElement? DialogAnswer(JsonElement dialog, bool agree)
	{
		var slot = agree ? "primary" : "secondary";
		var buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
		foreach (var b in buttons) if (Field(b, "Slot") == slot) return b;
		foreach (var b in buttons) if (Field(b, "Name").Contains($"-{slot}-", StringComparison.Ordinal)) return b;
		return null;
	}

	/// <summary>A dialog that asks: one answer goes ahead, another backs out.</summary>
	internal static bool DialogAsks(JsonElement dialog) => DialogAnswer(dialog, true) != null && DialogAnswer(dialog, false) != null;

	private static string Field(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

	private static int Help()
	{
		Console.WriteLine(HelpText);
		return 0;
	}

	/// <summary>The lines of the help that are about one command: its own, and those indented under them.</summary>
	private static int HelpFor(string command)
	{
		var own = false;
		var any = false;
		foreach (var line in HelpText.Split('\n'))
		{
			var indent = line.Length - line.TrimStart().Length;
			if (indent <= 2) own = indent == 2 && (line.TrimStart().Split(' ', '\t')[0] == command || line.TrimStart().StartsWith("fm " + command + " ", StringComparison.Ordinal));
			if (!own || line.Trim().Length == 0) continue;
			Console.WriteLine(line);
			any = true;
		}
		if (!any) Console.WriteLine(L($"{command}: yardım satırı yok; hepsi için: help"));
		return 0;
	}

	/// <summary>Also where tab completion takes each command's description from; in the game's language.</summary>
	private static string HelpText => Labels.Turkish ? HelpTurkish : HelpEnglish;

	private const string HelpTurkish = """
			fm — Football Manager 26'yı terminalden yönet

			  fm start        oyunu başlat, kaydı yükle, oturuma gir   (--menu: kayıt yüklemeden ana menüde bırak)
			    --debug       pencereyi göster (normalde Dock'ta ve ekranda görünmez)
			  fm              oturuma gir (oyun açıkken)
			  stop            oyunu kapat; önce kaydedilsin mi diye sorar (--save / --no-save)

			  continue        açık oyunda son kaydı yükle
			  squad           kadroyu bas
			    --fitness     kondisyon ve maç formu, en yorgun önce
			    --u19         altyapı takımlarının kadroları (U21, U18…)
			    --morale      moral, hiyerarşi ve sosyal grup
			    --stats       sezon istatistikleri: maç, dakika, gol, asist, ortalama puan, kart
			    --views       kadro tablosunun bütün görünümlerini listele
			    --view "<x>"  o görünümün kolonlarını bas (Contract, Injuries, Playing Time…)
			  player <isim>   oyuncu sayfası: kimlik, sözleşme, alışkanlıklar, özellikler, mevki puanları, sakatlık geçmişi (kadro dışıysa aramayla bulur)
			    --actions     oyuncu için yapılabilecekler: teklif, kısa liste, scout, menajer…
			    --report      scout raporu: not, yetenek / potansiyel, ilgisi, değeri, artılar ve eksiler, kadrodaki sırası
			    --career      sezon sezon kariyeri: kulüp, geliş (bonservis / kiralık), maç, gol, puan
			    --matches     bu sezon maç maç: rakip, skor, mevki/rol, girdi/çıktı, xG, gol, asist, puan
			  compare <a> <b> iki oyuncu yan yana: kimlik, sonra özellik özellik, iyisi işaretli (çok kelimeli ad: tırnak ya da "vs")
			  search <metin>  oyunun aramasında bul: oyuncu, kulüp, turnuva…
			  offer <oyuncu> [bonservis]  transfer teklifi: kart olarak gösterir, terminalde okla düzenletir
			    --term "<şart>=<değer>"   bir şartı koy ya da değiştir (ör. "Percentage Of Next Sale=20%",
			                              "Fee=Non-Negotiable"); --loan kiralık, --dry gönderme
			                  geri alınamaz: terminalde sorar, script'te --yes ister
			  targets         transfer hedefleri: kalan bütçe ve kadroda ince mevkiler
			  targets <mevki> [--max-value <para>] [players'ın arama seçenekleri]   o mevkide bütçeye sığan oyuncular ("targets DL", "targets \"sol bek\" --age 18-24")
			  players         oyuncu veritabanı (ekrandaki satırlar)
			    --filters / --filter "<x>"   hazır süzgeçler: Transfer Listed, Contract Expiring…
			    --free                       serbest oyuncular (sözleşmesi bitmiş; "Contract Expired" süzgeci)
			    --clear                      süzgeci kaldır (süzgeç oyunda kalıyor)
			    --views / --view "<x>"       kolonları belirleyen görünümler
			  table [turnuva] lig tablosu; turnuva adıyla o turnuvanın tablosu (ör. table europa)
			  stages [turnuva] [aşama]   kupa turları ve eşleşmeleri, lig aşaması tablosu; dünyadaki her turnuva (--all, --list)
			  scout [#|odak]  scout odakları; bir odağın bulduğu oyuncular (--matched, --near, --progress)
			  scout new [--position <mevki>] [--role <rol>] [--area "<bölge>, …"] [--age 18-23] [--ability <yıldız>] [--potential <yıldız>] [--by "<scout>, …"] [--analysts "<analist>, …"] [--name <ad>] --priority top|standard|ongoing   yeni odak
			  scout edit <#|ad> [--area …] [--age …] [--ability …] [--potential …] [--by …] [--analysts …] [--name …] [--priority …]   var olan odağı değiştirir
			  scout areas [yol] · scout remove <#|ad> [--yes] · scout restart <#|ad>   bölge seçenekleri; odağı durdur / yeniden başlat
			  scout staff     scout ekibi: yeteneği / potansiyeli görme, bildiği bölge ve ülke sayısı, üzerindeki odaklar
			  scout player <ad> [--by <scout>] --for <süre>   oyuncuya scout gönder ("1 hafta", "2 ay", "3 maç", "tam"); scout who <ad>: kimler atanabilir
			  league [turnuva]  liderler: en golcü takım, gol / asist kralı, en yüksek puan (varsayılan: lig)
			  league [turnuva] --stat <ad> [--teams] [--top <n> | --all]   tam sıralama: gol krallığı, asist, xG, form…;  league --stats: bütün istatistikler
			  fixtures        sıradaki maçlar;  --all: sezonun bütün fikstürü
			  calendar [ay]   takvim: o ayın maçları ve önemli tarihleri (transfer dönemi, kura…);  --u19 altyapı maçları da
			  report          son maçın raporu: skor, goller, maçın oyuncusu, istatistikler
			  inbox [--all|--tasks]   gelen kutusu (--all: arşiv; --tasks: görevler)
			  news [#]        basının yazdıkları: haber sitesindeki başlıklar, sayfanın dizdiği sırayla; numarayla haberin tamamı
			  reply <#>       mesajı aç: tam metni ve cevapları; terminalde cevabı okla seçtirir
			  reply <#> <no>  mesajı cevapla (numara ya da cevabın bir parçası)
			  next [n]        zamanı ilerlet (n kez; bir şey engel olunca durur), ne olduğunu söyle
			    --skip        maç sonu açık bir soru varsa cevaplamadan geç
			  match           maç gününü oyna: terminalde izlemek mi anında sonuç mu diye sorar
			    --play        maçı izle: anlatım, goller, sonuç; konuşmalarda sözü sorar
			                  (script'te konuşmada durur: talk <no>, sonra match)
			    --no-talk     konuşmaları geç
			    --instant     maçı oynatma, yardımcılara bırak: oyun birkaç saniyede simüle eder
			  shout [<x>]     maç sırasında kenardan bağır (Encourage, Fire Up, Calm Down…)
			  sub             maç sırasında: sahadakiler ve kulübe;  sub <çıkan> <giren>: oyuncu değiştir
			  dugout          maç sırasında yardımcının tavsiyeleri;  dugout <no> uygula | kaldır
			  resign [--yes]  menajerlikten istifa et (test kariyeri; sonra iş ilanları: jobs)
			  dialog [cevap]  açık onay penceresini göster ya da cevapla
			  talk            açık konuşmayı göster: ne dendi, tepkiler, söyleyebileceklerin
			  talk <no>       söyle (numara ya da cümlenin bir parçası);  talk leave: konuşmadan çık
			  tour            açık tanıtım turunu sonuna kadar yürüt
			    --skip        turu atla (tur izin veriyorsa)
			  contract [oyuncu]  sözleşme görüşmesi kartı: vaatler, sonra şartlar; terminalde okla düzenle
			  contract <oyuncu> --agent  menajerine yenilemeye açık mı diye sor (sonra talk ile)
			  offers          oyuncularımıza gelen, cevap bekleyen teklifler;  offers <# | oyuncu> view | accept | stall | reject
			  propose <oyuncu> [ücret] [--loan] [--yes]  oyuncuyu kulüplere öner (satış ya da kiralık)
			  release <oyuncu> [--free | --mutual] [--yes]  bedelsiz serbest bırak (tazminatı gösterir) ya da karşılıklı fesih
			                  oyuncu verilirse (bizim oyuncumuz) yeni sözleşme görüşmesini açar
			    --term "Şart=değer"  bir şartı ayarla / ekle ("Wage=20K", "Appearance Fee=kaldır",
			                  "Will appoint player as captain", "Tüm teklif=Pazarlıksız")
			    --wage <tutar>  ücret, oyunun maaş döneminde;  --next: vaatleri onayla, şartlara geç
			    --sign --yes  şartları menajere sun / imzala;  --cancel: görüşmeyi iptal et
			  training [oyuncu]  bireysel antrenman: mevki/rol, topsuz rol, odak;  --focus / --position "A>B" / --role
			  training <oyuncu> --report [--coach <ad>]  antrenörden antrenman raporu iste ve göster
			  training <oyuncu> --trait ["<alışkanlık>"]  alışkanlıkları ve öğretilebilecekler; verilirse öğret
			                  --trait-remove "<a>" bıraktır · --trait-cancel · --trait-suggest · --yes: yardımcının itirazına rağmen
			  prep            maç hazırlığı antrenmanı: taktik ve duran top odakları, her birinin seçenekleri
			  prep <topla | topsuz | duran1 | duran2> [odak]   o hazırlığı değiştir ("prep topla progression"); odak yazılmazsa seçenekleri sunar
			  schedule        antrenman programı: bu hafta ve gelecek hafta, gün gün üç seans
			  schedule set <gün> <1|2|3> [<seans>]   bir seansı değiştir; seans yazılmazsa okla seç
			  schedule style <gün> [<program>]       o haftanın programını değiştir (Tactical, Gegenpress…)
			  budget          transfer, maaş ve scouting bütçesi;  budget ayarla [<adım>]: transfer ↔ maaş dağılımı
			  settings        oyun ayarları: maç gösterimi ve hızı, otomatik kayıt, dil, para birimi, birimler
			  settings <ayar> [<değer>]   değiştir (ör. settings vurgu hızı 2x); değer yoksa okla seçtirir
			  happiness       oyuncuların mutluluğu, en mutsuz önce
			    happiness <oyuncu>   neden öyle: moral, soyunma odasındaki yeri, memnun olduğu ve şikâyet ettiği şeyler
			    --why           şikâyeti olan herkes, şikâyetleriyle
			  internationals  milli oyuncular: ülke, toplam milli maç ve gol, bu sezon oynadıkları
			  mentoring       mentorluk grupları ve üyeleri;  --auto: grupları yardımcıya kurdur (grup varsa --yes)
			    new [<o1> <o2> <o3>] [--name "<ad>"]   elle grup kur; oyuncusuz: kimler seçilebilir
			    remove "<grup>" --yes                  grubu dağıt
			  dynamics        soyunma odası: hava, menajere destek, hiyerarşi ve sosyal gruplar
			  contracts       sözleşme takvimi: kimin sözleşmesi ne zaman bitiyor, en yakın önce
			  contracts <oyuncu>  bir oyuncunun sözleşmesi: ücret, statü, primler ve maddeler
			  club            kulüp özeti: yönetim notu ve hedefler, taraftar, kasa ve maaş, tesisler, ekipteki boşluklar
			  staff           kulüp personeli: kim hangi işte, yaşı, maaşı, sözleşmesi; ekiplerdeki eksikler ve yönetimin görüşü
			  finances        finans: kasa, bu ayın gelir/gideri, borç, FFP, oyuncu tipine göre maaş bordrosu, sponsorluk, projeksiyon
			  finances gelir|gider [ay|geçen ay|sezon|geçen sezon]   kalem kalem gelir ya da gider
			  supporters      taraftarlar: hava, beklentiler ve tepkileri, seyirci, kombine, taraftar profili
			  responsibilities [süzgeç]   personel işleri kimde;  delegate "<görev>" devret · take "<görev>" geri al
			  youth           altyapı: notu, tesis ve kadro, A takıma aday görülenler, altyapı alımının tarihi; --training: altyapı takımının antrenman haftası; candidates: o yılın alım adayları (grup, yaş, mevki, yetenek, potansiyel)
			  progress        kim gelişiyor, kim geriliyor: her oyuncunun son dört aylık yeteneği ve değişimi
			  staff find [metin] [--filter <süzgeç>] [--view <görünüm>]   personel veritabanı: görev, sözleşme, maaş, nitelikler
			  staff hire <ad> [--wage <tutar>] [--sign --yes]   personel al: sözleşme görüşmesini açar, `contract` ile sürer
			  staff fire <ad> [--mutual] [--yes]   personelle yolları ayır: oyun tazminatı söyler, --yes olmadan "Hayır" der
			  actions <kişi> ["<seçenek>"]   oyunun o kişi için sağ tık menüsü (scout ata, listeye al, kursa gönder…); seçenek yazılırsa yapılır
			  board           yönetimin ve taraftarın görüşü: maçlar, kadro, taktik, transfer, vizyon; memnun oldukları, endişeleri
			    request ["<talep>" --yes]   yönetimden istenebilecekler; adıyla birini iste
			  doctor          kadroda ne yanlış: sakatlar, yorgunlar, maç formu düşükler, biten sözleşmeler, ince mevkiler, yönetimin endişeleri
			    --install     kurulumu denetle: oyun, BepInEx, gölge paket, eklenti, fm komutu, kayıt klasörü
			  facilities      tesisler: stadyum (kapasite, zemin, ölçüler), antrenman tesisi, altyapı, ticari
			  query "<koşullar>"   kadroda sorgu: "yaş<23 ve potansiyel>140", "sözleşme<1yıl", "mevki=ST ve gol>2";  çıplak hali alanları listeler
			    --sort <alan> / --asc      sıralama (varsayılan: ilk koşulun alanı)
			  health          kadro sağlığı: sakatlar, yorgunlar, maç formu düşükler, sakatlık riski en yüksekler
			  depth           kadro derinliği: her mevkide kimler oynayabiliyor, yaş profili
			  captains        kaptan ve yardımcısı;  --captain <oyuncu> / --vice <oyuncu>
			  vision          sezon sonu kulüp vizyonu toplantısı: hedefler;  confirm | remove <no> | suggest | undo | reset | end
			  penalties       penaltı sırası (soğukkanlılık, penaltı özelliği) ve kim ne zaman bırakır kuralları;  penalties <1|2|3> açık|kapalı
			  penalties move <oyuncu|sıra> <yeni sıra> · add <oyuncu> [<sıra>] · remove <oyuncu|sıra>   sırayı değiştir
			  takers [<tür>]  duran top atıcıları: korner, frikik, kanat, derin, tac; türle birlikte iki liste ayrıntısıyla
			  takers <tür> <sağ|sol> move <oyuncu|sıra> <yeni sıra> · add <oyuncu> [<sıra>] · remove <oyuncu|sıra>   o listenin sırasını değiştir
			  opponent        sıradaki rakip: form, diziliş, gol/asist kralı, kadro haberleri
			  world           yüklü kayıt, son kayıt zamanı, oynanan süre; dünyada kaç ülke / lig / oyuncu yüklü
			  saves           diskteki kayıtlar, en yenisi önce
			    delete "<ad>" --yes   bir kaydı sil (yüklü olan silinmez)
			  load "<ad>"     belirli bir kaydı yükle; oyun bir kayıttaysa kapatıp yeniden açar (--save / --no-save)
			  diff            son bakıştan bu yana ne değişti: lig sırası, kasa, bütçe, gelen/giden, sakatlar, sözleşmeler, maç/gol/asist
			    --keep        bu bakışı kaydetme (karşılaştırma yine öncekiyle);  --reset: yalnızca şimdiki hali kaydet
			  export [dosya.md] [komut…]  komutların çıktısı başlık başlık tek Markdown rapor (varsayılan: durum, kadro,
			                  tablo, fikstür, sağlık, sözleşmeler, finans);  export <dosya.csv> "<komut>": o komutun tablosu
			  --json / --csv / --md   her komutta: tablo yerine JSON, CSV ya da Markdown; soru sorulmaz
			  shortlist       kısa listedeki oyuncular (teklif verilenler de)
			    add <ad> --for <süre> · remove <ad>   listeye al ("3 ay", "1 yıl", "süresiz") / listeden çıkar
			  interest <ad> --top|--transfer|--loan [--yes]   oyuncuya ilgimizi duyur
			  enquire <ad>    kulübüne ne isteyeceğini sor
			  trial <ad> --for <1-4 hafta> [--yes]   oyuncuyu denemeye çağır
			  intensity       oyuncu oyuncu antrenman yoğunluğu;  intensity <oyuncu> <çift|"dinlen 3"|"tatil 1">, --auto [--yes]
			  coaches         antrenör görevleri: kim hangi antrenman alanında, alanların iş yükü ve baş antrenörü
			    coaches <antrenör> <alan> [--off]   ata / görevden al ("coaches Cohen topla taktik");  --auto: yardımcıya bırak
			  opposition      sıradaki rakibe oyuncu oyuncu talimatlar (markaj, pres, sert giriş, ayak)
			    --advice / --clear   yardımcının önerisini uygula / temizle;  opposition <oyuncu> Pres=Always
			  register        turnuva kadro listesi: kim listede, yetişme kotaları, kurallar; terminalde okla ekle/çıkar
			    --in <oyuncu> / --out <oyuncu>   listeye koy / listeden çıkar (tekrarlanabilir)
			    --auto        önce yardımcı seçsin;  --send: gönder (kurallar tutunca)
			  numbers         forma numaraları: kim kaç giyiyor, kim hangisini istedi, boş numaralar
			    numbers <oyuncu> <no>   numara ver (oyun aldığı sürece: sezon başı ya da yeni transfer);  --send: kaydet
			    --auto        yardımcı dağıtsın ve göndersin
			  setpieces new [<no> …]      duran top rutinlerini kur (altı soru; yazmadığını terminalde sorar)
			  setpieces <tür>             rutin: vuruş tipi, hedef, oyuncuların görevleri (korner, frikik, kanat, derin, tac)
			  setpieces <tür> vurus <in|out> | hedef <seçenek> | <yer> <görev> [<olmazsa>]   rutini değiştir
			  tactic          takımın taktiği: stil, mentalite, akıcılık, on bir mevki ve roller
			  tactic mentality [<x>]      mentaliteyi göster ya da değiştir
			  tactic style [<x>]          oyun stilini göster ya da değiştir
			  tactic pick                 ilk 11'i oyuna seçtir (Quick Pick)
			  tactic instructions ["<talimat>" [<değer>]]  takım talimatları: topla (aşama aşama) ve topsuz; değer yoksa seçenekleri
			  tactic pi <mevki|oyuncu> ["<talimat>" [<değer>]]  oyuncu talimatları (şut, koşu, pres, markaj…)
			  tactic slot [<n>]           taktik yuvaları; numarayla o taktiğe geç
			  tactic role <mevki> [<rol>] bir mevkinin rolünü göster ya da değiştir (--oop: topsuz rol)
			                              maç sırasında: tactic role <oyuncu> [<rol>]
			  tactic player <mevki> <oyuncu>  yedeklerden birini o mevkiye koy (oradaki yedeğe geçer)
			  tactic new ["<stil>" ["<topla diziliş>" ["<topsuz diziliş>"]]]
			                  taktik oluştur; eksik bıraktığın adımın seçeneklerini listeler
			  tactic copy [<taktik>]  taktiği yeni yuvaya kopyala · tactic clear [<n>] --yes: yuvayı boşalt
			  tactic save "<ad>" [--note "…"]  taktiği dosyaya kaydet · tactic saved: kaydedilenler · tactic load "<ad>" --yes: etkin yuvaya yükle

			  pages [metin]               oyunun bütün sayfaları
			  open "<sayfa>"              sayfayı aç
			    --tab <sekme>             aynı adda birden çok sayfa varsa
			  buttons [metin]             ekrandaki butonlar ve ne gönderdikleri
			    --all                     etiketsizleri de göster
			  press "<etiket>"            butona bas
			    --name <ad>               aynı etiketten birden çok varsa
			  dropdowns [metin]           ekrandaki açılır listeler ve seçenekleri
			  select <liste> "<seçenek>"  listeden seç
			    --dry                     press ve select için: yapma, ne yapacağını söyle
			  status          tarih ve oyunun şu an neyi beklediği
			  save --yes      oyunu kaydet (dosyanın üzerine yazar, geri alınamaz)
			    --as "<ad>"   başka adla kaydet; sonraki kayıtlar da o dosyaya gider

			  --json          ham veri (script'lenebilir)
			  completion zsh  tab tamamlama: ~/.zshrc'ye `source <(fm completion zsh)`; bash'te `eval "$(fm completion bash)"`

			Kısaltmalar: komutun başı yeter, tek komuta uyuyorsa (sq → squad, fix → fixtures). Kısa adlar:
			s status · n next · i inbox · t table · fx fixtures · m match · p player · tc tactic.
			Türkçe adlar da olur, aksansız da: kadro, taktik, maç, tablo, fikstür, gelen, ileri, sağlık, yönetim…
			  fm mcp          komutları MCP araçları olarak sun (stdio); bir modelin oynaması için

			Oturumda komutları `fm` yazmadan verirsin; çıkmak için `quit`.
			""";

	private static int Unknown(string command)
	{
		Console.Error.WriteLine(L($"bilinmeyen komut: {command} — komutlar için `help`"));
		return 64; // EX_USAGE
	}
}
