# fm-cli — Yol Haritası

> FM26'da yapılabilen her şeyi CLI komutuna çevirme listesi.
> Durum: `[x]` çalışıyor · `[~]` kısmen · `[k]` katalogda var, canlı denenmedi · `[ ]` yapılacak ·
> `[?]` yapılabilirliği belirsiz

`[k]` şu demek: verisi ve event'i metadata'dan çıkarıldı (`docs/reference/COOKBOOK.md`), yani
keşif bitti, geriye canlı doğrulama ve komutu yazmak kaldı. İşaretsiz `[ ]` ise katalogda henüz
aranmadı demek, yok demek değil.

---

## Sıradaki işler (2026-10-07)

Sıra, "en çok şeyin önünü açan önce" mantığıyla. Her adım bitince buradan silinip aşağıdaki
ilgili bölümde `[x]` olur, günlüğe bir satır düşülür.

Bütün adımların ortak aracı artık `/buttons` + `/press` (§8.25): ekrandaki her kontrol hangi
event'i hangi veriyle göndereceğini kendisi söylüyor ve kendi listesi kuyruğa konarak
"basılıyor". Yeni bir alan açmak = `fm open "<sayfa>"`, `fm buttons` ile bak, `fm press` ile dene.
77 sayfanın listesi `docs/reference/screens.txt`'te.

**Okuma için artık ekran gerekmiyor** (§8.40): `/pull?path=<yol>&props=<a,b>[&list=1]`. Hangi
referansın hangi property'si olduğu `/schema`'da. Yeni bir okuma komutu önce buradan denenmeli;
ekran açıp bağlanan veriyi beklemek yalnızca tıklama gereken yerlerde.

0. **UX ve görselleştirme** — yön kararı (yukarıda "Ürün kararları", ayrıntı §14). Büyük kısmı
   7 Ekim gecesi yapıldı (günlük). Kalan: maç ekranında on bir ve kulübe, fikstür renkleri,
   form. Otomatik kayıt ve maç hızı `fm settings`'te (9 Ekim gecesi).
1. **Maç içi yönetimin kalanı.** `fm sub` oyuncu değiştiriyor (§8.34). Kalan: sakatlık
   durağını `fm match`'in kendisinin geçmesi (sub + "Confirm Changes"), maç içi mentalite / rol /
   shout (hepsi alt menülü listeler, seçim yolu belli), "Dugout" tavsiyelerini okumak, maç hızı.
   Bire bir oyuncu konuşması henüz çıkmadı; `fm talk` aynı şablonu bekliyor.
2. **`fm tactic` kalanı** — oluşturma, okuma, mentalite, stil, rol ve Quick Pick tamam (§8.30,
   §8.31). Kalan: diziliş değiştirme, takım talimatları, kadro dışından oyuncu alma
   (`fm tactic player` yalnızca yedekleri adıyla bulabiliyor).
3. **Ucuz okuma komutları:** `fm table`, `fm fixtures`, `fm player` çıktı (§8.37, §8.38); kalan
   maç sonu raporu (istatistikler devre arası ekranında bağlı, §8.34), tam fikstür ve sonuçlar.
   Sayfalar artık adıyla açılıyor (`fm open`), yani her biri "aç, şekle göre oku". Kadro tablosunun diğer görünümleri (Contract, Transfer, Injuries,
   Playing Time, Development…) aynı mekanizmayla hazır: görünümü seç, satırı oku.
4. **Transfer ve sözleşme** — okuma tarafı başladı: `fm players` veritabanını hazır süzgeçlerle
   listeliyor (§8.38). `fm search` ve `fm player` herhangi bir oyuncuyu buluyor. `fm offer`
   teklifi kart olarak çiziyor ve şart şart düzenletiyor: ek ödeme ve madde ekleme / kaldırma,
   her şartın pazarlık kilidi (Negotiable · Semi · Non · Remove & Exclude), karşı teklifi de
   açıyor (§8.42, 7 Ekim akşamı). Kalan:
   - Alt menü seçimi 8 Ekim gecesi çözüldü (§8.45): kupa / ödül primleri, takasa oyuncu, alt
     menülü sözleşme maddeleri; kartta kademeli menü, script'te `--term "A>B>C=değer"`.
   - Kiralık teklif kartla çalışıyor ve gönderildi (§8.46); kulübün cevabı bekleniyor.
   - Kabul edilen bir teklifin "Accept" tuzağını kart üzerinden göstermek; kısa liste.
   - `fm contract` kartı 7→8 Ekim gecesi yapıldı (§8.44): vaatler + şartlar, yenileme, menajerin
     karşı teklifi, kuralın neden tutmadığı. Yenilemede oyuncunun kararı gelen kutusuna düşüyor;
     o mesajın cevabı henüz denenmedi.
5. **MCP'yi gerçek istemciyle denemek.** 8 Ekim gecesi Claude Code'dan çağrıldı (`fm_status`,
   `fm_tactic`), çalışıyor; sunucu süreci eski derlemeyi tuttuğu için yeni araçlar `/mcp` ile
   yeniden başlatınca gelir (§8.55). Ardından: araç çıktısındaki
   yönlendirmeleri araç adına çevirmek, uzun maç için zaman aşımı, `--json` her komutta.

### Bilinen hatalar (kılavuz çekimi sırasında bulundu, 2026-10-07)

- ~~Basın mesajındaki ara soru~~ — 10 Ekim akşam yeniden görüldü ve anlaşıldı: maç sonrası basın mesajı bir gazeteci
  sorusuyla birlikte geliyor; "Send Assistant" kaydediliyor, ardından oyun cevapsız kalan soruyu konuşma olarak soruyor
  (`talk` ile cevaplanınca mesaj kapanıyor). Hata değil; `reply` artık iki grubu ayrı başlıkla listeliyor.

- ~~Tek isimli oyuncu açılmıyor~~ — `fm player` arama yedeğiyle (25603ca) çözülmüş: 10 Ekim akşam `fm propose fred`
  Fred'in sayfasını açtı.

- ~~`fm players` dar terminalde okunmuyor~~ — 8 Ekim gecesi: kolonlar önceliğe göre sığdırılıyor,
  ham değerler okunur, sakat kırmızı. ~~`reply` numarası kayıyor~~ — numara son `inbox`'taki
  mesajın kimliğine bağlandı. Eski not:
- **`fm players` dar terminalde okunmuyor.** 110 sütunda 15 kolonun hepsi basılıyor; isimler
  harf harf kırılıyor ("Abdelh / amid / Sabiri"), "Scouting Required" dört satıra bölünüyor,
  `IS INJURED` True/False yazıyor. Terminale sığacak kolonları seçmeli, ham değerleri
  çevirmeli (sakat → kırmızı isim, `squad` gibi).
- **`fm reply <#>` bir kez yanlış mesajı açtı.** `reply 19` 18'i ("Training Review") açtı;
  hemen ardından 7 denemede hep doğruydu. Önceki komutlar başka sayfa açmıştı (`player`,
  `search`); gelen kutusu listesi bayat okunmuş olabilir. Seyrek, ama yanlış mesaja cevap
  vermek geri alınamaz: açtıktan sonra başlığı doğrulamalı. 7 Ekim akşamı bir sebebi görüldü:
  gelen kutusu iki `inbox` arasında 18 mesajdan 16'ya indi (okunmuş eski mesajlar düştü),
  `reply 17` ilk seferde doğru mesajı okudu, cevap vermek için çağrılınca "17 numaralı mesaj
  yok" dedi. Numara komutlar arasında kayıyor; numarayla birlikte başlık da tutulmalı.

---

## Ürün kararları (Faruk, 2026-10-06)

Geliştirme değil, yön. Sıradaki işler bunlara göre şekillenecek.

- **Otomatik kayıt bir ayar.** Kullanıcı açıp kapatacak (§12); bizim varsayılan seçmemiz
  gerekmiyor.
- **Maçı oynatmak da geçmek de seçenek.** Hangisi varsayılan diye bir karar yok; ikisi de
  kullanıcıya sunulacak (`fm match` her seferinde sormalı ya da ayardan okumalı).
