using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DailyTimer.Core;

internal static class PersistenceTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void Run(Action<string, Action> test)
    {
        string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            test("Missing configuration gives fresh empty roster and defaults", () => {
                string notice; var saved = new SettingsStore(Path.Combine(folder, "missing.json")).Load(out notice);
                Check(notice == null && saved.Names.Count == 0 && saved.Settings.PersonalSeconds == 90, "Defaults expected");
            });
            test("Save roundtrip preserves Unicode names duplicates order and settings", () => {
                var store = new SettingsStore(Path.Combine(folder, "nested", "settings.json"));
                var config = new SavedConfiguration {
                    Names = new List<string> { "Саша", "B", "Саша" }, AlwaysOnTop = true,
                    Settings = new MeetingSettings { PersonalSeconds = 30, SharedSeconds = 300, SoundEnabled = false }
                };
                string error = store.Save(config); Check(error == null, "Save succeeds: " + error);
                string notice; var actual = store.Load(out notice);
                Check(notice == null && actual.Names.SequenceEqual(config.Names), "Names preserved");
                Check(actual.AlwaysOnTop && !actual.Settings.SoundEnabled && actual.Settings.SharedSeconds == 300, "Settings preserved");
                config.Names.Reverse(); Check(store.Save(config) == null, "Atomic replacement succeeds");
                Check(store.Load(out notice).Names.SequenceEqual(config.Names), "Replacement persisted");
            });
            test("Legacy rosters get distinct identities and start with empty overtime history", () => {
                string path = Path.Combine(folder, "legacy.json");
                File.WriteAllText(path, "{\"Names\":[\"A\",\"A\",\"B\"]}");
                var store = new SettingsStore(path);
                string notice; var saved = store.Load(out notice);
                Check(notice == null && saved.Names.SequenceEqual(new[] { "A", "A", "B" }), "Keep the original roster");
                Check(saved.SpeakerIds.Count == 3 && saved.SpeakerIds.Distinct().Count() == 3, "Give duplicate names independent IDs");
                Check(saved.OvertimeHistory.Count == 0, "Historical results begin empty");
            });
            test("Overtime totals survive reopening and retain speakers outside the roster", () => {
                var store = new SettingsStore(Path.Combine(folder, "overtime.json"));
                var activeId = Guid.NewGuid();
                var absentId = Guid.NewGuid();
                var config = new SavedConfiguration {
                    Names = new List<string> { "A" },
                    SpeakerIds = new List<Guid> { activeId },
                    OvertimeHistory = new List<OvertimeRecord> {
                        new OvertimeRecord { SpeakerId = activeId, Name = "A", CompletedTurns = 4, OvertimeTurns = 1 },
                        new OvertimeRecord { SpeakerId = absentId, Name = "B", CompletedTurns = 2, OvertimeTurns = 0 }
                    }
                };
                string error = store.Save(config); Check(error == null, "Save succeeds: " + error);
                string notice; var actual = store.Load(out notice);
                Check(notice == null && actual.SpeakerIds.Single() == activeId, "Restore the roster identity");
                Check(actual.OvertimeHistory[0].OvertimeRate == 0.25m, "Restore the totals behind 25%");
                Check(actual.OvertimeHistory[1].SpeakerId == absentId && actual.OvertimeHistory[1].Name == "B",
                    "Keep an absent speaker's history");
            });
            test("Corrupt settings recover with notice without overwriting source", () => {
                string path = Path.Combine(folder, "corrupt.json"); File.WriteAllText(path, "broken JSON {");
                string notice; var actual = new SettingsStore(path).Load(out notice);
                Check(notice != null && actual.Settings.SharedSeconds == 600 && actual.Names.Count == 0, "Recover defaults");
                Check(File.ReadAllText(path) == "broken JSON {", "Source preserved");
            });
            test("Invalid stored duration and empty names recover safely", () => {
                string path = Path.Combine(folder, "invalid.json");
                File.WriteAllText(path, "{\"Settings\":{\"PersonalSeconds\":-1},\"Names\":[\"A\"]}");
                string notice; var actual = new SettingsStore(path).Load(out notice);
                Check(notice != null && actual.Settings.PersonalSeconds == 90, "Invalid duration recovered");
                File.WriteAllText(path, "{\"Names\":[\" \"]}");
                actual = new SettingsStore(path).Load(out notice);
                Check(notice != null && actual.Names.Count == 0, "Empty name recovered");
                File.WriteAllText(path, "null"); actual = new SettingsStore(path).Load(out notice);
                Check(notice != null && actual.Settings != null, "Null recovered");
            });
            test("Wrong JSON value types recover with a notice instead of crashing startup", () => {
                string path = Path.Combine(folder, "wrong-types.json");
                string[] malformed = {
                    "{\"Names\":[],\"AlwaysOnTop\":\"bad\"}",
                    "{\"Names\":[],\"Settings\":{\"PersonalSeconds\":\"bad\"}}",
                    "{\"Names\":[],\"Settings\":{\"SharedSeconds\":{\"value\":12}}}",
                    "{\"Names\":[],\"Settings\":{\"SoundEnabled\":[]}}",
                    "{\"Names\":{\"A\":\"B\"}}"
                };
                foreach (string json in malformed)
                {
                    File.WriteAllText(path, json);
                    string notice; var actual = new SettingsStore(path).Load(out notice);
                    Check(notice != null && actual.Settings.PersonalSeconds == 90 && actual.Names.Count == 0, "Bad types recovered: " + json);
                    Check(File.ReadAllText(path) == json, "Malformed source preserved");
                }
            });
            test("Locked settings read and write return notices instead of throwing", () => {
                string path = Path.Combine(folder, "locked.json");
                File.WriteAllText(path, "{}"); var store = new SettingsStore(path);
                using (var locked = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    string notice; store.Load(out notice); Check(notice != null, "Read notice");
                    Check(store.Save(new SavedConfiguration()) != null, "Write notice");
                }
                Check(!Directory.GetFiles(folder, "*.tmp").Any(), "No abandoned temporary files");
            });
            test("Unwritable destination and invalid configuration do not destroy existing data", () => {
                var store = new SettingsStore(folder);
                Check(store.Save(new SavedConfiguration()) != null, "Directory as destination rejected");
                string path = Path.Combine(folder, "valid.json"); store = new SettingsStore(path);
                string error = store.Save(new SavedConfiguration { Names = new List<string> { "A" } });
                Check(error == null, "First save: " + error);
                Check(store.Save(new SavedConfiguration { Settings = new MeetingSettings { SharedSeconds = 0 } }) != null, "Invalid write refused");
                string notice; Check(store.Load(out notice).Names.Single() == "A", "Original intact");
            });
        }
        finally { Directory.Delete(folder, true); }
    }
}
