# fm-cli — Yol Haritası

> FM26'da yapılabilen her şeyi CLI komutuna çevirme listesi.
> Durum: `[x]` çalışıyor · `[~]` kısmen · `[k]` katalogda var, canlı denenmedi · `[ ]` yapılacak ·
> `[?]` yapılabilirliği belirsiz

`[k]`: verisi ve olayı metadata'dan çıkarıldı (`docs/reference/COOKBOOK.md`), canlı doğrulama ve komut kaldı. İşaretsiz
`[ ]` katalogda henüz aranmadı demek, yok demek değil. 14 Ekim 2026'da sadeleştirildi; uzun hali `git show 515eef0:PLAN.md`.

---

## Şu an (14 Ekim 2026)

- **İlk öncelik taşınabilirlik** (Faruk, 13 Ekim): dil / ayar / lig / kayıt / makine / sürüm / platform bağımlılıklarını
  kaldırmak; yeni özellik yok. İş listesi `docs/BAGIMLILIKLAR.md` en üstteki CHECKLIST; oturumda madde madde
  ilerleniyor, her madde bitince Faruk'a haber verilip clear atılıyor (otomatik döngü 14 Ekim'de kaldırıldı).
- Yeni bir alan açmanın aracı: okuma için `/pull?path=&props=[&list=1]` (ekransız, §8.40; değişken isteyen özellik
  `/contexts` + `/pathset`, §8.129); tıklama için `fm open`, `fm buttons`, `fm press`, `fm dropdowns`, `fm select`.
- Eski "Sıradaki işler (7 Ekim)" ve "Bilinen hatalar" listelerinin hepsi bitti ya da aşağıdaki bölümlere taşındı.

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
- [x] Forma numaraları — `fm numbers`: kim kaç giyiyor, istediği numara, boş numaralar, oyun değişiklik alıyor mu (§8.126); `fm numbers <oyuncu> <no> --send` canlıda doğrulandı: Cancellieri 19 (§8.141); kaydedilince kilitleniyor
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
- [x] Oyuncu mutluluk durumu — `fm happiness` (§8.59); nedenleri `fm happiness <oyuncu>` ve `--why` (§8.127)
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
- [x] Oyuncu talimatları — `fm tactic pi <mevki>`: topla on bir, topsuz dört talimat, rolden gelenler işaretli, kart
      açmadan (§8.53, §8.133); "oyuncuya özel" (Apply to: Player) ve pas / orta stratejisinin ek ayarı yok
- [x] Takım talimatları — `fm tactic instructions [--oop]`: topla dört evre ve topsuz dokuz talimat, sayfa açmadan okuma
      ve değiştirme (§8.132); savunma çizgisi / kaleci dağıtımı / destek koşularının ek ayarı ve bölge ("Zone") görünümü yok
- [x] İlk 11'e yedeklerden oyuncu koy — `fm tactic player <mevki> <oyuncu>` (§8.31)
- [x] Yedek kulübesini göster — `fm tactic`'in "Yedekler" satırı
- [ ] İki as oyuncunun yerini değiştirme, kadro dışından oyuncu alma (adları taktik ekranında bağlı değil)
- [x] Duran top rutinlerini kur — `fm setpieces new`, oyunun altı soruluk sihirbazı (§8.36)
- [~] Duran top düzenlerini okuma / sonradan değiştirme, penaltı sırası — `fm penalties` sırayı ve üç kuralı okuyor, kuralları değiştiriyor (§8.87), `penalties move / add / remove` sırayı değiştiriyor (§8.90); `fm takers` korner, serbest vuruş ve taç atıcılarını okuyor ve değiştiriyor (§8.91); `fm setpieces <tür>` vuruş tipi, hedef ve oyuncu görevlerini okuyor ve değiştiriyor (§8.93); savunma senaryosu, rutin ekleme / kaydetme ve yerler arası sürükleme yok
- [x] Kaptan / yardımcı kaptan — `fm captains [--captain X] [--vice Y]`: okuma ve değiştirme; yardımcı günde bir kez değişebiliyor (§8.58, §8.134)
- [x] Taktik kaydet / yükle / kopyala — `fm tactic copy`, `fm tactic clear <n> --yes` (§8.77); dosyaya `fm tactic save / saved / load` (§8.107)
- [x] Rakip talimatları — `fm opposition`: tablo, yardımcının önerisi, temizle, oyuncu başına ayar (§8.49)
- [x] Taktik akıcılığı (familiarity) — `fm tactic` takımın ve on birin yüzdesi (§8.80); evre evre döküm yok

---

## 4. Antrenman

> Katalog: COOKBOOK §5.

