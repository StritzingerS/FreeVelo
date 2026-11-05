using System.Collections.Generic;

namespace FreeVelo.App
{
    /// <summary>Tiny German/English string table.</summary>
    public static class L
    {
        private static readonly Dictionary<string, (string En, string De)> Texts = new Dictionary<string, (string, string)>
        {
            ["devices"] = ("Devices", "Geräte"),
            ["free_ride"] = ("Free ride", "Freie Fahrt"),
            ["workouts"] = ("Workouts", "Workouts"),
            ["ftp_tests"] = ("FTP tests", "FTP-Tests"),
            ["settings"] = ("Settings", "Einstellungen"),
            ["back"] = ("Back", "Zurück"),
            ["scan"] = ("Scan", "Suchen"),
            ["stop_scan"] = ("Stop", "Stopp"),
            ["connect"] = ("Connect", "Verbinden"),
            ["disconnect"] = ("Disconnect", "Trennen"),
            ["no_devices"] = ("No devices found yet – tap Scan.", "Noch keine Geräte gefunden – tippe auf Suchen."),
            ["trainer"] = ("Trainer", "Rollentrainer"),
            ["power_meter"] = ("Power meter", "Powermeter"),
            ["heart_rate"] = ("Heart rate", "Herzfrequenz"),
            ["none"] = ("none", "keiner"),
            ["power"] = ("Power", "Leistung"),
            ["cadence"] = ("Cadence", "Trittfrequenz"),
            ["speed"] = ("Speed", "Tempo"),
            ["distance"] = ("Distance", "Distanz"),
            ["time"] = ("Time", "Zeit"),
            ["grade"] = ("Grade", "Steigung"),
            ["target"] = ("Target", "Ziel"),
            ["next"] = ("Next", "Danach"),
            ["free"] = ("Free ride", "Frei fahren"),
            ["pause"] = ("Pause", "Pause"),
            ["resume"] = ("Resume", "Weiter"),
            ["skip"] = ("Skip", "Überspringen"),
            ["end_ride"] = ("End ride", "Fahrt beenden"),
            ["paused_pedal"] = ("Paused – start pedaling", "Pausiert – los treten"),
            ["summary"] = ("Summary", "Zusammenfassung"),
            ["avg_power"] = ("Avg power", "Ø Leistung"),
            ["np"] = ("Normalized power", "Normalized Power"),
            ["avg_hr"] = ("Avg heart rate", "Ø Herzfrequenz"),
            ["new_ftp"] = ("Estimated FTP", "Geschätzte FTP"),
            ["apply_ftp"] = ("Apply new FTP", "Neue FTP übernehmen"),
            ["ftp"] = ("FTP", "FTP"),
            ["weight"] = ("Weight", "Gewicht"),
            ["difficulty"] = ("Trainer difficulty", "Trainer-Schwierigkeit"),
            ["power_source"] = ("Power source", "Leistungsquelle"),
            ["language"] = ("Language", "Sprache"),
            ["sim_power"] = ("Simulated rider power (editor)", "Simulierte Fahrerleistung (Editor)"),
            ["low_cadence"] = ("Low cadence – ERG reduced", "Niedrige Trittfrequenz – ERG reduziert"),
            ["min"] = ("min", "Min."),
            ["intensity"] = ("Intensity", "Intensität"),
            ["finished"] = ("Workout finished!", "Workout geschafft!"),
            ["share"] = ("Share FIT file", "FIT-Datei teilen"),
            ["save_downloads"] = ("Save to Downloads", "In Downloads speichern"),
            ["file_missing"] = ("File not found", "Datei nicht gefunden"),
            ["save_failed"] = ("Saving failed", "Speichern fehlgeschlagen"),
            ["plugin_outdated"] = ("Android plugin outdated – in Unity: FreeVelo → Update Android plugins, then build again", "Android-Plugin veraltet – in Unity: FreeVelo → Update Android plugins, dann neu bauen"),
            ["no_trainer"] = ("No trainer connected – connect one under Devices.", "Kein Rollentrainer verbunden – unter Geräte verbinden."),
        };

        public static string Get(string key)
        {
            if (!Texts.TryGetValue(key, out var t)) return key;
            return AppSettings.Language == "de" ? t.De : t.En;
        }
    }
}
