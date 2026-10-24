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

---

# Playtest — 2026-10-23 (gece turu · devam 6)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet Championship
Oyun tarihi: 15 Ağustos – 1 Eylül 2027 · 4 maç: Swansea 0-2 (M), Wrexham 1-1 pen. (M), Sheffield United 1-2 (G), Cardiff 1-1 (B)
Swansea, Sheffield United ve Cardiff `--instant`; Wrexham `--play --no-talk` ile izlendi. Basın sorumluluğu Tom Shaw'a devredildi, Okoro'nun sözleşmesi yenilendi, Ring'e gelen üç teklif reddedildi, Elerewe'nin kiralık teklifi kabul edildi. `fm save` çağrılmadı.
Komut bataryası: 23/23 rc=0, boş çıktı yok (15 Ağustos 09:15).

## Bulgular

1. **`fm match --instant` yeni maçta önceki maçın raporunu basıyor** — 1 Eyl 2027 20:00, `fm match --instant` rc=0: "Maç simüle edildi. Sheffield United 1-2 Lincoln … Maçın oyuncusu: Reeco Hackett (8,0)" — 28 Ağustos'taki maçın birebir aynı skoru, golleri ve istatistikleri; aynı dakikada `fm status` 20:30'da hâlâ "Sıradaki maç: Cardiff … (bugün)" diyor ve `fm table` Lincoln'ü 4 maçta bırakıyor. Ancak sonraki `fm next` "yeni: Lincoln ile Cardiff puanları paylaştı" diyor; `fm report` gerçek sonucu "Lincoln 1-1 Cardiff" (Moylan 27', Ashford 60') olarak veriyor. Maç simüle edilmiş ama rapor bir önceki fikstürden gelmiş.
2. **`fm match --play` "Puanlarımız" satırı resmî maç puanlarını yanlış eşliyor** — 25 Ağu 2027 21:45, `fm match --play --no-talk` rc=0: "Puanlarımız: Reeco Hackett 7,2 · Ryley Towler 6,9 · … · George Wickens 6,6 · Jack Moylan 6,4"; aynı maç için `fm player "Reeco Hackett" --matches` 7,40, `fm player "Ryley Towler" --matches` 6,60, `fm player "George Wickens" --matches` 6,90; `fm report` "Maçın oyuncusu: Reeco Hackett (7,4)". Aynı oyuncuya üç ayrı puan.
3. **`fm finances` maaş kategorileri aylık, toplam haftalık** — 1 Eyl 2027 21:45, `fm finances` rc=0: "Haftalık toplam 114.848 £ / 166.824 £ (%68,8)" ama altındaki 9 kategori satırının toplamı 427.719 £; `fm finances --json` Wages.Total=114.848, WagesByStatus toplamı=427.719. "Yıldız Oyuncu (1) 53.499 £" satırı Wickens'in `squad --json` değeriyle aynı (53,5B £ **aylık**), yani satırlar aylık, başlık haftalık.

---

# Playtest — 2026-10-23 (gece turu · devam 7)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet Championship
Oyun tarihi: 1–18 Eylül 2027 · 4 maç: Preston 2-0 (G), Coventry 1-0 (G), Birmingham 0-1 (M), Blackburn 1-2 (M)
Preston ve Blackburn `--instant`; Coventry `--play --no-talk` ile izlendi, 75. dakikada sakatlanan Jefferies yerine `fm sub` ile McKiernan girdi; Birmingham `--play` ile oynandı. Basın toplantıları asistanla geçildi, Elerewe'nin kiralık teklifi reddedildi, transfer dönemi kapatıldı, Hackett ile konuşma `fm talk` ile bitirildi. `fm save` çağrılmadı.
Komut bataryası: 23/23 rc=0, boş çıktı yok (1 Eylül 21:45).

## Bulgular

