using System;
using System.Collections.Generic;
using System.Linq;
using DailyTimer.Core;

internal static class Program
{
    private sealed class Clock { public double Now; }
    private static int passed;
    private static int failed;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Near(double expected, double actual)
    {
        Check(Math.Abs(expected - actual) < 0.00001, "Expected " + expected + ", got " + actual);
    }
    private static void Test(string name, Action body)
    {
        try { body(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static Meeting Create(Clock c, params string[] names)
    {
        return new Meeting(() => c.Now, names.Select(n => new Speaker(n)), new MeetingSettings());
    }

    private static int Main()
    {
        Test("Defaults are paused 90 and 600 seconds", () => {
            var m = Create(new Clock(), "A");
            Check(m.State == MeetingState.Ready && m.ActiveSpeaker == null, "Ready state");
            Near(90, m.PersonalRemaining); Near(600, m.SharedRemaining);
        });
        Test("Empty roster cannot start", () => {
            var m = Create(new Clock()); m.StartOrTogglePause();
            Check(m.State == MeetingState.Ready, "Empty start must be ignored");
        });
        Test("Personal expiry starts discussion and alerts once", () => {
            var c = new Clock(); var m = Create(c, "A"); var alerts = new List<AlertKind>();
            m.Alert += alerts.Add; m.StartOrTogglePause(); c.Now = 90; m.Tick(); m.Tick();
            Check(m.State == MeetingState.Discussion, "Discussion expected");
            Near(0, m.PersonalRemaining); Near(600, m.SharedRemaining);
            Check(alerts.SequenceEqual(new[] { AlertKind.PersonalExpired }), "Single alert");
        });
        Test("Each speaker gets their own allowance or the meeting default", () => {
            var c = new Clock();
            var first = new Speaker(Guid.NewGuid(), "A", 30);
            var second = new Speaker("B");
            var third = new Speaker(Guid.NewGuid(), "C", 45);
            var m = new Meeting(() => c.Now, new[] { first, second, third }, new MeetingSettings());
            Near(30, m.PersonalRemaining);
            m.StartOrTogglePause();
            Near(30, m.PersonalAllowance);
            c.Now = 35; m.Tick();
            Check(m.State == MeetingState.Discussion, "The custom deadline starts shared discussion");
            Near(0, m.PersonalRemaining); Near(595, m.SharedRemaining);
            m.Next();
            Check(m.ActiveSpeaker == second, "Advance to the next speaker");
            Near(90, m.PersonalRemaining); Near(90, m.PersonalAllowance);
            m.StartOrTogglePause(); m.Next();
            Check(m.State == MeetingState.Paused && m.ActiveSpeaker == third, "Advance while paused");
            Near(45, m.PersonalRemaining); Near(45, m.PersonalAllowance);
            Near(595, m.SharedRemaining);
        });
        Test("The ready timer previews the first speaker's allowance after queue edits", () => {
            var c = new Clock();
            var first = new Speaker(Guid.NewGuid(), "A", 30);
            var second = new Speaker("B");
            var m = new Meeting(() => c.Now, new[] { first, second }, new MeetingSettings());
            m.Move(second.Id, 0);
            Near(90, m.PersonalRemaining);
            m.Remove(second.Id);
            Near(30, m.PersonalRemaining);
            m.StartOrTogglePause();
            Near(30, m.PersonalRemaining);
            c.Now = 10; m.Discuss();
            c.Now = 15; m.ReturnToSpeaker();
            Near(20, m.PersonalRemaining); Near(30, m.PersonalAllowance); Near(595, m.SharedRemaining);
            m.Reset();
            Near(30, m.PersonalRemaining);
        });
        Test("Delayed refresh consumes personal remainder then shared remainder", () => {
            var c = new Clock(); var m = Create(c, "A"); m.StartOrTogglePause();
            c.Now = 105.25; m.Tick(); Near(584.75, m.SharedRemaining);
        });
        Test("Early discussion retains unused personal time", () => {
            var c = new Clock(); var m = Create(c, "A"); m.StartOrTogglePause();
            c.Now = 20; m.Discuss(); c.Now = 35; m.Tick();
            Near(70, m.PersonalRemaining); Near(585, m.SharedRemaining);
        });
        Test("Returning from discussion resumes the same speaker with both balances intact", () => {
            var c = new Clock(); var m = Create(c, "A", "B"); m.StartOrTogglePause();
            var speaker = m.ActiveSpeaker;
            c.Now = 20; m.Discuss();
            Check(m.CanReturnToSpeaker, "Early discussion should allow a return");
            c.Now = 35; m.ReturnToSpeaker();
            Check(m.State == MeetingState.Speaking && m.ActiveSpeaker == speaker && !speaker.Completed, "Resume the original turn");
            Near(70, m.PersonalRemaining); Near(585, m.SharedRemaining);
            Check(!m.CanReturnToSpeaker, "Already speaking");
            c.Now = 45; m.Discuss(); c.Now = 50; m.Tick();
            Near(60, m.PersonalRemaining); Near(580, m.SharedRemaining);
        });
        Test("Returning to the speaker from paused discussion leaves the timers paused", () => {
            var c = new Clock(); var m = Create(c, "A"); m.StartOrTogglePause();
            c.Now = 20; m.Discuss(); c.Now = 30; m.StartOrTogglePause();
            Check(m.CanReturnToSpeaker, "Paused discussion with personal time left can return");
            c.Now = 100; m.ReturnToSpeaker(); c.Now = 120; m.Tick();
            Check(m.State == MeetingState.Paused && m.Phase == MeetingState.Speaking, "Stay paused in the speaking phase");
            Near(70, m.PersonalRemaining); Near(590, m.SharedRemaining);
            m.StartOrTogglePause(); c.Now = 125; m.Tick();
            Near(65, m.PersonalRemaining); Near(590, m.SharedRemaining);
        });
        Test("Discussion cannot return to a speaker whose personal time expired", () => {
            var c = new Clock(); var m = Create(c, "A"); m.StartOrTogglePause();
            c.Now = 90; m.Tick();
            Check(!m.CanReturnToSpeaker, "An exhausted turn cannot resume");
            c.Now = 95; m.ReturnToSpeaker();
            Check(m.State == MeetingState.Discussion, "Continue discussion after personal expiry");
            Near(0, m.PersonalRemaining); Near(595, m.SharedRemaining);
        });
        Test("Only personal timer expiry counts as overtime on a completed turn", () => {
            var c = new Clock(); var m = Create(c, "Same name", "Same name");
            var results = new Dictionary<Guid, bool>();
            m.TurnCompleted += (s, overtime) => results.Add(s.Id, overtime);
            m.StartOrTogglePause();
            c.Now = 90; m.Next();
            c.Now = 100; m.Discuss();
            c.Now = 1000; m.Next(); m.Next();
            Check(results.Count == 2, "Record each completed turn once");
            Check(results[m.Speakers[0].Id], "Next at the personal deadline must record expiry");
            Check(!results[m.Speakers[1].Id], "Early discussion remains on time even when the shared timer expires");
        });
        Test("Removing or resetting an unfinished turn discards its overtime", () => {
            var c = new Clock(); var m = Create(c, "A", "B", "C");
            var results = new List<bool>();
            m.TurnCompleted += (s, overtime) => results.Add(overtime);
            m.StartOrTogglePause(); c.Now = 91; m.Remove(m.ActiveSpeaker.Id);
            c.Now = 182; m.Tick(); m.Reset();
            Check(results.Count == 0, "Discard removed and reset turns");
            m.StartOrTogglePause(); m.Next();
            Check(results.SequenceEqual(new[] { false }), "A new turn starts on time");
        });
        Test("Next preserves shared balance and starts fresh personal allowance", () => {
            var c = new Clock(); var m = Create(c, "A", "B"); m.StartOrTogglePause();
            c.Now = 100; m.Next(); Check(m.ActiveSpeaker.Name == "B", "B must speak");
            Near(90, m.PersonalRemaining); Near(590, m.SharedRemaining);
            c.Now = 110; m.Tick(); Near(80, m.PersonalRemaining); Near(590, m.SharedRemaining);
        });
        Test("Early next does not transfer unused personal allowance", () => {
            var c = new Clock(); var m = Create(c, "A", "B"); m.StartOrTogglePause();
            c.Now = 30; m.Next(); Near(90, m.PersonalRemaining); Near(600, m.SharedRemaining);
            Check(m.Speakers[0].Completed, "First speaker marked complete");
        });
        Test("Pause freezes speaking and resume excludes paused time", () => {
            var c = new Clock(); var m = Create(c, "A"); m.StartOrTogglePause();
            c.Now = 10; m.StartOrTogglePause(); c.Now = 110; m.Tick(); Near(80, m.PersonalRemaining);
            m.StartOrTogglePause(); c.Now = 115; m.Tick(); Near(75, m.PersonalRemaining);
        });
        Test("Discussion pause resumes same phase", () => {
            var c = new Clock(); var m = Create(c, "A"); m.StartOrTogglePause(); m.Discuss();
            c.Now = 10; m.StartOrTogglePause(); c.Now = 100; m.StartOrTogglePause();
            Check(m.State == MeetingState.Discussion, "Must resume discussion");
            c.Now = 105; m.Tick(); Near(585, m.SharedRemaining);
        });
        Test("Next while paused stays paused with fresh speaking slot", () => {
            var c = new Clock(); var m = Create(c, "A", "B"); m.StartOrTogglePause(); m.Discuss();
            c.Now = 10; m.StartOrTogglePause(); m.Next(); c.Now = 80; m.Tick();
            Check(m.State == MeetingState.Paused && m.ActiveSpeaker.Name == "B", "Paused B");
            Near(90, m.PersonalRemaining); Near(590, m.SharedRemaining);
            m.StartOrTogglePause(); Check(m.State == MeetingState.Speaking, "Resume new slot");
        });
        Test("One stalled update can exhaust both budgets; alerts are single", () => {
            var c = new Clock(); var m = Create(c, "A", "B"); var alerts = new List<AlertKind>();
            m.Alert += alerts.Add; m.StartOrTogglePause(); c.Now = 700; m.Tick(); m.Tick();
            Near(-10, m.SharedRemaining);
            Check(alerts.SequenceEqual(new[] { AlertKind.PersonalExpired, AlertKind.SharedExpired }), "Both alerts");
            m.Next(); c.Now = 800; m.Tick(); Near(-20, m.SharedRemaining);
            Check(alerts.Count(a => a == AlertKind.SharedExpired) == 1, "Shared alerts once per meeting");
        });
        Test("Final discussion remains until finish action", () => {
            var c = new Clock(); var m = Create(c, "A"); m.StartOrTogglePause(); m.Discuss();
            c.Now = 5; m.Tick(); Check(m.State == MeetingState.Discussion, "Still discussing");
            m.Next(); c.Now = 100; m.Tick(); Near(595, m.SharedRemaining);
            Check(m.State == MeetingState.Finished, "Finished");
        });
        Test("Reset restores budgets roster and alert eligibility", () => {
            var c = new Clock(); var m = Create(c, "A", "B"); int alerts = 0; m.Alert += a => alerts++;
            m.StartOrTogglePause(); c.Now = 700; m.Next(); m.Reset();
            Near(90, m.PersonalRemaining); Near(600, m.SharedRemaining);
            Check(m.State == MeetingState.Ready && m.Speakers.All(s => !s.Completed), "Reset flags");
            m.StartOrTogglePause(); c.Now = 790; m.Tick(); Check(alerts == 3, "Alerts eligible again");
        });
        Test("Configured durations apply on reset without changing a live slot", () => {
            var c = new Clock(); var m = Create(c, "A", "B"); m.StartOrTogglePause();
            c.Now = 10; m.Configure(new MeetingSettings { PersonalSeconds = 30, SharedSeconds = 60 });
            Near(80, m.PersonalRemaining); m.Next(); Near(90, m.PersonalRemaining);
            m.Reset(); Near(30, m.PersonalRemaining); Near(60, m.SharedRemaining);
        });
        Test("Rename and reorder preserve active identity and time", () => {
            var c = new Clock(); var m = Create(c, "A", "B", "C"); m.StartOrTogglePause();
            var active = m.ActiveSpeaker; m.Rename(active.Id, "New A"); m.Move(m.Speakers[2].Id, 1);
            c.Now = 10; m.Tick(); Check(m.ActiveSpeaker == active, "Stable identity");
            Near(80, m.PersonalRemaining); m.Next(); Check(m.ActiveSpeaker.Name == "C", "Moved order");
        });
        Test("Shuffle never moves active or completed speakers", () => {
            var m = Create(new Clock(), "A", "B", "C", "D", "E"); m.StartOrTogglePause(); m.Next();
            var active = m.ActiveSpeaker; var ids = m.Speakers.Select(s => s.Id).OrderBy(x => x).ToArray();
            for (int i = 0; i < 20; i++) m.ShuffleRemaining();
            Check(m.Speakers[0].Name == "A" && m.Speakers[1] == active, "Pinned completed and active");
            Check(ids.SequenceEqual(m.Speakers.Select(s => s.Id).OrderBy(x => x)), "No losses");
        });
        Test("Removing waiting person and adding late speaker preserves live clocks", () => {
            var c = new Clock(); var m = Create(c, "A", "B"); m.StartOrTogglePause();
            m.Remove(m.Speakers[1].Id); m.Add("C"); c.Now = 15; m.Tick(); Near(75, m.PersonalRemaining);
            m.Next(); Check(m.ActiveSpeaker.Name == "C", "Late joiner is next");
        });
        Test("Removing active speaker advances while preserving shared balance and pause", () => {
            var c = new Clock(); var m = Create(c, "A", "B"); m.StartOrTogglePause();
            c.Now = 100; m.StartOrTogglePause(); m.Remove(m.ActiveSpeaker.Id);
            Check(m.ActiveSpeaker.Name == "B" && m.State == MeetingState.Paused, "Paused new speaker");
            Near(590, m.SharedRemaining); Near(90, m.PersonalRemaining);
        });
        Test("Removing sole active speaker finishes safely", () => {
            var m = Create(new Clock(), "A"); m.StartOrTogglePause(); m.Remove(m.ActiveSpeaker.Id);
            Check(m.State == MeetingState.Finished && m.ActiveSpeaker == null, "Finish empty meeting");
            m.Add("B"); m.Reset(); m.StartOrTogglePause(); Check(m.ActiveSpeaker.Name == "B", "Restart");
        });
        Test("Duplicate names have independent identities", () => {
            var m = Create(new Clock(), "A", "A"); Check(m.Speakers[0].Id != m.Speakers[1].Id, "Unique IDs");
            m.StartOrTogglePause(); var id = m.ActiveSpeaker.Id; m.Next();
            Check(m.ActiveSpeaker.Id != id, "Advance to other A");
        });
        Test("Invalid names and durations are rejected", () => {
            var m = Create(new Clock()); bool rejected = false;
            try { m.Add("  "); } catch (ArgumentException) { rejected = true; }
            Check(rejected && m.Speakers.Count == 0, "Empty names rejected");
            rejected = false;
            try { m.Configure(new MeetingSettings { PersonalSeconds = double.NaN }); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "NaN rejected");
        });
        Test("Repeated discussion action does not reset shared time", () => {
            var c = new Clock(); var m = Create(c, "A"); m.StartOrTogglePause(); m.Discuss();
            c.Now = 12; m.Discuss(); Near(588, m.SharedRemaining);
        });
        Test("No time is charged before start or after finish", () => {
            var c = new Clock(); var m = Create(c, "A"); c.Now = 1000; m.Tick();
            Near(90, m.PersonalRemaining); m.StartOrTogglePause(); c.Now = 1010; m.Next();
            c.Now = 5000; m.Tick(); Near(600, m.SharedRemaining);
        });
        PersistenceTests.Run(Test);
        Console.WriteLine("\n" + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
