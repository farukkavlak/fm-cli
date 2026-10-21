Bu, otomatik bir tur. Faruk ekranda değil (gece yatıyor); kimseye soru sorma, cevap bekleme.

Görev: `docs/BAGIMLILIKLAR.md` → **"Canlı kalanlar"** bölümündeki kutulardan **tek birini** bitir, sonra çık.
O bölüm koddan üretilir (`python3 tools/remaining.py`); bir kutu = koddaki bir çağrı yeri. Üstteki uzun
bölümler kanıt/geçmiştir, oradan madde seçme.

Amacın: projenin taşınabilirlik borcunu kapatmak — donanım bağımlılığı (sabit bekleme) ve
dil/tema bağımlılığı (yazı / konum / sıra ile tutunma) kalıntılarını, oyunun **kimliğine**
(veri yolu, olay adı, sayfa GUID, oyunun "oldu" işareti) çevirmek. Motto: her dilde, her
temada, her kulüpte, her makinede aynı çalışmalı.

## Bu turun sınırı
- Yalnızca **A** (zamanlama), **B** (dil / kimlik), **C** (ayarlar) bölümlerinden ve mümkünse **D**.
- `[?]` maddelerini atla. **§14 UX/görsel**, **G (platform)**, **H (yayın)** bölümlerine GİRME.
- Canlı listede bir kutu = bir çağrı yeri (`Dosya:satır`). Bir turda **bir kutu**; kutu koddan kalkınca düşer.
- **Önce sınırlı iş:** kanıtı hızlı toplanan küçük değişiklikler (yazı→kimlik, kısa `Until`, sabit üst sınır) — B bölümü
  ağırlıklı. Sıradaki uygun madde bu değilse bir sonrakine geç.
- **Ağır maddeleri bu koşuda SEÇME:** bir listeyi baştan sona kaydırarak okumak/ölçmek gerektirenler. Böyle bir
  maddeye gelirsen işaretleme; `tools/autopilot-quest.md` → "Sonraya bırakılanlar"a yaz ve başka madde seç. O
  listedekileri bu koşuda tekrar seçme.
- Bir madde büyükse alt maddelerine böl, ilkini yap.
- `tools/autopilot-quest.md`'yi oku (önceki turların hafızası), sonunda güncelle.

## Durumu kendin kur — "kayıtta yok" park sebebi DEĞİL
Kayıt bir **test kaydı**; ona yazık etmek diye bir şey yok. Bir madde "işsiz kayıt lazım", "maç günü değil",
"örnek mesaj yok", "bedel sözü o an yok" diye duruyorsa **o durumu kendin yarat**, ölç, sonra geri yükle:
- **İşsizlik / istifa** (Clock 179): `fm open` ile Career/Kariyer sayfasını aç, İstifa/Resign düğmesini **olayıyla**
  bul ve bas, onay penceresini geç. İşsiz kal, `fm clock`/`fm next` yolundaki 2×400 ms'i ölç.
- **Maç akışı / 3 sn'ler** (`Bound` 20 sn maddesi): `fm next` ile maç gününe gel; `fm match` ile maçı oyna
  (terminalde izleme sorusu gelirse anında sonuç seç). Maç gününde ölçülecek ne varsa ölç.
- **Örnek mesaj** (Program cümle parçaları: kaptan eki "to be selected", yönetim beklentisi "End of",
  "Transfers In/Out", sezon özeti "injuries this season", yaş cümlesi): `fm next` ile günleri/sezonu ilerlet,
  `fm inbox` + `fm news` ile mesajı bul; kaptan seçimi gibi soruları **kendin cevapla**. Mesaj gelene kadar ilerlet.
- **Bedel sözü** (Guided 161 174): `fm next` ile sözleşme/transfer görüşmesi çıkana kadar ilerle ya da açık
  konuşmada `fm talk` ile bedel iste; pencere açılınca ölç.

