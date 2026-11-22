using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DailyTimer.Core
{
    public enum MeetingState { Ready, Speaking, Discussion, Paused, Finished }
    public enum AlertKind { PersonalExpired, SharedExpired }

    public sealed class Meeting
    {
        private readonly Func<double> clock;
        private readonly List<Speaker> speakers;
        private readonly Random random = new Random();
        private MeetingSettings configured;
        private MeetingSettings live;
        private MeetingState pausedPhase;
        private double lastTime;
        private double turnPersonalAllowance;
        private double personalRemaining;
        private bool sharedAlerted;

        public event Action<AlertKind> Alert;
        public event Action<Speaker, bool> TurnCompleted;
        public MeetingState State { get; private set; }
        public Speaker ActiveSpeaker { get; private set; }
        public double PersonalRemaining { get { return State == MeetingState.Ready ? PersonalAllowance : personalRemaining; } }
        public double SharedRemaining { get; private set; }
        public ReadOnlyCollection<Speaker> Speakers { get; private set; }
        public bool HasStarted { get { return State != MeetingState.Ready; } }
        public MeetingState Phase { get { return State == MeetingState.Paused ? pausedPhase : State; } }
        public bool CanReturnToSpeaker { get { return Phase == MeetingState.Discussion && PersonalRemaining > 0; } }
        public double PersonalAllowance { get { return State == MeetingState.Ready ? AllowanceFor(speakers.FirstOrDefault()) : turnPersonalAllowance; } }
        public double SharedAllowance { get { return live.SharedSeconds; } }

        public Meeting(Func<double> clock, IEnumerable<Speaker> speakers, MeetingSettings settings)
        {
            if (clock == null || speakers == null || settings == null) throw new ArgumentNullException();
            settings.Validate();
            this.clock = clock;
            this.speakers = speakers.ToList();
            if (this.speakers.Any(s => s == null) || this.speakers.Select(s => s.Id).Distinct().Count() != this.speakers.Count)
                throw new ArgumentException("Each speaker must have an independent identity.");
            Speakers = this.speakers.AsReadOnly();
            configured = settings.Copy();
            Reset();
        }

        public void Tick()
        {
            double now = clock();
            double elapsed = Math.Max(0, now - lastTime);
            lastTime = now;
            if (State != MeetingState.Speaking && State != MeetingState.Discussion) return;
            bool personalExpired = false;
            bool sharedExpired = false;
            if (State == MeetingState.Speaking)
            {
                if (elapsed < personalRemaining) { personalRemaining -= elapsed; return; }
                elapsed -= personalRemaining;
                personalRemaining = 0;
                State = MeetingState.Discussion;
                personalExpired = true;
            }
            SharedRemaining -= elapsed;
            if (SharedRemaining <= 0 && !sharedAlerted)
            {
                sharedAlerted = true;
                sharedExpired = true;
            }
            // Commit all clock changes before notifying external subscribers.
            if (personalExpired) RaiseAlert(AlertKind.PersonalExpired);
            if (sharedExpired) RaiseAlert(AlertKind.SharedExpired);
        }

        private void RaiseAlert(AlertKind kind)
        {
            var handler = Alert;
            if (handler != null) handler(kind);
        }

        public void StartOrTogglePause()
        {
            Tick();
            if (State == MeetingState.Ready)
            {
                ActiveSpeaker = speakers.FirstOrDefault(s => !s.Completed);
                if (ActiveSpeaker != null)
                {
                    StartPersonalTimer();
                    State = MeetingState.Speaking;
                }
            }
            else if (State == MeetingState.Paused) State = pausedPhase;
            else if (State == MeetingState.Speaking || State == MeetingState.Discussion)
            {
                pausedPhase = State;
                State = MeetingState.Paused;
            }
            lastTime = clock();
        }

        public void Discuss()
        {
            Tick();
            if (State == MeetingState.Speaking) State = MeetingState.Discussion;
        }

        public void ReturnToSpeaker()
        {
            Tick();
            if (!CanReturnToSpeaker) return;
            if (State == MeetingState.Paused) pausedPhase = MeetingState.Speaking;
            else State = MeetingState.Speaking;
        }

        public void Next()
        {
            Tick();
            if (ActiveSpeaker == null || State == MeetingState.Finished || State == MeetingState.Ready) return;
            var speaker = ActiveSpeaker;
            bool overtime = PersonalRemaining == 0;
            speaker.Completed = true;
            Advance(State == MeetingState.Paused);
            var handler = TurnCompleted;
            if (handler != null) handler(speaker, overtime);
        }

        private void Advance(bool paused)
        {
            ActiveSpeaker = speakers.FirstOrDefault(s => !s.Completed);
            if (ActiveSpeaker == null) State = MeetingState.Finished;
            else
            {
                StartPersonalTimer();
                State = paused ? MeetingState.Paused : MeetingState.Speaking;
                pausedPhase = MeetingState.Speaking;
            }
            lastTime = clock();
        }

        public void Reset()
        {
            live = configured.Copy();
            foreach (var s in speakers) s.Completed = false;
            ActiveSpeaker = null;
            StartPersonalTimer();
            SharedRemaining = live.SharedSeconds;
            sharedAlerted = false;
            State = MeetingState.Ready;
            pausedPhase = MeetingState.Speaking;
            lastTime = clock();
        }

        private double AllowanceFor(Speaker speaker)
        {
            return speaker != null && speaker.PersonalSeconds.HasValue ? speaker.PersonalSeconds.Value : live.PersonalSeconds;
        }

        private void StartPersonalTimer()
        {
            turnPersonalAllowance = AllowanceFor(ActiveSpeaker);
            personalRemaining = turnPersonalAllowance;
        }

        public void Configure(MeetingSettings settings)
        {
            settings.Validate();
            Tick();
            configured = settings.Copy();
            if (State == MeetingState.Ready) Reset();
        }

        public void Add(string name) { Add(Guid.NewGuid(), name); }

        public void Add(Guid id, string name)
        {
            if (speakers.Any(s => s.Id == id)) throw new ArgumentException("Each speaker must have an independent identity.");
            speakers.Add(new Speaker(id, name));
        }

        public void Rename(Guid id, string name)
        {
            var valid = Speaker.ValidateName(name);
            var s = speakers.FirstOrDefault(x => x.Id == id);
            if (s != null) s.Name = valid;
        }

        public void Remove(Guid id)
        {
            Tick();
            var s = speakers.FirstOrDefault(x => x.Id == id);
            if (s == null) return;
            bool active = ActiveSpeaker == s;
            bool paused = State == MeetingState.Paused;
            speakers.Remove(s);
            if (active) Advance(paused);
        }

        public void Move(Guid id, int destinationIndex)
        {
            var s = speakers.FirstOrDefault(x => x.Id == id);
            if (s == null || s.Completed || s == ActiveSpeaker || destinationIndex < 0 || destinationIndex >= speakers.Count) return;
            var target = speakers[destinationIndex];
            if (target.Completed || target == ActiveSpeaker) return;
            speakers.Remove(s);
            speakers.Insert(destinationIndex, s);
        }

        public void ShuffleRemaining()
        {
            var indexes = Enumerable.Range(0, speakers.Count)
                .Where(i => !speakers[i].Completed && speakers[i] != ActiveSpeaker).ToArray();
            for (int i = indexes.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var temp = speakers[indexes[i]];
                speakers[indexes[i]] = speakers[indexes[j]];
                speakers[indexes[j]] = temp;
            }
        }
    }
}
