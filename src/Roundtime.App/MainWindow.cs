using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Roundtime.Core;

namespace Roundtime.App
{
    public sealed class QueueEntry
    {
        private readonly Speaker speaker;
        public Guid Id { get { return speaker.Id; } }
        public string Name { get { return speaker.Name; } }
        public string Status { get; private set; }

        public QueueEntry(Speaker speaker, Speaker active)
        {
            this.speaker = speaker;
            Status = speaker == active ? "Speaking now" : "Waiting";
        }
    }

    internal sealed partial class MainWindow
    {
        private Meeting meeting;
        private readonly SavedConfiguration configuration;
        private readonly SettingsStore store;
        private readonly SoundService sounds = new SoundService();
        private readonly DispatcherTimer refresh = new DispatcherTimer();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly ListBox list;
        private bool canPersist;
        private bool initializing = true;
        public Window Window { get; private set; }

        private T Control<T>(string name) where T : FrameworkElement { return (T)Window.FindName(name); }
        private static Brush Brush(string color) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        private QueueEntry Selection { get { return list.SelectedItem as QueueEntry; } }

        public MainWindow(string settingsPath)
        {
            using (Stream xaml = Assembly.GetExecutingAssembly().GetManifestResourceStream("Roundtime.MainWindow.xaml"))
                Window = (Window)XamlReader.Load(xaml);
            using (Stream icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("Roundtime.Icon.ico"))
            {
                var decoder = BitmapDecoder.Create(icon, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Window.Icon = decoder.Frames.OrderByDescending(frame => frame.PixelWidth).First();
                Window.Icon.Freeze();
            }
            using (Stream mark = Assembly.GetExecutingAssembly().GetManifestResourceStream("Roundtime.Mark.xaml"))
            {
                var drawing = (DrawingImage)XamlReader.Load(mark);
                drawing.Freeze();
                Control<Image>("BrandMark").Source = drawing;
            }
            // Respect Windows' logical work area, including display scaling and
            // the taskbar. Smaller viewports can scroll the complete workspace.
            Window.MinWidth = Math.Min(Window.MinWidth, SystemParameters.WorkArea.Width);
            Window.MinHeight = Math.Min(Window.MinHeight, SystemParameters.WorkArea.Height);
            Window.Width = Math.Min(Window.Width, SystemParameters.WorkArea.Width);
            Window.Height = Math.Min(Window.Height, SystemParameters.WorkArea.Height);
            store = new SettingsStore(settingsPath);
            string notice;
            configuration = store.Load(out notice);
            canPersist = notice == null;
            list = Control<ListBox>("SpeakerList");
            Window.Topmost = configuration.AlwaysOnTop;
            Control<CheckBox>("TopmostCheck").IsChecked = configuration.AlwaysOnTop;
            Control<TextBox>("PersonalSetting").Text = FormatSetting(configuration.Settings.PersonalSeconds);
            Control<TextBox>("SharedSetting").Text = FormatSetting(configuration.Settings.SharedSeconds);
            var dragFeedback = new SpeakerDragFeedback(Control<Canvas>("QueueDragOverlay"),
                Control<Border>("QueueDragPreview"), Control<TextBlock>("QueueDragName"), Control<Border>("QueueDropMarker"));
            WireControls(dragFeedback);
            WireTeamControls(dragFeedback);
            WirePreferences();
            RefreshTeamLists(MeetingTeam);
            LoadMeetingRoster();
            initializing = false;
            RenderOvertimeHistory();
            if (notice != null) ShowNotice(notice);
            refresh.Interval = TimeSpan.FromMilliseconds(100);
            refresh.Tick += delegate {
                var oldState = meeting.State;
                meeting.Tick();
                if (oldState != meeting.State) RebuildQueue(null);
                Render();
            };
            refresh.Start();
            Window.Loaded += delegate { Control<ComboBox>("MeetingTeam").Focus(); };
            Window.Closing += delegate {
                Save(false);
                refresh.Stop();
                sounds.Dispose();
            };
        }

        private void WireControls(SpeakerDragFeedback dragFeedback)
        {
            Control<Button>("StartButton").Click += delegate { Act(meeting.StartOrTogglePause); };
            Control<Button>("DiscussButton").Click += delegate { Act(meeting.Discuss); };
            Control<Button>("ReturnButton").Click += delegate { Act(meeting.ReturnToSpeaker); };
            Control<Button>("NextButton").Click += delegate { Act(meeting.Next); };
            Control<Button>("RemoveButton").Click += delegate { RemoveSelected(); };
            Control<Button>("UpButton").Click += delegate { MoveSelected(-1); };
            Control<Button>("DownButton").Click += delegate { MoveSelected(1); };
            Control<Button>("ResetButton").Click += delegate {
                if (!CanSwitchTeams && !Confirm("Reset the timers and reload this team's saved roster?", "Reset meeting")) return;
                LoadMeetingRoster();
            };
            Control<Button>("ApplySettingsButton").Click += delegate { ApplyDurations(); };
            Control<Button>("DismissNoticeButton").Click += delegate { Control<Border>("NoticeBorder").Visibility = Visibility.Collapsed; };
            Control<CheckBox>("TopmostCheck").Checked += delegate { SetTopmost(); };
            Control<CheckBox>("TopmostCheck").Unchecked += delegate { SetTopmost(); };
            list.SelectionChanged += delegate { UpdateQueueButtons(); };
            new SpeakerListDrag<QueueEntry>(list, dragFeedback, row => row.Id, row => row.Name,
                row => meeting.Speakers.Any(speaker => speaker.Id == row.Id && IsWaiting(speaker)),
                (source, target) => ChangeMeetingQueue(() => meeting.Move(source.Id, meeting.Speakers.ToList().FindIndex(speaker => speaker.Id == target.Id))));
            Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (Keyboard.Modifiers != ModifierKeys.Alt) return;
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.S) { Act(meeting.StartOrTogglePause); e.Handled = true; }
                else if (key == Key.D) { Act(meeting.Discuss); e.Handled = true; }
                else if (key == Key.N) { Act(meeting.Next); e.Handled = true; }
            };
        }