**Geri dönüş kuralı (pazarlık yok):** bu testler için durumu değiştirdiysen **`fm save` YOK** — diske yazma.
İşin sonunda `fm load "<ana kayıt>" --no-save` ile ana kayda dön (adını `fm saves` ilk satırından al) ve `/state` ile
dil `turkish`, tarih ana kayıtla aynı olduğunu doğrula. Sadece okuma yaptıysan yükleme gerekmez.

## Boşta kalırsan: aracı kariyerde dene (hata avı)
CHECKLIST'te seçilebilecek madde kalmadıysa (kalanlar `[?]`, park edilmiş ya da Faruk'un kararını bekliyor), turu boş
kapatma — **aracı gerçek bir menajer gibi çalıştır ve hatasını ara**:
1. Önce toplu kontrol: `tools/sweep.sh` (oyun maç arasında olmalı). Kırılan komut varsa onu düzelt — bu turun işi o.
2. Sonra oyunun akışını yaşa: `fm next` ile günleri ilerlet, maç günü `fm match` ile maçı oyna, gelen mesajları
   `fm inbox` / `fm reply` / `fm talk` ile cevapla; `fm tactic`, `fm training`/`fm schedule`, `fm offer`,
   `fm contract`, `fm scout`, `fm staff`, `fm squad`, `fm table`, `fm finances`, `fm board`, `fm youth` komutlarını
   sırayla dene. Her komutta `rc`, insan çıktısı ve `--json` hâli makul mü, boş/yanlış/İngilizce kalan bir şey var mı?
3. Hata bulursan (çökme, `rc≠0`, boş tablo, yanlış sayı, yanlış dil): en küçük kanıtı topla (komut + öncesi/sonrası +
   `rc` + stderr), düzelt, `docs/FINDINGS.md`'ye §8.N olarak yaz, commit at. **Bir turda en fazla bir hata.**
4. Hata yoksa: ne denediğini ve çıktıların sağlam olduğunu `tools/autopilot-quest.md`'ye yaz,
   `PARK: boşta — hata çıkmadı` ile çık.
5. Kayıt: test için oynadıysan **`fm save` YOK**; tur sonunda `fm load "<ana kayıt>" --no-save` ile ana kayda dön.

