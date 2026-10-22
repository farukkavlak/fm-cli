# fm-cli

Gerçek **Football Manager 26**'yı terminalden oynat.

```
$ fm inbox
#  SAAT   GÖNDEREN          KONU
1  08:05  Sadettin Saran    Welcome to Fenerbahçe!                      okunmadı
4  08:13  Sadettin Saran    Club vision and expectations meeting        okunmadı, cevap bekliyor
...
4 mesaj cevap bekliyor — bunlar `fm next`'i blokluyor. Cevaplamak için: reply <#>

$ fm reply 4
  1  Negotiate
  2  Accept Current Vision
  3  View Club Vision

Seçim (1-3, x: vazgeç): 2
Cevaplandı: Club vision and expectations meeting → Accept Current Vision

$ fm squad
OYUNCU          POZİSYON         YAŞ  STATÜ         MAAŞ         DEĞER          DURUM
Anthony Musaba  AM (RL), ST (C)  24   Squad Player  £14.25K p/w  £2M - £4.9M
Ederson         GK               31   Star Player   £230K p/w    £56M - £62M
...

$ fm squad --fitness
OYUNCU             POZİSYON         KONDİSYON  MAÇ FORMU  DURUM
Talisca            AM (RC), ST (C)  -          35%        Inj
Dominik Livaković  GK               98%        82%
...
```

Oyunu sıfırdan yazmıyoruz — gerçek FM26'nın içine bir BepInEx plugin'i enjekte edip
oyunun kendi veri katmanını (`SI.Bindable.Bindings`) ve olay sistemini CLI'a açıyoruz.
Oyun arka planda açık kalır; ekran, fare, ekran görüntüsü yok.

**Durum:** kadro (kondisyon ve moral dahil), gelen kutusu, mesaj cevaplama ve zaman ilerletme
çalışıyor. Ekrandaki her buton ve açılır liste plugin üzerinden listelenip kullanılabiliyor.

📖 Ekran görüntülü kullanım kılavuzu: **[docs/manual/KILAVUZ.md](docs/manual/KILAVUZ.md)**
📄 Bütün teknik bulgular, API'ler ve kısıtlar: **[docs/FINDINGS.md](docs/FINDINGS.md)**
📋 Yol haritası: **[PLAN.md](PLAN.md)**

## Yapı

```
plugin/FmCliProbe/        FM26 içinde çalışan plugin (C#, BepInEx 6 IL2CPP)
cli/fm/                   `fm` komutu (.NET)
tools/sigdump/            interop'un gerçekten neyi açtığını raporlayan araç
docs/reference/           oyundan çıkarılmış veri modeli ve event kataloğu
```

## Komutlar

Tam liste `fm help`; aşağıdaki onun çıktısı. Kullanım örnekleri `docs/manual/KILAVUZ.md`.

