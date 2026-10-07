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
   form; otomatik kayıt ve maç hızı ayarları (karar bekliyor).
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
   listeliyor (§8.38). `fm search` ve `fm player` herhangi bir oyuncuyu buluyor. Kalan: kısa liste, ve
   teklif (`TransferOfferEditSessionReference`, 99 property; geri alınamaz, `--yes` ve `--dry` ile).
5. **MCP'yi gerçek istemciyle denemek.** `fm mcp` protokolü konuşuyor ve araçlar çalışıyor
   (§8.39) ama Claude Code / Desktop'tan hiç çağrılmadı. Ardından: araç çıktısındaki
   yönlendirmeleri araç adına çevirmek, uzun maç için zaman aşımı, `--json` her komutta.

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
- [ ] Kayıtları listele (`fm saves`)
- [ ] Belirli kaydı yükle (`fm load "Fenerbahçe"`)
- [x] Kaydet — `fm save --yes`, dosyanın üzerine (§8.35)
- [ ] Farklı kaydet (kayıt tarayıcısı paneli: `SaveGameHandlerModule.m_saveBrowserPanel`)
- [ ] Kaydı sil (`DeleteSelectedGameEvent` mevcut)
- [ ] Oyunu kapat
- [ ] Otomatik kaydetme ayarı — ⚠ açık ve ana dosyanın üzerine yazıyor (§8.35)
- [ ] Bulut kayıt durumu
- [?] Kariyer **oluşturma** (çok adımlı sihirbaz — lig seçimi, veritabanı boyutu, menajer profili)
- [?] Menajer profili düzenleme (isim, doğum tarihi, uyruk, görünüm)

---

## 2. Kadro ve oyuncu

- [x] Kadro canlandı — isim, pozisyon, yaş, uyruk, ortalama rating (§8.16)
- [x] `fm squad` — A takım: pozisyon, yaş, statü, maaş, piyasa değeri, durum ("General Info")
- [ ] `fm squad --u19` — altyapı kadrosu (eski `fm squad` özet panosundan basıyordu, artık basmıyor)
- [x] `fm squad --fitness` — kondisyon ve maç formu, "Selection Info" görünümünden (§8.26)
- [x] `fm squad --morale` — moral, hiyerarşi, sosyal grup, "Dynamics" görünümünden (§8.26)
- [x] Kadro tablosunun bütün görünümleri — `fm squad --views`, `fm squad --view "<ad>"`; kolonlar
      veriden çıkıyor (§8.38). Alt menülü ikisi (Attributes, Stats) seçilemiyor
- [ ] `/squad` iki listeyi birleştirirken bir oyuncunun kolonlarını eksik bırakabiliyor (Musaba)
- [~] Uyruk — `Nation` düğümü çocuksuz (§8.21), ama `NationButton` üç harfli kodu taşıyor; `fm squad`'a eklenmedi
- [x] `fm player <isim>` — kimlik, sözleşme, alışkanlıklar, 36 özellik (§8.38); kadro dışındakiler aramayla
- [x] Öznitelikler (teknik / zihinsel / fiziksel / duran top; kalecide kalecilik)
- [ ] Gizli öznitelikler (scout görüşü kadarıyla)
- [ ] Pozisyon yetkinlikleri
- [ ] Sakatlık durumu ve geçmişi
- [~] Sözleşme: maaş, bitiş tarihi, oynama süresi `fm player`'da; madde ve bonuslar yok
- [ ] Form, maç ratingleri, sezon istatistikleri
- [ ] Kariyer geçmişi, transfer geçmişi
- [ ] Gelişim grafiği / potansiyel
- [ ] Milli takım durumu
- [ ] Oyuncu mutluluk/şikâyet durumu
- [ ] Kadro derinliği analizi (pozisyon bazlı)
- [ ] Yaş profili / kadro dengesi

---

## 3. Taktik

> Katalog: COOKBOOK §1. Yazma tarafının ana event'i `ModifyTacticEvent`.

