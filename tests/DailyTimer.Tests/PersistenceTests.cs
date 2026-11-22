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

    private static TeamRoster CreateTeam(string name, params string[] names)
    {
        return new TeamRoster {
            Id = Guid.NewGuid(), Name = name,
            Members = names.Select(member => new RosterMember { Id = Guid.NewGuid(), Name = member }).ToList()
        };
    }

    private static IEnumerable<string> Names(TeamRoster team)
    {
        return team.Members.Select(member => member.Name);
    }

    public static void Run(Action<string, Action> test)
    {
        string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            test("Missing configuration gives fresh empty roster and defaults", () => {
                string notice; var saved = new SettingsStore(Path.Combine(folder, "missing.json")).Load(out notice);
                Check(notice == null && saved.Teams.Single().Members.Count == 0 && saved.Settings.PersonalSeconds == 90, "Defaults expected");
            });
            test("Team roundtrip preserves rosters, Unicode names, duplicate names, order and team selection", () => {
                var store = new SettingsStore(Path.Combine(folder, "nested", "settings.json"));
                var first = CreateTeam("Engineering", "Саша", "B", "Саша");
                var second = CreateTeam("Product", "Саша", "C");
                first.Members[0].PersonalSeconds = 30;
                second.Members[0].PersonalSeconds = 45;
                var config = new SavedConfiguration {
                    Teams = new List<TeamRoster> { first, second }, SelectedTeamId = second.Id, AlwaysOnTop = true,
                    Settings = new MeetingSettings { PersonalSeconds = 30, SharedSeconds = 300, SoundEnabled = false }
                };
                string error = store.Save(config); Check(error == null, "Save succeeds: " + error);
                string notice; var actual = store.Load(out notice);
                Check(notice == null && actual.Teams.Select(team => team.Name).SequenceEqual(new[] { "Engineering", "Product" }), "Teams preserved");
                Check(Names(actual.Teams[0]).SequenceEqual(Names(first)) && Names(actual.Teams[1]).SequenceEqual(Names(second)), "Separate rosters preserved");
                Check(actual.SelectedTeamId == second.Id && actual.Teams[1].Members[0].Id == second.Members[0].Id, "Team selection and identities preserved");
                Check(actual.Teams[0].Members[0].Id != actual.Teams[1].Members[0].Id, "Shared names across teams retain distinct identities");
                Check(actual.Teams[0].Members[0].PersonalSeconds == 30 && actual.Teams[1].Members[0].PersonalSeconds == 45 &&
                    actual.Teams[0].Members[1].PersonalSeconds == null, "Restore overrides by speaker and keep the default when unset");
                Check(actual.AlwaysOnTop && !actual.Settings.SoundEnabled && actual.Settings.SharedSeconds == 300, "Settings preserved");
                first.Members.Reverse();
                error = store.Save(config);
                Check(error == null, "Atomic replacement succeeds: " + error);
                Check(Names(store.Load(out notice).Teams[0]).SequenceEqual(Names(first)), "Replacement persisted");
            });
            test("Legacy rosters get distinct identities and start with empty overtime history", () => {
                string path = Path.Combine(folder, "legacy.json");
                File.WriteAllText(path, "{\"Names\":[\"A\",\"A\",\"B\"]}");
                var store = new SettingsStore(path);
                string notice; var saved = store.Load(out notice);
                var team = saved.Teams.Single();
                Check(notice == null && Names(team).SequenceEqual(new[] { "A", "A", "B" }), "Keep the original roster in one team");
                Check(team.Members.Select(member => member.Id).Distinct().Count() == 3, "Give duplicate names independent IDs");
                Check(saved.SelectedTeamId == team.Id, "The migrated team is selected");
                Check(team.Members.All(member => member.PersonalSeconds == null), "The existing roster uses the meeting default");
                Check(saved.OvertimeHistory.Count == 0, "Historical results begin empty");
            });
            test("Legacy roster fields retain names and identities regardless of field casing", () => {
                string path = Path.Combine(folder, "legacy-field-casing.json");
                var id = Guid.NewGuid();
                File.WriteAllText(path, "{\"names\":[\"A\"],\"speakerids\":[\"" + id + "\"]}");
                string notice; var saved = new SettingsStore(path).Load(out notice);
                var members = saved.Teams.Single().Members;
                Check(notice == null && members.Count == 1 && members[0].Name == "A" && members[0].Id == id,
                    "Migrate the original roster without assigning a replacement identity");
            });
            test("Migration preserves speaker IDs and connects historical turns to the default team", () => {
                string path = Path.Combine(folder, "overtime.json");
                var store = new SettingsStore(path);
                var activeId = Guid.NewGuid();
                var absentId = Guid.NewGuid();
                File.WriteAllText(path, "{\"Names\":[\"A\"],\"SpeakerIds\":[\"" + activeId + "\"],\"OvertimeHistory\":[" +
                    "{\"SpeakerId\":\"" + activeId + "\",\"Name\":\"A\",\"CompletedTurns\":4,\"OvertimeTurns\":1}," +
                    "{\"SpeakerId\":\"" + absentId + "\",\"Name\":\"B\",\"CompletedTurns\":2,\"OvertimeTurns\":0}]}");
                string notice; var migrated = store.Load(out notice);
                Check(notice == null && store.Save(migrated) == null, "Migrate and save legacy settings");
                var actual = store.Load(out notice);
                Check(notice == null && actual.Teams.Single().Members.Single().Id == activeId, "Restore the roster identity");
                Check(actual.OvertimeHistory[0].OvertimeRate == 0.25m, "Restore the totals behind 25%");
                Check(actual.OvertimeHistory[1].SpeakerId == absentId && actual.OvertimeHistory[1].Name == "B",
                    "Keep an absent speaker's history");
                Check(actual.OvertimeHistory.All(record => record.TeamId == actual.Teams[0].Id), "Scope migrated history to the migrated team");
                actual.Teams.Clear();
                Check(store.Save(actual) == null, "Save after removing the migrated team");
                Check(store.Load(out notice).OvertimeHistory.All(record => record.TeamName == "Default team"), "Keep the team's name in its historical turns");
            });
            test("Deleting the last team persists an empty team list", () => {
                var store = new SettingsStore(Path.Combine(folder, "empty-teams.json"));
                var config = new SavedConfiguration();
                config.SelectedTeamId = config.Teams.Single().Id;
                config.Teams.Clear();
                Check(store.Save(config) == null, "Save the deletion");
                string notice; var actual = store.Load(out notice);
                Check(notice == null && actual.Teams.Count == 0 && actual.SelectedTeamId == null, "Keep the empty state");
            });
            test("Invalid speaker overrides cannot replace saved settings", () => {
                var store = new SettingsStore(Path.Combine(folder, "invalid-override.json"));
                var config = new SavedConfiguration { Teams = new List<TeamRoster> { CreateTeam("Product", "A") } };
                var member = config.Teams[0].Members[0];
                member.PersonalSeconds = 30;
                Check(store.Save(config) == null, "Save the valid override");
                member.PersonalSeconds = 0;
                Check(store.Save(config) != null, "Reject zero seconds");
                member.PersonalSeconds = 3601;
                Check(store.Save(config) != null, "Reject an override above the personal limit");
                string notice; var saved = store.Load(out notice);
                Check(notice == null && saved.Teams[0].Members[0].PersonalSeconds == 30, "Keep the previous saved override");
            });
            test("Corrupt settings recover with notice without overwriting source", () => {
                string path = Path.Combine(folder, "corrupt.json"); File.WriteAllText(path, "broken JSON {");
                string notice; var actual = new SettingsStore(path).Load(out notice);
                Check(notice != null && actual.Settings.SharedSeconds == 600 && actual.Teams.Single().Members.Count == 0, "Recover defaults");
                Check(File.ReadAllText(path) == "broken JSON {", "Source preserved");
            });
            test("Invalid stored duration and empty names recover safely", () => {
                string path = Path.Combine(folder, "invalid.json");
                File.WriteAllText(path, "{\"Settings\":{\"PersonalSeconds\":-1},\"Names\":[\"A\"]}");
                string notice; var actual = new SettingsStore(path).Load(out notice);
                Check(notice != null && actual.Settings.PersonalSeconds == 90, "Invalid duration recovered");
                File.WriteAllText(path, "{\"Names\":[\" \"]}");
                actual = new SettingsStore(path).Load(out notice);
                Check(notice != null && actual.Teams.Single().Members.Count == 0, "Empty name recovered");
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
                    "{\"Names\":{\"A\":\"B\"}}",
                    "{\"Teams\":{}}",
                    "{\"Teams\":[null]}"
                };
                foreach (string json in malformed)
                {
                    File.WriteAllText(path, json);
                    string notice; var actual = new SettingsStore(path).Load(out notice);
                    Check(notice != null && actual.Settings.PersonalSeconds == 90 && actual.Teams.Single().Members.Count == 0, "Bad types recovered: " + json);
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
                string error = store.Save(new SavedConfiguration { Teams = new List<TeamRoster> { CreateTeam("Team", "A") } });
                Check(error == null, "First save: " + error);
                Check(store.Save(new SavedConfiguration { Settings = new MeetingSettings { SharedSeconds = 0 } }) != null, "Invalid write refused");
                string notice; Check(Names(store.Load(out notice).Teams.Single()).Single() == "A", "Original intact");
            });
        }
        finally { Directory.Delete(folder, true); }
    }
}
