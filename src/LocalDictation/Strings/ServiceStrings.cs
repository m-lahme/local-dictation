namespace LocalDictation
{
    // German texts for models, audio, microphones, text output, Windows integration, correction learning and voice commands.
    static class ServiceStrings
    {
        public static readonly (string English, string German)[] German =
        {
            // Model downloads
            ("Resuming the download of {0} …", "{0} wird weiter heruntergeladen …"),
            ("Downloading {0} …", "{0} wird heruntergeladen …"),
            ("Downloading {0} · {1}%", "{0} wird heruntergeladen · {1} %"),
            ("Not enough free disk space for {0}: about {1:N0} MB more is needed, {2} has {3:N0} MB free.", "Zu wenig freier Speicherplatz für {0}: Benötigt werden noch etwa {1:N0} MB, auf {2} sind {3:N0} MB frei."),
            ("The download of {0} has stalled. Check the internet connection and try again; parts already downloaded will be reused.", "Der Download von {0} ist ins Stocken geraten. Prüfe die Internetverbindung und versuche es erneut; bereits geladene Teile werden weiterverwendet."),
            ("The proxy server requires a sign-in that cannot happen automatically. {0} could not be downloaded.", "Der Proxy-Server verlangt eine Anmeldung, die nicht automatisch möglich ist. {0} konnte nicht heruntergeladen werden."),
            ("The download server did not deliver {0} (HTTP {1}). Try again later.", "Der Download-Server hat {0} nicht geliefert (HTTP {1}). Versuche es später erneut."),
            ("No connection to the download server. Check the internet connection and try again; parts already downloaded will be reused.", "Keine Verbindung zum Download-Server. Prüfe die Internetverbindung und versuche es erneut; bereits geladene Teile werden weiterverwendet."),
            ("The download of {0} was damaged and has been discarded. Try again.", "Der Download von {0} war beschädigt und wurde verworfen. Versuche es erneut."),
            ("No write access to the model folder {0}.", "Kein Schreibzugriff auf den Modellordner {0}."),
            ("Not enough free disk space for {0}. Free up some space and try again.", "Zu wenig freier Speicherplatz für {0}. Gib Speicherplatz frei und versuche es erneut."),
            ("{0} could not be saved: {1}", "{0} konnte nicht gespeichert werden: {1}"),
            ("{0} could not be downloaded: {1}", "{0} konnte nicht heruntergeladen werden: {1}"),
            ("Model file damaged: {0}. “{1}” downloads it again.", "Modelldatei beschädigt: {0}. „{1}“ lädt sie neu."),
            ("Download / verify", "Herunterladen / prüfen"),
            ("Please wait for the running model download to finish.", "Bitte den laufenden Modelldownload abwarten."),

            // Audio capture
            ("The microphone is not ready yet. Try again in a moment.", "Das Mikrofon ist noch nicht bereit. Versuche es gleich noch einmal."),
            ("No microphone was found. Connect a microphone or enable it in the Windows sound settings.", "Es wurde kein Mikrofon gefunden. Schließe ein Mikrofon an oder aktiviere es in den Windows-Soundeinstellungen."),
            ("The microphone delivers only silence. Check the mute setting, the microphone selection and, in Windows Settings under Privacy & security > Microphone, access for desktop apps.", "Das Mikrofon liefert nur Stille. Prüfe die Stummschaltung, die Mikrofonauswahl und in den Windows-Einstellungen unter Datenschutz und Sicherheit > Mikrofon den Zugriff für Desktop-Apps."),
            ("The microphone delivered no audio data.", "Das Mikrofon hat keine Audiodaten geliefert."),
            ("The connection to the microphone was interrupted.", "Die Verbindung zum Mikrofon wurde unterbrochen."),
            ("The audio device was changed.", "Das Audiogerät wurde geändert."),
            ("Recording interrupted. {0}", "Aufnahme unterbrochen. {0}"),
            ("The microphone is muted in Windows.", "Das Mikrofon ist in Windows stummgeschaltet."),
            ("The microphone could not be started. Check the selection in Settings.", "Das Mikrofon konnte nicht gestartet werden. Prüfe die Auswahl in den Einstellungen."),
            ("Windows is blocking access to the microphone. Allow it for desktop apps in Windows Settings under Privacy & security > Microphone.", "Windows blockiert den Zugriff auf das Mikrofon. Erlaube ihn in den Windows-Einstellungen unter Datenschutz und Sicherheit > Mikrofon für Desktop-Apps."),
            ("Another program is using the microphone exclusively. Close it or choose a different microphone in Settings.", "Ein anderes Programm verwendet das Mikrofon exklusiv. Schließe es oder wähle in den Einstellungen ein anderes Mikrofon."),

            // Microphone kinds and connections
            ("Built into the laptop", "Im Laptop eingebaut"),
            ("Built-in", "Eingebaut"),
            ("Headset", "Headset"),
            ("Webcam", "Webcam"),
            ("External microphone", "Externes Mikrofon"),
            ("USB audio device", "USB-Audiogerät"),
            ("Speakerphone", "Freisprecheinrichtung"),
            ("Line in", "Line-In"),
            ("Virtual device", "Virtuelles Gerät"),
            ("Remote Desktop", "Remotedesktop"),
            ("Audio input", "Audioeingang"),
            ("USB", "USB"),
            ("Bluetooth", "Bluetooth"),
            ("Audio jack", "Klinke"),

            // Text output and model process
            ("The text could not be inserted and stays in History.", "Der Text konnte nicht eingefügt werden und bleibt im Verlauf."),
            ("No text field was active.", "Es war kein Textfeld aktiv."),
            ("Text inserted. The previous clipboard will be restored shortly.", "Text eingefügt. Die bisherige Zwischenablage wird gleich wiederhergestellt."),
            ("Release the held modifier keys and try again. The dictation stays in History.", "Lass die gedrückten Zusatztasten los und versuche es erneut. Das Diktat bleibt im Verlauf."),
            ("The text could not be output and stays in History.", "Der Text konnte nicht ausgegeben werden und bleibt im Verlauf."),
            ("The text could not be inserted completely. The dictation stays in History.", "Der Text konnte nicht vollständig eingefügt werden. Das Diktat bleibt im Verlauf."),
            ("The model quit unexpectedly.", "Das Modell wurde unerwartet beendet."),
            ("The dictionary produces too much text.", "Das Wörterbuch erzeugt zu viel Text."),

            // Correction learning
            ("The comparison needs both the inserted and the corrected text.", "Für den Vergleich werden der eingefügte und der korrigierte Text benötigt."),
            ("The selection is not similar enough to the last dictation. Select the entire corrected text.", "Die Auswahl ähnelt dem letzten Diktat nicht genug. Markiere den gesamten korrigierten Text."),
            ("No suitable dictionary rules found.", "Keine geeigneten Wörterbuchregeln gefunden."),
            ("{0} rule suggested.", "{0} Regel vorgeschlagen."),
            ("{0} rules suggested.", "{0} Regeln vorgeschlagen."),
            ("Capitalization adjusted", "Groß- und Kleinschreibung angepasst"),
            ("Removal suggested · please check", "Entfernung vorgeschlagen · bitte prüfen"),
            ("Word corrected", "Wort korrigiert"),
            ("Split or joined words corrected", "Getrennt- oder Zusammenschreibung korrigiert"),
            ("Several words changed · please check", "Mehrere Wörter geändert · bitte prüfen"),
            ("Spelling or short phrase changed", "Schreibweise oder kurze Wortgruppe geändert"),
            ("Another rule already exists for this expression", "Für diesen Ausdruck gibt es bereits eine andere Regel"),
        };
    }
}
