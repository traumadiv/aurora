using System.IO;
using System.Text.Json;
using TdLib;

namespace Aurora.Services;

/// <summary>A message as Aurora saw it: enough to show it again after it was deleted or edited.</summary>
public sealed class ArchivedMessage
{
    public long Id { get; set; }
    public long ChatId { get; set; }
    public string Sender { get; set; } = "";
    public long SenderKey { get; set; }
    public int Date { get; set; }
    public bool Out { get; set; }
    public string Text { get; set; } = "";
    /// <summary>Earlier texts, oldest first.</summary>
    public List<string> Versions { get; set; } = new();
    public int DeletedAt { get; set; }
    public bool Deleted => DeletedAt > 0;

    /// <summary>A stand-in message for rendering a deleted one; TDLib has already forgotten it.</summary>
    public TdApi.Message ToMessage() => new()
    {
        Id = Id, ChatId = ChatId, Date = Date, IsOutgoing = Out,
        SenderId = SenderKey < 0
            ? new TdApi.MessageSender.MessageSenderChat { ChatId = -SenderKey }
            : new TdApi.MessageSender.MessageSenderUser { UserId = SenderKey },
        Content = new TdApi.MessageContent.MessageText { Text = new TdApi.FormattedText { Text = Text, Entities = [] } },
    };
}

/// <summary>
/// Local copy of every message that reaches this computer, kept in %APPDATA%\Aurora\archive, one file per chat.
/// Telegram tells clients to forget deleted messages; Aurora keeps what it already received and marks it deleted.
/// Only messages seen while Aurora was running can be kept. Nothing here leaves the machine.
/// </summary>
public static class Archive
{
    static readonly Dictionary<long, Dictionary<long, ArchivedMessage>> _chats = new();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    const int MaxPerChat = 20000;

    public static string Dir => Path.Combine(Config.Dir, "archive");

    static bool On => App.Cfg?.KeepDeleted ?? true;

    static Dictionary<long, ArchivedMessage> Chat(long chatId)
    {
        if (_chats.TryGetValue(chatId, out var d)) return d;
        d = new Dictionary<long, ArchivedMessage>();
        _chats[chatId] = d;
        if (Demo.On) return d;
        var file = FileOf(chatId);
        if (!File.Exists(file)) return d;
        try
        {
            // append-only log: the last line for an id wins
            foreach (var line in File.ReadLines(file))
            {
                if (line.Length == 0) continue;
                try
                {
                    var r = JsonSerializer.Deserialize<ArchivedMessage>(line, Json);
                    if (r != null) d[r.Id] = r;
                }
                catch { /* a torn line after a crash */ }
            }
            if (d.Count > MaxPerChat) Compact(chatId, d);
        }
        catch { }
        return d;
    }

    static string FileOf(long chatId) => Path.Combine(Dir, $"{chatId}.jsonl");

    static void Save(ArchivedMessage r)
    {
        if (Demo.On) return;
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(FileOf(r.ChatId), JsonSerializer.Serialize(r, Json) + "\n");
        }
        catch { }
    }

    /// <summary>Rewrites a chat file keeping the newest messages plus everything deleted or edited.</summary>
    static void Compact(long chatId, Dictionary<long, ArchivedMessage> d)
    {
        var keep = d.Values.OrderByDescending(r => r.Id).Take(MaxPerChat / 2)
            .Concat(d.Values.Where(r => r.Deleted || r.Versions.Count > 0)).DistinctBy(r => r.Id).OrderBy(r => r.Id).ToList();
        d.Clear();
        foreach (var r in keep) d[r.Id] = r;
        Rewrite(chatId, keep);
    }

    static void Rewrite(long chatId, IEnumerable<ArchivedMessage> all)
    {
        try { File.WriteAllLines(FileOf(chatId), all.OrderBy(r => r.Id).Select(r => JsonSerializer.Serialize(r, Json))); } catch { }
    }

    static long Key(TdApi.MessageSender? s) => s switch
    {
        TdApi.MessageSender.MessageSenderUser u => u.UserId,
        TdApi.MessageSender.MessageSenderChat c => -c.ChatId,
        _ => 0,
    };

    public static void Record(TdApi.Message m)
    {
        if (!On || m.Content == null || Text.IsService(m.Content) || m.SendingState != null) return;
        var text = Text.Describe(m.Content);
        if (text.Length == 0) return;
        var d = Chat(m.ChatId);
        if (d.TryGetValue(m.Id, out var old) && old.Text == text) return;
        if (old != null)
        {
            if (old.Deleted) return;
            old.Versions.Add(old.Text);
            old.Text = text;
            Save(old);
            return;
        }
        var r = new ArchivedMessage
        {
            Id = m.Id, ChatId = m.ChatId, Date = m.Date, Out = m.IsOutgoing, Text = text,
            Sender = m.IsOutgoing ? "Вы" : Tg.SenderName(m.SenderId), SenderKey = Key(m.SenderId),
        };
        d[m.Id] = r;
        Save(r);
    }

    public static void RecordAll(IEnumerable<TdApi.Message> ms)
    {
        foreach (var m in ms) Record(m);
    }

    /// <summary>The message text changed: keep what it said before.</summary>
    public static void Edited(long chatId, long id, TdApi.MessageContent content)
    {
        if (!On) return;
        var d = Chat(chatId);
        if (!d.TryGetValue(id, out var r) || r.Deleted) return;
        var text = Text.Describe(content);
        if (text == r.Text) return;
        r.Versions.Add(r.Text);
        r.Text = text;
        Save(r);
    }

    public static void Deleted(long chatId, long[] ids)
    {
        if (!On) return;
        var d = Chat(chatId);
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var id in ids)
            if (d.TryGetValue(id, out var r) && !r.Deleted)
            {
                r.DeletedAt = now;
                Save(r);
            }
    }

    public static ArchivedMessage? Get(long chatId, long id) => On && Chat(chatId).TryGetValue(id, out var r) ? r : null;

    /// <summary>Deleted messages of a chat with ids in [from, to], for weaving them back into the history.</summary>
    public static List<ArchivedMessage> DeletedBetween(long chatId, long from, long to) =>
        On ? Chat(chatId).Values.Where(r => r.Deleted && r.Id >= from && r.Id <= to).OrderBy(r => r.Id).ToList() : new();

    /// <summary>The user deleted these in Aurora on purpose: don't keep them.</summary>
    public static void Forget(long chatId, long[] ids)
    {
        var d = Chat(chatId);
        var any = false;
        foreach (var id in ids) any |= d.Remove(id);
        if (any && !Demo.On) Rewrite(chatId, d.Values);
    }

    public static void Clear()
    {
        _chats.Clear();
        try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { }
    }
}
