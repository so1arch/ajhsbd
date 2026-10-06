namespace MicStudio;

/// <summary>Биквадный фильтр (формулы RBJ Audio EQ Cookbook).</summary>
public sealed class Biquad
{
    float b0 = 1, b1, b2, a1, a2, z1, z2;

    public float Process(float x)
    {
        float y = b0 * x + z1;
        z1 = b1 * x - a1 * y + z2;
        z2 = b2 * x - a2 * y;
        if (MathF.Abs(z1) < 1e-20f) z1 = 0f;
        if (MathF.Abs(z2) < 1e-20f) z2 = 0f;
        return y;
    }

    void Set(double nb0, double nb1, double nb2, double a0, double na1, double na2)
    {
        b0 = (float)(nb0 / a0);
        b1 = (float)(nb1 / a0);
        b2 = (float)(nb2 / a0);
        a1 = (float)(na1 / a0);
        a2 = (float)(na2 / a0);
    }

    public void HighPass(double fs, double f, double q)
    {
        double w = 2 * Math.PI * f / fs, c = Math.Cos(w), s = Math.Sin(w), al = s / (2 * q);
        Set((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + al, -2 * c, 1 - al);
    }

    public void BandPass(double fs, double f, double q)
    {
        double w = 2 * Math.PI * f / fs, c = Math.Cos(w), s = Math.Sin(w), al = s / (2 * q);
        Set(al, 0, -al, 1 + al, -2 * c, 1 - al);
    }

    public void Peak(double fs, double f, double q, double dB)
    {
        double A = Math.Pow(10, dB / 40), w = 2 * Math.PI * f / fs, c = Math.Cos(w), s = Math.Sin(w), al = s / (2 * q);
        Set(1 + al * A, -2 * c, 1 - al * A, 1 + al / A, -2 * c, 1 - al / A);
    }

    public void LowShelf(double fs, double f, double dB)
    {
        double A = Math.Pow(10, dB / 40), w = 2 * Math.PI * f / fs, c = Math.Cos(w), s = Math.Sin(w);
        double al = s / 2 * Math.Sqrt(2), sq = 2 * Math.Sqrt(A) * al;
        Set(A * ((A + 1) - (A - 1) * c + sq),
            2 * A * ((A - 1) - (A + 1) * c),
            A * ((A + 1) - (A - 1) * c - sq),
            (A + 1) + (A - 1) * c + sq,
            -2 * ((A - 1) + (A + 1) * c),
            (A + 1) + (A - 1) * c - sq);
    }

    public void HighShelf(double fs, double f, double dB)
    {
        double A = Math.Pow(10, dB / 40), w = 2 * Math.PI * f / fs, c = Math.Cos(w), s = Math.Sin(w);
        double al = s / 2 * Math.Sqrt(2), sq = 2 * Math.Sqrt(A) * al;
        Set(A * ((A + 1) + (A - 1) * c + sq),
            -2 * A * ((A - 1) + (A + 1) * c),
            A * ((A + 1) + (A - 1) * c - sq),
            (A + 1) - (A - 1) * c + sq,
            2 * ((A - 1) - (A + 1) * c),
            (A + 1) - (A - 1) * c - sq);
    }
}

/// <summary>Простое БПФ по основанию 2.</summary>
public sealed class Fft
{
    readonly int n;
    readonly int[] rev;
    readonly float[] cos, sin;

    public Fft(int n)
    {
        this.n = n;
        int bits = (int)Math.Round(Math.Log2(n));
        rev = new int[n];
        for (int i = 0; i < n; i++)
        {
            int r = 0;
            for (int b = 0; b < bits; b++)
                if (((i >> b) & 1) != 0) r |= 1 << (bits - 1 - b);
            rev[i] = r;
        }
        cos = new float[n / 2];
        sin = new float[n / 2];
        for (int k = 0; k < n / 2; k++)
        {
            cos[k] = (float)Math.Cos(2 * Math.PI * k / n);
            sin[k] = (float)Math.Sin(2 * Math.PI * k / n);
        }
    }

    public void Run(float[] re, float[] im, bool inverse)
    {
        for (int i = 0; i < n; i++)
        {
            int j = rev[i];
            if (j > i)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int size = 2; size <= n; size <<= 1)
        {
            int half = size / 2, step = n / size;
            for (int i = 0; i < n; i += size)
            {
                for (int j = 0, k = 0; j < half; j++, k += step)
                {
                    float wr = cos[k];
                    float wi = inverse ? sin[k] : -sin[k];
                    int a = i + j, b = a + half;
                    float tr = re[b] * wr - im[b] * wi;
                    float ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }
        if (inverse)
        {
            float inv = 1f / n;
            for (int i = 0; i < n; i++) { re[i] *= inv; im[i] *= inv; }
        }
    }
}