- [x] Taktik oluştur — `fm tactic new "<stil>" "<topla>" "<topsuz>"`, oyunun sihirbazıyla (§8.30)
- [x] Mevcut taktiği göster — `fm tactic`: stil, mentalite, 11 mevki, top/topsuz roller, oyuncular (§8.31)
- [x] Mentalite ve stil değiştir — `fm tactic mentality <x>`, `fm tactic style <x>` (§8.31)
- [x] İlk 11'i oyuna seçtir — `fm tactic pick` (Quick Pick) (§8.31)
- [k] Formasyon değiştir — `Formation` enum'u, 72 üye
- [k] **In Possession / Out of Possession** ayrı şekiller — aynı event, `IsInPossession` bayrağı
- [x] Oyuncu rolleri — `fm tactic role <mevki> [<rol>] [--oop]` (§8.31)
- [k] Oyuncu talimatları — `PlayerInstruction`, 36 üye
- [k] Takım talimatları — 33 eksen, 14 faz (`TeamInstructionPhaseID`)
- [x] İlk 11'e yedeklerden oyuncu koy — `fm tactic player <mevki> <oyuncu>` (§8.31)
- [x] Yedek kulübesini göster — `fm tactic`'in "Yedekler" satırı
- [ ] İki as oyuncunun yerini değiştirme, kadro dışından oyuncu alma (adları taktik ekranında bağlı değil)
- [x] Duran top rutinlerini kur — `fm setpieces new`, oyunun altı soruluk sihirbazı (§8.36)
- [ ] Duran top düzenlerini okuma / sonradan değiştirme, penaltı sırası
- [ ] Kaptan / yardımcı kaptan
- [k] Taktik kaydet / yükle / kopyala — `ModifyTacticOption`, `EventFmxDuplicateTactic`, `EventFmxSetPresetTactic`
- [k] Rakip talimatları — `EventFmxOppositionInstructionsApplyAdvice` / `Clear`
- [ ] Taktik akıcılığı (familiarity)

---

## 4. Antrenman

> Katalog: COOKBOOK §5.

- [k] Haftalık antrenman programı göster — `CustomTrainingScheduleReference`, `DailyScheduleReference`
- [ ] Antrenman programı değiştir
- [ ] Maç hazırlığı odağı
- [k] Bireysel antrenman odağı — `EventFmxPlayerTrainingFocusChanged` + `…ConfirmTrainingFocusChanges`
- [k] Pozisyon / hareket öğretme — `PersonTrainingNewPosition`, `PersonTrainingNewPreferredMove`
- [ ] Mentor / mentee ilişkileri
- [k] **Antrenman raporları** — `PersonRequestTrainingReport`
- [k] Yoğunluk ayarı — `EventFmxPlayerTrainingIntensityLevelChanged`
- [k] Dinlenme — `PersonRestTrainingSchedule` / `PersonCancelRestTrainingSchedule`
- [ ] Gençlik antrenmanı

---

## 5. Transfer ve sözleşme

> Katalog: COOKBOOK §2. Pazarlığın tamamı `TransferOfferEditSessionReference` (99 property).

- [ ] Transfer listem (`fm transfers list`)
- [~] Oyuncu ara — `fm players [--filter "<hazır süzgeç>"] [--view "<görünüm>"]`: veritabanının
      ekrandaki 21 satırı, `--clear` süzgeci kaldırır (§8.38); isimle arama ve kaydırma yok
- [k] Teklif ver — `PersonMakeOfferForPlayer`
- [k] Gelen teklife yanıt — `EventFmxTransferOfferResponse`
- [k] Sözleşme teklifi / yenileme — `PersonOfferContract`, `ContractEditSessionReference` (80)
- [k] Sözleşme maddeleri — `EventFmxAddContractBonus`, `ClubTransferClausesReference`
- [k] Kiralık — `CanMakeLoanOffer`, `PersonOfferExtendLoan`, `LoanToBuy*`
- [k] Oyuncuyu kulüplere öner — `PersonOfferToClubs`
- [k] Sözleşme feshi / serbest bırakma — `PersonMutualTerminationContract`, `PersonReleaseClubFreeTransfer`
- [ ] Serbest oyuncular
- [k] Ön anlaşma sözü — `EventFmxAddPreContractPromise`
- [k] Menajerle görüşme — `PersonAskAgentAboutClientContractRenewal`
- [k] Transfer bütçesi ↔ maaş bütçesi — `ClubBudgetAdjustmentTransfers` / `Wages`, `ClubRequestIncreaseBudgets`
- [k] Takas teklifi — `ExchangePlayersList`, `ExchangePotentialPlayersList`
- [ ] Transfer dönemi durumu / kalan gün

---

## 6. Scouting

- [ ] Scout raporları
- [ ] Scout ata (bölge/oyuncu/takım)
- [ ] Scout odakları (recruitment focus)
- [ ] Kısa liste (shortlist)
- [ ] Oyuncu karşılaştırma
- [ ] Rakip analizi / next opponent raporu
- [ ] Gençlik adayları / youth intake

---

## 7. Maç

- [x] `fm next` — ilerle, bloke eden mesajları ve oyunun ilerle butonunun ne dediğini bildir
- [x] `fm next [n]` zamanı ilerletiyor, oyunun durmasını bekliyor, tarih/saat ve yeni mesajları
      bildiriyor; oyunun kendi durumunu okuyor (`continue.State`, §8.32)
