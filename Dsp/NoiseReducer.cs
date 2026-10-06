namespace MicStudio;

/// <summary>
/// Шумоподавление в частотной области: окно 512, шаг 128 (перекрытие 75%).
/// Шум оценивается по минимумам спектра за ~1,5 с, затем применяется
/// винеровский фильтр с «decision-directed» оценкой — это даёт меньше «булькающих» артефактов.
/// Задержка около 13 мс. Работает на 48 кГц, моно.
/// </summary>
public sealed class NoiseReducer
{
    const int N = 512, Hop = 128, Bins = N / 2 + 1;
    const int SubLen = 260;                 // ~0,7 с на подокно минимумов
    const float Beta = 0.90f;               // сглаживание decision-directed
    const float AlphaS = 0.85f;             // сглаживание спектра мощности
    const float NoiseBias = 2.2f;           // поправка на занижение минимумом

    readonly Fft fft = new(N);
    readonly float[] win = new float[N];
    readonly float[] inBuf = new float[N];
    readonly float[] outAcc = new float[N];
    readonly float[] outReady = new float[Hop];
    readonly float[] re = new float[N];
    readonly float[] im = new float[N];
    readonly float[] p = new float[Bins];
    readonly float[] ps = new float[Bins];
    readonly float[] minCur = new float[Bins];
    readonly float[] minPrev = new float[Bins];
    readonly float[] g = new float[Bins];
    readonly float[] gt = new float[Bins];
    readonly float[] clean = new float[Bins];

    int inCount, outIdx, subCount;
    bool init;

    /// <summary>Сила подавления 0..1 (0 — прозрачный проход с той же задержкой).</summary>
    public float Amount;

    public NoiseReducer()
    {
        for (int i = 0; i < N; i++)
            win[i] = MathF.Sqrt(0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / N));
        for (int k = 0; k < Bins; k++) g[k] = 1f;
    }

    public float Process(float x)
    {
        float y = outReady[outIdx];
        inBuf[N - Hop + inCount] = x;
        inCount++;
        outIdx++;
        if (inCount == Hop)
        {
            Frame();
            inCount = 0;
            outIdx = 0;
        }
        return y;
    }

    void Frame()
    {
        for (int i = 0; i < N; i++)
        {
            re[i] = inBuf[i] * win[i];
            im[i] = 0f;
        }
        fft.Run(re, im, false);

        float amt = Amount;
        bool active = amt > 0.01f;

        for (int k = 0; k < Bins; k++)
            p[k] = re[k] * re[k] + im[k] * im[k] + 1e-12f;

        if (!init)
        {
            for (int k = 0; k < Bins; k++)
            {
                ps[k] = p[k];
                minCur[k] = p[k];
                minPrev[k] = p[k];
                clean[k] = p[k];
            }
            init = true;
        }

        for (int k = 0; k < Bins; k++)
        {
            ps[k] = AlphaS * ps[k] + (1f - AlphaS) * p[k];
            if (ps[k] < minCur[k]) minCur[k] = ps[k];
        }
        if (++subCount >= SubLen)
        {
            subCount = 0;
            for (int k = 0; k < Bins; k++)
            {
                minPrev[k] = minCur[k];
                minCur[k] = ps[k];
            }
        }

        float over = 1f + 1.5f * amt;
        float floor = MathF.Pow(10f, -(5f + 25f * amt) / 20f);

        for (int k = 0; k < Bins; k++)
        {
            if (!active) { gt[k] = 1f; continue; }

            float n = NoiseBias * MathF.Min(minPrev[k], minCur[k]) * over + 1e-12f;
            float gamma = p[k] / n;
            float xi = Beta * clean[k] / n + (1f - Beta) * MathF.Max(gamma - 1f, 0f);
            float gw = xi / (1f + xi);
            if (gw < floor) gw = floor;

            float prev = g[k];
            gw = gw > prev ? prev + (gw - prev) * 0.8f : prev + (gw - prev) * 0.3f;
            gt[k] = gw;
        }

        // Сглаживание по частоте и запоминание «чистой» мощности
        for (int k = 0; k < Bins; k++)
        {
            float a = gt[k];
            float l = k > 0 ? gt[k - 1] : a;
            float r = k < Bins - 1 ? gt[k + 1] : a;
            g[k] = 0.25f * l + 0.5f * a + 0.25f * r;
            clean[k] = g[k] * g[k] * p[k];
        }

        for (int k = 0; k < Bins; k++)
        {
            re[k] *= g[k];
            im[k] *= g[k];
        }
        for (int k = 1; k < N / 2; k++)
        {
            re[N - k] *= g[k];
            im[N - k] *= g[k];
        }

        fft.Run(re, im, true);

        for (int i = 0; i < N; i++)
            outAcc[i] += re[i] * win[i] * 0.5f;   // 0,5 — нормировка для перекрытия 75% и окна sqrt-Hann

        Array.Copy(outAcc, 0, outReady, 0, Hop);
        Array.Copy(outAcc, Hop, outAcc, 0, N - Hop);
        Array.Clear(outAcc, N - Hop, Hop);
        Array.Copy(inBuf, Hop, inBuf, 0, N - Hop);
    }
}
