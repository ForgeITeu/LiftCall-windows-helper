# LiftCall Windows Helper

Helper dla Windows x64, publikowany jako self-contained single-file WinExe.
Nie wymaga Pythona, .NET Runtime ani PowerShella podczas zwykłego uruchomienia.
Po dwukliku działa w trayu i uruchamia lokalny serwer HTTP.

Źródła helpera są publiczną częścią projektu i znajdują się w katalogu `Helper/`.
Nie używamy płatnego podpisywania Authenticode. Build tworzy jawnie
self-contained single-file `win-x64` EXE.

## Windows SmartScreen

Ponieważ EXE nie jest podpisany certyfikatem Authenticode, Windows może pokazać
ostrzeżenie SmartScreen, na przykład „Windows protected your PC” albo
„Unknown publisher”. Jest to oczekiwane dla niepodpisanego, open-source'owego
artefaktu i nie oznacza zmiany API helpera.

Przed uruchomieniem sprawdź SHA-256 pobranego pliku względem `SHA256SUMS.txt`
z tego samego release oraz zweryfikuj wersję i commit w `release-manifest.json`.
Jeśli źródło i hash są zaufane, wybierz w SmartScreen `More info`, a następnie
`Run anyway`. Nie wyłączaj SmartScreen systemowo i nie uruchamiaj pliku tylko
dlatego, że ostrzeżenie się pojawiło.

## API

Serwer nasłuchuje wyłącznie na `127.0.0.1:32145`:

- `GET /health`
- `POST /show` bez body
- `POST /heartbeat` bez body
- `POST /heartbeat/stop` bez body

`/health` zwraca kontrakt:

```json
{
  "service": "liftcall-windows-helper",
  "version": "1.0.0",
  "platform": "windows",
  "status": "ok",
  "page_heartbeat_seen": true,
  "page_heartbeat_age_seconds": 5.0,
  "page_status": "healthy",
  "authenticated": true,
  "pwa_process_running": true,
  "window_found": true
}
```

Sukces oznacza HTTP 200 i znalezione okno LiftCall. Jeśli okna nie znaleziono,
helper zwraca HTTP 503 z tym samym schematem i `status: "error"`. Starsi
klienci mogą nadal używać wyłącznie pola `status`.

```json
{"status":"error"}
```

`POST /show` zwraca `{"status":"ok"}` tylko po potwierdzeniu, że okno jest
widoczne, niezminimalizowane i aktywne. W przeciwnym razie zwraca HTTP 503
oraz `{"status":"error"}`.

Odpowiedź `POST /heartbeat` pozostaje kompatybilna z dotychczasową wersją:

```json
{"ok":true,"ttl":60}
```

Żaden endpoint nie przyjmuje ani nie wykorzystuje numerów telefonów, ID
połączeń ani danych biznesowych.

Heartbeat może zawierać wyłącznie techniczny nagłówek
`X-LiftCall-Authenticated: true|false`. Helper zapisuje tylko boolean stanu
zalogowania; nie otrzymuje identyfikatora ani danych użytkownika.

`POST /heartbeat/stop` jest jednorazowym, technicznym zgłoszeniem zamknięcia PWA
i zwraca `{"ok":true,"status":"closed"}`. Nie przyjmuje body ani danych
biznesowych. PWA wysyła je przez `sendBeacon` przy `pagehide`, a `beforeunload`
jest awaryjnym zabezpieczeniem. TTL 60 sekund nadal pozostaje fallbackiem dla
awarii przeglądarki, komputera lub procesu.

Przykład `pwa-heartbeat.example.js` wysyła heartbeat natychmiast po uruchomieniu
PWA, co 20 sekund oraz po powrocie karty do widoczności. Timer jest zatrzymywany
przy `unload`, a błąd helpera jest tylko logowany i nie blokuje aplikacji.

## Konfiguracja

Plik `config.json` obok EXE jest opcjonalny i może nadpisywać wartości domyślne:

```json
{
  "host": "127.0.0.1",
  "port": 32145,
  "window_title_contains": "LiftCall",
  "window_class": "",
  "process_name": "",
  "maximize": true,
  "launch_pwa_on_start": true,
  "close_pwa_on_exit": true,
  "pwa_shortcut_path": "",
  "allowed_origins": [
    "https://liftcall.forge-it.eu"
  ]
}
```

Origin PWA jest porównywany dokładnie z whitelistą. Dla dozwolonego originu
helper dodaje CORS do `/health`, `/show`, `/heartbeat`, `/heartbeat/stop` i `OPTIONS`:

- `Access-Control-Allow-Origin`;
- `Access-Control-Allow-Methods: GET, POST, OPTIONS` dla preflightu;
- `Access-Control-Allow-Headers: Content-Type, X-LiftCall-Authenticated` dla preflightu;
- `Vary: Origin`.

Nie jest używane `Access-Control-Allow-Origin: *`.

## Uruchomienie

Rozpakuj cały folder i uruchom dwuklikiem:

```text
LiftCall.WindowsHelper.exe
```

Helper ukrywa się w trayu. Menu zawiera:

- `Status: połączony / PWA wymaga uwagi / błąd helpera` — wyszarzona etykieta stanu;
- `Konfiguruj` — otwiera formularz konfiguracji wszystkich ustawień helpera;
- `Pokaż LiftCall` — wykonuje `POST /show`;
- `Zamknij` — zatrzymuje serwer i kończy proces.

