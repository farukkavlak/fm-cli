# FM27'ye taşınacaklar — fm-cli'dan öğrendiklerimiz

15 Ekim 2026. FM27 bir ay sonra çıkıyor. FM26'ya özel daha fazla iş yapmak yerine proje rafa kalkıyor. Bu dosya, FM27
geldiğinde buradan devam edebilmek için yazıldı. Ayrıntılar başka dosyalarda duruyor, burada yalnızca özü var:

- `docs/FINDINGS.md`: oyunun içinde ne bulduk, §8.1–§8.188, kanıtlarıyla
- `docs/BAGIMLILIKLAR.md`: nerede neye bağlıyız, yarım kalan checklist
- `PLAN.md`: komutlar, ürün kararları, günlük
- `docs/manual/KILAVUZ.md`: kullanıcı kılavuzu

---

## 1. Motto

> **fm-cli kendimiz için değil, herkes için.** Her dilde, her temada, her kulüpte ve ligde, her makinede ve her
> platformda aynı şekilde çalışmalı. Bizim kayıtta, Türkçe oyunda, bizim Mac'te çalışması yeterli değil.

Bu motto şu sorulara verilen cevaplardan oluşuyor:

| Neye bağlı olmayacağız | Nasıl sağlıyoruz | Yapmayacağımız şey |
|---|---|---|
| **Oyunun diline** | Oyuna dilden bağımsız kimlikle tutunuyoruz: veri yolu, olay adı, sayfa GUID'i, oyunun kimlik numarası. Yazı gerekiyorsa oyunun kendi çeviri tablosundan geçiriyoruz. Kullanıcıya her şeyi oyunun dilinde gösteriyoruz. | `press "Confirm"` gibi İngilizce yazıyla basmak, cümle içinde "years old" aramak, kendi Türkçe→İngilizce sözlüğümüzü yazmak. |
| **Makinenin hızına** | Oyunun "oldu" işaretini bekliyoruz: değer değişti, öğe çizildi, `Processing` bitti, sayfa bağlandı. Verilen süre yalnızca cömert bir üst sınır. | "1 sn bekle, sonra oku" (`Task.Delay`), "3 sn'de gelmezse yok say". |
| **Ekrana, temaya, çözünürlüğe** | Mümkünse ekranı hiç okumuyoruz, veriyi doğrudan çekiyoruz (`/pull`). Okumamız gerekirse satırı içindeki kimlikle (`UniqueId`) buluyoruz. | Öğe adına, stil sınıfına ya da `up=N` gibi "yazıdan N basamak yukarı" konumuna güvenmek. |
| **Sıraya** | Bir şeyi neyse o olduğu için buluyoruz: o olayı gönderen düğme, o oyuncunun satırı. | "Başlıktan sonraki 2. yazı", `rest[i + 2]`, "listenin 3. seçeneği". |
| **Kayda, kulübe, lige** | Lig adı, altyapı takımları, para birimi, ondalık işareti ve klasörler oyundan okunuyor. Önbellek dil ve kayda göre ayrı tutuluyor. | Kodda "Süper Lig", "U19", "£", "haftalık maaş", `d/M/yyyy` gibi varsayımlar. |
| **Oyunun ayarlarına** | Para ve sayı biçimini oyunun kendi biçimlendiricisinden öğreniyoruz (`/money`, `GameNumbers`). | Tarih, birim ya da maaş dönemi tahmin etmek. |
| **Platforma** | Yollar ve başlatma tek bir yerde duruyor, oyunun klasörlerini oyuna soruyoruz (`/folders`). | macOS araçlarını (`osascript`, `pgrep`) kodun her yerine dağıtmak. |

**Tutunma sırası, en sağlamdan en kırılgana:**

1. Veri yolu + sayı/enum alanı · olay adı · oyunun kimlik numarası · sayfa GUID'i
2. Veri yolu + `…String` alanı (değer oyunun dilinde gelir)
3. Kontrol/öğe adı, `up=N`, sıra numarası (tema değişince ya da güncellemede kırılır)
4. Ekran yazısı, cümle ayrıştırma (dil değişince kırılır)

Yeni kod 1. sırada başlar. Aşağı inmek zorunda kalırsak nedenini yorum olarak yazarız.

---

## 2. Çalışma kuralları (FM26'da bunları acı çekerek öğrendik)

