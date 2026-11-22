using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using DailyTimer.App;
using DailyTimer.Core;

internal static class LayoutTests
{
    private sealed class QueueRow
    {
        public string Name { get; set; }
        public string Status { get { return "Waiting"; } }
        public Brush StatusBrush { get { return Brushes.White; } }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Window window;
            using (var input = File.OpenRead(args[0])) window = (Window)XamlReader.Load(input);
            if (window.MinWidth > 640 || window.MinHeight > 480)
                throw new Exception("Window cannot fit a 640x480 logical work area.");
            var root = (FrameworkElement)window.Content;
            var tabs = (TabControl)window.FindName("WorkspaceTabs");
            var meetingTab = (TabItem)window.FindName("MeetingTab");
            var teamsTab = (TabItem)window.FindName("TeamsTab");
            var rulesTab = (TabItem)window.FindName("RulesTab");
            Arrange(root, 640, 440);
            if (tabs == null || meetingTab == null || teamsTab == null || rulesTab == null || tabs.Items.Count != 3 || tabs.SelectedItem != meetingTab)
                throw new Exception("The meeting must open in its own tab alongside Teams and Rules & rankings.");
            var meetingViewport = (ScrollViewer)meetingTab.Content;
            CheckQueueScroll(window, root, meetingViewport, 1132, 740);
            if (meetingViewport.ScrollableHeight > 0)
                throw new Exception("The meeting workspace should fit the standard window.");
            CheckQueueScroll(window, root, meetingViewport, 640, 440);
            Console.WriteLine("PASS Long speaker queues scroll independently of timers and meeting controls");
            CheckReachable(window, root, meetingViewport, "MeetingTeam", "StartButton", "DiscussButton", "NextButton", "ResetButton");
            ((Button)window.FindName("DiscussButton")).Visibility = Visibility.Collapsed;
            ((Button)window.FindName("ReturnButton")).Visibility = Visibility.Visible;
            root.UpdateLayout();
            CheckReachable(window, root, meetingViewport, "ReturnButton");

            tabs.SelectedItem = teamsTab;
            Arrange(root, 640, 440);
            CheckReachable(window, root, (ScrollViewer)teamsTab.Content,
                "TeamNameInput", "AddTeamButton", "RenameTeamButton", "DeleteTeamButton",
                "MemberNameInput", "MemberPersonalTimeInput", "AddMemberButton", "SaveMemberButton", "MoveMemberUpButton", "MoveMemberDownButton", "RemoveMemberButton");
            Console.WriteLine("PASS Team and roster controls are reachable in a small work area");

            tabs.SelectedItem = rulesTab;
            var rulesViewport = (ScrollViewer)rulesTab.Content;
            ((TextBox)window.FindName("PersonalSetting")).Text = "60:00";
            ((TextBox)window.FindName("SharedSetting")).Text = "240:00";
            Arrange(root, 1132, 740);
            foreach (string name in new[] { "PersonalSetting", "SharedSetting" })
            {
                var input = (TextBox)window.FindName(name);
                if (input.ViewportWidth <= 0 || input.ExtentWidth > input.ViewportWidth + 1)
                    throw new Exception(name + " clips the longest supported duration.");
            }
            Console.WriteLine("PASS Duration fields show the longest supported values without horizontal scrolling");
            var table = (DataGrid)window.FindName("OvertimeTable");
            table.ItemsSource = new[] { new { Name = "Avery", TeamName = "Engineering", OvertimeRate = 0.25m, OvertimeTurns = 1, CompletedTurns = 4 } };
            table.Visibility = Visibility.Visible;
            ((TextBlock)window.FindName("EmptyOvertimeHistory")).Visibility = Visibility.Collapsed;
            Arrange(root, 640, 440);
            CheckReachable(window, root, rulesViewport, "ApplySettingsButton", "TopmostCheck", "OvertimeTable");
            Console.WriteLine("PASS Meeting controls and rankings are reachable in a small work area");
            CheckTeamQueueIsolation();
            Console.WriteLine("PASS Teams stay separate and Reset restores saved rosters after local queue changes");
            CheckSavedRosterRules();
            Console.WriteLine("PASS Saved-roster dragging and timer overrides carry through to new meetings");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL Layout: " + ex.Message); return 1; }
    }

    private static void Arrange(FrameworkElement root, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
    }

