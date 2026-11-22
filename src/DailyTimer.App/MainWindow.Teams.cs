using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using DailyTimer.Core;

namespace DailyTimer.App
{
    internal sealed partial class MainWindow
    {
        private bool refreshingTeams;
        private bool queueEdited;
        private Dictionary<Guid, RosterMember> meetingRosterMembers;

        private TeamRoster MeetingTeam
        {
            get { return configuration.Teams.FirstOrDefault(team => team.Id == configuration.SelectedTeamId); }
        }

        private TeamRoster EditingTeam { get { return Control<ListBox>("TeamsList").SelectedItem as TeamRoster; } }
        private RosterMember SelectedMember { get { return Control<ListBox>("RosterList").SelectedItem as RosterMember; } }
        private bool CanSwitchTeams
        {
            get { return meeting == null || meeting.State == MeetingState.Ready || meeting.State == MeetingState.Finished; }
        }

        private void WireTeamControls(SpeakerDragFeedback dragFeedback)
        {
            Control<ComboBox>("MeetingTeam").SelectionChanged += delegate {
                if (refreshingTeams || !CanSwitchTeams) return;
                var team = Control<ComboBox>("MeetingTeam").SelectedItem as TeamRoster;
                if (team == null || team.Id == configuration.SelectedTeamId) return;
                configuration.SelectedTeamId = team.Id;
                LoadMeetingRoster();
                Save(true);
            };
            Control<ListBox>("TeamsList").SelectionChanged += delegate {
                if (!refreshingTeams) ShowTeamForEditing();
            };
            var roster = Control<ListBox>("RosterList");
            roster.SelectionChanged += delegate { UpdateRosterControls(); };
            new SpeakerListDrag<RosterMember>(roster, dragFeedback, member => member.Id, member => member.Name,
                member => true, (source, target) => MoveMember(source, roster.Items.IndexOf(target)));
            Control<Button>("AddTeamButton").Click += delegate { SaveTeam(false); };
            Control<Button>("RenameTeamButton").Click += delegate { SaveTeam(true); };
            Control<Button>("DeleteTeamButton").Click += delegate { DeleteTeam(); };
            Control<Button>("AddMemberButton").Click += delegate { SaveMember(false); };
            Control<Button>("SaveMemberButton").Click += delegate { SaveMember(true); };
            var name = Control<TextBox>("MemberNameInput");
            var duration = Control<TextBox>("MemberPersonalTimeInput");
            var status = Control<TextBlock>("MemberSaveStatus");
            name.PreviewKeyDown += SaveSelectedMemberOnEnter;
            duration.PreviewKeyDown += SaveSelectedMemberOnEnter;
            name.TextChanged += delegate { status.Text = ""; };
            duration.TextChanged += delegate { status.Text = ""; };
            Control<Button>("RemoveMemberButton").Click += delegate { RemoveMember(); };
            Control<Button>("MoveMemberUpButton").Click += delegate { MoveMember(SelectedMember, roster.SelectedIndex - 1); };
            Control<Button>("MoveMemberDownButton").Click += delegate { MoveMember(SelectedMember, roster.SelectedIndex + 1); };
        }

        private void LoadMeetingRoster()
        {
            var team = MeetingTeam;
            var roster = team == null ? Enumerable.Empty<RosterMember>() : team.Members;
            meetingRosterMembers = roster.ToDictionary(member => member.Id);
            meeting = new Meeting(() => clock.Elapsed.TotalSeconds,
                roster.Select(member => new Speaker(member.Id, member.Name, member.PersonalSeconds)), configuration.Settings);
            meeting.Alert += delegate(AlertKind kind) {
                if (configuration.Settings.SoundEnabled && !sounds.Play(kind))
                    ShowNotice("The alert sound could not be played. The timers are still running.");
            };
            meeting.TurnCompleted += RecordTurn;
            queueEdited = false;
            list.SelectedIndex = -1;
            RebuildQueue(null);
            var viewport = FindScrollViewer(list);
            if (viewport != null) viewport.ScrollToTop();
            Render();
        }

