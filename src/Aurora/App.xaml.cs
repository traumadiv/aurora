using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TdLib;
using Aurora.Models;
using Aurora.Services;
using Aurora.Themes;
using Aurora.Views;
using Forms = System.Windows.Forms;

namespace Aurora;

public partial class App : Application
{
    public static Config Cfg { get; private set; } = null!;
    public static new App Current => (App)Application.Current;

    readonly Dictionary<long, ChatWindow> _chatWindows = new();
    readonly Queue<long> _pending = new();
    ContactListWindow? _list;
    LoginWindow? _login;
    Forms.NotifyIcon? _tray;
    DispatcherTimer? _trayBlink;
    Mutex? _single;
    bool _restartAfterClose;
    bool _invisible;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // the demo has no account or TDLib database, so it may run next to the real Aurora
        if (!e.Args.Contains("--demo"))
        {
            _single = new Mutex(true, @"Local\Aurora.SingleInstance", out var first);
            if (!first) { Shutdown(); return; }
        }

        Cfg = Config.Load();
        var forced = e.Args.FirstOrDefault(a => a.StartsWith("--skin="))?[7..] ?? (e.Args.Contains("--aero") ? "aero" : null);
        ApplySkin(forced ?? Cfg.Skin, rebuild: false);
        // screenshots: --accent=#RRGGBB recolors the skin for this run only (demo never saves the config)
        if (e.Args.Contains("--demo") && e.Args.FirstOrDefault(a => a.StartsWith("--accent=")) is { } acc)
            SetAccent((Color)ColorConverter.ConvertFromString(acc[9..]));
        Sounds.Init();
        Smileys.Init();
        SetupTray();

        Tg.AuthChanged += OnAuth;
        Tg.NewMessage += OnNewMessage;
        Tg.Fatal += err => { ShowLogin(); _login!.ShowApiError(err); };

