using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MicStudio;

static class Theme
{
    public static readonly Color Bg = ColorTranslator.FromHtml("#0F1A21");
    public static readonly Color Panel = ColorTranslator.FromHtml("#17262F");
    public static readonly Color Track = ColorTranslator.FromHtml("#2A3F4B");
    public static readonly Color Text = ColorTranslator.FromHtml("#EDE8DC");
    public static readonly Color Muted = ColorTranslator.FromHtml("#8DA2AD");
    public static readonly Color Accent = ColorTranslator.FromHtml("#F2A93B");
    public static readonly Color Good = ColorTranslator.FromHtml("#4FB3A2");
    public static readonly Color Bad = ColorTranslator.FromHtml("#E5574B");

    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font Head = new("Segoe UI Semibold", 11f);
    public static readonly Font Title = new("Segoe UI Semibold", 17f);

    public static GraphicsPath Pill(RectangleF r)
    {
        var p = new GraphicsPath();
        float d = r.Height;
        p.AddArc(r.X, r.Y, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        p.CloseFigure();
        return p;
    }
}

/// <summary>Переключатель «вкл/выкл».</summary>
sealed class Toggle : Control
{
    bool chk;
    public event EventHandler CheckedChanged;

    public bool Checked
    {
        get => chk;
        set
        {
            if (chk == value) return;
            chk = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Toggle()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(56, 30);
        Cursor = Cursors.Hand;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        using (var path = Theme.Pill(r))
        using (var b = new SolidBrush(chk ? Theme.Accent : Theme.Track))
            g.FillPath(b, path);
        float d = r.Height - 6;
        float x = chk ? r.Right - d - 3 : r.Left + 3;
        using (var b = new SolidBrush(chk ? Theme.Bg : Theme.Muted))
            g.FillEllipse(b, x, r.Top + 3, d, d);
    }
}

/// <summary>Ползунок с подписью и значением. Двойной щелчок — вернуть значение по умолчанию.</summary>
sealed class DarkSlider : Control
{
    public string Caption { get; set; } = "";
    public float Min { get; set; } = 0;
    public float Max { get; set; } = 100;
    public float DefaultValue { get; set; }
    public string Suffix { get; set; } = "";
    public bool ShowSign { get; set; }
    public bool OffAtMin { get; set; }

    float val;
    bool drag;
    public event EventHandler ValueChanged;

    public DarkSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(400, 46);
        Cursor = Cursors.Hand;
    }

    /// <summary>Программная установка значения (событие не вызывается).</summary>
    public float Value
    {
        get => val;
        set
        {
            float v = Math.Clamp(value, Min, Max);
            if (v == val) return;
            val = v;
            Invalidate();
        }
    }

    void SetByUser(float v)
    {
        v = Math.Clamp(v, Min, Max);
        if (v == val) return;
        val = v;
        Invalidate();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    float XToValue(int x)
    {
        float k = DeviceDpi / 96f;
        float x0 = 8 * k, x1 = Width - 8 * k;
        float t = Math.Clamp((x - x0) / (x1 - x0), 0f, 1f);
        float v = Min + t * (Max - Min);
        float step = (Max - Min) >= 50 ? 1f : 0.5f;
        return MathF.Round(v / step) * step;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        drag = true;
        SetByUser(XToValue(e.X));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (drag) SetByUser(XToValue(e.X));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        drag = false;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        SetByUser(DefaultValue);
    }

    string FormatValue()
    {
        if (OffAtMin && val <= Min + 0.001f) return "выкл";
        string s = val.ToString("0.#");
        if (ShowSign && val > 0) s = "+" + s;
        return s + Suffix;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        float k = DeviceDpi / 96f;

        TextRenderer.DrawText(g, Caption, Theme.Body, new Point(0, (int)(2 * k)), Theme.Text);
        string v = FormatValue();
        var sz = TextRenderer.MeasureText(g, v, Theme.Body);
        TextRenderer.DrawText(g, v, Theme.Body, new Point(Width - sz.Width, (int)(2 * k)), Theme.Accent);

        float x0 = 8 * k, x1 = Width - 8 * k, cy = Height - 12 * k, th = 4 * k;
        float t = (val - Min) / (Max - Min);
        float tx = x0 + t * (x1 - x0);

        using (var path = Theme.Pill(new RectangleF(x0, cy - th / 2, x1 - x0, th)))
        using (var b = new SolidBrush(Theme.Track))
            g.FillPath(b, path);
        if (tx > x0 + 1)
        {
            using (var path = Theme.Pill(new RectangleF(x0, cy - th / 2, tx - x0, th)))
            using (var b = new SolidBrush(Theme.Accent))
                g.FillPath(b, path);
        }
        float r = 7 * k;
        using (var b = new SolidBrush(Theme.Text))
            g.FillEllipse(b, tx - r, cy - r, r * 2, r * 2);
        using (var pen = new Pen(Theme.Accent, 2f * k))
            g.DrawEllipse(pen, tx - r, cy - r, r * 2, r * 2);
    }
}

/// <summary>Индикатор уровня в дБ с удержанием пика и меткой порога.</summary>
sealed class LevelMeter : Control
{
    public string Caption { get; set; } = "";
    public float MinDb = -60f;
    public float MarkerDb = float.NaN;