- [x] Haftalık antrenman programı göster — `fm schedule`: bu hafta ve gelecek hafta, gün gün üç seans (§8.73)
- [x] Antrenman programı değiştir — `fm schedule set <gün> <1-3> [<seans>]` tek seans, `fm schedule style <gün> [<program>]` bütün hafta (§8.89); kendi programını oluşturmak ("Create New Schedule") yok
- [x] Maç hazırlığı odağı — `fm prep` dört odağı ve seçeneklerini okuyor, `fm prep <topla | topsuz | duran1 | duran2> [odak]` değiştiriyor; ekransız (§8.130)
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
- [~] Kiralık — `fm offer --loan`: kart, şartlar, satın alma opsiyonu, gönderildi (§8.46); cevabı görüldü ve kiralama
      tamamlandı (Cancellieri, §8.141). Eksik: kabulden sonra oyuncuyla görüşmeyi kendimiz açmak (mesajda yalnız "Delegate"
      vardı, DoF'a bırakıldı), son onay mesajı ("set to move … Accept") `reply` ile yapıldı; kiralık uzatma görülmedi
- [x] Oyuncuyu kulüplere öner — `fm propose <oyuncu> [ücret] [--loan] [--yes]` ("Offer via TransferRoom", §8.94); hedef kulüp seçmek ve ek maddeler yok
- [x] Gelen teklifler — `fm offers`: cevap bekleyen teklifler; `view` / `accept` / `stall` / `reject` (§8.98). Mesajı gelen kutusundan düşmüş teklif de buradan cevaplanıyor; birden çok oyuncuya gelen teklifler şartlarıyla listeleniyor, satış ve kiralama canlıda kabul / beklet / ret edildi; kiralık gönderme "loan completed"e kadar izlendi (§8.161, 23–29 Haziran 2026)
- [x] Sözleşme feshi / serbest bırakma — `fm release <oyuncu> [--free|--mutual] [--yes]` (§8.76); `--free --yes` canlıda işledi, oyun gün ilerleyince uyguluyor (§8.136); `--mutual --yes`in sonucu görülmedi
- [x] Serbest oyuncular — `fm players --free` ("Contract Expired" süzgeci, §8.92)
- [x] Ön anlaşma sözü — `fm contract --term "<vaat>"` vaat ekliyor, `=kaldır` çıkarıyor ("Add Promise", `EventFmxAddPreContractPromise`; 18 Ekim 2025 oyun gününde Defrel kartında doğrulandı)
- [x] Menajerle görüşme — `fm contract <oyuncu> --agent`, sonra `talk` (§8.76)
- [x] Transfer bütçesi ↔ maaş bütçesi — `fm budget ayarla` (§8.65); bütçe artışı istemek yok
- [~] Takas teklifi — kartta "+ Takasa oyuncu ekle", transfer ya da kiralık (§8.45); takaslı
      teklif gönderilip cevabı görülmedi
- [x] Transfer dönemi durumu / kalan gün — `fm status` (§8.55)

---
- [x] İlgi duyurma, kulübe sorma, denemeye çağırma — `fm interest`, `fm enquire`, `fm trial` (§8.119); ilgi haber oluyor, deneme reddi ve sorunun cevabı ("£6,25M istiyorlar") gelen kutusuna düşüyor; kabul edilen deneme uçtan uca: "set to go on trial" → Accept → kadroda "Free Agent (denemede)", bitince "trial coming to an end" + rapor (§8.148)

## 6. Scouting

- [x] Scout raporları — `fm scout <odak>`: odağın bulduğu oyuncular, not ve tavsiyeyle (§8.109); `fm player <ad> --report`: oyuncunun tam raporu — not, yetenek / potansiyel, ilgisi, değeri, artılar / eksiler, kadrodaki sırası (§8.123)
- [~] Scout ata (bölge/oyuncu/takım) — oyuncuya: `fm scout player <ad> [--by <scout>] --for <süre>`, `scout who` (§8.119); iptal oyunun kendi işleyicisiyle de işlemiyor (§8.124); bölge / takım odak sihirbazında (§8.109)
- [x] Scout odakları (recruitment focus) — `fm scout`: odaklar, durum, atananlar, sayılar (§8.109); `scout new` (mevki, rol, bölge, yaş, ad, öncelik), `scout remove` / `restart`, `scout areas` (§8.120); en düşük yetenek / potansiyel (`--ability`, `--potential`), belirli scout (`--by`), `scout edit` (§8.124); analist seçimi yok
- [x] Kısa liste — `fm shortlist` (§8.56); `shortlist add <ad> --for <süre>` / `remove <ad>` (§8.119)
- [x] Oyuncu karşılaştırma — `fm compare <a> <b>`: kimlik ve özellikler yan yana, iyisi işaretli
- [x] Rakip analizi — `fm opponent`: raporun özeti (§8.57), sezon sayıları ve hüküm (§8.114), menajer ve oyun tarzı, beklenti, birim birim analiz ve öne çıkan oyuncular (§8.121)
- [x] Gençlik adayları / youth intake — `fm youth`: altyapı notu, A takıma aday görülenler, alım tarihi (§8.112); `fm youth --training` U19 antrenman haftası, alım adayları gelene kadar "henüz yok" (§8.125); `fm youth candidates` alım listesi: grup, yaş, mevki, rol, yetenek ve potansiyel aralığı (§8.152); adaya sözleşme önermek oyundan

---

## 7. Maç

- [x] `fm next` — ilerle, bloke eden mesajları ve oyunun ilerle butonunun ne dediğini bildir
- [x] `fm next [n]` zamanı ilerletiyor, oyunun durmasını bekliyor, tarih/saat ve yeni mesajları
      bildiriyor; oyunun kendi durumunu okuyor (`continue.State`, §8.32)
- [x] Konuşma durağında cümleyi kullanıcı seçer — `--talk` kalktı; `fm match --play` her konuşmada sözü soruyor
- [x] Maç gününü oyna — `fm match`: izlemek mi anında sonuç mu diye sorar; kadro gönder, onay penceresi, konuşmalar, maç,
      çıkış; gol/devre/sonuç bildiriyor (§8.34)
- [x] Maç içi oyuncu değişikliği — `fm sub <çıkan> <giren>`; `fm sub` sahayı ve kulübeyi listeler (§8.34)
- [x] Sakatlık durağı — canlıda görüldü (§8.139, Alanyaspor 5-1): `sub` değişikliği verdi, `match` Confirm'e basıp sürdürdü; `sub` onay butonunu bekleyip kendisi basıyor, durakta doğrulandı (§8.151); `sub` listesi sakatı, kartları ve maç puanını yazıyor, maç durunca kimin sakatlandığı söyleniyor
- [x] Anında sonuç — `fm match --instant`, 9 sn (§8.34)
- [x] Maç hızı — `fm settings vurgu hızı` (en çok 10x), `ara hızı` (en çok 40x), `maç gösterimi` (§8.61); maç içinden değiştirmek yok
- [~] Maç içi taktik değişimi — mentalite ✓, bağırış ✓ (`fm shout`, §8.62), rol ✓ (`fm tactic role
      <oyuncu>` maç sırasında), takım ve oyuncu talimatları ✓ (`tactic instructions`, `tactic pi <oyuncu>`; ilk ölü topta
      uygulanıyor, §8.137); oyuncuya özel bağırış yok
- [x] Maç öncesi, devre arası ve maç sonu takım konuşması — `fm talk` ya da `fm match --talk`
- [~] Canlı maç takibi — `fm match --play` anlatım satırlarını, golleri ve skoru akıtıyor; maç ekranının kalanı §14
- [x] Maç içi: rol değişimi, shout (§8.62)
- [x] Yardımcının maç içi tavsiyeleri — `fm dugout` okuyor, uygula / kaldır (doğrulama: Do it
      kapanıyor / tavsiye düşüyor); `match --play` yeni tavsiyeyi akışta yazıyor (§8.62)
- [x] Maç sonrası — `fm report`: skor, goller, maçın oyuncusu ve notu, istatistikler; basın toplantısı `fm talk`
- [~] Maç istatistikleri — `fm report` topla oynama, şut, isabet, xG, korner, faul; pas / ısı haritası yok
- [~] Highlight özeti (metin) — `match --play` sonunda kartlar (biz / rakip), sakatlananlar, oyuncularımızın puanları (§8.151); goller `report`'ta; dakika dakika önemli anlar listesi yok
- [?] Maç içi gerçek zamanlı yönetim (v2 — bambaşka problem)

---

## 8. Lig, kupa, takvim

- [x] Lig tablosu — `fm table` (§8.37); puanlı tabloda denenmedi
- [x] Fikstür — `fm fixtures` son ve sıradaki maçlar; `fm fixtures --all` sezonun tamamı (§8.66)
- [x] Başka ligler / ülkeler — `fm stages "<turnuva>"` / `fm table <turnuva>`: dünyadaki her turnuvanın tablosu ve turları (§8.105); `--list` hepsini sayar
- [x] Kupa ağaçları — `fm stages "<kupa>" [tur] [--all]`: eşleşmeler, skor, penaltı / uzatma, toplam skor (§8.105)
- [~] Avrupa kupaları — `fm table europa` lig aşaması tablosu (36'nın 33'ü, §8.100), `fm league <turnuva>` liderler; eleme turları `fm stages` ile (§8.105)
- [x] Gol krallığı / asist / form tablosu — `fm league [turnuva] --stat <ad> [--teams]`: 127 oyuncu, 100 takım istatistiğinde tam sıralama, ekransız (§8.129); `fm league` liderler
- [x] Takvim — `fm calendar [ay]`: maçlar, transfer dönemi açılış / kapanış, kupa kuraları, milli maçlar (§8.66)
- [x] Kulüp vizyonu toplantısı — `fm vision`: yönetimin hedefleri, çıkar / öner / geri al / onayla / bitir; yönetimin cevabı (§8.160). Kabul edilen öneri ve 5-Year Plan sekmesi görülmedi
- [~] Sezon özeti / ödüller — sezon sonu mesajları `reply` ile satır satır okunuyor: End of Season Review (beklenti, transferler, yönetim / taraftar görüşü, mali öngörü), yeni bütçeler, kulüp vizyonu hedefleri, sezonun sakatlık raporu (§8.159, 4 Mayıs 2026); ayrı bir `fm season` komutu ve ödüller yok

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
- [x] Personel sorumlulukları (delegasyon) — `fm responsibilities`: 29 personel işi kimde, `delegate` / `take` (§8.111), belirli kişiye `give` (§8.135); personel dışı devirler (antrenman, transfer, basın) bu sayfada değil
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
- [~] Oyuncu şikâyetlerini yönet — "<oyuncu> asks to discuss personal matters" `reply` ile açılıyor (Discuss / Decline / menajere ya da kaptana sor), konuşma `talk` ile sürüyor; fesih şikâyeti ikna edildi (§8.149). Başka şikâyet türleri (oynama süresi, söz) görülmedi
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
- [x] Transfer fırsat taraması — `fm targets`: bütçe + ince mevkiler; `fm targets <mevki>` o mevkide bütçeye sığanlar (§8.131)
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
- [ ] **Canlı maç ekranı — FM'in 2D / 3D / yalnız anlatım seçenekleri gibi, birkaç kaliteli görünüm ve kişiselleştirme**
      (Faruk 11 Ekim gece: "UX'i önemli"). Bugün `match --play` aşağı akan düz anlatım: skor sabit değil, istatistik yok,
      müdahale için ikinci terminal gerekiyor, anlatım çok kalabalık, sakatlık durağında ekran sessiz kalıyor.
      Hedef: gerçek terminalde tek ekranlık panel, script / MCP için bugünkü düz çıktı aynen kalsın.
      - Görünümler (3-4 tane, maç içinde tuşla geçilsin):
        1. **Panel** — üstte sabit skor / dakika / yarı + istatistik şeridi (topla oynama, şut, isabet, xG, kart);
           ortada yalnızca önemli olaylar (gol, kart, sakatlık, değişiklik, yardımcı tavsiyesi) kalıcı; altta anlatımın son
           2-3 satırı soluk akar; en altta tuş çubuğu.
        2. **Saha (2D benzeri)** — ASCII / Unicode saha, top hangi bölgede, iki takımın dizilişi, baskı / momentum
           çubuğu; olay olunca kenarda tek satır.
        3. **Yalnız önemli anlar** — anlatım yok; gol, kart, sakatlık, tavsiye ve devre / maç sonu özeti (hızlı izleme).
        4. **Tam anlatım** — bugünkü akış, ama üstte sabit skor satırıyla.
        5. **ASCII 3D** (Faruk 13 Ekim 2026; şimdi yapılmayacak, göz önünde dursun) — maçın 3D görüntüsü terminalde
           karakterlerle: sahaya eğik bakan kamera, oyuncular ve top derinlikle küçülen / parlaklığı değişen
           karakterler, üstte yarı saydam skor / durum kutuları. İlham: karakterlerle çizilmiş bir "ASCII MMORPG"
           videosu (3D sahne karakter ızgarasına dökülüyor, ~140 FPS, HP / MP çubukları üstte). İki yol var:
           (a) oyunun kendi 3D maç görüntüsünü kare kare alıp (`/shot` benzeri, küçük çözünürlük) parlaklığa göre
           karaktere çevirmek — veri gerekmez ama kare hızı ve gizli pencerede çizim sınanmalı; (b) oyuncu / top
           konumlarını okuyup kendi basit 3D sahnemizi çizmek — konum verisi okunabiliyorsa. Önce konum verisi var mı
           ve `/shot` saniyede kaç kare verebiliyor, o ölçülecek.
      - Tuşlar: değişiklik, bağırış, taktik / mentalite, maç hızı, görünüm değiştir, rapor, duraklat. Sakatlık durağında
        panel kendisi sorsun ("Brown sakatlandı, kim girsin?").
      - Kişiselleştirme: kullanıcı panelden parça ekleyip çıkarabilsin (istatistik şeridinde hangi sayılar, olay
        listesinde hangi türler, anlatım satırı sayısı, renkler, tuşlar, varsayılan görünüm); ayar dosyası
        (`~/.config/fm-cli/match.toml` gibi) + `fm settings` içinden okla düzenleme. Varsayılan dayatma yok.
      - Gereken veri: maç sırasında canlı istatistik (`fm report`'un okuduğu yer maç içinde de dolu mu), topun konumu /
        bölgesi (2D görünüm için — maç motorundan ne okunabiliyor bakılacak), momentum.

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
- [ ] Sezon arasında `opponent` ham hata veriyor ("no page named 'Next Opposition Report' under 'Match Day'") ve `opposition` "kart açılmadı" diyor: sıradaki maç hazırlık maçıyken rakip raporu sayfası yok; "sıradaki maç hazırlık maçı, rapor yok" demeli (`tools/sweep.sh`, 29 Haziran 2026)
- [ ] `status` kadrodaki sakatı ancak taktik ekranı açıkken görüyor: maç günü ilk `match` denemesinden önce uyarı yok, deneme "X is injured" diye durunca var (3 ve 11 Nisan 2026, McGrandles ve Ring). Seçili 11'i ekran açmadan veren bir alan bulunmalı (`PlayerReference`'ta aday çıkmadı) ya da `next` maç gününe varınca kadroyu kendisi yoklamalı
- [ ] `fm start` açılışta pencereyi gizlemiyor — pencere tam ekran boyutunda öne geliyor ve kullanıcı başka pencereye geçene kadar ekranda kalıyor (Faruk, 13 Ekim: "dikkatim dağılıyor"). Ölçüm: pencere 5. saniyede çıkıyor, `Minimise()` ancak ana menü gelince (~60. sn) çalışıyor; `-screen-width 640 -screen-height 360` dikkate alınmıyor (ekran 2940×1840). Denenen ve işe yaramayan: açılış boyunca 0,2 sn'de bir `AXMinimized` yazan betik — pencere menü gelene kadar küçülmedi (geri alındı). Bakılacaklar: Unity `PlayerPrefs`'te çözünürlük/pencere konumu, eklentiden açılışta pencereyi küçültmek/taşımak, `open -g -j` ile başlatmak, ayrı bir Space

---

---

### Taşınabilirlik: başka kulüp, tema, dil (Faruk 13 Ekim 2026)

Komutlar tek kayıtta yazıldı; Lincoln'de dördü kırık çıktı (§8.155). Döküm ve iş listesi `docs/BAGIMLILIKLAR.md`.
Faruk'un kararı: bağımlılıkları kaldırmak her şeyin önünde, yeni özellik yok; adım adım, oyunda deneyerek. Dil → oyunun
kendi çeviri tablosu (§8.167); gösterim oyunun dilinde (§8.173–8.174); sıra numarasıyla okuma kalkacak; kimliği olan her
şey kimlikle (sayfa → Guid, düğme → olay, lig → `CompetitionType`, durum → sayı/enum).

**Kural — oyuna nasıl tutunulur (yeni ve değişen her kodda):**
1. Önce dilden bağımsız kimlik: veri yolu (sayı / enum), olay adı, panel id / Guid, oyunun kimlik numarası.
2. Kimlik yoksa ekran yazısı, ama İngilizce sabit değil, oyunun çeviri tablosundan geçerek; yerinde neden kimlik
   bulunamadığı yazılır.
3. Sıra numarasıyla okuma (`rest[i + 2]`) yapılmaz; değer adıyla / yoluyla bulunur.
4. Para birimi, tarih biçimi, birim, lig / altyapı adı varsayılmaz; oyunun ayarından ya da verisinden okunur.
5. Diske yazılan önbellek hangi kayda ve dile ait olduğunu taşır.

- [ ] **Başka ülke, başka lig:** yeni kariyer (Almanya), önce `tools/sweep.sh`, sonra oynayarak saha testi; farklar
      FINDINGS'e. Lincoln kaydı yedekte kalsın.
- [ ] **Tema / skin, çözünürlük, arayüz ölçeği:** kontrol adlarını, ağaç derinliğini, sanal tabloların satır sayısını
      değiştiriyor mu; en az bir başka tema / ölçekle `tools/sweep.sh`.
- [x] Oyun dili: sayfalar Guid'le, çeviri katmanı, yazının veri yolu, motor yazıları, para/yüzde biçimi, gösterim ve
      CLI etiketleri oyunun dilinde (§8.162–8.177); Türkçe ve İngilizce tarama 49/50 (`league` sezon öncesi).
- [ ] Kalanlar BAGIMLILIKLAR CHECKLIST'te (konumla okumalar, zamanlama, platform §6.5).

## 15. MCP + doğal dil

- [x] MCP server — `fm mcp` (stdio), 34 araç, her araç bir komut (§8.39); gerçek istemciyle denenmedi
  (11 Ekim: 95 araç; eksik: compare, diff, export, interest, enquire, trial. Açık kalan sunucu eski derlemeyi tutuyor —
  her çağrıda güncel `fm`'i çalıştırmak düşünülmeli)

### Doğal dil katmanı — karar ve araştırma (11 Ekim gece; **henüz başlanmayacak**, normal plan önce)

Faruk'un tanımı: kullanıcı doğal dilde konuşsun, sistem doğru komutu (function call) çalıştırsın — **başka bir şey değil**
(yapay zekâ kendi kafasına karar vermez, oyunu yönetmez). **Yerel çalışmalı**: oyun için kimse para ödemek istemez, API
anahtarı / hesap / internet istememeli. Değiştiren işlerde (teklif, satış, kayıt, numara…) önce ne çalışacağını gösterip onay.

Hedef yapı (katmanlı; bir katman çözemezse bir sonrakine):
1. **Komut aynen yazıldıysa** model yok (bugünkü hal).
2. **Kural katmanı** — sık kalıplar ("sıradaki maç", "X'i sat", "kısa listeye ekle") basit eşleştiriciyle.
3. **Yerel yönlendirici** — cümle → kategori → komut, sonra argümanlar:
   - Argümanların çoğu sayılabilir (kadrodaki isimler, mevkiler, mentalite/rol/talimat listeleri): mevcut isim
     eşleştirici (aksansız, yazım hatası) + sözlük ("sol kanat" = AML). Serbest olanlar (bonservis rakamı, başka
     kulüpten isim) ayrıştırılır ya da sorulur.
   - Güven düşükse "şunu mu demek istedin?" ile 2-3 seçenek sun; tahmin etme.
   - Tek cümlede iki istek ("Kerem'i sola al, Musaba girsin"): "ve / sonra" bölücüsü.
4. **(İsteğe bağlı) bulut** — yalnızca kullanıcı açarsa: Claude Haiku 5.5 (liste fiyatı $0.10 / $0.50 milyon token,
   tahmini sezon ~$1) ya da TypeSafe **Jev** (karar modeli, ~$0.042 / milyon girdi, çıktı ücretsiz; erken erişim
   Eylül 2026, barındırılan API; serbest metin argüman dolduramıyor — `UnfillableRoute`; Pydantic AI'da `FallbackModel`
   ile büyük modele devretme var). Anthropic API'de "tool search" (araçları sonradan yükleme) iki aşamayı kendisi yapıyor.

Yerel yönlendirici adayları (11 Ekim araştırması; rakamlar çoğunlukla tek kaynak — kendi makinemizde ölçülecek):
- **Gömme tabanlı yönlendirici (semantic router)** — her komuta 5-10 Türkçe örnek cümle; gelen cümle en yakın örneğe
  (kosinüs benzerliği), eşik altıysa "emin değilim". Küçük çok dilli gömme modeli (ör. multilingual-e5-small sınıfı),
  tahmini ~100-500 MB RAM, CPU'da ms. **.NET'te ONNX Runtime ile doğrudan** (Python/Ollama kurdurmak yok, `fm` tek parça).
  Üretken değil → uydurma araç çağrısı yok. **İlk aday.** Riskler: eşik ayarı, çok istekli cümle, örnek yazma emeği.
- **Laya** (ModernBERT/mmBERT, 322-421M, 100+ dil) ve **Von** (ModernBERT ~395M, CPU, kalibre olasılık) — yerel "Jev
  benzerleri", seçenekten olasılıkla seçiyor; tahmini ~0.5-1.5 GB. Çok yeni projeler.
- **FunctionGemma 270M** (Google, araç çağırmaya özel) — dosya ~290 MB, RAM ~550-800 MB (kaynaklar çelişiyor); hazır
  ~%58, ince ayarla ~%85 doğruluk (tek kaynak); kendi çağrı biçimi var.
- NanoJev (Qwen3-0.6B), Kev (0.8-9B), Rizzo Flow (1.7-4B), Nimble (9B) — bize ağır / GPU istiyor.
- Ollama + 7-8B genel model — ~5 GB, FM26'nın yanında 16 GB Mac'te zor; 95 araçta şaşırıyor.

Başlarken sıra: (a) komutların ~12 kategorilik haritası (hangi yol seçilirse seçilsin lazım), (b) en çok kullanılan ~20
komuta Türkçe örnek cümleler, (c) küçük deneme: ONNX gömme modeli `fm` içinde — doğruluk ve RAM gerçek sayılarla,
(d) iyiyse genişlet; MCP araçlarını da ~15 kalın araca indirmek (az araç küçük modelde de, bulutta da daha doğru).
Kaynaklar: pydantic.dev/docs/ai/api/models/typesafe, docs.composio.dev/docs/providers/typesafe,
openrouter.ai/~typesafe/jev-latest, datacamp.com/blog/top-open-source-jev-alternatives,
developers.googleblog.com/on-device-function-calling-in-google-ai-edge-gallery, thoughtworks.com/radar/tools/semantic-router.
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

> En eski üstte, yeni gün dosyanın SONUNA `cat >>` ile eklenir (3–6 satır). Ayrıntı FINDINGS'te ve `git log`'da.

### 2026-09-28
- Oyun CLI'dan kariyere sokuldu (`/continue`, ağaç 186 → 4198); `GameSubsystems` ile `Bindings`/`EventSubsystem` tek
  yerden; kadro listesi `GameStateCache…Players`, 27 oyuncu; oyuncu 1245 özellik listeliyor; `tools/sigdump`.
- ⚠ Span köprüsü çalışmıyor (§8.2), `Exists("Game") = true` bir hafta doğru sanıldı → ders: uydurma girdinin
  reddedildiğini de test et. `Bind`'e null delegate oyunu çökertti.

### 2026-10-04
- Veri modeli 931 tip / 14504 özellik; canlandırma doğru olayla; `/newsaction` ile mesaj cevaplandı, saat ilerledi;
  `fm` CLI (`start`, `continue`, `squad`, `inbox`, `next`, REPL, `--json`); oyun tamamen gizli; aksiyon kataloğu; `/send`
  enum/`TypedValue`/`dry=1`.
- Engeller: headless olmuyor; aynı anda tek ekran bağlı; null `ResponseActionDataReferences` oyunu çökertti; iki yanlış
  pozitif (pencere gizleme, `inbox` bir dolu bir boş).

### 2026-10-05
- `/buttons` + `/press` (her kontrol olayını tarif ediyor), simülasyon katmanı canlı ("Unread 10 → 0"); `squad
  --fitness/--morale` (First Team görünümleri, `/dropdowns` + `/select`); sayfalar adıyla (`OpenBookmarkEvent`); `fm
  reply`; bloke eden mesaj `NewsItemIsPriority`. `Condition = -1` sakat.
- Engeller: menü seçimi sayfa açmadı (sonra çözüldü), bir deneme kuyruğu tıkadı (`/queue` koruması), tanıtım turu
  bitirilemedi. Test tıklamaları kayda yazılmadı.

### 2026-10-06 (gece)
- Tanıtım turu (`tour`), taktik sihirbazı (`tactic new`), `next` zamanı ilerletti, ilk maç (`match --talk`), basın ve
  beklentiler toplantısı `talk` ile, `fm mcp` (29 araç), `tactic role/player`, `squad --view`, `players`, `search`, `match
  --instant`, `sub`, `setpieces new`, `table`, `fixtures`, `next` durumu `continue.State`'ten.
- ⚠ Oyun otomatik kayıt aldı (ana dosya üzerine; temiz hal `(v02)` ve `save-backups/`'ta). `open` bazı sayfaları verisiz
  açıyordu (§8.31).
- Faruk'un 6 Ekim sabahı kararları: otomatik kayıt bir ayar (kullanıcı seçer); maç oynat / geç ikisi de seçenek;
  konuşmada cümleyi kullanıcı seçer; sırada UX ve görselleştirme.

### 2026-10-07
- Gece: Spectre.Console (çizim terminalde, pipe/MCP düz); okla seçim (reply, talk, dialog, sub, tactic…); **ekransız
  veri** (`/pull`, §8.40); `status` paneli, `fm report`; maç günü kararlara göre (izle / anında, kadro sahada, her
  konuşmada seçim); `fm offer`. ⚠ Demirbay: "Accept" kulübün £8.25M'ını kabul etti, genel Continue teklifi geri çekti →
  `reply` ilk cümleyi basıyor. Defrel transferi uçtan uca (Modena £160K, imza). Oyun bir kez çöktü (açıkken DLL kopyası).
- Faruk sabah: `fm offer` "kötüymüş" (yavaş, kırılgan, görünmez) → aynı akşam kart + okla düzenleme, ~5 sn.
- Akşam: `fm register` listeyi elle düzenliyor (§8.43); kılavuz `docs/manual/KILAVUZ.md` + `tools/manual/`.

### 2026-10-08 (gece)
- `fm contract` kartı (§8.44), alt menüler çözüldü (§8.45), kiralık kartı (§8.46), taktik yuvaları (§8.47), takım /
  oyuncu / rakip talimatları (§8.48, §8.53, §8.49), yoğunluk ve bireysel antrenman (§8.50–8.51), maç içi mentalite ve
  bağırış (§8.54), `shortlist`, `saves`, `opponent`, `captains`, `depth`, `happiness`, `prep`.
- Kararlar: Cancellieri kiralığı geri çekildi (para harcamayan seçenek); Škriniar kaptan, Kerem yardımcı kaptan. Oyun
  bir kez kilitlendi (kaptanlar sayfası sonrası), 10 Ağustos kaydından döndü.

### 2026-10-09 (gece)
- `fm settings` (§8.61), maç ekranı: bağırış doğrulandı, maç içi rol, `fm dugout` (§8.62), oyuncu kariyeri (§8.63),
  bütçe kaydırıcısı (§8.65), takvim ve bütün fikstür (§8.66), gelen teklif (§8.67), bayat liste satırları `?gen=`
  (§8.68), sakatlık durağı (§8.69), turnuva liderleri (§8.70), `health` (§8.71), `contracts` (§8.72). `next` yutulan
  basışı yeniden basıyor.
- Kayıtta: 10 Ağustos → 20 Eylül oynandı (UCL'den elendi, Avrupa Ligi'ne düştü), 20 Eylül'de kaydedildi; yedek
  `save-backups/…2025-08-13-feyenoord-day.fm`. Gece kararları: sakatlıklarda fizyoterapist, basına yardımcı, Mercan için
  £1.4M teklif reddedildi.

### 2026-10-10
- Gündüz: `schedule`, antrenman raporu, alışkanlık, `release` (⚠ Mutual Termination onaysız, `talk 4` kazası →
  yeniden başlatmayla geri alındı), `contract --agent`, `tactic copy/clear`, `club/staff/finances` (§8.78), sponsor /
  gelir-gider / taraftar / tesis (§8.79), akıcılık / mevki puanı / sakatlık geçmişi / sezon istatistikleri (§8.80),
  `board` ve bilet (§8.81), kısaltmalar / tamamlama / `doctor` (§8.82), `--json` her komutta (§8.83), maç maç puanlar
  (§8.84), potansiyel (§8.85), `--csv/--md`, `export`, `diff`, `compare`, yazım hatası toleransı.
- Akşam: penaltı sırası (§8.87, §8.90), antrenman programı (§8.88–8.89), `takers` (§8.91), `players --free` + kişi
  listesine sızan kadro (§8.92), `stop`, `query`, `setpieces <tür>` (§8.93), `propose` (§8.94), `dynamics` (§8.95),
  `contracts <oyuncu>` (§8.96), `news` (§8.97), `offers` (§8.98), ayarlara dört ayar + bir kilitlenme (§8.99), `table`
  doğru turnuva (§8.100), `internationals` (§8.101), `mentoring` (§8.102). Maç sonu raporu skor `LastFixture`'da
  görünene kadar bekliyor. Defrel'e Angers teklifi; Faruk "save önemsiz" deyince kabul edildi.

### 2026-10-11
- Gece: `load`, `save --as`, `saves delete`, `start --menu` (§8.104), `stages` (§8.105), elle mentorluk (§8.106),
  taktik dosyaya (§8.107), `board request` (§8.108), `scout` (§8.109), `world` (§8.110), `responsibilities` (§8.111),
  `youth` (§8.112), `progress` (§8.113), rakip sezon sayıları ve form (§8.114–8.115), `staff find` (§8.116), oyuncu
  veritabanında gerçek arama (§8.117). Kayıt 23 Ekim 2025'te kaydedildi (Qarabağ 2-0; üç yönetim talebi; Defrel satışı).
- Gündüz/akşam: sağ tık menüsü, personel al/kov (§8.118), scout/kısa liste/ilgi/soru/deneme (§8.119), scout odağı kur/
  durdur/yeniden başlat (§8.120), rakip raporunun kalanı (§8.121), scout raporu (§8.123), `/shot` (§8.124), forma
  numaraları (§8.126), mutluluğun nedeni (§8.127), analist (§8.128), tam istatistik listeleri ve `/contexts` (§8.129),
  maç hazırlığı odağı (§8.130), `targets` (§8.131). Scout görevi iptali park.
- Gece (yarımlar): topsuz takım/oyuncu talimatları ekransız (§8.132–8.133), yardımcı kaptan günde bir (§8.134),
  `responsibilities give` (§8.135), `release --free` gün ilerleyince (§8.136), maç içi talimatlar (§8.137). Ocak'a kadar
  oynandı: kovulma (§8.142); oyun 10 Ocak 2026, işsiz, kaydedildi.

### 2026-10-12
- Boş paneller ve siyah ekran Display Fix modundanmış (devre dışı), `fm stop` oyunun kendi çıkışıyla (§8.144); `fm
  jobs`; Lincoln City'ye girildi (19 Ocak 2026, League One); ilk günler (§8.145), Bradford maçı ve gelen kutusu kaydırması
  (§8.146), `fm coaches` (§8.147). 7 Mart 2026'da kaydedildi.

### 2026-10-13
- Gündüz (Faruk: "kariyer önemsiz, test ediyoruz"): deneme, personel alımı, karşılıklı fesih uçtan uca (§8.148–8.149),
  sorumlulukların dokuz alanı ve basın devri (§8.150), maç içi sakatlık/kart/puan (§8.151), altyapı adayları (§8.152),
  izlenen maç (§8.153), sakatlık durağı ikinci kez (§8.154), kulübe göre değişenler (§8.155), maç sonrası tablo (§8.156),
  sonuç ekranında zaman durması (§8.157), cevapsız mesaj ekleri (§8.158), sezon sonu (§8.159), `vision` (§8.160), birden
  çok oyuncuya gelen teklifler (§8.161). Sezon bitti (Lincoln 11.), oyun 29 Haziran 2026.
- Akşam/gece (taşınabilirlik): sayfalar Guid'le (§8.162–8.163), altyapı veriden (§8.164), onay pencereleri yuvayla
  (§8.165), düğme kataloğu (§8.166), çeviri tablosu ve katmanı; Türkçede tarama 21 → 12 (§8.167–8.168).

### 2026-10-14
- Gece: yazının veri yolu (§8.169–8.170); Türkçe tarama 13 → 1; motor yazıları `languages.fmf` (§8.171); para biçimi
  ve menü kimlikleri (§8.172). ⚠ Kimlik bulmak için her yoklamada bütün ağacı gezmek oyunu iki kez düşürdü.
- Öğleden sonra (Faruk: "Adamın oyunu tr ise tr görecek"): gösterim ve CLI etiketleri oyunun dilinde (§8.173–8.174);
  akşam rol adları, İngilizce oynama akışı (§8.175), mesaj gövdesi/ekleri kimlikle (§8.176), konumla okuma taraması,
  yüzde (§8.177). Türkçe ve İngilizce tarama 49/50. Kayıt bir ara 13 Temmuz'a döndü (`save --yes` unutuldu).
- Akşam: BAGIMLILIKLAR CHECKLIST'i ve otomatik tur döngüsü (aynı akşam kaldırıldı). Turlarda: `reply` cevapları (§8.178), Bridge
  beklemeleri ölçüldü (§8.179), `table` 8,7 → 2,6 sn, `coaches` beklemeleri ve yarım sayfa (§8.180). FINDINGS, PLAN ve
  hafıza dosyaları sadeleştirildi (veri korunarak).
- Otomatik tur: `next`'in `Settled` beklemesi 16 ilerlemede ölçüldü (başlama ≤1,24 sn / 4 sn, bitiş titremesi ≤0,53 sn /
  1,4 sn); pay yeterli, kod değişmedi (§8.181). Kaptanlar onaylandı, iki Salford teklifi reddedildi; oyun maç gününde.
