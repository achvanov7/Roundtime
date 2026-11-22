using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Roundtime.Core;

namespace Roundtime.App
{
    internal sealed partial class MainWindow
    {
        private readonly DispatcherTimer preferencesSave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        private ResourceDictionary themePalette;

        private void WirePreferences()
        {
            var sound = Control<ComboBox>("OvertimeSoundPicker");
            var theme = Control<ComboBox>("ThemePicker");
            var volume = Control<Slider>("VolumeSlider");
            var enabled = Control<CheckBox>("SoundCheck");
            sound.ItemsSource = new[] { "Classic", "Glass chime", "Morning alarm" };
            theme.ItemsSource = new[] { "Classic", "Forest", "Plum" };
            sound.SelectedIndex = (int)configuration.OvertimeSound;
            theme.SelectedIndex = (int)configuration.Theme;
            volume.Value = configuration.AlertVolume;
            enabled.IsChecked = configuration.Settings.SoundEnabled;
            ApplyTheme();

            sound.SelectionChanged += delegate {
                if (sound.SelectedIndex < 0) return;
                configuration.OvertimeSound = (OvertimeSound)sound.SelectedIndex;
                SchedulePreferenceSave();
            };
            theme.SelectionChanged += delegate {
                if (theme.SelectedIndex < 0) return;
                configuration.Theme = (ColorTheme)theme.SelectedIndex;
                ApplyTheme();
                SchedulePreferenceSave();
            };
            enabled.Checked += delegate { SetSoundEnabled(true); };
            enabled.Unchecked += delegate { SetSoundEnabled(false); };
            Control<Button>("TestSoundButton").Click += delegate { PlayAlert(AlertKind.PersonalExpired); };

            volume.ValueChanged += delegate {
                configuration.AlertVolume = (int)volume.Value;
                if (configuration.AlertVolume == 0) sounds.Stop();
                SchedulePreferenceSave();
            };
            preferencesSave.Tick += delegate { Save(false); };
        }

        private void SchedulePreferenceSave()
        {
            canPersist = true;
            preferencesSave.Stop();
            preferencesSave.Start();
        }

        private void ApplyTheme()
        {
            var resources = Window.Resources;
            var nextPalette = configuration.Theme == ColorTheme.Classic ? null
                : (ResourceDictionary)resources[configuration.Theme + "Palette"];
            if (themePalette != null) resources.MergedDictionaries.Remove(themePalette);
            themePalette = nextPalette;
            if (themePalette != null) resources.MergedDictionaries.Add(themePalette);
        }

        private void SetSoundEnabled(bool enabled)
        {
            configuration.Settings.SoundEnabled = enabled;
            if (!enabled) sounds.Stop();
            SchedulePreferenceSave();
        }

        private void PlayAlert(AlertKind kind)
        {
            if (!sounds.Play(kind, configuration.OvertimeSound, configuration.AlertVolume))
                ShowNotice("The alert sound could not be played. Check your Windows audio output.");
        }
    }
}
