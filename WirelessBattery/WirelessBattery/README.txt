WirelessBattery - kablosuz kulaklik / mouse pil gostergesi
============================================================

NE YAPAR
- Windows gorev cubugunda, saat/tepsi (^) alaninin hemen soluna kucuk bir gosterge koyar:
      [kulaklik simgesi] 85%   [mouse simgesi] 92%   (sarj olurken yanina simsek isareti)
  Pil okunamayan cihazda yuzde yerine "--" gorunur. %15 ve altinda renk kirmiziya doner.
  Uyari/bildirim YOKTUR.
- Gostergeye TIKLAYINCA detay penceresi acilir ve CANLI guncellenir:
    * Model adi, baglanti turu (Bluetooth / 2.4 GHz alici / USB), durum, sarj
    * Kulaklik: ses cikisi ve mikrofon icin kHz / bit / kanal (Windows'un o an kullandigi format)
    * "Ses aygitlari" karti: bilgisayardaki TUM aktif ses aygitlari ve kHz'leri (kulakligin hangisi
      oldugundan bagimsiz gorursun)
    * Mouse: CANLI rapor hizi (Hz) - fareyi hareket ettirince olculur (ornegin ~1000 Hz, ~8000 Hz)
    * Bluetooth adresi, VID:PID, verinin nereden okundugu
- Gosterge ustune gelince aydinlanir, basili tutunca cokup koyulasir (bastigin belli olur).
  Panel asagidan yukari dogru animasyonla acilir.
- Panel ekrana sigmazsa fare tekerlegi ile (veya ok / PageUp / PageDown / Home / End tuslariyla) kayar;
  sagda ince bir kaydirma cubugu gorunur.
- KLAVYE: panelde klavye logosuyla ayri kart olarak gorunur (gorev cubugunda yalnizca mouse/kulaklik pili vardir).
  Kartta: model, VID:PID, baglanti, tus rollover (6KRO/NKRO tahmini), medya tuslari, fare islevi,
  uretici arayuzu (RGB/makro yazilimi) ve CANLI tarama hizi testi (birkac tusa art arda hizlica bas;
  olculen en kisa tus araligindan Hz hesaplanir, hizli bastikca gercege yaklasir).
  GIZLILIK: hangi tusa bastigin OKUNMAZ ve saklanmaz; yalnizca olaylarin zamani olculur ve yalnizca
  panel aciksa calisir. Klavyede pil bilgisi yalnizca Bluetooth ve desteklenen 2.4 GHz alicilarda vardir.
- Panelin sag ustundeki dugme temayi degistirir: Otomatik (Windows'u izler) > Acik > Koyu.
- Sag tik menusu: Ayrintilari goster / Simdi yenile / Windows ile baslat / Tepsi ikonunu goster /
  Gosterge yazi boyutu (Kucuk-Orta-Buyuk) / Panel temasi / Rapor olustur / Programi kaldir / Cikis

YENI (v1.1)
- TARAMA HIZI: Mouse ve klavye icin "USB bildirilen hiz" / "Tarama hizi (USB)" satiri: aygitin USB'de
  bildirdigi GERCEK tarama hizi (hub'dan okunur, aninda, hareket etmeni gerektirmez). Bluetooth aygitlarda yoktur.
  Mouse'ta ayrica canli olcum vardir: artik arayuzden bagimsiz, yuksek oncelikli ayri bir is parcaciginda
  calisir (daha hizli ve olay kacirmaz). Fareyi hizlica daireler cizerek hareket ettir; "En yuksek olcum"
  satiri standart sinifi (500/1000/2000/4000/8000 Hz) gosterir.
- KARTLAR ACILIP KAPANIR: panelde bir kartin basligina (Mouse / Klavye / Ses aygitlari) tikla; kart
  animasyonla kapanir/acilir. Durum hatirlanir.
- DIL: panelin en altindaki "Dil" dugmesi (veya sag tik > Dil): Turkce, English, Deutsch, Francais, Espanol,
  Italiano, Portugues, Nederlands, Polski, Russkiy. Ilk acilista Windows dilini kullanir.

KURULUM GEREKTIRMEZ - HICBIR SEY INDIRME
Windows'un icinde zaten gelen C# derleyicisini (csc.exe) kullaniriz.

CALISTIRMA
1) Zip'e sag tik > "Tumunu ayikla" (Extract all) de. ZIP'IN ICINDEN CALISTIRMA!
   Olusan klasorde build.bat, kaldir.bat, README.txt ve icinde 11 .cs dosyasi olan "src" klasoru olmali.
2) "build.bat" dosyasina cift tikla. Birkac saniye icinde WirelessBattery.exe olusur ve kendiliginden baslar.
   - Kirmizi bir hata yazarsa pencerenin icindekini kopyalayip bana gonder, duzeltirim.