1. **`fm tactic pick` doğal orta sahayı yedeğe atıp %47/%55'lik oyuncuları ilk 11'e koyuyor** — 18 Eyl 2027 17:00, `fm tactic pick` rc=0: "İlk 11 seçildi: … Gallagher, Bayliss …"; hemen ardından `fm tactic` rc=0: "DMR … Gallagher %47", "DML … Bayliss %55", yedeklerde Varfolomeev. Oysa aynı gün 15:00'te `fm tactic` "DML … Varfolomeev %92" diyordu (8 Eylül'de DMR %100). Otomatik seçim, sağlam ve doğal rol oyuncusunu bench'te bırakıyor.
2. **Maç bittikten sonra "Maç şu an oynanıyor" diyen mesaj** — 18 Eyl 2027 17:00, `fm status` rc=0: "Sıradaki maç: QPR … 9 Ekim Cumartesi (21 gün sonra)"; aynı dakikada `fm reply 1` ("Kadro Seçim Önerisi: Blackburn") rc=0: "Cumartesi günü Blackburn'a karşı oynayacağımız … Maç şu an oynanıyor." Maç 2 saat önce 1-2 bitmişti; mesaj gövdesi maç durumuna göre canlı üretilip bayatlıyor.
3. **`fm offers` liste satırı ile `view` kartı aynı teklif için farklı bedel veriyor** — 1 Eyl 2027 21:45, `fm offers` rc=0: "… 25,5B £ (25,5B £) aylık … 25 maç sonra 625B £"; aynı teklif için `fm offers 1 view` rc=0: "Aylık ücret 9B £ · Maaş katkısı %100 (16.250 £ aylık)", "25 maç · 350B £", "Kalıcı olursa toplam: 725B £". Gelecek bonservis 625B ile 350B, toplam 625B ile 725B; aylık da 25,5B ile 25,25B tutmuyor.

---

# Playtest — 2026-10-23 (gece turu · devam 8)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet Championship
Oyun tarihi: 9–20 Ekim 2027 · 3 maç: QPR 1-3 (M), Watford 1-0 (M), Portsmouth 1-1 (D)
QPR, açık kalan maçtan `fm match --no-talk` ile bitirildi; Watford `fm match --play --no-talk` ile izlendi; Portsmouth `fm match --instant`. Ben House fizyoterapiste bırakıldı, Hackett'in sakatlık mesajı okundu, Hamer'in sakatlık konuşması denendi (konuşma durumu kayboldu), Fairlamb ve Broughton profesyonel sözleşme görüşmeleri yapıldı, Carlisle Hampton & Richmond'a satıldı, Fairlamb Oxford City'ye kiralandı. `fm save` çağrılmadı.
Komut bataryası: 23/23 rc=0, boş çıktı yok (9 Ekim 19:00; sonuç ekranı `fm next` ile geçildikten sonra). İlk denemede 13 komut "Oyun maç sonrası sonuçlar ekranında" diyerek rc=1 verdi; `next` sonrası hepsi geçti.

## Bulgular

1. **Maç saatinde `status` ve `match` maçı görmüyor, bir `next` daha gerekiyor** — 19 Ekim 2027 20:00 (Portsmouth maç saati), `fm status` rc=0: "İlerlemeye hazır: next" / "Sıradaki maç: Portsmouth … (bugün)"; aynı dakikada `fm match --instant` rc=1: "Şu an oynanacak maç yok. Oyun maç gününe gelince `next` haber verir."; ancak `fm next` rc=1: "Maç günü. Oynamak için: match" — saat ilerlemeden (20:00) maç ancak ikinci `next` ile görünür oldu. Watford'da 15:00'te durum doğrudan "Maç günü" diyordu.
2. **Açık konuşma durumu kayboluyor** — 15 Ekim 2027, `fm reply 10 1` rc=0: "Konuşma açıldı"; `fm talk` seçenekleri gösterdi; araya giren `fm inbox` sonrası `fm talk 1` rc=1: "Açık bir konuşma yok." (mesaj hâlâ cevaplanabilir). 16 Ekim'de de `fm reply 13 1` ile açılan Towler konuşması, `fm reply 2 1` açılınca kayboldu; `fm talk` rc=1: "Açık bir konuşma yok.", `fm inbox` 13 hâlâ "cevaplanabilir".
3. **Türkçe oyunda İngilizce kalan metinler** — 9 Ekim 2027, `fm reply 14` rc=0: "2 Leave To Physio (Free)"; 12 Ekim 2027, `fm talk 1` rc=0: "» Finalise Promises" (menüde "Vaatleri Sonlandır"); 16 Ekim 2027, `fm reply 11` (Hackett) rc=0: "Treatment: · Club Doctor". Aynı oyun Türkçe.

---