        private void ChangeMeetingQueue(Action action)
        {
            queueEdited = true;
            Act(action);
        }

        private void RefreshTeamLists(TeamRoster teamForEditing)
        {
            var teams = configuration.Teams.ToArray();
            var selected = MeetingTeam ?? teams.FirstOrDefault();
            configuration.SelectedTeamId = selected == null ? (Guid?)null : selected.Id;

            var chooser = Control<ComboBox>("MeetingTeam");
            var editor = Control<ListBox>("TeamsList");
            refreshingTeams = true;
            try
            {
                chooser.ItemsSource = teams;
                chooser.SelectedItem = selected;
                editor.ItemsSource = teams;
                editor.SelectedItem = teamForEditing ?? selected;
            }
            finally { refreshingTeams = false; }
            ShowTeamForEditing();
        }

        private void ShowTeamForEditing()
        {
            var team = EditingTeam;
            Control<TextBox>("TeamNameInput").Text = team == null ? "" : team.Name;
            Control<Button>("RenameTeamButton").IsEnabled = team != null;
            UpdateTeamDeleteButton();
            RefreshRoster(null);
        }

        private void UpdateTeamDeleteButton()
        {
            var team = EditingTeam;
            Control<Button>("DeleteTeamButton").IsEnabled = team != null &&
                (team.Id != configuration.SelectedTeamId || CanSwitchTeams);
        }

        private void RefreshRoster(RosterMember selected)
        {
            var team = EditingTeam;
            Control<TextBlock>("RosterHeading").Text = team == null ? "Choose a team" : team.Name;
            var roster = Control<ListBox>("RosterList");
            roster.ItemsSource = team == null ? null : team.Members.ToArray();
            roster.SelectedItem = selected;
            UpdateRosterControls();
        }

        private void UpdateRosterControls()
        {
            var team = EditingTeam;
            var member = SelectedMember;
            int index = team == null ? -1 : team.Members.IndexOf(member);
            var input = Control<TextBox>("MemberNameInput");
            input.IsEnabled = team != null;
            input.Text = member == null ? "" : member.Name;
            var duration = Control<TextBox>("MemberPersonalTimeInput");
            duration.IsEnabled = team != null;
            duration.Text = member != null && member.PersonalSeconds.HasValue ? FormatSetting(member.PersonalSeconds.Value) : "";
            Control<TextBlock>("MemberSaveStatus").Text = "";
            Control<Button>("AddMemberButton").IsEnabled = team != null;
            Control<Button>("SaveMemberButton").IsEnabled = index >= 0;
            Control<Button>("RemoveMemberButton").IsEnabled = index >= 0;
            Control<Button>("MoveMemberUpButton").IsEnabled = index > 0;
            Control<Button>("MoveMemberDownButton").IsEnabled = index >= 0 && index < team.Members.Count - 1;
        }

        private void SaveSelectedMemberOnEnter(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || SelectedMember == null) return;
            e.Handled = true;
            SaveMember(true);
        }

        private string ReadRosterName(TextBox input)
        {
            try { return Speaker.ValidateName(input.Text); }
            catch (ArgumentException ex) { ShowNotice(ex.Message); input.Focus(); return null; }
        }

