using System;
using System.IO;
using System.Media;
using Roundtime.Core;

namespace Roundtime.App
{
    internal sealed class SoundService : IDisposable
    {
        private readonly SoundPlayer player = new SoundPlayer();
        private MemoryStream stream;

        public bool Play(AlertKind kind, OvertimeSound sound, int volume)
        {
            Stop();
            if (volume == 0) return true;
            try
            {
                stream = CreateAlert(kind, sound, volume);
                player.Stream = stream;
                // Short sounds play asynchronously; they never block the meeting clock.
                player.Play();
                return true;
            }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }

        public void Stop()
        {
            if (stream == null) return;
            player.Stop();
            player.Stream = null;
            stream.Dispose();
            stream = null;
        }

        internal static MemoryStream CreateAlert(AlertKind kind, OvertimeSound sound, int volume)
        {
            double gain = volume / 100.0;
            if (kind == AlertKind.SharedExpired)
                return CreateTone(new[] { 440.0, 440.0, 330.0 }, 0.22, gain);

            switch (sound)
            {
                case OvertimeSound.GlassChime:
                    return CreateTone(new[] { 523.25, 783.99, 1046.5 }, 0.2, gain * 0.65);
                case OvertimeSound.MorningAlarm:
                    return CreateTone(new[] { 950.0, 1250.0, 950.0, 1250.0, 950.0, 1250.0, 950.0, 1250.0 },
                        0.14, gain, buzzy: true);
                default:
                    return CreateTone(new[] { 660.0, 880.0 }, 0.16, gain);
            }
        }

        private static MemoryStream CreateTone(double[] notes, double seconds, double gain, bool buzzy = false)
        {
            const int rate = 22050;
            int noteSamples = (int)(rate * seconds);
            int gapSamples = (int)(rate * 0.06);
            int total = notes.Length * (noteSamples + gapSamples);
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);
            writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            writer.Write(36 + total * 2);
            writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); writer.Write(total * 2);
            foreach (double frequency in notes)
            {
                for (int i = 0; i < noteSamples; i++)
                {
                    double envelope = Math.Min(1, i / (rate * 0.012)) * Math.Min(1, (noteSamples - i) / (rate * 0.04));
                    double phase = i * 2 * Math.PI * frequency / rate;
                    double sample = Math.Sin(phase);
                    if (buzzy) sample = 0.65 * sample + 0.25 * Math.Sin(3 * phase) + 0.1 * Math.Sin(5 * phase);
                    writer.Write((short)(sample * 6500 * envelope * gain));
                }
                for (int i = 0; i < gapSamples; i++) writer.Write((short)0);
            }
            writer.Flush(); stream.Position = 0;
            return stream;
        }

        public void Dispose()
        {
            Stop();
            player.Dispose();
        }
    }
}