# Playtest — 2026-10-23 (gece turu · devam 9)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet Championship
Oyun tarihi: 20 Ekim – 7 Kasım 2027 · 4 maç: Derby 0-0 (B), Sunderland 2-1 (G), Norwich 0-1 (M), Leyton Orient 1-3 (M)
Derby `--play --no-talk` ile izlendi; Sunderland, Norwich ve Leyton Orient `--instant`. Kadro önerileri `reply … 1` + `dialog 2` ile uygulandı; Carlisle Hampton & Richmond'a satıldı, Broughton Slough'a kiralandı, Bedford ve Bath City teklifleri reddedildi, Honohan fizyoterapiste bırakıldı, Varfolomeev ile görüşme `talk` ile bitirildi. Basın toplantıları asistan Tom Shaw'a geçmişti. `fm save` çağrılmadı.
Komut bataryası: 23/23 rc=0, boş çıktı yok, metinler Türkçe (JSON anahtarları hariç) (20 Ekim 09:15).

## Bulgular

1. **`fm reply` "Cevaplandı" diyor ama onay penceresi açık kalıyor, sıradaki `fm match` rc=1** — 23 Ekim 2027 15:00, `fm reply "Kadro Seçim Önerisi: Derby" 1` rc=0: "Cevaplandı: … → Tavsiye Edilen Kadroyla Sahaya Çık" ve "Ama oyun mesajı hâlâ cevap bekliyor gösteriyor."; hemen ardından `fm match --play --no-talk` rc=1: "Oyun bir şey soruyor: Tavsiye Edilen Kadroyla Sahaya Çık … Cevaplamak için: dialog". `fm dialog 2` verilmeden maç başlamıyor; aynı şey 30 Ekim (Sunderland) ve 6 Kasım (Leyton Orient) kadro önerilerinde de oldu.
2. **Canlı anlatım gol veriyor, maç 0-0 bitiyor** — 23 Ekim 2027, `fm match --play --no-talk` rc=0: "13:31 GOL Lincoln City!!"; aynı çıktıda "Devre arası: Derby 0-0 Lincoln" ve "Maç bitti: Derby 0-0 Lincoln"; `fm report` rc=0 "Derby 0-0 Lincoln" diyor ve gol listesi boş. Gol iptali/ofsayt açıklaması yok.
3. **Türkçe metinde stadyum adına ikinci kez "Stadı" ekleniyor** — 23 Ekim 2027 16:37, `fm inbox` rc=0: "Pride Park Stadium Stadı'nda taraflar yenişemedi"; 30 Ekim 2027 00:00, `fm inbox` rc=0: "LNER Stadium Stadı'nın büyütülmesi tamamlandı". "Stadium" adının üzerine Türkçe tamlama eki getirilince "Stadium Stadı" oluyor.

---

# Playtest — 2026-10-23 (gece turu · kariyer devam)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet League One → Championship
Oyun tarihi: 11 Mar – 3 Nis 2027 · 3 maç: Bristol Rovers 2-8 (D, izlendi), Port Vale 1-0 (G, instant), Sheffield Wednesday 1-2 (G, instant)
Newcastle (FF Kupası) maçı 89:52'de Moylan'ın kırmızı kartıyla durdu; oturum sunucusu yeniden başladı ve dönüşte oyun 8 Oca 2028'e ilerlemişti (aşağıda tur notu).
`fm save` çağrılmadı; oyunun otomatik kaydı kullanıldı. Basın toplantısı asistanla geçildi, sakatlıklarda fizyoterapist seçildi, Curtis ve Hackett sözleşmeleri yenilendi.
Komut bataryası: 22/23 rc=0; `fm jobs` 45 sn'de bitmedi (rc=142), 90 sn'lik ikinci koşuda 318 ilanla rc=0.

## Bulgular

