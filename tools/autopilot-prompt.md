Bu, otomatik bir tur. Faruk ekranda değil (gece yatıyor); kimseye soru sorma, cevap bekleme.

Görev: `docs/BAGIMLILIKLAR.md` en üstündeki CHECKLIST'ten **tek bir maddeyi** bitir, sonra çık.

Amacın: projenin taşınabilirlik borcunu kapatmak — donanım bağımlılığı (sabit bekleme) ve
dil/tema bağımlılığı (yazı / konum / sıra ile tutunma) kalıntılarını, oyunun **kimliğine**
(veri yolu, olay adı, sayfa GUID, oyunun "oldu" işareti) çevirmek. Motto: her dilde, her
temada, her kulüpte, her makinede aynı çalışmalı.

## Bu turun sınırı
- Yalnızca **A** (zamanlama), **B** (dil / kimlik), **C** (ayarlar) bölümlerinden ve mümkünse **D**.
- `[?]` maddelerini atla. **§14 UX/görsel**, **G (platform)**, **H (yayın)** bölümlerine GİRME.
- **Önce sınırlı iş:** kanıtı hızlı toplanan küçük değişiklikler (yazı→kimlik, kısa `Until`, sabit üst sınır) — B bölümü
  ağırlıklı. Sıradaki uygun madde bu değilse bir sonrakine geç.
- **Ağır maddeleri bu koşuda SEÇME:** bir listeyi baştan sona kaydırarak okumak/ölçmek gerektirenler. Böyle bir
  maddeye gelirsen işaretleme; `tools/autopilot-quest.md` → "Sonraya bırakılanlar"a yaz ve başka madde seç. O
  listedekileri bu koşuda tekrar seçme.
- Bir madde büyükse alt maddelerine böl, ilkini yap.
- `tools/autopilot-quest.md`'yi oku (önceki turların hafızası), sonunda güncelle.

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
- Maddeyi `[x]` yap ve bir satırla ne yapıldığını yaz.
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
