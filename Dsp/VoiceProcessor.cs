namespace MicStudio;

/// <summary>Параметры обработки. Интерфейс пишет сюда, аудиопоток читает.</summary>
public sealed class VoiceParams
{
    public volatile bool Enabled = true;
    public float InputGainDb, OutputGainDb;
    public float NoiseReduction = 60, GateDb = -55;
    public float Warmth = 2, Mud = -2.5f, Clarity = 3, Air = 3;
    public float Compression = 55, DeEss = 40;
    public int Version;

    public void Touch() => Interlocked.Increment(ref Version);
}

/// <summary>
/// Цепочка: усиление → ФВЧ 75 Гц → (ворота) → шумоподавление → эквалайзер →
/// компрессор → де-эссер → громкость → лимитер. 48 кГц, моно.
/// </summary>
public sealed class VoiceProcessor
{
    public const float Fs = 48000f;

    readonly VoiceParams P;
    readonly Biquad hp1 = new(), hp2 = new();
    readonly Biquad eqMud = new(), eqWarm = new(), eqClar = new(), eqAir = new();
    readonly NoiseReducer nr = new();
    readonly Gate gate = new();
    readonly Compressor comp = new();
    readonly DeEsser de = new();
    readonly Limiter lim = new();

    float inGain = 1f, outGain = 1f;
    int applied = -1;
    float inPeak, outPeak;

    public VoiceProcessor(VoiceParams p)
    {
        P = p;
        gate.Init(Fs);
        comp.Init(Fs);
        de.Init(Fs);
        lim.Init(Fs);
    }

    public float TakeIn() { float v = inPeak; inPeak = 0f; return v; }
    public float TakeOut() { float v = outPeak; outPeak = 0f; return v; }

    static float DbToLin(float db) => MathF.Pow(10f, db / 20f);

    void Apply()
    {
        inGain = DbToLin(P.InputGainDb);
        outGain = DbToLin(P.OutputGainDb);
        hp1.HighPass(Fs, 75, 0.7071);
        hp2.HighPass(Fs, 75, 0.7071);
        nr.Amount = P.NoiseReduction / 100f;
        gate.ThresholdDb = P.GateDb;
        eqMud.Peak(Fs, 300, 1.0, P.Mud);
        eqWarm.LowShelf(Fs, 150, P.Warmth);
        eqClar.Peak(Fs, 3500, 0.9, P.Clarity);
        eqAir.HighShelf(Fs, 9000, P.Air);
        comp.Set(P.Compression);
        de.Amount = P.DeEss / 100f;
    }

    public void Process(float[] b, int off, int n)
    {
        int v = Volatile.Read(ref P.Version);
        if (v != applied)
        {
            Apply();
            applied = v;
        }

        bool on = P.Enabled;
        float pi = 0f, po = 0f;

        for (int i = off; i < off + n; i++)
        {
            float raw = b[i];
            float x, y;

            if (on)
            {
                x = hp2.Process(hp1.Process(raw * inGain));
                float gg = gate.Next(x);
                y = nr.Process(x) * gg;
                y = eqMud.Process(y);
                y = eqWarm.Process(y);
                y = eqClar.Process(y);
                y = eqAir.Process(y);
                y = comp.Process(y);
                y = de.Process(y);
                y *= outGain;
                y = lim.Process(y);
            }
            else
            {
                x = raw;
                y = raw;
            }

            float ax = MathF.Abs(x);
            if (ax > pi) pi = ax;
            float ay = MathF.Abs(y);
            if (ay > po) po = ay;
            b[i] = y;
        }

        if (pi > inPeak) inPeak = pi;
        if (po > outPeak) outPeak = po;
    }
}
