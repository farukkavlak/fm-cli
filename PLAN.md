# fm-cli — Yol Haritası

> FM26'da yapılabilen her şeyi CLI komutuna çevirme listesi.
> Durum: `[x]` çalışıyor · `[~]` kısmen · `[k]` katalogda var, canlı denenmedi · `[ ]` yapılacak ·
> `[?]` yapılabilirliği belirsiz

`[k]` şu demek: verisi ve event'i metadata'dan çıkarıldı (`docs/reference/COOKBOOK.md`), yani
keşif bitti, geriye canlı doğrulama ve komutu yazmak kaldı. İşaretsiz `[ ]` ise katalogda henüz
aranmadı demek, yok demek değil.

---

## Sıradaki işler (2026-10-05)

Sıra, "en çok şeyin önünü açan önce" mantığıyla. Her adım bitince buradan silinip aşağıdaki
ilgili bölümde `[x]` olur, günlüğe bir satır düşülür.

Bütün adımların ortak aracı artık `/buttons` + `/press` (§8.25): ekrandaki her kontrol hangi
event'i hangi veriyle göndereceğini kendisi söylüyor ve kendi listesi kuyruğa konarak
"basılıyor". Yeni bir alan açmak = o ekrana git, `/buttons` ile bak, `/press` ile dene.

