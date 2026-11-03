using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;

namespace Aurora.Services;

/// <summary>
/// Interface language. Russian is the source: code calls L("Отправить"), and the other languages are tables
/// from Russian text to theirs (Lang/en.json, embedded). A missing line falls back to Russian, so nothing breaks
/// when a new string has not been translated yet. XAML text is translated by walking each window once on creation.
/// </summary>
public static class Loc
{
    public sealed record Language(string Code, string Name);

    public static readonly Language[] All = [new("ru", "Русский"), new("en", "English")];

    public static string Current { get; private set; } = "ru";

    static Dictionary<string, string> _map = new();

    /// <summary>Russian for Russian-speaking systems (and neighbours who read it), English for everyone else.</summary>
    public static string SystemDefault() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "ru" or "uk" or "be" or "kk" ? "ru" : "en";

    public static void Init(string? code)
    {
        Current = All.Any(l => l.Code == code) ? code! : SystemDefault();
        _map = Current == "ru" ? new() : Table(Current);
    }

    static Dictionary<string, string> Table(string code)
    {
        try
        {
            using var s = typeof(Loc).Assembly.GetManifestResourceStream($"Aurora.Lang.{code}.json");
            if (s == null) return new();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new();
        }
        catch { return new(); }
    }

    /// <summary>The text in the current language.</summary>
    public static string L(string ru) => _map.TryGetValue(ru, out var t) && t.Length > 0 ? t : ru;

    /// <summary>A template with {0}, {1}… placeholders, translated and filled in.</summary>
    public static string L(string ru, params object?[] args) => string.Format(CultureInfo.CurrentCulture, L(ru), args);

    /// <summary>Russian plural: 1 сообщение, 2 сообщения, 5 сообщений. English uses the first and the last.</summary>
    public static string Plural(long n, string one, string few, string many)
    {
        var form = Current == "ru"
            ? (n % 10 == 1 && n % 100 != 11 ? one : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few : many)
            : (n == 1 ? one : many);
        return L(form, n);
    }

    /// <summary>For the proxy list template, which the window walk cannot reach.</summary>
    public static string InUse => L(" · используется");

    // ---------- XAML ----------

    /// <summary>Translates the text written in a window's XAML. Bound values are left alone: they come from data.</summary>
    public static void Apply(DependencyObject root)
    {
        if (Current == "ru") return;
        Walk(root);
    }

    static void Walk(object node)
    {
        switch (node)
        {
            case Window w: w.Title = L(w.Title); break;
            case TextBlock tb when tb.Inlines.Count <= 1 && !Bound(tb, TextBlock.TextProperty):
                if (tb.Text.Length > 0) tb.Text = L(tb.Text);
                break;
            case Run r when !Bound(r, Run.TextProperty): r.Text = L(r.Text); break;
            case HeaderedContentControl h when h.Header is string s && !Bound(h, HeaderedContentControl.HeaderProperty): h.Header = L(s); break;
            case HeaderedItemsControl hi when hi.Header is string s2 && !Bound(hi, HeaderedItemsControl.HeaderProperty): hi.Header = L(s2); break;
            case ContentControl cc when cc.Content is string s3 && !Bound(cc, ContentControl.ContentProperty): cc.Content = L(s3); break;
        }
        if (node is FrameworkElement fe && fe.ToolTip is string tip && !Bound(fe, FrameworkElement.ToolTipProperty)) fe.ToolTip = L(tip);
        if (node is FrameworkContentElement fce && fce.ToolTip is string tip2) fce.ToolTip = L(tip2);
        if (node is not DependencyObject d) return;
        // TextBlock with several runs: the runs are its logical children and get translated one by one
        foreach (var child in LogicalTreeHelper.GetChildren(d).Cast<object>().ToList()) Walk(child);
        if (node is Popup { Child: { } pc }) Walk(pc);
    }

    static bool Bound(DependencyObject o, DependencyProperty p) => BindingOperations.IsDataBound(o, p);

    /// <summary>For --dump-strings: every Russian text a window shows, to check the translation table.</summary>
    public static IEnumerable<string> Collect(DependencyObject root)
    {
        var found = new List<string>();
        void Add(string? s) { if (!string.IsNullOrWhiteSpace(s) && s.Any(ch => ch is >= 'А' and <= 'я' or 'ё' or 'Ё')) found.Add(s); }
        void Go(object node)
        {
            switch (node)
            {
                case Window w: Add(w.Title); break;
                case TextBlock tb when tb.Inlines.Count <= 1: Add(tb.Text); break;
                case Run r: Add(r.Text); break;
                case HeaderedContentControl h: Add(h.Header as string); break;
                case HeaderedItemsControl hi: Add(hi.Header as string); break;
                case ContentControl cc: Add(cc.Content as string); break;
            }
            if (node is FrameworkElement fe) Add(fe.ToolTip as string);
            if (node is FrameworkContentElement fce) Add(fce.ToolTip as string);
            if (node is not DependencyObject d) return;
            foreach (var child in LogicalTreeHelper.GetChildren(d)) Go(child);
            if (node is Popup { Child: { } pc }) Go(pc);
        }
        Go(root);
        return found;
    }
}
