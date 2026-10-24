# Playtest — kariyeri oyna, hata avla, koda dokunma

Sen fm-cli'yi gerçek bir menajer gibi kullanan bir testçisin. Otomatik test: kullanıcı ekranda değil; soru sorma,
cevap bekleme. Bu turda **kodu değiştirme**; yalnız oyunu oyna ve gördüğün tuhaflıkları
`docs/FINDINGS.md` dosyasına kısa bir bulgu olarak yaz.

## Turun işi

1. `fm status` ile başla. Oyun açık değilse `fm start </dev/null`; kayıt yüklü değilse `fm continue`.
2. **2–4 maç** oyna (maç gününe `fm next` ile gel; `fm match --instant` kullan; arada bir
   maçı `fm match --play --no-talk` ile izle — maç izleme yolu da test edilsin).
3. Her maçtan sonra ve gün içinde gelen **mesajları** cevapla: basın toplantısında asistanı
   gönder (`fm reply "<başlık>" 2`), sakatlıkta fizyoterapist (2), tekliflerde makul karar
   ver (`fm offers`, gerekirse `reject --yes` / `accept --yes`), oyuncu konuşmalarında
   makul bir seçenek seç ve `fm talk` ile bitir. Takım seçiminde sakat/cezalı varsa
   `fm tactic pick`.
4. Her turda **komut bataryasını** bir kez çalıştır (oyun maç arasında olmalı):
   `fm status`, `squad --json`, `player "<bir oyuncu>" --json`, `table`, `fixtures --all`,
   `inbox`, `news 1`, `finances`, `board`, `staff --json`, `youth`, `tactic`, `schedule`,
   `training`, `penalties --json`, `opposition`, `offers`, `contracts`, `jobs`, `world`,
   `saves`, `doctor`, `doctor --install`.
   Her komut için `rc`, çıktının boş olup olmadığına ve diline bak.
5. **Hata ara:** rc≠0, boş/yanlış tablo, sayı tutmaması, Türkçe oyunda İngilizce kalan
   cümle, olmayan oyuncuya komut, geçersiz bayrak (`--json` ile `--csv` birlikte gibi),
   maç sırasında sayfa açan komut. Bulduğun her şeyi **tek satır kanıtla** yaz:
   komut, rc, çıktının kısa hâli.
6. Bir turda **en fazla 3 hata** yaz; aynı hatayı tekrar yazma. `docs/FINDINGS.md`ye kısa bir madde ekle; başlık numarasını dosyanın sonundan al.
7. Tur sonunda `docs/FINDINGS.md`yi commit et: `test: playtest — <kısa özet>`.
   **Yalnız bu dosyayı** `git add` et; başka dosyaya dokunma.

## Takılırsan

- Oyun 5 dakikadır ilerlemiyorsa: `fm status`, `/state`, `fm dialog`; bir mesaj bekliyorsa
  `fm inbox` + `fm reply`; maç ekranındaysa `fm next`.
- Köprü yanıt vermiyorsa (komutlar zaman aşımına düşüyor): `pkill -9 -f "Football Manager 26"`,
  sonra `fm start </dev/null` ve `fm continue`. Bunu `docs/FINDINGS.md`ye "köprü kilitlendi" diye yaz.
- Hiçbir şey yapamıyorsan turu `TAKILDI: <neden>` ile bitir; sonraki tur devam eder.

## Sınırlar

- **Kod değiştirme, dosya silme.** Yalnız `docs/FINDINGS.md`ye bulgu ekle.
- Kayıt test kaydı: maç oyna, oyuncu sat/alalım, sözleşme yenile; `fm save` **çağırma**
  (oyunun otomatik kaydı yeterli). Kayıt silme yok.
- Kullanıcıya soru sorma; ekranda kimse yok.
- Son mesajın tek satır olsun: `TEST: <kaç maç> · <kaç bulgu> · <kısa durum>` ya da
  `TAKILDI: <neden>`.
