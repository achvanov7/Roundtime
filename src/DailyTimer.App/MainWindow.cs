using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DailyTimer.Core;

namespace DailyTimer.App
{
    public sealed class QueueEntry : INotifyPropertyChanged
    {
        private readonly Speaker speaker;
        private readonly Action<Guid, string> rename;
        public Guid Id { get { return speaker.Id; } }
        public string Name
        {
            get { return speaker.Name; }
            set
            {
                if (value == speaker.Name) return;
                rename(speaker.Id, value);
                var changed = PropertyChanged;
                if (changed != null) changed(this, new PropertyChangedEventArgs("Name"));
            }
        }
        public string Status { get; private set; }
        public Brush StatusBrush { get; private set; }
        public event PropertyChangedEventHandler PropertyChanged;

        public QueueEntry(Speaker speaker, Speaker active, Action<Guid, string> rename)
        {
            this.speaker = speaker;
            this.rename = rename;
            Status = speaker.Completed ? "Finished" : speaker == active ? "Speaking now" : "Waiting";
            StatusBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(speaker == active ? "#C9F36B" : "#B0BCCB"));
        }
    }

    internal sealed class MainWindow
    {
        private readonly Meeting meeting;
        private readonly SavedConfiguration configuration;
        private readonly SettingsStore store;
        private readonly SoundService sounds = new SoundService();
        private readonly DispatcherTimer refresh = new DispatcherTimer();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly ListBox list;
        private bool canPersist;
        private bool initializing = true;
        private Point dragStart;
        private Guid? dragId;
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
            var roster = configuration.Names.Select((name, index) => new Speaker(configuration.SpeakerIds[index], name));
            meeting = new Meeting(() => clock.Elapsed.TotalSeconds, roster, configuration.Settings);
            list = Control<ListBox>("SpeakerList");
            Window.Topmost = configuration.AlwaysOnTop;
            Control<CheckBox>("TopmostCheck").IsChecked = configuration.AlwaysOnTop;
            Control<CheckBox>("SoundCheck").IsChecked = configuration.Settings.SoundEnabled;
            Control<TextBox>("PersonalSetting").Text = FormatSetting(configuration.Settings.PersonalSeconds);
            Control<TextBox>("SharedSetting").Text = FormatSetting(configuration.Settings.SharedSeconds);
            WireControls();
            initializing = false;
            RebuildQueue(null);
            RenderOvertimeHistory();
            Render();
            if (notice != null) ShowNotice(notice);
            refresh.Interval = TimeSpan.FromMilliseconds(100);
            refresh.Tick += delegate {
                var oldState = meeting.State;
                meeting.Tick();
                if (oldState != meeting.State) RebuildQueue(null);
                Render();
            };
            refresh.Start();
            Window.Loaded += delegate { Control<TextBox>("NewName").Focus(); };
            Window.Closing += delegate {
                CommitNameEdit();
                Save(false);
                refresh.Stop();
                sounds.Dispose();
            };
        }

        private void WireControls()
        {
            Control<Button>("StartButton").Click += delegate { Act(meeting.StartOrTogglePause); };
            Control<Button>("DiscussButton").Click += delegate { Act(meeting.Discuss); };
            Control<Button>("NextButton").Click += delegate { Act(meeting.Next); };
            Control<Button>("AddButton").Click += delegate { AddSpeaker(); };
            Control<TextBox>("NewName").KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Enter) { AddSpeaker(); e.Handled = true; }
            };
            Control<Button>("ShuffleButton").Click += delegate { Act(meeting.ShuffleRemaining); Save(true); };
            Control<Button>("RemoveButton").Click += delegate { RemoveSelected(); };
            Control<Button>("UpButton").Click += delegate { MoveSelected(-1); };
            Control<Button>("DownButton").Click += delegate { MoveSelected(1); };
            Control<Button>("ResetButton").Click += delegate {
                if (meeting.HasStarted && !Confirm("Reset this meeting? Both timers and everyone's completed status will be reset.", "Reset meeting")) return;
                Act(meeting.Reset);
            };
            Control<Button>("ApplySettingsButton").Click += delegate { ApplyDurations(); };
            Control<Button>("DismissNoticeButton").Click += delegate { Control<Border>("NoticeBorder").Visibility = Visibility.Collapsed; };
            Control<Button>("TestSoundButton").Click += delegate {
                if (!sounds.Play(AlertKind.PersonalExpired)) ShowNotice("The alert sound could not be played. Check your Windows audio output.");
            };
            Control<CheckBox>("SoundCheck").Checked += delegate { SetSound(); };
            Control<CheckBox>("SoundCheck").Unchecked += delegate { SetSound(); };
            Control<CheckBox>("TopmostCheck").Checked += delegate { SetTopmost(); };
            Control<CheckBox>("TopmostCheck").Unchecked += delegate { SetTopmost(); };
            list.SelectionChanged += delegate { UpdateQueueButtons(); };
            list.PreviewMouseLeftButtonDown += QueueMouseDown;
            list.PreviewMouseMove += QueueMouseMove;
            list.DragOver += QueueDragOver;
            list.Drop += QueueDrop;
            list.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Enter) { CommitNameEdit(); Control<Button>("StartButton").Focus(); e.Handled = true; }
            };
            Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (Keyboard.Modifiers != ModifierKeys.Alt) return;
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.S) { Act(meeting.StartOrTogglePause); e.Handled = true; }
                else if (key == Key.D) { Act(meeting.Discuss); e.Handled = true; }
                else if (key == Key.N) { Act(meeting.Next); e.Handled = true; }
            };
            meeting.Alert += delegate(AlertKind kind) {
                if (configuration.Settings.SoundEnabled && !sounds.Play(kind))
                    ShowNotice("The alert sound could not be played. The timers are still running.");
            };
            meeting.TurnCompleted += RecordTurn;
        }

        private void CommitNameEdit()
        {
            var focused = Keyboard.FocusedElement as TextBox;
            if (focused == null) return;
            var binding = BindingOperations.GetBindingExpression(focused, TextBox.TextProperty);
            if (binding != null) binding.UpdateSource();
        }

        private void Act(Action action)
        {
            CommitNameEdit();
            Guid? selected = Selection == null ? (Guid?)null : Selection.Id;
            action();
            RebuildQueue(selected);
            Render();
        }

        private void AddSpeaker()
        {
            var input = Control<TextBox>("NewName");
            try
            {
                string name = Speaker.ValidateName(input.Text);
                var returningId = FindReturningSpeaker(name);
                if (returningId.HasValue) meeting.Add(returningId.Value, name);
                else meeting.Add(name);
            }
            catch (ArgumentException ex) { ShowNotice(ex.Message); input.Focus(); return; }
            var speaker = meeting.Speakers.Last();
            UpdateHistoryName(speaker.Id, speaker.Name);
            input.Clear();
            RebuildQueue(speaker.Id);
            Render();
            Save(true);
            input.Focus();
        }

        private void RenameSpeaker(Guid id, string name)
        {
            try
            {
                meeting.Rename(id, name);
                var speaker = meeting.Speakers.FirstOrDefault(s => s.Id == id);
                if (speaker != null) UpdateHistoryName(id, speaker.Name);
                Save(true);
                Render();
            }
            catch (ArgumentException ex) { ShowNotice(ex.Message); }
        }

        private Guid? FindReturningSpeaker(string name)
        {
            if (meeting.Speakers.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))) return null;
            var matches = configuration.OvertimeHistory
                .Where(record => string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            return matches.Count == 1 ? (Guid?)matches[0].SpeakerId : null;
        }

        private void UpdateHistoryName(Guid id, string name)
        {
            var record = configuration.OvertimeHistory.Find(item => item.SpeakerId == id);
            if (record == null || record.Name == name) return;
            record.Name = name;
            RenderOvertimeHistory();
        }

        private void RecordTurn(Speaker speaker, bool overtime)
        {
            var record = configuration.OvertimeHistory.Find(item => item.SpeakerId == speaker.Id);
            if (record == null)
            {
                record = new OvertimeRecord { SpeakerId = speaker.Id, Name = speaker.Name };
                configuration.OvertimeHistory.Add(record);
            }
            record.CompletedTurns++;
            if (overtime) record.OvertimeTurns++;
            RenderOvertimeHistory();
            Save(false);
        }

        private void RenderOvertimeHistory()
        {
            var rows = configuration.OvertimeHistory.OrderByDescending(record => record.OvertimeRate)
                .ThenBy(record => record.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var table = Control<DataGrid>("OvertimeTable");
            table.ItemsSource = rows;
            table.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Control<TextBlock>("EmptyOvertimeHistory").Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RemoveSelected()
        {
            CommitNameEdit();
            var selected = Selection;
            if (selected == null) return;
            if (meeting.ActiveSpeaker != null && meeting.ActiveSpeaker.Id == selected.Id &&
                !Confirm("Remove the current speaker and advance to the next person?", "Remove current speaker")) return;
            Act(() => meeting.Remove(selected.Id));
            Save(true);
        }

        private void MoveSelected(int direction)
        {
            var selected = Selection;
            if (selected == null) return;
            int index = meeting.Speakers.ToList().FindIndex(s => s.Id == selected.Id);
            Act(() => meeting.Move(selected.Id, index + direction));
            Save(true);
        }

        private void RebuildQueue(Guid? selectedId)
        {
            if (!selectedId.HasValue && Selection != null) selectedId = Selection.Id;
            var rows = meeting.Speakers.Select(s => new QueueEntry(s, meeting.ActiveSpeaker, RenameSpeaker)).ToList();
            list.ItemsSource = rows;
            if (selectedId.HasValue) list.SelectedItem = rows.FirstOrDefault(r => r.Id == selectedId.Value);
            if (list.SelectedItem == null && meeting.ActiveSpeaker != null)
                list.SelectedItem = rows.FirstOrDefault(r => r.Id == meeting.ActiveSpeaker.Id);
            Control<StackPanel>("EmptyQueue").Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Control<TextBlock>("QueueCount").Text = rows.Count.ToString(CultureInfo.InvariantCulture);
            UpdateQueueButtons();
        }

        private void UpdateQueueButtons()
        {
            var selected = Selection;
            int index = selected == null ? -1 : meeting.Speakers.ToList().FindIndex(s => s.Id == selected.Id);
            bool movable = index >= 0 && IsWaiting(meeting.Speakers[index]);
            Control<Button>("RemoveButton").IsEnabled = selected != null;
            Control<Button>("UpButton").IsEnabled = movable && index > 0 && IsWaiting(meeting.Speakers[index - 1]);
            Control<Button>("DownButton").IsEnabled = movable && index + 1 < meeting.Speakers.Count && IsWaiting(meeting.Speakers[index + 1]);
            Control<Button>("ShuffleButton").IsEnabled = meeting.Speakers.Count(IsWaiting) > 1;
        }

        private bool IsWaiting(Speaker s) { return !s.Completed && s != meeting.ActiveSpeaker; }

        private void Render()
        {
            var state = meeting.State;
            var active = meeting.ActiveSpeaker;
            bool discussing = meeting.Phase == MeetingState.Discussion;
            bool running = state == MeetingState.Speaking || state == MeetingState.Discussion;
            Control<TextBlock>("SessionState").Text = state == MeetingState.Ready ? "Ready to start" : state == MeetingState.Finished ? "Meeting complete" : state == MeetingState.Paused ? "Paused" : discussing ? "Discussion" : "Speaking";
            string name = active != null ? active.Name : state == MeetingState.Finished ? "Everyone has spoken" : meeting.Speakers.Count > 0 ? meeting.Speakers[0].Name : "Ready for your team";
            Control<TextBlock>("CurrentName").Text = name;
            Control<TextBlock>("CurrentName").ToolTip = name;
            Control<TextBlock>("ProgressLabel").Text = meeting.Speakers.Count(s => s.Completed) + " / " + meeting.Speakers.Count + " finished";
            Control<TextBlock>("PersonalTime").Text = FormatTime(meeting.PersonalRemaining);
            Control<ProgressBar>("PersonalProgress").Value = Math.Max(0, meeting.PersonalRemaining / meeting.PersonalAllowance);
            Control<TextBlock>("PersonalTime").Foreground = Brush(discussing ? "#657184" : "#16263A");
            string hint;
            if (state == MeetingState.Ready) hint = meeting.Speakers.Count == 0 ? "Add speakers to get started" : "Start when everyone is ready";
            else if (state == MeetingState.Finished) hint = "Standup complete. Reset whenever you're ready for another round.";
            else if (state == MeetingState.Paused) hint = "Both timers are paused. Resume when you're ready.";
            else if (discussing) hint = "Shared time is running. Advance when the discussion ends.";
            else hint = "Progress, plans, blockers";
            Control<TextBlock>("PersonalHint").Text = hint;
            Control<TextBlock>("SharedTime").Text = FormatTime(meeting.SharedRemaining);
            bool exhausted = meeting.SharedRemaining <= 0;
            Control<TextBlock>("SharedTime").Foreground = Brush(exhausted ? "#FFB49C" : discussing && running ? "#C9F36B" : "#F2F6FA");
            Control<TextBlock>("SharedTitle").Text = exhausted ? "Shared budget exhausted" : "Shared discussion";
            Control<TextBlock>("SharedTitle").Foreground = Brush(exhausted ? "#FFB49C" : "#B0BCCB");
            Control<Border>("SharedCard").BorderBrush = Brush(discussing && running ? exhausted ? "#FFB49C" : "#849B61" : "#39495E");
            Control<TextBlock>("SharedHint").Text = exhausted ? "Overtime is shown below zero. You can keep going." : discussing && running ? "Running · overtime and clarifying questions" : "For overtime and questions between speakers";
            var start = Control<Button>("StartButton");
            start.Content = state == MeetingState.Ready ? "_Start meeting" : state == MeetingState.Paused ? "Re_sume" : "Pau_se";
            start.IsEnabled = state != MeetingState.Finished && meeting.Speakers.Count > 0;
            Control<Button>("DiscussButton").IsEnabled = state == MeetingState.Speaking;
            Control<Button>("NextButton").IsEnabled = active != null && state != MeetingState.Finished;
            bool last = active != null && meeting.Speakers.Count(IsWaiting) == 0;
            Control<Button>("NextButton").Content = last ? "Fi_nish meeting" : "_Next speaker →";
            Control<Button>("ResetButton").IsEnabled = meeting.HasStarted;
            Control<TextBlock>("SettingsHint").Text = meeting.HasStarted ? "Duration changes apply after Reset. Sound and window preferences apply immediately." : "Enter m:ss or whole minutes. Sound and window preferences apply immediately.";
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

        private void SetSound()
        {
            if (initializing) return;
            configuration.Settings.SoundEnabled = Control<CheckBox>("SoundCheck").IsChecked == true;
            Save(true);
        }

        private void SetTopmost()
        {
            if (initializing) return;
            configuration.AlwaysOnTop = Control<CheckBox>("TopmostCheck").IsChecked == true;
            Window.Topmost = configuration.AlwaysOnTop;
            Save(true);
        }

        private void Save(bool deliberate)
        {
            if (deliberate) canPersist = true;
            if (!canPersist) return;
            configuration.Names = meeting.Speakers.Select(s => s.Name).ToList();
            configuration.SpeakerIds = meeting.Speakers.Select(s => s.Id).ToList();
            string error = store.Save(configuration);
            if (error != null) ShowNotice(error);
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

        private static bool TryDuration(string text, out double seconds)
        {
            seconds = 0;
            string[] parts = text.Trim().Split(':');
            int minutes, remainder = 0;
            if (parts.Length < 1 || parts.Length > 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out minutes) || minutes > 240) return false;
            if (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out remainder) || remainder > 59)) return false;
            seconds = minutes * 60 + remainder;
            return true;
        }

        private static T Ancestor<T>(DependencyObject node) where T : DependencyObject
        {
            while (node != null)
            {
                var result = node as T;
                if (result != null) return result;
                node = VisualTreeHelper.GetParent(node);
            }
            return null;
        }

        private void QueueMouseDown(object sender, MouseButtonEventArgs e)
        {
            dragId = null;
            var item = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item == null) return;
            item.IsSelected = true;
            var grip = e.OriginalSource as TextBlock;
            if (grip != null && grip.Text == "≡" && Selection != null)
            {
                var s = meeting.Speakers.First(x => x.Id == Selection.Id);
                if (IsWaiting(s)) { dragId = s.Id; dragStart = e.GetPosition(list); }
            }
        }

        private void QueueMouseMove(object sender, MouseEventArgs e)
        {
            if (!dragId.HasValue || e.LeftButton != MouseButtonState.Pressed) return;
            Point position = e.GetPosition(list);
            if (Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance && Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            Guid id = dragId.Value; dragId = null;
            DragDrop.DoDragDrop(list, new DataObject(typeof(Guid), id), DragDropEffects.Move);
        }

        private void QueueDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(Guid)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void QueueDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(Guid))) return;
            Guid id = (Guid)e.Data.GetData(typeof(Guid));
            var target = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            var row = target == null ? null : target.DataContext as QueueEntry;
            int index = row == null ? meeting.Speakers.Count - 1 : meeting.Speakers.ToList().FindIndex(s => s.Id == row.Id);
            Act(() => meeting.Move(id, index)); Save(true); e.Handled = true;
        }
    }
}
