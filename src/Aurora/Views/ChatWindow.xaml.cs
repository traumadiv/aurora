using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TdLib;
using Aurora.Models;
using Aurora.Services;

namespace Aurora.Views;

/// <summary>One window per conversation, like an ICQ message session.</summary>
public partial class ChatWindow : Window
{
    readonly ChatItem _chat;
    readonly Dictionary<long, Paragraph> _paras = new();
    readonly Dictionary<long, TdApi.Message> _msgs = new();
    readonly Dictionary<long, Run> _ticks = new();
    /// <summary>Messages Telegram deleted that we keep showing from the local archive.</summary>
    readonly HashSet<long> _deleted = new();
    long _editingId;
    ScrollViewer? _scroll;
    long _oldestId;
    long _lastReadOutbox;
    bool _loadingOlder, _reachedStart;
    DateTime _lastTypingSent;

    static readonly Regex Url = new(@"(https?://|www\.|t\.me/)[^\s<>""]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Message colors come from the skin: dark skins need light names and text.
    Brush Res(string key) => (Brush)FindResource(key);
    Brush Mine => Res("MineInk");
    Brush Theirs => Res("TheirsInk");
    Brush Gray => Res("Muted");
    Brush DeletedInk => Res("DeletedInk");
    Brush DeletedMark => Res("Danger");
    static readonly Brush[] Palette =
        new[] { "#C0261A", "#1E7A20", "#7A3FB8", "#B5651D", "#0E7C86", "#A3227A", "#5B6B12" }.Select(Frozen).ToArray();

    static Brush Lighter(Brush b)
    {
        var c = ((SolidColorBrush)b).Color;
        var l = new SolidColorBrush(Color.FromRgb((byte)(c.R + (255 - c.R) * 0.5), (byte)(c.G + (255 - c.G) * 0.5), (byte)(c.B + (255 - c.B) * 0.5)));
        l.Freeze();
        return l;
    }

    static Brush Frozen(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    public ChatWindow(ChatItem chat)
    {
        InitializeComponent();
        App.Chrome(this);
        _chat = chat;
        DataContext = chat;
        Title = chat.Title + " — сообщения";
        Icon = Icons.Bitmap(chat.Icon);

        // Cascade new windows a little so several sessions don't stack exactly.
        var n = Application.Current.Windows.OfType<ChatWindow>().Count();
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.WorkArea.Left + 160 + n % 8 * 26;
        Top = SystemParameters.WorkArea.Top + 100 + n % 8 * 26;

        Tg.NewMessage += OnNew;
        Tg.SendSucceeded += OnSent;
        Tg.MessagesDeleted += OnDeleted;
        Tg.ContentChanged += OnEdited;
        Tg.ReadOutbox += OnReadOutbox;

        Loaded += async (_, _) =>
        {
            _scroll = FindScroll(History);
            if (_scroll != null) _scroll.ScrollChanged += OnScroll;
            Input.Focus();
            await Init();
        };
        Activated += (_, _) => MarkRead();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && SmilePopup.IsOpen) { SmilePopup.IsOpen = false; e.Handled = true; return; }
            if (e.Key == Key.Escape && _editingId != 0) { CancelEdit(); e.Handled = true; return; }
            if (e.Key == Key.Escape || (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)) { Close(); e.Handled = true; }
            if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control) { SmilePopup.IsOpen = !SmilePopup.IsOpen; e.Handled = true; }
        };
        BuildSmileyPicker();
    }

    void BuildSmileyPicker()
    {
        SmileGrid.Children.Clear();
        SmileIcon.Source = Smileys.All.FirstOrDefault()?.Still;
        foreach (var s in Smileys.All)
        {
            var b = new Button
            {
                Content = Smileys.MakeImage(s, 22),
                ToolTip = s.Insert,
                Width = 30, Height = 30, Margin = new Thickness(1),
                Style = (Style)FindResource("ToolButton"),
                Padding = new Thickness(3),
                Focusable = false,
            };
            b.Click += (_, _) => InsertSmiley(s.Insert);
            SmileGrid.Children.Add(b);
        }
    }

    void InsertSmiley(string code)
    {
        var at = Input.CaretIndex;
        // Keep text codes apart from words so they are recognized on the other side.
        var pad = code.All(ch => ch < 128) && at > 0 && !char.IsWhiteSpace(Input.Text[at - 1]) ? " " : "";
        Input.Text = Input.Text.Insert(at, pad + code);
        Input.CaretIndex = at + pad.Length + code.Length;
        if (Keyboard.Modifiers != ModifierKeys.Shift) SmilePopup.IsOpen = false; // Shift+click keeps the panel open
        Input.Focus();
    }

    /// <summary>Redraw every message, e.g. after the smiley pack or setting changed.</summary>
    public void Rerender()
    {
        BuildSmileyPicker();
        var ordered = _msgs.Values.OrderBy(m => m.Id).ToList();
        Doc.Blocks.Clear();
        _paras.Clear();
        _msgs.Clear();
        _ticks.Clear();
        foreach (var m in ordered) Append(m);
        ScrollToEnd();
    }

    void Info_Click(object sender, RoutedEventArgs e) => App.Current.ShowInfo(_chat);

    async Task Init()
    {
        if (!Demo.On)
        try
        {
            // A contact from the address book may have no chat yet.
            if (_chat.Kind is ChatKind.User or ChatKind.Bot && _chat.UserId != 0)
                await Tg.Client.ExecuteAsync(new TdApi.CreatePrivateChat { UserId = _chat.UserId, Force = false });
            var full = await Tg.Client.ExecuteAsync(new TdApi.GetChat { ChatId = _chat.Id });
            _lastReadOutbox = full.LastReadOutboxMessageId;
            await Tg.Client.ExecuteAsync(new TdApi.OpenChat { ChatId = _chat.Id });
        }
        catch { }

        var batch = await Tg.History(_chat.Id, 0, 40);
        batch.Reverse();
        var from = batch.Count > 0 ? batch[0].Id : 0;
        foreach (var m in WithDeleted(batch, from, long.MaxValue)) Append(m);
        if (batch.Count == 0) { _reachedStart = true; ScrollToEnd(); return; }
        _oldestId = batch[0].Id;
        ScrollToEnd();
        MarkRead();
    }

    // ---------- rendering ----------

    Paragraph Build(TdApi.Message m)
    {
        var p = new Paragraph { Margin = new Thickness(0, 0, 0, 5), Tag = m.Id };
        var body = Text.Describe(m.Content);
        var deleted = _deleted.Contains(m.Id);
        var arch = Archive.Get(_chat.Id, m.Id);

        if (Text.IsService(m.Content))
        {
            p.Foreground = Gray;
            p.FontStyle = FontStyles.Italic;
            p.Inlines.Add(new Run(Tg.SenderName(m.SenderId) + " " + body[2..] + "  (" + Text.Stamp(m.Date) + ")"));
            return p;
        }

        var name = m.IsOutgoing ? "Вы" : Tg.SenderName(m.SenderId);
        if (_chat.Kind == ChatKind.Channel && !m.IsOutgoing) name = _chat.Title;
        var color = deleted ? DeletedInk : m.IsOutgoing ? Mine : _chat.Kind == ChatKind.Group ? ColorFor(m.SenderId) : Theirs;
        var head = new Run($"{name} ({Text.Stamp(m.Date)}):") { Foreground = color, FontWeight = FontWeights.Bold };
        p.Inlines.Add(head);
        if (deleted)
            p.Inlines.Add(new Run("  удалено" + (arch is { DeletedAt: > 0 } ? " в " + Text.Stamp(arch.DeletedAt) : ""))
            {
                Foreground = DeletedMark, FontSize = 10, FontWeight = FontWeights.Bold,
                ToolTip = "Сообщение удалили в Telegram. Aurora показывает копию, которую успела получить.",
            });
        if (m.EditDate > 0 || arch?.Versions.Count > 0)
        {
            var mark = new Run(arch?.Versions.Count > 0 ? "  изм. ▾" : "  изм.") { Foreground = Gray, FontSize = 10 };
            if (arch?.Versions.Count > 0)
            {
                mark.ToolTip = "Было:\n" + string.Join("\n\n", arch!.Versions);
                mark.Cursor = Cursors.Help;
                ToolTipService.SetShowDuration(mark, 60000);
            }
            p.Inlines.Add(mark);
        }
        if (m.IsOutgoing && !deleted)
        {
            var tick = new Run(TickText(m)) { Foreground = Gray, FontSize = 10 };
            _ticks[m.Id] = tick;
            p.Inlines.Add(tick);
        }
        p.Inlines.Add(new LineBreak());
        if (!deleted && m.Content is TdApi.MessageContent.MessageSticker st)
        {
            p.Inlines.Add(StickerInline(st.Sticker));
            return p;
        }
        if (!deleted && m.Content is TdApi.MessageContent.MessagePhoto ph)
        {
            p.Inlines.Add(PhotoInline(ph.Photo));
            body = ph.Caption?.Text ?? "";
            if (body.Length == 0) return p;
            p.Inlines.Add(new LineBreak());
        }
        var start = p.Inlines.Count;
        AddWithLinks(p, body);
        if (deleted)
            foreach (var i in p.Inlines.Skip(start)) i.Foreground = DeletedInk;
        return p;
    }

    // ---------- photos and stickers ----------

    static (double W, double H) Fit(double w, double h, double maxW, double maxH)
    {
        if (w <= 0 || h <= 0) return (maxW, maxH * 0.75);
        var k = Math.Min(1, Math.Min(maxW / w, maxH / h));
        return (Math.Max(60, w * k), Math.Max(40, h * k));
    }

    Inline PhotoInline(TdApi.Photo photo)
    {
        var big = Media.Largest(photo);
        var (w, h) = Fit(big?.Width ?? 0, big?.Height ?? 0, 300, 260);
        var img = new Image
        {
            Width = w, Height = h, Stretch = Stretch.UniformToFill, Source = Media.Mini(photo.Minithumbnail),
            Cursor = Cursors.Hand, ToolTip = "Открыть фото",
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var frame = new Border
        {
            Child = img, BorderThickness = new Thickness(1), Margin = new Thickness(0, 3, 0, 1), Padding = new Thickness(2),
            Background = Brushes.White,
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "Line");
        var px = (int)(w * 1.5); // sharp on scaled displays
        _ = LoadInto(img, Media.Fit(photo, px)?.Photo, px);
        img.MouseLeftButtonUp += async (_, _) =>
        {
            if (await Media.Path(big?.Photo) is { } path) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        };
        return new InlineUIContainer(frame) { BaselineAlignment = BaselineAlignment.Bottom };
    }

    Inline StickerInline(TdApi.Sticker s)
    {
        var (w, h) = Fit(s.Width, s.Height, 140, 140);
        var grid = new Grid { Width = w, Height = h, ToolTip = "Стикер " + s.Emoji, Background = Brushes.Transparent };
        // until the picture is in (or if it can't be decoded) the emoji stands in as a smiley
        var fallback = new TextBlock { FontSize = 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        Smileys.SetText(fallback, s.Emoji);
        var img = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        grid.Children.Add(fallback);
        grid.Children.Add(img);
        _ = LoadSticker(img, fallback, s, (int)(w * 1.5));
        return new InlineUIContainer(grid) { BaselineAlignment = BaselineAlignment.Bottom };
    }

    static async Task LoadInto(Image img, TdApi.File? f, int width)
    {
        if (await Media.Image(f, width) is { } bmp) img.Source = bmp;
    }

    static async Task LoadSticker(Image img, TextBlock fallback, TdApi.Sticker s, int width)
    {
        if (await Media.Image(Media.StickerStill(s), width) is not { } bmp) return;
        img.Source = bmp;
        fallback.Visibility = Visibility.Collapsed;
    }

    string TickText(TdApi.Message m) => m.SendingState switch
    {
        TdApi.MessageSendingState.MessageSendingStatePending => "  ⌛",
        TdApi.MessageSendingState.MessageSendingStateFailed => "  ✗ не отправлено",
        _ => m.Id <= _lastReadOutbox ? "  ✓✓" : "  ✓",
    };

    Brush ColorFor(TdApi.MessageSender? s)
    {
        var id = s switch
        {
            TdApi.MessageSender.MessageSenderUser u => u.UserId,
            TdApi.MessageSender.MessageSenderChat c => c.ChatId,
            _ => 0,
        };
        var b = Palette[(int)(Math.Abs(id) % Palette.Length)];
        return FindResource("DarkSkin") is true ? Lighter(b) : b;
    }

    static void AddWithLinks(Paragraph p, string text)
    {
        var pos = 0;
        foreach (Match mt in Url.Matches(text))
        {
            if (mt.Index > pos) AddWithSmileys(p, text[pos..mt.Index]);
            var raw = mt.Value.TrimEnd('.', ',', ')', '!', '?');
            var href = raw.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? raw : "https://" + raw;
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
            {
                var link = new Hyperlink(new Run(raw)) { NavigateUri = uri, Cursor = Cursors.Hand };
                link.RequestNavigate += (_, e) => Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                p.Inlines.Add(link);
            }
            else p.Inlines.Add(new Run(raw));
            pos = mt.Index + raw.Length;
        }
        if (pos < text.Length) AddWithSmileys(p, text[pos..]);
    }

    static void AddWithSmileys(Paragraph p, string text)
    {
        foreach (var (piece, sm) in Smileys.Split(text))
            p.Inlines.Add(sm == null ? new Run(piece) : Smileys.Make(sm, 20, piece));
    }

    void Append(TdApi.Message m)
    {
        if (_paras.ContainsKey(m.Id)) return;
        var p = Build(m);
        _paras[m.Id] = p;
        _msgs[m.Id] = m;
        Doc.Blocks.Add(p);
    }

    void Prepend(List<TdApi.Message> olderNewestFirst)
    {
        var first = Doc.Blocks.FirstBlock;
        foreach (var m in olderNewestFirst)
        {
            if (_paras.ContainsKey(m.Id)) continue;
            var p = Build(m);
            _paras[m.Id] = p;
            _msgs[m.Id] = m;
            if (first == null) Doc.Blocks.Add(p);
            else Doc.Blocks.InsertBefore(first, p);
            first = p;
        }
    }

    /// <summary>Weaves archived deleted messages (ids in [from, to]) into an oldest-first batch.</summary>
    List<TdApi.Message> WithDeleted(List<TdApi.Message> oldestFirst, long from, long to)
    {
        if (!App.Cfg.KeepDeleted) return oldestFirst;
        var have = oldestFirst.Select(m => m.Id).ToHashSet();
        var extra = Archive.DeletedBetween(_chat.Id, from, to).Where(r => !have.Contains(r.Id) && !_msgs.ContainsKey(r.Id)).ToList();
        if (extra.Count == 0) return oldestFirst;
        foreach (var r in extra) _deleted.Add(r.Id);
        return oldestFirst.Concat(extra.Select(r => r.ToMessage())).OrderBy(m => m.Id).ToList();
    }

    // ---------- message menu: edit, copy, delete ----------

    TdApi.Message? MessageAt(Point pt)
    {
        var pos = History.GetPositionFromPoint(pt, true);
        return pos?.Paragraph?.Tag is long id && _msgs.TryGetValue(id, out var m) ? m : null;
    }

    static bool IsText(TdApi.Message m) => m.Content is TdApi.MessageContent.MessageText;

    void History_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var m = MessageAt(Mouse.GetPosition(History));
        var menu = History.ContextMenu;
        menu.Items.Clear();
        if (!History.Selection.IsEmpty)
            menu.Items.Add(MenuItem("Копировать выделенное", () => Clipboard.SetText(History.Selection.Text)));
        if (m != null)
        {
            var deleted = _deleted.Contains(m.Id);
            if (m.IsOutgoing && IsText(m) && !deleted)
            {
                var edit = MenuItem("Редактировать", () => BeginEdit(m));
                edit.InputGestureText = "↑";
                menu.Items.Insert(0, edit);
            }
            menu.Items.Add(MenuItem("Копировать текст", () => Clipboard.SetText(Text.Describe(m.Content))));
            if (Archive.Get(_chat.Id, m.Id) is { Versions.Count: > 0 } arch)
                menu.Items.Add(MenuItem("История правок…", () => MessageBox.Show(this,
                    string.Join("\n\n", arch.Versions.Select((v, i) => $"{i + 1}. {v}")) + $"\n\nСейчас: {arch.Text}",
                    "Как менялось сообщение", MessageBoxButton.OK, MessageBoxImage.Information)));
            menu.Items.Add(new Separator());
            if (deleted)
                menu.Items.Add(MenuItem("Убрать из архива", () => ForgetDeleted(m.Id)));
            else
            {
                menu.Items.Add(MenuItem("Удалить у меня", () => Delete(m, revoke: false)));
                if (m.IsOutgoing || _chat.Kind is ChatKind.User or ChatKind.Bot)
                    menu.Items.Add(MenuItem(_chat.Kind is ChatKind.User or ChatKind.Bot ? "Удалить у всех" : "Удалить для всех", () => Delete(m, revoke: true)));
            }
        }
        if (menu.Items.Count == 0) { e.Handled = true; return; }
        if (menu.Items[^1] is Separator) menu.Items.RemoveAt(menu.Items.Count - 1);
    }

    static MenuItem MenuItem(string header, Action act)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => act();
        return mi;
    }

    async void Delete(TdApi.Message m, bool revoke)
    {
        if (MessageBox.Show(this, revoke ? "Удалить сообщение у всех?" : "Удалить сообщение только у вас?", "Aurora",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Archive.Forget(_chat.Id, [m.Id]); // deleted on purpose: no ghost copy
        if (Demo.On) { Tg.DemoDelete(_chat.Id, m.Id); return; }
        try { await Tg.Client.ExecuteAsync(new TdApi.DeleteMessages { ChatId = _chat.Id, MessageIds = [m.Id], Revoke = revoke }); }
        catch (TdException ex) { Note("Не удалось удалить: " + (ex.Error?.Message ?? ex.Message)); }
    }

    void ForgetDeleted(long id)
    {
        Archive.Forget(_chat.Id, [id]);
        _deleted.Remove(id);
        _msgs.Remove(id);
        if (_paras.Remove(id, out var p)) Doc.Blocks.Remove(p);
    }

    void BeginEdit(TdApi.Message m)
    {
        _editingId = m.Id;
        var text = Text.Describe(m.Content);
        EditPreview.Text = text.ReplaceLineEndings(" ");
        EditBar.Visibility = Visibility.Visible;
        SendText.Text = "Сохранить";
        Input.Text = text;
        Input.CaretIndex = Input.Text.Length;
        Input.Focus();
    }

    void CancelEdit()
    {
        if (_editingId == 0) return;
        _editingId = 0;
        EditBar.Visibility = Visibility.Collapsed;
        SendText.Text = "Отправить";
        Input.Clear();
    }

    void CancelEdit_Click(object sender, RoutedEventArgs e) => CancelEdit();

    async Task SaveEdit(string text)
    {
        var id = _editingId;
        if (!_msgs.TryGetValue(id, out var m)) { CancelEdit(); return; }
        if (text == Text.Describe(m.Content)) { CancelEdit(); return; }
        var content = new TdApi.InputMessageContent.InputMessageText
        {
            Text = new TdApi.FormattedText { Text = text, Entities = Array.Empty<TdApi.TextEntity>() },
        };
        try
        {
            if (Demo.On)
                Tg.DemoEdit(_chat.Id, id, new TdApi.MessageContent.MessageText { Text = content.Text });
            else
                await Tg.Client.ExecuteAsync(new TdApi.EditMessageText { ChatId = _chat.Id, MessageId = id, InputMessageContent = content });
            CancelEdit();
        }
        catch (TdException ex)
        {
            Note((ex.Error?.Message ?? ex.Message) switch
            {
                "MESSAGE_EDIT_TIME_EXPIRED" => "Это сообщение уже нельзя изменить: прошло больше 48 часов.",
                "MESSAGE_NOT_MODIFIED" => "Текст не изменился.",
                "Message can't be edited" => "Это сообщение нельзя изменить.",
                var s => "Не удалось изменить: " + s,
            });
        }
    }

    void Note(string text)
    {
        Doc.Blocks.Add(new Paragraph(new Run(text)) { Foreground = Theirs, FontStyle = FontStyles.Italic });
        ScrollToEnd();
    }

    // ---------- live updates ----------

    void OnNew(TdApi.Message m)
    {
        if (m.ChatId != _chat.Id) return;
        var stick = IsNearBottom() || m.IsOutgoing;
        Append(m);
        if (stick) ScrollToEnd();
        if (IsActive) MarkRead();
    }

    void OnSent(TdApi.Message m, long oldId)
    {
        if (m.ChatId != _chat.Id || !_paras.Remove(oldId, out var p)) return;
        _msgs.Remove(oldId);
        _ticks.Remove(oldId);
        var fresh = Build(m);
        _paras[m.Id] = fresh;
        _msgs[m.Id] = m;
        Doc.Blocks.InsertBefore(p, fresh);
        Doc.Blocks.Remove(p);
    }

    void OnDeleted(long chatId, long[] ids)
    {
        if (chatId != _chat.Id) return;
        foreach (var id in ids)
        {
            if (id == _editingId) CancelEdit();
            if (!_paras.TryGetValue(id, out var p)) continue;
            _ticks.Remove(id);
            if (App.Cfg.KeepDeleted && Archive.Get(chatId, id) != null)
            {
                _deleted.Add(id);
                var fresh = Build(_msgs[id]);
                _paras[id] = fresh;
                Doc.Blocks.InsertBefore(p, fresh);
                Doc.Blocks.Remove(p);
                continue;
            }
            _paras.Remove(id);
            Doc.Blocks.Remove(p);
            _msgs.Remove(id);
        }
    }

    void OnEdited(long chatId, long id, TdApi.MessageContent content)
    {
        if (chatId != _chat.Id || !_msgs.TryGetValue(id, out var m) || !_paras.TryGetValue(id, out var old)) return;
        m.Content = content;
        if (m.EditDate == 0) m.EditDate = 1;
        var fresh = Build(m);
        _paras[id] = fresh;
        Doc.Blocks.InsertBefore(old, fresh);
        Doc.Blocks.Remove(old);
    }

    void OnReadOutbox(long chatId, long lastRead)
    {
        if (chatId != _chat.Id) return;
        _lastReadOutbox = lastRead;
        foreach (var (id, run) in _ticks)
            if (id <= lastRead && _msgs.TryGetValue(id, out var m)) run.Text = TickText(m);
    }

    void MarkRead()
    {
        if (_msgs.Count == 0 || _chat.Unread == 0) return;
        if (Demo.On) { Demo.Read(_chat.Id); return; }
        var ids = _msgs.Keys.OrderByDescending(x => x).Take(50).ToArray();
        Tg.Fire(new TdApi.ViewMessages
        {
            ChatId = _chat.Id, MessageIds = ids, Source = new TdApi.MessageSource.MessageSourceChatHistory(), ForceRead = true,
        });
    }

    // ---------- scrolling ----------

    static ScrollViewer? FindScroll(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is ScrollViewer sv) return sv;
            if (FindScroll(c) is { } found) return found;
        }
        return null;
    }

    bool IsNearBottom() => _scroll == null || _scroll.VerticalOffset >= _scroll.ScrollableHeight - 40;

    void ScrollToEnd() => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => History.ScrollToEnd());

    async void OnScroll(object sender, ScrollChangedEventArgs e)
    {
        if (_loadingOlder || _reachedStart || _oldestId == 0 || e.VerticalOffset > 30 || e.VerticalChange >= 0) return;
        _loadingOlder = true;
        try
        {
            var older = await Tg.History(_chat.Id, _oldestId, 50);
            var before = _scroll!.ExtentHeight - _scroll.VerticalOffset;
            var low = older.Count > 0 ? older[^1].Id : 0;
            var merged = WithDeleted(Enumerable.Reverse(older).ToList(), low, _oldestId - 1);
            merged.Reverse();
            Prepend(merged);
            if (older.Count == 0) { _reachedStart = true; return; }
            _oldestId = older[^1].Id;
            History.UpdateLayout();
            _scroll.ScrollToVerticalOffset(_scroll.ExtentHeight - before); // keep the reader's place
        }
        finally { _loadingOlder = false; }
    }

    // ---------- sending ----------

    void Input_KeyDown(object sender, KeyEventArgs e)
    {
        // Up in an empty box edits your last message, like in Telegram
        if (e.Key == Key.Up && Input.Text.Length == 0 && _editingId == 0)
        {
            var last = _msgs.Values.Where(m => m.IsOutgoing && IsText(m) && !_deleted.Contains(m.Id) && m.SendingState == null)
                .OrderByDescending(m => m.Id).FirstOrDefault();
            if (last != null) { BeginEdit(last); e.Handled = true; return; }
        }
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && PasteImage()) { e.Handled = true; return; }
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            Send();
        }
    }

    void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (Input.Text.Length == 0 || (DateTime.UtcNow - _lastTypingSent).TotalSeconds < 4) return;
        _lastTypingSent = DateTime.UtcNow;
        Tg.Fire(new TdApi.SendChatAction { ChatId = _chat.Id, Action = new TdApi.ChatAction.ChatActionTyping() });
    }

    void Send_Click(object sender, RoutedEventArgs e) => Send();

    async void Send()
    {
        var text = Input.Text.Trim();
        if (text.Length == 0) return;
        if (_editingId != 0) { await SaveEdit(text); return; }
        Input.Clear();
        Input.Focus();
        if (Demo.On) { Demo.Send(_chat.Id, text); return; }
        try
        {
            await Tg.Client.ExecuteAsync(new TdApi.SendMessage
            {
                ChatId = _chat.Id,
                InputMessageContent = new TdApi.InputMessageContent.InputMessageText
                {
                    Text = new TdApi.FormattedText { Text = text, Entities = Array.Empty<TdApi.TextEntity>() },
                },
            });
        }
        catch (TdException ex)
        {
            Input.Text = text; // give the text back instead of losing it
            var p = new Paragraph(new Run("Не отправлено: " + (ex.Error?.Message ?? ex.Message))) { Foreground = Theirs, FontStyle = FontStyles.Italic };
            Doc.Blocks.Add(p);
            ScrollToEnd();
        }
    }

    // ---------- photos and stickers: sending ----------

    void Photo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Отправить фото",
            Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|Все файлы|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var f in dlg.FileNames) SendPhoto(f);
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        if (DroppedImages(e).Length == 0) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        var files = DroppedImages(e);
        if (files.Length == 0) return;
        e.Handled = true;
        foreach (var f in files) SendPhoto(f);
    }

    static string[] DroppedImages(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] files ? files.Where(Media.IsImageFile).ToArray() : Array.Empty<string>();

    /// <summary>Ctrl+V with a picture (a screenshot or a copied file) sends it as a photo.</summary>
    bool PasteImage()
    {
        string? path = null;
        if (Clipboard.ContainsFileDropList())
            path = Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(Media.IsImageFile);
        else if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
        {
            path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"aurora-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            using var fs = System.IO.File.Create(path);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            enc.Save(fs);
        }
        if (path == null) return false;
        var note = Input.Text.Trim().Length > 0 ? "\nТекст из поля ввода станет подписью." : "";
        if (MessageBox.Show(this, "Отправить картинку из буфера обмена?" + note, "Aurora",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            SendPhoto(path);
        return true;
    }

    async void SendPhoto(string path)
    {
        int w, h;
        try
        {
            var frame = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            (w, h) = (frame.PixelWidth, frame.PixelHeight);
        }
        catch { Note("Это не похоже на картинку: " + System.IO.Path.GetFileName(path)); return; }

        // the typed text goes along as the caption
        var caption = _editingId == 0 ? Input.Text.Trim() : "";
        if (caption.Length > 0) Input.Clear();
        var text = new TdApi.FormattedText { Text = caption, Entities = Array.Empty<TdApi.TextEntity>() };

        if (Demo.On)
        {
            var file = new TdApi.File { Id = path.GetHashCode(), Local = new TdApi.LocalFile { Path = path, IsDownloadingCompleted = true } };
            Demo.SendContent(_chat.Id, new TdApi.MessageContent.MessagePhoto
            {
                Caption = text,
                Photo = new TdApi.Photo { Sizes = [new TdApi.PhotoSize { Type = "x", Photo = file, Width = w, Height = h }] },
            });
            return;
        }
        await SendContent(new TdApi.InputMessageContent.InputMessagePhoto
        {
            Photo = new TdApi.InputPhoto { Photo = new TdApi.InputFile.InputFileLocal { Path = path }, Width = w, Height = h },
            Caption = text,
        });
    }

    async void SendSticker(TdApi.Sticker s)
    {
        StickerPopup.IsOpen = false;
        await SendContent(new TdApi.InputMessageContent.InputMessageSticker
        {
            Sticker = new TdApi.InputSticker { Sticker = new TdApi.InputFile.InputFileId { Id = s.Sticker_.Id }, Width = s.Width, Height = s.Height },
            Emoji = s.Emoji,
        });
    }

    async Task SendContent(TdApi.InputMessageContent content)
    {
        try
        {
            await Tg.Client.ExecuteAsync(new TdApi.SendMessage { ChatId = _chat.Id, InputMessageContent = content });
        }
        catch (TdException ex) { Note("Не отправлено: " + (ex.Error?.Message ?? ex.Message)); }
    }

    // Sticker sets are the same for every window, so they are fetched once.
    static TdApi.Sticker[]? _recentStickers;
    static TdApi.StickerSetInfo[]? _stickerSets;
    static readonly Dictionary<long, TdApi.Sticker[]> _setStickers = new();
    Button? _stickerTab;

    async void StickerPopup_Opened(object? sender, EventArgs e)
    {
        if (StickerSets.Children.Count > 0) return;
        if (Demo.On || Tg.Client == null)
        {
            StickerNote.Text = "Здесь появятся стикеры из ваших наборов Telegram, когда вы войдёте в аккаунт.";
            return;
        }
        StickerNote.Text = "Загружаю наборы…";
        try
        {
            _recentStickers ??= (await Tg.Client.ExecuteAsync(new TdApi.GetRecentStickers { IsAttached = false })).Stickers_;
            _stickerSets ??= (await Tg.Client.ExecuteAsync(new TdApi.GetInstalledStickerSets { StickerType = new TdApi.StickerType.StickerTypeRegular() })).Sets;
        }
        catch (TdException ex) { StickerNote.Text = "Не удалось загрузить стикеры: " + (ex.Error?.Message ?? ex.Message); return; }

        StickerNote.Text = "";
        var recent = _recentStickers;
        if (recent.Length > 0)
            AddStickerTab(new TextBlock { Text = "Недавние", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) },
                "Недавние", () => Task.FromResult(recent));
        foreach (var set in _stickerSets)
        {
            var icon = new Image { Width = 26, Height = 26 };
            _ = LoadInto(icon, Media.ThumbFile(set.Thumbnail) ?? set.Covers.Select(c => Media.StickerStill(c, true)).FirstOrDefault(f => f != null), 40);
            var id = set.Id;
            AddStickerTab(icon, Text.NoEmoji(set.Title), async () =>
            {
                if (!_setStickers.TryGetValue(id, out var list))
                    _setStickers[id] = list = (await Tg.Client.ExecuteAsync(new TdApi.GetStickerSet { SetId = id })).Stickers;
                return list;
            });
        }
        if (StickerSets.Children.Count == 0) { StickerNote.Text = "У вас нет наборов стикеров. Добавьте их в Telegram на телефоне."; return; }
        ((Button)StickerSets.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    void AddStickerTab(UIElement content, string title, Func<Task<TdApi.Sticker[]>> load)
    {
        var tab = new Button { Content = content, ToolTip = title, Padding = new Thickness(2), Margin = new Thickness(0, 0, 2, 0), Focusable = false };
        tab.SetResourceReference(StyleProperty, "ToolButton");
        tab.Click += async (_, _) =>
        {
            _stickerTab?.ClearValue(Button.BackgroundProperty);
            _stickerTab = tab;
            tab.SetResourceReference(Button.BackgroundProperty, "Selected");
            StickerGrid.Children.Clear();
            StickerNote.Text = title;
            TdApi.Sticker[] list;
            try { list = await load(); }
            catch (TdException ex) { StickerNote.Text = "Не удалось открыть набор: " + (ex.Error?.Message ?? ex.Message); return; }
            if (_stickerTab != tab) return; // another tab was clicked meanwhile
            foreach (var s in list) StickerGrid.Children.Add(StickerCell(s));
        };
        StickerSets.Children.Add(tab);
    }

    Button StickerCell(TdApi.Sticker s)
    {
        var fallback = new TextBlock { FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Smileys.SetText(fallback, s.Emoji);
        var img = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var grid = new Grid { Width = 72, Height = 72 };
        grid.Children.Add(fallback);
        grid.Children.Add(img);
        var b = new Button { Content = grid, Padding = new Thickness(2), Margin = new Thickness(1), Focusable = false, ToolTip = s.Emoji };
        b.SetResourceReference(StyleProperty, "ToolButton");
        b.Click += (_, _) => SendSticker(s);
        _ = LoadStickerThumb(img, fallback, s);
        return b;
    }

    static async Task LoadStickerThumb(Image img, TextBlock fallback, TdApi.Sticker s)
    {
        if (await Media.Image(Media.StickerStill(s, true), 108) is not { } bmp) return;
        img.Source = bmp;
        fallback.Visibility = Visibility.Collapsed;
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        Tg.NewMessage -= OnNew;
        Tg.SendSucceeded -= OnSent;
        Tg.MessagesDeleted -= OnDeleted;
        Tg.ContentChanged -= OnEdited;
        Tg.ReadOutbox -= OnReadOutbox;
        Tg.Fire(new TdApi.CloseChat { ChatId = _chat.Id });
        base.OnClosed(e);
    }
}
