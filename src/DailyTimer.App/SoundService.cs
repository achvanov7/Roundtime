using System;
using System.IO;
using System.Media;
using DailyTimer.Core;

namespace DailyTimer.App
{
    internal sealed class SoundService : IDisposable
    {
        private readonly MemoryStream personalStream;
        private readonly MemoryStream sharedStream;
        private readonly SoundPlayer personal;
        private readonly SoundPlayer shared;

        public SoundService()
        {
            personalStream = CreateTone(new[] { 660.0, 880.0 }, 0.16);
            sharedStream = CreateTone(new[] { 440.0, 440.0, 330.0 }, 0.22);
            personal = new SoundPlayer(personalStream);
            shared = new SoundPlayer(sharedStream);
        }

        public bool Play(AlertKind kind)
        {
            try
            {
                // Short sounds play asynchronously; they never block the meeting clock.
                (kind == AlertKind.PersonalExpired ? personal : shared).Play();
                return true;
            }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }

        private static MemoryStream CreateTone(double[] notes, double seconds)
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
                    writer.Write((short)(Math.Sin(i * 2 * Math.PI * frequency / rate) * 6500 * envelope));
                }
                for (int i = 0; i < gapSamples; i++) writer.Write((short)0);
            }
            writer.Flush(); stream.Position = 0;
            return stream;
        }

        public void Dispose()
        {
            personal.Dispose(); shared.Dispose(); personalStream.Dispose(); sharedStream.Dispose();
        }
    }
}