## Bütçe (önce oku)
Bu tur limit/token yakıyor. **Gereksiz keşif yapma.** Maddeyi seç, ilgili dosyayı `grep -n` ile bul,
yalnız gereken satır aralığını oku. Bütün dosya ya da büyük doc (FINDINGS/PLAN) baştan sona okuma.
Turu **~30 dakikada** bitir. 30 dakikayı geçtiysen ve iş hâlâ ölçüm/keşifte ise elindekiyle karar ver: bitiremediysen
maddeyi `[?]` yap (neden + Faruk'a soru), commit et, çık. **60 dakikayı doldurup boşa yanma** — zaman aşımına takılan
tur hiçbir şey kazandırmaz, üstelik yaptığın yarım iş geri alınır.

## Kurallar (pazarlık yok)
1. **Önce ölç, sonra değiştir.** Yeni bir işaret en az ÜÇ durumda (sayfa ilk kez açılıyor /
   zaten açık / başka sayfa açık) ve İKİ dilde (TR + EN; `fm settings dil` ile geç, sonunda
   Türkçe'ye DÖN) kanıtlanmadan koda girmez.
2. **Sabit bekleme yazma** (`Task.Delay` ile "bekle sonra yap"). Oyunun kendi işaretini bekle:
   değer değişti, öğe çizildi, `Processing` bitti, satırlar oturdu. Süre yalnızca cömert üst sınır.
3. **Bağımlılık yerine bağımlılık koyma.** Yazıyı stil sınıfıyla, sıra numarasını `up=N` ile,
   sabit beklemeyi tek seferlik davranışla değiştirmek de bağımlılıktır. Dokunduğun fonksiyondaki
   BÜTÜN süre/yazı/sıra bağımlılığını birlikte temizle.
4. **Kanıtlanmazsa eskisi kalır (churn yok).** Yeni hal eskisinden kanıtlanmış biçimde iyi değilse
   `git restore <dosya>` ile geri al, maddeyi `[?]` yap, nedenini ve Faruk'a sorulacak net soruyu
   yanına yaz, commit et, çık.
5. **Tek denemeye güvenme.** FM sık sık "oldu" deyip hiçbir şey yapmaz; sonucu HER ZAMAN veriden
   doğrula. Negatif durumu da dene.
6. **Anlatım "kaç ms" değil "hangi işaret".** Süre yalnızca işaretin tuttuğunu kanıtlamak için ölçülür.

## "Bitti" tanımı — bunlar yoksa BİTTİ deme
Bir madde ancak ŞUNLARIN HEPSİ olunca biter:
1. Değişiklik uygulandı (ya da park kararı verildi).
2. En az ÜÇ durum + İKİ dil denendi; her denemenin **komutu ve çıktısı** (öncesi/sonrası) elinde.
3. Bu kanıt `tools/autopilot-quest.md`'ye yazıldı.
4. Commit atıldı.
Kanıt yoksa madde bitmemiştir: ya daha ölç, ya `[?]` yap. "Yaptım galiba / muhtemelen çalışır" yok.
Davranışı paylaşılan koda dokunduysan (Bridge, OpenPage/OpenSubpage, reply, ortak yardımcılar) regresyon için
`tools/sweep.sh` çalıştır (oyun maç arasında olmalı) ve kırılan komut çıkarsa düzelt.

## Bitirme (tek madde, sonra dur)
- Maddeyi `[x]` yap ve bir satırla ne yapıldığını yaz. Sonra `python3 tools/remaining.py --write` çalıştır:
  canlı blok koddan yenilenir, biten yerin kutusu düşer (`--count` üç sayıyı verir: borç `Task.Delay` · yazıyla
  basış · kısa `Until`).
- Kanıtı `tools/autopilot-quest.md`'ye yaz: hangi komut, öncesi/sonrası, üç durum, iki dil.
- Yeni bulgu varsa `docs/FINDINGS.md` **SONUNA** §8.N olarak 3–6 satır ekle (`cat >> ...`);
  PLAN günlüğü gerekmiyorsa dokunma. Büyük dosyaları bütün okuma: `grep -n` + gereken satır aralığı.
- Kullanıcıya görünen davranış değiştiyse `docs/manual/KILAVUZ.md`.
- **Commit:** İngilizce Conventional Commits; başlık deponun üslubunu taklit etsin — `perf: contracts
  reads each term until it has arrived instead of a fixed pause` gibi: ne yaptığını şimdi vs eskisi
  (küçük harf, davranış odaklı). Son satır `Co-Authored-By: OpenCode <noreply@opencode.ai>`. Yalnızca
  kendi değiştirdiğin dosyaları `git add <dosya>` (`-A` DEĞİL). **Push YOK** — Faruk yapıyor.
- Kayıt test kaydı, hiç önemi yok: maç oyna, zamanı ilerlet, antrenör/kaptan/taktik değiştir,
  oyundaki mesajları (kaptan seçimi dahil) kendin cevapla, kayıt al — sorma, park etme. Yalnızca
  kayıt silme gibi gerçekten Faruk'un kararı olan bir şey çıkarsa yapma: `[?]` yap, soruyu yaz,
  commit et, çık.
- Oyun açık değilse `fm start </dev/null` ile aç (oyun ve eklenti kodunda tuzağı `docs/FINDINGS.md`'de).
- Commit edilmemiş, senin olmayan bir değişiklik görürsen dokunma.
- Son mesajın tek satır olsun: `BİTTİ: <madde>` ya da `PARK: <madde> — <neden>` ya da `TAKILDI: <neden>`.
