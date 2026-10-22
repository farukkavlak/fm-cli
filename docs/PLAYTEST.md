# Playtest — 2026-10-22 (gece turu)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet League One
Oyun tarihi: 17–31 Ocak 2027 · 3 maç: Blackpool 0-1 (D), Stockport 3-2 (G), Reading 1-4 (M)
`fm save` çağrılmadı; oyunun otomatik kaydı kullanıldı.

## Bulgular

1. **Maç sonrası sonuç ekranında komut rc=0 ile yutuluyor** — 30 Oca 2027 16:45, `fm opponent` rc=0: çıktı "Oyun maç sonrası sonuçlar ekranında; başka sayfa açmadan önce geç: next"; aynı anda `fm table --json --csv` de rc=0 ve tablo yerine aynı cümle. Komut çalışmadığı hâlde rc=0 dönüyor, script hata göremiyor.
2. **Türkçe arayüzde İngilizce seçenek** — 29 Oca 2027 13:59, `fm reply "Erik Ring sakatlandı"` rc=0: seçenekler "1 İğneyle Oynat (650 £) / 2 Leave To Physio (Free)"; aynı seçenek `fm reply "Erik Ring sakatlandı" 2` sonrası "Fizyoterapist Gözetimine Bırak (Masrafsız)" olarak Türkçeleşiyor.
3. **Çelişen biçim bayrakları sessizce kabul ediliyor** — 23 Oca 2027, `fm table --json --csv` rc=0: hata yok, yalnız JSON dizisi bastı ("[{\"ShowTableSplit\": ..."); `--json` ile `--csv` birlikte verilince komut reddetmeli.

---

# Playtest — 2026-10-23 (gece turu)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet League One
Oyun tarihi: 31 Oca – 25 Şub 2027 · 4 maç: Exeter 1-4 (D), Norwich 3-0 (G, FF Kupası), Wigan 0-3 (D), Wycombe 3-1 (G)
`fm save` çağrılmadı; oyunun otomatik kaydı kullanıldı.

## Bulgular

1. **Maç sonrası sonuç ekranında `fm press` butonu bulamıyor** — 6 Şub 2027 16:45, `fm buttons` "Son Sonuçlar / continue-button / ContinueGame" diyor; aynı ekranda `fm press "Son Sonuçlar"` rc=1: "error: no control labelled 'Son Sonuçlar' on the loaded screens". 31 Oca'da "Devam" ile de aynı. Buton listede duruyor ama basılamıyor; ekran ancak `fm next` ile geçiliyor.
2. **`fm reply <mesaj> 1` onayı uygulamıyor ama rc=0 ile "cevaplandı" diyor** — 6 Şub 2027 15:00, `fm reply "Kadro Seçim Önerisi: Exeter" 1` rc=0: "Cevaplandı: … → Tavsiye Edilen Kadroyla Sahaya Çık" + "Ama oyun mesajı hâlâ cevap bekliyor gösteriyor."; `fm tactic` 11'i değişmiyor (Bradley/Street/Moylan), `fm match --play --no-talk` rc=1 "Oyun bir şey soruyor: Tavsiye Edilen Kadroyla Sahaya Çık". Ancak `fm dialog 2` uyguluyor; Norwich'te aynısı tekrarlandı.
3. **Maç sonu "Puanlarımız" satırı oyuncu puanlarını yanlış basıyor** — 6 Şub 2027, `fm match --play --no-talk` rc=0: "Maçın oyuncusu: Ben House (8,0)"; `fm player "Ben House" --matches` Exeter satırı da 8,00; ama aynı çıktının "Puanlarımız" satırı "Ben House 7,2" yazıyor ve listeyi 7,8'lik Okoro yönetiyor.

---

# Playtest — 2026-10-23 (gece turu · devam)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet League One
Oyun tarihi: 25 Şub – 11 Mar 2027 · 4 maç: Salford 0-3 (M), MK Dons 0-0 (D), Burnley 2-1 (G, FF Kupası 5. Tur), Mansfield 2-1 (G)
MK Dons maçı `--play --no-talk` ile izlendi, diğerleri `--instant`; basın toplantıları asistanla geçildi, sakatlıkta fizyoterapist seçildi. `fm save` çağrılmadı.
Komut bataryası: 23/23 rc=0, boş çıktı yok.

## Bulgular

