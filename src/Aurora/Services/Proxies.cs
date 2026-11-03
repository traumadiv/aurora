using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TdLib;

namespace Aurora.Services;

/// <summary>One proxy as TDLib keeps it. TDLib stores the list in its own database, so it survives restarts.</summary>
public sealed class ProxyEntry : INotifyPropertyChanged
{
    public int Id { get; init; }
    public string Server { get; init; } = "";
    public int Port { get; init; }
    /// <summary>"MTProto", "SOCKS5" or "HTTP".</summary>
    public string Kind { get; init; } = "MTProto";
    public string Secret { get; init; } = "";
    public string User { get; init; } = "";
    public string Password { get; init; } = "";
    /// <summary>"system" marks the proxy Aurora took from the Windows settings.</summary>
    public string Comment { get; init; } = "";
    public bool IsSystem => Comment == Proxies.SystemTag;

    bool _enabled;
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    string _ping = "";
    /// <summary>"120 мс", "нет связи" or empty while not checked.</summary>
    public string Ping { get => _ping; set => Set(ref _ping, value); }

    public string Title => IsSystem ? $"{Server}:{Port} (системный)" : $"{Server}:{Port}";

    public TdApi.Proxy ToTd() => new()
    {
        Server = Server, Port = Port,
        Type = Kind switch
        {
            "SOCKS5" => new TdApi.ProxyType.ProxyTypeSocks5 { Username = User, Password = Password },
            "HTTP" => new TdApi.ProxyType.ProxyTypeHttp { Username = User, Password = Password, HttpOnly = false },
            _ => new TdApi.ProxyType.ProxyTypeMtproto { Secret = Secret },
        },
    };

    public string Link => Kind == "HTTP" ? $"http://{Server}:{Port}" : Kind == "SOCKS5"
        ? $"tg://socks?server={Server}&port={Port}" + (User.Length > 0 ? $"&user={Uri.EscapeDataString(User)}&pass={Uri.EscapeDataString(Password)}" : "")
        : $"tg://proxy?server={Server}&port={Port}&secret={Secret}";

    public event PropertyChangedEventHandler? PropertyChanged;

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public static class Proxies
{
    public static readonly ObservableCollection<ProxyEntry> List = new();
    static int _demoId;

    /// <summary>
    /// Reads tg://proxy, tg://socks, https://t.me/proxy and t.me/socks links, or a bare "server:port:secret".
    /// Returns null when it is not a proxy.
    /// </summary>
    public static ProxyEntry? Parse(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;

        var bare = text.Split(':');
        if (bare.Length == 3 && !text.Contains('/') && int.TryParse(bare[1], out var bp))
            return Valid(new ProxyEntry { Server = bare[0], Port = bp, Secret = bare[2] });

        if (text.StartsWith("t.me/", StringComparison.OrdinalIgnoreCase) || text.StartsWith("telegram.me/", StringComparison.OrdinalIgnoreCase))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;

        string kind;
        if (uri.Scheme == "tg") kind = uri.Host.ToLowerInvariant();
        else if (uri.Scheme is "http" or "https" && uri.Host.ToLowerInvariant() is "t.me" or "telegram.me" or "www.t.me")
            kind = uri.AbsolutePath.Trim('/').ToLowerInvariant();
        else return null;
        if (kind is not ("proxy" or "socks")) return null;

        var q = Query(uri.Query);
        if (!int.TryParse(q.GetValueOrDefault("port"), out var port)) return null;
        var entry = kind == "socks"
            ? new ProxyEntry
            {
                Kind = "SOCKS5", Server = q.GetValueOrDefault("server") ?? "", Port = port,
                User = q.GetValueOrDefault("user") ?? "", Password = q.GetValueOrDefault("pass") ?? "",
            }
            : new ProxyEntry { Server = q.GetValueOrDefault("server") ?? "", Port = port, Secret = q.GetValueOrDefault("secret") ?? "" };
        return Valid(entry);
    }

    static ProxyEntry? Valid(ProxyEntry e) =>
        e.Server.Length > 0 && e.Port is > 0 and < 65536 && (e.Kind == "SOCKS5" || e.Secret.Length >= 16) ? e : null;

    static Dictionary<string, string> Query(string query)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            // secrets are base64url or hex: '+' must not turn into a space here
            d[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
        }
        return d;
    }

    static bool Live => !Demo.On && Tg.Client != null;

    public static async Task Refresh()
    {
        if (!Live) return;
        var res = await Tg.Client.ExecuteAsync(new TdApi.GetProxies());
        List.Clear();
        foreach (var p in res.Proxies) List.Add(From(p));
    }