        private void SaveTeam(bool rename)
        {
            var team = rename ? EditingTeam : null;
            if (rename && team == null) return;
            var input = Control<TextBox>("TeamNameInput");
            string name = ReadRosterName(input);
            if (name == null) return;
            if (configuration.Teams.Any(other => other != team && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                ShowNotice("A team with that name already exists.");
                input.Focus();
                return;
            }
            if (team == null)
            {
                team = new TeamRoster { Id = Guid.NewGuid(), Name = name };
                configuration.Teams.Add(team);
            }
            else team.Name = name;
            SaveTeamList(team);
        }

        private void DeleteTeam()
        {
            var team = EditingTeam;
            if (team == null || (team.Id == configuration.SelectedTeamId && !CanSwitchTeams)) return;
            if (!Confirm("Delete " + team.Name + " and its saved roster? Completed turn history will be kept.", "Delete team")) return;
            foreach (var record in configuration.OvertimeHistory.Where(record => record.TeamId == team.Id))
                record.TeamName = team.Name;
            configuration.Teams.Remove(team);
            SaveTeamList(null);
        }

        private void SaveTeamList(TeamRoster selectedForEditing)
        {
            var previousId = configuration.SelectedTeamId;
            RefreshTeamLists(selectedForEditing);
            if (configuration.SelectedTeamId != previousId) LoadMeetingRoster();
            RenderOvertimeHistory();
            Save(true);
        }

        private void SaveMember(bool update)
        {
            var team = EditingTeam;
            var member = update ? SelectedMember : null;
            if (team == null || (update && member == null)) return;
            var input = Control<TextBox>("MemberNameInput");
            string name = ReadRosterName(input);
            if (name == null) return;
            var duration = Control<TextBox>("MemberPersonalTimeInput");
            int? personalSeconds = null;
            if (!string.IsNullOrWhiteSpace(duration.Text))
            {
                double seconds;
                if (!TryDuration(duration.Text, out seconds, true) || !MeetingSettings.IsValidPersonalDuration(seconds))
                {
                    ShowNotice("Enter a personal time from 1 to 3600 seconds or 0:01 to 60:00. Leave it blank for the default.");
                    duration.Focus();
                    return;
                }
                personalSeconds = (int)seconds;
            }
            if (member == null)
            {
                member = new RosterMember { Id = FindReturningSpeaker(team, name) ?? Guid.NewGuid() };
                team.Members.Add(member);
            }
            member.Name = name;
            member.PersonalSeconds = personalSeconds;
            var record = configuration.OvertimeHistory.Find(item => item.SpeakerId == member.Id);
            if (record != null) record.Name = name;
            RenderOvertimeHistory();
            if (SaveRoster(team, member))
            {
                string message = personalSeconds.HasValue ? "Saved " + FormatSetting(personalSeconds.Value) + "." : "Saved the meeting default.";
                if (team.Id == configuration.SelectedTeamId && (meeting.HasStarted || queueEdited))
                    message += " Applies after Reset.";
                Control<TextBlock>("MemberSaveStatus").Text = message;
            }
            if (!update)
            {
                input.Focus();
                input.SelectAll();
            }
        }

        private Guid? FindReturningSpeaker(TeamRoster team, string name)
        {
            if (team.Members.Any(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))) return null;
            var matches = configuration.OvertimeHistory.Where(record => record.TeamId == team.Id &&
                string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            if (matches.Count != 1) return null;
            var id = matches[0].SpeakerId;
            return configuration.Teams.Any(other => other.Members.Any(member => member.Id == id)) ? (Guid?)null : id;
        }

        private void RemoveMember()
        {
            var team = EditingTeam;
            var member = SelectedMember;
            if (team == null || member == null) return;
            team.Members.Remove(member);
            SaveRoster(team, null);
        }

        private void MoveMember(RosterMember member, int destination)
        {
            var team = EditingTeam;
            if (team == null || member == null) return;
            int index = team.Members.IndexOf(member);
            if (index < 0 || destination == index || destination < 0 || destination >= team.Members.Count) return;
            team.Members.RemoveAt(index);
            team.Members.Insert(destination, member);
            SaveRoster(team, member);
        }

        private bool SaveRoster(TeamRoster team, RosterMember selected)
        {
            RefreshRoster(selected);
            if (team.Id == configuration.SelectedTeamId && meeting.State == MeetingState.Ready && !queueEdited)
                LoadMeetingRoster();
            else if (team.Id == configuration.SelectedTeamId)
                UpdateEmptyQueueHint();
            return Save(true);
        }
    }
}