    private static void CheckTeamQueueIsolation()
    {
        string settings = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "teams-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(settings, "{\"Names\":[\"Avery\",\"Morgan\",\"Casey\"]}");
        try
        {
            var window = new MainWindow(settings).Window;
            try
            {
                var tabs = (TabControl)window.FindName("WorkspaceTabs");
                var chooser = (ComboBox)window.FindName("MeetingTeam");
                var teams = (ListBox)window.FindName("TeamsList");
                var roster = (ListBox)window.FindName("RosterList");
                var queue = (ListBox)window.FindName("SpeakerList");
                if (((TeamRoster)chooser.SelectedItem).Name != "Default team")
                    throw new Exception("The existing roster should be selected as the default team.");
                AssertQueue(queue, "Avery", "Morgan", "Casey");

                tabs.SelectedItem = window.FindName("TeamsTab");
                ((TextBox)window.FindName("TeamNameInput")).Text = "Product";
                ClickButton(window, "AddTeamButton");
                ((TextBox)window.FindName("MemberNameInput")).Text = "Robin";
                ClickButton(window, "AddMemberButton");
                tabs.SelectedItem = window.FindName("MeetingTab");
                chooser.SelectedIndex = 1;
                AssertQueue(queue, "Robin");
                chooser.SelectedIndex = 0;
                AssertQueue(queue, "Avery", "Morgan", "Casey");

                ClickButton(window, "StartButton");
                if (chooser.IsEnabled) throw new Exception("The team picker must be locked during a meeting.");
                Arrange((FrameworkElement)window.Content, 1132, 740);
                var moving = (QueueEntry)queue.Items[1];
                var target = (ListBoxItem)queue.ItemContainerGenerator.ContainerFromIndex(2);
                var dragData = new DataObject(typeof(QueueEntry).FullName, moving.Id);
                RaiseDragEvent(target, dragData, DragDrop.PreviewDropEvent);
                AssertQueue(queue, "Avery", "Casey", "Morgan");
                queue.SelectedIndex = 1;
                ClickButton(window, "RemoveButton");
                AssertQueue(queue, "Avery", "Morgan");

                tabs.SelectedItem = window.FindName("TeamsTab");
                teams.SelectedIndex = 0;
                if (!roster.Items.Cast<RosterMember>().Select(member => member.Name).SequenceEqual(new[] { "Avery", "Morgan", "Casey" }))
                    throw new Exception("Meeting-only edits reached the saved roster.");
                roster.SelectedIndex = 1;
                ((TextBox)window.FindName("MemberNameInput")).Text = "Morgan Park";
                ClickButton(window, "SaveMemberButton");
                ClickButton(window, "RemoveMemberButton");
                ((TextBox)window.FindName("MemberNameInput")).Text = "Drew";
                ClickButton(window, "AddMemberButton");
                AssertQueue(queue, "Avery", "Morgan");

                tabs.SelectedItem = window.FindName("MeetingTab");
                ClickButton(window, "NextButton");
                ClickButton(window, "NextButton");
                string notice;
                var saved = new SettingsStore(settings).Load(out notice);
                if (notice != null || !saved.OvertimeHistory.Any(record => record.Name == "Morgan Park"))
                    throw new Exception("Completing a turn should record the speaker's corrected roster name.");
                ClickButton(window, "ResetButton");
                AssertQueue(queue, "Avery", "Casey", "Drew");
                chooser.SelectedIndex = 1;
            }
            finally { window.Close(); }

            var reopened = new MainWindow(settings).Window;
            try { AssertQueue((ListBox)reopened.FindName("SpeakerList"), "Robin"); }
            finally { reopened.Close(); }
        }
        finally { File.Delete(settings); }
    }

