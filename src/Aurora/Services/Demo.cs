using System.Windows.Threading;
using TdLib;
using Aurora.Models;

namespace Aurora.Services;

/// <summary>
/// Aurora.exe --demo: a fake account with made-up contacts, no network, no TDLib.
/// Lets anyone look at the UI without Telegram API keys or a login.
/// </summary>
public static class Demo
{
    public static bool On { get; private set; }

    static readonly Dictionary<long, List<TdApi.Message>> History = new();
    static long _nextId = 1000;
    static readonly Random Rnd = new(7);

    public static void Start()
    {
        On = true;
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Tg.InitDemo(new TdApi.User { Id = 1, FirstName = "Вы", LastName = "", PhoneNumber = "79990000000" });

        Contact(2, "Алина", Presence.Online, "в сети", now - 120,
            ("Привет! Ты видел, у нас теперь ICQ для телеги? :D", false),
            ("Ага, ставлю прямо сейчас 👍", true),
            ("Звук помнишь? У меня ностальгия 😢 <3", false));
        Contact(3, "Дима Котов", Presence.Away, "был(а) 5 мин назад", now - 900,
            ("Скинь ссылку на репо", false),
            ("https://github.com/ — как выложу, скину", true));
        Contact(4, "Мама", Presence.Online, "в сети", now - 3600,
            ("Ты поел? :)", false), ("Да, мам 😊", true), ("Молодец 🌹", false));
        Contact(5, "Серёга", Presence.Offline, "был(а) вчера в 23:10", now - 86400,
            ("Го в субботу? 🍺", true), ("Посмотрим ;)", false));
        Contact(6, "Катя Белова", Presence.Offline, "был(а) на этой неделе", now - 4 * 86400,
            ("Спасибо за помощь! 😘", false));
        Contact(7, "Олег", Presence.Away, "был(а) недавно", now - 7200, ("ок", false));

        Chat(10, "Чат двора 🏠", ChatKind.Group, now - 300,
            (8, "Кто оставил самокат у подъезда?"), (9, "Не я 😐"), (1, "Тоже не я :)"), (8, "Ну кто-то же оставил 😠"));
        Chat(11, "Работа · фронт", ChatKind.Group, now - 1800,
            (9, "Деплой в 18:00 🔥"), (1, "Принял 👍"));
        Chat(12, "Новости технологий", ChatKind.Channel, now - 600,
            (0, "Вышел .NET 10, что нового 😎"));
        Chat(13, "Погода бот", ChatKind.Bot, now - 40000, (0, "Сегодня +12, облачно 🙁"));
        Chat(14, "Избранное", ChatKind.Saved, now - 50000, (1, "Идея: тема Luna Silver"));

        Tg.DemoUser(8, "Петрович");
        Tg.DemoUser(9, "Аня");

        // Address book: everyone above plus one person you have never written to.
        Tg.DemoUser(15, "Бабушка", contact: true);
        foreach (var id in new long[] { 2, 3, 4, 5, 6, 7, 15 }) Tg.DemoContact(id);

        Tg.Chats[10].Unread = 2;
        Tg.Chats[12].Unread = 5;
        Tg.Chats[12].Muted = true;

        // Alina writes, deletes it and edits an older message: shows the archive keeping both.
        After(2, () => { var m = Msg(2, 2, "", Now); m.Content = DemoPhoto(); Deliver(m); });
        After(4, () => Incoming(2, 2, "Ой, это не тебе было 🙈"));
        After(8, () =>
        {
            var m = History[2].Last(x => !x.IsOutgoing);
            History[2].Remove(m);
            Tg.DemoDelete(2, m.Id);
        });
        After(10, () =>
        {
            var m = History[2].First(x => !x.IsOutgoing && Text.Describe(x.Content).StartsWith("Звук"));
            var c = new TdApi.MessageContent.MessageText { Text = new TdApi.FormattedText { Text = "Звук помнишь? Прям ностальгия накрыла 😢 <3", Entities = [] } };
            m.Content = c;
            m.EditDate = Now;
            Tg.DemoEdit(2, m.Id, c);
        });

        // A message from someone else a few seconds in, to show the blinking envelope, tray and sound.
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        t.Tick += (_, _) => { t.Stop(); Incoming(3, 3, "Эй, ты тут? Кинь ссылку, плиз"); };
        t.Start();
    }

    static void Contact(long id, string name, Presence p, string status, int lastDate, params (string text, bool mine)[] msgs)
    {
        Tg.DemoUser(id, name, contact: true);
        var c = new ChatItem { Id = id, UserId = id, Kind = ChatKind.User, Title = name, Presence = p, StatusText = status };
        var list = new List<TdApi.Message>();
        var d = lastDate - msgs.Length * 90;
        foreach (var (text, mine) in msgs) list.Add(Msg(id, mine ? 1 : id, text, d += 90));
        Add(c, list);
    }

    static void Chat(long id, string title, ChatKind kind, int lastDate, params (long from, string text)[] msgs)
    {
        var c = new ChatItem { Id = id, Kind = kind, Title = title };
        c.StatusText = kind switch { ChatKind.Channel => "1 204 подписчика", ChatKind.Bot => "бот", ChatKind.Saved => "заметки для себя", _ => "12 участников" };
        var list = new List<TdApi.Message>();
        var d = lastDate - msgs.Length * 60;
        foreach (var (from, text) in msgs) list.Add(Msg(id, from == 0 ? -id : from, text, d += 60));
        Add(c, list);
    }

    static void Add(ChatItem c, List<TdApi.Message> list)
    {
        History[c.Id] = list;
        var last = list[^1];
        c.LastText = Text.Describe(last.Content);
        c.LastDate = last.Date;
        c.Order = (long)last.Date << 8 | c.Id;
        Tg.DemoChat(c);
    }

