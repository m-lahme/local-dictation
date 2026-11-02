namespace LocalDictation
{
    // German texts for Program, Overlay, application menus, domain helpers, storage and power routing.
    static class AppStrings
    {
        public static readonly (string English, string German)[] German =
        {
            // Tray, startup and recovery
            ("Local Dictation is still running", "Local Dictation läuft weiter"),
            ("To quit, open the tray menu and choose Quit.", "Zum Beenden das Tray-Menü öffnen und Beenden wählen."),
            ("The settings could not be read and were reset. The old file with the dictionary is at {0}.", "Die Einstellungen waren nicht lesbar und wurden zurückgesetzt. Die alte Datei mit dem Wörterbuch liegt unter {0}."),
            ("The history could not be read and was cleared. The old file is at {0}.", "Der Verlauf war nicht lesbar und wurde geleert. Die alte Datei liegt unter {0}."),
            ("Damaged file backed up", "Beschädigte Datei gesichert"),
            ("Local Dictation could not start.", "Local Dictation konnte nicht starten."),
            ("The settings were saved by a newer version of the app. Please use that version.", "Die Einstellungen stammen aus einer neueren App-Version. Bitte diese Version verwenden."),
            ("An unexpected error ended the dictation. Details are in diagnostics.log.", "Ein unerwarteter Fehler hat das Diktat beendet. Details stehen in diagnostics.log."),
            ("An unexpected error occurred. Details are in diagnostics.log.", "Ein unerwarteter Fehler ist aufgetreten. Details stehen in diagnostics.log."),
            // Dictation status
            ("The recording is very long and is being processed now.", "Die Aufnahme ist sehr lang und wird jetzt verarbeitet."),
            ("The automatic model switch did not work. The previous model stays active.", "Der automatische Modellwechsel hat nicht geklappt. Das bisherige Modell bleibt aktiv."),
            ("The model could not be started. Try again in Settings.", "Das Modell konnte nicht gestartet werden. Versuche es in den Einstellungen erneut."),
            ("The recording stays in History for one hour and can be transcribed again there.", "Die Aufnahme liegt eine Stunde lang im Verlauf und lässt sich dort erneut transkribieren."),
            ("Ready · Whisper loads when needed", "Bereit · Whisper wird bei Bedarf geladen"),
            ("Recording · model is loading in parallel", "Aufnahme läuft · Modell wird parallel geladen"),
            ("Recording · Escape discards the dictation", "Aufnahme läuft · Escape verwirft das Diktat"),
            ("The recording could not be started. Try again.", "Die Aufnahme konnte nicht gestartet werden. Versuche es erneut."),
            ("The recording could not be processed. Try again.", "Die Aufnahme konnte nicht verarbeitet werden. Versuche es erneut."),
            ("No speech detected.", "Keine Sprache erkannt."),
            ("The recording was interrupted. The text is in History.", "Die Aufnahme wurde unterbrochen. Der Text liegt im Verlauf."),
            ("Text inserted · please review", "Text eingefügt · bitte kurz prüfen"),
            ("Not inserted. The text is on the clipboard; Ctrl+V pastes it.", "Nicht eingefügt. Der Text liegt in der Zwischenablage, Strg+V fügt ihn ein."),
            ("Whisper is currently unavailable{0} Parakeet is taking over; the graphics card will be checked again at the next dictation.", "Whisper ist gerade nicht verfügbar{0} Parakeet übernimmt; beim nächsten Diktat wird die Grafikkarte erneut geprüft."),
            ("The model is currently unavailable{0} Restart it in Settings.", "Das Modell ist gerade nicht verfügbar{0} Starte es in den Einstellungen neu."),
            ("The dictation could not be completed. Try again.", "Das Diktat konnte nicht abgeschlossen werden. Versuche es erneut."),
            ("There is no recently inserted dictation to compare.", "Es gibt kein aktuelles eingefügtes Diktat zum Vergleichen."),
            ("The selected text could not be read in this app.", "Die markierte Auswahl konnte in dieser App nicht gelesen werden."),
            ("Select the corrected text in the same app as the last dictation.", "Markiere den korrigierten Text in derselben App wie das letzte Diktat."),
            ("The recording is no longer available.", "Die Aufnahme ist nicht mehr verfügbar."),
            ("Transcribing again with {0} …", "Wird mit {0} erneut transkribiert …"),
            ("The new transcription is ready.", "Erneute Transkription ist fertig."),
            ("Transcribing again did not work.", "Die erneute Transkription hat nicht geklappt."),
            ("Preparing {0} …", "{0} wird vorbereitet …"),
            ("Ready · {0}", "Bereit · {0}"),
            ("Text copied", "Text kopiert"),
            ("Text inserted and copied", "Text eingefügt und kopiert"),
            ("Text inserted", "Text eingefügt"),
            // Dictionary
            ("At most 1,000 replacement rules are allowed.", "Maximal 1.000 Ersetzungsregeln erlaubt."),
            ("The dictionary produces too much text. The original dictation stays in History.", "Das Wörterbuch erzeugt zu viel Text. Das ursprüngliche Diktat bleibt im Verlauf."),
            // Overlay
            ("Ready", "Bereit"),
            ("Recording", "Aufnahme"),
            ("Loading model", "Modell wird geladen"),
            ("Transcribing", "Transkription"),
            ("Text delivered", "Text ausgegeben"),
            ("Text delivered · please review", "Text ausgegeben · bitte kurz prüfen"),
            ("Error, see History or Settings", "Fehler, siehe Verlauf oder Einstellungen"),
            ("Not inserted · text is on the clipboard", "Nicht eingefügt · Text liegt in der Zwischenablage"),
            ("Not inserted · Ctrl+V", "Nicht eingefügt · Strg+V"),
            // Application menu
            ("History", "Verlauf"),
            ("Dictionary", "Wörterbuch"),
            ("Hide overlay", "Overlay ausblenden"),
            ("Show overlay", "Overlay anzeigen"),
            ("Settings", "Einstellungen"),
            ("Quit", "Beenden"),
            ("Try again with {0}", "Mit {0} neu versuchen"),
        };
    }
}