Helper nie tworzy dodatkowego okna statusowego ani modala. Status tray jest
aktualizowany automatycznie na podstawie działania serwera, procesu PWA,
znalezionego okna, świeżego heartbeat oraz technicznego pola `authenticated`.
Symfony przekazuje to pole jawnie z `IS_AUTHENTICATED_FULLY`; brak wartości
albo `false` nie daje zielonego statusu.
`Pokaż LiftCall` i `POST /show` używają tej samej funkcji aktywacji okna i
zwracają sukces dopiero po sprawdzeniu widoczności, foreground oraz maksymalizacji.
Brak heartbeat, okna, odpowiedzi PWA albo potwierdzenia logowania daje stan
`PWA wymaga uwagi`; błąd serwera, konfiguracji lub monitorowania PWA daje stan
`błąd helpera`.

### Tryb diagnostyczny

Zwykły EXE pozostaje aplikacją WinExe bez okna konsoli. Do diagnostyki uruchom
go z argumentem `--debug`; helper przydzieli własną konsolę i wypisze wyłącznie
techniczne dane heartbeatów, `/health` oraz zmiany stanu:

```powershell
.\LiftCall.WindowsHelper.exe --debug --config .\config.json
```

Można też uruchomić dołączony skrypt:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\debug-helper.ps1
```

Brak nagłówka `X-LiftCall-Authenticated` jest raportowany jako `null` i nigdy
nie daje zielonego statusu. Wartość `authenticated` pochodzi z Symfony i jest
odświeżana także przy nawigacji PWA bez pełnego przeładowania strony.

Formularz `Konfiguruj` pozwala ustawić port, selektory okna, maksymalizację,
uruchamianie i zamykanie PWA, skrót `.lnk` oraz listę dozwolonych Originów.
Skrót PWA można wskazać przyciskiem `Wybierz...`; nie trzeba ręcznie wpisywać
ścieżki ani pamiętać o podwójnych ukośnikach w JSON.

Jeżeli `config.json` znajduje się obok EXE, helper używa tego pliku. Gdy go nie ma,
konfiguracja zapisana z formularza trafia do `%LOCALAPPDATA%\LiftCall\Helper\config.json`,
co pozwala konfigurować helper uruchomiony z `C:\Program Files` bez uprawnień administratora.

Przy pierwszym uruchomieniu brak pliku `config.json` nie jest błędem helpera.
Helper pozostaje w trayu ze statusem `Konfiguracja wymagana`, nie uruchamia PWA,
nie włącza monitorowania ani alertów i od razu otwiera formularz konfiguracji.
Po anulowaniu formularza można otworzyć go ponownie z menu `Konfiguruj`. Taki sam
przepływ obowiązuje dla niepoprawnego albo nieczytelnego pliku JSON; formularz
pokazuje wtedy komunikat walidacyjny, a zapis poprawnej konfiguracji uruchamia
serwer, monitoring i opcjonalny autostart PWA.

## Test API

```powershell
Invoke-WebRequest http://127.0.0.1:32145/health `
  -Headers @{ Origin = 'https://liftcall.forge-it.eu' }

Invoke-WebRequest -Method Post http://127.0.0.1:32145/show `
  -Headers @{ Origin = 'https://liftcall.forge-it.eu' }

Invoke-WebRequest -Method Post http://127.0.0.1:32145/heartbeat `
  -Headers @{ Origin = 'https://liftcall.forge-it.eu' }

Invoke-WebRequest -Method Post http://127.0.0.1:32145/heartbeat/stop `
  -Headers @{ Origin = 'https://liftcall.forge-it.eu' }
```

## Testy automatyczne

Test kontraktu HTTP, CORS i minimalnego JSON:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\test-helper.ps1
```

Test rzeczywistej aktywacji okna:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\test-show.ps1
```

`test-show.ps1` zatrzymuje starą instancję, uruchamia EXE z bieżącego folderu,
znajduje okno według `config.json`, minimalizuje je, wykonuje `POST /show` i
sprawdza widoczność, brak minimalizacji oraz foreground. Przy błędzie pokazuje
ostatnie wpisy z `helper.log`.

## Autostart

Autostart przez `shell:startup`, bez uprawnień administratora:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install-startup.ps1
```

Usunięcie autostartu:

```powershell
.\uninstall-startup.ps1
```

Opcjonalny Harmonogram zadań:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install-task-scheduler.ps1
```

Zadanie działa przy logowaniu aktualnego użytkownika, bez okna konsoli,
z ograniczonym poziomem uprawnień i tylko jedną instancją procesu.

## Zatrzymanie

Wybierz `Zamknij` w menu tray. Autostart nie jest wtedy
usuwany. Awaryjnie:

```powershell
Stop-Process -Name LiftCall.WindowsHelper -ErrorAction SilentlyContinue
```

Log znajduje się obok EXE jako `helper.log`. Każda próba aktywacji zapisuje
HWND, tytuł, klasę, proces i wynik weryfikacji.

## Budowanie release

Na komputerze z .NET SDK uruchom:

```powershell
.\build-release.ps1
```

Skrypt wykonuje produkcyjny publish bez płatnego podpisywania kodu:

```powershell
dotnet publish .\LiftCall.WindowsHelper.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  --output .\release
```

Release zawiera również:

```text
SHA256SUMS.txt
release-manifest.json
```

Manifest zapisuje wersję, commit źródłowy, czas budowania, runtime, SHA-256
artefaktu i instrukcję publish.

W folderze `release` powstanie self-contained single-file:

```text
LiftCall.WindowsHelper.exe
config.json
install-startup.ps1
uninstall-startup.ps1
install-task-scheduler.ps1
test-helper.ps1
test-show.ps1
README.md
SHA256SUMS.txt
release-manifest.json
```