    static TdApi.Message Msg(long chatId, long from, string text, int date) => new()
    {
        Id = ++_nextId,
        ChatId = chatId,
        Date = date,
        IsOutgoing = from == 1,
        SenderId = from < 0
            ? new TdApi.MessageSender.MessageSenderChat { ChatId = -from }
            : new TdApi.MessageSender.MessageSenderUser { UserId = from },
        Content = new TdApi.MessageContent.MessageText { Text = new TdApi.FormattedText { Text = text, Entities = [] } },
    };

    static int Now => (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Newest first, like getChatHistory.</summary>
    public static List<TdApi.Message> GetHistory(long chatId, long fromId, int limit) =>
        History.TryGetValue(chatId, out var l)
            ? l.Where(m => fromId == 0 || m.Id < fromId).OrderByDescending(m => m.Id).Take(limit).ToList()
            : new();

    /// <summary>A made-up holiday snapshot for the demo: drawn once into %TEMP%, so photos can be shown without Telegram.</summary>
    static TdApi.MessageContent DemoPhoto()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aurora-demo-photo.png");
        const int w = 640, h = 420;
        if (!System.IO.File.Exists(path))
        {
            var v = new System.Windows.Media.DrawingVisual();
            using (var dc = v.RenderOpen())
            {
                var sky = new System.Windows.Media.LinearGradientBrush(
                    System.Windows.Media.Color.FromRgb(0x2B, 0x6C, 0xD8), System.Windows.Media.Color.FromRgb(0xFF, 0xC0, 0x7A), 90);
                dc.DrawRectangle(sky, null, new System.Windows.Rect(0, 0, w, h));
                dc.DrawEllipse(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xF1, 0xB8)), null,
                    new System.Windows.Point(450, 250), 60, 60);
                var sea = new System.Windows.Media.LinearGradientBrush(
                    System.Windows.Media.Color.FromRgb(0x1E, 0x8C, 0xC8), System.Windows.Media.Color.FromRgb(0x0B, 0x3D, 0x6B), 90);
                dc.DrawRectangle(sea, null, new System.Windows.Rect(0, 280, w, h - 280));
                var hill = System.Windows.Media.Geometry.Parse("M0,300 C120,200 260,230 360,300 Z");
                dc.DrawGeometry(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x8E, 0x3A)), null, hill);
            }
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(v);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = System.IO.File.Create(path);
            enc.Save(fs);
        }
        var file = new TdApi.File { Id = 77, Local = new TdApi.LocalFile { Path = path, IsDownloadingCompleted = true } };
        return new TdApi.MessageContent.MessagePhoto
        {
            Caption = new TdApi.FormattedText { Text = "Смотри, какой закат был :)", Entities = [] },
            Photo = new TdApi.Photo { Sizes = [new TdApi.PhotoSize { Type = "x", Photo = file, Width = w, Height = h }] },
        };
    }

    public static void SendContent(long chatId, TdApi.MessageContent content)
    {
        var m = Msg(chatId, 1, "", Now);
        m.Content = content;
        Deliver(m);
        After(0.8, () => Tg.DemoReadOutbox(chatId, m.Id));
    }

    public static void Send(long chatId, string text)
    {
        var m = Msg(chatId, 1, text, Now);
        Deliver(m);
        if (!Tg.Chats.TryGetValue(chatId, out var c) || c.Kind is ChatKind.Channel or ChatKind.Saved) return;

        // The other side "reads" it, types for a bit and answers.
        After(0.8, () => Tg.DemoReadOutbox(chatId, m.Id));
        After(1.2, () => { c.Typing = "печатает…"; });
        After(3.0, () =>
        {
            c.Typing = "";
            var replies = new[] { "Ахах, точно 😂", "Ну да 👍", "Сейчас гляну :)", "Ого, звучит как раньше! :D", "Ок, договорились ;)", "😎", "Люблю это <3" };
            var from = c.Kind == ChatKind.Group ? 9 : chatId;
            Incoming(chatId, from, replies[Rnd.Next(replies.Length)]);
        });
    }

    static void Incoming(long chatId, long from, string text) => Deliver(Msg(chatId, from, text, Now));

    static void Deliver(TdApi.Message m)
    {
        if (!History.TryGetValue(m.ChatId, out var list)) History[m.ChatId] = list = new();
        list.Add(m);
        if (Tg.Chats.TryGetValue(m.ChatId, out var c))
        {
            Tg.DemoChat(c); // first message to a contact puts the chat into the Chats tab
            c.LastText = (m.IsOutgoing ? "Вы: " : "") + Text.Describe(m.Content);
            c.LastDate = m.Date;
            c.Order = (long)m.Date << 8 | c.Id;
            if (!m.IsOutgoing && !App.IsChatActive(m.ChatId)) c.Unread++;
        }
        Tg.DemoNewMessage(m);
    }

    static string _myBio = "Пишу Aurora 🌼 :)";

    public static void SetMyBio(string bio) => _myBio = bio;

    public static string About(long id) => id switch
    {
        1 => _myBio,
        2 => "Дизайнер. Люблю старые интерфейсы ;)",
        3 => "frontend · кофе · велик",
        10 => "Чат жильцов дома 12. Без рекламы, пожалуйста 🙏",
        12 => "Главное из мира технологий, коротко 🔥",
        13 => "Пришлю погоду по запросу. Команды: /now, /week",
        _ => "",
    };

    public static void Read(long chatId)
    {
        if (Tg.Chats.TryGetValue(chatId, out var c)) c.Unread = 0;
    }

    static void After(double sec, Action a)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(sec) };
        t.Tick += (_, _) => { t.Stop(); a(); };
        t.Start();
    }
}