1. **Önce toplu keşif, sonra tek tek deneme.** Oyun IL2CPP olsa da şema, olay listesi ve alan adları statik olarak
   çıkarılabiliyor. Bir dökümde 6.571 alan çıktı. Canlı oyunu yalnızca *değerler* için kurcalıyoruz. Yeni bir alana
   başlarken ilk soru şu: "Oyun bunu bir yerde liste olarak tutuyor mu?"
2. **Tek denemeye güvenmiyoruz.** FM çok sık "oldu" deyip hiçbir şey yapmıyor. Her şeyi 3–5 kez deniyoruz, negatif
   durumu da deniyoruz.
3. **Koda girmeden önce üç durumda ve iki dilde ölçüyoruz.** Üç durum: sayfa ilk kez açılıyor, sayfa zaten açık, başka
   bir sayfa açık. İki dil: Türkçe ve İngilizce. Yeni yöntem kanıtlanmadıysa eskisi yerinde kalıyor (churn yok).
4. **Bir bağımlılığı kaldırırken yerine başka bir bağımlılık koymuyoruz.** Sabit beklemenin yerine yazı, stil sınıfı ya
   da tek bir kez görülen davranış koymak da bağımlılıktır. Bir fonksiyona dokunduğumuzda içindeki **bütün** süre, yazı
   ve sıra bağımlılıklarını birlikte temizliyoruz.
5. **Eklentide "soru" ile "eylem" ayrı tutuluyor.** Soru soran yol oyunda hiçbir şeyi değiştirmemeli.
6. **Anlatırken "kaç ms" değil, "hangi işaret" diyoruz.** Süre yalnızca işaretin tuttuğunu kanıtlamak için ölçülür.
7. **UX önce gelir.** Komutun çalışması işi bitirmez. Terminalde görsel ve akıcı bir oynanış olmalı (4-3-3, 4-3-3 gibi
   görünmeli). Kullanıcı adına seçim yapan varsayılanlar yok, seçim kullanıcıya sunuluyor.

---

## 3. İşe yarayan mimari (FM27'de de büyük ihtimalle aynısı)

```
fm (C# CLI, ~100 dosya)  ──HTTP──▶  127.0.0.1:7777  ◀──  FmCliProbe (BepInEx eklentisi, oyunun içinde)
MCP sunucusu (aynı CLI)                                   └─ SI.Bindable veri ağacı + navigasyon + olaylar
```

- **Algı:** Ekran okumak yerine oyunun bağlama ağacını (`SI.Bindable.Bindings`) kullanıyoruz. `BindingPath.Create("…")`
  ile yazıdan yol kuruluyor ve tipli veri geliyor. Ağaç tembel çalışıyor: bir yola bağlanınca canlanıyor, ekran açmak
  gerekmiyor. Örnek: `Human.Team.MentoringGroups` ile mentorluk grupları sayfa açmadan 0,7 sn'de okunuyor (önceden 8,4 sn sürüyordu).
- **Eylem:** Ham tuş ya da tıklama göndermiyoruz. Oyunun anlamsal olaylarını (`EventFmxClearChanges`, `ActionID`),
  sayfa GUID'iyle açmayı, onay penceresinde yuvayla (birincil/ikincil) basmayı kullanıyoruz.
- **Dil:** Eklenti oyunun yazısını içeride İngilizceye çeviriyor (oyunun çeviri tablosuyla). Gösterirken oyunun diline
  geri dönülüyor (`X-Shown`, `GameWords`). CLI'ın kendi etiketleri `L(...)` ile yazılıyor ve oyunun dilinde çıkıyor.
  Motorun kurduğu cümleler `languages.fmf`'ten geliyor.
- **Reddedilenler:** Kayıt dosyasını ayrıştırmak (kaydetmek çok yavaş). Ekran görüntüsü + vision (yalnızca B planı).
- **Ön koşul:** Apple Silicon'da resmi BepInEx çalışmıyor, DadMych/fm26-player-export-macos yamalı paketi gerekiyor.

---

## 3½. Repo planı ve taşınacak parçalar

**Aynı repoda devam ediyoruz.** FM27'ye başlarken bugünkü kodun tamamı `archive/fm26/` klasörüne taşınacak (`git mv`,
böylece geçmiş korunur). FM27 kodu kök dizinde sıfırdan, temiz başlayacak. FM26 arşivi başvuru kaynağı olacak: "bu ekran
FM26'da nasıl çalışıyordu, hangi olayı gönderiyordu" diye sürekli bakacağız. Kopyalanan her parçayı yukarıdaki kurallara
göre gözden geçireceğiz. Yazıya, süreye ya da sıraya bağlı kod körü körüne kopyalanmayacak.

