using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using TdLib;
using Aurora.Models;

namespace Aurora.Services;

/// <summary>
/// Single TDLib client for the whole app. Updates come in on a TDLib thread and are
/// replayed on the UI thread, so everything below the dispatcher hop is single-threaded.
/// </summary>
public static class Tg
{
    public static TdClient Client { get; private set; } = null!;
    public static long MyId { get; private set; }
    public static TdApi.AuthorizationState? Auth { get; private set; }

    public static readonly Dictionary<long, TdApi.User> Users = new();
    public static readonly Dictionary<long, ChatItem> Chats = new();
    /// <summary>Chats that are in the main chat list (order != 0). Bound to the Chats tab.</summary>
    public static readonly ObservableCollection<ChatItem> ChatList = new();
    /// <summary>People from the Telegram address book. Bound to the Contacts tab.</summary>
    public static readonly ObservableCollection<ChatItem> Contacts = new();
    static readonly HashSet<long> _contactIds = new();
    static bool _ready;

    public static event Action<TdApi.AuthorizationState>? AuthChanged;
    public static event Action<TdApi.Message>? NewMessage;
    public static event Action<TdApi.Message, long>? SendSucceeded;
    public static event Action<long, long[]>? MessagesDeleted;
    public static event Action<long, long, TdApi.MessageContent>? ContentChanged;
    public static event Action<long, long>? ReadOutbox;
    public static event Action? MeChanged;
    /// <summary>Human text for the connection state, empty when connected.</summary>
    public static event Action<string>? ConnectionChanged;
    public static string Connection { get; private set; } = "";
    /// <summary>TDLib rejected the setup (bad api_id/api_hash and similar).</summary>
    public static event Action<string>? Fatal;

    static Dispatcher _ui = null!;
    static Config _cfg = null!;
    static readonly Dictionary<long, DateTime> _typingSince = new();
    static DispatcherTimer? _tick;

