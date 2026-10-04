# fm-cli — Yol Haritası

> FM26'da yapılabilen her şeyi CLI komutuna çevirme listesi.
> Durum: `[x]` çalışıyor · `[~]` kısmen · `[ ]` yapılacak · `[?]` yapılabilirliği belirsiz

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
- [x] Oyunu gizli başlatma (`fm start`, pencere saklanır — headless imkânsız, §8.19)
- [x] Plugin ↔ CLI protokolü: JSON çıktı (`--json`)
- [x] Hata yönetimi: oyun kapalıysa/yükleniyorsa anlamlı mesaj
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
- [ ] `fm squad --fitness` — kondisyon/yorgunluk
- [ ] `fm squad --morale` — moral
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

- [ ] Mevcut taktiği göster (`fm tactic`)
- [ ] Formasyon değiştir
- [ ] **In Possession / Out of Possession** ayrı şekiller (FM26 yeniliği)
- [ ] Oyuncu rolleri (Inverted Full-Back, Wide Centre-Back dahil)
- [ ] Takım talimatları
- [ ] Taktik stilleri (Low Block dahil)
- [ ] İlk 11 seç / oyuncu değiştir
- [ ] Yedek kulübesi
- [ ] Duran top düzenleri (korner, faul, penaltı sırası)
- [ ] Kaptan / yardımcı kaptan
- [ ] Taktik kaydet / yükle / kopyala
- [ ] Rakibe göre taktik önerisi
- [ ] Taktik akıcılığı (familiarity)

---

## 4. Antrenman

- [ ] Haftalık antrenman programı göster
- [ ] Antrenman programı değiştir
- [ ] Maç hazırlığı odağı
- [ ] Bireysel antrenman (oyuncu bazlı)
- [ ] Pozisyon öğretme
- [ ] Mentor / mentee ilişkileri
- [ ] **Antrenman raporları** (form + medikal + antrenör geri bildirimi — FM26'da genişletildi)
- [ ] Yoğunluk ayarı
- [ ] Dinlenme günleri
- [ ] Gençlik antrenmanı

---

## 5. Transfer ve sözleşme

- [ ] Transfer listem (`fm transfers list`)
- [ ] Oyuncu ara (filtreli)
- [ ] Teklif ver
- [ ] Gelen teklifleri gör / kabul / reddet / pazarlık
- [ ] Sözleşme teklifi / yenileme
- [ ] Sözleşme maddeleri (bonus, serbest kalma, satış yüzdesi)
- [ ] Kiralık gönder / al
- [ ] Oyuncuyu transfer/kiralık listesine koy
- [ ] Serbest oyuncular
- [ ] Bonservissiz ön anlaşma
- [ ] Menajerle görüşme
- [ ] Transfer bütçesi ↔ maaş bütçesi aktarımı
- [ ] Takas teklifi (`ExchangePlayers` binding'leri görüldü)
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
- [ ] Basın toplantısı
- [ ] Maç öncesi/sonrası röportaj
- [ ] Oyuncuyla konuş (övgü, uyarı, söz verme)
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
- [~] İnteraktif oturum (`fm start` sonrası komutlar `fm` önekisiz) — TUI değil, REPL
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

## Bugünün bilançosu (2026-09-28)

### + Kazanımlar

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

### − Kayıplar / engeller

- **`ReadOnlySpan<char>` köprüsü çalışmıyor** (§8.2). İki yöntem de sessizce boş span üretti.
  Sonuç: path string'i ile `Get`/`Exists`/`Bind` **kullanılamıyor**.
- **Bunu geç fark ettik.** `Exists("Game") = true` yanlış pozitifti ve bir hafta doğru sanıldı.
  Ders: bir lookup'ın uydurma girdiyi reddettiğini de test et.
- **Oyuncu özellikleri henüz canlanmıyor.** Ağaç tembel; düğümlerde sadece `UniqueId` var.
- **Bir kez oyunu çökerttim** — `Bind`'e null delegate verdim.
- **Property ID'leri okunaksız** — bir kısmı FourCC (`NEWS`, `gend`), çoğu hash; release build'de
  açıklamalar ayıklanmış.

### → Sıradaki adım

Squad ekranını event/panel ile açtırmak. UI'ın kendisi oyuncu özelliklerini canlandırıyor; biz de
ağaçtan okuruz. Span'a gerek yok, ekranın görünür olması da gerekmiyor.