**Oyunun içeriğinden bağımsız olduğu için büyük ihtimalle neredeyse aynen taşınacaklar:**

| Parça | FM26'da nerede |
|---|---|
| HTTP köprüsü (eklenti tarafı) | `plugin/FmCliProbe/src/HttpBridge.cs` |
| IL2CPP arayüz yardımcıları (span köprüsü vb.) | `plugin/FmCliProbe/src/Interop.cs` |
| Oyunun çeviri tablosu katmanı | `plugin/FmCliProbe/src/Translations.cs` |
| Köprü istemcisi, `Until` (işaret bekleme) | `cli/fm/Bridge.cs`, `cli/fm/Program.cs` |
| Oyunun dilinde gösterim, CLI etiketleri | `cli/fm/GameWords.cs`, `cli/fm/Labels.cs`, `cli/fm/labels-english.tsv`, `tools/labels/` |
| Para ve sayı biçimi | `cli/fm/MoneyFormat.cs` |
| Motor yazıları (`languages.fmf`) | `cli/fm/EngineTexts.cs` |
| Oyunun klasörleri, başlatma | `cli/fm/GameFolders.cs`, `cli/fm/Launcher.cs` |
| MCP sunucusu | `cli/fm/Mcp.cs` |
| Statik döküm, toplu tarama | `tools/sigdump/`, `tools/sweep.sh`, `tools/buttons-catalog.py` |

**Değişecek olanlar (oyunun içeriği):** sayfa GUID'leri (`Screens.cs`), veri yolları, olaylar, roller, yeni ve kalkan
özellikler. Asıl iş zaten bunlar. Her birini FM26 ve FM27 dökümlerini karşılaştırarak bulacağız.

**BepInEx / Apple Silicon yaması:** FM27 için topluluktan (DadMych, Thunderstore) bir paket çıkmazsa yamayı kendimiz
yapacağız. FM26'da sorun arm64'teki IL2CPP ClassInjector hatasıydı. DadMych paketinin neyi nasıl yamadığı bunun için
başlangıç noktası.

---

## 4. FM27 çıktığı gün: ilk kontrol listesi

Sıra önemli, çünkü her soru projenin zorluğunu değiştiriyor.

1. [ ] Unity sürümü, IL2CPP mi, `global-metadata.dat` şifreli mi (FM26: Unity 6000.0.52f1, şifresiz).
2. [ ] BepInEx arm64'te ayağa kalkıyor mu? Yamalı paketin FM27 sürümü var mı (DadMych, Thunderstore resmi pack)?
3. [ ] Il2CppDumper dökümü. `FM.UI`, `FMGame`, `SI.Bindable`, `SI.UI` assembly'leri hâlâ var mı?
4. [ ] `SI.Bindable.Bindings` + `BindingPath.Create(string)` duruyor mu? Bu tutuyorsa projenin %80'i taşınır.
5. [ ] Eklentinin kullandığı özel alanlar (`m_choices`, `m_key`, `m_selectionIndex`, `m_boundElements`… ~25 ad)
   değişti mi? Bunlar `plugin/FmCliProbe/src` içinde.
6. [ ] Sayfa GUID'leri (`cli/fm/Screens.cs`, 36 tane) ve olay adları (`OpenSubtabEvent`, `SpeakToAction`,
   `EventFmxClearChanges`…) aynı mı? Statik dökümden toplu karşılaştırılabilir.
7. [ ] Veri yolları (`Human.Team…`, `Club.Teams`, `PlayerTraining`…). Şema dökümünü FM26'nınkiyle `diff` et.
8. [ ] Çeviri tablosu ve `languages.fmf` aynı biçimde mi?
9. [ ] Sonra `tools/sweep.sh` ile bütün komutları iki dilde tara, kırılanları listele.

**Yaklaşım:** FM26 kodunu körü körüne taşımıyoruz. Önce statik dökümleri diff'liyoruz (madde 1–8), sonra yalnızca
kırılanları onarıyoruz. Kimlikle yazılmış komutların büyük ihtimalle az değişiklikle çalışması bekleniyor. Yazıya ve
konuma bağlı olanlar ise yeniden yazılmalı.

---

## 5. FM26'da öğrendiğimiz oyun tuzakları (FM27'de de görülebilir)

- **Oyun sık sık yanlış pozitif veriyor.** Başarılı olduğunu söylüyor ama hiçbir şey yapmıyor. Sonucu her zaman veriden
  doğrula.
