using System.Text.Json;
using NAudio.CoreAudioApi;

namespace MicStudio;

public sealed class Settings
{
    public string InputDeviceId { get; set; } = "";
    public string OutputDeviceId { get; set; } = "";
    public string MonitorDeviceId { get; set; } = "";
    public bool Monitor { get; set; }
    public bool ProcessingOn { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool TrayHintShown { get; set; }

    public string Preset { get; set; } = "Студия";
    public float NoiseReduction { get; set; } = 60;
    public float Gate { get; set; } = -55;
    public float Warmth { get; set; } = 2;
    public float Mud { get; set; } = -2.5f;
    public float Clarity { get; set; } = 3;
    public float Air { get; set; } = 3;
    public float Compression { get; set; } = 55;
    public float DeEss { get; set; } = 40;
    public float InputGain { get; set; } = 0;
    public float OutputGain { get; set; } = 0;

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicStudio", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

public sealed record Preset(string Name, float Nr, float Gate, float Warmth, float Mud, float Clarity, float Air, float Comp, float DeEss);

public static class Presets
{
    public static readonly Preset[] All =
    {
        new("Студия",            60, -55, 2f,   -2.5f, 3f,   3f,   55, 40),
        new("Тёплый голос",      55, -55, 4.5f, -3f,   1.5f, 1.5f, 50, 55),
        new("Яркий (стрим)",     65, -52, 1f,   -2f,   4.5f, 4.5f, 65, 50),
        new("Минимум обработки", 30, -65, 0f,   -1f,   1f,   1f,   25, 20),
    };
}

public sealed record DeviceItem(string Id, string Name)
{
    public override string ToString() => Name;
}

public static class Devices
{
    public static List<string> Ids(DataFlow flow)
    {
        var list = new List<string>();
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                try { list.Add(d.ID); } catch { }
                d.Dispose();
            }
        }
        catch { }
        return list;
    }

    public static List<DeviceItem> List(DataFlow flow)
    {
        var list = new List<DeviceItem>();
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                try { list.Add(new DeviceItem(d.ID, d.FriendlyName)); } catch { }
                d.Dispose();
            }
        }
        catch { }
        return list;
    }

    public static string DefaultId(DataFlow flow)
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            using var d = en.GetDefaultAudioEndpoint(flow, flow == DataFlow.Capture ? Role.Communications : Role.Multimedia);
            return d.ID;
        }
        catch { return null; }
    }

    public static bool IsCableInput(string name) => name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase);
    public static bool IsCableOutput(string name) => name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase);
}