1. **Inbox yalnız ekranda görünen satırları okuyor: `reply <konu>` mesajı bulamıyor, numaralar kayıyor** — 11 Mar 2027 09:15, `fm reply "Dom Jefferies sakatlandı"` rc=1 "Konusunda 'Dom Jefferies sakatlandı' geçen mesaj yok." — mesaj 2 Mar 13:50'de geldi, `fm doctor` sakatlığı gösteriyor ve o gün listedeyken `fm reply Jefferies` açıyordu; aynı dakikada `fm inbox` 11 satır (Rutin Haber Özeti'nden başlıyor), birkaç komut sonra 21 satır (09:58 9 Mar – 11 Mar) dönüyor. 25 Şub 09:15'te `inbox` #1 "Kadro Seçim Önerisi: Wycombe" iken `reply 5` sonrası #1 "Bu Hafta: Luton…" oldu ve `reply 10` eski #9'u ("Jones'in maaşı arttırılacak") açtı.
2. **Sakatlık molasında `fm sub` sakat oyuncuyu listelemiyor, başarılı değişikliğe "olmadı" diyor** — 3 Mar 2027 ~20:47, `fm match` "Oyun sakat oyuncu sahadayken sürdürmüyor. Değişiklik şart: sub <çıkan> <giren>"; `fm sub` sahada 10 kişi gösteriyor (sakat Okoro ne listede ne işaretli); `fm sub Hackett Waine` "onaylandı ama maç hâlâ duruyor"; `fm sub Okoro McKiernan` "McKiernan seçildi ama AML hâlâ Okoro gösteriyor" — ardından `fm match` maçı 90'a kadar oynattı, yani değişiklik uygulanmıştı.
3. **Türkçe oyunda İngilizce kalan oyuncu alanları** — 11 Mar 2027 09:15, `fm player "Ben House"` rc=0 ilk satır "… İskoçya   Attacking Midfielder (Sağ, Sol, Merkez)"; aynı oyuncunun `--json`u "PlayingTime":"Fringe Player", "Personality":"Ambitious", "Nation":"Scotland" yazarken insan çıktısı "Yedek Oyuncu / Azimli / İskoçya" diyor.

---

# Playtest — 2026-10-23 (gece turu · devam 2)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet League One
Oyun tarihi: 3–21 Nisan 2027 · 3 maç: Newcastle 5-0 (M, FF Kupası Çeyrek Final; tur başında devam eden maç), Burton 1-0 (G), Oxford United 2-1 (M)
Oxford maçı `--play --no-talk` ile izlendi, Burton `--instant`; basın toplantıları asistanla geçildi, sakatlıkta fizyoterapist seçildi, Moylan cezası ve sakat/cezalı kadro `tactic pick` ile düzeltildi. `fm save` çağrılmadı.
Komut bataryası: 23/23 rc=0, boş çıktı yok (3 Nisan 14:45).

## Bulgular

1. **`fixtures --all` deplasman sonuçlarını ters yazıyor** — 21 Nis 2027 22:15, `fm fixtures` rc=0: "21 Nis Çar Deplasman Oxford United Sky Bet League One 2 - 1"; aynı maç `fm fixtures --all` rc=0: "… Oxford United (5.) … 1 - 2". Aynı çelişki 13 Mar Bristol Rovers için de var: `--all` "8 - 2", `fm player "Ben House" --matches` "Bristol Rovers 2 - 8" (House 4 gol, 10,00). Ev sahibi/deplasman skor sırası iki görünümde farklı.
2. **Geçersiz bayrak ve bilinmeyen komut rc=0 ile yutuluyor** — 21 Nis 2027 22:22, `fm status --nobayrak` rc=0 (normal durum çıktısı, hata yok), `fm table --nobayrak` rc=0 (normal tablo); 22:20 `fm bukomut` rc=0: "bilinmeyen komut: bukomut — komutlar için `help`". Script yazım hatasını göremiyor; bilinmeyen bayrak komutu sessizce normal çalıştırıyor.
3. **Altyapı alımı durumu kendi içinde çelişiyor** — 21 Nis 2027 22:15, `fm youth` rc=0: "Altyapı alımı 2 Nisan 2028 (347 gün sonra)" ve "Alım adayları GELDİ — liste: youth candidates"; aynı dakikada `fm youth candidates` rc=0: "Alım adayları okunamadı (henüz gelmemiş olabilir: youth)." 3 Nis 14:45'te de tarih geçmişte olduğu hâlde `fm youth` "2 Nisan 2027 (-1 gün sonra)" yazıyordu.
