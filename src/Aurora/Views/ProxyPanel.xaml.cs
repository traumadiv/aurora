using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TdLib;
using Aurora.Services;

namespace Aurora.Views;

/// <summary>MTProxy / SOCKS5 list. Lives on the Settings "Прокси" page and in the proxy window of the login screen.</summary>
public partial class ProxyPanel : UserControl
{
    public ProxyPanel()
    {
        InitializeComponent();
        ListView.ItemsSource = Proxies.List;
        UseSystem.IsChecked = App.Cfg.UseSystemProxy;
        var sys = Proxies.ReadSystem();
        UseSystem.ToolTip = sys != null ? $"Сейчас в Windows: {sys.Kind} {sys.Server}:{sys.Port}" : "Сейчас в Windows прокси не включён";
        Loaded += async (_, _) =>
        {
            // A proxy link copied from a channel or a browser is what people usually have: offer it right away.
            try
            {
                if (LinkBox.Text.Length == 0 && Clipboard.ContainsText() && Proxies.Parse(Clipboard.GetText()) != null)
                    LinkBox.Text = Clipboard.GetText().Trim();
            }
            catch { /* clipboard busy */ }
            await Run(Proxies.Refresh);
            ListView.SelectedItem ??= Proxies.Active ?? Proxies.List.FirstOrDefault();
        };
    }

    async void UseSystem_Click(object sender, RoutedEventArgs e)
    {
        App.Cfg.UseSystemProxy = UseSystem.IsChecked == true;
        if (!Demo.On) App.Cfg.Save();
        await Run(() => Proxies.ApplySystem(App.Cfg.UseSystemProxy),
            App.Cfg.UseSystemProxy ? (Proxies.ReadSystem() is { } s ? $"Подключаемся через системный прокси {s.Server}:{s.Port}." : "В Windows сейчас нет прокси.") : "Системный прокси больше не используется.");
    }

    ProxyEntry? Selected => ListView.SelectedItem as ProxyEntry;

    async Task Run(Func<Task> act, string? done = null)
    {
        try
        {
            await act();
            if (done != null) Status.Text = done;
        }
        catch (TdException ex) { Status.Text = "Ошибка: " + (ex.Error?.Message ?? ex.Message); }
        catch (Exception ex) { Status.Text = "Ошибка: " + ex.Message; }
    }

    async Task Add(ProxyEntry e)
    {
        await Run(() => Proxies.Add(e), $"Прокси {e.Title} добавлен и включён.");
        ListView.SelectedItem = Proxies.List.FirstOrDefault(p => p.Server == e.Server && p.Port == e.Port);
        if (Selected != null) await Proxies.Ping(Selected);
    }

    async void AddLink_Click(object sender, RoutedEventArgs e)
    {
        var p = Proxies.Parse(LinkBox.Text);
        if (p == null)
        {
            Status.Text = "Это не похоже на ссылку прокси. Пример: tg://proxy?server=1.2.3.4&port=443&secret=…";
            return;
        }
        await Add(p);
        LinkBox.Clear();
    }

    void LinkBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddLink_Click(sender, e);
    }

    void Kind_Changed(object sender, RoutedEventArgs e)
    {
        if (SecretBox == null) return;
        var socks = KindSocks.IsChecked == true;
        SecretLabel.Text = socks ? "Логин:" : "Секрет:";
        SecretBox.Visibility = socks ? Visibility.Collapsed : Visibility.Visible;
        UserBox.Visibility = PassBox.Visibility = PassLabel.Visibility = socks ? Visibility.Visible : Visibility.Collapsed;
        if (PortBox.Text is "443" or "1080") PortBox.Text = socks ? "1080" : "443";
    }

    async void AddManual_Click(object sender, RoutedEventArgs e)
    {
        var socks = KindSocks.IsChecked == true;
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is <= 0 or > 65535 || ServerBox.Text.Trim().Length == 0)
        {
            Status.Text = "Укажите сервер и порт.";
            return;
        }
        if (!socks && SecretBox.Text.Trim().Length < 16)
        {
            Status.Text = "У MTProto-прокси должен быть секрет (обычно 32 символа или длинная строка, начинающаяся с ee).";
            return;
        }
        await Add(new ProxyEntry
        {
            Kind = socks ? "SOCKS5" : "MTProto", Server = ServerBox.Text.Trim(), Port = port,
            Secret = SecretBox.Text.Trim(), User = UserBox.Text.Trim(), Password = PassBox.Password,
        });
    }

    async void Use_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } p) await Run(() => Proxies.Enable(p), $"Подключаемся через {p.Title}.");
    }

    void List_DoubleClick(object sender, MouseButtonEventArgs e) => Use_Click(sender, e);

    async void Off_Click(object sender, RoutedEventArgs e) =>
        await Run(Proxies.Disable, "Прокси выключен, соединение напрямую.");

    async void Ping_Click(object sender, RoutedEventArgs e)
    {
        Status.Text = "";
        await Task.WhenAll(Proxies.List.ToList().Select(Proxies.Ping));
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        Clipboard.SetText(p.Link);
        Status.Text = "Ссылка скопирована: её можно отправить другу.";
    }

    async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } p) await Run(() => Proxies.Remove(p), $"Прокси {p.Title} удалён.");
    }
}
