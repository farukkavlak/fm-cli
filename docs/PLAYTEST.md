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

---

# Playtest — 2026-10-23 (gece turu · devam 3)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet League One
Oyun tarihi: 21 Nisan – 1 Mayıs 2027 · 2 maç (kayıtta): Huddersfield 3-1 (G), Peterborough 0-2 (M)
Huddersfield `--instant`, Peterborough `--play --no-talk`; maç içi sakatlıkta `fm sub` ile değişiklik yapıldı, maç sonu konuşması `fm talk 6` ile geçildi. `fm save` çağrılmadı.
Not: 1 Mayıs'taki ilk Peterborough maçı 0-1 (G) oynandı; köprü kilidi sonrası otomatik kayıt 25 Nisan 05:45'e döndüğü için maç yeniden oynandı (0-2).

Komut bataryası: 22/23 rc=0 (21 Nisan 22:15); `fm player "Lucas" --json` belirsiz ada rc=1 (beklenen).

## Bulgular

1. **Sezon bittiğinde `fm table --json` tabloyu "kupada tablo olmaz" diye reddediyor** — 1 May 2027 14:00, `fm table --json` rc=1: "Sky Bet League One: tablo açılmadı (kupada tablo olmaz)." Aynı dakikada `fm table` rc=0 46 maçlık tabloyu, `fm table --csv` rc=0 CSV'yi basıyor. Bayrak yüzünden komut yanlış bağlam seçiyor.
2. **Sıradaki maç yokken `fm opposition` kendi oyuncumuzu rakip sanıyor** — 1 May 2027 14:00 (sezon bitti, `status` "Sıradaki maç" satırı yok), `fm opposition` rc=0: "James Collins - - - -" ve "Yardımcının önerisi..." — James Collins Lincoln oyuncusu; rakip listesi boş olmalı ya da "maç yok" demeli. Huddersfield maçı öncesi aynı komut doğru şekilde Huddersfield oyuncularını listeliyordu.
3. **Sakatlık mesajında fizyoterapist seçeneği yok; geçersiz seçenek rc=0 ile yutuluyor** — 1 May 2027 13:33, `fm reply 9` ("Ben House sakatlandı") rc=0: gövdeden sonra "(cevap seçeneği yok)"; `fm reply 9 2` rc=0: aynı metin, "cevaplandı" yok, sakatlık durumu değişmiyor. Görevdeki "sakatlıkta fizyoterapist (2)" akışı bu mesajda çalışmıyor.

## Köprü

1 May 2027 14:00'te `fm reply 9` → `fm health` → `fm buttons` dizisinden sonra `fm status` ve `fm dialog` 120 sn'de yanıt vermedi. `pkill -9 -f "Football Manager 26"`, `fm start </dev/null`, `fm continue` ile dönüldü; otomatik kayıt 25 Nisan 05:45'e döndüğü için 1 Mayıs maçı yeniden oynandı.

---

# Playtest — 2026-10-23 (gece turu · devam 4)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet League One
Oyun tarihi: 6–11 Temmuz 2027 · 4 maç: Young Boys 5-4 (G, hazırlık), Crewe 0-3 (D, hazırlık), Vyškov 3-0 (G, Dostluk Kupası), Kayserispor 0-3 (D, Dostluk Kupası)
Young Boys maçı kaldığı yerden `--play --no-talk`, Crewe ve Vyškov `--instant`, Kayserispor `--play --no-talk`; basın sorularında asistan görevlendirildi, Draper/Thorn teklifleri reddedildi, sakat/cezalı kadro `tactic pick` ile düzeltildi. `fm save` çağrılmadı.
Komut bataryası: 23/23 rc=0, boş çıktı yok (6 Temmuz 21:00).

## Bulgular

