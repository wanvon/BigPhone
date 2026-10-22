using System.IO;
using NAudio.Wave;

namespace BigPhone;

/// <summary>Plays a loud, classic US telephone ring (440 + 480 Hz, 2 s on / 4 s off) in a loop.</summary>
public sealed class Ringer : IDisposable
{
    const int SampleRate = 16000;
    readonly int _deviceNumber;
    readonly byte[] _pcm;
    WaveOut? _out;

    public Ringer(int deviceNumber)
    {
        _deviceNumber = deviceNumber;
        _pcm = BuildRing();
    }

    public bool IsRinging => _out != null;

    public void Start()
    {
        if (_out != null) return;
        try
        {
            var stream = new LoopStream(new RawSourceWaveStream(new MemoryStream(_pcm), new WaveFormat(SampleRate, 16, 1)));
            _out = new WaveOut { DeviceNumber = _deviceNumber, Volume = 1.0f };
            _out.Init(stream);
            _out.Play();
        }
        catch
        {
            _out?.Dispose();
            _out = null;
            System.Media.SystemSounds.Asterisk.Play();
        }
    }

    public void Stop()
    {
        _out?.Stop();
        _out?.Dispose();
        _out = null;
    }

    public void Dispose() => Stop();

    static byte[] BuildRing()
    {
        int on = SampleRate * 2, off = SampleRate * 4;
        var buf = new byte[(on + off) * 2];
        for (int i = 0; i < on; i++)
        {
            double t = (double)i / SampleRate;
            double env = Math.Min(1.0, Math.Min(i, on - i) / (SampleRate * 0.02));   // 20 ms fade to avoid clicks
            double v = 0.45 * (Math.Sin(2 * Math.PI * 440 * t) + Math.Sin(2 * Math.PI * 480 * t)) * env;
            short s = (short)(v * short.MaxValue);
            buf[i * 2] = (byte)s;
            buf[i * 2 + 1] = (byte)(s >> 8);
        }
        return buf;
    }

    sealed class LoopStream : WaveStream
    {
        readonly WaveStream _src;
        public LoopStream(WaveStream src) => _src = src;
        public override WaveFormat WaveFormat => _src.WaveFormat;
        public override long Length => long.MaxValue;
        public override long Position { get => _src.Position; set => _src.Position = value; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = _src.Read(buffer, offset + total, count - total);
                if (n == 0) _src.Position = 0;
                total += n;
            }
            return total;
        }
    }
}
