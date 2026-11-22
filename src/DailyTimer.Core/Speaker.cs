using System;

namespace DailyTimer.Core
{
    public sealed class Speaker
    {
        public Guid Id { get; private set; }
        public string Name { get; internal set; }
        public bool Completed { get; internal set; }
        public int? PersonalSeconds { get; private set; }

        public Speaker(string name) : this(Guid.NewGuid(), name) { }

        public Speaker(Guid id, string name) : this(id, name, null) { }

        public Speaker(Guid id, string name, int? personalSeconds)
        {
            if (id == Guid.Empty) throw new ArgumentException("A speaker must have an identity.", "id");
            Id = id;
            Name = ValidateName(name);
            PersonalSeconds = MeetingSettings.ValidatePersonalOverride(personalSeconds);
        }

        public static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
                throw new ArgumentException("Enter a name between 1 and 120 characters.");
            return name.Trim();
        }
    }
}
