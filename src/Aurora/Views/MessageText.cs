using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TdLib;
using Aurora.Services;

namespace Aurora.Views;

/// <summary>
/// Message text as WPF inlines: Telegram's formatting (bold, links, spoilers, custom emoji…)
/// and the newer "rich" messages, which are built from page blocks like Instant View articles.
/// </summary>
public static class MessageText
{
    static readonly Regex Url = new(@"(https?://|www\.|t\.me/)[^\s<>""]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly FontFamily Mono = new("Consolas, Courier New");

    // ---------- plain text ----------

    /// <summary>Text with clickable links and smileys.</summary>
    public static void Plain(InlineCollection to, string text)
    {
        var pos = 0;
        foreach (Match mt in Url.Matches(text))
        {
            if (mt.Index < pos) continue;
            if (mt.Index > pos) Smiles(to, text[pos..mt.Index]);
            var raw = mt.Value.TrimEnd('.', ',', ')', '!', '?');
            var href = raw.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? raw : "https://" + raw;
            to.Add(Link(new Run(raw), href) ?? (Inline)new Run(raw));
            pos = mt.Index + raw.Length;
        }
        if (pos < text.Length) Smiles(to, text[pos..]);
    }

    static void Smiles(InlineCollection to, string text)
    {
        foreach (var (piece, sm) in Smileys.Split(text))
            to.Add(sm == null ? new Run(piece) : Smileys.Make(sm, 20, piece));
    }

    static Hyperlink? Link(Inline content, string href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)) return null;
        var link = new Hyperlink(content) { NavigateUri = uri, Cursor = Cursors.Hand, ToolTip = href };
        link.RequestNavigate += (_, e) =>
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
        };
        return link;
    }

    // ---------- formatted text ----------

    public static void Formatted(InlineCollection to, TdApi.FormattedText? ft)
    {
        var text = ft?.Text ?? "";
        var ents = ft?.Entities ?? Array.Empty<TdApi.TextEntity>();
        if (ents.Length == 0) { Plain(to, text); return; }

        // a message of nothing but one to three custom emoji is shown big, like in Telegram
        var emoji = ents.Where(e => e.Type is TdApi.TextEntityType.TextEntityTypeCustomEmoji).ToArray();
        var big = emoji.Length is > 0 and <= 3 && Strip(text, emoji).Trim().Length == 0;

        var cuts = new SortedSet<int> { 0, text.Length };
        foreach (var e in ents)
        {
            cuts.Add(Math.Clamp(e.Offset, 0, text.Length));
            cuts.Add(Math.Clamp(e.Offset + e.Length, 0, text.Length));
        }
        var points = cuts.ToArray();
        for (var i = 0; i + 1 < points.Length; i++)
        {
            int a = points[i], b = points[i + 1];
            var on = ents.Where(e => e.Offset <= a && e.Offset + e.Length >= b).ToArray();
            var ce = on.FirstOrDefault(e => e.Type is TdApi.TextEntityType.TextEntityTypeCustomEmoji);
            if (ce != null)
            {
                // one picture for the whole entity, even if other formatting splits it
                if (ce.Offset == a)
                    to.Add(Emoji(((TdApi.TextEntityType.TextEntityTypeCustomEmoji)ce.Type).CustomEmojiId,
                        text.Substring(ce.Offset, Math.Min(ce.Length, text.Length - ce.Offset)), big ? 72 : 20));
                continue;
            }
            to.Add(Styled(text[a..b], on));
        }
    }

    static string Strip(string text, TdApi.TextEntity[] ents)
    {
        var chars = text.ToCharArray();
        foreach (var e in ents)
            for (var i = e.Offset; i < e.Offset + e.Length && i < chars.Length; i++) chars[i] = ' ';
        return new string(chars);
    }

    static Inline Styled(string piece, TdApi.TextEntity[] on)
    {
        var span = new Span();
        string? href = null;
        foreach (var e in on)
            switch (e.Type)
            {
                case TdApi.TextEntityType.TextEntityTypeTextUrl u: href = u.Url; break;
                case TdApi.TextEntityType.TextEntityTypeMention: href = "https://t.me/" + piece.TrimStart('@'); break;
                case TdApi.TextEntityType.TextEntityTypeEmailAddress: href = "mailto:" + piece; break;
            }
        if (href != null && Link(new Run(piece), href) is { } link) span.Inlines.Add(link);
        else Plain(span.Inlines, piece);

        foreach (var e in on)
            switch (e.Type)
            {
                case TdApi.TextEntityType.TextEntityTypeBold: span.FontWeight = FontWeights.Bold; break;
                case TdApi.TextEntityType.TextEntityTypeItalic: span.FontStyle = FontStyles.Italic; break;
                case TdApi.TextEntityType.TextEntityTypeUnderline: Decorate(span, TextDecorations.Underline); break;
                case TdApi.TextEntityType.TextEntityTypeStrikethrough: Decorate(span, TextDecorations.Strikethrough); break;
                case TdApi.TextEntityType.TextEntityTypeCode:
                case TdApi.TextEntityType.TextEntityTypePre:
                case TdApi.TextEntityType.TextEntityTypePreCode:
                    Code(span);
                    break;
                case TdApi.TextEntityType.TextEntityTypeBlockQuote:
                case TdApi.TextEntityType.TextEntityTypeExpandableBlockQuote:
                    span.FontStyle = FontStyles.Italic;
                    span.SetResourceReference(TextElement.ForegroundProperty, "SubText");
                    break;
                case TdApi.TextEntityType.TextEntityTypeSpoiler: Spoiler(span); break;
            }
        return span;
    }

    static void Decorate(Span s, TextDecorationCollection d)
    {
        var all = new TextDecorationCollection(s.TextDecorations ?? new TextDecorationCollection());
        foreach (var x in d) all.Add(x);
        s.TextDecorations = all;
    }

    static void Code(Span s)
    {
        s.FontFamily = Mono;
        s.Background = new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80));
    }

    /// <summary>Hidden under a grey bar until clicked.</summary>
    static void Spoiler(Span s)
    {
        var bar = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        s.Background = bar;
        s.Foreground = bar;
        s.Cursor = Cursors.Hand;
        s.ToolTip = L("Спойлер: нажмите, чтобы показать");
        s.MouseLeftButtonUp += (_, _) =>
        {
            s.ClearValue(TextElement.BackgroundProperty);
            s.ClearValue(TextElement.ForegroundProperty);
            s.ClearValue(FrameworkContentElement.CursorProperty);
            s.ClearValue(FrameworkContentElement.ToolTipProperty);
        };
    }

    public static Inline Emoji(long id, string alt, double size) =>
        new InlineUIContainer(new StickerBox(Media.CustomEmoji(id), size, size, alt, play: true, fontSize: size >= 40 ? 0 : 14))
        {
            BaselineAlignment = size >= 40 ? BaselineAlignment.Bottom : BaselineAlignment.Center,
        };

    // ---------- rich messages ----------

    /// <summary>Lays out a rich message block by block, one line or more each.</summary>
    public static void Rich(InlineCollection to, TdApi.RichMessage rm, Func<TdApi.Photo, Inline> photo)
    {
        var first = true;
        foreach (var b in rm.Blocks)
        {
            var before = to.Count;
            if (!first) to.Add(new LineBreak());
            if (!Block(to, b, photo, 0))
            {
                while (to.Count > before) to.Remove(to.LastInline);
                continue;
            }
            first = false;
        }
    }

    static bool Block(InlineCollection to, TdApi.PageBlock b, Func<TdApi.Photo, Inline> photo, int depth)
    {
        switch (b)
        {
            case TdApi.PageBlock.PageBlockParagraph x: to.Add(Rt(x.Text)); return true;
            case TdApi.PageBlock.PageBlockTitle x: to.Add(Sized(Rt(x.Title), 17, true)); return true;
            case TdApi.PageBlock.PageBlockSubtitle x: to.Add(Sized(Rt(x.Subtitle), 14, false)); return true;
            case TdApi.PageBlock.PageBlockHeader x: to.Add(Sized(Rt(x.Header), 15, true)); return true;
            case TdApi.PageBlock.PageBlockSubheader x: to.Add(Sized(Rt(x.Subheader), 13, true)); return true;
            case TdApi.PageBlock.PageBlockSectionHeading x: to.Add(Sized(Rt(x.Text), x.Size <= 1 ? 16 : x.Size == 2 ? 14 : 13, true)); return true;
            case TdApi.PageBlock.PageBlockKicker x: to.Add(Muted(Sized(Rt(x.Kicker), 11, true))); return true;
            case TdApi.PageBlock.PageBlockFooter x: to.Add(Muted(Sized(Rt(x.Footer), 11, false))); return true;
            case TdApi.PageBlock.PageBlockAuthorDate x:
                var by = new Span(Rt(x.Author));
                if (x.PublishDate > 0) by.Inlines.Add(new Run(", " + Text.Stamp(x.PublishDate)));
                to.Add(Muted(Sized(by, 11, false)));
                return true;
            case TdApi.PageBlock.PageBlockThinking x:
                var think = new Span(Rt(x.Text)) { FontStyle = FontStyles.Italic };
                to.Add(Muted(think));
                return true;
            case TdApi.PageBlock.PageBlockPreformatted x:
                var pre = new Span(Rt(x.Text));
                Code(pre);
                to.Add(pre);
                return true;
            case TdApi.PageBlock.PageBlockMathematicalExpression x:
                var math = new Span(new Run(x.Expression));
                Code(math);
                to.Add(math);
                return true;
            case TdApi.PageBlock.PageBlockDivider:
                to.Add(Muted(new Span(new Run("────────────────"))));
                return true;
            case TdApi.PageBlock.PageBlockBlockQuote x:
                Quote(to, x.Blocks.Select(bl => (Action<InlineCollection>)(t => Block(t, bl, photo, depth + 1))), x.Credit);
                return true;
            case TdApi.PageBlock.PageBlockPullQuote x:
                Quote(to, [t => t.Add(Rt(x.Text))], x.Credit);
                return true;
            case TdApi.PageBlock.PageBlockExpandableBlockQuote x:
                Quote(to, [t => t.Add(Rt(x.Text))], x.Credit);
                return true;
            case TdApi.PageBlock.PageBlockList x:
                var n = 0;
                foreach (var item in x.Items)
                {
                    if (n++ > 0) to.Add(new LineBreak());
                    var mark = item.HasCheckbox ? (item.IsChecked ? "☑ " : "☐ ") : string.IsNullOrEmpty(item.Label) ? "• " : item.Label + " ";
                    to.Add(new Run(new string(' ', depth * 3) + "  " + mark));
                    var inner = false;
                    foreach (var bl in item.Blocks)
                    {
                        if (inner) to.Add(new LineBreak());
                        inner = Block(to, bl, photo, depth + 1) || inner;
                    }
                }
                return n > 0;
            case TdApi.PageBlock.PageBlockDetails x:
                to.Add(new Span(Rt(x.Header)) { FontWeight = FontWeights.Bold });
                foreach (var bl in x.Blocks)
                {
                    to.Add(new LineBreak());
                    Block(to, bl, photo, depth + 1);
                }
                return true;
            case TdApi.PageBlock.PageBlockTable x:
                var rows = 0;
                if (x.Caption != null && Text.RichPlain(x.Caption).Length > 0) { to.Add(new Span(Rt(x.Caption)) { FontWeight = FontWeights.Bold }); rows++; }
                foreach (var row in x.Cells)
                {
                    if (rows++ > 0) to.Add(new LineBreak());
                    var line = new Span();
                    for (var c = 0; c < row.Length; c++)
                    {
                        if (c > 0) line.Inlines.Add(Muted(new Span(new Run("  │  "))));
                        var cell = new Span(Rt(row[c].Text));
                        if (row[c].IsHeader) cell.FontWeight = FontWeights.Bold;
                        line.Inlines.Add(cell);
                    }
                    to.Add(line);
                }
                return rows > 0;
            case TdApi.PageBlock.PageBlockPhoto x:
                to.Add(photo(x.Photo));
                Caption(to, x.Caption);
                return true;
            case TdApi.PageBlock.PageBlockCollage x: return Many(to, x.Blocks, x.Caption, photo, depth);
            case TdApi.PageBlock.PageBlockSlideshow x: return Many(to, x.Blocks, x.Caption, photo, depth);
            case TdApi.PageBlock.PageBlockCover x: return Block(to, x.Cover, photo, depth);
            case TdApi.PageBlock.PageBlockAnimation x: return Tag(to, "[GIF]", x.Caption);
            case TdApi.PageBlock.PageBlockVideo x: return Tag(to, L("[Видео]"), x.Caption);
            case TdApi.PageBlock.PageBlockAudio x: return Tag(to, L("[Музыка]"), x.Caption);
            case TdApi.PageBlock.PageBlockVoiceNote x: return Tag(to, L("[Голосовое]"), x.Caption);
            case TdApi.PageBlock.PageBlockDocument x: return Tag(to, L("[Файл: {0}]", x.Document?.FileName), x.Caption);
            case TdApi.PageBlock.PageBlockMap x: return Tag(to, L("[Карта]"), x.Caption);
            case TdApi.PageBlock.PageBlockEmbedded x:
                to.Add(Link(new Run(x.Url), x.Url) ?? (Inline)new Run(L("[Встроенное содержимое]")));
                Caption(to, x.Caption);
                return true;
            case TdApi.PageBlock.PageBlockEmbeddedPost x:
                to.Add(new Span(new Run(x.Author)) { FontWeight = FontWeights.Bold });
                foreach (var bl in x.Blocks) { to.Add(new LineBreak()); Block(to, bl, photo, depth + 1); }
                return true;
            case TdApi.PageBlock.PageBlockChatLink x:
                var chat = new Run(x.Title + (string.IsNullOrEmpty(x.Username) ? "" : " (@" + x.Username + ")"));
                to.Add(string.IsNullOrEmpty(x.Username) ? chat : Link(chat, "https://t.me/" + x.Username) ?? (Inline)chat);
                return true;
            case TdApi.PageBlock.PageBlockRelatedArticles x:
                to.Add(new Span(Rt(x.Header)) { FontWeight = FontWeights.Bold });
                foreach (var a in x.Articles)
                {
                    to.Add(new LineBreak());
                    var title = new Run("  • " + (string.IsNullOrEmpty(a.Title) ? a.Url : a.Title));
                    to.Add(Link(title, a.Url) ?? (Inline)title);
                }
                return true;
            default:
                return false; // anchors, button rows and blocks Aurora does not know yet
        }
    }

    static bool Many(InlineCollection to, TdApi.PageBlock[] blocks, TdApi.PageBlockCaption? caption, Func<TdApi.Photo, Inline> photo, int depth)
    {
        var any = false;
        foreach (var bl in blocks)
        {
            if (any) to.Add(new LineBreak());
            any = Block(to, bl, photo, depth + 1) || any;
        }
        Caption(to, caption);
        return any;
    }

    static bool Tag(InlineCollection to, string tag, TdApi.PageBlockCaption? caption)
    {
        to.Add(Muted(new Span(new Run(tag))));
        Caption(to, caption);
        return true;
    }

    static void Caption(InlineCollection to, TdApi.PageBlockCaption? c)
    {
        if (c == null) return;
        if (Text.RichPlain(c.Text).Length > 0) { to.Add(new LineBreak()); to.Add(Sized(Rt(c.Text), 11, false)); }
        if (Text.RichPlain(c.Credit).Length > 0) { to.Add(new LineBreak()); to.Add(Muted(Sized(Rt(c.Credit), 10, false))); }
    }

    static void Quote(InlineCollection to, IEnumerable<Action<InlineCollection>> parts, TdApi.RichText? credit)
    {
        var q = new Span { FontStyle = FontStyles.Italic };
        var bar = new Run("▌ ") { FontStyle = FontStyles.Normal };
        bar.SetResourceReference(TextElement.ForegroundProperty, "LinkInk");
        var n = 0;
        foreach (var part in parts)
        {
            if (n++ > 0) q.Inlines.Add(new LineBreak());
            q.Inlines.Add(n == 1 ? bar : new Run("▌ ") { Foreground = bar.Foreground, FontStyle = FontStyles.Normal });
            part(q.Inlines);
        }
        if (Text.RichPlain(credit).Length > 0)
        {
            q.Inlines.Add(new LineBreak());
            q.Inlines.Add(Muted(Sized(Rt(credit!), 11, false)));
        }
        to.Add(q);
    }

    static Span Sized(Inline i, double size, bool bold) => new(i) { FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };

    static Span Muted(Span s)
    {
        s.SetResourceReference(TextElement.ForegroundProperty, "Muted");
        return s;
    }

    /// <summary>One piece of rich text, styles nested the way Telegram nests them.</summary>
    static Inline Rt(TdApi.RichText? t)
    {
        switch (t)
        {
            case null: return new Run("");
            case TdApi.RichText.RichTextPlain x:
                var plain = new Span();
                Plain(plain.Inlines, x.Text);
                return plain;
            case TdApi.RichText.RichTexts x:
                var all = new Span();
                foreach (var part in x.Texts) all.Inlines.Add(Rt(part));
                return all;
            case TdApi.RichText.RichTextBold x: return new Span(Rt(x.Text)) { FontWeight = FontWeights.Bold };
            case TdApi.RichText.RichTextItalic x: return new Span(Rt(x.Text)) { FontStyle = FontStyles.Italic };
            case TdApi.RichText.RichTextUnderline x:
                var u = new Span(Rt(x.Text));
                Decorate(u, TextDecorations.Underline);
                return u;
            case TdApi.RichText.RichTextStrikethrough x:
                var s = new Span(Rt(x.Text));
                Decorate(s, TextDecorations.Strikethrough);
                return s;
            case TdApi.RichText.RichTextFixed x:
                var f = new Span(Rt(x.Text));
                Code(f);
                return f;
            case TdApi.RichText.RichTextMarked x:
                return new Span(Rt(x.Text)) { Background = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xE0, 0x40)) };
            case TdApi.RichText.RichTextSpoiler x:
                var sp = new Span(Rt(x.Text));
                Spoiler(sp);
                return sp;
            case TdApi.RichText.RichTextSubscript x: return new Span(Rt(x.Text)) { BaselineAlignment = BaselineAlignment.Subscript, FontSize = 9 };
            case TdApi.RichText.RichTextSuperscript x: return new Span(Rt(x.Text)) { BaselineAlignment = BaselineAlignment.Superscript, FontSize = 9 };
            case TdApi.RichText.RichTextUrl x: return Link(Rt(x.Text), x.Url) ?? Rt(x.Text);
            case TdApi.RichText.RichTextAnchorLink x: return string.IsNullOrEmpty(x.Url) ? Rt(x.Text) : Link(Rt(x.Text), x.Url) ?? Rt(x.Text);
            case TdApi.RichText.RichTextReferenceLink x: return string.IsNullOrEmpty(x.Url) ? Rt(x.Text) : Link(Rt(x.Text), x.Url) ?? Rt(x.Text);
            case TdApi.RichText.RichTextEmailAddress x: return Link(Rt(x.Text), "mailto:" + x.EmailAddress) ?? Rt(x.Text);
            case TdApi.RichText.RichTextMention x: return Link(Rt(x.Text), "https://t.me/" + x.Username) ?? Rt(x.Text);
            case TdApi.RichText.RichTextPhoneNumber x: return Rt(x.Text);
            case TdApi.RichText.RichTextMentionName x: return Rt(x.Text);
            case TdApi.RichText.RichTextHashtag x: return Rt(x.Text);
            case TdApi.RichText.RichTextCashtag x: return Rt(x.Text);
            case TdApi.RichText.RichTextBotCommand x: return Rt(x.Text);
            case TdApi.RichText.RichTextBankCardNumber x: return Rt(x.Text);
            case TdApi.RichText.RichTextDateTime x: return Rt(x.Text);
            case TdApi.RichText.RichTextDiff x: return Rt(x.Text);
            case TdApi.RichText.RichTextReference x: return Rt(x.Text);
            case TdApi.RichText.RichTextCustomEmoji x: return Emoji(x.CustomEmojiId, x.AlternativeText, 20);
            case TdApi.RichText.RichTextMathematicalExpression x:
                var m = new Span(new Run(x.Expression));
                Code(m);
                return m;
            default: return new Run(""); // anchors, icons, buttons
        }
    }
}
