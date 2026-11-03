using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TdLib;
using Aurora.Services;
using Aurora.Themes;

namespace Aurora.Views;

public partial class SettingsWindow : Window
{
    sealed record Page(string Key, string Title, string Icon, FrameworkElement View);

    readonly List<Page> _pages;
    string _origFirst = "", _origLast = "", _origUser = "", _origBio = "";

    public SettingsWindow(string? page = null)
    {
        InitializeComponent();
        App.Chrome(this);
        Icon = Icons.Bitmap("online");

        _pages =
        [
            new("profile", L("Мой профиль"), "saved", ProfilePage),
            new("look", L("Внешний вид"), "group", LookPage),
            new("notify", L("Уведомления"), "msg", NotifyPage),
            new("list", L("Контакт-лист"), "online", ListPage),
            new("archive", L("Архив сообщений"), "saved", ArchivePage),
            new("smileys", L("Смайлы"), "away", SmileysPage),
            new("proxy", L("Прокси"), "channel", ProxyPage),
            new("account", L("Аккаунт"), "offline", AccountPage),
            new("lang", "Язык / Language", "channel", LangPage),
        ];
        Nav.ItemsSource = _pages;

        var cfg = App.Cfg;
        PlaySounds.IsChecked = cfg.PlaySounds;
        NotifyGroups.IsChecked = cfg.NotifyGroups;
        Popup.IsChecked = cfg.PopupOnMessage;
        HideOffline.IsChecked = cfg.HideOffline;
        OnTop.IsChecked = cfg.AlwaysOnTop;
        KeepDeleted.IsChecked = cfg.KeepDeleted;
        UseSmileys.IsChecked = cfg.UseSmileys;
        FillSmileys();
        UpdateSoundName();
        FillSkins();
        FillArchive();
        FillLanguages();

        Loaded += async (_, _) =>
        {
            Nav.SelectedIndex = Math.Max(0, _pages.FindIndex(p => p.Key == page));
            await LoadProfile();
        };
    }

    public void Select(string? page)
    {
        var i = _pages.FindIndex(p => p.Key == page);
        if (i >= 0) Nav.SelectedIndex = i;
    }

    void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        for (var i = 0; i < _pages.Count; i++)
            _pages[i].View.Visibility = i == Nav.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- appearance ----------

    /// <summary>Called by App after a skin switch: icons in this window were baked for the old skin.</summary>
    public void RefreshSkin()
    {
        Icon = Icons.Bitmap("online");
        var i = Nav.SelectedIndex;
        Nav.ItemsSource = null;
        Nav.ItemsSource = _pages;
        Nav.SelectedIndex = i;
        FillSkins();
    }

    void FillSkins()
    {
        SkinCards.Children.Clear();
        foreach (var (key, title, note) in SkinBuilder.Skins)
        {
            var aero = key == "aero";
            var d = SkinBuilder.Build(key, App.AccentOf(key));
            Brush Res(string k) => d[k] as Brush ?? Brushes.Transparent;

            var radius = aero ? 8.0 : key == "classic" ? 0.0 : 2.0;
            var grid = new Grid();
            grid.Children.Add(new Border { Background = Res("Decor"), CornerRadius = new CornerRadius(radius) });
            grid.Children.Add(new Border
            {
                Height = 17, VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(radius, radius, 0, 0), Background = Res("CaptionActive"),
            });
            var rows = new StackPanel { Margin = new Thickness(6, 22, 6, 0) };
            foreach (var (icon, w, sel) in new[] { ("online", 74.0, false), ("online", 60.0, true), ("away", 54.0, false), ("offline", 66.0, false) })
            {
                var row = new Border
                {
                    Padding = new Thickness(3, 1, 3, 1), Margin = new Thickness(0, 0, 0, 2),
                    CornerRadius = new CornerRadius(aero ? 4 : 0),
                    Background = sel ? Res("Selected") : Brushes.Transparent,
                };
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                line.Children.Add(new Image { Source = Icons.GetFor(key, icon), Width = 12, Height = 12, Margin = new Thickness(0, 0, 5, 0) });
                line.Children.Add(new Border
                {
                    Width = w, Height = 5, CornerRadius = new CornerRadius(2.5), VerticalAlignment = VerticalAlignment.Center,
                    Background = sel ? Brushes.White : Res("SubText"), Opacity = sel ? 0.9 : 0.6,
                });
                row.Child = line;
                rows.Children.Add(row);
            }
            grid.Children.Add(rows);
            var preview = new Border
            {
                Width = 150, Height = 96, CornerRadius = new CornerRadius(radius),
                BorderBrush = Res("FrameActive"), BorderThickness = new Thickness(1), Background = Res("Face"), Child = grid,
            };

            var card = new StackPanel { Margin = new Thickness(4) };
            card.Children.Add(preview);
            card.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Margin = new Thickness(2, 5, 0, 0) });
            var noteText = new TextBlock { Text = note, Margin = new Thickness(2, 0, 0, 0) };
            noteText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            card.Children.Add(noteText);