1. **`fm match --instant` eski maçın sonucunu basıyor, `status` oynanan maçı hâlâ "sıradaki" gösteriyor** — 26 Mar 2027 21:15, `fm match --instant` rc=0: "Maç simüle edildi. / Bristol Rovers 2-8 Lincoln ... Ben House (10,0)" — oysa oynanan maç Port Vale'di; aynı dakikada `fm status` hâlâ "Sıradaki maç: Port Vale · iç saha ... (bugün)" dedi; `fm next` sonrası `fm report` "Lincoln 1-0 Port Vale" gösterdi.
2. **`fm tactic` milli maçta olan oyuncuyu ilk 11'de gösteriyor, uyarı vermiyor** — 26 Mar 2027 20:00, `fm tactic` rc=0: "DML ... Varfolomeev %91"; `fm match --instant` rc=1: "Ivan Varfolomeev ve Ben Waine milli maç görevleri sebebiyle yoklar ve bu maçta kullanılamazlar."; `fm tactic pick` sonrası aynı maç rc=0 oynandı.
3. **Tek seçenekli haber cevabından sonra "Konuşma açıldı" deniyor ama konuşma yok** — 13 Mar 2027 16:45, `fm reply 15 1` rc=0: "Cevaplandı ... → House'nin Performansını Öv / Konuşma açıldı; mesaj konuşma bitince kapanır. Devam için: talk"; hemen ardından `fm talk` rc=1: "Açık bir konuşma yok." Aynı şey 11 Mar 09:15'te `fm reply 16 1` sonrası da oldu.

## Tur notu

- 3 Nis 2027 12:30 Newcastle maçı `--play --no-talk` ile 4-0'a kadar izlendi; 89:52'de Moylan kırmızı kart görünce maç durdu ("Değişiklik için: sub ..."). O sırada oturum sunucusu yeniden başladı; dönüşte tarih 8 Oca 2028, lig Sky Bet Championship, transfer dönemi açıktı. Bu, CLI hatasından çok oturum/oyun durumu sıçraması gibi görünüyor; sonraki tur bu kayıttan devam etmeli.

---

# Playtest — 2026-10-23 (gece turu · devam 10)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet Championship
Oyun tarihi: 17 Temmuz 2027 – 17 Ocak 2028 · 3 maç CLI ile: Lincoln Utd 1-8 (G, hazırlık, `--play --no-talk`), Sheffield Wednesday 0-0 (B, hazırlık, `--instant`), Heracles Almelo 0-1 (M, hazırlık, `--instant`); 4. maç Leeds 2-3 (G, FF Kupası 3. Tur) oturum sıçraması sırasında oyun tarafından oynandı.
Wickens sözleşmesi 3 yıl uzatıldı (53,5B £ aylık); Thorn Swindon'a kiralandı; Okoro ve Carlisle teklifleri reddedildi. `fm save` çağrılmadı.
Komut bataryası: 23/23 rc=0 (31 Tem 21:45). Yeniden başlatma sonrası `news 1` rc=1, `inbox` boş, `finances` 300 sn zaman aşımı.

## Bulgular

1. **Sakatlık kartındaki süre ile ekteki süre çelişiyor** — 13 Tem 2027 13:48, `fm reply 1` rc=0: gövde "5 - 6 gün boyunca sahalardan uzak kalacak", Ek: "Diz dönmesi — 1 - 3 gün içinde dönmesi bekleniyor"; 20 Tem 13:55, `fm reply 12` rc=0: gövde "6 - 7 hafta boyunca sahalardan uzak kalacak", Ek: "Diz burkulması — 4 - 7 hafta içinde dönmesi bekleniyor".
2. **`fm tactic pick` sakat oyuncuyu yedekte bırakıyor, maç uyarıyla duruyor** — 8 Oca 2028 15:00, `fm doctor` rc=0: "Tom Hamer — İç uyluk kasında kopma, 1 gün daha"; `fm tactic pick` rc=0: "İlk 11 seçildi: …"; hemen ardından `fm tactic` rc=0 yedeklerde "Hamer"; `fm status` rc=0: "Dikkat: seçili kadroda sakat var (Hamer); maç onlarla başlamaz."
3. **Var olan oyuncunun sayfası açılmıyor** — 17 Oca 2028, `fm search Aldridge` rc=0: "Oyuncu Orin Aldridge 19 yaş · D (SğSl), KB (Sğ) · ENG · Lincoln"; aynı dakikada `fm player "Orin Aldridge"` rc=1: "Orin Aldridge için oyuncu sayfası açılmadı." (iki denemede de aynı).

## Köprü

Sunucu yeniden başladığında köprü kilitlendi: `fm status` rc=69 "FM çalışmıyor"; `fm start </dev/null` + son kayıt yüklendi (9 Oca 2028). Dönüşte oyun 8 Oca 2028'deydi (önceki tur notundaki sıçrama). Bu turda `fm next` 9 Oca → 17 Oca arasında Watford 1-2 maçını, 17 Oca → 25 Oca arasında da Derby 3-0 maçını `fm match` çağrılmadan oynattı; 8 Oca Leeds 2-3 de aynı şekilde oynanmıştı. Yeniden başlatma sonrası `fm news 1` rc=1 ("oyun menüsüz bir ekranda (CanContinue)"), `fm inbox` boş döndü, `fm finances` 300 sn'de yanıt vermedi.