```
fm start        oyunu başlat, kaydı yükle, oturuma gir
  --debug       pencereyi göster (normalde Dock'ta ve ekranda görünmez)
fm              oturuma gir (oyun açıkken)

continue        açık oyunda son kaydı yükle
squad           kadroyu bas
  --fitness     kondisyon ve maç formu, en yorgun önce
  --u19         altyapı (U19) kadrosu
  --morale      moral, hiyerarşi ve sosyal grup
  --stats       sezon istatistikleri: maç, dakika, gol, asist, ortalama puan, kart
  --views       kadro tablosunun bütün görünümlerini listele
  --view "<x>"  o görünümün kolonlarını bas (Contract, Injuries, Playing Time…)
player <isim>   oyuncu sayfası: kimlik, sözleşme, alışkanlıklar, özellikler, mevki puanları, sakatlık geçmişi (kadro dışıysa aramayla bulur)
  --actions     oyuncu için yapılabilecekler: teklif, kısa liste, scout, menajer…
  --career      sezon sezon kariyeri: kulüp, geliş (bonservis / kiralık), maç, gol, puan
search <metin>  oyunun aramasında bul: oyuncu, kulüp, turnuva…
offer <oyuncu> [bonservis]  transfer teklifi: kart olarak gösterir, terminalde okla düzenletir
  --term "<şart>=<değer>"   bir şartı koy ya da değiştir (ör. "Percentage Of Next Sale=20%",
                            "Fee=Non-Negotiable"); --loan kiralık, --dry gönderme
                geri alınamaz: terminalde sorar, script'te --yes ister
players         oyuncu veritabanı (ekrandaki satırlar)
  --filters / --filter "<x>"   hazır süzgeçler: Transfer Listed, Contract Expiring…
  --clear                      süzgeci kaldır (süzgeç oyunda kalıyor)
  --views / --view "<x>"       kolonları belirleyen görünümler
table           lig tablosu
fixtures        sıradaki maçlar;  --all: sezonun bütün fikstürü
calendar [ay]   takvim: o ayın maçları ve önemli tarihleri (transfer dönemi, kura…);  --u19 altyapı maçları da
report          son maçın raporu: skor, goller, maçın oyuncusu, istatistikler
inbox           gelen kutusu
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
dialog [cevap]  açık onay penceresini göster ya da cevapla
talk            açık konuşmayı göster: ne dendi, tepkiler, söyleyebileceklerin
talk <no>       söyle (numara ya da cümlenin bir parçası);  talk leave: konuşmadan çık
tour            açık tanıtım turunu sonuna kadar yürüt
  --skip        turu atla (tur izin veriyorsa)
contract [oyuncu]  sözleşme görüşmesi kartı: vaatler, sonra şartlar; terminalde okla düzenle
contract <oyuncu> --agent  menajerine yenilemeye açık mı diye sor (sonra talk ile)
release <oyuncu> [--free | --mutual] [--yes]  bedelsiz serbest bırak (tazminatı gösterir) ya da karşılıklı fesih
                oyuncu verilirse (bizim oyuncumuz) yeni sözleşme görüşmesini açar
  --term "Şart=değer"  bir şartı ayarla / ekle ("Wage=20K", "Appearance Fee=kaldır",
                "Will appoint player as captain", "Tüm teklif=Pazarlıksız")
  --wage <tutar>  haftalık ücret;  --next: vaatleri onayla, şartlara geç
  --sign --yes  şartları menajere sun / imzala;  --cancel: görüşmeyi iptal et
training [oyuncu]  bireysel antrenman: mevki/rol, topsuz rol, odak;  --focus / --position "A>B" / --role
training <oyuncu> --report [--coach <ad>]  antrenörden antrenman raporu iste ve göster
training <oyuncu> --trait ["<alışkanlık>"]  alışkanlıkları ve öğretilebilecekler; verilirse öğret
                --trait-remove "<a>" bıraktır · --trait-cancel · --trait-suggest · --yes: yardımcının itirazına rağmen
prep            maç hazırlığı antrenmanı: taktik ve duran top odakları
schedule        antrenman programı: bu hafta ve gelecek hafta, gün gün üç seans
schedule set <gün> <1|2|3> [<seans>]   bir seansı değiştir; seans yazılmazsa okla seç
schedule style <gün> [<program>]       o haftanın programını değiştir (Tactical, Gegenpress…)
budget          transfer, maaş ve scouting bütçesi;  budget ayarla [<adım>]: transfer ↔ maaş dağılımı
settings        oyun ayarları: maç gösterimi ve hızı, otomatik kayıt, dil, para birimi, birimler
settings <ayar> [<değer>]   değiştir (ör. settings vurgu hızı 2x); değer yoksa okla seçtirir
happiness       oyuncuların mutluluğu, en mutsuz önce
contracts       sözleşme takvimi: kimin sözleşmesi ne zaman bitiyor, en yakın önce
club            kulüp özeti: yönetim notu ve hedefler, taraftar, kasa ve maaş, tesisler, ekipteki boşluklar
staff           kulüp personeli: kim hangi işte, yaşı, maaşı, sözleşmesi; ekiplerdeki eksikler ve yönetimin görüşü
finances        finans: kasa, bu ayın gelir/gideri, borç, FFP, oyuncu tipine göre maaş bordrosu, sponsorluk, projeksiyon
finances gelir|gider [ay|geçen ay|sezon|geçen sezon]   kalem kalem gelir ya da gider
supporters      taraftarlar: hava, beklentiler ve tepkileri, seyirci, kombine, taraftar profili
board           yönetimin ve taraftarın görüşü: maçlar, kadro, taktik, transfer, vizyon; memnun oldukları, endişeleri
doctor          kadroda ne yanlış: sakatlar, yorgunlar, maç formu düşükler, biten sözleşmeler, ince mevkiler, yönetimin endişeleri
facilities      tesisler: stadyum (kapasite, zemin, ölçüler), antrenman tesisi, altyapı, ticari
health          kadro sağlığı: sakatlar, yorgunlar, maç formu düşükler, sakatlık riski en yüksekler
depth           kadro derinliği: her mevkide kimler oynayabiliyor, yaş profili
captains        kaptan ve yardımcısı;  --captain <oyuncu> / --vice <oyuncu>
opponent        sıradaki rakip: form, diziliş, gol/asist kralı, kadro haberleri
saves           diskteki kayıtlar, en yenisi önce
shortlist       kısa listedeki oyuncular (teklif verilenler de);  add <ad> --for <süre> · remove <ad>
scout new [--position <mevki>] [--role <rol>] [--area "<bölge>, …"] [--age 18-23] [--name <ad>] --priority <öncelik>   yeni scout odağı
scout areas [yol] · scout remove <#|ad> · scout restart <#|ad>   bölge seçenekleri; odağı durdur / yeniden başlat
scout staff     scout ekibi: gözleri, bildikleri yer, üzerlerindeki odaklar
scout player <ad> [--by <scout>] --for <süre>   oyuncuya scout gönder;  scout who <ad>: atanabilecek scoutlar
interest <ad> --top|--transfer|--loan · enquire <ad> · trial <ad> --for <hafta>   ilgi duyur, kulübe sor, denemeye çağır
intensity       oyuncu oyuncu antrenman yoğunluğu;  intensity <oyuncu> <çift|"dinlen 3"|"tatil 1">, --auto
opposition      sıradaki rakibe oyuncu oyuncu talimatlar (markaj, pres, sert giriş, ayak)
  --advice / --clear   yardımcının önerisini uygula / temizle;  opposition <oyuncu> Pres=Always
register        turnuva kadro listesi: kim listede, yetişme kotaları, kurallar; terminalde okla ekle/çıkar
  --in <oyuncu> / --out <oyuncu>   listeye koy / listeden çıkar (tekrarlanabilir)
  --auto        önce yardımcı seçsin;  --send: gönder (kurallar tutunca)
numbers         forma numaralarını gönder (aynı şekilde)
setpieces new [<no> …]      duran top rutinlerini kur (altı soru; yazmadığını terminalde sorar)
tactic          takımın taktiği: stil, mentalite, akıcılık, on bir mevki ve roller
tactic mentality [<x>]      mentaliteyi göster ya da değiştir
tactic style [<x>]          oyun stilini göster ya da değiştir
tactic pick                 ilk 11'i oyuna seçtir (Quick Pick)
tactic role <mevki> [<rol>] bir mevkinin rolünü göster ya da değiştir (--oop: topsuz rol)
                            maç sırasında: tactic role <oyuncu> [<rol>]
tactic player <mevki> <oyuncu>  yedeklerden birini o mevkiye koy (oradaki yedeğe geçer)
tactic new ["<stil>" ["<topla diziliş>" ["<topsuz diziliş>"]]]
                taktik oluştur; eksik bıraktığın adımın seçeneklerini listeler
tactic copy [<taktik>]  taktiği yeni yuvaya kopyala · tactic clear [<n>] --yes: yuvayı boşalt

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

--json          ham veri (script'lenebilir)
completion zsh  tab tamamlama: ~/.zshrc'ye `source <(fm completion zsh)`; bash'te `eval "$(fm completion bash)"`

Kısaltmalar: komutun başı yeter, tek komuta uyuyorsa (sq → squad, fix → fixtures). Kısa adlar:
s status · n next · i inbox · t table · fx fixtures · m match · p player · tc tactic.
Türkçe adlar da olur, aksansız da: kadro, taktik, maç, tablo, fikstür, gelen, ileri, sağlık, yönetim…
fm mcp          komutları MCP araçları olarak sun (stdio); bir modelin oynaması için

Oturumda komutları `fm` yazmadan verirsin; çıkmak için `quit`.
```