    public static void Start(Config cfg)
    {
        _cfg = cfg;
        _ui = Dispatcher.CurrentDispatcher;
        // Restart after logout: drop the old instance and everything it knew.
        _tick?.Stop();
        Client?.Dispose();
        Users.Clear(); Chats.Clear(); ChatList.Clear(); _inList.Clear(); _typingSince.Clear();
        Contacts.Clear(); _contactIds.Clear();
        MyId = 0;
        _ready = false;
        Client = new TdClient();
        Client.Execute(new TdApi.SetLogVerbosityLevel { NewVerbosityLevel = 1 });
        Client.UpdateReceived += (_, u) => _ui.BeginInvoke(DispatcherPriority.Normal, () => OnUpdate(u));

        // Clears "typing…" labels that TDLib never cancels explicitly, and refreshes "was online" texts.
        _tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), _ui);
        _tick.Start();
    }

    public static async Task Shutdown()
    {
        if (Demo.On) return;
        try { await Client.ExecuteAsync(new TdApi.Close()).WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
    }

    static void OnUpdate(TdApi.Update u)
    {
        switch (u)
        {
            case TdApi.Update.UpdateAuthorizationState s:
                Auth = s.AuthorizationState;
                if (s.AuthorizationState is TdApi.AuthorizationState.AuthorizationStateWaitTdlibParameters)
                    _ = SendParameters();
                else if (s.AuthorizationState is TdApi.AuthorizationState.AuthorizationStateReady)
                    _ = OnReady();
                AuthChanged?.Invoke(s.AuthorizationState);
                break;

            case TdApi.Update.UpdateUser uu:
                Users[uu.User.Id] = uu.User;
                if (Chats.TryGetValue(uu.User.Id, out var pc)) ApplyUser(pc, uu.User);
                if (_ready) SyncContact(uu.User);
                break;

            case TdApi.Update.UpdateUserStatus us:
                if (Users.TryGetValue(us.UserId, out var user)) user.Status = us.Status;
                if (Chats.TryGetValue(us.UserId, out var sc)) ApplyStatus(sc, us.Status);
                break;

            case TdApi.Update.UpdateNewChat nc:
                AddChat(nc.Chat);
                break;

            case TdApi.Update.UpdateChatTitle t:
                if (Chats.TryGetValue(t.ChatId, out var tc) && tc.Kind != ChatKind.Saved) tc.Title = t.Title;
                break;

            case TdApi.Update.UpdateChatPosition p:
                if (Chats.TryGetValue(p.ChatId, out var poc)) ApplyPosition(poc, p.Position);
                break;

            case TdApi.Update.UpdateChatLastMessage lm:
                if (Chats.TryGetValue(lm.ChatId, out var lc))
                {
                    ApplyLast(lc, lm.LastMessage);
                    if (lm.LastMessage != null) Archive.Record(lm.LastMessage);
                    foreach (var pos in lm.Positions) ApplyPosition(lc, pos);
                }
                break;

            case TdApi.Update.UpdateChatReadInbox ri:
                if (Chats.TryGetValue(ri.ChatId, out var rc)) rc.Unread = ri.UnreadCount;
                break;

            case TdApi.Update.UpdateChatReadOutbox ro:
                ReadOutbox?.Invoke(ro.ChatId, ro.LastReadOutboxMessageId);
                break;

            case TdApi.Update.UpdateChatNotificationSettings ns:
                if (Chats.TryGetValue(ns.ChatId, out var nsc)) nsc.Muted = ns.NotificationSettings.MuteFor > 0;
                break;

            case TdApi.Update.UpdateChatAction ca:
                if (Chats.TryGetValue(ca.ChatId, out var ac)) ApplyAction(ac, ca);
                break;

            case TdApi.Update.UpdateNewMessage nm:
                Archive.Record(nm.Message);
                if (Chats.TryGetValue(nm.Message.ChatId, out var mc)) { mc.Typing = ""; _typingSince.Remove(mc.Id); }
                NewMessage?.Invoke(nm.Message);
                break;

            case TdApi.Update.UpdateMessageSendSucceeded ok:
                Archive.Record(ok.Message);
                SendSucceeded?.Invoke(ok.Message, ok.OldMessageId);
                break;

            case TdApi.Update.UpdateDeleteMessages dm when dm.IsPermanent && !dm.FromCache:
                Archive.Deleted(dm.ChatId, dm.MessageIds);
                MessagesDeleted?.Invoke(dm.ChatId, dm.MessageIds);
                break;

            case TdApi.Update.UpdateConnectionState cs:
                Connection = cs.State switch
                {
                    TdApi.ConnectionState.ConnectionStateWaitingForNetwork => "Нет сети…",
                    TdApi.ConnectionState.ConnectionStateConnectingToProxy => "Подключение к прокси…",
                    TdApi.ConnectionState.ConnectionStateConnecting => "Подключение…",
                    TdApi.ConnectionState.ConnectionStateUpdating => "Обновление…",
                    _ => "",
                };
                ConnectionChanged?.Invoke(Connection);
                break;

            case TdApi.Update.UpdateMessageContent mcu:
                Archive.Edited(mcu.ChatId, mcu.MessageId, mcu.NewContent);
                ContentChanged?.Invoke(mcu.ChatId, mcu.MessageId, mcu.NewContent);
                break;
        }
    }

    static async Task SendParameters()
    {
        var root = Path.Combine(Config.Dir, "td");
        try
        {
            await Client.ExecuteAsync(new TdApi.SetTdlibParameters
            {
            DatabaseDirectory = root,
            FilesDirectory = Path.Combine(root, "files"),
            DatabaseEncryptionKey = Array.Empty<byte>(),
            UseFileDatabase = true,
            UseChatInfoDatabase = true,
            UseMessageDatabase = true, // instant start: chats and history come from the local DB first
            UseSecretChats = false,
            ApiId = _cfg.EffectiveApiId,
            ApiHash = _cfg.EffectiveApiHash,
            SystemLanguageCode = "ru",
            DeviceModel = "Aurora Desktop",
            SystemVersion = Environment.OSVersion.VersionString,
            ApplicationVersion = "0.1.0",
            });
            // before the first connection attempt, so login goes through the VPN right away
            try { await Proxies.ApplySystem(_cfg.UseSystemProxy); } catch { }
        }
        catch (TdException ex) { Fatal?.Invoke(ex.Error?.Message ?? ex.Message); }
    }

    static async Task OnReady()
    {
        try
        {
            var me = await Client.ExecuteAsync(new TdApi.GetMe());
            MyId = me.Id;
            Users[me.Id] = me;
            if (Chats.TryGetValue(me.Id, out var saved)) ApplyUser(saved, me);
            MeChanged?.Invoke();
        }
        catch { }

        _ready = true;
        _ = LoadContacts();

        // Pull the whole main list; TDLib answers 404 when there is nothing left.
        for (var i = 0; i < 50; i++)
        {
            try { await Client.ExecuteAsync(new TdApi.LoadChats { ChatList = new TdApi.ChatList.ChatListMain(), Limit = 200 }); }
            catch { break; }
        }
    }

    // ---------- chat bookkeeping ----------

    static async Task LoadContacts()
    {
        try
        {
            var ids = await Client.ExecuteAsync(new TdApi.GetContacts());
            foreach (var id in ids.UserIds)
            {
                if (!Users.ContainsKey(id))
                    try { Users[id] = await Client.ExecuteAsync(new TdApi.GetUser { UserId = id }); } catch { continue; }
                SyncContact(Users[id]);
            }
        }
        catch { }
    }

    /// <summary>Adds or removes a user from the Contacts tab when they enter or leave the address book.</summary>
    static void SyncContact(TdApi.User u)
    {
        var isContact = u.IsContact && u.Id != MyId && u.Type is not TdApi.UserType.UserTypeDeleted;
        if (isContact && _contactIds.Add(u.Id)) Contacts.Add(ItemForUser(u.Id));
        else if (!isContact && _contactIds.Remove(u.Id) && Chats.TryGetValue(u.Id, out var c)) Contacts.Remove(c);
    }

    /// <summary>The list item for a person; private chat id equals user id in TDLib, so one object serves both tabs.</summary>
    public static ChatItem ItemForUser(long userId)
    {
        if (Chats.TryGetValue(userId, out var c)) return c;
        c = new ChatItem { Id = userId, UserId = userId, Kind = ChatKind.User, Title = UserName(userId) };
        if (Users.TryGetValue(userId, out var u)) ApplyUser(c, u);
        Chats[userId] = c;
        return c;
    }

    static void AddChat(TdApi.Chat chat)
    {
        // A contact may already have an item from the address book; update it in place.
        var item = Chats.TryGetValue(chat.Id, out var existing) ? existing : new ChatItem { Id = chat.Id };
        item.Title = chat.Title;
        switch (chat.Type)
        {
            case TdApi.ChatType.ChatTypePrivate p:
                item.UserId = p.UserId;
                item.Kind = ChatKind.User;
                if (Users.TryGetValue(p.UserId, out var u)) ApplyUser(item, u);
                break;
            case TdApi.ChatType.ChatTypeSecret s:
                item.UserId = s.UserId;
                item.Kind = ChatKind.User;
                if (Users.TryGetValue(s.UserId, out var su)) ApplyUser(item, su);
                break;
            case TdApi.ChatType.ChatTypeSupergroup sg:
                item.Kind = sg.IsChannel ? ChatKind.Channel : ChatKind.Group;
                break;
            default:
                item.Kind = ChatKind.Group;
                break;
        }
        item.Unread = chat.UnreadCount;
        item.Muted = chat.NotificationSettings?.MuteFor > 0;
        ApplyLast(item, chat.LastMessage);
        Chats[chat.Id] = item;
        foreach (var pos in chat.Positions) ApplyPosition(item, pos);
    }

    static void ApplyUser(ChatItem c, TdApi.User u)
    {
        if (u.Id == MyId && MyId != 0)
        {
            c.Kind = ChatKind.Saved;
            c.Title = "Избранное";
            c.StatusText = "заметки для себя";
            return;
        }
        if (u.Type is TdApi.UserType.UserTypeBot)
        {
            c.Kind = ChatKind.Bot;
            c.StatusText = "бот";
            return;
        }
        if (u.Type is TdApi.UserType.UserTypeDeleted) c.Title = "Удалённый аккаунт";
        ApplyStatus(c, u.Status);
    }

    static void ApplyStatus(ChatItem c, TdApi.UserStatus? s)
    {
        if (c.Kind != ChatKind.User) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        switch (s)
        {
            case TdApi.UserStatus.UserStatusOnline:
                c.Presence = Presence.Online;
                c.StatusText = "в сети";
                break;
            case TdApi.UserStatus.UserStatusOffline off:
                c.Presence = now - off.WasOnline < 15 * 60 ? Presence.Away : Presence.Offline;
                c.StatusText = "был(а) " + Text.WasOnline(off.WasOnline);
                break;
            case TdApi.UserStatus.UserStatusRecently:
                c.Presence = Presence.Away;
                c.StatusText = "был(а) недавно";
                break;
            case TdApi.UserStatus.UserStatusLastWeek:
                c.Presence = Presence.Offline;
                c.StatusText = "был(а) на этой неделе";
                break;
            case TdApi.UserStatus.UserStatusLastMonth:
                c.Presence = Presence.Offline;
                c.StatusText = "был(а) в этом месяце";
                break;
            default:
                c.Presence = Presence.Offline;
                c.StatusText = "был(а) давно";
                break;
        }
    }

    static void ApplyPosition(ChatItem c, TdApi.ChatPosition pos)
    {
        if (pos.List is not TdApi.ChatList.ChatListMain) return;
        c.Order = pos.Order;
        c.Pinned = pos.IsPinned;
        var listed = c.Order != 0;
        var inList = _inList.Contains(c.Id);
        if (listed && !inList) { _inList.Add(c.Id); ChatList.Add(c); }
        else if (!listed && inList) { _inList.Remove(c.Id); ChatList.Remove(c); }
    }

    static readonly HashSet<long> _inList = new();

    static void ApplyLast(ChatItem c, TdApi.Message? m)
    {
        if (m == null) return;
        var who = m.IsOutgoing ? "Вы: " : c.Kind == ChatKind.Group ? SenderShort(m.SenderId) + ": " : "";
        c.LastText = who + Text.Describe(m.Content);
        c.LastDate = m.Date;
        c.LastMessageId = m.Id;
    }

    static void ApplyAction(ChatItem c, TdApi.Update.UpdateChatAction a)
    {
        if (a.Action is TdApi.ChatAction.ChatActionCancel) { c.Typing = ""; _typingSince.Remove(c.Id); return; }
        var verb = a.Action switch
        {
            TdApi.ChatAction.ChatActionTyping => "печатает…",
            TdApi.ChatAction.ChatActionRecordingVoiceNote => "записывает голосовое…",
            TdApi.ChatAction.ChatActionRecordingVideoNote => "записывает кружок…",
            TdApi.ChatAction.ChatActionUploadingPhoto => "отправляет фото…",
            TdApi.ChatAction.ChatActionUploadingDocument => "отправляет файл…",
            TdApi.ChatAction.ChatActionChoosingSticker => "выбирает стикер…",
            _ => "что-то делает…",
        };
        c.Typing = c.Kind == ChatKind.Group ? SenderShort(a.SenderId) + " " + verb : verb;
        _typingSince[c.Id] = DateTime.UtcNow;
    }

    static int _ticks;
    static void Tick()
    {
        if (_typingSince.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var id in _typingSince.Where(kv => (now - kv.Value).TotalSeconds > 6).Select(kv => kv.Key).ToList())
            {
                _typingSince.Remove(id);
                if (Chats.TryGetValue(id, out var c)) c.Typing = "";
            }
        }
        // Once a minute: re-evaluate "was online N min ago" and the away/offline threshold.
        if (++_ticks % 60 == 0 && !Demo.On)
            foreach (var c in Chats.Values)
            {
                if (c.Kind == ChatKind.User && Users.TryGetValue(c.UserId, out var u)) ApplyStatus(c, u.Status);
                c.RefreshTime();
            }
    }

    // ---------- helpers used by windows ----------

    public static string UserName(long userId) =>
        Users.TryGetValue(userId, out var u) ? Text.NoEmoji((u.FirstName + " " + u.LastName).Trim()) : "…";

    public static string SenderName(TdApi.MessageSender? s) => s switch
    {
        TdApi.MessageSender.MessageSenderUser u => UserName(u.UserId),
        TdApi.MessageSender.MessageSenderChat c => Chats.TryGetValue(c.ChatId, out var ch) ? ch.Title : "Канал",
        _ => "",
    };

    static string SenderShort(TdApi.MessageSender? s) => s switch
    {
        TdApi.MessageSender.MessageSenderUser u when Users.TryGetValue(u.UserId, out var usr) => usr.FirstName,
        _ => SenderName(s),
    };

    // ---------- demo mode hooks (no TDLib behind them) ----------

    public static void InitDemo(TdApi.User me)
    {
        _ui = Dispatcher.CurrentDispatcher;
        MyId = me.Id;
        Users[me.Id] = me;
    }

    public static void DemoUser(long id, string name, bool contact = false) =>
        Users[id] = new TdApi.User
        {
            Id = id, FirstName = name, LastName = "", IsContact = contact, IsMutualContact = contact,
            PhoneNumber = contact ? "7999" + id.ToString("0000000") : "",
            Type = new TdApi.UserType.UserTypeRegular(),
        };

    public static void DemoChat(ChatItem c)
    {
        Chats[c.Id] = c;
        if (_inList.Add(c.Id)) ChatList.Add(c);
    }

    public static void DemoNewMessage(TdApi.Message m)
    {
        Archive.Record(m);
        NewMessage?.Invoke(m);
    }

    public static void DemoDelete(long chatId, long id)
    {
        Archive.Deleted(chatId, [id]);
        MessagesDeleted?.Invoke(chatId, [id]);
    }

    public static void DemoEdit(long chatId, long id, TdApi.MessageContent content)
    {
        Archive.Edited(chatId, id, content);
        ContentChanged?.Invoke(chatId, id, content);
    }
    public static void DemoReadOutbox(long chatId, long id) => ReadOutbox?.Invoke(chatId, id);

    public static void DemoContact(long userId)
    {
        if (_contactIds.Add(userId)) Contacts.Add(ItemForUser(userId));
    }

    // ---------- actions used by menus and windows ----------

    public static void SetMuted(ChatItem c, bool mute)
    {
        c.Muted = mute;
        Fire(new TdApi.SetChatNotificationSettings
        {
            ChatId = c.Id,
            NotificationSettings = new TdApi.ChatNotificationSettings
            {
                UseDefaultMuteFor = !mute,
                MuteFor = mute ? 10 * 365 * 24 * 3600 : 0, // "forever" in Telegram terms
                UseDefaultSound = true,
                UseDefaultShowPreview = true,
                UseDefaultMuteStories = true,
                UseDefaultStorySound = true,
                UseDefaultShowStoryPoster = true,
                UseDefaultDisablePinnedMessageNotifications = true,
                UseDefaultDisableMentionNotifications = true,
            },
        });
    }

    public static void MarkRead(ChatItem c)
    {
        if (Demo.On) { c.Unread = 0; return; }
        if (c.LastMessageId == 0) return;
        Fire(new TdApi.ViewMessages
        {
            ChatId = c.Id, MessageIds = [c.LastMessageId],
            Source = new TdApi.MessageSource.MessageSourceChatList(), ForceRead = true,
        });
    }

    /// <summary>Bio of a user, or description of a group/channel.</summary>
    public static async Task<string> About(ChatItem c)
    {
        if (Demo.On) return Demo.About(c.Id);
        try
        {
            if (c.Kind is ChatKind.User or ChatKind.Bot or ChatKind.Saved)
            {
                var full = await Client.ExecuteAsync(new TdApi.GetUserFullInfo { UserId = c.UserId != 0 ? c.UserId : c.Id });
                return full.Bio?.Text ?? full.BotInfo?.ShortDescription ?? "";
            }
            var chat = await Client.ExecuteAsync(new TdApi.GetChat { ChatId = c.Id });
            return chat.Type switch
            {
                TdApi.ChatType.ChatTypeSupergroup sg =>
                    (await Client.ExecuteAsync(new TdApi.GetSupergroupFullInfo { SupergroupId = sg.SupergroupId })).Description,
                TdApi.ChatType.ChatTypeBasicGroup bg =>
                    (await Client.ExecuteAsync(new TdApi.GetBasicGroupFullInfo { BasicGroupId = bg.BasicGroupId })).Description,
                _ => "",
            };
        }
        catch { return ""; }
    }

    public static async Task SaveProfile(string first, string last, string bio, string username)
    {
        var me = Users[MyId];
        var oldUser = me.Usernames?.EditableUsername ?? "";
        if (Demo.On)
        {
            me.FirstName = first; me.LastName = last;
            Demo.SetMyBio(bio);
            MeChanged?.Invoke();
            return;
        }
        if (first != me.FirstName || last != me.LastName)
            await Client.ExecuteAsync(new TdApi.SetName { FirstName = first, LastName = last });
        await Client.ExecuteAsync(new TdApi.SetBio { Bio = bio });
        if (username != oldUser)
            await Client.ExecuteAsync(new TdApi.SetUsername { Username = username });
        MeChanged?.Invoke();
    }

    /// <summary>Fire-and-forget TDLib call; a no-op in demo mode.</summary>
    public static void Fire<T>(TdApi.Function<T> f) where T : TdApi.Object
    {
        if (Demo.On) return;
        _ = Client.ExecuteAsync(f);
    }

    public static void SetOnline(bool online) =>
        Fire(new TdApi.SetOption { Name = "online", Value = new TdApi.OptionValue.OptionValueBoolean { Value = online } });

    /// <summary>getChatHistory can return fewer messages than asked (esp. first call), so keep asking.</summary>
    public static async Task<List<TdApi.Message>> History(long chatId, long fromId, int want)
    {
        if (Demo.On)
        {
            var demo = Demo.GetHistory(chatId, fromId, want);
            Archive.RecordAll(demo);
            return demo;
        }
        var result = new List<TdApi.Message>();
        for (var attempt = 0; attempt < 5 && result.Count < want; attempt++)
        {
            TdApi.Messages batch;
            try
            {
                batch = await Client.ExecuteAsync(new TdApi.GetChatHistory
                {
                    ChatId = chatId, FromMessageId = fromId, Offset = 0, Limit = want - result.Count, OnlyLocal = false,
                });
            }
            catch { break; }
            if (batch.Messages_.Length == 0) break;
            result.AddRange(batch.Messages_);
            fromId = batch.Messages_[^1].Id;
        }
        Archive.RecordAll(result);
        return result; // newest first
    }
}
