using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace MicStudio;

public sealed class MainForm : Form
{
    const string Custom = "Свои настройки";

    readonly Settings S = Settings.Load();
    readonly AudioEngine engine = new();
    readonly NotifyIcon tray = new();
    readonly Icon iconOn = IconFactory.Make(true);
    readonly Icon iconOff = IconFactory.Make(false);

    readonly Toggle tgl = new();
    readonly Label lblOn = new();
    readonly Label lblStatus = new();
    readonly Button btnInstall = new();
    readonly LevelMeter mIn = new() { Caption = "Вход" };
    readonly LevelMeter mOut = new() { Caption = "Выход" };
    readonly ComboBox cbPreset = MakeCombo();
    readonly ComboBox cbIn = MakeCombo();
    readonly ComboBox cbOut = MakeCombo();
    readonly ComboBox cbMon = MakeCombo();
    readonly CheckBox chkMon = new();
    readonly CheckBox chkAuto = new();
    readonly Dictionary<string, DarkSlider> sl = new();
    readonly ToolStripMenuItem miPower = new("Обработка голоса");

    readonly System.Windows.Forms.Timer uiTimer = new() { Interval = 33 };
    readonly System.Windows.Forms.Timer watchdog = new() { Interval = 2500 };
    readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 600 };

    FlowLayoutPanel body;
    bool loading, exiting, startHidden, cableExists;
    string devSig = "", lastError = "";

    public MainForm(bool startInTray)
    {
        startHidden = startInTray;
        SuspendLayout();

        Text = "MicStudio";
        Icon = iconOn;
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        float k = DeviceDpi / 96f;
        int h = Math.Min(800, (int)(Screen.PrimaryScreen.WorkingArea.Height / k) - 40);
        ClientSize = new Size(460, Math.Max(520, h));

        BuildBody();
        BuildHeader();
        BuildTray();

        ResumeLayout(true);

        // Начальное состояние
        loading = true;
        tgl.Checked = S.ProcessingOn;
        chkMon.Checked = S.Monitor;
        cbMon.Enabled = S.Monitor;
        chkAuto.Checked = S.StartWithWindows;
        loading = false;
        tgl.CheckedChanged += (_, _) =>
        {
            S.ProcessingOn = tgl.Checked;
            ApplyPowerUi();
            PushParams();
            ScheduleSave();
        };
        ApplyPowerUi();
        SyncSliders();
        PushParams();
        RefreshDevices(true);
        TryStart();
        UpdateStatus();

        uiTimer.Tick += (_, _) =>
        {
            float a = engine.Proc.TakeIn();
            float b = engine.Proc.TakeOut();
            if (!Visible) return;
            mIn.Push(a);
            mOut.Push(b);
        };
        watchdog.Tick += (_, _) => Watchdog();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); S.Save(); };
        uiTimer.Start();
        watchdog.Start();
    }

    // ───────────── Построение интерфейса ─────────────

    static ComboBox MakeCombo() => new ComboBox
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Panel,
        ForeColor = Theme.Text,
        Font = Theme.Body,
        Width = 410,
        DropDownWidth = 520,
        Margin = new Padding(0, 0, 0, 10)
    };

    static Label Head(string text, int top = 18) => new Label
    {
        Text = text,
        Font = Theme.Head,
        ForeColor = Theme.Text,
        AutoSize = true,
        Margin = new Padding(0, top, 0, 6)
    };

    static Label Cap(string text) => new Label
    {
        Text = text,
        Font = Theme.Small,
        ForeColor = Theme.Muted,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 3)
    };

    static Label Hint(string text) => new Label
    {
        Text = text,
        Font = Theme.Small,
        ForeColor = Theme.Muted,
        AutoSize = true,
        MaximumSize = new Size(400, 0),
        Margin = new Padding(0, 2, 0, 4)
    };

    void AddSlider(string key, string caption, float min, float max, float def, string suffix, bool sign = false, bool off = false)
    {
        var s = new DarkSlider
        {
            Caption = caption,
            Min = min,
            Max = max,
            DefaultValue = def,
            Suffix = suffix,
            ShowSign = sign,
            OffAtMin = off,
            Width = 410,
            Margin = new Padding(0, 0, 0, 2)
        };
        s.ValueChanged += (_, _) => OnSlider(key, s.Value);
        sl[key] = s;
        body.Controls.Add(s);
    }

    void BuildBody()
    {
        body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(18, 0, 18, 18),
            BackColor = Theme.Bg
        };

        foreach (var p in Presets.All) cbPreset.Items.Add(p.Name);
        cbPreset.Items.Add(Custom);
        cbPreset.SelectedIndexChanged += (_, _) =>
        {
            if (loading) return;
            var p = Presets.All.FirstOrDefault(x => x.Name == (cbPreset.SelectedItem as string));
            if (p != null) ApplyPreset(p);
        };

        body.Controls.Add(Head("Пресет", 12));
        body.Controls.Add(cbPreset);

        body.Controls.Add(Head("Чистка звука", 8));
        AddSlider("nr", "Шумоподавление", 0, 100, 60, " %", false, true);
        AddSlider("gate", "Шумовые ворота (тишина между словами)", -70, -30, -55, " дБ", false, true);
        body.Controls.Add(Hint("Если между словами слышен шум — сдвиньте ворота вправо, пока белая метка на шкале «Вход» не окажется чуть выше фонового шума."));

        body.Controls.Add(Head("Тембр"));
        AddSlider("warm", "Тепло (низкие)", -4, 8, 2, " дБ", true);
        AddSlider("mud", "Убрать «бочку» (гулкость)", -8, 0, -2.5f, " дБ", true);
        AddSlider("clar", "Чёткость (разборчивость)", -3, 8, 3, " дБ", true);
        AddSlider("air", "Воздух (блеск)", -3, 8, 3, " дБ", true);

        body.Controls.Add(Head("Динамика"));
        AddSlider("comp", "Ровная громкость (компрессор)", 0, 100, 55, " %", false, true);
        AddSlider("deess", "Шипящие «с», «ш» (де-эссер)", 0, 100, 40, " %", false, true);

        body.Controls.Add(Head("Громкость"));
        AddSlider("gin", "Усиление микрофона", -12, 30, 0, " дБ", true);
        AddSlider("gout", "Громкость на выходе", -12, 12, 0, " дБ", true);
        body.Controls.Add(Hint("Двойной щелчок по ползунку возвращает значение по умолчанию."));

        body.Controls.Add(Head("Устройства"));
        body.Controls.Add(Cap("Микрофон"));
        body.Controls.Add(cbIn);
        body.Controls.Add(Cap("Куда отправлять звук (виртуальный микрофон)"));
        body.Controls.Add(cbOut);

        chkMon.Text = "Слышать себя (в наушниках — проверить, как вас слышат)";
        chkMon.AutoSize = true;
        chkMon.ForeColor = Theme.Text;
        chkMon.Margin = new Padding(0, 2, 0, 4);
        body.Controls.Add(chkMon);
        body.Controls.Add(cbMon);

        cbIn.SelectedIndexChanged += (_, _) =>
        {
            if (loading || cbIn.SelectedItem is not DeviceItem d) return;
            S.InputDeviceId = d.Id; S.Save(); RestartEngine();
        };
        cbOut.SelectedIndexChanged += (_, _) =>
        {
            if (loading || cbOut.SelectedItem is not DeviceItem d) return;
            S.OutputDeviceId = d.Id; S.Save(); RestartEngine();
        };
        cbMon.SelectedIndexChanged += (_, _) =>
        {
            if (loading || cbMon.SelectedItem is not DeviceItem d) return;
            S.MonitorDeviceId = d.Id; S.Save(); RestartEngine();
        };
        chkMon.CheckedChanged += (_, _) =>
        {
            if (loading) return;
            S.Monitor = chkMon.Checked;
            cbMon.Enabled = chkMon.Checked;
            S.Save();
            RestartEngine();
        };

        body.Controls.Add(Head("Программа"));
        chkAuto.Text = "Запускать вместе с Windows (сразу в трей)";
        chkAuto.AutoSize = true;
        chkAuto.ForeColor = Theme.Text;
        chkAuto.Margin = new Padding(0, 0, 0, 4);
        chkAuto.CheckedChanged += (_, _) =>
        {
            if (loading) return;
            S.StartWithWindows = chkAuto.Checked;
            SetAutostart(chkAuto.Checked);
            S.Save();
        };
        body.Controls.Add(chkAuto);
        body.Controls.Add(Hint("Крестик сворачивает программу в трей. Выйти совсем — правый щелчок по значку у часов."));

        body.Controls.Add(Head("Как подключить Discord"));
        body.Controls.Add(Hint("Discord → Настройки → Голос и видео → Устройство ввода: «CABLE Output». Там же отключите шумоподавление и автоматическую регулировку усиления — иначе Discord испортит уже готовый звук. Устройство вывода по умолчанию в Windows менять не нужно. Тот же микрофон «CABLE Output» выбирается в любой другой программе."));

        Controls.Add(body);
    }

    void BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 196, BackColor = Theme.Panel };

        header.Controls.Add(new Label
        {
            Text = "MicStudio",
            Font = Theme.Title,
            ForeColor = Theme.Text,
            AutoSize = true,
            BackColor = Color.Transparent,
            Location = new Point(16, 10)
        });

        lblOn.Font = Theme.Small;
        lblOn.ForeColor = Theme.Muted;
        lblOn.AutoSize = true;
        lblOn.BackColor = Color.Transparent;
        lblOn.Location = new Point(19, 54);
        header.Controls.Add(lblOn);

        tgl.Location = new Point(460 - 18 - 56, 20);
        tgl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(tgl);

        mIn.Bounds = new Rectangle(18, 90, 424, 26);
        mIn.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        mOut.Bounds = new Rectangle(18, 120, 424, 26);
        mOut.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        header.Controls.Add(mIn);
        header.Controls.Add(mOut);

        lblStatus.Font = Theme.Small;
        lblStatus.ForeColor = Theme.Muted;
        lblStatus.AutoSize = false;
        lblStatus.BackColor = Color.Transparent;
        lblStatus.Bounds = new Rectangle(18, 154, 300, 36);
        header.Controls.Add(lblStatus);

        btnInstall.Text = "Установить";
        btnInstall.FlatStyle = FlatStyle.Flat;
        btnInstall.FlatAppearance.BorderSize = 0;
        btnInstall.BackColor = Theme.Accent;
        btnInstall.ForeColor = Theme.Bg;
        btnInstall.Font = new Font(Theme.Body, FontStyle.Bold);
        btnInstall.Cursor = Cursors.Hand;
        btnInstall.Bounds = new Rectangle(326, 152, 116, 32);
        btnInstall.Visible = false;
        btnInstall.Click += (_, _) => InstallVirtualMic();
        header.Controls.Add(btnInstall);

        Controls.Add(header);
    }

    void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть MicStudio", null, (_, _) => ShowMain());
        miPower.Click += (_, _) => tgl.Checked = !tgl.Checked;
        menu.Items.Add(miPower);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitApp());

        tray.ContextMenuStrip = menu;
        tray.Icon = iconOn;
        tray.Text = "MicStudio";
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };
        tray.Visible = true;
    }

    // ───────────── Логика ─────────────

    void OnSlider(string key, float v)
    {
        if (loading) return;
        switch (key)
        {
            case "nr": S.NoiseReduction = v; break;
            case "gate": S.Gate = v; break;
            case "warm": S.Warmth = v; break;
            case "mud": S.Mud = v; break;
            case "clar": S.Clarity = v; break;
            case "air": S.Air = v; break;
            case "comp": S.Compression = v; break;
            case "deess": S.DeEss = v; break;
            case "gin": S.InputGain = v; break;
            case "gout": S.OutputGain = v; break;
        }
        if (key != "gin" && key != "gout")
        {
            S.Preset = Custom;
            loading = true;
            cbPreset.SelectedItem = Custom;
            loading = false;
        }
        PushParams();
        ScheduleSave();
    }

    void ApplyPreset(Preset p)
    {
        S.Preset = p.Name;
        S.NoiseReduction = p.Nr;
        S.Gate = p.Gate;
        S.Warmth = p.Warmth;
        S.Mud = p.Mud;
        S.Clarity = p.Clarity;
        S.Air = p.Air;
        S.Compression = p.Comp;
        S.DeEss = p.DeEss;
        SyncSliders();
        PushParams();
        ScheduleSave();
    }

    void SyncSliders()
    {
        loading = true;
        sl["nr"].Value = S.NoiseReduction;
        sl["gate"].Value = S.Gate;
        sl["warm"].Value = S.Warmth;
        sl["mud"].Value = S.Mud;
        sl["clar"].Value = S.Clarity;
        sl["air"].Value = S.Air;
        sl["comp"].Value = S.Compression;
        sl["deess"].Value = S.DeEss;
        sl["gin"].Value = S.InputGain;
        sl["gout"].Value = S.OutputGain;
        int idx = cbPreset.Items.IndexOf(S.Preset);
        cbPreset.SelectedIndex = idx >= 0 ? idx : cbPreset.Items.IndexOf(Custom);
        loading = false;
    }

    void PushParams()
    {
        var p = engine.P;
        p.Enabled = S.ProcessingOn;
        p.InputGainDb = S.InputGain;
        p.OutputGainDb = S.OutputGain;
        p.NoiseReduction = S.NoiseReduction;
        p.GateDb = S.Gate;
        p.Warmth = S.Warmth;
        p.Mud = S.Mud;
        p.Clarity = S.Clarity;
        p.Air = S.Air;
        p.Compression = S.Compression;
        p.DeEss = S.DeEss;
        p.Touch();
        mIn.MarkerDb = S.Gate <= -69.9f ? float.NaN : S.Gate;
    }

    void ApplyPowerUi()
    {
        bool on = S.ProcessingOn;
        lblOn.Text = on ? "Обработка включена" : "Обработка выключена — голос идёт как есть";
        miPower.Checked = on;
        tray.Icon = on ? iconOn : iconOff;
        tray.Text = on ? "MicStudio — обработка включена" : "MicStudio — обработка выключена";
    }

    void ScheduleSave()
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    static void Fill(ComboBox cb, List<DeviceItem> items, string wantedId, string fallbackId, bool pickFirst)
    {
        cb.BeginUpdate();
        cb.Items.Clear();
        foreach (var d in items) cb.Items.Add(d);
        var sel = items.FirstOrDefault(d => d.Id == wantedId) ?? items.FirstOrDefault(d => d.Id == fallbackId);
        if (sel == null && pickFirst && items.Count > 0) sel = items[0];
        cb.SelectedItem = sel;
        cb.EndUpdate();
    }

    void RefreshDevices(bool force)
    {
        string sig = string.Join("|", Devices.Ids(DataFlow.Capture)) + "#" + string.Join("|", Devices.Ids(DataFlow.Render));
        if (!force && sig == devSig) return;
        devSig = sig;

        var ins = Devices.List(DataFlow.Capture).Where(d => !Devices.IsCableOutput(d.Name)).ToList();
        var outs = Devices.List(DataFlow.Render);
        string cableId = outs.FirstOrDefault(d => Devices.IsCableInput(d.Name))?.Id;
        cableExists = cableId != null;

        loading = true;
        Fill(cbIn, ins, S.InputDeviceId, Devices.DefaultId(DataFlow.Capture), true);
        Fill(cbOut, outs, S.OutputDeviceId, cableId, false);
        Fill(cbMon, outs, S.MonitorDeviceId, Devices.DefaultId(DataFlow.Render), true);
        loading = false;
    }

    string CurrentConfig()
    {
        var i = cbIn.SelectedItem as DeviceItem;
        var o = cbOut.SelectedItem as DeviceItem;
        var m = chkMon.Checked ? cbMon.SelectedItem as DeviceItem : null;
        return $"{i?.Id}|{o?.Id}|{m?.Id}";
    }

    void TryStart()
    {
        var i = cbIn.SelectedItem as DeviceItem;
        var o = cbOut.SelectedItem as DeviceItem;
        var m = chkMon.Checked ? cbMon.SelectedItem as DeviceItem : null;
        if (i == null || o == null) return;
        try
        {
            engine.Start(i.Id, o.Id, m?.Id);
            lastError = "";
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            engine.Stop();
        }
    }

    void RestartEngine()
    {
        engine.Stop();
        TryStart();
        UpdateStatus();
    }

    void Watchdog()
    {
        RefreshDevices(false);
        if (engine.Running && CurrentConfig() != engine.Config) engine.Stop();
        if (!engine.Running)
        {
            if (!string.IsNullOrEmpty(engine.LastError)) lastError = engine.LastError;
            TryStart();
        }
        UpdateStatus();
    }

    void UpdateStatus()
    {
        var o = cbOut.SelectedItem as DeviceItem;
        btnInstall.Visible = !cableExists;
        string t;
        Color c;
        if (cbIn.SelectedItem == null)
        {
            t = "Микрофон не найден. Подключите его — программа подхватит сама.";
            c = Theme.Bad;
        }
        else if (o == null)
        {
            t = "Виртуальный микрофон не установлен. Нажмите «Установить» — нужно один раз.";
            c = Theme.Accent;
        }
        else if (!engine.Running)
        {
            bool err = !string.IsNullOrEmpty(lastError);
            t = err ? "Не удалось запустить: " + lastError : "Подключаюсь…";
            c = err ? Theme.Bad : Theme.Muted;
        }
        else if (Devices.IsCableInput(o.Name))
        {
            t = "Работает. В Discord выберите микрофон «CABLE Output».";
            c = Theme.Good;
        }
        else
        {
            t = "Работает. Звук идёт на: " + o.Name;
            c = Theme.Good;
        }
        lblStatus.Text = t;
        lblStatus.ForeColor = c;
    }

    void InstallVirtualMic()
    {
        try { Process.Start(new ProcessStartInfo("https://vb-cable.com/") { UseShellExecute = true }); } catch { }
        MessageBox.Show(this,
            "Чтобы Discord и другие программы видели обработанный микрофон, Windows нужен виртуальный аудиодрайвер. " +
            "Он ставится один раз.\n\n" +
            "1. На открывшейся странице VB-CABLE скачайте драйвер (Download).\n" +
            "2. Распакуйте архив.\n" +
            "3. Правой кнопкой на VBCABLE_Setup_x64.exe → «Запуск от имени администратора» → Install Driver.\n" +
            "4. Перезагрузите компьютер.\n\n" +
            "После перезагрузки MicStudio подключится сам.",
            "Виртуальный микрофон", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    static void SetAutostart(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (on) key.SetValue("MicStudio", $"\"{Environment.ProcessPath}\" --tray");
            else key.DeleteValue("MicStudio", false);
        }
        catch { }
    }

    void ShowMain()
    {
        startHidden = false;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    void ExitApp()
    {
        exiting = true;
        uiTimer.Stop();
        watchdog.Stop();
        saveTimer.Stop();
        S.Save();
        engine.Dispose();
        tray.Visible = false;
        tray.Dispose();
        Application.Exit();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (startHidden)
        {
            if (!IsHandleCreated) CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!S.TrayHintShown)
            {
                tray.ShowBalloonTip(2500, "MicStudio работает в трее",
                    "Двойной щелчок по значку — открыть окно. Выход — правый щелчок по значку.", ToolTipIcon.Info);
                S.TrayHintShown = true;
                S.Save();
            }
            return;
        }
        base.OnFormClosing(e);
    }
}
