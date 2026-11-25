using System;
using System.Collections.Generic;
using System.Globalization;

namespace WirelessBattery
{
    // Arayüz dili. Kaynak metinler Türkçedir; diğer diller için çeviri tablosundan bulunur.
    // Sıra: 0 Türkçe, 1 English, 2 Deutsch, 3 Français, 4 Español, 5 Italiano, 6 Português, 7 Nederlands, 8 Polski, 9 Русский
    static class Loc
    {
        public static readonly string[] Names = { "Türkçe", "English", "Deutsch", "Français", "Español", "Italiano", "Português", "Nederlands", "Polski", "Русский" };
        static readonly string[] Codes = { "tr", "en", "de", "fr", "es", "it", "pt", "nl", "pl", "ru" };

        static int index = -1;
        static readonly Dictionary<string, string[]> table = Build();

        public static int Index
        {
            get
            {
                if (index < 0) Init();
                return index;
            }
        }

        static void Init()
        {
            int saved = Util.GetInt("Lang", -1);
            if (saved >= 0 && saved < Names.Length) { index = saved; return; }
            string two = "en";
            try { two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant(); } catch (Exception) { }
            index = 1;
            for (int i = 0; i < Codes.Length; i++)
                if (Codes[i] == two) index = i;
        }

        public static void Set(int i)
        {
            if (i < 0 || i >= Names.Length) return;
            index = i;
            Util.SetInt("Lang", i);
        }

        // Türkçe kaynak metni geçerli dile çevirir; çeviri yoksa metni aynen döndürür.
        public static string T(string tr)
        {
            if (string.IsNullOrEmpty(tr)) return tr;
            int i = Index;
            if (i == 0) return tr;
            string[] row;
            if (table.TryGetValue(tr, out row) && i - 1 < row.Length && !string.IsNullOrEmpty(row[i - 1])) return row[i - 1];
            return tr;
        }

        public static string F(string tr, params object[] args)
        {
            return string.Format(T(tr), args);
        }

        static Dictionary<string, string[]> Build()
        {
            Dictionary<string, string[]> d = new Dictionary<string, string[]>();
            foreach (string[] row in Rows())
            {
                string[] rest = new string[row.Length - 1];
                Array.Copy(row, 1, rest, 0, rest.Length);
                d[row[0]] = rest;
            }
            return d;
        }

        static string[] R(params string[] row) { return row; }