---

# Playtest — 2026-10-23 (gece turu · devam 4)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet Championship (League One'dan yükseldi)
Oyun tarihi: 1 May 2027 – 23 Oca 2028 · 4 maç: Young Boys 5-4 (Hazırlık, `--play --no-talk`), Leeds 2-3 (FF Kupası, köprü kilidi sırasında), Watford 1-2 (G, `--instant`), Derby 3-0 (G, `--instant`)
`fm save` çağrılmadı; oyunun otomatik kaydı kullanıldı. Basın soruları asistanla, sakatlıklar fizyoterapist (2) ile geçildi; tekliflerde düşük teklifler reddedildi, Thorn Exeter'e kiralandı.

Komut bataryası: 23/23 rc=0, boş çıktı yok (4 Mayıs 2027 09:00).

## Bulgular

1. **`fm match --instant` önceki maçın sonucunu basıyor** — 22 Oca 2028 15:00, `fm match --instant` rc=0: "Maç simüle edildi. Watford 1-2 Lincoln ..."; aynı dakikada `fm report` rc=0: "Lincoln 3-0 Derby". Komut yeni maçın sonucunu değil 15 Oca'daki Watford maçının birebir aynısını yazdı; oysa oyun Derby'yi oynamıştı.
2. **Maç gününde `fm next` maç ekranına giriyor, `--instant` artık çalışmıyor** — 6 Tem 2027 18:00, `fm next 10` "Maç günü. Oynamak için: match" dedikten sonra `fm status` rc=0: "Maçtayız, başlamadı. Crewe – Lincoln"; `fm match --instant` rc=1: "Maç başladı; anında sonuç yalnızca kadro gönderilmeden önce istenebiliyor." Maçı `--play` ile izlemek zorunda kaldım.
3. **Yönetim ekranında İngilizce cümle** — 15 Oca 2028 19:00, `fm board` rc=0: "Vizyon      The board are delighted with the progress towards our overall vision, and also recognise the improvements made to the club's stature." Aynı çıktının diğer satırları Türkçe.

## Köprü

6 Tem 2027 ~18:00'de `fm match --play` çağrısı sunucu yeniden başlamasıyla kesildi; ardından `fm status` rc=69 "FM çalışmıyor" döndü. `pkill -9 -f "Football Manager 26"`, `fm start </dev/null` (kayıt zaten yüklü) ile dönüldü; oyun 9 Oca 2028'e ilerlemişti ve Leeds (FF Kupası) maçı oynanmıştı. Köprü kilitlendi.

---

# Playtest — 2026-10-23 (gece turu · devam 11)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet Championship
Oyun tarihi: 9–23 Ocak 2028 · 3 maç: Leeds 2-3 (G, FF Kupası), Watford 1-2 (G), Derby 3-0 (G)
Leeds kayıt yüklenirken oynanmış geldi; Watford `--play --no-talk`, Derby `--instant` denendi ama iki maçı da oyun `fm match` beklemeden arka planda oynattı. QPR maçı iki kez denendi; sonuç ekranı kilidi ve oturum sıçraması yüzünden kayıt 3 Ekim 2027'ye döndü, maç tamamlanamadı. `fm save` çağrılmadı.
Komut bataryası: 23/23 rc=0, boş çıktı yok (18 Eylül 2027 17:00).

## Bulgular