Oturumda komutlar `fm` öneki olmadan verilir:

```
$ fm start
FM başlatılıyor…
Son kayıt yükleniyor…
Hazır.
fm› next
Bloke: 5 mesaj cevap bekliyor.
  08:13  Club vision and expectations meeting
  ...
```

`fm start` oyun zaten açıksa ona bağlanır; kayıt yüklü değilse yükler.

Oyun **Dock'ta ve ekranda görünmez**: bundle accessory olarak işaretlenir, pencereli
başlatılır ve küçültülür. Veri bu haldeyken de akar. `--debug` üçünü de atlar.
Gerçekten headless (`-nographics`) çalışmıyor — UI olmadan binding ağacı kurulmuyor
(`docs/FINDINGS.md` §8.19). Küçültme Erişilebilirlik izni ister; verilmezse `fm` bunu söyler.

## Gereksinimler

- macOS, **Apple Silicon** (Intel kararı henüz verilmedi)
- Football Manager 26 (Steam)
- BepInEx 6 IL2CPP arm64 — oyun klasörüne bir kez kurulur: `docs/FINDINGS.md` §3
- `~/fm26_bep/fm.app` — Steam kopyasından hazırlanan gölge paket (Info.plist yaması; `fm doctor --install` nerede eksik olduğunu söyler)
- Derlemek için .NET SDK; **hazır `dist/fm/fm` için SDK gerekmez**

