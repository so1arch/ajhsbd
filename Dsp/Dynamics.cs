namespace MicStudio;

/// <summary>Шумовые ворота: детектор смотрит на сигнал ДО шумоподавления (даёт упреждение ~13 мс).</summary>
public sealed class Gate
{
    public float ThresholdDb = -55f;
    float env, gain = 1f, relEnv, atk, rel;
    int hold, holdSamples;
    bool open = true;

    public void Init(float fs)
    {
        relEnv = MathF.Exp(-1f / (0.03f * fs));
        atk = 1f - MathF.Exp(-1f / (0.003f * fs));
        rel = 1f - MathF.Exp(-1f / (0.12f * fs));
        holdSamples = (int)(0.18f * fs);
    }

    public float Next(float x)
    {
        if (ThresholdDb <= -69.9f)
        {
            gain += (1f - gain) * atk;
            return gain;
        }
        float a = MathF.Abs(x);
        env = a > env ? a : env * relEnv;
        float db = 20f * MathF.Log10(env + 1e-9f);
        if (db > ThresholdDb) { open = true; hold = holdSamples; }
        else if (hold > 0) hold--;
        else open = false;

        float target = open ? 1f : 0.04f;   // закрытые ворота = −28 дБ
        gain += (target - gain) * (target > gain ? atk : rel);
        return gain;
    }
}

/// <summary>Компрессор с мягким коленом, детектор в лог-области.</summary>
public sealed class Compressor
{
    public bool Bypass = true;
    float thr = -20f, ratio = 3f, makeup, env, atk, rel;
    const float Knee = 6f;

    public void Init(float fs)
    {
        atk = MathF.Exp(-1f / (0.005f * fs));
        rel = MathF.Exp(-1f / (0.10f * fs));
    }

    /// <summary>pct: 0..100.</summary>
    public void Set(float pct)
    {
        if (pct < 1f) { Bypass = true; return; }
        Bypass = false;
        thr = -8f - 0.22f * pct;
        ratio = 1.5f + 0.035f * pct;
        makeup = -thr * (1f - 1f / ratio) * 0.45f;
    }

    public float Process(float x)
    {
        if (Bypass) { env = 0f; return x; }
        float lvl = 20f * MathF.Log10(MathF.Abs(x) + 1e-9f);
        float over = lvl - thr;
        float gr;
        if (2f * over < -Knee) gr = 0f;
        else if (2f * MathF.Abs(over) <= Knee)
        {
            float t = over + Knee / 2f;
            gr = (1f / ratio - 1f) * t * t / (2f * Knee);
        }
        else gr = (1f / ratio - 1f) * over;

        env = gr < env ? atk * env + (1f - atk) * gr : rel * env + (1f - rel) * gr;
        return x * MathF.Pow(10f, (env + makeup) / 20f);
    }
}

/// <summary>Де-эссер: приглушает полосу 5–8 кГц, когда она доминирует в сигнале.</summary>
public sealed class DeEsser
{
    public float Amount;   // 0..1
    readonly Biquad bp = new();
    float envB, envW, gain = 1f, relEnv, atk, rel;

    public void Init(float fs)
    {
        bp.BandPass(fs, 6500, 1.4);
        relEnv = MathF.Exp(-1f / (0.006f * fs));
        atk = 1f - MathF.Exp(-1f / (0.0008f * fs));
        rel = 1f - MathF.Exp(-1f / (0.03f * fs));
    }

    public float Process(float x)
    {
        float s = bp.Process(x);
        if (Amount < 0.01f) return x;

        float a = MathF.Abs(s);
        envB = a > envB ? a : envB * relEnv;
        float w = MathF.Abs(x);
        envW = w > envW ? w : envW * relEnv;

        float ratio = envB / (envW + 1e-6f);
        float target = 1f;
        const float thr = 0.45f;
        if (ratio > thr && envW > 0.003f)
        {
            float redDb = 20f * MathF.Log10(ratio / thr) * (0.4f + 0.8f * Amount);
            redDb = MathF.Min(redDb, 12f);
            target = MathF.Pow(10f, -redDb / 20f);
        }
        gain += (target - gain) * (target < gain ? atk : rel);
        return x + s * (gain - 1f);
    }
}

/// <summary>Лимитер на −1 дБ с жёсткой подстраховкой от перегруза.</summary>
public sealed class Limiter
{
    const float Ceiling = 0.891f;   // −1 дБFS
    float gain = 1f, rel;

    public void Init(float fs) => rel = 1f - MathF.Exp(-1f / (0.08f * fs));

    public float Process(float x)
    {
        float a = MathF.Abs(x);
        float target = a > Ceiling ? Ceiling / a : 1f;
        if (target < gain) gain += (target - gain) * 0.5f;
        else gain += (target - gain) * rel;
        float y = x * gain;
        if (y > 0.99f) y = 0.99f;
        else if (y < -0.99f) y = -0.99f;
        return y;
    }
}
