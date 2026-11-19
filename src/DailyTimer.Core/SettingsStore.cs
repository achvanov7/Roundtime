using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Web.Script.Serialization;

namespace DailyTimer.Core
{
    public sealed class OvertimeRecord
    {
        public Guid SpeakerId { get; set; }
        public string Name { get; set; }
        public int CompletedTurns { get; set; }
        public int OvertimeTurns { get; set; }
        [ScriptIgnore]
        public decimal OvertimeRate { get { return (decimal)OvertimeTurns / CompletedTurns; } }
    }

    public sealed class SavedConfiguration
    {
        public List<string> Names { get; set; }
        public List<Guid> SpeakerIds { get; set; }
        public List<OvertimeRecord> OvertimeHistory { get; set; }
        public MeetingSettings Settings { get; set; }
        public bool AlwaysOnTop { get; set; }

        public SavedConfiguration()
        {
            Names = new List<string>();
            OvertimeHistory = new List<OvertimeRecord>();
            Settings = new MeetingSettings();
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
                object names;
                if (properties.TryGetValue("Names", out names) &&
                    (!(names is object[]) || ((object[])names).Any(n => !(n is string))))
                    throw new ArgumentException("Speaker names must be a JSON array of strings.");
                return ValidatedCopy(serializer.ConvertToType<SavedConfiguration>(raw));
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

        private static SavedConfiguration ValidatedCopy(SavedConfiguration configuration)
        {
            if (configuration == null || configuration.Settings == null || configuration.Names == null ||
                configuration.OvertimeHistory == null)
                throw new ArgumentException("Incomplete settings.");
            configuration.Settings.Validate();
            var names = configuration.Names.Select(Speaker.ValidateName).ToList();
            // Settings written before overtime tracking only contain names.
            var ids = configuration.SpeakerIds ?? names.Select(name => Guid.NewGuid()).ToList();
            if (ids.Count != names.Count || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Count)
                throw new ArgumentException("Each saved speaker must have an independent identity.");

            var history = new List<OvertimeRecord>();
            var recordedIds = new HashSet<Guid>();
            foreach (var record in configuration.OvertimeHistory)
            {
                if (record == null || record.SpeakerId == Guid.Empty || !recordedIds.Add(record.SpeakerId) ||
                    record.CompletedTurns <= 0 || record.OvertimeTurns < 0 || record.OvertimeTurns > record.CompletedTurns)
                    throw new ArgumentException("Invalid overtime history.");
                history.Add(new OvertimeRecord {
                    SpeakerId = record.SpeakerId, Name = Speaker.ValidateName(record.Name),
                    CompletedTurns = record.CompletedTurns, OvertimeTurns = record.OvertimeTurns
                });
            }
            return new SavedConfiguration {
                Names = names,
                SpeakerIds = new List<Guid>(ids),
                OvertimeHistory = history,
                Settings = configuration.Settings.Copy(),
                AlwaysOnTop = configuration.AlwaysOnTop
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