- Bağlanmamış alan `{}` döner. "Yol boş" ile "henüz gelmedi" aynı cevabı veriyor.
- Kaydırınca tablo satırları birden fazla adımda diziliyor. "Art arda iki aynı okuma" kuralıyla bekle.
- Oyunun `TypedValue`'larını yerinde değiştirme (havuzdan geliyorlar, ana thread kilitleniyor). Devre dışı düğmeye
  tıklama, oyun kilitleniyor. Yoklama döngüsünde bütün ağacı gezme (~40.000 düğüm, oyun çöküyor).
- Eklenti DLL'ini oyun açıkken kopyalama, önce oyunu kapat. Eklenti yalnızca açılışta yükleniyor (~1,5 dk).
- Oyunun otomatik kaydı ana dosyanın üzerine yazıyor. "Kaydetmeden kapat" ilerlemeyi siliyor.
- Maçtan sonra `Results` ekranında sayfa açmak Continue'yu bozuyor. Match Day › Overview açılınca ekran çizimi duruyor.
- Aynı oyun gününde grup silip yeniden kurmak mentorluk gruplarını karıştırıyor. Oyunun kendi iç kopyaları gün
  ilerleyince yenileniyor.
- Ekran çözünürlüğünü zorlayan modlar (FM26DisplayFix) sayfaları boş çizdiriyor.
- Köprü adresi için `127.0.0.1` kullan, `localhost` "Not Found" dönüyor. macOS'ta `pkill` seçeneklerini desenden önce
  yaz (yanlış süreçleri öldürmüştü).
- Mutual Termination gibi bazı oyun eylemleri onay istemeden işliyor. Numara ile cevap verirken önce listeyi yeniden oku.

---

## 6. Rafa kalkarken durum (15 Ekim 2026, son commit 480e560)

- **Çalışan:** 100'den fazla MCP aracı / CLI komutu. Kadro, taktik, antrenman, transfer, sözleşme, scout, maç, basın, personel,
  finans, ayarlar. Fenerbahçe → kovulma → Lincoln City ile bir sezon uçtan uca oynandı.
- **Taşınabilirlik temizliği yarıda kaldı** (`docs/BAGIMLILIKLAR.md` CHECKLIST):
  - A (zamanlama): 89 `Task.Delay` vardı. Bridge, Clock, Coaches, Contracts, Finances, Guided-pick, Happiness,
    IndividualTraining, Intensity ve Mentoring bitti. Kalanlar listede.
  - B (dil): yazıyla basılan 44 yer, cümle arayan ~15 yer ve "Acknowledge and Move On" ile basılan 8 yer (yerine
    `Event` 31 kullanılacak) kaldı.
  - C–G (ayarlar, kayıt, tema, sürüm, platform): çoğu ölçülmedi. Başka ülkede kariyer, başka tema ve Windows hiç
    denenmedi.
  - H (yayın: macOS + Steam, tek komutla kurulum): başlanmadı. Tahmini süre ~3–4 haftaydı.
- **FM27'de bu listeye nasıl bakılmalı:** Liste, FM26 kodunda nerelerin kırılgan olduğunu gösteren bir harita. FM27'de
  sıfırdan yazarken bu yerleri en baştan kimlikle yazarsak temizlik işi hiç ortaya çıkmaz.

---

## 7. FM27'de baştan farklı yapacaklarımız

1. **Birinci günden kimlikle yazacağız.** FM26'da kimlikler 28 Eylül'de biliniyordu ama hız için yazı kullandık. Sonra
   iki hafta temizlikle uğraştık.
2. **Birinci günden iki dilde test edeceğiz:** her komut Türkçe ve İngilizce oyunda.
3. **Birinci günden ikinci bir kayıt olacak:** başka ülke, başka lig, sıfırdan başlayan kariyer.
4. **Sabit bekleme hiç olmayacak.** `Until(işaret, üst sınır)` dışında bekleme yazılmayacak.
5. **Kurulum ve yayın baştan düşünülecek:** platform yolları tek bir yerde, sürüm koruması olacak (eklenti oyun sürümünü
   bilecek, desteklemediği sürümde "desteklenmiyor" diyecek), `fm doctor` kurulumu kontrol edecek.
6. **Görsel ve akıcı UX, komut sayısından önce gelecek.**
7. **Doğal dil katmanı** (yalnızca cümleyi komuta çevirme, yerelde çalışan) PLAN §15'te duruyor. Temel sağlam olunca
   başlanacak.
