Bu, otomatik bir tur. Faruk ekranda değil; kimseye soru sorma, cevap bekleme.

Görev: `docs/BAGIMLILIKLAR.md` en üstündeki CHECKLIST'ten **tek bir maddeyi** bitir, sonra çık.

0. Hafızadaki (MEMORY.md) bütün kurallar geçerli; hiçbirini atlama. Özellikle:
   - Commit: İngilizce Conventional Commits (`fix:`, `feat:`, `docs:`…), son satır Co-Authored-By; yalnızca kendi
     değiştirdiğin dosyaları ekle (`git add <dosya>`, `-A` değil).
   - Yeni kod çevresindeki koda benzesin (yorum yoğunluğu, adlandırma, İngilizce yorumlar).
   - CLI'ın yazdığı her yazı `L(...)` ile, karşılığı `cli/fm/labels-english.tsv`'ye; oyunun yazıları oyunun dilinde
     (`display-in-game-language`). Oyunda kimlikle tutun: veri yolu, olay adı, öğe adı; ekran yazısı ya da sıra numarası
     değil (`anchor-by-identity`). Her dil/kulüp/lig/platform için yaz (`global-tool-vision`).
   - Komut davranışı değişirse `docs/manual/KILAVUZ.md` (`user-manual-upkeep`); bulgu FINDINGS'e, günlük PLAN.md'ye.
   - Tek denemeye güvenme, negatif durumu da dene (`verify-before-declaring-fixed`); pkill/oyun tuzakları `fm-dev-loop`'ta.
   - Commit edilmemiş, senin olmayan bir değişiklik görürsen dokunma; checklist'te öyle bir madde varsa onu bitir.
1. Hafızadaki `portability-plan`, `measure-before-rework`, `no-timing-dependence`, `anchor-by-identity` ve `fm-dev-loop`
   notlarını oku. Checklist'te sıradaki `[ ]` maddeyi seç (yukarıdan aşağı; büyük bir maddeyi alt maddelere bölebilirsin,
   o zaman ilk alt maddeyi yap). `[?]` olanları atla.
2. Önce ölç, sonra değiştir: yeni bir işareti en az üç durumda (ilk açılış / zaten açık / başka sayfa açık) ve mümkünse
   iki dilde gözle. Sabit bekleme (`Task.Delay` ile "bekle sonra yap") yazma; oyunun kendi işaretini bekle.
3. Değişikliği oyunda dene (Türkçe; gerekiyorsa İngilizceye geç ve geri dön, nasıl yapılacağı hafızada). Yeni hal eskisinden
   kanıtlanmış biçimde iyi değilse geri al, eskisi kalsın; maddeye ne ölçtüğünü yaz.
4. Bitince: maddeyi `[x]` yap ve bir satırla ne yapıldığını yaz; FINDINGS'e gerekiyorsa kısa not; İngilizce Conventional
   Commit at (son satır `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`); hafızadaki `portability-plan`'a bir satır ekle.
5. Faruk'un kararı gereken bir şey çıkarsa (para harcama, kulüp/lig seçimi, kayıt silme, oyunda geri alınamaz bir seçim)
   yapma: maddeyi `[?]` yap, nedenini ve sorulacak soruyu yanına yaz, commit et ve çık.
6. Oyun açık değilse `fm start < /dev/null` ile aç. Eklenti değiştiyse önce `fm save --yes`, sonra yeniden başlat
   (`fm-dev-loop`). Oyunda bir mesaj cevap bekliyorsa ve cevap Faruk'un kararıysa dokunma, başka maddeye geç.
7. Tek madde, sonra dur. Son mesajın tek satır olsun: `BİTTİ: <madde>` ya da `PARK: <madde> — <neden>` ya da
   `TAKILDI: <neden>`.
