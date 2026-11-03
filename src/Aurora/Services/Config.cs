using System.IO;
using System.Text.Json;

namespace Aurora.Services;

/// <summary>Local settings stored in %APPDATA%\Aurora\config.json. Never committed anywhere.</summary>
public sealed class Config
{
    public int ApiId { get; set; }
    public string ApiHash { get; set; } = "";
    public bool HideOffline { get; set; }
    public bool PlaySounds { get; set; } = true;
    public bool PopupOnMessage { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool UseSmileys { get; set; } = true;
    /// <summary>"icq" (2003, beige and green) or "aero" (Frutiger Aero glass).</summary>
    public string Skin { get; set; } = "icq";
    /// <summary>Accent color per skin, "#RRGGBB". Missing means the skin's own default.</summary>
    public Dictionary<string, string> Accents { get; set; } = new();
    public bool NotifyGroups { get; set; } = true;
    public bool ShowPreview { get; set; } = true;
    /// <summary>Connect through the Windows proxy (VPN clients) like the official app does.</summary>
    public bool UseSystemProxy { get; set; } = true;
    /// <summary>Keep messages other people deleted (and earlier versions of edited ones) in the local archive.</summary>
    public bool KeepDeleted { get; set; } = true;
    public int ListTab { get; set; }
    public double ListLeft { get; set; } = double.NaN;
    public double ListTop { get; set; } = double.NaN;
    public double ListHeight { get; set; } = 560;
    public double ListWidth { get; set; } = 230;
    public List<string> CollapsedGroups { get; set; } = new();

    public static string Dir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aurora");

    static string FilePath => Path.Combine(Dir, "config.json");

    /// <summary>Keys compiled into the exe (see aurora.keys.example.props). Config keys are only a fallback for keyless builds.</summary>
    static readonly (int Id, string Hash) BuiltIn = ReadBuiltIn();

    public static bool HasBuiltInKeys => BuiltIn.Id > 0 && BuiltIn.Hash.Length > 0;
    public int EffectiveApiId => HasBuiltInKeys ? BuiltIn.Id : ApiId;
    public string EffectiveApiHash => HasBuiltInKeys ? BuiltIn.Hash : ApiHash;
    public bool HasApi => EffectiveApiId > 0 && EffectiveApiHash.Length > 0;

    static (int, string) ReadBuiltIn()
    {
        var meta = typeof(Config).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>()
            .ToDictionary(a => a.Key, a => a.Value ?? "");
        int.TryParse(meta.GetValueOrDefault("AuroraApiId"), out var id);
        return (id, meta.GetValueOrDefault("AuroraApiHash") ?? "");
    }

    public static Config Load()
    {
        // The app used to be called Telq: carry its settings and session over once.
        var old = Path.Combine(Path.GetDirectoryName(Dir)!, "Telq");
        try { if (!Directory.Exists(Dir) && Directory.Exists(old)) Directory.Move(old, Dir); } catch { }
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new Config();
        }
        catch { /* broken config: start fresh */ }
        return new Config();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
