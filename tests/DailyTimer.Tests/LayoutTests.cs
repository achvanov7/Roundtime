using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

internal static class LayoutTests
{
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
            var rulesTab = (TabItem)window.FindName("RulesTab");
            Arrange(root, 640, 440);
            if (tabs == null || meetingTab == null || rulesTab == null || tabs.Items.Count != 2 || tabs.SelectedItem != meetingTab)
                throw new Exception("The meeting must open in its own tab alongside Rules & rankings.");
            var meetingViewport = (ScrollViewer)meetingTab.Content;
            CheckReachable(window, root, meetingViewport, "StartButton", "DiscussButton", "NextButton");

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
            table.ItemsSource = new[] { new { Name = "Avery", OvertimeRate = 0.25m, OvertimeTurns = 1, CompletedTurns = 4 } };
            table.Visibility = Visibility.Visible;
            ((TextBlock)window.FindName("EmptyOvertimeHistory")).Visibility = Visibility.Collapsed;
            Arrange(root, 640, 440);
            CheckReachable(window, root, rulesViewport, "ApplySettingsButton", "TopmostCheck", "OvertimeTable");
            Console.WriteLine("PASS Both tabs keep their controls and rankings reachable in a small work area");
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

    private static void CheckReachable(Window window, FrameworkElement root, ScrollViewer viewport, params string[] names)
    {
        foreach (string name in names)
        {
            var control = (FrameworkElement)window.FindName(name);
            Point position = control.TransformToAncestor(viewport).Transform(new Point(0, 0));
            viewport.ScrollToHorizontalOffset(viewport.HorizontalOffset + position.X - 12);
            viewport.ScrollToVerticalOffset(viewport.VerticalOffset + position.Y - 12);
            root.UpdateLayout();
            position = control.TransformToAncestor(viewport).Transform(new Point(0, 0));
            if (position.X < -1 || position.Y < -1 || position.X + control.ActualWidth > viewport.ViewportWidth + 1 || position.Y + control.ActualHeight > viewport.ViewportHeight + 1)
                throw new Exception(name + " cannot be fully scrolled into view.");
        }
    }
}