    static ProxyEntry From(TdApi.AddedProxy p) => new()
    {
        Id = p.Id, Server = p.Proxy.Server, Port = p.Proxy.Port, Enabled = p.IsEnabled,
        Kind = p.Proxy.Type switch
        {
            TdApi.ProxyType.ProxyTypeSocks5 => "SOCKS5",
            TdApi.ProxyType.ProxyTypeHttp => "HTTP",
            _ => "MTProto",
        },
        Comment = p.Comment ?? "",
        Secret = (p.Proxy.Type as TdApi.ProxyType.ProxyTypeMtproto)?.Secret ?? "",
        User = (p.Proxy.Type as TdApi.ProxyType.ProxyTypeSocks5)?.Username ?? (p.Proxy.Type as TdApi.ProxyType.ProxyTypeHttp)?.Username ?? "",
        Password = (p.Proxy.Type as TdApi.ProxyType.ProxyTypeSocks5)?.Password ?? (p.Proxy.Type as TdApi.ProxyType.ProxyTypeHttp)?.Password ?? "",
    };

    /// <summary>Adds a proxy and switches to it right away, like tapping a proxy link in the official app.</summary>
    public static async Task Add(ProxyEntry e, bool enable = true)
    {
        var dupe = List.FirstOrDefault(p => p.Server == e.Server && p.Port == e.Port && p.Kind == e.Kind);
        if (dupe != null)
        {
            if (enable) await Enable(dupe);
            return;
        }
        if (!Live)
        {
            var demo = new ProxyEntry
            {
                Id = ++_demoId, Server = e.Server, Port = e.Port, Kind = e.Kind, Secret = e.Secret, User = e.User, Password = e.Password,
            };
            if (enable) foreach (var p in List) p.Enabled = false;
            demo.Enabled = enable;
            List.Add(demo);
            return;
        }
        await Tg.Client.ExecuteAsync(new TdApi.AddProxy { Proxy = e.ToTd(), Enable = enable, Comment = e.Comment });
        await Refresh();
    }

    public static async Task Enable(ProxyEntry e)
    {
        if (Live) await Tg.Client.ExecuteAsync(new TdApi.EnableProxy { ProxyId = e.Id });
        foreach (var p in List) p.Enabled = p == e;
    }

    public static async Task Disable()
    {
        if (Live) await Tg.Client.ExecuteAsync(new TdApi.DisableProxy());
        foreach (var p in List) p.Enabled = false;
    }

    public static async Task Remove(ProxyEntry e)
    {
        if (Live) await Tg.Client.ExecuteAsync(new TdApi.RemoveProxy { ProxyId = e.Id });
        List.Remove(e);
    }

    public static async Task Ping(ProxyEntry e)
    {
        e.Ping = "проверка…";
        if (!Live)
        {
            await Task.Delay(400);
            e.Ping = $"{80 + e.Port % 90} мс";
            return;
        }
        try
        {
            var s = await Tg.Client.ExecuteAsync(new TdApi.PingProxy { Proxy = e.ToTd() }).WaitAsync(TimeSpan.FromSeconds(12));
            e.Ping = $"{Math.Round((s.Seconds_ ?? 0) * 1000)} мс";
        }
        catch { e.Ping = "нет связи"; }
    }

    public const string SystemTag = "system";

    /// <summary>
    /// The proxy from Windows settings, where VPN clients (v2rayN, Hiddify, Clash…) put themselves.
    /// TDLib ignores it on its own, so without this Aurora hangs on "Подключение…" where the official app works.
    /// </summary>
    public static ProxyEntry? ReadSystem()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (key?.GetValue("ProxyEnable") is not int enabled || enabled == 0) return null;
            if (key.GetValue("ProxyServer") is not string server || server.Trim().Length == 0) return null;

            // either "host:port" for everything or "http=host:port;https=host:port;socks=host:port"
            var kind = "HTTP";
            var addr = server.Trim();
            if (addr.Contains('='))
            {
                var parts = addr.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
                    .ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());
                if (parts.TryGetValue("socks", out var socks)) (kind, addr) = ("SOCKS5", socks);
                else addr = parts.GetValueOrDefault("https") ?? parts.GetValueOrDefault("http") ?? "";
            }
            var scheme = addr.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) addr = addr[(scheme + 3)..];
            var colon = addr.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(addr[(colon + 1)..].TrimEnd('/'), out var port)) return null;
            return new ProxyEntry { Kind = kind, Server = addr[..colon], Port = port, Comment = SystemTag };
        }
        catch { return null; }
    }

    /// <summary>
    /// Keeps the "system" proxy in TDLib in sync with Windows. A proxy the user turned on by hand always wins.
    /// </summary>
    public static async Task ApplySystem(bool use)
    {
        if (!Live) return;
        await Refresh();
        var current = List.FirstOrDefault(p => p.IsSystem);
        var found = use ? ReadSystem() : null;
        if (current != null && (found == null || current.Server != found.Server || current.Port != found.Port || current.Kind != found.Kind))
        {
            var wasOn = current.Enabled;
            await Remove(current);
            if (wasOn && found == null) await Disable();
            current = null;
        }
        if (found == null) return;
        if (List.Any(p => p.Enabled && !p.IsSystem)) return;
        if (current == null) await Add(found);
        else if (!current.Enabled) await Enable(current);
    }

    public static ProxyEntry? Active => List.FirstOrDefault(p => p.Enabled);
}
