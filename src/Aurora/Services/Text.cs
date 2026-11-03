using TdLib;

namespace Aurora.Services;

public static class Text
{
    public static DateTime Local(int unix) => DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;

    public static string WasOnline(int unix)
    {
        var t = Local(unix);
        var mins = (DateTime.Now - t).TotalMinutes;
        if (mins < 1) return L("только что");
        if (mins < 60) return L("{0} мин назад", (int)mins);
        if (t.Date == DateTime.Today) return L("в ") + t.ToString("HH:mm");
        if (t.Date == DateTime.Today.AddDays(-1)) return L("вчера в ") + t.ToString("HH:mm");
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

    /// <summary>Rich text without styling: for previews in the chat list and for copying.</summary>
    public static string RichPlain(TdApi.RichText? t) => t switch
    {
        null => "",
        TdApi.RichText.RichTextPlain x => x.Text,
        TdApi.RichText.RichTexts x => string.Concat(x.Texts.Select(RichPlain)),
        TdApi.RichText.RichTextCustomEmoji x => x.AlternativeText,
        TdApi.RichText.RichTextMathematicalExpression x => x.Expression,
        _ => t.GetType().GetProperty("Text")?.GetValue(t) is TdApi.RichText inner ? RichPlain(inner) : "",
    };

    /// <summary>The text of a rich message, block by block on separate lines.</summary>
    public static string RichPlain(TdApi.RichMessage? m) =>
        m == null ? "" : string.Join("\n", m.Blocks.Select(BlockPlain).Where(s => s.Length > 0));

    static string BlockPlain(TdApi.PageBlock b) => b switch
    {
        TdApi.PageBlock.PageBlockParagraph x => RichPlain(x.Text),
        TdApi.PageBlock.PageBlockTitle x => RichPlain(x.Title),
        TdApi.PageBlock.PageBlockSubtitle x => RichPlain(x.Subtitle),
        TdApi.PageBlock.PageBlockHeader x => RichPlain(x.Header),
        TdApi.PageBlock.PageBlockSubheader x => RichPlain(x.Subheader),
        TdApi.PageBlock.PageBlockSectionHeading x => RichPlain(x.Text),
        TdApi.PageBlock.PageBlockPreformatted x => RichPlain(x.Text),
        TdApi.PageBlock.PageBlockPullQuote x => RichPlain(x.Text),
        TdApi.PageBlock.PageBlockExpandableBlockQuote x => RichPlain(x.Text),
        TdApi.PageBlock.PageBlockBlockQuote x => string.Join("\n", x.Blocks.Select(BlockPlain)),
        TdApi.PageBlock.PageBlockList x => string.Join("\n", x.Items.Select(i => "• " + string.Join(" ", i.Blocks.Select(BlockPlain)))),
        TdApi.PageBlock.PageBlockDetails x => RichPlain(x.Header),
        TdApi.PageBlock.PageBlockPhoto => L("[Фото]"),
        TdApi.PageBlock.PageBlockVideo => L("[Видео]"),
        _ => "",
    };

    static string RichPreview(TdApi.RichMessage m)
    {
        var text = RichPlain(m);
        var line = text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        return line.Length > 0 ? line : L("[Сообщение]");
    }

    public static string Describe(TdApi.MessageContent? c) => c switch
    {
        null => "",
        TdApi.MessageContent.MessageText t => t.Text.Text,
        TdApi.MessageContent.MessagePhoto p => Cap(p.Caption, L("[Фото]")),
        TdApi.MessageContent.MessageVideo v => Cap(v.Caption, L("[Видео]")),
        TdApi.MessageContent.MessageAnimation a => Cap(a.Caption, "[GIF]"),
        TdApi.MessageContent.MessageAudio au => Cap(au.Caption, L("[Музыка]")),
        TdApi.MessageContent.MessageDocument d => Cap(d.Caption, L("[Файл: {0}]", d.Document.FileName)),
        TdApi.MessageContent.MessageVoiceNote vn => Cap(vn.Caption, L("[Голосовое]")),
        TdApi.MessageContent.MessageVideoNote => L("[Кружок]"),
        TdApi.MessageContent.MessageSticker s => L("[Стикер {0}]", s.Sticker.Emoji),
        TdApi.MessageContent.MessageAnimatedEmoji e => e.Emoji,
        TdApi.MessageContent.MessageContact => L("[Контакт]"),
        TdApi.MessageContent.MessageLocation => L("[Геолокация]"),
        TdApi.MessageContent.MessageVenue => L("[Место]"),
        TdApi.MessageContent.MessagePoll => L("[Опрос]"),
        TdApi.MessageContent.MessageDice d => d.Emoji,
        TdApi.MessageContent.MessageCall => L("[Звонок]"),
        TdApi.MessageContent.MessageChatAddMembers => L("* добавил(а) участников"),
        TdApi.MessageContent.MessageChatJoinByLink => L("* вступил(а) по ссылке"),
        TdApi.MessageContent.MessageChatDeleteMember => L("* покинул(а) чат"),
        TdApi.MessageContent.MessageChatChangeTitle ct => L("* новое название: ") + ct.Title,
        TdApi.MessageContent.MessageChatChangePhoto => L("* новое фото чата"),
        TdApi.MessageContent.MessagePinMessage => L("* закрепил(а) сообщение"),
        TdApi.MessageContent.MessageContactRegistered => L("* теперь в Telegram!"),
        TdApi.MessageContent.MessageRichMessage r => RichPreview(r.Message),
        TdApi.MessageContent.MessageSupergroupChatCreate g => L("* создал(а) группу «{0}»", NoEmoji(g.Title)),
        TdApi.MessageContent.MessageBasicGroupChatCreate g => L("* создал(а) группу «{0}»", NoEmoji(g.Title)),
        TdApi.MessageContent.MessageChatUpgradeTo => L("* группа стала супергруппой"),
        TdApi.MessageContent.MessageChatUpgradeFrom => L("* группа стала супергруппой"),
        TdApi.MessageContent.MessageScreenshotTaken => L("* сделал(а) скриншот"),
        TdApi.MessageContent.MessageChatSetMessageAutoDeleteTime => L("* изменил(а) автоудаление сообщений"),
        TdApi.MessageContent.MessageForumTopicCreated => L("* создал(а) тему"),
        TdApi.MessageContent.MessageVideoChatStarted => L("* начал(а) видеочат"),
        TdApi.MessageContent.MessageVideoChatEnded => L("* видеочат завершён"),
        TdApi.MessageContent.MessageGiftedPremium => L("[Подарок: Telegram Premium]"),
        TdApi.MessageContent.MessageStory => L("[История]"),
        TdApi.MessageContent.MessageGame => L("[Игра]"),
        TdApi.MessageContent.MessageInvoice => L("[Счёт]"),
        TdApi.MessageContent.MessageExpiredPhoto => L("[Фото удалено]"),
        TdApi.MessageContent.MessageExpiredVideo => L("[Видео удалено]"),
        TdApi.MessageContent.MessageUnsupported => L("[Не поддерживается]"),
        _ => L("[Сообщение, которое Aurora пока не умеет показывать]"),
    };

    public static bool IsService(TdApi.MessageContent? c) =>
        Describe(c).StartsWith("* ", StringComparison.Ordinal);
}