- **Konuşmada cümleyi kullanıcı seçer, oyundaki gibi.** "İlk cümleyi söyle" anlamsız:
  `fm match --talk` bu haliyle kalkacak. Maç akışı konuşma durağına gelince seçenekleri
  gösterip seçtirmeli (`fm talk`'ın yaptığı gibi), konuşmadan geçmek de bir seçenek olmalı.
- **Projenin asıl işi UX.** Komutların çalışması yetmiyor; adam CLI'dan gerçekten kolay ve
  akıcı oynayabilmeli. Yoksa projenin anlamı yok.
- **Görselleştirme şart.** 4-3-3 terminalde 4-3-3 gibi görünmeli; değişiklik yapınca saha
  üstünde görülmeli. Tablo dökmek yerine CLI ölçeğinde çizim. Bunun için hazır, güçlü terminal
  arayüz kütüphaneleri var; araştırılıp biri seçilecek (bkz. §14 "Görselleştirme").

---

## 0. Altyapı

- [x] BepInEx arm64'te ayağa kalkıyor (Apple Silicon)
- [x] Plugin oyunun içinde çalışıyor
- [x] Oyun içinde HTTP server (`localhost:7777`)
- [x] `GameSubsystems` üzerinden `Bindings` + `EventSubsystem` erişimi
- [x] Ağaçta gezinme, düğüm okuma, tam yol raporlama
- [x] `Application.runInBackground` — terminale geçince oyun donmuyor
- [x] `tools/sigdump` — interop'un gerçekten neyi açtığını raporlayan araç
- [x] `fm` CLI binary
- [x] Oyunu gizli başlatma — Dock'ta yok, ekranda yok (`fm start`, §8.19)
- [x] Plugin ↔ CLI protokolü: JSON çıktı (`--json`)
- [x] Hata yönetimi: oyun kapalıysa/yükleniyorsa anlamlı mesaj
- [x] Veri modeli kataloğu — 931 referans tipi, 14504 property (`data-model.txt`, §8.11)
- [x] Aksiyon kataloğu — 275 UI event, 680 `InteropEventIDs`, 225 `ActionID`, 2160 enum (§8.23)
- [x] Genel event göndericisi `/send` — referans, enum, `TypedValue`, `PanelID` alanları isimle (§8.24)
- [x] Kuru çalıştırma (`dry=1`) ve çözülemeyen alanda göndermeme (§8.24)
- [x] Ekranı isimle açma; her komut kendi ekranını geri getiriyor (§8.16, §8.22)
- [x] Veri bağlanana kadar bekleme (sabit gecikme yerine 20 sn'ye kadar yoklama)
- [x] Ekrandaki kontrolleri event'leriyle listeleme (`/buttons`) ve basma (`/press`) (§8.25)
- [x] `fm buttons`, `fm press`, `fm dropdowns`, `fm select` — ekranı CLI'dan gezmek (§8.27)
- [x] Ekran haritası ve sayfayı adıyla açma — `fm pages`, `fm open "<sayfa>"`, 77 sayfa (§8.28)
- [x] Sayfayı verisi bağlanacak şekilde açma — sekmenin ilk sayfası + alt sekme butonu (§8.31)
- [ ] Yalnızca "More" menüsünde olan sayfalar hâlâ bookmark'la açılıyor; verileri bağlanmayabilir
- [x] Kuyruk denetimi — `/queue`, tıkalıyken `/press` reddediyor (§8.27)
- [x] Açılır listeleri seçenekleriyle listeleme (`/dropdowns`) ve seçme (`/select`) (§8.26)
- [x] Kontrolü etiketle, listeyi adıyla adresleme — adresler ekran değişince bayatlıyor
- [x] Simülasyon event'ini canlı gönderme — `EventToPlugin`, oyunun kendi doldurucusuyla (§8.25)
- [ ] Ekran açmadan tek property canlandırma — kanal açılıyor, düğüm yaratılamıyor (§8.21)
- [x] Radyo gruplarını listeleme ve seçme — `/dropdowns` + `/select` (§8.30)
- [x] Etiketin etrafındaki eleman ağacı — `/elements` (§8.30)
- [x] Metin kutusuna yazma — `/textboxes`, `/type` (§8.38); "Edit Search" ve "farklı kaydet"te denenmedi
- [x] İsimle arama — `fm search <metin>`; `fm player` kadro dışındaki oyuncuyu aramayla açıyor (§8.38)
- [ ] FM27'ye taşıma: sürüme özel her şey tek config dosyasında

---

## 1. Oyun yaşam döngüsü

- [x] Oyunu başlat (`fm start`)
- [x] **Son kaydı yükle** (`LoadSelectedGameEvent`)
- [x] Kayıtları listele (`fm saves`)
- [x] Belirli kaydı yükle — `fm load "<ad>" [--save|--no-save]`; kayıttayken oyunu yeniden başlatıyor (§8.104); `fm start --menu`
- [x] Kaydet — `fm save --yes`, dosyanın üzerine (§8.35)
- [x] Farklı kaydet — `fm save --as "<ad>" --yes` (§8.104)
- [x] Kaydı sil — `fm saves delete "<ad>" --yes`; yüklü kayıt silinmez (§8.104)
- [x] Oyunu kapat — `fm stop` (önce kaydedilsin mi diye sorar; `--save` / `--no-save`)
- [x] Otomatik kaydetme ayarı — `fm settings` (§8.61); ⚠ açıkken ana dosyanın üzerine yazıyor (§8.35)
- [ ] Bulut kayıt durumu
- [?] Kariyer **oluşturma** (çok adımlı sihirbaz — lig seçimi, veritabanı boyutu, menajer profili)
- [?] Menajer profili düzenleme (isim, doğum tarihi, uyruk, görünüm)

---

## 2. Kadro ve oyuncu

- [x] Kadro canlandı — isim, pozisyon, yaş, uyruk, ortalama rating (§8.16)
- [x] `fm squad` — A takım: pozisyon, yaş, statü, maaş, piyasa değeri, durum ("General Info")
- [x] `fm squad --u19` — altyapı kadrosu, kadro tablosundaki U19 satırlarından
- [x] `fm squad --fitness` — kondisyon ve maç formu, "Selection Info" görünümünden (§8.26)
- [x] `fm squad --morale` — moral, hiyerarşi, sosyal grup, "Dynamics" görünümünden (§8.26)
- [x] Kadro tablosunun bütün görünümleri — `fm squad --views`, `fm squad --view "<ad>"`; kolonlar
      veriden çıkıyor (§8.38). Alt menülü ikisi (Attributes, Stats) seçilemiyor
- [~] `/squad` iki listeyi birleştirirken bir oyuncunun kolonlarını eksik bırakabiliyor (Musaba) — olası neden düzeltildi (boş alan öbür listeden doluyor), hata yeniden görülmedi (§8.86)
- [x] Uyruk — `fm squad` terminal görünümünde ULUS sütunu (oyuncunun `Nation.ThreeLetterName`, satırının altından)
- [x] Turnuva kadro listesi — `fm register`: kim listede, yetişme kotaları, kurallar; okla ekle / çıkar, gönder (§8.43)
- [~] Forma numaraları — `fm numbers` yardımcıya verdiriyor; elle numara verme yok
- [x] `fm player <isim>` — kimlik, sözleşme, alışkanlıklar, 36 özellik (§8.38); kadro dışındakiler aramayla
- [x] Öznitelikler (teknik / zihinsel / fiziksel / duran top; kalecide kalecilik)
- [~] Gizli öznitelikler (scout görüşü kadarıyla) — kartta kulübün gördüğü potansiyel (`PerceivedPotentialAbility`, /200); kişilik özellikleri, tutarlılık vb. `/pull` ile gelmiyor (§8.85); kişilik `fm dynamics`'te (§8.95)
- [x] Pozisyon yetkinlikleri — `fm player` mevki başına 1–20 puanı veriyor (§8.80)
- [x] Sakatlık durumu — `fm player` sakatlığı ve geçmişi (§8.80), `fm squad --view Injuries` listeyi veriyor
- [x] Sözleşme — maaş, bitiş tarihi, oynama süresi `fm player`'da; primler ve maddeler `fm contracts <oyuncu>` (§8.96)
- [x] Form — `fm squad --fitness` maç formunu, `fm squad --stats` sezon istatistiklerini (§8.80), `fm player <ad> --matches` maç maç puanları veriyor (§8.84)
- [x] Kariyer geçmişi, transfer geçmişi — `fm player <isim> --career` (§8.63)
- [x] Gelişim grafiği / potansiyel — potansiyel kartta (§8.85); son dört aylık yetenek kartta ve `fm progress` tablosunda (§8.113)
- [~] Milli takım — `fm internationals`: ülke, toplam milli maç / gol, bu sezon oynadıkları (§8.101); davet / kamp durumu yok (milli arada bakılacak)
- [~] Oyuncu mutluluk durumu — `fm happiness` (§8.59); şikâyetlerin nedeni yok
- [x] Kadro derinliği analizi (pozisyon bazlı) — `fm depth`, mevki dizgilerinden
- [x] Yaş profili — `fm depth`'in altında

---

## 3. Taktik

> Katalog: COOKBOOK §1. Yazma tarafının ana event'i `ModifyTacticEvent`.

- [x] Taktik oluştur — `fm tactic new "<stil>" "<topla>" "<topsuz>"`, oyunun sihirbazıyla (§8.30)
- [x] Mevcut taktiği göster — `fm tactic`: stil, mentalite, 11 mevki, top/topsuz roller, oyuncular (§8.31)
- [x] Mentalite ve stil değiştir — `fm tactic mentality <x>`, `fm tactic style <x>` (§8.31)
- [x] İlk 11'i oyuna seçtir — `fm tactic pick` (Quick Pick) (§8.31)
- [x] Formasyon değiştir — oyunda yeni taktik kurmakla oluyor: `fm tactic new` taktik varken yeni
      yuvaya kurar, `fm tactic slot [<n>]` üç yuva arasında geçer (§8.47); yuva boşaltma yok
- [k] **In Possession / Out of Possession** ayrı şekiller — aynı event, `IsInPossession` bayrağı
- [x] Oyuncu rolleri — `fm tactic role <mevki> [<rol>] [--oop]` (§8.31)
- [~] Oyuncu talimatları — `fm tactic pi <mevki>`: topla oynarken talimatlar (§8.53); topsuz ve
      "oyuncuya özel" yok
- [~] Takım talimatları — `fm tactic instructions`: topla oynarken dört evre, okuma ve değiştirme
      (§8.48); topsuz talimatlar ve bölge ("Zone") görünümleri yok
- [x] İlk 11'e yedeklerden oyuncu koy — `fm tactic player <mevki> <oyuncu>` (§8.31)
- [x] Yedek kulübesini göster — `fm tactic`'in "Yedekler" satırı
- [ ] İki as oyuncunun yerini değiştirme, kadro dışından oyuncu alma (adları taktik ekranında bağlı değil)
- [x] Duran top rutinlerini kur — `fm setpieces new`, oyunun altı soruluk sihirbazı (§8.36)
- [~] Duran top düzenlerini okuma / sonradan değiştirme, penaltı sırası — `fm penalties` sırayı ve üç kuralı okuyor, kuralları değiştiriyor (§8.87), `penalties move / add / remove` sırayı değiştiriyor (§8.90); `fm takers` korner, serbest vuruş ve taç atıcılarını okuyor ve değiştiriyor (§8.91); `fm setpieces <tür>` vuruş tipi, hedef ve oyuncu görevlerini okuyor ve değiştiriyor (§8.93); savunma senaryosu, rutin ekleme / kaydetme ve yerler arası sürükleme yok
- [~] Kaptan / yardımcı kaptan — `fm captains`: okuma, kaptan değişimi; yardımcı yalnızca boşken (§8.58)
- [x] Taktik kaydet / yükle / kopyala — `fm tactic copy`, `fm tactic clear <n> --yes` (§8.77); dosyaya `fm tactic save / saved / load` (§8.107)
- [x] Rakip talimatları — `fm opposition`: tablo, yardımcının önerisi, temizle, oyuncu başına ayar (§8.49)
- [x] Taktik akıcılığı (familiarity) — `fm tactic` takımın ve on birin yüzdesi (§8.80); evre evre döküm yok

---

## 4. Antrenman

> Katalog: COOKBOOK §5.

- [x] Haftalık antrenman programı göster — `fm schedule`: bu hafta ve gelecek hafta, gün gün üç seans (§8.73)
- [x] Antrenman programı değiştir — `fm schedule set <gün> <1-3> [<seans>]` tek seans, `fm schedule style <gün> [<program>]` bütün hafta (§8.89); kendi programını oluşturmak ("Create New Schedule") yok
- [~] Maç hazırlığı odağı — `fm prep` okuyor (§8.60); değiştirmek yok: seans listelerinde bu odaklar yok, "Match Focus" seansı kilitli (§8.92)
- [x] Bireysel antrenman odağı — `fm training <oyuncu> --focus/--position/--role` (§8.51)
- [x] Pozisyon / hareket öğretme — pozisyon `fm training --position` (§8.51), alışkanlık `fm training <oyuncu> --trait` (§8.75)
- [x] Mentor / mentee ilişkileri — `fm mentoring`: gruplar ve üyeleri, `--auto` yardımcıya kurduruyor (§8.102); `new <o1> <o2> <o3> [--name]` elle kuruyor, `remove` dağıtıyor (§8.106)
- [x] **Antrenman raporları** — `fm training <oyuncu> --report [--coach <ad>]` (§8.74)
- [x] Yoğunluk ayarı — `fm intensity`: oyuncu başına seviye, `--auto` (§8.50); takım kuralları yok
- [x] Dinlenme — `fm intensity <oyuncu> "dinlen 3"` / `"tatil 1"` (§8.50)
- [ ] Gençlik antrenmanı

---

## 5. Transfer ve sözleşme

> Katalog: COOKBOOK §2. Pazarlığın tamamı `TransferOfferEditSessionReference` (99 property).

- [x] Kısa liste — `fm shortlist` (§8.56); transfer listesine konan kendi oyuncularımız yok
- [~] Oyuncu ara — `fm players [--filter "<hazır süzgeç>"] [--view "<görünüm>"]`: veritabanının
      ekrandaki 21 satırı, `--clear` süzgeci kaldırır (§8.38); gerçek arama `--role --age --grade --contract --listed`
      (§8.117), nitelik koşulu `--attr "pace>=16"`, mevki `--position DL`; kaydırma (ilk 21 satırdan ötesi) yok
- [x] Teklif ver — `fm offer`: kart, şart düzenleme, pazarlık kilitleri, karşı teklif (§8.42)
- [x] Gelen teklife yanıt — mesajda `reply <#> Accept | Reject`; "View Offer" sonrası `fm offer` (argümansız) kartı gösteriyor (§8.67)
- [x] Sözleşme görüşmesi — `fm contract [oyuncu]`: kart, vaatler, şartlar, kilitler, menajerin
      karşı teklifi, iptal; kendi oyuncumuzla yenileme (§8.44)
- [x] Sözleşme maddeleri ve primleri — kartta ekle / kaldır / değiştir, kupa ve ödül primleri dahil (§8.45)
- [x] Yenilemede oyuncunun kararı ve "menajere sor" yolu — `contract --agent` (§8.74 civarı); menajer şartları kabul edince karar soru olarak değil haber olarak geliyor ("Mert Müldür signs new 3-year Fenerbahçe deal", iki gün sonra; §8.94)
- [~] Kiralık — `fm offer --loan`: kart, şartlar, satın alma opsiyonu, gönderildi (§8.46); cevabı ve
      kiralık uzatma (`PersonOfferExtendLoan`) görülmedi
- [x] Oyuncuyu kulüplere öner — `fm propose <oyuncu> [ücret] [--loan] [--yes]` ("Offer via TransferRoom", §8.94); hedef kulüp seçmek ve ek maddeler yok
- [x] Gelen teklifler — `fm offers`: cevap bekleyen teklifler; `view` / `accept` / `stall` / `reject` (§8.98). Mesajı gelen kutusundan düşmüş teklif de buradan cevaplanıyor
- [~] Sözleşme feshi / serbest bırakma — `fm release <oyuncu> [--free|--mutual] [--yes]` (§8.76); `--yes` ile gerçek bırakma denenmedi
- [x] Serbest oyuncular — `fm players --free` ("Contract Expired" süzgeci, §8.92)
- [x] Ön anlaşma sözü — `fm contract --term "<vaat>"` vaat ekliyor, `=kaldır` çıkarıyor ("Add Promise", `EventFmxAddPreContractPromise`; 18 Ekim 2025 oyun gününde Defrel kartında doğrulandı)
- [x] Menajerle görüşme — `fm contract <oyuncu> --agent`, sonra `talk` (§8.76)
- [x] Transfer bütçesi ↔ maaş bütçesi — `fm budget ayarla` (§8.65); bütçe artışı istemek yok
- [~] Takas teklifi — kartta "+ Takasa oyuncu ekle", transfer ya da kiralık (§8.45); takaslı
      teklif gönderilip cevabı görülmedi
- [x] Transfer dönemi durumu / kalan gün — `fm status` (§8.55)

---
- [~] İlgi duyurma, kulübe sorma, denemeye çağırma — `fm interest`, `fm enquire`, `fm trial` (§8.119); ilgi haber oluyor, deneme reddi ve sorunun cevabı ("£6,25M istiyorlar") gelen kutusuna düşüyor; kabul edilen deneme görülmedi

## 6. Scouting

- [x] Scout raporları — `fm scout <odak>`: odağın bulduğu oyuncular, not ve tavsiyeyle (§8.109); `fm player <ad> --report`: oyuncunun tam raporu — not, yetenek / potansiyel, ilgisi, değeri, artılar / eksiler, kadrodaki sırası (§8.123)
- [~] Scout ata (bölge/oyuncu/takım) — oyuncuya: `fm scout player <ad> [--by <scout>] --for <süre>`, `scout who` (§8.119); iptal oyunun kendi işleyicisiyle de işlemiyor (§8.124); bölge / takım odak sihirbazında (§8.109)
- [x] Scout odakları (recruitment focus) — `fm scout`: odaklar, durum, atananlar, sayılar (§8.109); `scout new` (mevki, rol, bölge, yaş, ad, öncelik), `scout remove` / `restart`, `scout areas` (§8.120); en düşük yetenek / potansiyel (`--ability`, `--potential`), belirli scout (`--by`), `scout edit` (§8.124); analist seçimi yok
- [x] Kısa liste — `fm shortlist` (§8.56); `shortlist add <ad> --for <süre>` / `remove <ad>` (§8.119)
- [x] Oyuncu karşılaştırma — `fm compare <a> <b>`: kimlik ve özellikler yan yana, iyisi işaretli
- [x] Rakip analizi — `fm opponent`: raporun özeti (§8.57), sezon sayıları ve hüküm (§8.114), menajer ve oyun tarzı, beklenti, birim birim analiz ve öne çıkan oyuncular (§8.121)
- [~] Gençlik adayları / youth intake — `fm youth`: altyapı notu, A takıma aday görülenler, alım tarihi (§8.112); alım gününde aday listesi bakılacak (Mart)

---

## 7. Maç

- [x] `fm next` — ilerle, bloke eden mesajları ve oyunun ilerle butonunun ne dediğini bildir
- [x] `fm next [n]` zamanı ilerletiyor, oyunun durmasını bekliyor, tarih/saat ve yeni mesajları
      bildiriyor; oyunun kendi durumunu okuyor (`continue.State`, §8.32)
- [x] Konuşma durağında cümleyi kullanıcı seçer — `--talk` kalktı; `fm match --play` her konuşmada sözü soruyor
- [x] Maç gününü oyna — `fm match`: izlemek mi anında sonuç mu diye sorar; kadro gönder, onay penceresi, konuşmalar, maç,
      çıkış; gol/devre/sonuç bildiriyor (§8.34)
- [x] Maç içi oyuncu değişikliği — `fm sub <çıkan> <giren>`; `fm sub` sahayı ve kulübeyi listeler (§8.34)
- [~] Sakatlık durağı — durak: değişiklik + "Confirm Changes" (§8.69, elle doğrulandı); `match` ve `sub` bunu yapıyor, yeni akış canlı durakta denenmedi
- [x] Anında sonuç — `fm match --instant`, 9 sn (§8.34)
- [x] Maç hızı — `fm settings vurgu hızı` (en çok 10x), `ara hızı` (en çok 40x), `maç gösterimi` (§8.61); maç içinden değiştirmek yok
- [~] Maç içi taktik değişimi — mentalite ✓, bağırış ✓ (`fm shout`, §8.62), rol ✓ (`fm tactic role
      <oyuncu>` maç sırasında); oyuncu / takım talimatı maç içinde yok, oyuncuya özel bağırış yok
- [x] Maç öncesi, devre arası ve maç sonu takım konuşması — `fm talk` ya da `fm match --talk`
- [~] Canlı maç takibi — `fm match --play` anlatım satırlarını, golleri ve skoru akıtıyor; maç ekranının kalanı §14
- [x] Maç içi: rol değişimi, shout (§8.62)
- [x] Yardımcının maç içi tavsiyeleri — `fm dugout` okuyor, uygula / kaldır (doğrulama: Do it
      kapanıyor / tavsiye düşüyor); `match --play` yeni tavsiyeyi akışta yazıyor (§8.62)
- [x] Maç sonrası — `fm report`: skor, goller, maçın oyuncusu ve notu, istatistikler; basın toplantısı `fm talk`
- [~] Maç istatistikleri — `fm report` topla oynama, şut, isabet, xG, korner, faul; pas / ısı haritası yok
- [ ] Highlight özeti (metin)
- [?] Maç içi gerçek zamanlı yönetim (v2 — bambaşka problem)

---

## 8. Lig, kupa, takvim

- [x] Lig tablosu — `fm table` (§8.37); puanlı tabloda denenmedi
- [x] Fikstür — `fm fixtures` son ve sıradaki maçlar; `fm fixtures --all` sezonun tamamı (§8.66)
- [x] Başka ligler / ülkeler — `fm stages "<turnuva>"` / `fm table <turnuva>`: dünyadaki her turnuvanın tablosu ve turları (§8.105); `--list` hepsini sayar
- [x] Kupa ağaçları — `fm stages "<kupa>" [tur] [--all]`: eşleşmeler, skor, penaltı / uzatma, toplam skor (§8.105)
- [~] Avrupa kupaları — `fm table europa` lig aşaması tablosu (36'nın 33'ü, §8.100), `fm league <turnuva>` liderler; eleme turları `fm stages` ile (§8.105)
- [~] Gol krallığı / asist / form tablosu — `fm league [turnuva]` liderleri veriyor (§8.70); tam liste yok — veri `CompReference.PlayerStats`ta, açan kart bulunamadı (§8.81)
- [x] Takvim — `fm calendar [ay]`: maçlar, transfer dönemi açılış / kapanış, kupa kuraları, milli maçlar (§8.66)
- [ ] Sezon özeti / ödüller

---

## 9. Finans

- [x] Bütçe durumu — `fm budget`; transfer ↔ maaş dağılımı `fm budget ayarla` (§8.65)
- [x] Gelir/gider dökümü — `fm finances gelir|gider [ay|geçen ay|sezon|geçen sezon]` kalem kalem (§8.79)
- [x] Maaş bordrosu — `fm finances`: toplam / bütçe, oyuncu statüsüne göre; personel maaşları `fm staff` (§8.78)
- [x] Sponsorluklar — `fm finances`: anlaşma anlaşma tür, sezonluk değer, bitiş, süre (§8.79)
- [x] Bilet fiyatları — `fm finances` maç bileti ve kombine (§8.81); değiştirmek yok
- [~] Kulüp değeri / borç — `fm finances` kalan ve başlangıç borcu; kulüp değeri yalnızca boş bir debug alanında (§8.81)
- [x] Finansal fair play durumu — `fm finances` (§8.78)

---

## 10. Personel ve kulüp

- [x] Teknik ekip listesi — `fm staff`: kişi kişi iş, yaş, maaş, sözleşme; rol eksikleri ve yönetimin görüşü (§8.78)
- [x] Personel işe al / kov (Coaching, Scouting, Medical) — `fm staff find` (arama, §8.116), `fm staff hire <ad>` (sözleşme masası, `contract` ile sürer), `fm staff fire <ad>` (tazminat onayı); kişinin sağ tık menüsü `fm actions` (§8.118). Teklifin cevabının düştüğü an görülmedi
- [~] Personel sorumlulukları (delegasyon) — `fm responsibilities`: 29 personel işi kimde, `delegate` / `take` (§8.111); belirli kişiye verme ve personel dışı devirler yok
- [x] Tesisler (antrenman, gençlik, altyapı) — `fm facilities`: stadyum, antrenman, altyapı, ticari (§8.79)
- [x] Tesis geliştirme talebi — `fm board request "improve training facilities" --yes` (§8.108)
- [x] Yönetim kurulu beklentileri — `fm club`: not, hava, vizyon hedefleri (§8.78)
- [x] Yönetimle görüşme / talep — `fm board request ["<talep>" --yes]`: 33 talep türü, karar mesajla geliyor (§8.108); Build New Stadium / New Contract görüşmesi denenmedi
- [x] Taraftar memnuniyeti — `fm supporters`: hava, beklentiler ve tepkileri, seyirci, profil (§8.79)
- [x] Kulüp vizyonu / hedefler — `fm club` (§8.78)
- [~] İş güvenliği (sacılma riski) — `fm board` yönetimin ve taraftarın görüşü konu konu (§8.81); `JobSecurityStatus` sayısının karşılığı bilinmiyor

---

## 11. İletişim

- [x] Inbox oku (`fm inbox`)
- [x] Mesaja cevap ver — `fm reply <#>` seçenekleri gösterip seçimi soruyor; `fm reply <#> <no>`
      tek satırda cevaplıyor (§8.29)
- [x] Tanıtım turları — `fm tour` yürütüyor, `fm tour --skip` atlıyor (§8.30)
- [x] Basın toplantısı — `fm talk`; liste cevaplı sorular dahil (§8.36)
- [x] Maç öncesi/sonrası röportaj — basın toplantısı gelen kutusundan açılıyor, `fm talk` yürütüyor
- [x] Takım toplantısı — `fm talk`: ne dendi, tepkiler, söylenebilecekler (§8.33)
- [x] Oyuncuyla bire bir konuş — `fm talk` şablonu canlıda doğrulandı (sakatlık mesajından açılan konuşma, tepki değişimiyle; §8.94)
- [ ] Oyuncu şikâyetlerini yönet
- [x] Soyunma odası atmosferi / takım liderleri — `fm dynamics`: menajere destek, sosyal gruplar, her oyuncunun hiyerarşisi ve kişiliği (§8.95)
- [~] Sosyal medya / haber akışı — `fm news`: haber sitesindeki başlıklar, konu, yaş ve ilk cümle (§8.97); `fm news <#>` haberin tamamı; sosyal medya yok
- [x] Sözleşme görüşmelerinde diyalog — `fm contract` vaatleri gösteriyor, `--term` ile vaat seçtiriyor / ekletiyor, menajerin cevabını basıyor

---

## 12. Ayarlar

- [x] **Dil değiştir** — `fm settings dil` (uyarıyla: komutlar İngilizce okuyor; canlı denenmedi) (§8.61)
- [x] **Para birimi değiştir** — `fm settings para birimi euro` (§8.61)
- [x] Tarih formatı — `fm settings tarih`
- [x] Ölçü birimi — `fm settings boy / kısa mesafe / uzun mesafe / sıcaklık`, maaş haftalık/aylık/yıllık
- [x] Maç hızı ve gösterimi — `fm settings vurgu hızı / ara hızı / maç gösterimi / dinamik mod`
- [?] Detay seviyesi — FM26'nın tercihlerinde böyle bir ayar yok (sekmeler: Your World, Audio & Visual, Accessibility, Match Day, Processing, Third Party, Shortcuts; §8.99); yakını `settings diğer maçların hızı`
- [~] Hangi ligler yüklü (add/remove league) — `fm world`: kaç ülke / lig / oyuncu, detay seviyesi, yüklü kaydın adı ve son kayıt (FM menüsünden); lig ekleyip çıkarmak yok; turnuvaların listesi `fm stages --list`
- [?] Bildirim tercihleri — FM26'nın tercihlerinde bildirim sekmesi yok (§8.99)
- [x] Otomatik kaydetme aç / kapat, sıklığı ve türü — `fm settings otomatik kayıt / kayıt sıklığı / kayıt türü`
- [x] Maç varsayılanı: oynat / anında sonuç — `fm match` terminalde her seferinde soruyor, script'te `--play` / `--instant` (kalıcı bir varsayılan yok, ürün kararı gereği)
- [ ] Skin / tema (bizde anlamsız ama API'de var)

---

## 13. CLI'ın süper gücü: sorgu ve analiz

> FM arayüzünde yapması işkence olan, terminalde tek satır olan şeyler.

- [~] Serbest sorgu — `fm query "yaş<23 ve potansiyel>140 ve sözleşme<1yıl"`: kendi kadromuzda on yedi alan, `--sort`; oyuncu veritabanında (başka kulüpler) yok
- [x] Kadro sağlık taraması — `fm health`: sakat, yorgun, maç formu düşük, sakatlık riski en yüksek (§8.71)
- [x] Sözleşme takvimi — `fm contracts`: en yakın biten önce, renkli (§8.72)
- [x] Derinlik matrisi: hangi pozisyon zayıf — `fm depth`, `fm doctor` ince mevkiler
- [~] Transfer fırsat taraması (bütçe + ihtiyaç + uygunluk) — taşları var: `fm players --role --age --grade --contract --listed` (§8.117), `fm scout <odak>` (§8.109), `fm budget`; tek komutta birleştirilmedi
- [ ] Sezon karşılaştırması / trend
- [x] `fm diff` — son bakıştan beri ne değişti: lig sırası, kasa, bütçe, gelen/giden, sakatlar, sözleşmeler, maç/gol/asist (`--keep`, `--reset`)
- [ ] `fm watch` — bir oyuncuyu/durumu izle, değişince haber ver (`Bindings.Bind` ile)
- [x] Export: CSV / JSON / Markdown rapor — her komutta `--csv` / `--md`; `fm export rapor.md [komutlar…]` başlık başlık rapor, `export x.csv "<komut>"`

---

## 14. CLI kalitesi

### Görselleştirme ve akıcılık (öncelikli, 2026-10-06)

- [x] Terminal arayüz kütüphanesi seç — **Spectre.Console** (2026-10-07). Terminal.Gui tam ekran
      bir uygulama ister ve komut + pipe yapısını bozardı; Spectre panel, tablo, ızgara ve okla
      seçimi mevcut komutların çıktısına ekliyor, script/MCP düz çıktıyı koruyor (`Ui.Rich`)
- [x] **Saha çizimi** — `fm tactic` iki sahayı yan yana çiziyor (dar terminalde alt alta)
- [x] Değişiklik sahada görünsün — `tactic role` / `player` / `pick` sonrası değişen mevki sarı
- [x] Maç ekranı — anlatım satırları, takım adlarıyla skor, maç öncesi kadro sahada, izle /
      anında sonuç seçimi, konuşmada söz seçimi, maç sonu raporu (`fm report`), yardımcının
      tavsiyeleri akışta; maç sırasında `fm sub` on biri sahada çizip kulübeyi altında veriyor
- [x] Seçim istemleri — reply, talk, dialog, sub, tactic new/mentality/style/role/player,
      setpieces new, match okla soruyor; script'te düz çıktı
- [x] Konuşma ekranı — `talk` konuşmayı baştan sona okla yürütüyor (basın toplantısı, takım toplantısı, bire bir); tepki değişimi satırda ("Encouraged (+1)")
- [x] Oyuncu kartı — mevki sahası, renkli çubuklar, bloklar yan yana, scout aralığı gölgeli
- [~] Lig tablosu ve fikstür — tablo: bölge renkleri ve takım vurgusu; fikstür: G / B / M renkli. Form: tabloda son beş maç G / B / M (§8.115), `fm table` da Stages sayfasından tam tabloyu alıyor
- [x] Kolon başlıkları — `squad --view` / `players` okunur başlık basıyor
- [~] Akış — `fm status` takım, menajer ve sıradaki maçı bir panelde veriyor; `next` gürültüsü azaldı

### Diğer

- [x] Renkli, hizalı tablolar
- [~] Sparkline / mini grafik (form, kondisyon trendi) — `player --matches` puanları çubuk satırı olarak veriyor (`Ui.Bar`); kondisyon trendi yok (geçmişi tutulmuyor)
- [x] `--json` her komutta (script'lenebilir) — bir şey gösteren her komut; `--json` soru sormuyor, çizmiyor (§8.83)
- [x] Pipe dostu çıktı (`fm squad | grep ...`)
- [~] İnteraktif oturum (`fm start` sonrası komutlar `fm` önekisiz) — TUI değil, REPL; tırnaklı
      ifadeler tek kelime sayılıyor
- [x] İsim eşleme — isim parçası, aksansız yazım ve ufak yazım hatası (`osimen` → Osimhen, `guendozi` → Guendouzi); oyunun araması bulamazsa adın başıyla yeniden arar
- [x] Kısaltmalar / takma adlar — komutun tek anlamlı başı, kısa adlar (s, n, t, fx…), Türkçe adlar aksanlı/aksansız; oturumda da
- [x] Tab tamamlama (zsh/bash) — `fm completion zsh|bash`; zsh açıklamalar, seçenekler ve kadrodaki oyuncu adları
- [x] `fm status` — tarih, saat, oyunun neyi beklediği, sıradaki maç, bekleyen mesajlar (§8.32)
- [x] Onay pencereleri — `fm dialog` gösteriyor / cevaplıyor (§8.34)
- [x] Geri alınamayan işlerde onay — terminalde sorar, script'te `--yes` ister: `save`, `offer`, `contract`
- [x] Geri alma yok — yıkıcı aksiyonlarda uyarı: teklif, sözleşme imzası, serbest bırakma / fesih, kayıt, taktik silme, dil değişimi, alışkanlık antrenmanı ve konuşmadaki fesih seçenekleri terminalde soruyor, script'te `--yes` istiyor (10 Ekim taraması)
- [x] Oyun kapalıysa otomatik başlat (`fm start`)

---

## 15. MCP + doğal dil

- [x] MCP server — `fm mcp` (stdio), 34 araç, her araç bir komut (§8.39); gerçek istemciyle denenmedi
- [ ] "Kadroda yorgun kim var?"
- [~] "Sol bek arıyorum, 10M bütçem var, öner" — araması var: `fm players --position DL --max-value 10M --age … --attr …` (§8.117); öneriyi sıralamak LLM'e kalıyor (MCP `fm_players`)
- [ ] "Bu hafta kimi dinlendirmeliyim?"
- [ ] "Rakibi analiz et, taktik öner"
- [ ] Çok adımlı görev: "transfer dönemi bitmeden kadroyu güçlendir"
- [ ] Sesle oynama (STT önde, MCP arkada)
- [ ] Sesli maç anlatımı (TTS + canlı maç akışı)
- [ ] LLM'in maç sonrası basın toplantısını senin adına yönetmesi
- [ ] "Asistan menajer" modu — sen onay ver, o oynasın

---

## 16. Uçuk fikirler

- [ ] `fm` üzerinden **Discord/Slack botu** — takımı sohbetten yönet
- [ ] Sezonu otomatik oynat, sadece kritik kararlarda sana sor
- [ ] İki fm-cli birbirine karşı — LLM'ler kafa kafaya lig
- [ ] Git entegrasyonu: her sezon bir commit, kadro değişimi diff olarak
- [ ] Kariyer günlüğü otomatik yazılsın (LLM, maç verilerinden)
- [ ] Web dashboard (plugin zaten HTTP konuşuyor)
- [ ] Mobil bildirim: "Osimhen sakatlandı"
- [x] `fm doctor` — sakatlar (kalan gün), yorgunlar, maç formu düşükler, biten sözleşmeler, ince mevkiler, yönetimin endişeleri; ekransız (§8.82)
- [ ] Zaman serisi DB'ye akıt, Grafana'da kariyer dashboard'u
- [ ] Başka oyunlara aynı mimari (Unity + IL2CPP olan her şey)

---

## Günlük

### 2026-10-11 (öğleden sonra — "yarım kalanları kapatalım")

- `fm scout new` artık en düşük yeteneği / potansiyeli yıldızla (`--ability`, `--potential`) ve belirli scoutları
  (`--by`) alıyor; `fm scout edit <#|ad>` var olan odağı değiştiriyor. `scout restart` canlıda görüldü (§8.124).
- Köprüye göz: `/shot` ekran görüntüsü alıyor. Youth Candidates gibi "okunmuyor" denen sayfalara artık bakılabilir.
- Scout görevi iptali: oyunun kendi işleyicisiyle basınca da kalkmıyor — bizim tıklamamız değil. Park.
- Oyun 23 Ekim 2025 kaydından açık, kaydedilmedi; oturumda deneme odakları kaldı.

### 2026-10-11 (akşam)

- **Oyuncuya scout / kısa liste / yoklama** (§8.119): `fm scout player <ad> [--by <scout>] --for <süre>`, `fm scout who`,
  `fm shortlist add / remove`, `fm interest`, `fm enquire`, `fm trial`. Hepsi sağ tık menüsünün girişleri; süre ve tür
  söylenmezse komut seçenekleri sayar, kendisi seçmez. İlgi ve deneme `--yes` ister.
- **Scout odağı kurmak** (§8.120): `fm scout new --position DL --role wing-back --area "Northern Europe, turkiye" --age 18-23
  --name … --priority standard`, `fm scout areas`, `fm scout remove` (durdurur — oyun silmiyor, listede "Durduruldu"
  kalıyor), `fm scout restart`. Kurulan odak oyunun düzenleme formunda doğrulandı.
- **`fm opponent` genişledi** (§8.121): menajer ve tarzı (mentalite, pres, markaj), maç beklentisi, savunma / yaratıcılık
  blokları ve öne çıkan oyuncular — lig rakibinde dolu geliyor.
- 26 Ekim'de odak sayfasından "Create / Remove / Restart" butonları kayboldu (nedeni bilinmiyor, §8.120); `scout new` ve
  `scout remove` o zaman "Assignments" paneli üzerinden yürüyor (canlıda kur → durdur döngüsü doğrulandı). Aynı panelden
  **`fm scout staff`**: scoutların gözü, bildikleri yer, üzerlerindeki odaklar. `<komut> --help` artık komutu çalıştırmıyor.
- **`fm player <ad> --report`** (§8.123): scoutların tam raporu — not ve cümle, yetenek / potansiyel, transfere ilgisi, değer ve
  maaş tahmini, artılar, eksiler, kadroda o mevkide kaçıncı olacağı.
- Youth Performance / Candidates yine bulunamadı (§8.122; görünür pencereyle denenecek).
- Kocaelispor 1-0 kaybedildi (26 Ekim); sakatlık durağı yine çıkmadı. Oyun 26 Ekim 2025 akşamı, kaydedilmedi.
- Çalışmayan: scout görevini **iptal** (oyun soruyu kapatıyor, görev duruyor) — komut bunu doğrulayıp söylüyor.
- Canlıda (kayıtta duruyor, kaydedilmedi): Čerin kısa listede (6 ay) ve soru soruldu; Čerin, Jović, Icardi izleniyor;
  Jović'e kiralık ilgisi; Şahin ve Chirivella denemeye çağrıldı — ikisini de kulüpleri reddetti, ilgi ertesi sabah haber
  oldu. Scout odaklarında üç durmuş deneme girişi var ("CLI deneme" ×2, "CLI sol bek"). Oyun 24 Ekim 2025.

### 2026-10-11 (gündüz)

- **Personel işe alma / kovma** (§8.118): `fm staff hire <ad>` (sözleşme masasını açar, `contract` ile sürer),
  `fm staff fire <ad>` (oyun tazminatı sorar; `--yes` olmadan "Hayır"). Gece "teklif eylemi bulunamadı" diye park edilen şey
  kişinin sağ tık menüsüymüş; köprüye `/actions` eklendi.
- **`fm actions <kişi>`**: o menünün tamamı — oyuncuya scout atamak, listeye almak, ilgi bildirmek, denemeye çağırmak;
  personeli kursa göndermek. Seçenek yazılınca yapılır.
- Canlıda: Agostini'ye (fizyoterapist) £1.400'e teklif verildi, Cabir Sevim (kondisyoner) kovuldu — ikisi de deneme; ardından
  23 Ekim kaydı kaydetmeden yeniden yüklendi, yani kayıtta ikisi de yok.
- Görülmedi: personel teklifinin cevabının geldiği an; "Mutual Termination" personelde.

### 2026-10-11 (gece, otonom — "orta grup")

**Biten komutlar** (her biri canlıda denendi, FINDINGS'te bölümü var):

- **Kayıtlar** (§8.104): `fm load "<ad>"` (kayıttayken oyunu yeniden başlatıp ana menüden yüklüyor), `fm save --as "<ad>"`,
  `fm saves delete "<ad>"`, `fm start --menu`.
- **`fm stages`** (§8.105): kupa turları ve eşleşmeleri (penaltı / uzatma / toplam skor), dünyadaki her turnuvanın tablosu;
  `fm table <başka lig>` buraya düşüyor.
- **`fm mentoring new / remove`** (§8.106): elle üçlü grup kurma, dağıtma; çok gruplu okuma düzeltildi.
- **`fm tactic save / saved / load`** (§8.107): taktiği dosyaya kaydet, geri yükle.
- **`fm board request`** (§8.108): yönetimden 33 tür talep.
- **`fm scout`** (§8.109): scout odakları ve her odağın bulduğu oyuncular.
- **`fm world`** (§8.110), **`fm responsibilities`** (§8.111, devret / geri al), **`fm youth`** (§8.112).

**Plugin:** `/rowselect` (satır seç, `check=` işaretle, `expand=`), `/ties`, `/click` devre dışı butonu reddediyor.

**Kayıpsız kazalar:**
- Devre dışı "Delete"e simüle tıklama oyunu kilitledi (yeniden başlatıldı; koruma eklendi).
- Mentorluk temizliği bir kez bütün grupları sildi (sayfa tam yüklenmeden okunan boş "önceki" listesi yüzünden);
  gruplar yeniden kuruldu, okuma artık sayfa oturana dek bekliyor ve en çok bir grubu siliyor.

**Kayıtta ne değişti:** ana kayıt önce 19 Ekim 2025 haliyle (farklı kaydet denemesi), sonra **23 Ekim 2025** haliyle
kaydedildi (plugin değişikliği için yeniden başlatmadan önce): Qarabağ 2-0 kazanıldı (Asensio, Talisca pen.); yönetimden üç talep yapıldı (transfer bütçesi — reddedildi, "kulüp şu an
karşılayamıyor"; scout sayısı — artırıldı; U19 antrenör sayısı — "Staff Numbers Update" mesajı geldi, okunmadı); Defrel'in Angers'e satışı onaylandı (1 Ocak 2026'da gidiyor; teklifi akşam sen kabul
ettirmiştin); mentorluk grubu yeniden kuruldu (Škriniar, Brown, Mercan).

**Sonradan eklenenler:** `fm progress` (§8.113: son dört aylık yetenek; plugin `/pull` artık liste değerlerini açıyor),
`fm opponent`'a rakibin sezon sayıları (§8.114). Maç sonu raporunun bayat golcü vermesi düzeltmesi bu maçta doğrulandı.

Ardından: lig tablosunda **FORM** sütunu ve `fm table`'ın tam tabloyu Stages sayfasından alması (§8.115), **`fm staff
find`** (personel veritabanı; görev / serbest / itibar araması, §8.116), **`fm players --role --age --grade --contract
--listed --attr "pace>=16" --position DL --max-value 10M`** (oyuncu veritabanında gerçek arama, §8.117 — "10 milyona sol bek"
artık tek komut), kılavuza yeni ekran görüntüleri.

**Park edilenler:** scout odağı kurma / scout atama (iki adımlı sihirbaz, notları §8.109); topla / topsuz ayrı şekil
(kapsamı belirsiz); rakip raporunda kilit oyuncular (bu rakipte veri yok, lig rakibinde bakılacak); belirli kişiye
sorumluluk verme; Youth Performance / Candidates sekmeleri.

### 2026-10-10

- **`fm internationals`** (§8.101) ve **`fm mentoring`** (§8.102) (akşam): milli oyuncular veriden; mentorluk grupları
  okunuyor, yardımcıya kurduruluyor (oyunda bir grup kuruldu, kaydedilmedi).
- **`fm table` yanlış turnuvayı gösteriyordu** (akşam, FINDINGS §8.100): Avrupa maçı öncesi lig yerine Avrupa Ligi
  tablosu. Düzeltildi; `fm table <turnuva>` eklendi (plugin `/table?groups=1`). `fm news <#>` haberin tamamı,
  `fm offers` gelen teklifler. Oyun yeni plugin'le açık, 19 Ekim 2025 (otomatik kayıt); Defrel teklifi bekliyor.
- **`fm settings`'e dört ayar** (akşam, FINDINGS §8.99): oran biçimi, diğer maçların hızı, maç takvimi, okumadan ilerleme yok.
  ⚠ Deneme sırasında oyun bir kez kilitlendi (ana thread, 13. ardışık ayar değişikliğinde); yeniden başlatıldı, 19 Ekim
  2025 otomatik kaydından açıldı (Qarabağ 6-0 gitti). Bütün ayarlar eski değerinde.
- **Gelen teklife mesajsız cevap** (akşam, FINDINGS §8.98): Defrel teklifinin mesajı listeden düşünce oyun "Review Transfer
  Offer"da durdu; Transfer Activity satırındaki ikon butonuyla reddedildi (satış kararı Faruk'un, oyuncu kaldı).
  Ardından `fm offers` yazıldı; Defrel teklifi BEKLETİLDİ (karar Faruk'un: `fm offers defrel accept --yes` ya da `reject --yes`).
- **`fm news`** (akşam, FINDINGS §8.97): basın başlıkları. `fm contracts <oyuncu>` (§8.96): sözleşmenin primleri ve
  maddeleri. `fm propose` uçtan uca doğrulandı: Defrel £50K'ya önerildikten bir hafta sonra Angers aynı bedelle teklif
  yaptı (mesaj cevapsız bırakıldı — satış kararı Faruk'un).
- **Maç sonu raporu bayattı** (akşam): `match` bitince basılan rapor bir önceki maçın takımlarını ve istatistiklerini yeni
  gollerle karışık gösterdi ("Antalyaspor 0-0 Fenerbahçe", Karagümrük 2-0'ından sonra); birkaç saniye sonra `fm report`
  doğruydu. `match` artık raporu, maçın skoru `LastFixture`'da görünene kadar bekletiyor (en çok 12 sn; Qarabağ 6-0'ında
  doğrulandı ✓: rapor ilk basışta doğru). Karagümrük 2-0; Asensio'nun darbesi oyunu durdurmadı, sakatlık durağı yine denk gelmedi.
- **`fm dynamics`** (akşam, FINDINGS §8.95): sosyal gruplar, hiyerarşi, kişilik ve menajere destek. Ön anlaşma sözü
  (`contract --term`) canlıda doğrulandı; `contract --cancel` vaat aşamasında da çıkıyor.
- **`fm propose`** (akşam, FINDINGS §8.94): oyuncuyu kulüplere önerme, satış ya da kiralık; canlıda iki oyuncu önerildi.
  Bire bir oyuncu konuşması `fm talk` ile canlıda görüldü. Yenilemede oyuncunun kararı haber olarak geliyor (Müldür imzaladı).
  `contract` kilitli açılan kartı söylüyor (Defrel). Oyun 11 Ekim 2025'e ilerledi (kaydedilmedi; yedek `…2026-10-10-evening.fm`).
- **`fm setpieces <tür>`** (akşam, FINDINGS §8.93): duran top rutininin vuruş tipi, hedefi ve yer yer oyuncu görevleri;
  okuma ve değiştirme, beş türde. Sayfayı açan basış ara sıra yutuluyordu (oyun Match Day › Overview'da kalıyor);
  `takers` ve `setpieces` artık bir kez yeniden açıyor (12 ardışık çağrıda 12 başarı, üçü yeniden açarak).
- **`fm query`**: kadroda serbest sorgu (yaş, mevki, kondisyon, maç formu, sakat, potansiyel, maaş, sözleşme süresi,
  statü, maç / gol / asist / puan…); ekran açmadan tek `/pull`. Maaş `FullContract.Wage.DisplayValue`, statü
  `FullContract.AgreedPlayingTime.DisplayValue` yolundan geliyor; piyasa değeri `/pull`'da bulunamadı.
- `player --matches` puanların çubuk grafiğini de basıyor (eski → yeni, 5.5–9 ölçeği).
- Fikstürde beraberlik de renkli (oyunun işareti `ResultType` 9; 1 galibiyet, 3 penaltıyla, 10 mağlubiyet).
- **`fm stop`**: oyunu kapatıyor (süreç sonlandırılıyor); kaydedip kaydetmemeyi soruyor, script'te `--save` /
  `--no-save` şart. `--no-save` denendi (3 sn), `--save` yolu `fm save --yes`'in kendisi, ayrıca denenmedi.
- **Serbest oyuncular ve bir sızıntı** (FINDINGS §8.92): `fm players --free`. `/pull` ile okunan kendi kadromuz
  (squad, contracts, doctor…) sonraki `fm players` listesine karışıyordu; köprü artık kendi koyduğu düğümleri kişi
  listelerine almıyor. Maç hazırlığı odağını değiştirmek ve gelişim grafiği denendi, ikisi de park.
- **Duran top atıcıları** (FINDINGS §8.91): `fm takers` beş türün (korner, üç serbest vuruş, hücum bölgesi tacı)
  sağ ve sol atıcı listelerini veriyor, `fm takers <tür>` özellikleriyle; `takers <tür> <sağ|sol> move / add / remove`
  sırayı değiştiriyor. Penaltıdaki taşıma kodu ortak hale geldi (`Reorder`). Denemelerden sonra bütün listeler eski
  haline döndü; oyun 5 Ekim 2025, kaydedilmedi.
- **Antrenman programı** (FINDINGS §8.73): `fm schedule` bu hafta ve gelecek hafta, her gün üç seans,
  maç günleri sarı. Oyun 5 Ekim 2025.
- `fm player Fred` açılmıyordu: oyunun araması yalnızca ilk birkaç sonucu gösteriyor, kısa adlı
  bizim oyuncu listeye girmiyor. Kendi oyuncumuz aramada yoksa kadro tablosundaki adından açılıyor.
  Arama ayrıca önceki aramanın ekranda kalan sonuçlarını yeni sanmıyor.
- **Antrenman raporu** (FINDINGS §8.74): `fm training <oyuncu> --report [--coach <ad>]` antrenörden
  raporu istiyor, gelen mesajı açıp değerlendirmeyi ve ekteki ilerleme raporunu basıyor. Yan ürün:
  `reply` artık gövdesinde oyuncu adı geçen mesajların tamamını gösteriyor (önceden ilk cümle).
  Denemelerde Fred, Kerem, Talisca, Kanté için raporlar gelen kutusunda (oyun kaydedilmedi).
- **Alışkanlık öğretme** (FINDINGS §8.75): `fm training <oyuncu> --trait [<alışkanlık>]`, `--trait-remove`,
  `--trait-cancel`, `--trait-suggest`; yardımcının itirazı onay penceresi, `--yes` ile geçiliyor.
  Archie Brown'a "Plays One-Twos" başlatılıp iptal edildi; kalıcı değişiklik yok.
- **Serbest bırakma** (FINDINGS §8.76): `fm release <oyuncu> [--free | --mutual] [--yes]`. Bedelsizde oyun
  tazminatı söyleyip soruyor (Fred: £7,5M), komut onu gösteriyor. ⚠ Karşılıklı fesih oyunda
  **onaysız** işliyor — denemede Fred'e gitti; oyun kaydedilmeden yeniden başlatılıp geri alındı.
  Komut bu yüzden `--mutual`'da kendisi onay istiyor.
- **Menajere yenileme sorusu**: `fm contract <oyuncu> --agent` aynı sözleşme bölümünden "Discuss New Contract
  With Agent"i açıp konuşmayı `talk`a bırakıyor (Fred'in menajeri: "he doesn't want to, under any circumstances").
- ⚠ Kaza: denemeler arasında `fm release Fred` sözleşme bölümünü açtığı için `talk 4` Kerem'in konuşması yerine
  Fred'inkinde "Mutual Termination"ı seçti; oyun kaydedilmeden yeniden başlatılıp geri alındı. `talk` artık
  numarayla Mutual Termination / Release On A Free'yi `--yes` olmadan göndermiyor.
- **Taktik kopyala / yuva boşalt** (FINDINGS §8.77): `fm tactic copy`, `fm tactic clear <n> --yes`. Kopyalanıp
  silindi, tek yuvaya dönüldü.

- **Kulüp okumaları** (FINDINGS §8.78): `fm club` (Club › Overview'ün kutuları: yönetim notu, hedefler, taraftar,
  kasa, maaş, tesisler, ekip boşlukları), `fm staff` (Staff kartının All Staff tablosu, kaydırarak 45 kişi),
  `fm finances` (finans kartı: gelir/gider, borç, FFP, statüye göre maaş, sponsorluk, projeksiyon). Hepsi salt okuma.
  Yan ürün: sayfa açmak ara sıra oyunun çizim anına denk gelip "VisualElements cannot be added to an active visual
  tree…" hatası veriyordu; `OpenPage` artık bu cevapta yarım saniye bekleyip yeniden deniyor (8/8 temiz).

- **Kulübün kalanı** (FINDINGS §8.79): `fm finances gelir|gider [dönem]` kalem kalem, `fm finances` sponsor
  anlaşmalarını tek tek veriyor, `fm supporters` (beklentiler, seyirci, profil), `fm facilities` (stadyum,
  antrenman, altyapı). Yönetim kartı okundu ama talepte bulunmak eylem — ayrı iş.


- **Kolay okumalar (1. grup, öğleden sonra)** (FINDINGS §8.80): `fm tactic` akıcılığı yüzde olarak veriyor, takımın
  ve on birin ("Taktiğe tam alışamayanlar: İsmail 71%…"); `fm player` mevki puanları ve sakatlık geçmişi (kadro dışı
  oyuncuda da); `fm squad --stats` sezon istatistikleri, kiralıktakiler ayrı. Yan ürün: oyuncu sayfası artık
  özellikler bağlanmadan okunmuyor; arama, önceki aramanın geç beliren sonuçlarını yeni sanmıyor.
- **Yönetim görüşü ve bilet** (FINDINGS §8.81): `fm board` yönetimin ve taraftarın konu konu görüşü, memnun oldukları ve
  endişeleri; `fm finances` maç bileti ve kombine fiyatı. Park: tam gol listesi, başka ligler, scout tavsiyeleri,
  kulüp değeri (yalnızca debug alanı).
- **Orta grup: penaltı sırasını değiştirmek** (FINDINGS §8.90, 10 Ekim akşam): `fm penalties move / add / remove`.
  Sürükleme, tablonun kendi `OnMoveItemInternal` / `OnRemoveItemInternal` yöntemleriyle (plugin `/rowmove`,
  `/rowremove`); "Add Player" listesindeki kişiler kadrodaki `Index` ile eşleniyor.
- **Orta grup: antrenman programını değiştirmek** (FINDINGS §8.89, 10 Ekim akşam): düzenleyen sayfa Portal › Calendar'ın
  Training sekmesiymiş. `fm schedule set` tek seansı, `fm schedule style` bütün haftayı değiştiriyor; seçenek yazılmazsa
  okla. Plugin'de `/form?buttons=1`. Not: bir haftaya aynı programı yeniden uygulamak haftayı birebir eski haline
  getirmeyebiliyor (3 Kasım haftasında bir seans farklı geldi).
- **Orta grup: antrenman programı** (FINDINGS §8.88): ilk bakış; düzenleyen sayfa bulunamadı, olaylar ve veri tipleri
  not edildi.
- **Orta grup: `fm penalties`** (FINDINGS §8.87): okuma ve üç kural; sıra sürüklemeyle değişiyor, yapılmadı.
  Ayrıca: kartta kulübün gördüğü potansiyel (§8.85), `/squad` boş alan birleştirme düzeltmesi (§8.86, doğrulanmadı).
- **Kolay grup: maç maç puanlar** (FINDINGS §8.84): `PastMatchesRatings` `/pull`'la boş liste; oyuncu sayfasındaki form
  kutusu ("Last 5 Club Games") tıklanınca açılan Match Ratings sayfası okunuyor. Skor ev sahibi önce, hangi tarafta
  oynadığını söylemiyor (fikstürle eşlemek 18 sn sürdüğü için yapılmadı).
- **Kolay grup: yazım hatası** (`Matching`): önce içeren adlar, yoksa 4-6 harfte 1, 7+ harfte 2 harf farkla en yakınlar.
  Oyunun araması "osimen"de oyuncu bulmuyor, "osim"de buluyor; o yüzden adın ilk 4, sonra 3 harfiyle yeniden aranıyor.
  Kadroda yazım hatası eşleşmesi, dünyada tam eşleşme yoksa kullanılıyor (`sane` iki Sané'yi soruyor, kadrodan birini değil).
- **Kolay grup: `fm compare`**: iki oyuncu sayfası art arda açılıyor. Yan ürün: ikinci sayfa özelliklerin yarısı
  gelmişken okunabiliyordu (Guendouzi 36 yerine ~18); artık özellik sayısı iki okumada aynı kalınca alınıyor.
- **Kolay grup: `fm diff`**: bakışı `~/.config/fm-cli`'ye yazıp sonrakinde karşılaştırıyor; oyuncu verisi `/pull` ile,
  lig / kasa / bütçe diğer komutların `--json`'ından. Değer, maaş, statü `/pull` ile gelmiyor (yalnızca ekrandan), dışarıda kaldı.
  Gerçek bir ilerlemeyle değil, kayıtlı anı elle değiştirerek denendi.
- **Kolay grup: export**: `--csv` / `--md` her komutta (tablolar `Table.Write`'tan geçtiği için tek yerde), `fm export`
  birkaç komutu tek Markdown rapora yazıyor. Yan ürün: düz `fm table` portal kutusunda boş G/B/M/AV kolonları basıyordu.
- **Kolay grup: `--json`** (FINDINGS §8.83): bir şey gösteren her komut JSON veriyor; tuple'lar ve Türkçe harfler
  düzgün. Yan ürün: `fm league` adlar gelmeden okuyordu; `tactic pi` talimat görünümünden sahaya dönemiyordu
  ("Team Shape" gerçek tıklama), `tactic instructions` erken basışı kaçırıyordu.
- **2. grup** (FINDINGS §8.82): kısaltmalar ve Türkçe takma adlar, `fm completion zsh|bash`, `fm doctor`. Yan ürün:
  `fm health` sakatın kalan süresi yerine şimdiye kadar kaçırdığı süreyi "daha" diye yazıyordu; düzeldi. Oyun bir kez
  kendiliğinden çöktü (13:19, oyunun kendi iş parçacığında yakalanmamış hata); aynı sıra yeniden başlatınca tekrarlamadı.

### 2026-10-09 (gece, otonom)

**Kayıtta ne değişti:** 10 Ağustos'tan 20 Eylül'e oynandı. Sonuçlar: Feyenoord 1-1 (toplam 1-2, UCL'den
elendik, Avrupa Ligi'ne düştük), Göztepe 0-0, Konyaspor 1-0, Kasımpaşa deplasman 2-0, Başakşehir 4-1,
Gençlerbirliği deplasman 0-4. 20 Eylül'de (maçtan sonra) elle kaydedildi. Yedek: `save-backups/…2025-08-13-feyenoord-day.fm`.

**Gece verdiğim oyun kararları (geri alınabilir, istersen değiştir):**
- Asensio ve Söyüncü sakatlıklarında iğne yerine fizyoterapist (ücretsiz, risksiz).
- Basın toplantılarına yardımcı gönderildi; iki maç sonu sorusuna ılımlı cevap verildi.
- Avrupa Ligi kadro listesi yardımcının seçimiyle gönderildi; lig listesi transfer dönemi sonunda
  olduğu gibi onaylandı (kurallar tutuyordu).
- Kocaelispor'un Levent Mercan için £1.4M teklifi reddedildi (oyuncu da ilgilenmiyordu).
- Ayarlar ve bütçe denendi, eski değerlerine döndürüldü.

- **`fm settings`** (FINDINGS §8.61): oyunun tercihleri — maç gösterimi, vurgu / ara hızı, dinamik
  mod, otomatik kayıt (aç/kapa, sıklık, tür), dil, para birimi, maaş, tarih, birimler. Çıplak hali
  gösterir ve terminalde okla değiştirtir; `settings <ayar> <değer>`. Oyunda ayar "Confirm"
  basılınca işleniyor; komut onaylıyor ve sayfayı yeniden açıp doğruluyor. Köprüye `/form`
  (metin + kontrol okuma sırasıyla), `/setcontrol` (kaydırıcı, aç/kapa) ve `/click?text=`.
  Denemeler sonunda ayarlar eski haline döndürüldü (1.5x / 30x, Önemli anlar, her hafta).
- **Sözleşme takvimi** (FINDINGS §8.72): `fm contracts` (Defrel'in sözleşmesi Haziran 2026'da bitiyor).
- **Kadro sağlığı** (FINDINGS §8.71): `fm health` sakatlar (süresiyle), yorgunlar, maç formu düşükler,
  sakatlık riski en yüksekler.
- **Turnuva liderleri** (FINDINGS §8.70): `fm league [turnuva]` en golcü takım, gol / asist kralı, en
  yüksek puan; lig, Avrupa Ligi, Türkiye Kupası.
- **Sakatlık durağı** (FINDINGS §8.69): Forest maçında Kanté sakatlandı; maç duraklıyor, değişiklik
  "Confirm Changes" ile işleniyor. `match --play` durağı tanıyıp soruyor, `sub` onaylayıp sürdürüyor.
  Avrupa Ligi ilk maç: Fenerbahçe 1-2 Nott'm Forest. Beşiktaş derbisi 2-3; raporda penaltı golleri
  eksikti (olay türü 2), VAR iptali akışta "GOL" diye yazılıyordu — ikisi düzeltildi.
- **Maç raporundaki gol dakikaları önceki maçtan geliyordu** (FINDINGS §8.68): köprünün liste okuması
  satır düğümlerini yeniden kullanıyordu; artık her okuma kendi satırlarını açıyor.
- Gençlerbirliği deplasmanı 0-4 kaybedildi (sonuncu takım; 22 şut, 1.23 xG'ye karşı 1.81).
- **Gelen teklif** (FINDINGS §8.67): Kocaelispor'un Levent Mercan için £1.4M teklifi; `fm offer` açık kartı
  gösterdi, teklif reddedildi (oyuncu da ilgilenmiyordu). `fm status` / `fm next` maç sonu açık soruyu söylüyor.
  Avrupa Ligi kadro listesi yardımcının seçimiyle gönderildi (`register --send`).
- **Takvim** (FINDINGS §8.66): `fm calendar [ay]` gün gün maçlar ve önemli tarihler; `fm fixtures
  --all` sezonun bütün fikstürü, sonuçlar bizim açımızdan ve renkli.
- **Bütçe** (FINDINGS §8.65): `fm budget` transfer / maaş / scouting bütçesi; `budget ayarla`
  kurulun kaydırıcısının 16 adımını rakamlarıyla listeler, seçileni uygular. Denendi, geri alındı.
- Maç hazırlığını değiştirmek: kartta kontrol yok (antrenman takviminden geliyor olabilir), açık.
  Topsuz takım talimatları: `/scoped` ile sayfa değişkeni yazıldı ama görünüm değişmedi (§8.64), açık.
- **Oyuncu kariyeri** (FINDINGS §8.63): `fm player <isim> --career` sezon sezon kulüp, geliş
  (bonservis / kiralık / bedelsiz), lig, maç, gol, puan ve toplamlar.
- `fm next` her iki basıştan birini oyun yutuyordu; artık boşa giden basışta bir kez daha basıyor.
- **Maç ekranı** (FINDINGS §8.62): `fm shout` doğrulandı (oyun listeyi kapatıyor, anlatım
  söylüyor) ve kapalıyken reddediyor; maç içinde `tactic role <oyuncu> [<rol>]`; `fm dugout`
  yardımcının tavsiyeleri (uygula / kaldır); `match --play` tavsiyeleri akışta yazıyor.
  Feyenoord rövanşı 1-1 (toplam 1-2, UCL'den elendik).

### 2026-10-08 (gece, otonom)

**Kayıtta ne değişti:** 5 Ağustos'ta kaydedildi (eklenti yeniden yüklenmeden önce, ilerleme kaybolmasın
diye). Sonra: Al-Ain hazırlık maçı 4-2 kazanıldı, basın toplantısına katılındı (ölçülü cevaplar),
Feyenoord deplasmanı (UCL 3. ön eleme ilk maç) 1-0 kaybedildi, oyun 6 Ağustos akşamında. Bu son
kısım henüz kaydedilmedi, oyunun otomatik kaydı alabilir.

**Karar bekleyenler (8 Ekim)**

1. **Cancellieri kiralığı.** Celesti iki kez aynı şeyi istedi: aylık £42.5K oynarsa / £190K
   oynamazsa, maaşın %100'ü bizden, sezon sonunda £14.75M'lık satın alma *opsiyonu* (toplam
   "£15.25M'lık anlaşma"). Mesaj (`inbox`'ta "Celesti negotiation over Cancellieri") bloklamıyor,
   açık duruyor. Seçenekler: kabul et (`reply <#> Accept`), geri çek, ya da kartla yeniden pazarlık.
   Önerim: geri çek — oynamazsa ayda £190K bu kadro için pahalı. **7 Ağustos'ta mesaj zamanı
   bloklamaya başlayınca geri çektim** (para harcamayan seçenek; oyuncuya yeniden teklif yapılabilir).
2. **Kaptanlık.** Sezon kaptanı seçilmeden oyun ilerlemiyordu ve kaptan boştu; Škriniar (yardımcı
   kaptandı) kaptan yapıldı. Yardımcı kaptan seçimi oyuna bırakıldı. Başka biri olsun istersen
   Squad › Dynamics › Captains. Yardımcı kaptan boştu; `fm captains` denerken **Kerem Aktürkoğlu**
   yapıldı (oyun bir kez atanınca sayfada değiştirme listesi göstermiyor).

- **Bireysel antrenman** (FINDINGS §8.51): `fm training`, odak / mevki-rol / topsuz rol.
- **Antrenman yoğunluğu ve dinlendirme** (FINDINGS §8.50): `fm intensity`; sanal tabloyu kaydırıp
  okuyan genel `/namedrows`.
- `fm squad --u19`, `fm happiness` (§8.59), `fm prep` (§8.60).
- `fm shortlist` (§8.56), `fm saves`, `fm opponent` (§8.57), `fm captains` (§8.58), `fm depth`
  (kadro derinliği: 10 Ağustos'ta sağ orta sahada oyuncu yok, sol orta sahada tek).
- Oyun bir kez kilitlendi (ana iş parçacığı 30 sn+ cevap vermedi, kaptanlar sayfasıyla uğraştıktan
  sonra); yeniden başlatıldı, kayıt 10 Ağustos'tan geri geldi. Sebebi bulunamadı.
- **Maç içi mentalite ve bağırış** (FINDINGS §8.54). `fm status` transfer dönemini yazıyor (§8.55).
- **Oyuncu talimatları** (FINDINGS §8.53): `fm tactic pi <mevki> [<talimat> [<değer>]]`.
- **Rakip talimatları** (FINDINGS §8.49): `fm opposition`, `--advice`, `--clear`, elle ayar.
- **Takım talimatları** (FINDINGS §8.48): `tactic instructions` topla oynarken; sağ tık kartı.
- **Diziliş değiştirme / taktik yuvaları** (FINDINGS §8.47): `tactic new` taktik varken de,
  `tactic slot`. `players` dar terminale sığıyor, `reply` numaraları son `inbox`'a bağlı.
- **Alt menüler çözüldü** (FINDINGS §8.45). Sebep: oyun seçilen satırın `RowData`'sını bekliyordu,
  biz menü satırını yazıyorduk. Kupa / ödül primleri, takasa oyuncu (transfer ya da kiralık),
  sözleşmede alt menülü maddeler; kartta kademeli menü, script'te `A>B>C`. Prim adları kalıbına
  göre Türkçe ("Turkish Cup: Semi Final primi").
- **Kiralık teklifi kartla** (FINDINGS §8.46): kiralığın kendi başlıkları okunuyor, evet / hayır
  maddeleri, kalıcı olursa toplam; oyunun gri gösterdiği seçenekler menüden çıktı. Cancellieri'ye
  kiralık teklif gönderildi.
- **`fm contract` kart oldu** (FINDINGS §8.44). Teklifteki gibi: vaatler ve şartlar kart olarak,
  okla düzenleme, `--term`, kilitler, ekle / kaldır. `fm contract <oyuncu>` kendi oyuncumuzla
  yenileme görüşmesini açıyor. Menajerin cevabı "neyin değiştiği" olarak yazılıyor; kuralı
  tutmayan şartın sebebi (görüşme kaydındaki `InvalidOrDisabledReason`) satırın yanında. Teklif
  ve sözleşme aynı düzenleme kodunu paylaşıyor (`Terms.cs`). Kılavuz bölümü + üç görüntü.

### 2026-10-07 (akşam)

- **`fm register` artık listeyi elle düzenliyor** (FINDINGS §8.43). Bütün uygun oyuncular,
  yetişme bayrakları, kurallar; terminalde okla, script'te `--in/--out`, MCP'de `add/remove`.
  UCL 3. ön eleme listesi gönderildi (Emre Mor dışarıda, 23 kişi), oyun kaydedildi.
- Kılavuz (`docs/manual/KILAVUZ.md`) ve çekim araçları (`tools/manual/`) eklendi.

### 2026-10-07 (gece, otonom)

**Kazanımlar**

- **Görselleştirme: Spectre.Console.** Terminalde çizim var, pipe/script/MCP'de düz çıktı aynen
  kaldı (`Ui.Rich`). `fm tactic` iki sahayı yan yana çiziyor; rol / oyuncu / Quick Pick
  değişince değişen mevki sarı. Oyuncu kartı: mevki sahası, renkli özellik çubukları, scout
  aralığı gölgeli. Lig tablosu: Avrupa / küme düşme bölgeleri, takımımız vurgulu. Gelen kutusu:
  okunmamış kalın, bekleyen kırmızı, mesajın ilk cümlesi altında. Bütün düz tablolar çerçeveli.
- **Okla seçim.** reply, talk, dialog, sub, tactic new / mentality / style / role / player,
  setpieces new, match, player --actions: eksik seçimi terminalde okla soruyor, son satır hep
  "vazgeç". `talk` konuşmayı baştan sona yürütüyor; basın toplantısı ve beklentiler toplantısı
  böyle oynandı. Her cümleden sonra tepkilerin ne kadar oynadığı görünüyor.
- **Ekran açmadan veri (FINDINGS §8.40) — gecenin asıl kilidi.** `ReadOnlySpan<char>`'ı elle
  kurup oyunun yol alan metodlarını il2cpp'den doğrudan çağırınca `Bindings.Bind(path)` çalıştı.
  `/pull?path=…&props=…[&list=1]` herhangi bir referansın ya da listenin property'lerini okuyor:
  `Human.Team.Name`, sıradaki maç, son maçın istatistikleri, kadronun kondisyonu… Ekranlar
  birbirini bozmuyor, maç sürerken bile okunuyor.
- **Bunun üstüne:** `fm status` takım, menajer ve sıradaki maçı bir panelde veriyor; `fm squad`
  ekransız ve çubuklu; yeni **`fm report`** son maçın skorunu, gollerini, maçın oyuncusunu ve
  istatistiklerini iki taraf yan yana veriyor.
- **Maç günü kararlara göre.** `fm match` terminalde "izle mi, anında sonuç mu" diye soruyor;
  izlerken kadroyu sahada gösterip onay alıyor, ilk 11 yoksa yardımcıya seçtirmeyi teklif
  ediyor, her konuşmada cümleyi sorduruyor (ya da geçiliyor), anlatımı (gol tekrarları ayıklanmış)
  ve takım adlı skoru basıyor, maç sonu raporla bitiyor. Script / MCP: `--play` konuşmada durup
  seçenekleri veriyor (`talk <no>`, sonra `match`), `--no-talk` geçiyor, `--instant` simüle
  ettiriyor. `--talk` (ilk cümleyi söyle) kaldırıldı. MCP'de `mode` artık zorunlu.
- **Transfer teklifi: `fm offer <oyuncu> <bonservis>`** (`--loan`, `--dry`, script'te `--yes`).
  Oyuncu sayfası → değer butonu → Make An Offer → bonservis → Make Offer. İki gerçek teklif
  gitti (Icardi £7.25M → yönetim "rakip kulüp" diye engelledi; Demirbay £1.5M). `fm player
  <isim> --actions` kısa liste, scout, teklif gibi aksiyonları okla sunuyor.
- **Oynanan:** Sarıyer 0-4 (izlendi, üç konuşma seçildi), Boluspor 0-1 (anında sonuç),
  Adana D.S. 0-4 (script modu: konuşmada durdu, `talk 1`, `match --no-talk`), Al-Ittihad 3-2
  yenilgi (anında). Tanıtım turlarının hepsi, basın toplantısı, beklentiler toplantısı,
  kamp kadrosu, davranış kuralları.
- Düzeltmeler: `fm player Kerem` kadrodaki oyuncuyu bulamıyordu (tablo satırları parça parça
  doluyor); taktik sihirbazının ilk "Confirm"ü turda yutuluyordu; `next N` boş adım gürültüsü.

**Kayıplar / engeller**

- **Bonservisi yazmak** uzun sürdü ama sonunda çözüldü (§8.42): kutu tutarı odak kaybında
  alıyor, odak olayları elle üretilince `fm offer` 22 sn'ye indi. Yoldaki denemelerden biri
  oyunun değer nesnesini yerinde değiştirdi ve ana thread kilitlendi; o kod kaldırıldı.
- **Oyun bir kez çöktü**: açıkken plugin DLL'inin üzerine kopyaladım. Kayıttan sonraki Demirbay
  teklifi o yüzden kayıtta yok.
- "Club vision" mesajında **Negotiate** ayrı bir pazarlık ekranı açıyor, `talk` onu cevapsız
  görüyor. Vizyon kabul edildi; pazarlık ekranı keşfedilmedi.
- **Demirbay transferi bozuldu, iki hatayla.** £2.3M reddedildi; Negotiate ile £3M verdim.
  Kulüp pazarlıkla döndü, mesajın içine bakmadan "Accept" dedim: kabul edilen rakam £8.25M'miş.
  Sonra oyun "Negotiate Contract" beklerken genel Continue'ya basınca teklif geri çekildi.
  Düzeltmeler: `reply` artık mesajın ilk cümlesini cevaplardan önce basıyor; `status` bekleyen
  adıma "bas" demiyor.
- **Sonra transfer zinciri baştan sona çalıştı:** Defrel (Modena, listede) £160K → kabul →
  `fm contract`: menajer £9,250 istedi, £7,500 önerildi, kabul, imza; "set to sign" mesajına
  Accept, Defrel kadroda. `fm register` / `fm numbers` sezon kadro listesini ve forma
  numaralarını yardımcıya yaptırıp gönderiyor (terminalde soruyor).
- **Sakat / eksik on bir** artık maçı durdurmuyor: iki maç yolu da "Team Selection" uyarısını
  tanıyıp yardımcıya yeniden seçtirmeyi teklif ediyor (önceden anında sonuç her denemede bir
  uyarı daha biriktiriyordu).
- Fikstür renkleri: `ResultType` 1 galibiyet, 10 yenilgi; beraberlik değeri henüz görülmedi.

**Kayıt dosyasının durumu**

- Gece başında yedek: `~/fm26_bep/save-backups/Faruk Kavlak - Fenerbahçe.2026-10-06-night-start.fm`
  (30 Haziran 2025).
- Ana kayıt `fm save --yes` ile en son **19 Temmuz 2025**'te kaydedildi (Defrel imzasından
  sonra); sonrası oyunun kendi haftalık otomatik kaydına kaldı.

**Sabah geri bildirimi (Faruk, 7 Ekim)**

- `fm offer` "kötüymüş": üçü birden — yavaş (~20 sn), kırılgan (bonservis sahte odak olaylarıyla
  yazılıyor), görünür değil (teklif ekranı terminalde yok, tek satır sonuç). "Böyle şeylerde
  UX'i arttırmamız lazım." Aynı akşam yapıldı (c23ac95, a91cb58): kart, okla düzenleme,
  gönderimden önce onay, ~30 sn → ~5 sn. `fm contract` için aynısı sırada (yukarıda §4).

**Karar bekleyenler**

1. **Otomatik kayıt ayarı**: oyunda "her hafta" açık (`game.AutosaveModeAndIntervalString`).
   Kullanıcıya nasıl sorulsun: `fm settings autosave` gibi bir komut mu, ilk `fm start`'ta bir
   kerelik soru mu? Değiştirmek Preferences ekranından; henüz yazılmadı.
2. **Maç hızı**: `settings.MatchSpeedDuringHighlights` 1.5 (en çok 10). İzlenen bir maç ~10 dk
   sürüyor. Hızlandırmak bir oyun ayarı, kullanıcının seçimi; komutu yazılsın mı?
3. **`fm player` sonrası aksiyon menüsü** şimdilik `--actions` bayrağıyla; kartın altında hep
   sorsun mu, yoksa bayrakta mı kalsın?
4. **Şampiyonlar Ligi kadro listesi (oyun burada bekliyor).** 3. ön eleme (Feyenoord) için liste
   isteniyor; yardımcının 24 kişilik seçimi iki yerli kotasını tutturmuyor (Türkiye'de yetişmiş
   en az 8, kulüpte yetişmiş en az 4). Tutmayan kotanın yeri boş kalmalı, yani yabancılardan
   birkaçı listeden çıkmalı. Kimin çıkacağı senin kararın; `fm register` kuralları ✓/✗ ile
   gösteriyor. Tek tek oyuncu çıkarma/ekleme komutu yok, istersen sıradaki iş o olabilir.

### 2026-10-06 (gece, otonom)

**Kazanımlar**

- **Tanıtım turu çözüldü.** Turun kendi butonları var (Continue / End / Skip Introduction);
  mesajdaki "Continue Introduction" turu ilerletmiyormuş. `fm tour` turu yürütüyor.
- **Taktik turu taktik istiyordu.** Takımın taktiği yoktu; sihirbazla oluşturunca mesaj
  kendiliğinden kapandı. `fm tactic new "<stil>" "<topla>" "<topsuz>"`.
- **`fm next` zamanı ilerletti** — ilk kez, iki ayrı oturumda (30/6 → 1/7/2025).
- **İlk maç oynandı.** Maç gününün on iki durağı çıkarıldı; `fm match --talk` tek komutla
  oynadı (6 dk, 0-0). Onay pencereleri `fm dialog` ile, kaydetme `fm save --yes` ile.
- **Basın toplantısı ve beklentiler toplantısı `fm talk` ile yürüdü**; liste cevaplı soru dahil.
- **`fm mcp`**: komutların hepsi MCP aracı olarak sunuluyor (29 araç); protokol elle denendi.
- **`fm tactic role`** mevkinin topla / topsuz rolünü, **`fm tactic player`** ilk 11'deki oyuncuyu
  değiştiriyor; **`fm squad --view`** kadro tablosunun her görünümünü, **`fm players`** oyuncu
  veritabanını (hazır süzgeçlerle) basıyor; **`fm search`** ve **`fm player`** herhangi bir
  oyuncuyu adıyla buluyor (metin kutusuna yazma çözüldü).
- **`fm match --instant`** maçı 9 sn'de simüle ettiriyor; **`fm sub`** maç içinde oyuncu değiştiriyor.
- **`fm setpieces new`** duran top sihirbazını, **`fm table` / `fm fixtures`** tabloyu ve
  sıradaki maçları, **`fm player <isim>`** oyuncunun sayfasını veriyor.
- **`fm next` bekliyor ve anlatıyor**; oyunun durumu sabit bir yoldan okunuyor (`continue.State`).
- **`fm talk` takım toplantısını yürüttü** (yedi cümle, "End Chat").
- **`fm tactic` taktiği okuyor**, `mentality` / `style` değiştiriyor, `pick` ilk 11'i seçtiriyor.
- **Radyo grupları** (`SIRadioGroup`) `/dropdowns` ve `/select`'e girdi; `/elements` event
  taşımayan kontrolleri bulmak için eklendi.

**Kayıplar / engeller**

- **⚠ Oyun otomatik kayıt aldı.** 7 Temmuz'a geçerken ana kayıt dosyasının üzerine yazdı;
  temiz hal `(v02)` adıyla yanında ve `~/fm26_bep/save-backups/`'ta duruyor (§8.35).
- **Sakatlık maçı durdurdu.** Oyun değişiklik bekledi. `fm sub` sonradan yazıldı ve canlı
  maçta iki değişiklik yaptı, ama sakatlık durağının kendisinde denenmedi.
- **`fm open` bazı sayfaları verisiz açıyordu** (Tactics). Dünkü doğrulama yalnızca panelin
  açıldığına bakmıştı. Artık sekmenin ilk sayfasından alt sekme butonuna basıyor (§8.31).
- **`fm reply`'dan hemen sonraki `fm next` mesajı hâlâ engel sanıyordu**; `reply` artık gelen
  kutusunun güncellenmesini bekliyor.

**Kayıt dosyasının durumu (sabah itibarıyla)**

- Ana kayıt (`Faruk Kavlak - Fenerbahçe.fm`) **30 Haziran 2025'teki temiz haline geri kondu**;
  bayt bayt aynı, yalnızca dosya tarihi değişti.
- Gece iki şey yazdı: oyunun otomatik kaydı (7 Temmuz) ve `fm save --yes` denemesi (maç günü).
  İkisi de `~/fm26_bep/save-backups/`'ta duruyor; `…matchday.fm` maç denemeleri için hazır.
- Oyun klasöründe oyunun kendi bıraktığı iki dosya var: `… (v02).fm` (temiz halin kopyası) ve
  `last save overwrite backup.fm` (7 Temmuz otomatik kaydı). Silinmedi.
- Maç günü kaydındaki kariyerde özensiz verilmiş sözler var (şampiyonluk, kupa, kaleci
  transferi): toplantı "ilk seçeneği söyle" diye yürütüldü.

**Bekleyen kararlar — 6 Ekim sabahı cevaplandı**

1. Otomatik kayıt: ayar olacak, kullanıcı seçecek.
2. `fm match` oynat / geç: ikisi de seçenek olarak sunulacak.
3. MCP araçlarının biçimi: konuşulmadı, açık.
4. Konuşmada ilk cümleyi söylemek: anlamsız, kullanıcı seçecek.
5. Sırada ne var: UX ve görselleştirme (yukarıda "Ürün kararları").

### 2026-10-05

**Kazanımlar**

- **Her kontrol kendi event'ini tarif ediyor.** Oyunda her UI event'inin bir `…EventContext`
  sınıfı var; hangi binding yolunun hangi alanı doldurduğunu o tutuyor. `/buttons` bunları
  ekrandaki bütün kontroller için döküyor (portal: 130 kontrol, kadro: 236).
- **Basmak çalışıyor.** `/press` kontrolün kendi event listesini `UIEventQueue`'ya koyuyor,
  payload'ı oyun dolduruyor. Kadro/Portal sekmeleri arasında üç tur: ağaç her seferinde
  4198 → 6093 → 4198. Kuru çalıştırma ve uydurma adres hiçbir şey değiştirmedi.
- **Simülasyon katmanı canlı doğrulandı.** "Tümünü okundu işaretle" butonu bir
  `EventToPluginContext` (`EventFmxMarkAllHumanMessagesAsRead`); basınca "Unread (10)" →
  "Unread (0)". Tek gözlem: tekrarı yok çünkü işlem bir kez yapılabiliyor.
- **Plan ve bulgular ayrıldı:** sıralı iş listesi burada, `FINDINGS.md` §9 buraya yönleniyor.

- **`fm squad --fitness` ve `--morale` çıktı** — en eski açık madde. Veri kadro özetinde
  değil, "First Team" tablosunun görünümlerindeymiş; görünüm seçici event taşımayan bir açılır
  liste olduğu için `/dropdowns` + `/select` eklendi. Soğuk başlangıçtan 5-7 sn; fitness, inbox,
  morale, düz kadro sırasıyla üç tur, hepsi aynı sonuç.

- **Sakat oyuncu `Condition = -1` veriyor**; "Injuries" görünümünden doğrulandı, fitness
  tablosuna durum kolonu eklendi.
- **Ekran CLI'dan gezilebiliyor:** `fm buttons`, `fm press "<etiket>"`, `fm dropdowns`,
  `fm select <liste> "<seçenek>"`. Etiketlerdeki `<link>`/`<style>` markup'ı temizlendi; art
  arda komutlar ekranın kurulmasını 8 sn'ye kadar bekliyor.
- **Ekran haritası çıktı:** altı ana sekmenin 77 alt sayfası `docs/reference/screens.txt`'te.

- **Sayfalar adıyla açılıyor.** Menü satırı açacağı panelin asset GUID'ini taşıyor;
  `OpenBookmarkEvent` panel nesnesi değil asset referansı aldığı için yüklemeyi oyun yapıyor.
  Altı sekmeden on bir açılış denendi (ikisi tekrar), hepsi açıldı, kuyruk boş kaldı.
  `fm squad --fitness` soğuk başlangıçtan 4,8 sn yerine 2,2 sn.
- **`fm squad` artık hep aynı şeyi basıyor:** "General Info" görünümünden maaş, değer, statü.

- **`fm reply` çıktı.** Mesajı açınca cevapları buton olarak geliyor (`SpeakToActionEvent`,
  basın toplantısı için beklenen event'in aynısı). Üç mesaj cevaplandı, her birinde bekleyen
  sayısı bir düştü (5 → 2). Elle kayıt kopyalayan `/newsaction` silindi.
- **Hangi mesajın blokladığı düzeldi:** `NewsItemIsPriority`. "Pre-season Preparation" yalnızca
  isteğe bağlı bir cevap sunuyor, artık "cevaplanabilir" diye görünüyor.

**Kayıplar / engeller**

- **Basın toplantısı görülmedi** — kayıtta bekleyen yok. Sıradaki işlerde duruyor.
- **Kontrol adresleri ekran değişince değişiyor.** Çözüldü: `/press?text=` ve
  `/select?name=&choice=` etiketle buluyor; etiket birden çok kontrole uyarsa reddediyor.
- **Test tıklamaları kayda yazılmadı.** Oyun gün boyu kaydedilmeden yeniden başlatıldı; "okundu
  işaretle" dahil hiçbir değişiklik kalıcı değil.
- **Kondisyon -1 = sakat.** Talisca (hamstring) ve Becão (ön çapraz bağ); "Injuries"
  görünümünden doğrulandı, `fm squad --fitness` durumu ayrı kolonda gösteriyor.
- **Menüden seçerek sayfa açılamadı** (seçmek, menünün event'lerini kuyruğa koymak,
  `ProcessSelectionEvents` — üçü de ekranı değiştirmedi). Menünün verisinden event kurmak
  çözdü, yukarıda.
- **`fm squad` önceki komuta göre farklı kadro basıyordu** (47 ya da 27 satır): hangi liste
  bağlıysa onu okuyordu. Sabit bir görünüme bağlandı; altyapı kadrosu bu arada düştü.
- **Tanıtım turu bitirilemedi.** "Begin Introduction" turu başlatıyor, mesaj açık kalıyor;
  bu kayıtta `fm next` buna takılıyor. Dünkü `/newsaction` aynı cevabı farklı bir event'le
  gönderip mesajı kapatıyordu, yani buton yolu oyunun gerçek davranışı ve daha uzun.
- **Bir deneme oyunun buton kuyruğunu tıkadı.** Menünün seçim event'ini elle kuyruğa koymak
  çözülemeyen bir event bıraktı; arkasındaki bütün basışlar "gönderildi" deyip hiçbir şey
  yapmadı. `/queue` ve `/press`'teki koruma bunun için eklendi (§8.27).
- **Alt sekmenin açılır butonu da açmıyor.** "Dynamics" butonunun `LeftClickEvents`'i kuyruğa
  girdi, ekran değişmedi. Düz alt sekme butonları ("First Team") ve pano kutuları
  ("Medical Centre") açıyor.

### 2026-10-04

**Kazanımlar**

- **Veri modeli büyüdü:** bazı referans tipleri `InteropReference`'tan türemiyormuş; filtre
  düzelince 160 tip / 6571 property → 931 tip / 14504 property.
- **Canlandırma çözüldü:** ekran açmak şart değil, ekranın göndereceği event'i göndermek yetiyor.
  Kadro `SquadScreen` isimle açılarak geldi.
- **Mesaja cevap verildi, zaman ilerledi.** `/newsaction` aksiyonun kendi kaydını kopyalıyor;
  dört bloke mesajdan sonra saat 08:45 → 12:55.
- **`fm` CLI çıktı:** `start`, `continue`, `squad`, `inbox`, `next`, oturum (REPL), `--json`.
- **Oyun tamamen gizli:** `LSUIElement` + pencereli başlatma + küçültme, üçü birden.
- **Aksiyon yüzeyinin tamamı statik çıktı:** 680 simülasyon event'i, 225 `ActionID`, 2160 enum.
  Taktik, transfer, basın, konuşma, antrenman alan alan `COOKBOOK.md`'de.
- **`/send` katalogları kullanıyor:** enum isimle, `TypedValue` scalar'dan, `dry=1` ile kuru
  çalıştırma. Generic il2cpp çağrıları arm64'te çalışıyor.

**Kayıplar / engeller**

- **Oyun headless çalışmıyor.** `-batchmode -nographics` ile UI boot etmiyor, binding gelmiyor.
- **Aynı anda tek ekranın verisi bağlı** (§8.22) — her komut kendi ekranını açmak zorunda.
- **Ekran açmadan property canlandırma yarım** (§8.21) — span köprüsü yine duvar.
- **Kondisyon/moral hâlâ yok**, kadro ekranı bağlamıyor.
- **Oyun bir kez daha çöktü:** event alan alan doldurulunca `ResponseActionDataReferences` null
  kaldı. `dry=1` ve "çözülemeyen alanda gönderme" bunun için eklendi.
- **İki yanlış pozitif:** pencere gizleme `visible=false` dönüp ekranda kaldı; `fm inbox` bir kez
  dolu gelip sonra boş geldi.

**Sıradaki adım:** en üstteki "Sıradaki işler" listesi.

### 2026-09-28

**Kazanımlar**

- **Oyunu CLI'dan kariyere soktuk.** `curl /continue` → FM kaydı yükledi, ağaç 186 → 4198 düğüm.
  Projenin "yazma" tarafı kanıtlandı.
- **`GameSubsystems` bulundu.** Artık `Bindings` ve `EventSubsystem` tek, güvenilir yerden geliyor;
  boot aşamasına göre kırılan ScriptableObject avı bitti.
- **Kadro listesi bulundu:** `GameStateCache.Manager.Club.MainTeam.Players.ListLoader0.<0..1A>`,
  27 oyuncu.
- **Referanslar kendini tanımlıyor:** bir oyuncu 1245 özellik listeliyor.
- **Geliştirme döngüsü kapandı.** Derle → kur → başlat → `/continue` → keşfet, hepsi tek tarafta.
  Artık her deneme için kullanıcıya "şuna bas" demeye gerek yok.
- **`tools/sigdump`** — dump'a değil interop'un gerçeğine bakan araç. İki yanlış yolu bu çözdü.

**Kayıplar / engeller**

- **`ReadOnlySpan<char>` köprüsü çalışmıyor** (§8.2). İki yöntem de sessizce boş span üretti.
  Sonuç: path string'i ile `Get`/`Exists`/`Bind` **kullanılamıyor**.
- **Bunu geç fark ettik.** `Exists("Game") = true` yanlış pozitifti ve bir hafta doğru sanıldı.
  Ders: bir lookup'ın uydurma girdiyi reddettiğini de test et.
- **Oyuncu özellikleri henüz canlanmıyor.** Ağaç tembel; düğümlerde sadece `UniqueId` var.
- **Bir kez oyunu çökerttim** — `Bind`'e null delegate verdim.
- **Property ID'leri okunaksız** — bir kısmı FourCC (`NEWS`, `gend`), çoğu hash; release build'de
  açıklamalar ayıklanmış.

**Sıradaki adım** (yapıldı, 2026-10-04)

Squad ekranını event/panel ile açtırmak. UI'ın kendisi oyuncu özelliklerini canlandırıyor; biz de
ağaçtan okuruz. Span'a gerek yok, ekranın görünür olması da gerekmiyor.
