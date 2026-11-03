using System.ComponentModel;
using System.Runtime.CompilerServices;
using Aurora.Services;

namespace Aurora.Models;

public enum ChatKind { User, Bot, Group, Channel, Saved }

/// <summary>Presence shown by the status flower.</summary>
public enum Presence { Online, Away, Offline, None }

public sealed class ChatItem : INotifyPropertyChanged
{
    public long Id { get; init; }
    public long UserId { get; set; }
    public long LastMessageId { get; set; }

    ChatKind _kind;
    public ChatKind Kind { get => _kind; set { if (Set(ref _kind, value)) { Raise(nameof(Group)); Raise(nameof(Icon)); } } }

    string _title = "";
    /// <summary>Shown name, emoji stripped (see Text.NoEmoji).</summary>
    public string Title { get => _title; set => Set(ref _title, Text.NoEmoji(value)); }

    Presence _presence = Presence.None;
    public Presence Presence
    {
        get => _presence;
        set { if (Set(ref _presence, value)) { Raise(nameof(Icon)); Raise(nameof(IsOnlineish)); Raise(nameof(ContactGroup)); } }
    }

    string _statusText = "";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    int _unread;
    public int Unread
    {
        get => _unread;
        set { if (Set(ref _unread, value)) { Raise(nameof(HasUnread)); Raise(nameof(UnreadBadge)); } }
    }

    public bool HasUnread => Unread > 0;
    public string UnreadBadge => Unread > 99 ? "99+" : Unread.ToString();

    bool _muted;
    public bool Muted { get => _muted; set => Set(ref _muted, value); }

    long _order;
    public long Order { get => _order; set => Set(ref _order, value); }

    bool _pinned;
    public bool Pinned { get => _pinned; set => Set(ref _pinned, value); }

    string _lastText = "";
    public string LastText { get => _lastText; set { if (Set(ref _lastText, value)) Raise(nameof(Preview)); } }

    int _lastDate;
    public int LastDate { get => _lastDate; set { if (Set(ref _lastDate, value)) Raise(nameof(TimeText)); } }

    string _typing = "";
    public string Typing
    {
        get => _typing;
        set { if (Set(ref _typing, value)) { Raise(nameof(Preview)); Raise(nameof(IsTyping)); } }
    }

    public bool IsTyping => Typing.Length > 0;

    /// <summary>Second line in the Chats tab.</summary>
    public string Preview => IsTyping ? Typing : LastText.ReplaceLineEndings(" ");

    public string TimeText
    {
        get
        {
            if (LastDate == 0) return "";
            var t = DateTimeOffset.FromUnixTimeSeconds(LastDate).LocalDateTime;
            if (t.Date == DateTime.Today) return t.ToString("HH:mm");
            if (t.Date == DateTime.Today.AddDays(-1)) return L("вчера");
            return t.Year == DateTime.Today.Year ? t.ToString("dd.MM") : t.ToString("dd.MM.yy");
        }
    }

    /// <summary>ICQ split the list into online on top, offline below.</summary>
    public string ContactGroup => Presence is Presence.Online or Presence.Away ? L("В сети") : L("Не в сети");

    public void RefreshTime() => Raise(nameof(TimeText));

    /// <summary>Contact-list section, ICQ style.</summary>
    public string Group => Kind switch
    {
        ChatKind.Group => L("Группы"),
        ChatKind.Channel => L("Каналы"),
        ChatKind.Bot => L("Боты"),
        _ => L("Контакты"),
    };

    /// <summary>Key of the icon resource drawn next to the name.</summary>
    public string Icon => Kind switch
    {
        ChatKind.Group => "group",
        ChatKind.Channel => "channel",
        ChatKind.Bot => "bot",
        ChatKind.Saved => "saved",
        _ => Presence switch
        {
            Presence.Online => "online",
            Presence.Away => "away",
            _ => "offline",
        },
    };

    /// <summary>Used by "hide offline": groups, channels and bots always count as visible.</summary>
    public bool IsOnlineish => Kind != ChatKind.User || Presence is Presence.Online or Presence.Away;

    public event PropertyChangedEventHandler? PropertyChanged;

    void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name!);
        return true;
    }
}
