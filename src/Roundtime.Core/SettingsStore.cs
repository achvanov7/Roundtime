using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Web.Script.Serialization;

namespace Roundtime.Core
{
    public enum OvertimeSound { Classic = 0, GlassChime = 1, MorningAlarm = 2 }
    public enum ColorTheme { Classic = 0, Forest = 1, Plum = 2 }

    public sealed class OvertimeRecord
    {
        public Guid SpeakerId { get; set; }
        public Guid? TeamId { get; set; }
        public string TeamName { get; set; }
        public string Name { get; set; }
        public int CompletedTurns { get; set; }
        public int OvertimeTurns { get; set; }
        [ScriptIgnore]
        public decimal OvertimeRate { get { return (decimal)OvertimeTurns / CompletedTurns; } }
    }

    public sealed class SavedConfiguration
    {
        public List<TeamRoster> Teams { get; set; }
        public Guid? SelectedTeamId { get; set; }
        public List<OvertimeRecord> OvertimeHistory { get; set; }
        public MeetingSettings Settings { get; set; }
        public bool AlwaysOnTop { get; set; }
        public OvertimeSound OvertimeSound { get; set; }
        public int AlertVolume { get; set; }
        public ColorTheme Theme { get; set; }

        public SavedConfiguration()
        {
            Teams = new List<TeamRoster> { new TeamRoster { Id = Guid.NewGuid(), Name = "Default team" } };
            SelectedTeamId = Teams[0].Id;
            OvertimeHistory = new List<OvertimeRecord>();
            Settings = new MeetingSettings();
            AlertVolume = 100;
        }
    }

    public sealed class SettingsStore
    {
        private readonly string path;

        public SettingsStore(string path) { this.path = path; }

        public SavedConfiguration Load(out string notice)
        {
            notice = null;
            try
            {
                if (!File.Exists(path)) return ValidatedCopy(new SavedConfiguration());
                if (new FileInfo(path).Length > 1024 * 1024) throw new ArgumentException("Settings file is too large.");
                var serializer = new JavaScriptSerializer();
                object raw = serializer.DeserializeObject(File.ReadAllText(path, Encoding.UTF8));
                var properties = raw as Dictionary<string, object>;
                if (properties == null) throw new ArgumentException("Settings must be a JSON object.");
                var fields = new Dictionary<string, object>(properties, StringComparer.OrdinalIgnoreCase);
                object teams;
                bool hasTeams = fields.TryGetValue("Teams", out teams);
                if (hasTeams && !(teams is object[]))
                    throw new ArgumentException("Teams must be a JSON array.");
                var configuration = serializer.ConvertToType<SavedConfiguration>(raw);
                if (!hasTeams) MigrateLegacyRoster(configuration, fields, serializer);
                return ValidatedCopy(configuration);
            }
            catch (Exception ex)
            {
                if (!Recoverable(ex)) throw;
                notice = "Saved settings could not be loaded. Defaults are in use. " + ex.Message;
                return ValidatedCopy(new SavedConfiguration());
            }
        }

