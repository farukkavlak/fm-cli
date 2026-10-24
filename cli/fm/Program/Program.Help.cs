using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static string Field(JsonElement element, string name) =>
		JsonRead.Text(element, name);

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