- [ ] `fm match --talk` kalkacak: konuşma durağında seçenekler gösterilip seçtirilecek (ürün kararı)
- [x] Maç gününü oyna — `fm match [--talk]`: kadro gönder, onay penceresi, konuşmalar, maç,
      çıkış; gol/devre/sonuç bildiriyor (§8.34)
- [x] Maç içi oyuncu değişikliği — `fm sub <çıkan> <giren>`; `fm sub` sahayı ve kulübeyi listeler (§8.34)
- [ ] Sakatlık durağı — maç duruyor, `fm match` söyleyip bırakıyor; `fm sub` + "Confirm Changes" orada denenmedi
- [x] Anında sonuç — `fm match --instant`, 9 sn (§8.34)
- [ ] Maç hızı (`EventFmxHighlightModeChanged`, `…HighlightSpeedChanged`) — oynanan maç ~6-7 dk
- [k] Maç içi taktik değişimi — `EventFmxMatchTacticsChanged`, `MatchTacticReference` (227)
- [x] Maç öncesi, devre arası ve maç sonu takım konuşması — `fm talk` ya da `fm match --talk`
- [ ] Canlı maç takibi (metin akışı)
- [ ] Maç içi: taktik/rol değişimi, shout (listeleri ekranda, alt menü seçimi çözüldü — §8.34)
- [ ] Maç sonrası: rating, istatistik, basın
- [ ] Maç istatistikleri (xG, pas haritası, ısı haritası → terminalde?)
- [ ] Highlight özeti (metin)
- [?] Maç içi gerçek zamanlı yönetim (v2 — bambaşka problem)

---

## 8. Lig, kupa, takvim

- [x] Lig tablosu — `fm table` (§8.37); puanlı tabloda denenmedi
- [~] Fikstür — `fm fixtures` son oynanan ve sıradaki maçları, sonucuyla veriyor (§8.37); tam liste yok
- [ ] Başka ligler / ülkeler
- [ ] Kupa ağaçları
- [ ] Avrupa kupaları
- [ ] Gol krallığı / asist / form tablosu
- [ ] Takvim: sıradaki maçlar, önemli tarihler
- [ ] Sezon özeti / ödüller

---

## 9. Finans