        if (e.Args.Contains("--demo"))
        {
            Demo.Start();
            _ = Proxies.Add(new ProxyEntry { Server = "mtp.example.net", Port = 443, Secret = "ee0123456789abcdef0123456789abcdef6578616d706c652e636f6d" }, enable: true);
            _ = Proxies.Add(new ProxyEntry { Kind = "SOCKS5", Server = "10.0.0.2", Port = 1080 }, enable: false);
            ShowList();
            if (e.Args.Contains("--showcase"))
            {
                // README screenshots: a chat, the info card and settings open side by side.
                _list!.Tabs.SelectedIndex = 1;
                var chat = OpenChat(2);
                chat.Left = _list!.Left - chat.Width - 12;
                chat.Top = _list.Top;
                var page = e.Args.FirstOrDefault(a => a.StartsWith("--page="))?[7..] ?? "look";
                ShowSettings(page);
                _settings!.Left = chat.Left - 120;
                _settings.Top = chat.Top + chat.Height - 120;
            }
        }
        else if (Cfg.HasApi) Tg.Start(Cfg);
        else ShowLogin();
    }

    /// <summary>Called by the login window once api_id/api_hash are entered.</summary>
    public void StartClient() => Tg.Start(Cfg);

    // ---------- auth ----------

    void OnAuth(TdApi.AuthorizationState s)
    {
        switch (s)
        {
            case TdApi.AuthorizationState.AuthorizationStateReady:
                _login?.Close();
                _login = null;
                ShowList();
                Tg.SetOnline(!_invisible);
                break;

            case TdApi.AuthorizationState.AuthorizationStateWaitTdlibParameters:
                break;

            case TdApi.AuthorizationState.AuthorizationStateClosed:
                if (_restartAfterClose)
                {
                    _restartAfterClose = false;
                    Tg.Start(Cfg); // a fresh TDLib instance asks for the phone number again
                }
                break;

            case TdApi.AuthorizationState.AuthorizationStateLoggingOut:
            case TdApi.AuthorizationState.AuthorizationStateClosing:
                break;

            default:
                ShowLogin();
                _login!.OnAuthState(s);
                break;
        }
    }

    void ShowLogin()
    {
        if (_login == null)
        {
            _login = new LoginWindow();
            _login.Closed += (_, _) =>
            {
                var wasLogin = _login != null;
                _login = null;
                if (wasLogin && Tg.Auth is not TdApi.AuthorizationState.AuthorizationStateReady) Shutdown();
            };
        }
        _login.Show();
        _login.Activate();
    }

    public async void LogOut()
    {
        foreach (var w in _chatWindows.Values.ToList()) w.Close();
        _list?.ForceClose();
        _list = null;
        Tg.Chats.Clear();
        Tg.ChatList.Clear();
        _restartAfterClose = true;
        Archive.Clear();
        try { await Tg.Client.ExecuteAsync(new TdApi.LogOut()); } catch { }
    }

    // ---------- windows ----------

    public void ShowList()
    {
        _list ??= new ContactListWindow();
        _list.Show();
        if (_list.WindowState == WindowState.Minimized) _list.WindowState = WindowState.Normal;
        _list.Activate();
    }

    public ChatWindow OpenChat(long chatId, bool activate = true)
    {
        if (!_chatWindows.TryGetValue(chatId, out var w))
        {
            w = new ChatWindow(Tg.Chats[chatId]);
            _chatWindows[chatId] = w;
            w.Closed += (_, _) => _chatWindows.Remove(chatId);
            if (!activate) w.ShowActivated = false;
            w.Show();
        }
        if (activate)
        {
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
        }
        ClearPending(chatId);
        return w;
    }

    public static Color AccentOf(string skin) =>
        Cfg.Accents.TryGetValue(skin, out var hex) && TryColor(hex, out var c) ? c : SkinBuilder.DefaultAccent(skin);

    static bool TryColor(string hex, out Color c)
    {
        try { c = (Color)ColorConverter.ConvertFromString(hex); return true; }
        catch { c = default; return false; }
    }

    /// <summary>Recolors the current skin. Every brush is a DynamicResource, so no window has to be rebuilt.</summary>
    public void SetAccent(Color accent)
    {
        Cfg.Accents[Cfg.Skin] = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}";
        Resources.MergedDictionaries[1] = SkinBuilder.Build(Cfg.Skin, accent);
    }

    public void ResetAccent()
    {
        Cfg.Accents.Remove(Cfg.Skin);
        Resources.MergedDictionaries[1] = SkinBuilder.Build(Cfg.Skin, AccentOf(Cfg.Skin));
    }

    /// <summary>
    /// Swaps the skin dictionary (colors, glass, fonts, control styles) and redraws the windows.
    /// Icons are baked into bindings, so open windows are rebuilt in place.
    /// </summary>
    public void ApplySkin(string skin, bool rebuild = true)
    {
        if (!SkinBuilder.Skins.Any(s => s.Key == skin)) skin = "icq";
        Resources.MergedDictionaries[0] = new ResourceDictionary { Source = SkinBuilder.ThemeUri(skin) };
        Resources.MergedDictionaries[1] = SkinBuilder.Build(skin, AccentOf(skin));
        Icons.Skin = skin;
        Cfg.Skin = skin;
        if (_tray != null) _tray.Icon = Icons.TrayIcon(_invisible ? "invisible" : "online");
        if (!rebuild) return;

        if (_list != null)
        {
            var (left, top, visible, tab) = (_list.Left, _list.Top, _list.IsVisible, _list.Tabs.SelectedIndex);
            _list.ForceClose();
            _list = new ContactListWindow { Left = left, Top = top };
            _list.Tabs.SelectedIndex = tab;
            if (visible) _list.Show();
        }
        foreach (var (id, w) in _chatWindows.ToList())
        {
            var (left, top, width, height) = (w.Left, w.Top, w.Width, w.Height);
            w.Close();
            var fresh = OpenChat(id, activate: false);
            (fresh.Left, fresh.Top, fresh.Width, fresh.Height) = (left, top, width, height);
        }
        _settings?.RefreshSkin();
    }

    SettingsWindow? _settings;

    public void ShowSettings(string? page = null)
    {
        if (_settings == null)
        {
            _settings = new SettingsWindow(page);
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }
        else _settings.Select(page);
        _settings.Activate();
    }

    public void ShowInfo(ChatItem chat)
    {
        var w = new InfoWindow(chat);
        if (_list is { IsVisible: true }) w.Owner = _list;
        w.Show();
    }

    public void ConfirmLogOut()
    {
        if (_list != null) _list.ConfirmLogOut();
        else LogOut();
    }

    /// <summary>Settings window applied changes: refresh what depends on them.</summary>
    public void SettingsChanged(bool smileys)
    {
        _list?.RefreshFilters();
        if (smileys)
            foreach (var w in _chatWindows.Values) w.Rerender();
    }

    public static bool IsChatActive(long chatId) =>
        Current._chatWindows.TryGetValue(chatId, out var w) && w.IsActive;

    public bool Invisible
    {
        get => _invisible;
        set
        {
            _invisible = value;
            Tg.SetOnline(!value);
            if (_tray != null) _tray.Icon = Icons.TrayIcon(value ? "invisible" : "online");
            _list?.UpdateStatusButton();
        }
    }

    // ---------- notifications ----------

    void OnNewMessage(TdApi.Message m)
    {
        if (m.IsOutgoing || !Tg.Chats.TryGetValue(m.ChatId, out var chat)) return;
        if (chat.Muted || chat.Kind == ChatKind.Channel) return;
        if (chat.Kind == ChatKind.Group && !Cfg.NotifyGroups) return;

        if (_chatWindows.TryGetValue(m.ChatId, out var open))
        {
            if (open.IsActive) return;
            Flash(open);
        }
        else if (Cfg.PopupOnMessage)
        {
            Flash(OpenChat(m.ChatId, activate: false));
        }

        if (Cfg.PlaySounds) Sounds.Message();
        if (!_pending.Contains(m.ChatId)) _pending.Enqueue(m.ChatId);
        _trayBlink!.Start();
        if (_tray != null) _tray.Text = Trim($"Aurora — {chat.Title}: {Text.Describe(m.Content)}");
    }

    void ClearPending(long chatId)
    {
        if (!_pending.Contains(chatId)) return;
        var rest = _pending.Where(id => id != chatId).ToList();
        _pending.Clear();
        foreach (var id in rest) _pending.Enqueue(id);
        if (_pending.Count == 0) StopTrayBlink();
    }

    void StopTrayBlink()
    {
        _trayBlink?.Stop();
        if (_tray != null)
        {
            _tray.Icon = Icons.TrayIcon(_invisible ? "invisible" : "online");
            _tray.Text = "Aurora";
        }
    }

    static string Trim(string s) => s.Length > 63 ? s[..62] + "…" : s; // NotifyIcon.Text limit

    void SetupTray()
    {
        var online = Icons.TrayIcon("online");
        var msg = Icons.TrayIcon("msg");
        _tray = new Forms.NotifyIcon { Icon = online, Text = "Aurora", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Контакт-лист", null, (_, _) => ShowList());
        menu.Items.Add("Настройки…", null, (_, _) => ShowSettings());
        menu.Items.Add("В сети", null, (_, _) => Invisible = false);
        menu.Items.Add("Невидимый", null, (_, _) => Invisible = true);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Quit());
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button != Forms.MouseButtons.Left) return;
            // Like ICQ: a click on the blinking icon opens the waiting message.
            if (_pending.Count > 0 && Tg.Chats.ContainsKey(_pending.Peek())) OpenChat(_pending.Peek());
            else if (_list is { IsVisible: true, IsActive: true }) _list.Hide();
            else ShowList();
        };

        var on = false;
        _trayBlink = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) =>
        {
            on = !on;
            _tray.Icon = on ? msg : Icons.TrayIcon(_invisible ? "invisible" : "online");
        }, Dispatcher);
    }

    public async void Quit()
    {
        if (_tray != null) _tray.Visible = false;
        if (!Demo.On) Cfg.Save();
        await Tg.Shutdown();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }

    // ---------- shared window plumbing ----------

    [StructLayout(LayoutKind.Sequential)]
    struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }

    [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO fi);

    public static void Flash(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        var fi = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = h, dwFlags = 3 | 12, uCount = 0 };
        FlashWindowEx(ref fi); // tray + caption, until the window comes to the foreground
    }

    /// <summary>Wires the caption buttons drawn by the XpWindow template.</summary>
    public static void Chrome(Window w)
    {
        w.CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => w.Close()));
        w.CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(w)));
    }
}
