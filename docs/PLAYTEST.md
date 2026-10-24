# Test kontrol listesi

## Oturum başlangıcı

- [ ] `fm status` tarih, kulüp, sıradaki maç ve bekleyen işi doğru söylüyor.
- [ ] `fm doctor --install` yedi kurulum adımını denetliyor.
- [ ] `fm inbox --tasks` Görevler sekmesindeki teklif/registration mesajlarını gösteriyor.
- [ ] Oyun ön planda değilken `next` ve `press` takılmıyor; komut oyunu öne getiriyor.

## Günlük akış

- [ ] `next` yalnız gerçek bir engelde duruyor; durduğunda nedeni ve çözümü yazıyor.
- [ ] Basın toplantısı, sakatlık, teklif ve disiplin mesajları `reply` ile açılıp kapanıyor.
- [ ] `talk` seçenekleri gösteriyor; maç sonu basın toplantısı asistanla geçilebiliyor.
- [ ] `save --yes` sonrası `fm saves` yeni saati ve dosyayı gösteriyor.

## Maç günü

- [ ] `tactic pick` ilk 11'i dolduruyor; boş kadro `match` tarafından açıkça söyleniyor.
- [ ] Yeni sezonda forma numarası istenirse `numbers --auto` gönderiyor.
- [ ] `match --instant` raporu **bu maçın** skorunu basıyor; önceki maçın raporu tekrarlanmıyor.
- [ ] `match --play` anlatım, gol, devre ve maç sonu duraklarını geçiyor.
- [ ] Maç sonrası sonuç ekranı `next` ile kapanıyor; köprü yanıt veriyor.

## Sık kırılan yerler

- **Olay kuyruğu:** bir press/dialog kaynağı gelmezse sonraki `press` komutları da bekler; `/queue` sıfır olmalı.
- **Odak:** FM bazı Continue/UI olaylarını yalnız ön plandayken işler; `next`/`press` öncesi oyun öne gelmeli.
- **Görevler:** teklif mesajı varsayılan inbox'ta görünmeyebilir; `inbox --tasks` ile bakılmalı.
- **Eski rapor:** anında sonuçta son fikstür değişmeden rapor okunmamalı.
- **Forma numaraları:** sezon başında `SquadNumbers` ekranı `match`i bloklamamalı.
- **Sakatlık:** tedavi düğmeleri `reply` seçeneklerinde yoksa `buttons` + tam etiketli `press` kullanılmalı.
- **Eklenti sürümü:** eklenti değişikliği açık oyuna yüklenmez. `/press` "unknown route" (404) dönüyorsa
  oyun eski eklentiyi taşıyor: `save --yes`, `stop --no-save`, `start`.

## Bilinen açık uçlar

- Gölge paket ve BepInEx dağıtımı paket içinde; Steam'siz ikinci kullanıcıda oyun açılmıyor.
- Bazı teklif satırları “Sözleşme” durumunda cevapsız kalabiliyor; Görevler filtresi ve `offers` birlikte kontrol edilmeli.
- Hareketli pencerede yakalanan görüntülerde görünüm verisi bir kare geç gelebilir; `tools/manual/shots.sh` yeniden dener.
