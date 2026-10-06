using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicStudio;

/// <summary>Микрофон → обработка → выходное устройство (обычно CABLE Input = виртуальный микрофон).</summary>
public sealed class AudioEngine : IDisposable
{
    public readonly VoiceParams P = new();
    public readonly VoiceProcessor Proc;
    public volatile bool Running;
    public string Config = "";
    public string LastError = "";

    WasapiCapture capture;
    WasapiOut output, monitor;
    BufferedWaveProvider capBuf, monBuf;
    MMDevice inDev, outDev, monDev;

    public AudioEngine()
    {
        Proc = new VoiceProcessor(P);
    }

    public void Start(string inId, string outId, string monId)
    {
        Stop();
        try
        {
            using (var en = new MMDeviceEnumerator())
            {
                inDev = en.GetDevice(inId);
                outDev = en.GetDevice(outId);
                if (!string.IsNullOrEmpty(monId)) monDev = en.GetDevice(monId);
            }

            // Захват в родном формате устройства (самый надёжный вариант)
            capture = new WasapiCapture(inDev, true, 20);
            capBuf = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(150),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
            capture.DataAvailable += OnData;
            capture.RecordingStopped += OnCaptureStopped;

            ISampleProvider sp = new PrimedWaveProvider(capBuf, 40).ToSampleProvider();
            sp = new ToMono(sp);
            if (sp.WaveFormat.SampleRate != 48000)
                sp = new WdlResamplingSampleProvider(sp, 48000);

            var dsp = new ProcessingSampleProvider(sp, Proc);

            if (monDev != null)
            {
                monBuf = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1))
                {
                    BufferDuration = TimeSpan.FromMilliseconds(200),
                    DiscardOnBufferOverflow = true,
                    ReadFully = true
                };
                dsp.Tap = monBuf;
            }

            // Выход: приводим к формату устройства (частота + число каналов)
            output = new WasapiOut(outDev, AudioClientShareMode.Shared, true, 40);
            output.PlaybackStopped += OnPlaybackStopped;
            output.Init(ToDevice(dsp, outDev.AudioClient.MixFormat).ToWaveProvider());

            if (monDev != null)
            {
                ISampleProvider ms = new PrimedWaveProvider(monBuf, 60).ToSampleProvider();
                monitor = new WasapiOut(monDev, AudioClientShareMode.Shared, true, 60);
                monitor.Init(ToDevice(ms, monDev.AudioClient.MixFormat).ToWaveProvider());
            }

            capture.StartRecording();
            output.Play();
            monitor?.Play();

            Config = $"{inId}|{outId}|{monId}";
            LastError = "";
            Running = true;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    static ISampleProvider ToDevice(ISampleProvider mono48, WaveFormat mix)
    {
        ISampleProvider p = mono48;
        if (mix.SampleRate != p.WaveFormat.SampleRate)
            p = new WdlResamplingSampleProvider(p, mix.SampleRate);
        if (mix.Channels > 1)
            p = new MonoToMulti(p, mix.Channels);
        return p;
    }

    void OnData(object sender, WaveInEventArgs e)
    {
        capBuf?.AddSamples(e.Buffer, 0, e.BytesRecorded);
    }

    void OnCaptureStopped(object sender, StoppedEventArgs e)
    {
        if (e.Exception != null) LastError = e.Exception.Message;
        Running = false;
    }

    void OnPlaybackStopped(object sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            LastError = e.Exception.Message;
            Running = false;
        }
    }

    public void Stop()
    {
        Running = false;
        Config = "";

        var c = capture; capture = null;
        if (c != null)
        {
            try { c.DataAvailable -= OnData; c.RecordingStopped -= OnCaptureStopped; c.StopRecording(); } catch { }
            try { c.Dispose(); } catch { }
        }
        var o = output; output = null;
        if (o != null)
        {
            try { o.PlaybackStopped -= OnPlaybackStopped; o.Stop(); } catch { }
            try { o.Dispose(); } catch { }
        }
        var m = monitor; monitor = null;
        if (m != null)
        {
            try { m.Stop(); } catch { }
            try { m.Dispose(); } catch { }
        }
        capBuf = null;
        monBuf = null;

        try { inDev?.Dispose(); } catch { }
        try { outDev?.Dispose(); } catch { }
        try { monDev?.Dispose(); } catch { }
        inDev = outDev = monDev = null;
    }

    public void Dispose() => Stop();
}

