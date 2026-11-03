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
        Tg.InitDemo(new TdApi.User { Id = 1, FirstName = L("Вы"), LastName = "", PhoneNumber = "79990000000" });

        Contact(2, L("Алина"), Presence.Online, L("в сети"), now - 120,
            (L("Привет! Ты видел, у нас теперь ICQ для телеги? :D"), false),
            (L("Ага, ставлю прямо сейчас 👍"), true),
            (L("Звук помнишь? У меня ностальгия 😢 <3"), false));
        Contact(3, L("Дима Котов"), Presence.Away, L("был(а) 5 мин назад"), now - 900,
            (L("Скинь ссылку на репо"), false),
            (L("https://github.com/ — как выложу, скину"), true));
        Contact(4, L("Мама"), Presence.Online, L("в сети"), now - 3600,
            (L("Ты поел? :)"), false), (L("Да, мам 😊"), true), (L("Молодец 🌹"), false));
        Contact(5, L("Серёга"), Presence.Offline, L("был(а) вчера в 23:10"), now - 86400,
            (L("Го в субботу? 🍺"), true), (L("Посмотрим ;)"), false));
        Contact(6, L("Катя Белова"), Presence.Offline, L("был(а) на этой неделе"), now - 4 * 86400,
            (L("Спасибо за помощь! 😘"), false));
        Contact(7, L("Олег"), Presence.Away, L("был(а) недавно"), now - 7200, (L("ок"), false));

        Chat(10, L("Чат двора 🏠"), ChatKind.Group, now - 300,
            (8, L("Кто оставил самокат у подъезда?")), (9, L("Не я 😐")), (1, L("Тоже не я :)")), (8, L("Ну кто-то же оставил 😠")));
        Chat(11, L("Работа · фронт"), ChatKind.Group, now - 1800,
            (9, L("Деплой в 18:00 🔥")), (1, L("Принял 👍")));
        Chat(12, L("Новости технологий"), ChatKind.Channel, now - 600,
            (0, L("Вышел .NET 10, что нового 😎")));
        Chat(13, L("Погода бот"), ChatKind.Bot, now - 40000, (0, L("Сегодня +12, облачно 🙁")));
        Chat(14, L("Избранное"), ChatKind.Saved, now - 50000, (1, L("Идея: тема Luna Silver")));

        Tg.DemoUser(8, L("Петрович"));
        Tg.DemoUser(9, L("Аня"));

        // Address book: everyone above plus one person you have never written to.
        Tg.DemoUser(15, L("Бабушка"), contact: true);
        foreach (var id in new long[] { 2, 3, 4, 5, 6, 7, 15 }) Tg.DemoContact(id);

        Tg.Chats[10].Unread = 2;
        Tg.Chats[12].Unread = 5;
        Tg.Chats[12].Muted = true;

        // Alina writes, deletes it and edits an older message: shows the archive keeping both.
        After(1, () => { var m = Msg(2, 2, "", Now); m.Content = DemoRich(); Deliver(m); });
        After(1.5, () => { var m = Msg(2, 2, "", Now); m.Content = DemoFormatted(); Deliver(m); });
        After(2, () => { var m = Msg(2, 2, "", Now); m.Content = DemoPhoto(); Deliver(m); });
        After(3, () => { var m = Msg(2, 2, "", Now); m.Content = DemoStickerMessage(); Deliver(m); });
        After(4, () => Incoming(2, 2, L("Ой, это не тебе было 🙈")));
        After(8, () =>
        {
            var m = History[2].Last(x => !x.IsOutgoing);
            History[2].Remove(m);
            Tg.DemoDelete(2, m.Id);
        });
        After(10, () =>
        {
            var m = History[2].First(x => !x.IsOutgoing && Text.Describe(x.Content) == L("Звук помнишь? У меня ностальгия 😢 <3"));
            var c = new TdApi.MessageContent.MessageText { Text = new TdApi.FormattedText { Text = L("Звук помнишь? Прям ностальгия накрыла 😢 <3"), Entities = [] } };
            m.Content = c;
            m.EditDate = Now;
            Tg.DemoEdit(2, m.Id, c);
        });

        // A message from someone else a few seconds in, to show the blinking envelope, tray and sound.
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        t.Tick += (_, _) => { t.Stop(); Incoming(3, 3, L("Эй, ты тут? Кинь ссылку, плиз")); };
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
        c.StatusText = kind switch { ChatKind.Channel => L("1 204 подписчика"), ChatKind.Bot => L("бот"), ChatKind.Saved => L("заметки для себя"), _ => L("12 участников") };
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
            Caption = new TdApi.FormattedText { Text = L("Смотри, какой закат был :)"), Entities = [] },
            Photo = new TdApi.Photo { Sizes = [new TdApi.PhotoSize { Type = "x", Photo = file, Width = w, Height = h }] },
        };
    }

    // A bouncing, winking smiley drawn by hand as Lottie: shows animated stickers and custom emoji without Telegram.
    const string SmileyLottie = """
    {"v":"5.7.4","fr":60,"ip":0,"op":120,"w":512,"h":512,"layers":[{"ty":4,"ind":1,"ip":0,"op":120,"st":0,
     "ks":{"o":{"a":0,"k":100},"a":{"a":0,"k":[0,0,0]},"s":{"a":0,"k":[100,100,100]},
      "r":{"a":1,"k":[{"t":0,"s":[-12],"i":{"x":[0.5],"y":[1]},"o":{"x":[0.5],"y":[0]}},{"t":60,"s":[12],"i":{"x":[0.5],"y":[1]},"o":{"x":[0.5],"y":[0]}},{"t":120,"s":[-12]}]},
      "p":{"a":1,"k":[{"t":0,"s":[256,290,0],"i":{"x":0.5,"y":1},"o":{"x":0.5,"y":0}},{"t":30,"s":[256,222,0],"i":{"x":0.5,"y":1},"o":{"x":0.5,"y":0}},{"t":60,"s":[256,290,0],"i":{"x":0.5,"y":1},"o":{"x":0.5,"y":0}},{"t":90,"s":[256,222,0],"i":{"x":0.5,"y":1},"o":{"x":0.5,"y":0}},{"t":120,"s":[256,290,0]}]}},
     "shapes":[
      {"ty":"gr","it":[
        {"ty":"sh","ks":{"a":0,"k":{"c":false,"v":[[-90,40],[90,40]],"i":[[0,0],[-50,70]],"o":[[50,70],[0,0]]}}},
        {"ty":"st","c":{"a":0,"k":[0.45,0.25,0.05,1]},"o":{"a":0,"k":100},"w":{"a":0,"k":20},"lc":2,"lj":2},
        {"ty":"tr","p":{"a":0,"k":[0,0]},"a":{"a":0,"k":[0,0]},"s":{"a":0,"k":[100,100]},"r":{"a":0,"k":0},"o":{"a":0,"k":100}}]},
      {"ty":"gr","it":[
        {"ty":"el","p":{"a":0,"k":[-60,-50]},"s":{"a":0,"k":[44,60]}},
        {"ty":"el","p":{"a":0,"k":[60,-50]},"s":{"a":1,"k":[{"t":0,"s":[44,60],"i":{"x":[0.5,0.5],"y":[1,1]},"o":{"x":[0.5,0.5],"y":[0,0]}},{"t":70,"s":[44,60],"i":{"x":[0.5,0.5],"y":[1,1]},"o":{"x":[0.5,0.5],"y":[0,0]}},{"t":80,"s":[50,6],"i":{"x":[0.5,0.5],"y":[1,1]},"o":{"x":[0.5,0.5],"y":[0,0]}},{"t":90,"s":[44,60]}]}},
        {"ty":"fl","c":{"a":0,"k":[0.35,0.2,0.05,1]},"o":{"a":0,"k":100}},
        {"ty":"tr","p":{"a":0,"k":[0,0]},"a":{"a":0,"k":[0,0]},"s":{"a":0,"k":[100,100]},"r":{"a":0,"k":0},"o":{"a":0,"k":100}}]},
      {"ty":"gr","it":[
        {"ty":"el","p":{"a":0,"k":[-55,-85]},"s":{"a":0,"k":[110,60]}},
        {"ty":"fl","c":{"a":0,"k":[1,1,0.85,1]},"o":{"a":0,"k":55}},
        {"ty":"tr","p":{"a":0,"k":[0,0]},"a":{"a":0,"k":[0,0]},"s":{"a":0,"k":[100,100]},"r":{"a":0,"k":-25},"o":{"a":0,"k":100}}]},
      {"ty":"gr","it":[
        {"ty":"el","p":{"a":0,"k":[0,0]},"s":{"a":0,"k":[330,330]}},
        {"ty":"gf","o":{"a":0,"k":100},"r":1,"t":2,"s":{"a":0,"k":[-60,-90]},"e":{"a":0,"k":[120,170]},"h":{"a":0,"k":0},"a":{"a":0,"k":0},
         "g":{"p":2,"k":{"a":0,"k":[0,1,0.93,0.45,1,0.98,0.62,0.1]}}},
        {"ty":"st","c":{"a":0,"k":[0.8,0.45,0.05,1]},"o":{"a":0,"k":100},"w":{"a":0,"k":8},"lc":2,"lj":2},
        {"ty":"tr","p":{"a":0,"k":[0,0]},"a":{"a":0,"k":[0,0]},"s":{"a":0,"k":[100,100]},"r":{"a":0,"k":0},"o":{"a":0,"k":100}}]}
     ]}]}
    """;

    static TdApi.File DemoTgs()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aurora-demo-sticker.tgs");
        using (var fs = System.IO.File.Create(path))
        using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal))
        using (var w = new System.IO.StreamWriter(gz))
            w.Write(SmileyLottie);
        return new TdApi.File { Id = 78, Local = new TdApi.LocalFile { Path = path, IsDownloadingCompleted = true } };
    }

    static TdApi.Sticker DemoSticker(long customEmojiId = 0) => new()
    {
        Id = 5000 + customEmojiId,
        Width = 512,
        Height = 512,
        Emoji = "😊",
        Format = new TdApi.StickerFormat.StickerFormatTgs(),
        FullType = customEmojiId != 0
            ? new TdApi.StickerFullType.StickerFullTypeCustomEmoji { CustomEmojiId = customEmojiId }
            : new TdApi.StickerFullType.StickerFullTypeRegular(),
        Sticker_ = DemoTgs(),
    };

    public static TdApi.Sticker? CustomEmoji(long id) => DemoSticker(id);

    static TdApi.MessageContent DemoStickerMessage() => new TdApi.MessageContent.MessageSticker { Sticker = DemoSticker() };

    static TdApi.MessageContent DemoFormatted()
    {
        var text = L("Смотри, кастомные эмодзи 😊 теперь живые, а тут спойлер: завтра тусим");
        var emoji = text.IndexOf("😊", StringComparison.Ordinal);
        var bold = text.IndexOf(L("кастомные эмодзи"), StringComparison.Ordinal);
        var spoiler = text.IndexOf(L("завтра"), StringComparison.Ordinal);
        return new TdApi.MessageContent.MessageText
        {
            Text = new TdApi.FormattedText
            {
                Text = text,
                Entities =
                [
                    new TdApi.TextEntity { Offset = bold, Length = L("кастомные эмодзи").Length, Type = new TdApi.TextEntityType.TextEntityTypeBold() },
                    new TdApi.TextEntity { Offset = emoji, Length = 2, Type = new TdApi.TextEntityType.TextEntityTypeCustomEmoji { CustomEmojiId = 1 } },
                    new TdApi.TextEntity { Offset = spoiler, Length = text.Length - spoiler, Type = new TdApi.TextEntityType.TextEntityTypeSpoiler() },
                ],
            },
        };
    }

    static TdApi.RichText P(string s) => new TdApi.RichText.RichTextPlain { Text = s };

    static TdApi.MessageContent DemoRich() => new TdApi.MessageContent.MessageRichMessage
    {
        Message = new TdApi.RichMessage
        {
            IsFull = true,
            Blocks =
            [
                new TdApi.PageBlock.PageBlockHeader { Header = P(L("Планы на субботу")) },
                new TdApi.PageBlock.PageBlockParagraph
                {
                    Text = new TdApi.RichText.RichTexts
                    {
                        Texts = [P(L("Собираемся у Димы в ")), new TdApi.RichText.RichTextBold { Text = P("16:00") }, P(L(", берём ")),
                                 new TdApi.RichText.RichTextItalic { Text = P(L("старые диски")) }, P(".")],
                    },
                },
                new TdApi.PageBlock.PageBlockList
                {
                    Items =
                    [
                        new TdApi.PageBlockListItem { HasCheckbox = true, IsChecked = true, Blocks = [new TdApi.PageBlock.PageBlockParagraph { Text = P(L("Пицца")) }] },
                        new TdApi.PageBlockListItem { HasCheckbox = true, Blocks = [new TdApi.PageBlock.PageBlockParagraph { Text = P(L("Колонки")) }] },
                    ],
                },
                new TdApi.PageBlock.PageBlockPullQuote { Text = P(L("Кто опоздает, тот ставит музыку")), Credit = P(L("Дима")) },
            ],
        },
    };

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
        After(1.2, () => { c.Typing = L("печатает…"); });
        After(3.0, () =>
        {
            c.Typing = "";
            var replies = new[] { L("Ахах, точно 😂"), L("Ну да 👍"), L("Сейчас гляну :)"), L("Ого, звучит как раньше! :D"), L("Ок, договорились ;)"), "😎", L("Люблю это <3") };
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
            c.LastText = (m.IsOutgoing ? L("Вы: ") : "") + Text.Describe(m.Content);
            c.LastDate = m.Date;
            c.Order = (long)m.Date << 8 | c.Id;
            if (!m.IsOutgoing && !App.IsChatActive(m.ChatId)) c.Unread++;
        }
        Tg.DemoNewMessage(m);
    }

    static string _myBio = L("Пишу Aurora 🌼 :)");

    public static void SetMyBio(string bio) => _myBio = bio;

    public static string About(long id) => id switch
    {
        1 => _myBio,
        2 => L("Дизайнер. Люблю старые интерфейсы ;)"),
        3 => L("frontend · кофе · велик"),
        10 => L("Чат жильцов дома 12. Без рекламы, пожалуйста 🙏"),
        12 => L("Главное из мира технологий, коротко 🔥"),
        13 => L("Пришлю погоду по запросу. Команды: /now, /week"),
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