    private static void CheckSavedRosterRules()
    {
        string settings = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "roster-rules-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(settings, "{\"Names\":[\"Avery\",\"Morgan\"]}");
        try
        {
            var window = new MainWindow(settings).Window;
            try
            {
                var root = (FrameworkElement)window.Content;
                var tabs = (TabControl)window.FindName("WorkspaceTabs");
                var roster = (ListBox)window.FindName("RosterList");
                var duration = (TextBox)window.FindName("MemberPersonalTimeInput");
                var saveStatus = (TextBlock)window.FindName("MemberSaveStatus");
                var personalTime = (TextBlock)window.FindName("PersonalTime");
                tabs.SelectedItem = window.FindName("TeamsTab");
                Arrange(root, 1132, 740);
                roster.SelectedIndex = 1;
                duration.Text = "30";
                PressEnter(duration);
                root.UpdateLayout();
                var source = (RosterMember)roster.Items[1];
                var target = (ListBoxItem)roster.ItemContainerGenerator.ContainerFromIndex(0);
                if (source.PersonalSeconds != 30 || target == null)
                    throw new Exception("Pressing Enter in Personal time did not save the selected speaker's override.");
                if (!saveStatus.Text.Contains("0:30"))
                    throw new Exception("Confirm the saved duration after pressing Enter.");
                var sourceRow = (ListBoxItem)roster.ItemContainerGenerator.ContainerFromIndex(1);
                var timeLabel = FindDescendant<TextBlock>(sourceRow, label => label.Name == "PersonalOverride");
                if (timeLabel == null || timeLabel.Visibility != Visibility.Visible || timeLabel.Text != "30 s")
                    throw new Exception("The roster should show which speakers have a custom duration.");

                var data = new DataObject(typeof(RosterMember).FullName, source.Id);
                RaiseDragEvent(target, data, DragDrop.PreviewDragOverEvent);
                if (((TextBlock)window.FindName("QueueDragName")).Text != "Morgan" ||
                    ((Border)window.FindName("QueueDropMarker")).Visibility != Visibility.Visible)
                    throw new Exception("The roster drag must show the dragged name and drop position.");
                RaiseDragEvent(target, data, DragDrop.PreviewDropEvent);
                if (roster.Items[0] != source || ((Border)window.FindName("QueueDragPreview")).Visibility == Visibility.Visible)
                    throw new Exception("Dropping must reorder the saved roster and clear feedback.");

                tabs.SelectedItem = window.FindName("MeetingTab");
                AssertQueue((ListBox)window.FindName("SpeakerList"), "Morgan", "Avery");
                if (personalTime.Text != "00:30") throw new Exception("Preview the reordered speaker's custom time.");
                ClickButton(window, "StartButton");
                tabs.SelectedItem = window.FindName("TeamsTab");
                duration.Text = "0:45";
                if (!string.IsNullOrEmpty(saveStatus.Text)) throw new Exception("An edit must clear the previous save confirmation.");
                ClickButton(window, "SaveMemberButton");
                if (personalTime.Text != "00:30") throw new Exception("Roster edits must leave the live timer intact.");
                if (!saveStatus.Text.Contains("0:45") || !saveStatus.Text.Contains("Reset"))
                    throw new Exception("Confirm that the saved time applies after Reset during a meeting.");
                tabs.SelectedItem = window.FindName("MeetingTab");
                ClickButton(window, "NextButton");
                if (personalTime.Text != "01:30") throw new Exception("Use the meeting default for a speaker with no override.");
                ClickButton(window, "NextButton");
                ClickButton(window, "ResetButton");
                if (personalTime.Text != "00:45") throw new Exception("Reset must reload the saved override.");
            }
            finally { window.Close(); }

            var reopened = new MainWindow(settings).Window;
            try
            {
                AssertQueue((ListBox)reopened.FindName("SpeakerList"), "Morgan", "Avery");
                if (((TextBlock)reopened.FindName("PersonalTime")).Text != "00:45")
                    throw new Exception("The custom duration must persist across app launches.");
                var roster = (ListBox)reopened.FindName("RosterList");
                roster.SelectedIndex = 0;
                ((TextBox)reopened.FindName("MemberPersonalTimeInput")).Clear();
                PressEnter((TextBox)reopened.FindName("MemberPersonalTimeInput"));
                if (((TextBlock)reopened.FindName("PersonalTime")).Text != "01:30")
                    throw new Exception("A blank override must restore the default.");
            }
            finally { reopened.Close(); }
        }
        finally { File.Delete(settings); }
    }

    private static void PressEnter(TextBox input)
    {
        using (var source = new HwndSource(new HwndSourceParameters("Roundtime test input") { WindowStyle = 0 }))
        {
            var enter = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            input.RaiseEvent(enter);
        }
    }

