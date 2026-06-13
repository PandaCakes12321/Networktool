// Networktool — Floating network monitor widget for Windows
// Author : Teffers
// Version: 1.09
// License: Private

using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace Networktool;

public static class SoundManager
{
    // Descending two-tone beep for ping failure
    public static void PlayPingFail() => PlayAsync(
        new[] { (660, 120), (440, 180) },
        volume: 0.35f);

    // Ascending chime for successful swap
    public static void PlaySwapSuccess() => PlayAsync(
        new[] { (523, 100), (659, 100), (784, 180) },
        volume: 0.5f);

    private static void PlayAsync((int hz, int ms)[] notes, float volume)
    {
        Task.Run(() =>
        {
            try
            {
                var wav = BuildWav(notes, volume);
                using var ms2 = new MemoryStream(wav);
                using var player = new SoundPlayer(ms2);
                player.PlaySync();
            }
            catch { }
        });
    }

    private static byte[] BuildWav((int hz, int ms)[] notes, float volume)
    {
        const int sampleRate = 44100;
        const int channels = 1;
        const int bitsPerSample = 16;

        // Build PCM samples for all notes with fade-out per note
        int totalSamples = 0;
        foreach (var (_, ms) in notes)
            totalSamples += (int)(sampleRate * ms / 1000.0);

        var samples = new short[totalSamples];
        int pos = 0;

        foreach (var (hz, ms) in notes)
        {
            int count = (int)(sampleRate * ms / 1000.0);
            for (int i = 0; i < count; i++)
            {
                double t = i / (double)sampleRate;
                double fade = 1.0 - (double)i / count; // fade out each note
                double sine = Math.Sin(2 * Math.PI * hz * t);
                samples[pos++] = (short)(sine * fade * volume * short.MaxValue);
            }
        }

        // Write WAV header + PCM data
        int dataSize = samples.Length * 2;
        using var mem = new MemoryStream();
        using var w = new BinaryWriter(mem);

        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataSize);
        w.Write("WAVEfmt "u8.ToArray());
        w.Write(16);              // chunk size
        w.Write((short)1);        // PCM
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * bitsPerSample / 8); // byte rate
        w.Write((short)(channels * bitsPerSample / 8));     // block align
        w.Write((short)bitsPerSample);
        w.Write("data"u8.ToArray());
        w.Write(dataSize);
        foreach (var s in samples) w.Write(s);

        return mem.ToArray();
    }
}