3) Ilk acilista "bilgisayar acilinca otomatik baslasin mi?" diye bir kez sorar. Sonradan degistirmek icin:
   sag tik > "Windows ile baslat". (Klasoru tasirsan/silersen otomatik baslama calismaz; kaldir.bat veya
   "Programi kaldir" kaydi temizler.)

KALDIRMA (silmek istersen)
- Yontem 1: gostergeye sag tik > "Programi kaldir..." > Evet.
- Yontem 2: klasordeki "kaldir.bat" dosyasina cift tikla > E.
Ikisi de: programi kapatir, Windows ile baslama kaydini ve ayarlari siler, program dosyalarini siler.
Baska hicbir yere dokunmaz (klasoru sadece bos kalirsa kaldirir).

KAPSAM / SINIRLAR
- Pil degeri kaynaklari:
    * Bluetooth cihazlar: Windows'un kendi pil bilgisi (Ayarlar > Bluetooth ve cihazlar'da gorunen).
      Windows pil gostermiyorsa bu program da gosteremez.
    * 2.4 GHz alicili mouse'lar: Attack Shark / Ajazz (Rongyuan), WLMouse, LAMZU / Arbiter (373E, 37B0
      ailesi), Razer mouse'lar. Diger markalar (Logitech, SteelSeries...) bu surumde yok.
- Pil sorgulari salt-okunurdur (cihaza ayar yazilmaz). Program internete BAGLANMAZ.
- Mouse Hz olcumu: Windows'a gelen ham fare olaylarini sayar; fare durunca olculemez.
- Kulaklik kHz'i: Windows'un o aygit icin kullandigi ses formatidir. Bluetooth kulaklikta
  mikrofon kullanilirken 16 kHz'e duser, kullanilmazken genelde 44.1/48 kHz olur.

OYUNDAN CIKINCA GOSTERGE KAYBOLUYORSA
- Tam ekran oyun ekran modunu degistirdiginde Windows seffaf pencerenin icerigini silebiliyor. Gosterge
  ekran degisimini algilar ve en gec 3 sn'de kendini yeniden cizer.

GOSTERGE GORUNUP KAYBOLUYORSA
- Gosterge, baska uygulama acilinca gorev cubugunun arkasinda kalmasin diye 150 ms'de bir ve
  "one gelen pencere degisti" olaylarinda en uste alinir. Tam ekran sayilmasi icin on pencerenin
  ekranin tamamini kaplamasi ve Windows'un "tam ekran" demesi gerekir; yanlis alarmlara karsi
  art arda iki kontrol sart.
- Yine de oluyorsa: sag tik > "Rapor olustur" > raporun en ustundeki "Gosterge" satirlarini gonder.

KAYNAK KULLANIMI (tahmini)
- Gosterge her saniye birkac Windows cagrisi yapar, sadece degisince yeniden cizer.
- Bluetooth/ses taramasi 30 sn'de bir, uretici (alici) pil sorgusu 60 sn'de bir; cihaz takilip cikinca
  veya bilgisayar uykudan uyaninca hemen yenilenir. Panel aciksa 2 sn / 10 sn.
- Fare Hz olcumu yalnizca panel acikken calisir. Internet kullanimi yok.
- Gorev Yoneticisi'nde WirelessBattery.exe'ye bakarak kendi bilgisayarinda dogrulayabilirsin.

SORUN GIDERME
- Cihaz gorunmuyor / pil okunmuyorsa: sag tik > "Rapor olustur". Not Defteri'nde acilan metni bana gonder.
  (Komut satiri: WirelessBattery.exe --rapor)
- Gosterge gorev cubugunda gorunmuyorsa: sag tik > "Rapor olustur" de; raporun en ustunde gostergenin
  neden gizli oldugu / nereye yerlestigi yazar (tam ekran uygulama, gizli gorev cubugu vb.).
- Windows SmartScreen / Defender uyarisi: program senin bilgisayarinda derlendigi icin imzasiz gorunebilir.
