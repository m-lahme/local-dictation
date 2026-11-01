# Local Dictation

Native Diktier-App für Windows 11 x64: Aufnahme per Hotkey, lokale Transkription und Einfügen in die aktive Anwendung. Oberfläche und Audioaufnahme sind in C# mit WPF und WASAPI implementiert. Ein separater C#-Worker bindet whisper.cpp und sherpa-onnx ein. Python ist keine Abhängigkeit.

## Bauen und starten

Voraussetzungen zum Entwickeln: Windows 11 x64, PowerShell 7 und .NET SDK 10. Beim ersten Build werden NuGet-Pakete heruntergeladen. Die App benötigt außerdem die Microsoft Visual C++ x64 Runtime.

Aus dem Repository-Hauptverzeichnis:

```powershell
.\build.ps1
.\run.ps1
```

Der Build enthält die .NET-Laufzeit. Alternativ direkt `bin\LocalDictation.exe` starten. Ein ZIP-Release vollständig entpacken und darin `LocalDictation.exe` öffnen. Die Sprachmodelle lädt die App separat herunter; sie sind weder im Repository noch im App-Paket enthalten.

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

Der Modellkatalog enthält feste Quellrevisionen, Größen und SHA-256-Prüfsummen. Downloads werden vor der Aktivierung geprüft. Inaktive Modelle lassen sich über die Einstellungen entfernen.

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

## Repository-Struktur

```text
LocalDictation.slnx      Projektmappe für App, Worker und Tests
src/LocalDictation/      WPF-App, Aufnahme, Hotkeys, Daten und Windows-Integration
src/NativeWorker/        Isolierter Transkriptionsprozess
tests/NativeAudit/       C#-Regressionen und optionale Integrationstests
tests/audio/            Synthetische Sprachclips für manuelle Modelltests
assets/                 App-Icon und Logo-Größen
licenses/               Lizenztexte der Abhängigkeiten
docs/                   Architektur-Recherche, Planung und Entwicklungshinweise
build.ps1               App und Worker nach bin/ veröffentlichen
package.ps1             ZIP, Hashmanifest und SHA-256-Datei erzeugen
setup.ps1               Geprüftes Paket pro Benutzer installieren
configure.ps1           Einstellungen programmgesteuert lesen/ändern
```

Nur die native App wird weitergeführt. Die abgelöste Python-App und UI-Prototypen sind aus dem aktuellen Stand entfernt und bleiben über die Git-Historie zugänglich. `bin/`, `dist/`, `work/` und lokale Abhängigkeitscaches sind nicht versioniert. Die Dokumente unter `docs/planning` und `docs/research` beschreiben frühere Entscheidungen und Recherchen; sie sind keine vollständige Beschreibung des heutigen Funktionsumfangs.

## Prüfen und paketieren

```powershell
# Logiktests ohne Fenster, Mikrofonaufnahme oder simulierte Tastatureingaben
.\tests\run-native-regression.ps1

# Optional: zusätzlich native Fenster öffnen und Renderbilder erzeugen
.\tests\run-native-regression.ps1 -Visual

# Sauberen separaten Build paketieren; laufende Installation bleibt unberührt
.\build.ps1 -Output .\dist\publish
.\package.ps1 -Source .\dist\publish
```

Das ZIP und seine SHA-256-Datei liegen unter `dist/`. Das Hashmanifest liegt im ZIP. Nach dem Entpacken installiert `setup.ps1` das Paket pro Benutzer. Updates behalten die vorherige Installation als Rückfallordner. Modelle und Nutzerdaten liegen außerhalb. Es gibt keinen automatischen Online-Updater und keine öffentliche Codesignatur. Hinweise zu Abhängigkeiten stehen in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Einstellungen können ohne UI gelesen und geändert werden:

```powershell
.\configure.ps1 -Show
.\configure.ps1 -Engine parakeet -Restart
.\configure.ps1 -AutomaticPowerModel $true -Restart
.\bin\LocalDictation.exe --shutdown
```

Ohne `-Restart` verweigert das Skript Änderungen an einer laufenden App. Die Regressionen verwenden eigene Daten unter `work/tests/`. Hinweise zu gezielten Hardwaretests stehen in [tests/README.md](tests/README.md).