1. **Kadro Seçim Önerisi 10 oyuncu veriyor, kalecisini sağ bek yerine koyuyor** — 8 Ekim 2027 08:15, `fm reply 12` rc=0: "önerilen kadro" tablosunda 10 satır var (KL + 3 defans + 1 OOS + 2 DOS + 2 kanat + 1 ST); sağ bek yok. Üstelik "KL George Wickens — D (Sğ) — Kondisyonu sebebiyle Jenson Jones'in yerine oynar" diyor; `fm player "George Wickens" --json` onu `Goalkeeper` olarak, `fm player "Jenson Jones"` ise `D (SğSl)` olarak veriyor.
2. **`fm offers`, `--json` ve `view` aynı teklif için üç ayrı cevap veriyor** — 17 Ocak 2028 05:45, `fm status` "bir teklif cevap bekliyor" derken `fm offers` rc=0 "Bekleyen teklif yok." dedi; aynı dakikada `fm offers --json` rc=0 `[{"No":1,"Player":"Salifu Sanneh","Club":"Birmingham","Kind":"Transfer","Status":"Offer Made","Fee":"150B £ (350B £)"}]` döndü ve `fm offers 1 view` rc=1 "'1' için bekleyen teklif yok." dedi. 26 Ocak'ta tersi oldu: `fm offers` #1'de Jimmy Chick'i gösterirken `--json` `[]` döndü; iki çağrı sonra #1 Tom Hamer oldu ve ancak `fm offers "Tom Hamer" view` kartı açtı.
3. **Maç raporu mesajının ilk paragrafı boş geliyor, cümle ortadan başlıyor** — 15 Ocak 2028 16:38, `fm reply 9` (Watford 1-2) rc=0: çıktı boş satırla başlıyor, ardından "Egil Selvik'in hatasında ayağı iyi stoper Deji Elerewe tarafından kısa mesafeden yaptığı kafa vuruşuyla kaydedilen gole engel olamayan Watford..." geliyor; skor ve açılış cümlesi yok. Aynı boş başlangıç 9 Ekim 2027'deki QPR 2-2 raporunda da vardı ("... Lincoln kaptanı Tendayi Darikwa...").

## Köprü

9 Ekim 2027 19:00'da maç sonrası Results ekranı `next` ile geçmedi; `fm buttons` "Son Sonuçlar / continue-button" gösterirken `fm press "Son Sonuçlar"` rc=1 "no control labelled" dedi. `pkill -9 -f "Football Manager 26"` + `fm start </dev/null` + `fm continue` sonrası kayıt 3 Ekim 2027'ye döndü. Aynı gün QPR `--play --no-talk` 83:33'te sakat oyuncu yüzünden durdu; oturum koptuğunda oyun 8 Ocak 2028'e sıçramıştı, `fm start` 9 Ocak 05:45 kaydını yükledi. Köprü kilitlendi.

---

# Playtest — 2026-10-24 (Empoli · 2028/29 sezonu)

Kayıt: `Faruk Kavlak - Empoli` · Empoli FC · Serie BKT
Oyun tarihi: 20 Ağustos – 22 Ekim 2028 · 10 lig maçı: Catanzaro 2-0, Juve Stabia 1-1, Vicenza 4-1,
Südtirol 3-1, Reggiana 2-0, Blu-neri U23 1-1, Salernitana 2-1, Cremonese 3-3, Sampdoria 3-3, Modena 1-1.
**5G 5B 0M · 20 puan · 3. sıra · yenilgisiz.** Sıradaki: Spezia (ev), 29 Ekim.

## Bu turda düzeltilenler

1. **`next`/`press` odakta asılı kalıyordu** — oyun Terminal'in arkasındayken Continue/UI olayları
   işlenmiyor; `fm next 8` iki kez 5 dakika timeout verdi, `status` ise “hazır” dedi. `Launcher.Activate()`
   eklendi; `next`, `press`, `match` başında oyun öne getiriliyor (`e66c149`). `fm next 3` elle
   `osascript` olmadan döndü.
2. **Görevler sekmesindeki teklif mesajları görünmüyordu** — `offers` satırı cevapsız kalınca `next`
   kilitlendi; Frosinone/Ignacchiti mesajı `Görevler`te bulunup reddedildi. `inbox --tasks` ve
   `inbox --all` eklendi (`5760043`, `a05e608`).
3. **Anında sonuç eski maçı basıyordu** — Cagliari maçında Monza raporu geldi; son fikstür anahtarı
   değişene kadar bekleme eklendi (`132c8c9`).

## Notlar

- Yeni sezon kayıt/forma numaraları (`numbers --auto`), primler, kaptanlık, disiplin ve transfer
  dönemi kapanışı çalıştı.
- Sakatlık `reply` seçenekleri bu maçlarda düzenli geldi; fizyoterapist/club doctor seçildi.
- Cagliari'nin Campaniello teklifi hâlâ “Sözleşme” durumunda ve `offers`ta cevapsız; bloklamıyor.
