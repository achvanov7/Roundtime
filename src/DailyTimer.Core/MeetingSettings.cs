using System;

namespace DailyTimer.Core
{
    public sealed class MeetingSettings
    {
        public double PersonalSeconds { get; set; }
        public double SharedSeconds { get; set; }
        public bool SoundEnabled { get; set; }

        public MeetingSettings()
        {
            PersonalSeconds = 90;
            SharedSeconds = 600;
            SoundEnabled = true;
        }

        public MeetingSettings Copy()
        {
            return new MeetingSettings {
                PersonalSeconds = PersonalSeconds,
                SharedSeconds = SharedSeconds,
                SoundEnabled = SoundEnabled
            };
        }

        public void Validate()
        {
            if (double.IsNaN(PersonalSeconds) || double.IsInfinity(PersonalSeconds) ||
                PersonalSeconds < 1 || PersonalSeconds > 3600 ||
                double.IsNaN(SharedSeconds) || double.IsInfinity(SharedSeconds) ||
                SharedSeconds < 1 || SharedSeconds > 14400)
                throw new ArgumentException("Personal time must be 0:01–60:00 and discussion time 0:01–240:00.");
        }
    }
}
