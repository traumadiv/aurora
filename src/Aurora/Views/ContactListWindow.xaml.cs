using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Aurora.Models;
using Aurora.Services;

namespace Aurora.Views;

public partial class ContactListWindow : Window
{
    readonly ListCollectionView _contacts;
    readonly ListCollectionView _chats;
    string _chatFilter = "all";
    bool _forceClose;

    static readonly string[] GroupOrder = { "В сети", "Не в сети" };

    public ContactListWindow()
    {
        InitializeComponent();
        App.Chrome(this);
        Icon = Icons.Bitmap("online");

        var cfg = App.Cfg;
        Width = cfg.ListWidth;
        Height = cfg.ListHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (!double.IsNaN(cfg.ListLeft) && IsOnScreen(cfg.ListLeft, cfg.ListTop))
        {
            Left = cfg.ListLeft;
            Top = cfg.ListTop;
        }
        else
        {
            // ICQ lived at the right edge of the screen.
            Left = SystemParameters.WorkArea.Right - Width - 20;
            Top = SystemParameters.WorkArea.Top + 40;
        }
        Topmost = cfg.AlwaysOnTop;

        // Contacts: online on top, then offline, alphabetical inside.
        _contacts = new ListCollectionView(Tg.Contacts)
        {
            Filter = ContactVisible,
            IsLiveSorting = true, IsLiveGrouping = true, IsLiveFiltering = true,
        };
        _contacts.SortDescriptions.Add(new SortDescription(nameof(ChatItem.Title), ListSortDirection.Ascending));
        _contacts.LiveSortingProperties.Add(nameof(ChatItem.Title));
        _contacts.LiveGroupingProperties.Add(nameof(ChatItem.ContactGroup));
        _contacts.LiveFilteringProperties.Add(nameof(ChatItem.Presence));
        _contacts.LiveFilteringProperties.Add(nameof(ChatItem.HasUnread));
        _contacts.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ChatItem.ContactGroup)) { CustomSort = new ByGroup() });
        ContactsList.GroupStyle.Add((GroupStyle)FindResource("Groups"));
        ContactsList.ItemsSource = _contacts;

        // Chats: Telegram order (pinned first, then most recent).
        _chats = new ListCollectionView(Tg.ChatList)
        {
            CustomSort = new ByOrder(),
            Filter = ChatVisible,
            IsLiveSorting = true, IsLiveFiltering = true,
        };
        _chats.LiveSortingProperties.Add(nameof(ChatItem.Order));
        _chats.LiveFilteringProperties.Add(nameof(ChatItem.Kind));
        ChatsList.ItemsSource = _chats;

        Tabs.SelectedIndex = Math.Clamp(cfg.ListTab, 0, 1);

        Tg.MeChanged += UpdateMe;
        Tg.ConnectionChanged += OnConnection;
        UpdateMe();
        UpdateStatusButton();

        // Unread counter on the Chats tab.
        foreach (var c in Tg.ChatList) c.PropertyChanged += OnChatChanged;
        Tg.ChatList.CollectionChanged += OnChatListChanged;
        UpdateBadge();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { Search.Focus(); Search.SelectAll(); e.Handled = true; }
            if (e.Key == Key.M && Keyboard.Modifiers == ModifierKeys.Alt) { Menu_Click(MenuBtn, new RoutedEventArgs()); e.Handled = true; }
            if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control) { App.Current.ShowSettings(); e.Handled = true; }
            if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Control) { Tabs.SelectedIndex = 1 - Tabs.SelectedIndex; e.Handled = true; }
        };
    }

    ListBox ActiveList => Tabs.SelectedIndex == 1 ? ChatsList : ContactsList;
    ListCollectionView ActiveView => Tabs.SelectedIndex == 1 ? _chats : _contacts;

    static bool IsOnScreen(double x, double y) =>
        x >= SystemParameters.VirtualScreenLeft - 50 && x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50 &&
        y >= SystemParameters.VirtualScreenTop - 10 && y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50;

    void UpdateMe()
    {
        if (Tg.MyId == 0 || !Tg.Users.TryGetValue(Tg.MyId, out var me)) { MeName.Text = "Загрузка…"; return; }
        MeName.Text = (me.FirstName + " " + me.LastName).Trim();
        var user = me.Usernames?.ActiveUsernames?.FirstOrDefault();
        MeSub.Text = Tg.Connection.Length > 0 ? Tg.Connection : user != null ? "@" + user : "+" + me.PhoneNumber;
        Title = "Aurora — " + me.FirstName;
    }

    void OnConnection(string _)
    {
        UpdateMe();
        MeSub.SetResourceReference(TextBlock.ForegroundProperty, Tg.Connection.Length > 0 ? "Typing" : "SubText");
    }

    public void UpdateStatusButton()
    {
        var key = App.Current.Invisible ? "invisible" : "online";
        StatusIcon.Source = Icons.Get(key);
        MeIcon.Source = Icons.Get(key);
        StatusText.Text = App.Current.Invisible ? "Невидимый" : "В сети";
    }

    // ---------- unread badge on the Chats tab ----------

    void OnChatListChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (ChatItem c in e.NewItems) c.PropertyChanged += OnChatChanged;
        if (e.OldItems != null) foreach (ChatItem c in e.OldItems) c.PropertyChanged -= OnChatChanged;
        UpdateBadge();
    }

    void OnChatChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatItem.Unread) or nameof(ChatItem.Muted)) UpdateBadge();
    }

    void UpdateBadge()
    {
        var n = Tg.ChatList.Count(c => c.Unread > 0 && !c.Muted);
        ChatsBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        ChatsBadgeText.Text = n.ToString();
    }

    // ---------- filtering & sorting ----------

    string Query => Search.Text.Trim();

    bool ContactVisible(object o)
    {
        var c = (ChatItem)o;
        if (Query.Length > 0) return c.Title.Contains(Query, StringComparison.CurrentCultureIgnoreCase);
        return !App.Cfg.HideOffline || c.IsOnlineish || c.HasUnread;
    }

    bool ChatVisible(object o)
    {
        var c = (ChatItem)o;
        if (Query.Length > 0 && !c.Title.Contains(Query, StringComparison.CurrentCultureIgnoreCase)) return false;
        return _chatFilter switch
        {
            "private" => c.Kind is ChatKind.User or ChatKind.Bot or ChatKind.Saved,
            "groups" => c.Kind == ChatKind.Group,
            "channels" => c.Kind == ChatKind.Channel,
            _ => true,
        };
    }

    sealed class ByOrder : IComparer
    {
        public int Compare(object? x, object? y) => ((ChatItem)y!).Order.CompareTo(((ChatItem)x!).Order);
    }

    sealed class ByGroup : IComparer
    {
        public int Compare(object? x, object? y) =>
            Array.IndexOf(GroupOrder, ((CollectionViewGroup)x!).Name as string)
                .CompareTo(Array.IndexOf(GroupOrder, ((CollectionViewGroup)y!).Name as string));
    }

    void Filter_Checked(object sender, RoutedEventArgs e)
    {
        _chatFilter = (string)((FrameworkElement)sender).Tag;
        _chats?.Refresh();
    }

    void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Tabs) return;
        App.Cfg.ListTab = Tabs.SelectedIndex;
        if (Search.Text.Length > 0) ActiveView.Refresh();
    }

    void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _contacts.Refresh();
        _chats.Refresh();
        if (Search.Text.Length > 0 && ActiveList.Items.Count > 0) ActiveList.SelectedIndex = 0;
    }

    void Search_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Search.Clear(); ActiveList.Focus(); e.Handled = true; break;
            case Key.Down: MoveSelection(1); e.Handled = true; break;
            case Key.Up: MoveSelection(-1); e.Handled = true; break;
            case Key.Enter: OpenSelected(); e.Handled = true; break;
        }
    }

    void MoveSelection(int d)
    {
        var list = ActiveList;
        if (list.Items.Count == 0) return;
        list.SelectedIndex = Math.Clamp(list.SelectedIndex + d, 0, list.Items.Count - 1);
        list.ScrollIntoView(list.SelectedItem);
    }

    // ---------- list interaction ----------

    void OpenSelected()
    {
        if (ActiveList.SelectedItem is not ChatItem c) return;
        App.Current.OpenChat(c.Id);
        if (Search.Text.Length > 0) Search.Clear();
    }

    void List_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement((ListBox)sender, d) is ListBoxItem) OpenSelected();
    }

    void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
        else if (e.Key == Key.Escape && Search.Text.Length > 0) { Search.Clear(); e.Handled = true; }
    }

    /// <summary>Type-to-search: any printable key in a list goes to the search box.</summary>
    void List_TextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Text) || char.IsControl(e.Text[0])) return;
        Search.Focus();
        Search.Text += e.Text;
        Search.CaretIndex = Search.Text.Length;
        e.Handled = true;
    }

    /// <summary>Right click selects the row under the mouse first, so the menu acts on it.</summary>
    void List_RightDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement((ListBox)sender, d) is ListBoxItem item)
            item.IsSelected = true;
    }

    void List_ContextMenu(object sender, ContextMenuEventArgs e)
    {
        var list = (ListBox)sender;
        if (list.SelectedItem is not ChatItem c) { e.Handled = true; return; }
        var m = list.ContextMenu!;
        m.Items.Clear();
        var open = Item("Открыть чат", () => App.Current.OpenChat(c.Id), c.Icon);
        open.FontWeight = FontWeights.Bold;
        open.InputGestureText = "Enter";
        m.Items.Add(open);
        m.Items.Add(Item(c.Kind switch
        {
            ChatKind.Group => "О группе",
            ChatKind.Channel => "О канале",
            _ => "Информация",
        }, () => App.Current.ShowInfo(c)));
        m.Items.Add(new Separator());
        if (c.HasUnread) m.Items.Add(Item("Отметить прочитанным", () => Tg.MarkRead(c)));
        if (c.Kind != ChatKind.Saved)
            m.Items.Add(Item(c.Muted ? "Включить звук" : "Без звука", () => Tg.SetMuted(c, !c.Muted)));
        m.Items.Add(Item("Копировать имя", () => Clipboard.SetText(c.Title)));
    }

    void Group_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander { DataContext: CollectionViewGroup g } ex)
            ex.IsExpanded = !App.Cfg.CollapsedGroups.Contains(g.Name as string ?? "");
    }

    void Group_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander { DataContext: CollectionViewGroup g, IsLoaded: true } ex) return;
        var name = g.Name as string ?? "";
        App.Cfg.CollapsedGroups.Remove(name);
        if (!ex.IsExpanded) App.Cfg.CollapsedGroups.Add(name);
    }

    void Me_Click(object sender, MouseButtonEventArgs e) => App.Current.ShowSettings();

    // ---------- menus ----------

    void Menu_Click(object sender, RoutedEventArgs e)
    {
        var cfg = App.Cfg;
        var m = new ContextMenu { PlacementTarget = MenuBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        var settings = Item("Настройки…", () => App.Current.ShowSettings(), "online");
        settings.InputGestureText = "Ctrl+P";
        m.Items.Add(settings);
        m.Items.Add(Item("Мой профиль…", () => App.Current.ShowSettings("profile"), "saved"));
        m.Items.Add(new Separator());
        m.Items.Add(Check("Скрыть офлайн", cfg.HideOffline, v => { cfg.HideOffline = v; _contacts.Refresh(); }));
        m.Items.Add(Check("Звуки", cfg.PlaySounds, v => cfg.PlaySounds = v));
        m.Items.Add(Check("Поверх всех окон", cfg.AlwaysOnTop, v => { cfg.AlwaysOnTop = v; Topmost = v; }));
        m.Items.Add(new Separator());
        m.Items.Add(Item("Выйти из аккаунта…", ConfirmLogOut));
        m.Items.Add(Item("Выход", () => App.Current.Quit()));
        m.IsOpen = true;
    }

    public void ConfirmLogOut()
    {
        if (MessageBox.Show(this, "Выйти из аккаунта Telegram на этом компьютере?", "Aurora",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            App.Current.LogOut();
    }

    public void RefreshFilters()
    {
        _contacts.Refresh();
        _chats.Refresh();
        Topmost = App.Cfg.AlwaysOnTop;
    }

    void Status_Click(object sender, RoutedEventArgs e)
    {
        var m = new ContextMenu { PlacementTarget = StatusBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        m.Items.Add(Item("В сети", () => { App.Current.Invisible = false; UpdateStatusButton(); }, "online"));
        m.Items.Add(Item("Невидимый", () => { App.Current.Invisible = true; UpdateStatusButton(); }, "invisible"));
        m.IsOpen = true;
    }

    static MenuItem Item(string text, Action act, string? icon = null)
    {
        var mi = new MenuItem { Header = text };
        if (icon != null) mi.Icon = new Image { Source = Icons.Get(icon), Width = 16, Height = 16 };
        mi.Click += (_, _) => act();
        return mi;
    }

    static MenuItem Check(string text, bool value, Action<bool> set)
    {
        var mi = new MenuItem { Header = text, IsCheckable = true, IsChecked = value };
        mi.Click += (_, _) => { set(mi.IsChecked); App.Cfg.Save(); };
        return mi;
    }

    // ---------- lifetime ----------

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        var cfg = App.Cfg;
        if (WindowState == WindowState.Normal)
        {
            cfg.ListLeft = Left; cfg.ListTop = Top; cfg.ListWidth = Width; cfg.ListHeight = Height;
        }
        if (!Demo.On) cfg.Save();
        if (!_forceClose)
        {
            // Closing the contact list keeps Aurora in the tray, as ICQ did.
            e.Cancel = true;
            Hide();
        }
        else
        {
            Tg.MeChanged -= UpdateMe;
            Tg.ConnectionChanged -= OnConnection;
            Tg.ChatList.CollectionChanged -= OnChatListChanged;
        }
        base.OnClosing(e);
    }
}