        static string[][] Rows()
        {
            return new string[][] {
            R("Kablosuz cihazlar", "Wireless devices", "Kabellose Geräte", "Appareils sans fil", "Dispositivos inalámbricos", "Dispositivi wireless", "Dispositivos sem fios", "Draadloze apparaten", "Urządzenia bezprzewodowe", "Беспроводные устройства"),
            R("Otomatik", "Auto", "Automatisch", "Auto", "Automático", "Automatico", "Automático", "Automatisch", "Automatyczny", "Авто"),
            R("Açık", "Light", "Hell", "Clair", "Claro", "Chiaro", "Claro", "Licht", "Jasny", "Светлая"),
            R("Koyu", "Dark", "Dunkel", "Sombre", "Oscuro", "Scuro", "Escuro", "Donker", "Ciemny", "Тёмная"),
            R("Pil bilgisi veren cihaz bulunamadı.\nCihazın Bluetooth ile bağlı olduğundan emin ol. 2.4 GHz alıcılı cihazlar yalnızca desteklenen markalarda görünür.", "No device reporting battery info was found.\nMake sure the device is connected via Bluetooth. 2.4 GHz receiver devices only appear for supported brands.", "Kein Gerät mit Akkuinformation gefunden.\nStelle sicher, dass das Gerät per Bluetooth verbunden ist. Geräte mit 2,4-GHz-Empfänger erscheinen nur bei unterstützten Marken.", "Aucun appareil fournissant l'état de la batterie n'a été trouvé.\nVérifie que l'appareil est connecté en Bluetooth. Les appareils à récepteur 2,4 GHz n'apparaissent que pour les marques prises en charge.", "No se encontró ningún dispositivo con información de batería.\nAsegúrate de que el dispositivo esté conectado por Bluetooth. Los dispositivos con receptor de 2,4 GHz solo aparecen en marcas compatibles.", "Nessun dispositivo con informazioni sulla batteria trovato.\nAssicurati che il dispositivo sia connesso via Bluetooth. I dispositivi con ricevitore a 2,4 GHz compaiono solo per i marchi supportati.", "Nenhum dispositivo com informação de bateria foi encontrado.\nVerifica se o dispositivo está ligado por Bluetooth. Os dispositivos com recetor de 2,4 GHz só aparecem em marcas suportadas.", "Geen apparaat met batterij-informatie gevonden.\nControleer of het apparaat via Bluetooth is verbonden. Apparaten met een 2,4 GHz-ontvanger verschijnen alleen bij ondersteunde merken.", "Nie znaleziono urządzenia z informacją o baterii.\nUpewnij się, że urządzenie jest połączone przez Bluetooth. Urządzenia z odbiornikiem 2,4 GHz pojawiają się tylko dla obsługiwanych marek.", "Устройства с данными о батарее не найдены.\nУбедитесь, что устройство подключено по Bluetooth. Устройства с приёмником 2,4 ГГц отображаются только для поддерживаемых брендов."),
            R("Durum", "Status", "Status", "Statut", "Estado", "Stato", "Estado", "Status", "Stan", "Состояние"),
            R("Bağlantı", "Connection", "Verbindung", "Connexion", "Conexión", "Connessione", "Ligação", "Verbinding", "Połączenie", "Подключение"),
            R("Pil", "Battery", "Akku", "Batterie", "Batería", "Batteria", "Bateria", "Batterij", "Bateria", "Батарея"),
            R("Ürün adı", "Product name", "Produktname", "Nom du produit", "Nombre del producto", "Nome prodotto", "Nome do produto", "Productnaam", "Nazwa produktu", "Название продукта"),
            R("Donanım kimliği", "Hardware ID", "Hardware-ID", "ID matériel", "ID de hardware", "ID hardware", "ID de hardware", "Hardware-ID", "Identyfikator sprzętu", "ID оборудования"),
            R("Bluetooth adresi", "Bluetooth address", "Bluetooth-Adresse", "Adresse Bluetooth", "Dirección Bluetooth", "Indirizzo Bluetooth", "Endereço Bluetooth", "Bluetooth-adres", "Adres Bluetooth", "Адрес Bluetooth"),
            R("Ses çıkışı", "Audio output", "Audioausgabe", "Sortie audio", "Salida de audio", "Uscita audio", "Saída de áudio", "Audio-uitvoer", "Wyjście audio", "Аудиовыход"),
            R("Mikrofon", "Microphone", "Mikrofon", "Microphone", "Micrófono", "Microfono", "Microfone", "Microfoon", "Mikrofon", "Микрофон"),
            R("aygıt", "device", "Gerät", "appareil", "dispositivo", "dispositivo", "dispositivo", "apparaat", "urządzenie", "устройство"),
            R("Mod", "Mode", "Modus", "Mode", "Modo", "Modalità", "Modo", "Modus", "Tryb", "Режим"),
            R("Rapor hızı (canlı)", "Report rate (live)", "Berichtsrate (live)", "Fréquence de rapport (direct)", "Tasa de informe (en vivo)", "Frequenza di report (live)", "Taxa de relatório (ao vivo)", "Rapportagesnelheid (live)", "Częstotliwość raportów (na żywo)", "Частота отчётов (live)"),
            R("En yüksek ölçüm", "Highest measured", "Höchster Messwert", "Mesure maximale", "Medición máxima", "Misura massima", "Medição máxima", "Hoogste meting", "Najwyższy pomiar", "Максимум измерения"),
            R("USB bildirilen hız", "Reported by USB", "Von USB gemeldet", "Annoncé par l'USB", "Informado por USB", "Dichiarato dall'USB", "Reportado pelo USB", "Gemeld door USB", "Zgłoszone przez USB", "Заявлено по USB"),
            R("Tarama hızı (USB)", "Polling rate (USB)", "Abfragerate (USB)", "Fréquence d'interrogation (USB)", "Frecuencia de sondeo (USB)", "Frequenza di polling (USB)", "Taxa de polling (USB)", "Polling-snelheid (USB)", "Częstotliwość odpytywania (USB)", "Частота опроса (USB)"),
            R("Tuş testi (canlı)", "Key test (live)", "Tastentest (live)", "Test de touches (direct)", "Prueba de teclas (en vivo)", "Test tasti (live)", "Teste de teclas (ao vivo)", "Toetstest (live)", "Test klawiszy (na żywo)", "Тест клавиш (live)"),
            R("Veri kaynağı", "Source", "Quelle", "Source", "Fuente", "Origine", "Origem", "Bron", "Źródło", "Источник"),
            R("Gizlilik", "Privacy", "Datenschutz", "Confidentialité", "Privacidad", "Privacy", "Privacidade", "Privacy", "Prywatność", "Конфиденциальность"),
            R("Hangi tuşa bastığın okunmaz; yalnızca olayların zamanı ölçülür.", "Which key you press is never read; only the timing of events is measured.", "Welche Taste du drückst, wird nie gelesen; nur der Zeitpunkt der Ereignisse wird gemessen.", "La touche pressée n'est jamais lue ; seule la durée entre les événements est mesurée.", "Nunca se lee qué tecla pulsas; solo se mide el momento de los eventos.", "Non viene mai letto quale tasto premi; viene misurata solo la tempistica degli eventi.", "A tecla premida nunca é lida; apenas o tempo dos eventos é medido.", "Welke toets je indrukt wordt nooit gelezen; alleen de timing van gebeurtenissen wordt gemeten.", "Nie odczytujemy, który klawisz naciskasz; mierzony jest tylko czas zdarzeń.", "Какая клавиша нажата, не считывается; измеряется только время событий."),
            R("Bağlı", "Connected", "Verbunden", "Connecté", "Conectado", "Connesso", "Ligado", "Verbonden", "Połączono", "Подключено"),
            R("Bağlı değil (son bilinen değer)", "Not connected (last known value)", "Nicht verbunden (letzter bekannter Wert)", "Non connecté (dernière valeur connue)", "No conectado (último valor conocido)", "Non connesso (ultimo valore noto)", "Não ligado (último valor conhecido)", "Niet verbonden (laatst bekende waarde)", "Niepołączono (ostatnia znana wartość)", "Не подключено (последнее значение)"),
            R("Uyuyor (son bilinen değer)", "Asleep (last known value)", "Im Ruhezustand (letzter bekannter Wert)", "En veille (dernière valeur connue)", "En reposo (último valor conocido)", "In sospensione (ultimo valore noto)", "Em repouso (último valor conhecido)", "Slaapstand (laatst bekende waarde)", "Uśpione (ostatnia znana wartość)", "Спит (последнее значение)"),
            R("Şarj oluyor", "Charging", "Lädt", "En charge", "Cargando", "In carica", "A carregar", "Laden", "Ładowanie", "Заряжается"),
            R("Uyuyor, henüz değer okunamadı", "Asleep, no value read yet", "Im Ruhezustand, noch kein Wert gelesen", "En veille, aucune valeur lue pour l'instant", "En reposo, aún no se ha leído ningún valor", "In sospensione, nessun valore letto finora", "Em repouso, ainda sem valor lido", "Slaapstand, nog geen waarde gelezen", "Uśpione, nie odczytano jeszcze wartości", "Спит, значение пока не считано"),
            R("Okunamadı (cihaz pil bilgisi vermiyor ya da bu model için sürücü yok)", "Not readable (device reports no battery info, or no driver for this model)", "Nicht lesbar (Gerät meldet keinen Akkustand oder kein Treiber für dieses Modell)", "Illisible (l'appareil ne fournit pas la batterie ou aucun pilote pour ce modèle)", "No se pudo leer (el dispositivo no informa la batería o no hay controlador para este modelo)", "Non leggibile (il dispositivo non fornisce la batteria o manca il driver per questo modello)", "Não foi possível ler (o dispositivo não informa a bateria ou não há driver para este modelo)", "Niet leesbaar (apparaat meldt geen batterij of geen driver voor dit model)", "Nie można odczytać (urządzenie nie podaje baterii lub brak sterownika dla tego modelu)", "Не удалось прочитать (устройство не сообщает заряд или нет драйвера для этой модели)"),
            R("(varsayılan)", "(default)", "(Standard)", "(par défaut)", "(predeterminado)", "(predefinito)", "(predefinido)", "(standaard)", "(domyślne)", "(по умолчанию)"),
            R("okunamadı", "unreadable", "nicht lesbar", "illisible", "no legible", "non leggibile", "ilegível", "onleesbaar", "nieczytelne", "не считано"),
            R("{0} kanal", "{0} channels", "{0} Kanäle", "{0} canaux", "{0} canales", "{0} canali", "{0} canais", "{0} kanalen", "{0} kanały", "{0} каналов"),
            R("Konuşma (Hands-Free) — mikrofon kullanıldığı için ses kalitesi düşük", "Call mode (Hands-Free) — audio quality is lower while the microphone is in use", "Gesprächsmodus (Hands-Free) — niedrigere Klangqualität, solange das Mikrofon genutzt wird", "Mode appel (mains libres) — qualité audio réduite pendant l'utilisation du micro", "Modo llamada (manos libres) — la calidad de audio baja mientras se usa el micrófono", "Modalità chiamata (vivavoce) — qualità audio ridotta mentre il microfono è in uso", "Modo chamada (mãos livres) — qualidade de áudio reduzida enquanto o microfone é usado", "Gespreksmodus (handsfree) — lagere geluidskwaliteit zolang de microfoon wordt gebruikt", "Tryb rozmowy (zestaw głośnomówiący) — niższa jakość dźwięku podczas używania mikrofonu", "Режим звонка (hands-free) — качество звука снижено, пока используется микрофон"),
            R("Yüksek kalite ses", "High-quality audio", "Hochwertiger Klang", "Audio haute qualité", "Audio de alta calidad", "Audio ad alta qualità", "Áudio de alta qualidade", "Hoogwaardig geluid", "Dźwięk wysokiej jakości", "Высокое качество звука"),
            R("Fareyi hızlıca daireler çizerek hareket ettir…", "Move the mouse quickly in circles…", "Bewege die Maus schnell im Kreis …", "Déplace vite la souris en cercles…", "Mueve el ratón rápido en círculos…", "Muovi il mouse velocemente in cerchio…", "Move o rato rapidamente em círculos…", "Beweeg de muis snel in cirkels…", "Szybko poruszaj myszą po okręgach…", "Быстро двигайте мышью по кругу…"),
            R("(durdu)", "(stopped)", "(gestoppt)", "(arrêtée)", "(detenida)", "(ferma)", "(parou)", "(gestopt)", "(zatrzymano)", "(остановлено)"),
            R("{0} Hz  →  ~{1} Hz sınıfı", "{0} Hz  →  ~{1} Hz class", "{0} Hz  →  Klasse ~{1} Hz", "{0} Hz  →  classe ~{1} Hz", "{0} Hz  →  clase ~{1} Hz", "{0} Hz  →  classe ~{1} Hz", "{0} Hz  →  classe ~{1} Hz", "{0} Hz  →  klasse ~{1} Hz", "{0} Hz  →  klasa ~{1} Hz", "{0} Hz  →  класс ~{1} Hz"),
            R("Birkaç tuşa art arda hızlıca bas…", "Quickly press several keys in a row…", "Drücke schnell mehrere Tasten hintereinander …", "Appuie rapidement sur plusieurs touches d'affilée…", "Pulsa varias teclas seguidas rápidamente…", "Premi rapidamente più tasti di seguito…", "Prime várias teclas seguidas rapidamente…", "Druk snel meerdere toetsen achter elkaar in…", "Szybko naciśnij kilka klawiszy pod rząd…", "Быстро нажмите несколько клавиш подряд…"),
            R("{0}   ·   en kısa tuş aralığı {1} ms", "{0}   ·   shortest key interval {1} ms", "{0}   ·   kürzester Tastenabstand {1} ms", "{0}   ·   intervalle minimal {1} ms", "{0}   ·   intervalo mínimo {1} ms", "{0}   ·   intervallo minimo {1} ms", "{0}   ·   intervalo mínimo {1} ms", "{0}   ·   kortste toetsinterval {1} ms", "{0}   ·   najkrótszy odstęp {1} ms", "{0}   ·   минимальный интервал {1} ms"),
            R("düşük hız", "Low speed", "Low-Speed", "Bas débit", "Baja velocidad", "Bassa velocità", "Baixa velocidade", "Lage snelheid", "Niska prędkość", "Низкая скорость"),
            R("tam hız", "Full speed", "Full-Speed", "Pleine vitesse", "Velocidad completa", "Piena velocità", "Velocidade total", "Volle snelheid", "Pełna prędkość", "Полная скорость"),
            R("yüksek hız", "High speed", "High-Speed", "Haut débit", "Alta velocidad", "Alta velocità", "Alta velocidade", "Hoge snelheid", "Wysoka prędkość", "Высокая скорость"),
            R("süper hız", "SuperSpeed", "SuperSpeed", "SuperSpeed", "SuperSpeed", "SuperSpeed", "SuperSpeed", "SuperSpeed", "SuperSpeed", "SuperSpeed"),
            R("Kulaklık", "Headset", "Headset", "Casque", "Auriculares", "Cuffie", "Auscultadores", "Headset", "Słuchawki", "Наушники"),
            R("Mouse", "Mouse", "Maus", "Souris", "Ratón", "Mouse", "Rato", "Muis", "Mysz", "Мышь"),
            R("Klavye", "Keyboard", "Tastatur", "Clavier", "Teclado", "Tastiera", "Teclado", "Toetsenbord", "Klawiatura", "Клавиатура"),
            R("Oyun kolu", "Gamepad", "Gamepad", "Manette", "Mando", "Gamepad", "Comando", "Gamepad", "Gamepad", "Геймпад"),
            R("Cihaz", "Device", "Gerät", "Appareil", "Dispositivo", "Dispositivo", "Dispositivo", "Apparaat", "Urządzenie", "Устройство"),
            R("2.4 GHz alıcı", "2.4 GHz receiver", "2,4-GHz-Empfänger", "Récepteur 2,4 GHz", "Receptor de 2,4 GHz", "Ricevitore 2,4 GHz", "Recetor de 2,4 GHz", "2,4 GHz-ontvanger", "Odbiornik 2,4 GHz", "Приёмник 2,4 ГГц"),
            R("USB kablo", "USB cable", "USB-Kabel", "Câble USB", "Cable USB", "Cavo USB", "Cabo USB", "USB-kabel", "Kabel USB", "USB-кабель"),
            R("USB / 2.4 GHz alıcı", "USB / 2.4 GHz receiver", "USB / 2,4-GHz-Empfänger", "USB / récepteur 2,4 GHz", "USB / receptor 2,4 GHz", "USB / ricevitore 2,4 GHz", "USB / recetor 2,4 GHz", "USB / 2,4 GHz-ontvanger", "USB / odbiornik 2,4 GHz", "USB / приёмник 2,4 ГГц"),
            R("USB / kablolu", "USB / wired", "USB / kabelgebunden", "USB / filaire", "USB / con cable", "USB / cablato", "USB / com fios", "USB / bekabeld", "USB / przewodowe", "USB / проводное"),
            R("Ses aygıtları", "Audio devices", "Audiogeräte", "Périphériques audio", "Dispositivos de audio", "Dispositivi audio", "Dispositivos de áudio", "Audioapparaten", "Urządzenia audio", "Аудиоустройства"),
            R("Windows'un kullandığı ses formatı", "Audio format used by Windows", "Von Windows verwendetes Audioformat", "Format audio utilisé par Windows", "Formato de audio usado por Windows", "Formato audio usato da Windows", "Formato de áudio usado pelo Windows", "Door Windows gebruikt audioformaat", "Format audio używany przez Windows", "Формат звука, используемый Windows"),
            R("Fare girişleri (canlı rapor hızı)", "Mouse inputs (live report rate)", "Mauseingaben (Live-Berichtsrate)", "Entrées souris (fréquence en direct)", "Entradas de ratón (tasa en vivo)", "Ingressi mouse (frequenza live)", "Entradas de rato (taxa ao vivo)", "Muisinvoer (live rapportagesnelheid)", "Wejścia myszy (częstotliwość na żywo)", "Входы мыши (частота в реальном времени)"),
            R("Fare", "Mouse", "Maus", "Souris", "Ratón", "Mouse", "Rato", "Muis", "Mysz", "Мышь"),
            R("Bluetooth ve ses: 2 sn'de bir  ·  alıcı pilleri: 10 sn'de bir", "Bluetooth & audio: every 2 s  ·  receiver batteries: every 10 s", "Bluetooth und Audio: alle 2 s  ·  Empfänger-Akkus: alle 10 s", "Bluetooth et audio : toutes les 2 s  ·  batteries des récepteurs : toutes les 10 s", "Bluetooth y audio: cada 2 s  ·  baterías de receptores: cada 10 s", "Bluetooth e audio: ogni 2 s  ·  batterie dei ricevitori: ogni 10 s", "Bluetooth e áudio: a cada 2 s  ·  baterias dos recetores: a cada 10 s", "Bluetooth en audio: elke 2 s  ·  ontvangerbatterijen: elke 10 s", "Bluetooth i audio: co 2 s  ·  baterie odbiorników: co 10 s", "Bluetooth и звук: каждые 2 с  ·  батареи приёмников: каждые 10 с"),
            R("Hız testleri: fareyi hızlıca hareket ettir / klavyede hızlıca tuşlara bas  ·  ", "Speed tests: move the mouse fast / press keys quickly  ·  ", "Geschwindigkeitstests: Maus schnell bewegen / Tasten schnell drücken  ·  ", "Tests de vitesse : déplace vite la souris / appuie vite sur les touches  ·  ", "Pruebas de velocidad: mueve rápido el ratón / pulsa teclas rápido  ·  ", "Test di velocità: muovi veloce il mouse / premi rapidamente i tasti  ·  ", "Testes de velocidade: move o rato depressa / prime teclas rapidamente  ·  ", "Snelheidstests: beweeg de muis snel / druk snel op toetsen  ·  ", "Testy szybkości: szybko poruszaj myszą / szybko naciskaj klawisze  ·  ", "Тесты скорости: быстро двигайте мышью / быстро нажимайте клавиши  ·  "),
            R("Dil", "Language", "Sprache", "Langue", "Idioma", "Lingua", "Idioma", "Taal", "Język", "Язык"),
            R("Üretici", "Manufacturer", "Hersteller", "Fabricant", "Fabricante", "Produttore", "Fabricante", "Fabrikant", "Producent", "Производитель"),
            R("HID arayüzü sayısı", "Number of HID interfaces", "Anzahl HID-Schnittstellen", "Nombre d'interfaces HID", "Número de interfaces HID", "Numero di interfacce HID", "Número de interfaces HID", "Aantal HID-interfaces", "Liczba interfejsów HID", "Число интерфейсов HID"),
            R("Tuş rollover (tahmin)", "Key rollover (estimate)", "Tasten-Rollover (geschätzt)", "Rollover des touches (estimation)", "Rollover de teclas (estimado)", "Rollover tasti (stima)", "Rollover de teclas (estimado)", "Key rollover (schatting)", "Rollover klawiszy (szacunek)", "Ролловер клавиш (оценка)"),
            R("Medya tuşları", "Media keys", "Mediatasten", "Touches multimédia", "Teclas multimedia", "Tasti multimediali", "Teclas multimédia", "Mediatoetsen", "Klawisze multimedialne", "Мультимедийные клавиши"),
            R("Fare işlevi", "Mouse function", "Mausfunktion", "Fonction souris", "Función de ratón", "Funzione mouse", "Função de rato", "Muisfunctie", "Funkcja myszy", "Функция мыши"),
            R("Üretici arayüzü", "Vendor interface", "Hersteller-Schnittstelle", "Interface constructeur", "Interfaz del fabricante", "Interfaccia del produttore", "Interface do fabricante", "Fabrikant-interface", "Interfejs producenta", "Интерфейс производителя"),
            R("6KRO: aynı anda en çok 6 tuş (standart rapor)", "6KRO: up to 6 keys at once (standard report)", "6KRO: bis zu 6 Tasten gleichzeitig (Standardbericht)", "6KRO : jusqu'à 6 touches simultanées (rapport standard)", "6KRO: hasta 6 teclas a la vez (informe estándar)", "6KRO: fino a 6 tasti contemporaneamente (report standard)", "6KRO: até 6 teclas em simultâneo (relatório padrão)", "6KRO: tot 6 toetsen tegelijk (standaardrapport)", "6KRO: do 6 klawiszy naraz (raport standardowy)", "6KRO: до 6 клавиш одновременно (стандартный отчёт)"),
            R("NKRO olasılığı yüksek: genişletilmiş rapor ({0} bayt)", "Likely NKRO: extended report ({0} bytes)", "Wahrscheinlich NKRO: erweiterter Bericht ({0} Bytes)", "NKRO probable : rapport étendu ({0} octets)", "Probable NKRO: informe ampliado ({0} bytes)", "Probabile NKRO: report esteso ({0} byte)", "Provável NKRO: relatório alargado ({0} bytes)", "Waarschijnlijk NKRO: uitgebreid rapport ({0} bytes)", "Prawdopodobnie NKRO: rozszerzony raport ({0} bajtów)", "Вероятно NKRO: расширенный отчёт ({0} байт)"),
            R("Var (ses, oynat/durdur vb.)", "Yes (volume, play/pause, etc.)", "Ja (Lautstärke, Wiedergabe/Pause usw.)", "Oui (volume, lecture/pause, etc.)", "Sí (volumen, reproducir/pausa, etc.)", "Sì (volume, riproduci/pausa, ecc.)", "Sim (volume, reproduzir/pausa, etc.)", "Ja (volume, afspelen/pauze, enz.)", "Tak (głośność, odtwarzaj/pauza itd.)", "Да (громкость, пуск/пауза и т. д.)"),
            R("Ayrı medya arayüzü yok", "No separate media interface", "Keine separate Medien-Schnittstelle", "Pas d'interface multimédia séparée", "Sin interfaz multimedia independiente", "Nessuna interfaccia multimediale separata", "Sem interface multimédia separada", "Geen aparte media-interface", "Brak osobnego interfejsu multimediów", "Нет отдельного мультимедийного интерфейса"),
            R("Var (imleç/tıklama gönderebilir)", "Yes (can send cursor/click)", "Ja (kann Zeiger/Klick senden)", "Oui (peut envoyer curseur/clic)", "Sí (puede enviar cursor/clic)", "Sì (può inviare cursore/clic)", "Sim (pode enviar cursor/clique)", "Ja (kan cursor/klik sturen)", "Tak (może wysyłać kursor/kliknięcia)", "Да (может отправлять курсор/клики)"),
            R("Var (RGB, makro, ayar yazılımı bu yoldan çalışır)", "Yes (RGB, macro and settings software work through it)", "Ja (RGB-, Makro- und Einstellungssoftware nutzen ihn)", "Oui (RGB, macros et logiciel de réglage l'utilisent)", "Sí (RGB, macros y software de ajustes lo usan)", "Sì (RGB, macro e software di impostazioni lo usano)", "Sim (RGB, macros e software de definições usam-na)", "Ja (RGB-, macro- en instellingssoftware gebruiken hem)", "Tak (RGB, makra i oprogramowanie ustawień korzystają z niego)", "Да (RGB, макросы и ПО настроек работают через него)"),
            R("Ayrıntıları göster", "Show details", "Details anzeigen", "Afficher les détails", "Mostrar detalles", "Mostra dettagli", "Mostrar detalhes", "Details tonen", "Pokaż szczegóły", "Показать подробности"),
            R("Şimdi yenile", "Refresh now", "Jetzt aktualisieren", "Actualiser maintenant", "Actualizar ahora", "Aggiorna ora", "Atualizar agora", "Nu vernieuwen", "Odśwież teraz", "Обновить сейчас"),
            R("Windows ile başlat", "Start with Windows", "Mit Windows starten", "Démarrer avec Windows", "Iniciar con Windows", "Avvia con Windows", "Iniciar com o Windows", "Starten met Windows", "Uruchamiaj z Windows", "Запускать с Windows"),
            R("Tepsi ikonunu göster", "Show tray icon", "Symbol im Infobereich anzeigen", "Afficher l'icône de la zone de notification", "Mostrar icono de la bandeja", "Mostra icona nell'area di notifica", "Mostrar ícone na área de notificação", "Systeemvakpictogram tonen", "Pokaż ikonę w zasobniku", "Показывать значок в трее"),
            R("Gösterge yazı boyutu", "Indicator text size", "Textgröße der Anzeige", "Taille du texte de l'indicateur", "Tamaño del texto del indicador", "Dimensione testo indicatore", "Tamanho do texto do indicador", "Tekstgrootte indicator", "Rozmiar tekstu wskaźnika", "Размер текста индикатора"),
            R("Küçük", "Small", "Klein", "Petit", "Pequeño", "Piccolo", "Pequeno", "Klein", "Mały", "Маленький"),
            R("Orta", "Medium", "Mittel", "Moyen", "Mediano", "Medio", "Médio", "Gemiddeld", "Średni", "Средний"),
            R("Büyük", "Large", "Groß", "Grand", "Grande", "Grande", "Grande", "Groot", "Duży", "Большой"),
            R("Panel teması", "Panel theme", "Panel-Design", "Thème du panneau", "Tema del panel", "Tema del pannello", "Tema do painel", "Paneelthema", "Motyw panelu", "Тема панели"),
            R("Otomatik (Windows'u izle)", "Automatic (follow Windows)", "Automatisch (Windows folgen)", "Automatique (suivre Windows)", "Automático (seguir Windows)", "Automatico (segui Windows)", "Automático (seguir o Windows)", "Automatisch (Windows volgen)", "Automatyczny (zgodnie z Windows)", "Авто (как в Windows)"),
            R("Rapor oluştur (sorun giderme)", "Create report (troubleshooting)", "Bericht erstellen (Fehlersuche)", "Créer un rapport (dépannage)", "Crear informe (solución de problemas)", "Crea rapporto (risoluzione problemi)", "Criar relatório (resolução de problemas)", "Rapport maken (probleemoplossing)", "Utwórz raport (rozwiązywanie problemów)", "Создать отчёт (диагностика)"),
            R("Programı kaldır…", "Uninstall program…", "Programm deinstallieren …", "Désinstaller le programme…", "Desinstalar programa…", "Disinstalla programma…", "Desinstalar programa…", "Programma verwijderen…", "Odinstaluj program…", "Удалить программу…"),
            R("Çıkış", "Exit", "Beenden", "Quitter", "Salir", "Esci", "Sair", "Afsluiten", "Zamknij", "Выход"),
            R("Bilgisayar her açıldığında WirelessBattery kendiliğinden başlasın mı?\n\n(İstediğin zaman gösterge üzerinde sağ tık > \"Windows ile başlat\" ile değiştirebilirsin.)", "Should WirelessBattery start automatically every time the computer starts?\n\n(You can change this anytime: right-click the indicator > \"Start with Windows\".)", "Soll WirelessBattery bei jedem Computerstart automatisch starten?\n\n(Du kannst das jederzeit ändern: Rechtsklick auf die Anzeige > \"Mit Windows starten\".)", "WirelessBattery doit-il démarrer automatiquement à chaque démarrage de l'ordinateur ?\n\n(Tu peux le changer à tout moment : clic droit sur l'indicateur > « Démarrer avec Windows ».)", "¿Quieres que WirelessBattery se inicie automáticamente cada vez que se encienda el equipo?\n\n(Puedes cambiarlo cuando quieras: clic derecho en el indicador > \"Iniciar con Windows\".)", "Vuoi che WirelessBattery si avvii automaticamente a ogni avvio del computer?\n\n(Puoi cambiarlo in qualsiasi momento: clic destro sull'indicatore > \"Avvia con Windows\".)", "Queres que o WirelessBattery inicie automaticamente sempre que o computador arrancar?\n\n(Podes alterar isto a qualquer momento: clique direito no indicador > \"Iniciar com o Windows\".)", "Moet WirelessBattery automatisch starten wanneer de computer opstart?\n\n(Je kunt dit altijd wijzigen: rechtsklik op de indicator > \"Starten met Windows\".)", "Czy WirelessBattery ma uruchamiać się automatycznie przy każdym starcie komputera?\n\n(Możesz to zmienić w dowolnej chwili: prawy przycisk na wskaźniku > \"Uruchamiaj z Windows\".)", "Запускать WirelessBattery автоматически при каждом включении компьютера?\n\n(Это можно изменить в любой момент: правый клик по индикатору > \"Запускать с Windows\".)"),
            R("WirelessBattery kaldırılsın mı?\n\n• Windows ile başlama kaydı silinir\n• Program dosyaları (exe, kaynak kodlar, build.bat) silinir\n\nKlasör: ", "Uninstall WirelessBattery?\n\n• The Windows startup entry will be removed\n• Program files (exe, source code, build.bat) will be deleted\n\nFolder: ", "WirelessBattery deinstallieren?\n\n• Der Windows-Autostarteintrag wird entfernt\n• Programmdateien (exe, Quellcode, build.bat) werden gelöscht\n\nOrdner: ", "Désinstaller WirelessBattery ?\n\n• L'entrée de démarrage Windows sera supprimée\n• Les fichiers du programme (exe, code source, build.bat) seront supprimés\n\nDossier : ", "¿Desinstalar WirelessBattery?\n\n• Se eliminará la entrada de inicio de Windows\n• Se borrarán los archivos del programa (exe, código fuente, build.bat)\n\nCarpeta: ", "Disinstallare WirelessBattery?\n\n• Verrà rimossa la voce di avvio di Windows\n• I file del programma (exe, codice sorgente, build.bat) verranno eliminati\n\nCartella: ", "Desinstalar o WirelessBattery?\n\n• A entrada de arranque do Windows será removida\n• Os ficheiros do programa (exe, código-fonte, build.bat) serão eliminados\n\nPasta: ", "WirelessBattery verwijderen?\n\n• De Windows-opstartvermelding wordt verwijderd\n• Programmabestanden (exe, broncode, build.bat) worden gewist\n\nMap: ", "Odinstalować WirelessBattery?\n\n• Wpis autostartu Windows zostanie usunięty\n• Pliki programu (exe, kod źródłowy, build.bat) zostaną usunięte\n\nFolder: ", "Удалить WirelessBattery?\n\n• Запись автозагрузки Windows будет удалена\n• Файлы программы (exe, исходный код, build.bat) будут удалены\n\nПапка: "),
            R("WirelessBattery'yi kaldır", "Uninstall WirelessBattery", "WirelessBattery deinstallieren", "Désinstaller WirelessBattery", "Desinstalar WirelessBattery", "Disinstalla WirelessBattery", "Desinstalar o WirelessBattery", "WirelessBattery verwijderen", "Odinstaluj WirelessBattery", "Удаление WirelessBattery"),
            R("Kaldırma sırasında hata: ", "Error during uninstall: ", "Fehler bei der Deinstallation: ", "Erreur lors de la désinstallation : ", "Error durante la desinstalación: ", "Errore durante la disinstallazione: ", "Erro durante a desinstalação: ", "Fout tijdens verwijderen: ", "Błąd podczas odinstalowywania: ", "Ошибка при удалении: "),
            R("Kablosuz pil: cihaz yok", "Wireless battery: no device", "Kabelloser Akku: kein Gerät", "Batterie sans fil : aucun appareil", "Batería inalámbrica: sin dispositivo", "Batteria wireless: nessun dispositivo", "Bateria sem fios: sem dispositivo", "Draadloze batterij: geen apparaat", "Bateria bezprzewodowa: brak urządzenia", "Беспроводная батарея: нет устройства"),
            R("Kablosuz pil", "Wireless battery", "Kabelloser Akku", "Batterie sans fil", "Batería inalámbrica", "Batteria wireless", "Bateria sem fios", "Draadloze batterij", "Bateria bezprzewodowa", "Беспроводная батарея"),
            };
        }
    }
}