        public string Save(SavedConfiguration configuration)
        {
            string temporary = null;
            try
            {
                var clean = ValidatedCopy(configuration);
                string fullPath = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var serializer = new JavaScriptSerializer();
                File.WriteAllText(temporary, serializer.Serialize(clean), new UTF8Encoding(false));
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
                else File.Move(temporary, fullPath);
                return null;
            }
            catch (Exception ex)
            {
                if (!Recoverable(ex)) throw;
                return "Settings could not be saved. Your meeting can continue. " + ex.Message;
            }
            finally
            {
                if (temporary != null)
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        private static void MigrateLegacyRoster(SavedConfiguration configuration, Dictionary<string, object> properties, JavaScriptSerializer serializer)
        {
            object names;
            if (!properties.TryGetValue("Names", out names)) names = new object[0];
            var entries = names as object[];
            if (entries == null || entries.Any(name => !(name is string)))
                throw new ArgumentException("Speaker names must be a JSON array of strings.");

            object storedIds;
            var ids = properties.TryGetValue("SpeakerIds", out storedIds) && storedIds != null
                ? serializer.ConvertToType<List<Guid>>(storedIds)
                : entries.Select(name => Guid.NewGuid()).ToList();
            if (ids.Count != entries.Length)
                throw new ArgumentException("Each saved speaker must have an independent identity.");

            var team = configuration.Teams.Single();
            team.Members = entries.Select((name, index) => new RosterMember { Id = ids[index], Name = (string)name }).ToList();
            if (configuration.OvertimeHistory != null)
                foreach (var record in configuration.OvertimeHistory)
                    if (record != null) record.TeamId = team.Id;
        }

        private static SavedConfiguration ValidatedCopy(SavedConfiguration configuration)
        {
            if (configuration == null || configuration.Settings == null || configuration.Teams == null ||
                configuration.OvertimeHistory == null)
                throw new ArgumentException("Incomplete settings.");
            configuration.Settings.Validate();
            var teams = new List<TeamRoster>();
            var teamIds = new HashSet<Guid>();
            var teamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var memberIds = new HashSet<Guid>();
            foreach (var team in configuration.Teams)
            {
                if (team == null || team.Id == Guid.Empty || !teamIds.Add(team.Id) || team.Members == null)
                    throw new ArgumentException("Invalid team roster.");
                var name = Speaker.ValidateName(team.Name);
                if (!teamNames.Add(name)) throw new ArgumentException("Team names must be unique.");
                var copy = new TeamRoster { Id = team.Id, Name = name };
                foreach (var member in team.Members)
                {
                    if (member == null || member.Id == Guid.Empty || !memberIds.Add(member.Id))
                        throw new ArgumentException("Each saved speaker must have an independent identity.");
                    copy.Members.Add(new RosterMember {
                        Id = member.Id, Name = Speaker.ValidateName(member.Name),
                        PersonalSeconds = MeetingSettings.ValidatePersonalOverride(member.PersonalSeconds)
                    });
                }
                teams.Add(copy);
            }

            var selected = teams.FirstOrDefault(team => team.Id == configuration.SelectedTeamId) ?? teams.FirstOrDefault();

            var history = new List<OvertimeRecord>();
            var teamLabels = teams.ToDictionary(team => team.Id, team => team.Name);
            var recordedIds = new HashSet<Guid>();
            foreach (var record in configuration.OvertimeHistory)
            {
                if (record == null || record.SpeakerId == Guid.Empty || record.TeamId == Guid.Empty || !recordedIds.Add(record.SpeakerId) ||
                    record.CompletedTurns <= 0 || record.OvertimeTurns < 0 || record.OvertimeTurns > record.CompletedTurns)
                    throw new ArgumentException("Invalid overtime history.");
                string teamName;
                if (!record.TeamId.HasValue || !teamLabels.TryGetValue(record.TeamId.Value, out teamName))
                    teamName = record.TeamName == null ? null : Speaker.ValidateName(record.TeamName);
                history.Add(new OvertimeRecord {
                    SpeakerId = record.SpeakerId, TeamId = record.TeamId, TeamName = teamName, Name = Speaker.ValidateName(record.Name),
                    CompletedTurns = record.CompletedTurns, OvertimeTurns = record.OvertimeTurns
                });
            }
            return new SavedConfiguration {
                Teams = teams,
                SelectedTeamId = selected == null ? (Guid?)null : selected.Id,
                OvertimeHistory = history,
                Settings = configuration.Settings.Copy(),
                AlwaysOnTop = configuration.AlwaysOnTop,
                OvertimeSound = Enum.IsDefined(typeof(OvertimeSound), configuration.OvertimeSound)
                    ? configuration.OvertimeSound : OvertimeSound.Classic,
                AlertVolume = Math.Max(0, Math.Min(100, configuration.AlertVolume)),
                Theme = Enum.IsDefined(typeof(ColorTheme), configuration.Theme)
                    ? configuration.Theme : ColorTheme.Classic
            };
        }

        private static bool Recoverable(Exception ex)
        {
            // Framework type converters sometimes wrap a bad JSON value in a
            // plain Exception. Recognize the conversion failure at its source.
            Exception cause = ex.GetBaseException();
            return ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException ||
                ex is InvalidOperationException || ex is NotSupportedException || ex is SecurityException ||
                cause is FormatException || cause is OverflowException;
        }
    }
}