            var rb = new RadioButton
            {
                GroupName = "skin", Content = card, IsChecked = App.Cfg.Skin == key, Margin = new Thickness(0, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            rb.Checked += (_, _) =>
            {
                if (App.Cfg.Skin == key) return;
                App.Current.ApplySkin(key);
                if (!Demo.On) App.Cfg.Save();
            };
            SkinCards.Children.Add(rb);
        }
        FillAccents();
    }

    // ---------- accent color ----------

    void FillAccents()
    {
        var current = App.AccentOf(App.Cfg.Skin);
        Swatches.Children.Clear();
        foreach (var (name, color) in SkinBuilder.Presets)
            Swatches.Children.Add(Swatch(color, name, color == current));
        AccentHex.Text = Hex(current);
        AccentDefault.IsEnabled = App.Cfg.Accents.ContainsKey(App.Cfg.Skin);
    }

    /// <summary>A glossy color drop. Built by hand so skin button styles don't touch it.</summary>
    FrameworkElement Swatch(Color c, string name, bool selected)
    {
        var drop = new Grid { Width = 26, Height = 26 };
        drop.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Fill = new LinearGradientBrush(SkinBuilder.Mix(c, Colors.White, 0.35), SkinBuilder.Mix(c, Colors.Black, 0.15), 90),
            Stroke = new SolidColorBrush(SkinBuilder.Mix(c, Colors.Black, 0.35)), StrokeThickness = 1,
        });
        drop.Children.Add(new System.Windows.Shapes.Ellipse { Margin = new Thickness(4, 2, 4, 12), Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB0, 255, 255, 255)) });
        var ring = new Border
        {
            Child = drop, Padding = new Thickness(2), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(2), BorderBrush = selected ? new SolidColorBrush(SkinBuilder.Mix(c, Colors.Black, 0.2)) : Brushes.Transparent,
            Background = Brushes.Transparent, ToolTip = name, Cursor = System.Windows.Input.Cursors.Hand,
        };
        ring.MouseLeftButtonUp += (_, _) => SetAccent(c);
        return ring;
    }

    void SetAccent(Color c)
    {
        App.Current.SetAccent(c);
        if (!Demo.On) App.Cfg.Save();
        FillSkins();
    }

    void AccentCustom_Click(object sender, RoutedEventArgs e)
    {
        var cur = App.AccentOf(App.Cfg.Skin);
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true, AnyColor = true, Color = System.Drawing.Color.FromArgb(cur.R, cur.G, cur.B),
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SetAccent(System.Windows.Media.Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
    }

    void AccentHex_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        var text = AccentHex.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        try { SetAccent((Color)ColorConverter.ConvertFromString(text)); }
        catch { AccentHex.Text = Hex(App.AccentOf(App.Cfg.Skin)); }
    }

    void AccentDefault_Click(object sender, RoutedEventArgs e)
    {
        App.Current.ResetAccent();
        if (!Demo.On) App.Cfg.Save();
        FillSkins();
    }

    static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";


    // ---------- profile ----------

    async Task LoadProfile()
    {
        if (!Tg.Users.TryGetValue(Tg.MyId, out var me)) return;
        FirstName.Text = _origFirst = me.FirstName;
        LastName.Text = _origLast = me.LastName;
        Username.Text = _origUser = me.Usernames?.EditableUsername ?? "";
        PhoneBox.Text = "+" + me.PhoneNumber;
        AccountInfo.Text = L("Вы вошли как {0}, +{1}.", (me.FirstName + " " + me.LastName).Trim(), me.PhoneNumber);
        Bio.Text = _origBio = await Tg.About(Tg.ItemForUser(Tg.MyId));
    }

    void Bio_TextChanged(object sender, TextChangedEventArgs e) => BioCount.Text = $"{Bio.Text.Length}/70";

    async Task<bool> SaveProfile()
    {
        ProfileError.Text = "";
        var first = FirstName.Text.Trim();
        var last = LastName.Text.Trim();
        var user = Username.Text.Trim().TrimStart('@');
        var bio = Bio.Text.Trim();
        if (first == _origFirst && last == _origLast && user == _origUser && bio == _origBio) return true;
        if (first.Length == 0)
        {
            Nav.SelectedItem = _pages[0];
            ProfileError.Text = L("Имя не может быть пустым.");
            return false;
        }
        try
        {
            await Tg.SaveProfile(first, last, bio, user);
            (_origFirst, _origLast, _origUser, _origBio) = (first, last, user, bio);
            return true;
        }
        catch (TdException ex)
        {
            Nav.SelectedItem = _pages[0];
            ProfileError.Text = (ex.Error?.Message ?? ex.Message) switch
            {
                "USERNAME_OCCUPIED" => L("Это имя пользователя уже занято."),
                "USERNAME_INVALID" => L("Недопустимое имя пользователя."),
                "FIRSTNAME_INVALID" => L("Недопустимое имя."),
                var m => m,
            };
            return false;
        }
    }

    // ---------- other pages ----------

    void ApplyLocal()
    {
        var cfg = App.Cfg;
        cfg.PlaySounds = PlaySounds.IsChecked == true;
        cfg.NotifyGroups = NotifyGroups.IsChecked == true;
        cfg.PopupOnMessage = Popup.IsChecked == true;
        cfg.HideOffline = HideOffline.IsChecked == true;
        cfg.AlwaysOnTop = OnTop.IsChecked == true;
        cfg.KeepDeleted = KeepDeleted.IsChecked == true;
        var keep = SelectedKeepDays;
        if (keep != cfg.ArchiveDays) { cfg.ArchiveDays = keep; Archive.Cleanup(keep); UpdateArchiveUsage(); }
        var smileysChanged = cfg.UseSmileys != (UseSmileys.IsChecked == true);
        cfg.UseSmileys = UseSmileys.IsChecked == true;
        if (!Demo.On) cfg.Save();
        App.Current.SettingsChanged(smileysChanged);
    }

    void FillSmileys()
    {
        SmileyPreview.Children.Clear();
        foreach (var s in Smileys.All)
        {
            var img = Smileys.MakeImage(s, 24);
            img.Margin = new Thickness(4);
            img.ToolTip = s.Insert;
            SmileyPreview.Children.Add(img);
        }
        var custom = File.Exists(Path.Combine(Smileys.PackDir, "smileys.txt"));
        PackInfo.Text = custom
            ? L("Загружен ваш пак смайлов: {0} шт.", Smileys.All.Count)
            : L("Встроенный набор. Чтобы поставить свой пак, положите картинки (GIF анимируются) и smileys.txt ")
              + L("в папку смайлов. Строка файла: smile.gif 🙂 :) :-)");
    }

    void SmileyFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(Smileys.PackDir);

    void SmileyReload_Click(object sender, RoutedEventArgs e)
    {
        Smileys.Init();
        FillSmileys();
        App.Current.SettingsChanged(true);
    }

    void UpdateSoundName()
    {
        var custom = Sounds.Custom;
        SoundName.Text = custom == null ? L("Стандартный") : L("Свой: ") + Path.GetFileName(custom);
        ResetSoundBtn.IsEnabled = custom != null;
    }

    void PickSound_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = L("Звук нового сообщения"),
            Filter = L("Звуки (*.wav;*.mp3)|*.wav;*.mp3"),
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Sounds.SetCustom(dlg.FileName);
            UpdateSoundName();
            Sounds.Message();
        }
        catch (Exception ex) { MessageBox.Show(this, L("Не получилось поставить звук: ") + ex.Message, "Aurora"); }
    }

    void ResetSound_Click(object sender, RoutedEventArgs e)
    {
        Sounds.ResetCustom();
        UpdateSoundName();
        Sounds.Message();
    }

    void TestSound_Click(object sender, RoutedEventArgs e) => Sounds.Message();

    static void OpenFolder(string dir)
    {
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    void LogOut_Click(object sender, RoutedEventArgs e)
    {
        Close();
        App.Current.ConfirmLogOut();
    }

    // ---------- archive ----------

    static string KeepLabel(int days) => days switch
    {
        0 => L("никогда не удалять"),
        1 => L("1 дня"),
        7 => L("недели"),
        30 => L("месяца"),
        90 => L("3 месяцев"),
        365 => L("года"),
        _ => L("{0} дн.", days),
    };

    void FillArchive()
    {
        ArchiveDays.Items.Clear();
        foreach (var d in Archive.KeepChoices) ArchiveDays.Items.Add(new ComboBoxItem { Content = KeepLabel(d), Tag = d });
        var cur = App.Cfg.ArchiveDays;
        ArchiveDays.SelectedItem = ArchiveDays.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == cur)
            ?? ArchiveDays.Items[2];
        UpdateArchiveUsage();
    }

    int SelectedKeepDays => ArchiveDays.SelectedItem is ComboBoxItem { Tag: int d } ? d : 30;

    void UpdateArchiveUsage()
    {
        var (bytes, chats) = Archive.Usage();
        ArchiveUsage.Text = bytes == 0
            ? L("Архив пуст.")
            : L("Архив занимает {0} в {1}.", Size(bytes), Plural(chats, "{0} чате", "{0} чатах", "{0} чатах"));
    }

    static string Size(long b) => b switch
    {
        < 1024 => L("{0} Б", b),
        < 1024 * 1024 => L("{0:0.#} КБ", b / 1024.0),
        _ => L("{0:0.#} МБ", b / 1024.0 / 1024.0),
    };

    void ArchiveCleanNow_Click(object sender, RoutedEventArgs e)
    {
        App.Cfg.ArchiveDays = SelectedKeepDays;
        if (App.Cfg.ArchiveDays == 0) { Status.Text = L("Срок не задан: архив хранится всегда."); return; }
        var n = Archive.Cleanup(App.Cfg.ArchiveDays);
        Status.Text = n == 0 ? L("Нечего удалять.") : Plural(n, "Удалено {0} сообщение", "Удалено {0} сообщения", "Удалено {0} сообщений");
        UpdateArchiveUsage();
    }

    void ArchiveClear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, L("Удалить весь архив? Удалённые сообщения и прошлые версии правок больше не будут видны."),
                "Aurora", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        Archive.Clear();
        Status.Text = L("Архив удалён.");
        UpdateArchiveUsage();
    }

    void ArchiveFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Archive.Dir);
        Process.Start(new ProcessStartInfo(Archive.Dir) { UseShellExecute = true });
    }

    // ---------- language ----------

    void FillLanguages()
    {
        LangList.Children.Clear();
        var chosen = App.Cfg.Language ?? Loc.Current;
        foreach (var lang in Loc.All)
        {
            var rb = new RadioButton { Content = lang.Name, Tag = lang.Code, GroupName = "lang", Margin = new Thickness(0, 0, 0, 6), IsChecked = lang.Code == chosen };
            rb.Checked += (_, _) => PickLanguage(lang.Code);
            LangList.Children.Add(rb);
        }
        LangNote.Text = L("Язык меняется после перезапуска Aurora.");
        LangRestart.Content = L("Перезапустить сейчас");
    }

    void PickLanguage(string code)
    {
        App.Cfg.Language = code;
        if (!Demo.On) App.Cfg.Save();
        // the hint is shown in the language the user just picked, so they can read it
        LangNote.Text = code == "ru" ? "Язык изменится после перезапуска Aurora." : "The language changes after Aurora restarts.";
        LangRestart.Content = code == "ru" ? "Перезапустить сейчас" : "Restart now";
        LangRestart.Visibility = code == Loc.Current ? Visibility.Collapsed : Visibility.Visible;
    }

    void LangRestart_Click(object sender, RoutedEventArgs e)
    {
        ApplyLocal();
        App.Current.Restart();
    }

    // ---------- buttons ----------

    async void Ok_Click(object sender, RoutedEventArgs e)
    {
        ApplyLocal();
        if (await SaveProfile()) Close();
    }

    async void Apply_Click(object sender, RoutedEventArgs e)
    {
        ApplyLocal();
        if (await SaveProfile()) Status.Text = L("Сохранено ✓");
    }
}