1. **Menüden sayfa açma.** `screens.txt` altı ana sekmenin 77 alt sayfasını sayıyor ama
   menüden seçmek sayfayı açmıyor (§8.27): seçim `selectedSubtabElement`'e yazılıyor, sayfa
   değişmiyor. Seçilen kaydın `PropertyValue`'su bir panel değil, iç içe bir kayıt; her satır
   bir `ChildList` taşıyor. Sıra: o alt listeleri oku (`/dropdowns` derinleşsin), yaprak
   satırı seçmeyi dene (`OnSelectionChangedInternal`'ın liste alan hali), olmazsa kayıttaki
   paneli `OpenSubtabEvent`'e elle ver. Bu çözülünce her komut ekranına tek adımda gider.
2. **Inbox cevabını `/press` üzerine taşı** ve `fm reply` komutunu çıkar. Mesaj açılınca
   cevap butonlarının nasıl listelendiğine henüz bakılmadı; `/newsaction` o zamana kadar duruyor.
3. **Basın toplantısı ve oyuncu konuşmaları.** Bu kayıtta bekleyen toplantı yok, o yüzden canlı
   görülmedi. Cevaplar `SpeakToActionEvent` ile gidiyor ve bunu inbox'la aynı modül
   (`NewsActionHandlerModule`) işliyor. Toplantı açılınca cevap butonları `/buttons`'ta
   `SpeakToActionEventContext` olarak görünmeli; 4 bekleyen "Tasks" mesajı aynı yolun ilk
   denemesi olabilir.
4. **`fm tactic`** — önce oku (`TacticReference`, 207 property), sonra yaz. Taktik ekranındaki
   kontroller (`FMPitchPosition.Events`, `FMTacticsPanel.SelectionEvent`) da event listesi
   taşıyor, yani rol ve formasyon değişimi de basılabilir olmalı.
5. **Ucuz okuma komutları:** `fm table`, `fm fixtures`, `fm player <isim>`, `fm status`.
   1. adım bitince seri iş. Kadro tablosunun diğer görünümleri (Contract, Transfer, Injuries,
   Playing Time, Development…) aynı mekanizmayla hazır: görünümü seç, satırı oku.
6. **Transfer ve sözleşme** — `TransferOfferEditSessionReference` (99 property). Geri alınamaz
   olduğu için `--yes` onayı ve `dry=1` ile birlikte.
7. **Maç** — `EventFmxGoToMatch`, hızlı sonuç, maç sonrası özet.
8. **`--json` her komutta, ardından MCP server.** Komut sayısı 8-10'a çıkınca anlamlı.

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
- [~] Ekran haritası — ana sekmeler ve alt sayfaları `screens.txt`'te; menüden açma çalışmıyor
- [x] Kuyruk denetimi — `/queue`, tıkalıyken `/press` reddediyor (§8.27)
- [x] Açılır listeleri seçenekleriyle listeleme (`/dropdowns`) ve seçme (`/select`) (§8.26)
- [x] Kontrolü etiketle, listeyi adıyla adresleme — adresler ekran değişince bayatlıyor
- [x] Simülasyon event'ini canlı gönderme — `EventToPlugin`, oyunun kendi doldurucusuyla (§8.25)
- [ ] Ekran açmadan tek property canlandırma — kanal açılıyor, düğüm yaratılamıyor (§8.21)
- [ ] FM27'ye taşıma: sürüme özel her şey tek config dosyasında

---

## 1. Oyun yaşam döngüsü

- [x] Oyunu başlat (`fm start`)
- [x] **Son kaydı yükle** (`LoadSelectedGameEvent`)
- [ ] Kayıtları listele (`fm saves`)
- [ ] Belirli kaydı yükle (`fm load "Fenerbahçe"`)
- [ ] Kaydet / farklı kaydet (`SaveGameEvent` mevcut)
- [ ] Kaydı sil (`DeleteSelectedGameEvent` mevcut)
- [ ] Oyunu kapat
- [ ] Otomatik kaydetme ayarı
- [ ] Bulut kayıt durumu
- [?] Kariyer **oluşturma** (çok adımlı sihirbaz — lig seçimi, veritabanı boyutu, menajer profili)
- [?] Menajer profili düzenleme (isim, doğum tarihi, uyruk, görünüm)

---

## 2. Kadro ve oyuncu

- [x] Kadro canlandı — isim, pozisyon, yaş, uyruk, ortalama rating (§8.16)
- [x] `fm squad` — kadroyu tablo olarak bas
- [x] `fm squad --fitness` — kondisyon ve maç formu, "Selection Info" görünümünden (§8.26)
- [x] `fm squad --morale` — moral, hiyerarşi, sosyal grup, "Dynamics" görünümünden (§8.26)
- [~] Kadro tablosunun diğer görünümleri — veri `--json`'da geliyor, tablo komutu yok: maaş,
      sözleşme, piyasa değeri, oynama süresi, sakatlık riski (§8.26)
- [~] Uyruk — `Nation` düğümü çocuksuz (§8.21), ama `NationButton` üç harfli kodu taşıyor; `fm squad`'a eklenmedi
- [ ] `fm player <isim>` — oyuncu kartı (öznitelikler, sözleşme, form)
- [ ] Öznitelikler (teknik / zihinsel / fiziksel)
- [ ] Gizli öznitelikler (scout görüşü kadarıyla)
- [ ] Pozisyon yetkinlikleri
- [ ] Sakatlık durumu ve geçmişi
- [ ] Sözleşme detayları, bitiş tarihi, maaş, madde ve bonuslar
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

- [k] Mevcut taktiği göster (`fm tactic`) — `TacticReference`, 207 property
- [k] Formasyon değiştir — `Formation` enum'u, 72 üye
- [k] **In Possession / Out of Possession** ayrı şekiller — aynı event, `IsInPossession` bayrağı
- [k] Oyuncu rolleri — 57 topla + 46 topsuz rol; değerler bit maskesi
- [k] Oyuncu talimatları — `PlayerInstruction`, 36 üye
- [k] Takım talimatları — 33 eksen, 14 faz (`TeamInstructionPhaseID`)
- [k] Mentalite (8) ve taktik stilleri (13)
- [k] İlk 11 seç / oyuncu değiştir — `TacticTeamSelectionDropdownEvent`
- [ ] Yedek kulübesi
- [ ] Duran top düzenleri (korner, faul, penaltı sırası)
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
- [ ] Oyuncu ara (filtreli)
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

- [x] `fm next` — ilerle, bloke eden mesajları bildir
- [k] Maça gir / çık — `EventFmxGoToMatch`, `EventFmxExitMatch`; `MatchReference` 206 property
- [k] Maç içi taktik değişimi — `EventFmxMatchTacticsChanged`, `MatchTacticReference` (227)
- [ ] Maç öncesi brifing / takım konuşması
- [ ] Maçı simüle et (hızlı sonuç)
- [ ] Canlı maç takibi (metin akışı)
- [ ] Devre arası konuşma
- [ ] Maç içi: oyuncu değişikliği, taktik değişimi, shout
- [ ] Maç sonrası: rating, istatistik, basın
- [ ] Maç istatistikleri (xG, pas haritası, ısı haritası → terminalde?)
- [ ] Highlight özeti (metin)
- [?] Maç içi gerçek zamanlı yönetim (v2 — bambaşka problem)

---

## 8. Lig, kupa, takvim

- [ ] Lig tablosu (`fm table`)
- [ ] Fikstür / sonuçlar (`fm results`, `fm fixtures`)
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
- [x] Mesaja cevap ver / aksiyon al (`/newsaction`)
- [k] Basın toplantısı — `PressConferenceEditSessionReference` + `EventFmxPressConferenceAnswerQuestion` / `NoComment` / `EarlyExit` / `StormOut` (COOKBOOK §3)
- [ ] Maç öncesi/sonrası röportaj
- [k] Oyuncuyla konuş — `ConversationEditSessionReference` (24) + `EventFmxConversationReplySelected` (COOKBOOK §4)
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
- [ ] Otomatik kaydetme sıklığı
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

- [x] Renkli, hizalı tablolar
- [ ] Sparkline / mini grafik (form, kondisyon trendi)
- [ ] `--json` her komutta (script'lenebilir)
- [x] Pipe dostu çıktı (`fm squad | grep ...`)
- [~] İnteraktif oturum (`fm start` sonrası komutlar `fm` önekisiz) — TUI değil, REPL; tırnaklı
      ifadeler tek kelime sayılıyor
- [ ] Fuzzy isim eşleme (`fm player osim` → Osimhen)
- [ ] Kısaltmalar / takma adlar
- [ ] Tab tamamlama (zsh/bash)
- [ ] `fm status` — tek ekranda özet (tarih, sıradaki maç, inbox, uyarılar)
- [ ] Onay isteyen aksiyonlar (transfer teklifi gibi) için `--yes`
- [ ] Geri alma yok — yıkıcı aksiyonlarda uyarı
- [x] Oyun kapalıysa otomatik başlat (`fm start`)

---

## 15. MCP + doğal dil

- [ ] MCP server — komutları Claude'a tool olarak aç
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

**Kayıplar / engeller**

- **Basın toplantısı görülmedi** — kayıtta bekleyen yok. Sırada 4. adıma indi.
- **Kontrol adresleri ekran değişince değişiyor.** Çözüldü: `/press?text=` ve
  `/select?name=&choice=` etiketle buluyor; etiket birden çok kontrole uyarsa reddediyor.
- **Test tıklamaları kayda yazılmadı.** Oyun kaydedilmeden üç kez yeniden başlatıldı; "okundu
  işaretle" dahil hiçbir değişiklik kalıcı değil.
- **Kondisyon -1 = sakat.** Talisca (hamstring) ve Becão (ön çapraz bağ); "Injuries"
  görünümünden doğrulandı, `fm squad --fitness` durumu ayrı kolonda gösteriyor.
- **Menüden sayfa açılamıyor.** Menü satırını seçmek ve menünün kendi event'lerini kuyruğa
  koymak denendi (Finances, Tactics, First Team); üçünde de ekran değişmedi. Sıradaki ilk iş.
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