        private void Act(Action action)
        {
            Guid? selected = Selection == null ? (Guid?)null : Selection.Id;
            var previousSpeaker = meeting.ActiveSpeaker;
            action();
            RebuildQueue(selected);
            if (meeting.ActiveSpeaker != null && meeting.ActiveSpeaker != previousSpeaker)
            {
                var queueViewport = FindScrollViewer(list);
                if (queueViewport != null) queueViewport.ScrollToTop();
            }
            Render();
        }

        private void RecordTurn(Speaker speaker, bool overtime)
        {
            var record = configuration.OvertimeHistory.Find(item => item.SpeakerId == speaker.Id);
            if (record == null)
            {
                RosterMember member;
                meetingRosterMembers.TryGetValue(speaker.Id, out member);
                record = new OvertimeRecord {
                    SpeakerId = speaker.Id, TeamId = configuration.SelectedTeamId,
                    Name = member == null ? speaker.Name : member.Name
                };
                configuration.OvertimeHistory.Add(record);
            }
            record.CompletedTurns++;
            if (overtime) record.OvertimeTurns++;
            RenderOvertimeHistory();
            Save(false);
        }

        private void RenderOvertimeHistory()
        {
            var names = configuration.Teams.ToDictionary(team => team.Id, team => team.Name);
            var rows = configuration.OvertimeHistory.Select(record => new {
                record.Name, record.OvertimeRate, record.OvertimeTurns, record.CompletedTurns,
                TeamName = record.TeamId.HasValue && names.ContainsKey(record.TeamId.Value)
                    ? names[record.TeamId.Value] : record.TeamName ?? "Previous team"
            }).OrderByDescending(record => record.OvertimeRate)
                .ThenBy(record => record.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var table = Control<DataGrid>("OvertimeTable");
            table.ItemsSource = rows;
            table.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Control<TextBlock>("EmptyOvertimeHistory").Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RemoveSelected()
        {
            var selected = Selection;
            if (selected == null) return;
            if (meeting.ActiveSpeaker != null && meeting.ActiveSpeaker.Id == selected.Id &&
                !Confirm("Remove the current speaker and advance to the next person?", "Remove current speaker")) return;
            ChangeMeetingQueue(() => meeting.Remove(selected.Id));
        }

        private void MoveSelected(int direction)
        {
            var selected = Selection;
            if (selected == null) return;
            int index = meeting.Speakers.ToList().FindIndex(s => s.Id == selected.Id);
            ChangeMeetingQueue(() => meeting.Move(selected.Id, index + direction));
        }

        private void RebuildQueue(Guid? selectedId)
        {
            if (!selectedId.HasValue && Selection != null) selectedId = Selection.Id;
            var active = meeting.ActiveSpeaker;
            var rows = meeting.Speakers.Where(s => !s.Completed).OrderByDescending(s => s == active)
                .Select(s => new QueueEntry(s, active)).ToList();
            list.ItemsSource = rows;
            if (selectedId.HasValue) list.SelectedItem = rows.FirstOrDefault(r => r.Id == selectedId.Value);
            if (list.SelectedItem == null && active != null)
                list.SelectedIndex = 0;
            Control<StackPanel>("EmptyQueue").Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateEmptyQueueHint();
            Control<TextBlock>("QueueCount").Text = rows.Count.ToString(CultureInfo.InvariantCulture);
            UpdateQueueButtons();
        }

        private void UpdateEmptyQueueHint()
        {
            var team = MeetingTeam;
            Control<TextBlock>("EmptyQueueTitle").Text = team == null ? "Choose a team" : "No speakers in this queue";
            Control<TextBlock>("EmptyQueueHint").Text = team != null && team.Members.Count > 0
                ? "Reset to reload this team's roster."
                : "Manage saved rosters in the Teams tab.";
        }

        private void UpdateQueueButtons()
        {
            var selected = Selection;
            int index = selected == null ? -1 : meeting.Speakers.ToList().FindIndex(s => s.Id == selected.Id);
            bool movable = index >= 0 && IsWaiting(meeting.Speakers[index]);
            Control<Button>("RemoveButton").IsEnabled = selected != null;
            Control<Button>("UpButton").IsEnabled = movable && index > 0 && IsWaiting(meeting.Speakers[index - 1]);
            Control<Button>("DownButton").IsEnabled = movable && index + 1 < meeting.Speakers.Count && IsWaiting(meeting.Speakers[index + 1]);
        }

        private bool IsWaiting(Speaker s) { return !s.Completed && s != meeting.ActiveSpeaker; }

        private void Render()
        {
            var state = meeting.State;
            var active = meeting.ActiveSpeaker;
            bool discussing = meeting.Phase == MeetingState.Discussion;
            Grid.SetRow(Control<Border>("PersonalCard"), discussing ? 2 : 0);
            Grid.SetRow(Control<Border>("SharedCard"), discussing ? 0 : 2);
            string name = active != null ? active.Name : state == MeetingState.Finished ? "Meeting complete" : meeting.Speakers.Count > 0 ? meeting.Speakers[0].Name : "Ready for your team";
            Control<TextBlock>("CurrentName").Text = name;
            Control<TextBlock>("CurrentName").ToolTip = name;
            Control<TextBlock>("ProgressLabel").Text = meeting.Speakers.Count(s => s.Completed) + " / " + meeting.Speakers.Count + " finished";
            Control<TextBlock>("PersonalTime").Text = FormatTime(meeting.PersonalRemaining);
            Control<ProgressBar>("PersonalProgress").Value = Math.Max(0, meeting.PersonalRemaining / meeting.PersonalAllowance);
            string hint;
            if (state == MeetingState.Ready) hint = meeting.Speakers.Count == 0 ? "Choose a team with speakers to get started" : "Start when everyone is ready";
            else if (state == MeetingState.Finished) hint = "Standup complete. Reset whenever you're ready for another round.";
            else if (discussing) hint = "Speaking time remaining";
            else if (state == MeetingState.Paused) hint = "Both timers are paused. Resume when you're ready.";
            else hint = "Progress, plans, blockers";
            Control<TextBlock>("PersonalHint").Text = hint;
            var sharedTime = Control<TextBlock>("SharedTime");
            var sharedTitle = Control<TextBlock>("SharedTitle");
            sharedTime.Text = FormatTime(meeting.SharedRemaining);
            bool exhausted = meeting.SharedRemaining <= 0;
            sharedTitle.Text = exhausted ? "Shared budget exhausted" : "Shared discussion";
            if (exhausted)
            {
                var warning = Brush(discussing ? "#9C3728" : "#FFB49C");
                sharedTime.Foreground = warning;
                sharedTitle.Foreground = warning;
            }
            else
            {
                sharedTime.ClearValue(TextBlock.ForegroundProperty);
                sharedTitle.ClearValue(TextBlock.ForegroundProperty);
            }
            string sharedHint = "For overtime and questions between speakers";
            if (discussing && state == MeetingState.Paused) sharedHint = "Discussion paused. Resume when you're ready.";
            else if (exhausted) sharedHint = "Overtime is shown below zero. You can keep going.";
            else if (discussing) sharedHint = "Time for questions and clarifications.";
            Control<TextBlock>("SharedHint").Text = sharedHint;
            var start = Control<Button>("StartButton");
            start.Content = state == MeetingState.Ready ? "_Start meeting" : state == MeetingState.Paused ? "Re_sume" : "Pau_se";
            start.IsEnabled = state != MeetingState.Finished && meeting.Speakers.Count > 0;
            var discuss = Control<Button>("DiscussButton");
            discuss.IsEnabled = state == MeetingState.Speaking;
            discuss.Visibility = discussing ? Visibility.Collapsed : Visibility.Visible;
            var returnToSpeaker = Control<Button>("ReturnButton");
            returnToSpeaker.IsEnabled = meeting.CanReturnToSpeaker;
            returnToSpeaker.Visibility = discussing ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("NextButton").IsEnabled = active != null && state != MeetingState.Finished;
            bool last = active != null && meeting.Speakers.Count(IsWaiting) == 0;
            Control<Button>("NextButton").Content = last ? "Fi_nish meeting" : "_Next speaker →";
            Control<Button>("ResetButton").IsEnabled = meeting.HasStarted || queueEdited;
            Control<ComboBox>("MeetingTeam").IsEnabled = CanSwitchTeams && configuration.Teams.Count > 0;
            UpdateTeamDeleteButton();
            Control<TextBlock>("SettingsHint").Text = meeting.HasStarted ? "Duration changes apply after Reset." : "Enter m:ss or whole minutes.";
        }

        private void ApplyDurations()
        {
            double personal, shared;
            if (!TryDuration(Control<TextBox>("PersonalSetting").Text, out personal) ||
                !TryDuration(Control<TextBox>("SharedSetting").Text, out shared))
            {
                ShowNotice("Enter a duration as m:ss (for example 1:30), or enter whole minutes."); return;
            }
            var settings = configuration.Settings.Copy();
            settings.PersonalSeconds = personal; settings.SharedSeconds = shared;
            try { meeting.Configure(settings); }
            catch (ArgumentException ex) { ShowNotice(ex.Message); return; }
            configuration.Settings = settings;
            Control<TextBox>("PersonalSetting").Text = FormatSetting(personal);
            Control<TextBox>("SharedSetting").Text = FormatSetting(shared);
            Render(); Save(true);
            if (meeting.HasStarted) ShowNotice("Durations saved. Reset the meeting to use the new allowances.");
        }

        private void SetTopmost()
        {
            if (initializing) return;
            configuration.AlwaysOnTop = Control<CheckBox>("TopmostCheck").IsChecked == true;
            Window.Topmost = configuration.AlwaysOnTop;
            Save(true);
        }

        private bool Save(bool deliberate)
        {
            preferencesSave.Stop();
            if (deliberate) canPersist = true;
            if (!canPersist) return false;
            string error = store.Save(configuration);
            if (error == null) return true;
            ShowNotice(error);
            return false;
        }

        private void ShowNotice(string text)
        {
            Control<TextBlock>("NoticeText").Text = text;
            Control<Border>("NoticeBorder").Visibility = Visibility.Visible;
        }

        private bool Confirm(string text, string title)
        {
            return MessageBox.Show(Window, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        private static string FormatTime(double seconds)
        {
            long whole = (long)Math.Ceiling(Math.Abs(seconds));
            return (seconds < 0 ? "−" : "") + (whole / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (whole % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        private static string FormatSetting(double seconds)
        {
            return ((int)seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" + ((int)seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        private static bool TryDuration(string text, out double seconds, bool plainSeconds = false)
        {
            seconds = 0;
            string[] parts = text.Trim().Split(':');
            int minutes, remainder = 0;
            bool wholeSeconds = plainSeconds && parts.Length == 1;
            if (parts.Length > 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out minutes) ||
                (!wholeSeconds && minutes > 240)) return false;
            if (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out remainder) || remainder > 59)) return false;
            seconds = wholeSeconds ? minutes : minutes * 60 + remainder;
            return true;
        }

        private static ScrollViewer FindScrollViewer(DependencyObject node)
        {
            var scrollViewer = node as ScrollViewer;
            if (scrollViewer != null) return scrollViewer;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                scrollViewer = FindScrollViewer(VisualTreeHelper.GetChild(node, i));
                if (scrollViewer != null) return scrollViewer;
            }
            return null;
        }
    }
}