1. **Kaldığı yerden devam eden maçta hayalet fikstür/skor** — 6 Tem 2027 21:00, `fm status` rc=0: "Maçtayız. 66:04 Crewe 0-1 Lincoln"; `fm match --play --no-talk` rc=0 aynı maçı "Maç bitti: Crewe 0-1 Lincoln" diye bitirip hemen ardından "Lincoln 5-4 Young Boys (Hazırlık)" raporunu bastı; sonraki `fm status` yine 6 Tem 21:00 / "Sıradaki maç: Crewe" dedi ve Crewe 7 Tem'de yeniden oynanıp 0-3 bitti.
2. **Haber gövdesi okunamıyor** — 6 Tem 2027 21:00, `fm news 1` rc=0: "Sezon Öncesi Raporu: Peterborough ... (haberin metni okunamadı)"; başlık ve kaynak var, gövde yok. Aynı gün `news 2`–`4` tam metin bastı.
3. **Türkçe teklif kartında İngilizce başlık** — 6 Tem 2027 21:05, `fm offers 1 view` rc=0: "Main Details:" ile "Ek ödemeler:", "Bütçeden çıkacak:" aynı kartta karışık; Türkçe arayüzde başlık çevrilmemiş.

---

# Playtest — 2026-10-23 (gece turu · devam 5)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet Championship
Oyun tarihi: 31 Tem – 15 Ağu 2027 · 3 maç: Middlesbrough 2-3 (D), Sheffield United 1-0 (G, Carabao Cup), Ipswich 2-3 (M)
Middlesbrough `--play --no-talk` ile izlendi, diğer ikisi `--instant`; 2 Ağustos'ta kaptan mesajı CLI ile kapanmayınca kayıt yeniden yüklendi. `fm save` çağrılmadı.
Komut bataryası: 23/23 rc=0, boş çıktı yok (31 Temmuz 21:45).

## Bulgular

1. **Türkçe arayüzde kaptan komutları çalışmıyor; yardımcı kaptan seçimi yanlış onaya düşüyor** — 15 Ağu 2027 09:15, `fm captains --captain "Tendayi Darikwa"` rc=1: "Kaptanlar sayfasında 'Captain' listesi yok."; aynı dakikada `fm captains --vice "Tom Hamer"` rc=1: "Yardımcı kaptan bugün zaten değişti…" — oysa `fm captains` "Yardımcı kaptan: Ben House" diyor ve değişiklik 1 Ağustos'ta yapılmıştı. Sayfadaki listeler `Replace as Captain` / `İkinci Kaptanı Seç`; `fm talk` menüsünde "[İkinci Kaptanı Seç]" altındaki Ben House seçilince "Kaptan Değişikliğini Onayla" açılıyor. 2 Ağu 09:00'da kaptan mesajı bu yüzden `fm reply ... 2` ile kapanmadı.
2. **`fm offers` kiralık teklifini görmüyor** — 14 Ağu 2027 09:00, `fm reply "Bristol Rovers, Jefferies için kiralama teklifi yaptı"` rc=0: "Bristol Rovers … kiralamak için teklif yaptı", ekte "Bedel: 22B £ · Süre: Sezon sonu"; aynı dakikada `fm offers` rc=0: "Bekleyen teklif yok."; `fm offers "Dom Jefferies" view` rc=1: "'Dom Jefferies' için bekleyen teklif yok." Mesajda yalnız "Yetkiyi Al" seçeneği var, kabul/red yok.
3. **Türkçe adlarla sayfa açılamıyor; bazı metinler İngilizce kalıyor** — 15 Ağu 2027 09:15, `fm pages` "Portal Mesajlar" / "Kadro As Takım" basıyor; `fm open "Mesajlar" --tab "Portal"` rc=1: "no page named 'Mesajlar' under 'Portal'", `fm open "Messages" --tab "Portal"` rc=0: "opened Portal › Messages". Aynı gün `fm board` rc=0: "Vizyon      The board are pleased with the progress towards our overall vision…" — Türkçe arayüzde tek İngilizce satır.