    private static void RaiseDragEvent(ListBoxItem target, DataObject data, RoutedEvent routedEvent)
    {
        var dragEvent = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, target, new Point(30, 20) }, null);
        dragEvent.RoutedEvent = routedEvent;
        target.RaiseEvent(dragEvent);
    }

    private static void ClickButton(Window window, string name)
    {
        var button = (Button)window.FindName(name);
        if (!button.IsEnabled) throw new Exception(name + " is disabled.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }

    private static void AssertQueue(ListBox queue, params string[] names)
    {
        if (!queue.Items.Cast<QueueEntry>().Select(entry => entry.Name).SequenceEqual(names))
            throw new Exception("Expected the meeting queue to contain " + string.Join(", ", names) + " in order.");
    }

    private static void CheckQueueScroll(Window window, FrameworkElement root, ScrollViewer viewport, double width, double height)
    {
        var list = (ListBox)window.FindName("SpeakerList");
        var emptyQueue = (FrameworkElement)window.FindName("EmptyQueue");
        emptyQueue.Visibility = Visibility.Collapsed;
        var rows = Enumerable.Range(1, 50).Select(i => new QueueRow { Name = "Speaker " + i }).ToArray();
        list.ItemsSource = rows.Take(1).ToArray();
        Arrange(root, width, height);

        var controls = new[] { "SpeakerList", "PersonalCard", "SharedCard", "StartButton", "RemoveButton" }
            .Select(name => (FrameworkElement)window.FindName(name)).ToArray();
        Func<Rect[]> bounds = () => controls.Select(control =>
            new Rect(control.TranslatePoint(new Point(), root), control.RenderSize)).ToArray();
        var initialBounds = bounds();
        double initialScrollableHeight = viewport.ScrollableHeight;
        double initialOffset = viewport.VerticalOffset;

        list.ItemsSource = rows;
        Arrange(root, width, height);
        if (viewport.ScrollableHeight != initialScrollableHeight || !initialBounds.SequenceEqual(bounds()))
            throw new Exception(string.Format("Adding speakers resized the meeting workspace or moved its controls at {0}x{1}.", width, height));

        var queueViewport = FindDescendant<ScrollViewer>(list, viewer => true);
        if (queueViewport == null || queueViewport.ScrollableHeight <= 0)
            throw new Exception("A long speaker queue must have its own scrollable area.");
        var firstRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(rows.First());
        var nameLabel = firstRow == null ? null : FindDescendant<TextBlock>(firstRow, label => label.Name == "QueueSpeakerName");
        if (nameLabel == null)
            throw new Exception("The first speaker's name was not laid out.");
        double queueOffset = queueViewport.VerticalOffset;
        nameLabel.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent });
        root.UpdateLayout();
        if (queueViewport.VerticalOffset <= queueOffset)
            throw new Exception("The mouse wheel must scroll the queue over a speaker name.");

        queueViewport.ScrollToBottom();
        root.UpdateLayout();
        var lastRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(rows.Last());
        double lastRowTop = lastRow == null ? -1 : lastRow.TranslatePoint(new Point(), queueViewport).Y;
        if (lastRowTop < 0 || lastRowTop + lastRow.ActualHeight > queueViewport.ActualHeight + 1)
            throw new Exception("The last speaker could not be scrolled into view inside the queue.");
        if (viewport.VerticalOffset != initialOffset || !initialBounds.SequenceEqual(bounds()))
            throw new Exception("Scrolling the speaker queue moved the meeting workspace.");

        list.ItemsSource = null;
        emptyQueue.Visibility = Visibility.Visible;
        root.UpdateLayout();
    }

    private static T FindDescendant<T>(DependencyObject element, Predicate<T> matches) where T : DependencyObject
    {
        var candidate = element as T;
        if (candidate != null && matches(candidate)) return candidate;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            candidate = FindDescendant(VisualTreeHelper.GetChild(element, i), matches);
            if (candidate != null) return candidate;
        }
        return null;
    }

    private static void CheckReachable(Window window, FrameworkElement root, ScrollViewer viewport, params string[] names)
    {
        var presenter = (ScrollContentPresenter)viewport.Template.FindName("PART_ScrollContentPresenter", viewport);
        foreach (string name in names)
        {
            var control = (FrameworkElement)window.FindName(name);
            Point position = control.TransformToAncestor(presenter).Transform(new Point(0, 0));
            viewport.ScrollToHorizontalOffset(viewport.HorizontalOffset + position.X - 12);
            viewport.ScrollToVerticalOffset(viewport.VerticalOffset + position.Y - 12);
            root.UpdateLayout();
            position = control.TransformToAncestor(presenter).Transform(new Point(0, 0));
            if (position.X < -1 || position.Y < -1 || position.X + control.ActualWidth > viewport.ViewportWidth + 1 || position.Y + control.ActualHeight > viewport.ViewportHeight + 1)
                throw new Exception(name + " cannot be fully scrolled into view.");
        }
    }
}