    float level = -100f, peak = -100f;
    int hold;

    public LevelMeter()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(420, 26);
    }

    public void Push(float lin)
    {
        float db = lin < 1e-5f ? -100f : 20f * MathF.Log10(lin);
        if (db > level) level = db;
        else level = MathF.Max(db, level - 2.2f);

        if (db >= peak) { peak = db; hold = 25; }
        else if (hold > 0) hold--;
        else peak = MathF.Max(level, peak - 1f);

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        float k = DeviceDpi / 96f;
        float capW = 56 * k;

        TextRenderer.DrawText(g, Caption, Theme.Small, new Point(0, (Height - Theme.Small.Height) / 2), Theme.Muted);

        var bar = new RectangleF(capW, Height / 2f - 5 * k, Width - capW - 1, 10 * k);
        float Pos(float db) => bar.X + bar.Width * Math.Clamp((db - MinDb) / (0f - MinDb), 0f, 1f);

        using (var path = Theme.Pill(bar))
        {
            using (var b = new SolidBrush(Theme.Track))
                g.FillPath(b, path);

            float fx = Pos(level);
            if (fx > bar.X + 1)
            {
                g.SetClip(new RectangleF(bar.X, bar.Y, fx - bar.X, bar.Height));
                using (var lg = new LinearGradientBrush(bar, Theme.Good, Theme.Bad, LinearGradientMode.Horizontal))
                {
                    lg.InterpolationColors = new ColorBlend
                    {
                        Positions = new[] { 0f, 0.62f, 0.85f, 1f },
                        Colors = new[] { Theme.Good, Theme.Good, Theme.Accent, Theme.Bad }
                    };
                    g.FillPath(lg, path);
                }
                g.ResetClip();
            }
        }

        if (peak > MinDb)
        {
            float px = Pos(peak);
            using (var b = new SolidBrush(Theme.Text))
                g.FillRectangle(b, px - k, bar.Y, 2 * k, bar.Height);
        }
        if (!float.IsNaN(MarkerDb))
        {
            float mx = Pos(MarkerDb);
            using (var pen = new Pen(Theme.Text, 1.5f * k))
                g.DrawLine(pen, mx, bar.Y - 3 * k, mx, bar.Bottom + 3 * k);
        }
    }
}

static class IconFactory
{
    public static Icon Make(bool on)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            Color bg = on ? Theme.Accent : Color.FromArgb(120, 130, 138);
            using (var b = new SolidBrush(bg)) g.FillEllipse(b, 1, 1, 30, 30);

            using var ink = new SolidBrush(Theme.Bg);
            using var pen = new Pen(Theme.Bg, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using (var cap = new GraphicsPath())
            {
                cap.AddArc(12f, 6f, 8f, 8f, 180, 180);
                cap.AddArc(12f, 13f, 8f, 8f, 0, 180);
                cap.CloseFigure();
                g.FillPath(ink, cap);
            }
            g.DrawArc(pen, 9f, 9f, 14f, 13f, 0, 180);
            g.DrawLine(pen, 16f, 22f, 16f, 25.5f);
            g.DrawLine(pen, 12f, 25.5f, 20f, 25.5f);
        }
        IntPtr h = bmp.GetHicon();
        using var tmp = Icon.FromHandle(h);
        return (Icon)tmp.Clone();
    }
}
