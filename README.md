# Local Dictation

Native Diktier-App für Windows 11 x64: Aufnahme per Hotkey, lokale Transkription und Einfügen in die aktive Anwendung. Oberfläche und Audioaufnahme sind in C# mit WPF und WASAPI implementiert. Ein separater C#-Worker bindet whisper.cpp und sherpa-onnx ein. Die Sprache wird vollständig auf dem eigenen Rechner erkannt; es gibt kein Konto und keine Cloud.

Lizenz: [MIT](LICENSE). Hinweise zu den verwendeten Bibliotheken und Modellen stehen in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Systemvoraussetzungen

- Windows 11 x64. Windows 10 und Windows on ARM sind nicht getestet. Auf ARM läuft die x64-Version per Emulation, dann nur mit Parakeet.
- Parakeet (Standardmodell) läuft auf jeder x64-CPU. Der Download beim ersten Diktat ist rund 640 MiB groß.
- Whisper benötigt eine dedizierte Grafikkarte mit Vulkan-Treiber und die [Microsoft Visual C++ Runtime 2015–2022 (x64)](https://aka.ms/vc14/vc_redist.x64.exe). Der Download ist rund 550 MiB groß. Integrierte Grafikeinheiten werden derzeit nicht verwendet; ohne passende GPU übernimmt Parakeet.
- Windows muss Desktop-Apps den Zugriff auf das Mikrofon erlauben (Einstellungen › Datenschutz und Sicherheit › Mikrofon).

## Installieren

Aus einem Release-ZIP:

1. Vor dem Entpacken die ZIP-Datei freigeben: Rechtsklick › Eigenschaften › „Zulassen“, oder in PowerShell `Unblock-File .\LocalDictation-<Version>-win-x64.zip`.
2. Das ZIP vollständig entpacken und `setup.cmd` starten. Die App wird pro Benutzer nach `%LOCALAPPDATA%\Programs\Local Dictation` installiert und erhält eine Startmenü-Verknüpfung. Administratorrechte sind nicht nötig. Alternativ lässt sich `LocalDictation.exe` direkt aus dem entpackten Ordner starten.
3. Die Programmdateien sind nicht signiert. Windows SmartScreen warnt deshalb beim ersten Start („Weitere Informationen“ › „Trotzdem ausführen“). Ist die intelligente App-Steuerung (Smart App Control) aktiv, blockiert Windows die App; dann hilft nur das Bauen aus dem Quellcode.

Updates installiert man genauso. Die vorherige Installation bleibt als Rückfallordner erhalten; ältere Rückfallordner werden entfernt.

### Deinstallieren

Im entpackten Release-Ordner `setup.cmd -Uninstall` ausführen. Das beendet die App und entfernt Installationsordner, Rückfallordner, Startmenü-Verknüpfung und Autostart-Eintrag. Mit `setup.cmd -Uninstall -RemoveData` werden zusätzlich Einstellungen, Verlauf, Statistik und die heruntergeladenen Modelle unter `%LOCALAPPDATA%\LocalDictation` gelöscht.

## Bedienung

Beim Erststart erscheint das Overlay. Ein erneuter Start öffnet das Hauptfenster derselben Instanz. Ein linker Klick auf das Tray-Icon blendet das Hauptfenster ein oder aus. Ist das Overlay im Leerlauf ausgeblendet, öffnet der normale Erststart stattdessen das Hauptfenster. Windows-Autostart bleibt ohne Hauptfenster.

- Strg + Windows kurz drücken startet ein Diktat; erneut drücken beendet es.
- Gedrückt halten und nach der Halteschwelle loslassen verwendet Push-to-talk.
- Zweimal schnell drücken öffnet die Korrektur des letzten Diktats anhand des markierten, korrigierten Textes.
- Alternativ ist Strg + Umschalt + Leertaste konfigurierbar.
- Escape verwirft Aufnahme, Modellwartezeit oder Transkription. Im Leerlauf bleibt Escape frei.
- Das Fenster schließen lässt die App weiterlaufen. Vollständig beenden über Seitenleiste, Tray oder Overlay-Menü.

## Modelle

Whisper 3 Turbo verwendet whisper.cpp mit Vulkan auf einer dedizierten GPU. Parakeet TDT 0.6B v3 verwendet sherpa-onnx auf der CPU. Aufnahme und Modellvorbereitung laufen unabhängig. Bei der ersten GPU-Nutzung kann der Shader-Aufbau einige Sekunden dauern. Wenn Vulkan oder die GPU ausfällt, kann Parakeet dasselbe Diktat auf der CPU verarbeiten. Whisper auf der CPU ist deaktiviert.

Der optionale automatische Modellwechsel reagiert auf Windows-Energieereignisse: Auf unterstützten ASUS-Systemen bedeutet Leise Parakeet/CPU, Performance oder Turbo Whisper/GPU. Auf anderen Geräten werden eindeutige Energiespar- und Höchstleistungsprofile entsprechend zugeordnet. Ausbalancierte und unbekannte Profile behalten das aktive Modell bei. Wechsel erfolgen nach 15 stabilen Sekunden und erst nach einem laufenden Diktat. Optional wird Whisper nach einer einstellbaren Leerlaufzeit entladen und beim nächsten Diktat wieder geladen.

Der Modellkatalog enthält feste Quellrevisionen, Größen und SHA-256-Prüfsummen. Downloads werden vor der Aktivierung geprüft. Inaktive Modelle lassen sich über die Einstellungen entfernen. Ohne Internetzugang kann man die Dateien aus `src/LocalDictation/model-catalog.json` auch selbst nach `%LOCALAPPDATA%\LocalDictation\native-models\whisper` bzw. `...\native-models\parakeet` kopieren; sie werden beim Start anhand der Prüfsumme erkannt.

## Mikrofone

Unter „Audio und Hotkeys“ steht eine Prioritätsliste aller Mikrofone, die Windows kennt, auch der gerade nicht angeschlossenen. Verwendet wird das oberste verbundene Mikrofon. Die Reihenfolge lässt sich per Drag & Drop oder Alt+Pfeil ändern. Typ und Anschluss werden aus den Windows-Geräteeigenschaften abgeleitet, etwa Laptop, Headset, Webcam, USB oder Bluetooth. Beim ersten Start steht ein zuvor gewähltes Mikrofon oben; die übrigen werden nach Gerätetyp sortiert. Neu angeschlossene Geräte kommen ans Ende.

Die App reagiert auf Windows-Geräteereignisse und wechselt im Leerlauf zum besten verfügbaren Mikrofon, ohne den Aufnahmestrom zu starten. Während eines Diktats wird nicht gewechselt. Fällt das verwendete Mikrofon währenddessen aus, wird das Diktat wie bisher als unterbrochen behandelt.

## Wörterbuch, Verlauf und Statistik

Ersetzungsregeln werden in sichtbarer Reihenfolge angewendet. Sie unterstützen ganze Wörter oder Wortteile, wählbare Groß-/Kleinschreibung und Unicode-Normalisierung. Regeln lassen sich suchen und per Drag & Drop umsortieren. Aus markiertem, korrigiertem Text kann die App neue Regeln vorschlagen; diese werden vor dem Übernehmen geprüft und bearbeitet.

Der Verlauf enthält Diktate der letzten 30 Tage. Aufnahmen stehen eine Stunde lang für eine erneute Transkription mit einem anderen Modell zur Verfügung. Eine optionale zweite Whisper-Transkription prüft auffällige fehlende Großschreibung oder Satzzeichen. Ein orangefarbener Haken weist auf ein prüfenswertes Ergebnis hin; er ist keine verlässliche Aussage darüber, ob der Text korrekt ist.

Optionale Sprachbefehle setzen Stichpunkte, neue Zeilen und Absätze um. Die Statistik zeigt Wörter, Diktierzeit und geschätzte Zeitersparnis. Die Tippgeschwindigkeit lässt sich einstellen oder mit einem Tipptest ermitteln.

## Daten und Textausgabe

Alle Nutzerdaten liegen unter `%LOCALAPPDATA%\LocalDictation`: Einstellungen, Wörterbuch, Verlauf, Nutzungsstatistik, vorübergehend gespeicherte Aufnahmen, begrenzte Diagnoselogs und `native-models`. Alte Text-/TSV-Einstellungen werden übernommen; die Originaldateien bleiben als Migrationssicherung erhalten. Die automatische Bereinigung betrifft die aktiven Daten, nicht diese Sicherungen. Es gibt keine feste Aufnahmedauer; Arbeitsspeicher und Laufzeitgrenzen begrenzen sehr lange Aufnahmen.

„Nur einfügen“ nutzt einmaliges Strg+V, wenn sämtliche Zwischenablageinhalte sicher gesichert und wiederhergestellt werden können. Unterstützt werden unter anderem Text, HTML, RTF, Bilddaten und bekannte Metadaten von Browsern und Code-Editoren. Ein neuer Kopiervorgang hat Vorrang vor der Wiederherstellung. Temporärer Text und Wiederherstellung werden vom Windows-Zwischenablageverlauf und Cloud-Sync ausgeschlossen. Unbekannte oder nicht sicher kopierbare Formate bleiben unangetastet; dann verwendet die App langsamere Unicode-Zeicheneingabe.

„Nur Zwischenablage“ und „Einfügen plus Zwischenablage“ behalten das Diktat ausdrücklich in der Zwischenablage. Höher berechtigte Zielprogramme können Eingaben blockieren. Ein Abruf aus der Zwischenablage bestätigt nicht in jeder Anwendung erfolgreiches Einfügen; der Text bleibt zusätzlich im Verlauf. Es werden keine partiellen Modellergebnisse eingefügt.

Umgebungsvariablen für abweichende Speicherorte:

- `LOCAL_DICTATION_DATA`: Datenordner statt `%LOCALAPPDATA%\LocalDictation`
- `LOCAL_DICTATION_MODELS`: Modellordner statt `<Datenordner>\native-models`

## Datenschutz

- Audio, Transkripte und alle Einstellungen bleiben auf dem Rechner. Es gibt keine Telemetrie, kein Konto und keine Update-Prüfung.
- Die einzige Netzwerkverbindung ist der Modell-Download von huggingface.co. Dabei sieht Hugging Face wie bei jedem Download die IP-Adresse.
- Der Verlauf speichert diktierten Text 30 Tage lang unverschlüsselt in `history.json`. Aufnahmen werden nach einer Stunde gelöscht.
- `diagnostics.log` ist auf etwa 2 MiB begrenzt und enthält keinen diktierten Text, wohl aber Gerätenamen (etwa „AirPods von …“), Fehlermeldungen und lokale Pfade. Vor dem Anhängen an ein öffentliches Issue bitte durchsehen.
- Der globale Tastatur-Hook wertet nur die eingestellten Tastenkombinationen und Escape aus und zeichnet keine Tastatureingaben auf. Für das Einfügen wird die Zwischenablage kurz ersetzt und danach wiederhergestellt.

## Fehlerbehebung

- Diagnoseprotokoll: `%LOCALAPPDATA%\LocalDictation\diagnostics.log`.
- „Whisper ist nicht verfügbar“: Grafikkartentreiber mit Vulkan-Unterstützung und Visual C++ Runtime prüfen. Bis dahin übernimmt Parakeet.
- Kein Ton oder „Mikrofon blockiert“: In den Windows-Einstellungen unter Datenschutz und Sicherheit › Mikrofon den Zugriff für Desktop-Apps erlauben und prüfen, ob ein anderes Programm das Mikrofon exklusiv belegt.
- Text erscheint nicht: Programme, die als Administrator laufen, nehmen keine Eingaben einer normal gestarteten App an. Der Text bleibt im Verlauf und lässt sich von dort kopieren.

## Bauen und starten

Voraussetzungen zum Entwickeln: Windows 11 x64, .NET SDK 10.0.x (ein reines .NET-11-SDK lehnt `global.json` ab) und Windows PowerShell 5.1 oder PowerShell 7. Beim ersten Build werden die NuGet-Pakete nach `.tools/packages` geladen.

Windows blockiert Skripte standardmäßig. Die Befehle unten umgehen das nur für den jeweiligen Aufruf; alternativ einmalig `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` setzen.

Aus dem Repository-Hauptverzeichnis:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\run.ps1
```

Der Build enthält die .NET-Laufzeit und legt App und Worker nach `bin\`. Die App nur aus dieser Ausgabe starten: `dotnet run` oder Visual Studio bauen nur die App ohne den Transkriptions-Worker unter `native\`, sodass keine Transkription möglich ist.

## Repository-Struktur

```text
LocalDictation.slnx      Projektmappe für App, Worker und Tests
src/LocalDictation/      WPF-App, Aufnahme, Hotkeys, Daten und Windows-Integration
src/NativeWorker/        Isolierter Transkriptionsprozess
tests/NativeAudit/       C#-Regressionen und optionale Integrationstests
tests/audio/             Synthetische Sprachclips für manuelle Modelltests
assets/                  App-Icon und Logo-Größen
licenses/                Lizenztexte der Abhängigkeiten
docs/                    Architektur-Recherche, Planung und Entwicklungshinweise
.github/workflows/       CI: Regressionstests und Build auf windows-latest
build.ps1                App und Worker nach bin/ veröffentlichen
package.ps1              Sauberen Build als ZIP mit Hashmanifest und SHA-256-Datei verpacken
setup.ps1, setup.cmd     Geprüftes Paket pro Benutzer installieren oder deinstallieren
configure.ps1            Einstellungen programmgesteuert lesen/ändern
```

`bin/`, `dist/`, `work/` und lokale Abhängigkeitscaches sind nicht versioniert. Die Dokumente unter `docs/planning` und `docs/research` beschreiben frühere Entscheidungen und Recherchen; sie sind keine vollständige Beschreibung des heutigen Funktionsumfangs.

## Prüfen und paketieren

```powershell
# Logiktests ohne Fenster, Mikrofonaufnahme oder simulierte Tastatureingaben
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-native-regression.ps1

# Optional: zusätzlich native Fenster öffnen und Renderbilder erzeugen
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-native-regression.ps1 -Visual

# Release-ZIP aus einem frischen Build erzeugen; die laufende Installation bleibt unberührt
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

Das ZIP und seine SHA-256-Datei liegen unter `dist/`. Das Hashmanifest liegt im ZIP. Updates behalten die vorherige Installation als Rückfallordner. Modelle und Nutzerdaten liegen außerhalb. Es gibt keinen automatischen Online-Updater.

Einstellungen können ohne UI gelesen und geändert werden, etwa in einer mit `powershell -NoProfile -ExecutionPolicy Bypass` gestarteten Sitzung:

```powershell
.\configure.ps1 -Show
.\configure.ps1 -Engine parakeet -Restart
.\configure.ps1 -AutomaticPowerModel $true -Restart
.\bin\LocalDictation.exe --shutdown
```

Ohne `-Restart` verweigert das Skript Änderungen an einer laufenden App. Die Regressionen verwenden eigene Daten unter `work/tests/`. Hinweise zu gezielten Hardwaretests stehen in [tests/README.md](tests/README.md). Sicherheitsprobleme bitte wie in [SECURITY.md](SECURITY.md) beschrieben melden.