- [ ] Bütçe durumu
- [ ] Gelir/gider dökümü (`ExpenditureBreakdown*` binding'leri mevcut)
- [ ] Maaş bordrosu
- [ ] Sponsorluklar
- [ ] Bilet fiyatları
- [ ] Kulüp değeri / borç
- [ ] Finansal fair play durumu

---

## 10. Personel ve kulüp

- [ ] Teknik ekip listesi
- [ ] Personel işe al / kov (Coaching, Scouting, Medical)
- [ ] Personel sorumlulukları (delegasyon)
- [ ] Tesisler (antrenman, gençlik, altyapı)
- [ ] Tesis geliştirme talebi
- [ ] Yönetim kurulu beklentileri
- [ ] Yönetimle görüşme / talep
- [ ] Taraftar memnuniyeti
- [ ] Kulüp vizyonu / hedefler
- [ ] İş güvenliği (sacılma riski)

---

## 11. İletişim

- [x] Inbox oku (`fm inbox`)
- [x] Mesaja cevap ver — `fm reply <#>` seçenekleri gösterip seçimi soruyor; `fm reply <#> <no>`
      tek satırda cevaplıyor (§8.29)
- [x] Tanıtım turları — `fm tour` yürütüyor, `fm tour --skip` atlıyor (§8.30)
- [x] Basın toplantısı — `fm talk`; liste cevaplı sorular dahil (§8.36)
- [ ] Maç öncesi/sonrası röportaj
- [x] Takım toplantısı — `fm talk`: ne dendi, tepkiler, söylenebilecekler (§8.33)
- [~] Oyuncuyla bire bir konuş — `fm talk` aynı şablonu bekliyor, canlı görülmedi
- [ ] Oyuncu şikâyetlerini yönet
- [ ] Soyunma odası atmosferi / takım liderleri
- [ ] Sosyal medya / haber akışı
- [ ] Sözleşme görüşmelerinde diyalog

---

## 12. Ayarlar

- [ ] **Dil değiştir**
- [ ] **Para birimi değiştir**
- [ ] Tarih formatı
- [ ] Ölçü birimi (metrik/emperyal)
- [ ] Oyun hızı / detay seviyesi
- [ ] Hangi ligler yüklü (add/remove league)
- [ ] Bildirim tercihleri
- [ ] Otomatik kaydetme aç / kapat ve sıklığı — kullanıcı seçecek (ürün kararı)
- [ ] Maç varsayılanı: oynat / anında sonuç — kullanıcı seçecek
- [ ] Skin / tema (bizde anlamsız ama API'de var)

---

## 13. CLI'ın süper gücü: sorgu ve analiz

> FM arayüzünde yapması işkence olan, terminalde tek satır olan şeyler.

- [ ] Serbest sorgu: `fm query "yaş<23 ve potansiyel>4 ve sözleşme<1yıl"`
- [ ] Kadro sağlık taraması: yorgun + sakatlık riski + form düşüşü tek raporda
- [ ] Sözleşme takvimi: kim ne zaman bitiyor, kim tehlikede
- [ ] Derinlik matrisi: hangi pozisyon zayıf
- [ ] Transfer fırsat taraması (bütçe + ihtiyaç + uygunluk)
- [ ] Sezon karşılaştırması / trend
- [ ] `fm diff` — son maçtan beri ne değişti
- [ ] `fm watch` — bir oyuncuyu/durumu izle, değişince haber ver (`Bindings.Bind` ile)
- [ ] Export: CSV / JSON / Markdown rapor

---

## 14. CLI kalitesi

### Görselleştirme ve akıcılık (öncelikli, 2026-10-06)

- [x] Terminal arayüz kütüphanesi seç — **Spectre.Console** (2026-10-07). Terminal.Gui tam ekran
      bir uygulama ister ve komut + pipe yapısını bozardı; Spectre panel, tablo, ızgara ve okla
      seçimi mevcut komutların çıktısına ekliyor, script/MCP düz çıktıyı koruyor (`Ui.Rich`)
- [x] **Saha çizimi** — `fm tactic` iki sahayı yan yana çiziyor (dar terminalde alt alta)
- [x] Değişiklik sahada görünsün — `tactic role` / `player` / `pick` sonrası değişen mevki sarı
- [~] Maç ekranı — anlatım satırları, takım adlarıyla skor, maç öncesi kadro sahada, izle /
      anında sonuç seçimi, konuşmada söz seçimi, maç sonu raporu (`fm report`). Kalan: sahadaki on
      bir ve kulübe aynı ekranda, `fm sub` buradan
- [x] Seçim istemleri — reply, talk, dialog, sub, tactic new/mentality/style/role/player,
      setpieces new, match okla soruyor; script'te düz çıktı
- [~] Konuşma ekranı — `talk` konuşmayı baştan sona okla yürütüyor (basın toplantısı, takım
      toplantısı denendi). Kalan: tepki değişimini görünür kılmak
- [x] Oyuncu kartı — mevki sahası, renkli çubuklar, bloklar yan yana, scout aralığı gölgeli
- [~] Lig tablosu ve fikstür — tablo: bölge renkleri ve takım vurgusu. Kalan: form, fikstür renkleri
- [x] Kolon başlıkları — `squad --view` / `players` okunur başlık basıyor
- [~] Akış — `fm status` takım, menajer ve sıradaki maçı bir panelde veriyor; `next` gürültüsü azaldı

### Diğer

- [x] Renkli, hizalı tablolar
- [ ] Sparkline / mini grafik (form, kondisyon trendi)
- [ ] `--json` her komutta (script'lenebilir)
- [x] Pipe dostu çıktı (`fm squad | grep ...`)
- [~] İnteraktif oturum (`fm start` sonrası komutlar `fm` önekisiz) — TUI değil, REPL; tırnaklı
      ifadeler tek kelime sayılıyor
- [ ] Fuzzy isim eşleme (`fm player osim` → Osimhen)
- [ ] Kısaltmalar / takma adlar
- [ ] Tab tamamlama (zsh/bash)
- [~] `fm status` — tarih, saat ve oyunun neyi beklediği (§8.32); sıradaki maç ve uyarılar yok
- [x] Onay pencereleri — `fm dialog` gösteriyor / cevaplıyor (§8.34)
- [~] Onay isteyen aksiyonlar için `--yes` — yalnızca `fm save`'de
- [ ] Geri alma yok — yıkıcı aksiyonlarda uyarı
- [x] Oyun kapalıysa otomatik başlat (`fm start`)

---

## 15. MCP + doğal dil

- [x] MCP server — `fm mcp` (stdio), 29 araç, her araç bir komut (§8.39); gerçek istemciyle denenmedi
- [ ] "Kadroda yorgun kim var?"
- [ ] "Sol bek arıyorum, 10M bütçem var, öner"
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
- [ ] `fm doctor` — kadronda neyin yanlış olduğunu söyleyen tanı komutu
- [ ] Zaman serisi DB'ye akıt, Grafana'da kariyer dashboard'u
- [ ] Başka oyunlara aynı mimari (Unity + IL2CPP olan her şey)

---

## Günlük

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

- `fm offer` (teklifin ~20 sn'de gitmesi, bonservisin sahte odak olaylarıyla yazılması) "kötüymüş";
  8 Ekim'de buradan devam edilecek. Neyin kötü olduğu henüz konuşulmadı.

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