/// <summary>Ждёт, пока в буфере накопится немного звука — защита от щелчков из-за дрожания таймингов.</summary>
sealed class PrimedWaveProvider : IWaveProvider
{
    readonly BufferedWaveProvider src;
    readonly int primeBytes;
    bool primed;

    public WaveFormat WaveFormat => src.WaveFormat;

    public PrimedWaveProvider(BufferedWaveProvider s, int ms)
    {
        src = s;
        int bytes = (int)((long)s.WaveFormat.AverageBytesPerSecond * ms / 1000);
        primeBytes = bytes - bytes % s.WaveFormat.BlockAlign;
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        if (!primed)
        {
            if (src.BufferedBytes >= primeBytes) primed = true;
            else
            {
                Array.Clear(buffer, offset, count);
                return count;
            }
        }

        int avail = src.BufferedBytes;
        if (avail >= count) return src.Read(buffer, offset, count);

        int take = avail - avail % src.WaveFormat.BlockAlign;
        int got = take > 0 ? src.Read(buffer, offset, take) : 0;
        Array.Clear(buffer, offset + got, count - got);
        primed = false;
        return count;
    }
}

/// <summary>Любое число каналов → моно (среднее).</summary>
sealed class ToMono : ISampleProvider
{
    readonly ISampleProvider src;
    readonly int ch;
    float[] tmp = new float[0];

    public WaveFormat WaveFormat { get; }

    public ToMono(ISampleProvider s)
    {
        src = s;
        ch = s.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(s.WaveFormat.SampleRate, 1);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (ch == 1) return src.Read(buffer, offset, count);
        int need = count * ch;
        if (tmp.Length < need) tmp = new float[need];
        int got = src.Read(tmp, 0, need);
        int frames = got / ch;
        for (int i = 0; i < frames; i++)
        {
            float s = 0f;
            for (int c = 0; c < ch; c++) s += tmp[i * ch + c];
            buffer[offset + i] = s / ch;
        }
        return frames;
    }
}

/// <summary>Моно → N каналов (одинаковый сигнал во все).</summary>
sealed class MonoToMulti : ISampleProvider
{
    readonly ISampleProvider src;
    readonly int ch;
    float[] tmp = new float[0];

    public WaveFormat WaveFormat { get; }

    public MonoToMulti(ISampleProvider s, int channels)
    {
        src = s;
        ch = channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(s.WaveFormat.SampleRate, channels);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / ch;
        if (tmp.Length < frames) tmp = new float[frames];
        int got = src.Read(tmp, 0, frames);
        for (int i = 0; i < got; i++)
            for (int c = 0; c < ch; c++)
                buffer[offset + i * ch + c] = tmp[i];
        return got * ch;
    }
}

/// <summary>Прогоняет звук через VoiceProcessor и (по желанию) дублирует результат в буфер «слышать себя».</summary>
sealed class ProcessingSampleProvider : ISampleProvider
{
    readonly ISampleProvider src;
    readonly VoiceProcessor proc;
    byte[] tapBytes = new byte[0];

    public BufferedWaveProvider Tap;
    public WaveFormat WaveFormat => src.WaveFormat;

    public ProcessingSampleProvider(ISampleProvider s, VoiceProcessor p)
    {
        src = s;
        proc = p;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int n = src.Read(buffer, offset, count);
        if (n <= 0) return n;
        proc.Process(buffer, offset, n);

        var t = Tap;
        if (t != null)
        {
            int bytes = n * 4;
            if (tapBytes.Length < bytes) tapBytes = new byte[bytes];
            Buffer.BlockCopy(buffer, offset * 4, tapBytes, 0, bytes);
            t.AddSamples(tapBytes, 0, bytes);
        }
        return n;
    }
}