## Kur

Tek komut (BepInEx ve gölge paket zaten hazırsa):

```bash
tools/install.sh
```

Yaptıkları: gölge paket yoksa oluşturur (`tools/make-shadow.sh`), eklentiyi derleyip
BepInEx'in plugin klasörüne kopyalar, `fm`yi tek dosya olarak yayınlar (`dist/fm/fm`,
SDK'sız), `~/.local/bin/fm` bağlar ve kurulumu denetler.
BepInEx ve gölge paket **dağıtılmıyor** (lisans); eksikse script nerede olduğunu söyler.

Elle:

```bash
# eklenti
dotnet build plugin/FmCliProbe/FmCliProbe.csproj -c Release
cp plugin/FmCliProbe/bin/Release/net6.0/FmCliProbe.dll \
   "$HOME/Library/Application Support/Steam/steamapps/common/Football Manager 26/BepInEx/plugins/FmCliProbe/"

# cli: ya bağla ya da SDK'sız tek dosya üret
dotnet build cli/fm -c Release
ln -sf "$PWD/cli/fm/bin/Release/net10.0/fm" ~/.local/bin/fm
# veya: tools/publish.sh  → dist/fm/fm (osx-arm64, kendi kendine yeter)

# denetle
fm doctor --install
```

`fm doctor --install` yedi adımı tek tek söyler: oyun, BepInEx çekirdeği, başlatıcı,
gölge paket, eklenti, `fm` komutu, kayıt klasörü. Eksik olanın yanına ne yapılacağını yazar;
`--json` ile makine okunur hâli de var.

## Referans dosyaları

`docs/reference/` oyundan çıkarılmış, elle yazılmamış iki katalog içeriyor:

| Dosya | İçerik |
|---|---|
| `COOKBOOK.md` | **buradan başla** — taktik, transfer, basın, konuşma, antrenman reçeteleri |
| `data-model.txt` | 931 referans tipi, 14504 isimli property, referans kenarlarıyla |
| `events.txt` | 275 UI event tipi, alanlarıyla |
| `actions.txt` | 225 `ActionID` + 680 `InteropEventIDs` — simülasyonun aksiyon yüzeyi |
| `enums.txt` | 2160 enum, 19119 üye — roller, direktifler, mentalite, formasyonlar |
| `panels.txt` | yüklü ekranlar, çevrilmiş isimleriyle (kısmi) |

Yenilemek için: oyun açıkken `curl localhost:7777/schema` ve `/panels`; oyun gerekmeden
`dotnet run --project tools/sigdump -- --catalog events` / `--enums` / `--method <ad>`.
