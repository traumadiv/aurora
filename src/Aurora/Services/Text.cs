using TdLib;

namespace Aurora.Services;

public static class Text
{
    public static DateTime Local(int unix) => DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;

    public static string WasOnline(int unix)
    {
        var t = Local(unix);
        var mins = (DateTime.Now - t).TotalMinutes;
        if (mins < 1) return "только что";
        if (mins < 60) return $"{(int)mins} мин назад";
        if (t.Date == DateTime.Today) return "в " + t.ToString("HH:mm");
        if (t.Date == DateTime.Today.AddDays(-1)) return "вчера в " + t.ToString("HH:mm");
        return t.ToString("dd.MM.yy");
    }

    public static string Stamp(int unix)
    {
        var t = Local(unix);
        return t.Date == DateTime.Today ? t.ToString("HH:mm:ss") : t.ToString("dd.MM.yyyy HH:mm");
    }

    static string Cap(TdApi.FormattedText? caption, string tag) =>
        string.IsNullOrWhiteSpace(caption?.Text) ? tag : tag + " " + caption!.Text;

    /// <summary>Plain-text rendering of any message. Media is shown as a tag, ICQ was text-only anyway.</summary>
    /// <summary>
    /// Names without emoji: WPF draws emoji as grey outlines, which looks broken next to a chat title.
    /// Message text keeps them (they turn into smileys there). A name made only of emoji stays as is.
    /// </summary>
    public static string NoEmoji(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var r in s.EnumerateRunes())
        {
            var v = r.Value;
            var emoji = v is >= 0x1F000 and <= 0x1FAFF   // pictographs, smileys, flags, transport
                or >= 0x2600 and <= 0x27BF              // misc symbols, dingbats
                or >= 0x2B00 and <= 0x2BFF              // stars, arrows used as emoji
                or 0xFE0F or 0xFE0E or 0x200D or 0x20E3 // variation selectors, joiner, keycap
                or >= 0xE0020 and <= 0xE007F;           // flag tags
            if (!emoji) sb.Append(r.ToString());
        }
        var clean = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim(' ', '·', '|', '-', '—');
        return clean.Length > 0 ? clean : s;
    }

    public static string Describe(TdApi.MessageContent? c) => c switch
    {
        null => "",
        TdApi.MessageContent.MessageText t => t.Text.Text,
        TdApi.MessageContent.MessagePhoto p => Cap(p.Caption, "[Фото]"),
        TdApi.MessageContent.MessageVideo v => Cap(v.Caption, "[Видео]"),
        TdApi.MessageContent.MessageAnimation a => Cap(a.Caption, "[GIF]"),
        TdApi.MessageContent.MessageAudio au => Cap(au.Caption, "[Музыка]"),
        TdApi.MessageContent.MessageDocument d => Cap(d.Caption, $"[Файл: {d.Document.FileName}]"),
        TdApi.MessageContent.MessageVoiceNote vn => Cap(vn.Caption, "[Голосовое]"),
        TdApi.MessageContent.MessageVideoNote => "[Кружок]",
        TdApi.MessageContent.MessageSticker s => $"[Стикер {s.Sticker.Emoji}]",
        TdApi.MessageContent.MessageAnimatedEmoji e => e.Emoji,
        TdApi.MessageContent.MessageContact => "[Контакт]",
        TdApi.MessageContent.MessageLocation => "[Геолокация]",
        TdApi.MessageContent.MessageVenue => "[Место]",
        TdApi.MessageContent.MessagePoll => "[Опрос]",
        TdApi.MessageContent.MessageDice d => d.Emoji,
        TdApi.MessageContent.MessageCall => "[Звонок]",
        TdApi.MessageContent.MessageChatAddMembers => "* добавил(а) участников",
        TdApi.MessageContent.MessageChatJoinByLink => "* вступил(а) по ссылке",
        TdApi.MessageContent.MessageChatDeleteMember => "* покинул(а) чат",
        TdApi.MessageContent.MessageChatChangeTitle ct => "* новое название: " + ct.Title,
        TdApi.MessageContent.MessageChatChangePhoto => "* новое фото чата",
        TdApi.MessageContent.MessagePinMessage => "* закрепил(а) сообщение",
        TdApi.MessageContent.MessageContactRegistered => "* теперь в Telegram!",
        TdApi.MessageContent.MessageUnsupported => "[Не поддерживается]",
        _ => "[" + c.GetType().Name.Replace("Message", "") + "]",
    };

    public static bool IsService(TdApi.MessageContent? c) =>
        Describe(c).StartsWith("* ", StringComparison.Ordinal);
}
