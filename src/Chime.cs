using System.IO;
using NAudio.Wave;

namespace BigPhone;

/// <summary>A short two-note "ding-dong" for new text messages (distinct from the phone ring).</summary>
public static class Chime
{
    const int SampleRate = 22050;
    static readonly byte[] Pcm = Build();

    /// <summary>Plays the chime once on the given output device (-1 = Windows default). Never throws.</summary>
    public static void Play(int deviceNumber, bool soft)
    {
        try
        {
            var stream = new RawSourceWaveStream(new MemoryStream(Pcm), new WaveFormat(SampleRate, 16, 1));
            var output = new WaveOut { DeviceNumber = deviceNumber, Volume = soft ? 0.3f : 1.0f };
            output.PlaybackStopped += (_, _) => { output.Dispose(); stream.Dispose(); };
            output.Init(stream);
            output.Play();
        }
        catch
        {
            System.Media.SystemSounds.Exclamation.Play();
        }
    }

    // E6 then C6, each a bell-like tone with a quick attack and gentle decay, plus a soft octave overtone.
    static byte[] Build()
    {
        (double freq, double start, double length)[] notes = { (1318.5, 0.0, 0.55), (1046.5, 0.32, 0.9) };
        int total = (int)(SampleRate * 1.25);
        var mix = new double[total];
        foreach (var (freq, start, length) in notes)
        {
            int s0 = (int)(start * SampleRate), n = (int)(length * SampleRate);
            for (int i = 0; i < n && s0 + i < total; i++)
            {
                double t = (double)i / SampleRate;
                double env = Math.Min(1.0, t / 0.005) * Math.Exp(-t * 5.0);
                mix[s0 + i] += env * (0.6 * Math.Sin(2 * Math.PI * freq * t) + 0.15 * Math.Sin(2 * Math.PI * freq * 2 * t));
            }
        }
        var buf = new byte[total * 2];
        for (int i = 0; i < total; i++)
        {
            short v = (short)(Math.Clamp(mix[i], -1, 1) * short.MaxValue * 0.9);
            buf[i * 2] = (byte)v;
            buf[i * 2 + 1] = (byte)(v >> 8);
        }
        return buf;
    }
}
