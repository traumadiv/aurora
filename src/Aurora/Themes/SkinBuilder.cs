using System.Windows;
using System.Windows.Media;

namespace Aurora.Themes;

/// <summary>
/// Builds the active skin from a skin name and one accent color, so every skin can be recolored from Settings.
/// Retro skins sit on the control themes WPF itself ships (Luna, Royale, Classic), so buttons, check boxes,
/// scroll bars and tabs are the real thing; the window frame and the palette are drawn here.
/// Keys are consumed through DynamicResource by Classic.xaml, Controls.Aero.xaml and the views.
/// </summary>
public static class SkinBuilder
{
    public static readonly (string Key, string Title, string Note)[] Skins =
    [
        ("xp", L("XP синяя"), L("Luna, как в 2001-м")),
        ("olive", L("XP оливковая"), L("зелёная Luna")),
        ("silver", L("XP серебристая"), L("металлик")),
        ("royale", "Royale", L("тёмно-синее стекло Media Center")),
        ("classic", L("Классика"), L("серые окна 98 / 2000")),
        ("icq", "ICQ 2003", L("XP, бежевый и цветочки")),
        ("aero", "Frutiger Aero", L("стекло, небо, пузыри")),
    ];

    /// <summary>Accent presets offered in Settings.</summary>
    public static readonly (string Name, Color Color)[] Presets =
    [
        (L("Синий"), C("#316AC5")),
        (L("Аква"), C("#1FA2E6")),
        (L("Трава"), C("#3DAA3F")),
        (L("Оливка"), C("#7A8F4A")),
        (L("Бирюза"), C("#14A89A")),
        (L("Сирень"), C("#8A5BD6")),
        (L("Розовый"), C("#E0508E")),
        (L("Закат"), C("#EE7A1B")),
        (L("Графит"), C("#5E7186")),
    ];

    public static Color DefaultAccent(string skin) => skin switch
    {
        "icq" => C("#3D9A3F"),
        "olive" => C("#7A8F4A"),
        "silver" => C("#6E7491"),
        "royale" => C("#335EA8"),
        "classic" => C("#0A246A"),
        "aero" => C("#1FA2E6"),
        _ => C("#316AC5"),
    };

    /// <summary>The system control theme under the skin: real XP / Royale / 98 buttons, check boxes and scroll bars.</summary>
    public static Uri ThemeUri(string skin) => new(skin switch
    {
        "olive" => "/PresentationFramework.Luna;component/themes/luna.homestead.xaml",
        "silver" => "/PresentationFramework.Luna;component/themes/luna.metallic.xaml",
        "royale" => "/PresentationFramework.Royale;component/themes/royale.normalcolor.xaml",
        "classic" => "/PresentationFramework.Classic;component/themes/classic.xaml",
        _ => "/PresentationFramework.Luna;component/themes/luna.normalcolor.xaml",
    }, UriKind.Relative);

    public static ResourceDictionary Build(string skin, Color accent) => skin switch
    {
        "aero" => Aero(accent),
        "icq" => Icq(accent),
        _ => Retro(skin, accent),
    };

    static readonly Color W = Colors.White;

    // ======================================================================
    // ICQ 2003: XP Luna frame stays blue, accent drives the green bits.
    // ======================================================================

    static ResourceDictionary Icq(Color a)
    {
        var d = new ResourceDictionary { Source = new Uri("Themes/Skin.Icq.xaml", UriKind.Relative) };
        d["Accent"] = B(Shade(a, -0.55));
        d["AccentSoft"] = B(Mix(a, C("#8A8A8A"), 0.5));
        d["Arrow"] = B(Shade(a, -0.35));
        d["SubText"] = B(Mix(Shade(a, -0.5), C("#777777"), 0.5));
        d["Hover"] = B(Mix(a, W, 0.9));
        d["Badge"] = B(a);
        d["BadgeBorder"] = B(Shade(a, -0.2));
        d["Typing"] = B(Shade(a, -0.2));
        d["TypingBg"] = B(Mix(a, W, 0.93));
        d["Header"] = V(W, Mix(a, W, 0.85));
        d["HeaderSide"] = H(Mix(a, W, 0.85), W);
        d["Banner"] = V(Shade(a, 0.05), Shade(a, -0.45));
        d["BannerText"] = B(Mix(a, W, 0.8));
        d["RowRadius"] = new CornerRadius(0);
        d["Sheet"] = B(W);
        d["SheetRadius"] = new CornerRadius(0);
        d["HoverBorder"] = B(Colors.Transparent);
        d["SelectedBorder"] = B(Colors.Transparent);
        d["CaptionEdge"] = B(Colors.Transparent);
        d["FrameInner"] = B(Colors.Transparent);
        d["DecorFloat"] = B(Colors.Transparent);
        d["DecorFloatVis"] = Visibility.Collapsed;
        d["CapGlyph"] = B(W);
        d["ChatFont"] = new FontFamily("Tahoma");
        d["Ink"] = B(Colors.Black);
        d["Muted"] = B(C("#777777"));
        d["Danger"] = B(C("#C0261A"));
        d["MineInk"] = B(C("#1A3FB8"));
        d["TheirsInk"] = B(C("#C0261A"));
        d["DeletedInk"] = B(C("#9A8A8A"));
        d["LinkInk"] = B(C("#0645AD"));
        d["DarkSkin"] = false;
        return d;
    }

    // ======================================================================
    // Retro Windows: XP blue / olive / silver, Royale, Classic.
    // Starts from the ICQ dictionary (same layout keys), then repaints
    // the frame, face and selection the way each theme did.
    // ======================================================================

    static ResourceDictionary Retro(string skin, Color a)
    {
        var d = Icq(a);
        d["Selected"] = B(a);
        d["Hover"] = B(Mix(a, W, 0.88));
        d["Header"] = V(W, Mix(a, W, 0.88));
        d["HeaderSide"] = H(Mix(a, W, 0.88), W);
        d["Banner"] = V(Shade(a, 0.15), Shade(a, -0.4));
        d["Typing"] = B(Shade(a, -0.15));
        d["TypingBg"] = B(Mix(a, W, 0.92));
        d["Heading"] = B(Shade(a, -0.35));
        d["SubText"] = B(Mix(Shade(a, -0.45), C("#707070"), 0.5));
        d["Accent"] = B(Shade(a, -0.4));
        d["Arrow"] = B(Shade(a, -0.2));
        d["MineInk"] = B(Shade(a, -0.25));
        d["ToolHover"] = B(Mix(a, W, 0.85));
        d["ToolHoverBorder"] = B(Mix(a, Colors.Black, 0.1));
        d["ToolPressed"] = B(Mix(a, W, 0.65));

        switch (skin)
        {
            case "olive":
                Luna(d,
                    active: [("#D6E2B4", 0), ("#A9BC84", 0.08), ("#97AC70", 0.45), ("#879D60", 0.9), ("#71874A", 1)],
                    inactive: V(C("#E6EBD2"), C("#CBD4B0")),
                    frame: "#93A56C", frameOff: "#CBD4B0", shadow: "#4C5A2E",
                    btn: H2("#B9CC93", "#83995A"), btnHover: "#C7D9A2", btnPressed: "#6E8446");
                d["Face"] = B(C("#ECE9D8"));
                break;

            case "silver":
                Luna(d,
                    active: [("#FFFFFF", 0), ("#ECEDF2", 0.1), ("#D8D9E2", 0.45), ("#C4C5D3", 0.9), ("#A9ABBE", 1)],
                    inactive: V(C("#F6F6F8"), C("#E2E2E8")),
                    frame: "#A5A7BE", frameOff: "#D8D9E2", shadow: "#FFFFFF",
                    btn: H2("#FFFFFF", "#C9CAD8"), btnHover: "#FFFFFF", btnPressed: "#B4B6C8");
                d["CaptionFore"] = B(C("#1C1C28"));
                d["CapGlyph"] = B(C("#33354A"));
                d["CapBorder"] = B(C("#8E90A8"));
                d["CapCloseBg"] = H2("#FFFFFF", "#C9CAD8");
                d["CapCloseBgHover"] = B(C("#FFFFFF"));
                d["CapCloseBgPressed"] = B(C("#B4B6C8"));
                d["Face"] = B(C("#E0DFE3"));
                d["Line"] = B(C("#A7A6AA"));
                break;

            case "royale":
                // dark glossy caption with a sheen on the top half
                d["CaptionActive"] = Gloss(C("#6C8FDC"), C("#3B66C6"), C("#1E46A8"), C("#2C57B8"));
                d["CaptionInactive"] = Gloss(C("#A9B8DA"), C("#8EA2CE"), C("#7C91C2"), C("#8699C8"));
                d["CaptionSheen"] = V(A(W, 0x60), A(W, 0x10));
                d["CaptionShadow"] = C("#0A1A50");
                d["CaptionRadius"] = new CornerRadius(7, 7, 0, 0);
                d["FrameActive"] = V(C("#2A4FAE"), C("#1C3A8A"));
                d["FrameInactive"] = B(C("#8699C8"));
                d["CapBtn"] = Gloss(C("#7E9DE0"), C("#4A73CF"), C("#2D52B4"), C("#3A60C0"));
                d["CapBtnHover"] = B(C("#86A8F0"));
                d["CapBtnPressed"] = B(C("#1C3A8A"));
                d["CapGloss"] = V(A(W, 0x70), A(W, 0x10));
                d["CapCloseBg"] = Gloss(C("#F2A088"), C("#E06A48"), C("#C8401C"), C("#D85A38"));
                d["CaptionEdge"] = B(A(W, 0x50));
                d["Face"] = B(C("#EBE9ED"));
                d["Line"] = B(C("#A7A6AA"));
                break;

            case "classic":
                // flat gradient caption, grey 3D frame and buttons, no rounding
                d["UiFont"] = new FontFamily("Tahoma");
                d["CaptionFont"] = new FontFamily("Tahoma");
                d["CaptionActive"] = H(C("#0A246A"), C("#A6CAF0"));
                d["CaptionInactive"] = H(C("#808080"), C("#C0C0C0"));
                d["CaptionSheen"] = B(Colors.Transparent);
                d["CaptionShadow"] = Colors.Transparent;
                d["CaptionShadowDepth"] = 0.0;
                d["CaptionRadius"] = new CornerRadius(0);
                d["CaptionMargin"] = new Thickness(0, 0, 0, 0);
                d["FrameThickness"] = new Thickness(4, 4, 4, 4);
                d["FrameActive"] = B(C("#D4D0C8"));
                d["FrameInactive"] = B(C("#D4D0C8"));
                d["FrameInner"] = B(C("#808080"));
                d["CapWidth"] = 21.0;
                d["CapCloseWidth"] = 21.0;
                d["CapRadius"] = new CornerRadius(0);
                d["ToolRadius"] = new CornerRadius(0);
                d["CapBorder"] = new LinearGradientBrush(W, C("#404040"), 45);
                d["CapGloss"] = B(Colors.Transparent);
                d["CapBtn"] = B(C("#D4D0C8"));
                d["CapBtnHover"] = B(C("#D4D0C8"));
                d["CapBtnPressed"] = B(C("#C0BCB4"));
                d["CapCloseBg"] = B(C("#D4D0C8"));
                d["CapCloseBgHover"] = B(C("#D4D0C8"));
                d["CapCloseBgPressed"] = B(C("#C0BCB4"));
                d["CapGlyph"] = B(Colors.Black);
                d["Face"] = B(C("#D4D0C8"));
                d["Line"] = B(C("#808080"));
                d["Frame"] = B(C("#808080"));
                d["Toolbar"] = B(C("#D4D0C8"));
                d["Header"] = B(C("#D4D0C8"));
                d["HeaderSide"] = B(C("#D4D0C8"));
                d["ToolHover"] = B(C("#D4D0C8"));
                d["ToolHoverBorder"] = new LinearGradientBrush(W, C("#404040"), 45);
                d["ToolPressed"] = B(C("#C0BCB4"));
                d["Banner"] = V(C("#0A246A"), C("#3A6EA5"));
                d["Muted"] = B(C("#606060"));
                break;

            default: // XP blue: the Luna frame from the ICQ dictionary, blue selection instead of green
                break;
        }
        if (skin != "xp") d["Toolbar"] = d["Face"];
        return d;
    }

    /// <summary>A Luna caption: five-stop gradient, solid frame, glossy square buttons, white title with a hard shadow.</summary>
    static void Luna(ResourceDictionary d, (string Hex, double At)[] active, Brush inactive, string frame, string frameOff, string shadow,
        Brush btn, string btnHover, string btnPressed)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (hex, at) in active) g.GradientStops.Add(new GradientStop(C(hex), at));
        g.Freeze();
        d["CaptionActive"] = g;
        d["CaptionInactive"] = inactive;
        d["FrameActive"] = B(C(frame));
        d["FrameInactive"] = B(C(frameOff));
        d["CaptionShadow"] = C(shadow);
        d["CapBtn"] = btn;
        d["CapBtnHover"] = B(C(btnHover));
        d["CapBtnPressed"] = B(C(btnPressed));
    }

    static LinearGradientBrush H2(string a, string b)
    {
        var br = new LinearGradientBrush(C(a), C(b), new Point(0, 0), new Point(1, 1));
        br.Freeze();
        return br;
    }

    // ======================================================================
    // Frutiger Aero
    // ======================================================================

    static ResourceDictionary Aero(Color a)
    {
        var d = new ResourceDictionary();
        d.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/Controls.Aero.xaml", UriKind.Relative) });

        var light = Mix(a, W, 0.55);
        var pale = Mix(a, W, 0.85);
        var deep = Shade(a, -0.6);
        var grass = C("#5DBE2E");

        d["UiFont"] = new FontFamily("Segoe UI");
        d["UiFontSize"] = 12.0;
        d["CaptionFont"] = new FontFamily("Segoe UI");
        d["CaptionWeight"] = FontWeights.SemiBold;
        d["ChatFont"] = new FontFamily("Segoe UI");
        d["DarkSkin"] = false;

        d["Ink"] = B(Mix(deep, C("#102030"), 0.4));
        d["Muted"] = B(C("#6A7C8E"));
        d["SubText"] = B(Mix(Shade(a, -0.35), C("#506070"), 0.4));
        d["Danger"] = B(C("#C0261A"));
        d["MineInk"] = B(Shade(a, -0.4));
        d["TheirsInk"] = B(C("#C0261A"));
        d["DeletedInk"] = B(C("#9A8A8A"));
        d["LinkInk"] = B(Shade(a, -0.3));
        d["Accent"] = B(deep);
        d["AccentSoft"] = B(Mix(Shade(a, -0.35), C("#7A8A99"), 0.5));
        d["Arrow"] = B(Shade(a, -0.1));
        d["Heading"] = B(deep);
        d["Typing"] = B(Shade(a, -0.35));
        d["TypingBg"] = B(A(pale, 0xE0));
        d["ShadowColor"] = C("#103050");

        d["CaptionActive"] = Gloss(Mix(a, W, 0.55), Mix(a, W, 0.22), Shade(a, -0.08), Shade(a, 0.08));
        d["CaptionInactive"] = Gloss(Mix(a, W, 0.85), Mix(a, W, 0.75), Mix(a, W, 0.65), Mix(a, W, 0.7));
        d["CaptionSheen"] = V(A(W, 0xB0), A(W, 0x18));
        d["CaptionFore"] = B(W);
        d["CaptionShadow"] = deep;
        d["CaptionEdge"] = B(A(W, 0xB0));
        d["FrameActive"] = V(Shade(a, -0.05), Mix(a, W, 0.35));
        d["FrameInactive"] = B(Mix(a, W, 0.7));
        d["FrameInner"] = B(A(W, 0x70));
        d["CaptionShadowDepth"] = 0.0;
        d["CaptionShadowBlur"] = 7.0;
        d["CaptionRadius"] = new CornerRadius(9, 9, 0, 0);
        d["CaptionMargin"] = new Thickness(-2, 0, -2, 0);
        d["FrameThickness"] = new Thickness(2, 0, 2, 2);

        d["CapWidth"] = 27.0;
        d["CapCloseWidth"] = 44.0;
        d["CapRadius"] = new CornerRadius(4);
        d["ToolRadius"] = new CornerRadius(10);
        d["RowRadius"] = new CornerRadius(5);
        d["CapGloss"] = V(A(W, 0xC8), A(W, 0x30));
        d["CapGlyph"] = B(W);
        d["CapBorder"] = B(A(deep, 0xC0));
        d["CapBtn"] = V(Mix(a, W, 0.45), Shade(a, -0.15));
        d["CapBtnHover"] = Glow(Mix(a, C("#8CF6FF"), 0.5), a);
        d["CapBtnPressed"] = B(Shade(a, -0.4));
        d["CapCloseBg"] = Gloss(C("#F5AE98"), C("#E3704F"), C("#CC3A1C"), C("#DB5B3C"));
        d["CapCloseBgHover"] = Glow(C("#FFC690"), C("#E0442A"));
        d["CapCloseBgPressed"] = B(C("#9E2410"));

        d["Face"] = new LinearGradientBrush(new GradientStopCollection
        {
            new(Mix(a, W, 0.9), 0), new(Mix(a, W, 0.62), 0.45), new(Mix(a, W, 0.5), 0.78),
            new(Mix(grass, W, 0.55), 0.93), new(Mix(grass, W, 0.25), 1),
        }, 90);
        d["Decor"] = AeroDecor(a, grass);
        d["DecorFloat"] = Bubbles(a);
        d["DecorFloatVis"] = Visibility.Visible;

        d["Line"] = B(A(Shade(a, -0.1), 0x70));
        d["Frame"] = B(A(Shade(a, -0.1), 0x90));
        d["Hover"] = V(A(W, 0xD0), A(pale, 0xC0));
        d["HoverBorder"] = B(A(W, 0xF0));
        d["Selected"] = Gloss(Mix(a, W, 0.4), Mix(a, W, 0.12), Shade(a, -0.08), Shade(a, 0.08));
        d["SelectedBorder"] = B(A(Shade(a, -0.3), 0xC0));
        d["Toolbar"] = V(A(W, 0xC0), A(pale, 0xA0));
        d["ToolHover"] = V(A(W, 0xF0), A(light, 0xB0));
        d["ToolHoverBorder"] = B(A(W, 0xF0));
        d["ToolPressed"] = B(A(light, 0xE0));
        d["Header"] = Gloss(A(W, 0xE8), A(W, 0xA8), A(pale, 0x90), A(pale, 0xC0));
        d["HeaderSide"] = H(A(light, 0xE0), A(W, 0xF0));
        d["PanelBack"] = V(A(W, 0xA8), A(W, 0x70));
        d["ChatBack"] = B(A(W, 0xD8));
        d["Sheet"] = V(A(W, 0xF0), A(Mix(a, W, 0.92), 0xE0));
        d["SheetRadius"] = new CornerRadius(7);
        d["ListBack"] = B(Colors.Transparent);
        d["Badge"] = Gloss(C("#B6EC7E"), C("#7ACD3D"), C("#4DAE1F"), C("#5DBE2E"));
        d["BadgeBorder"] = B(C("#3E8E1C"));
        d["BannerText"] = B(Mix(a, W, 0.85));
        d["Banner"] = new LinearGradientBrush(new GradientStopCollection
        {
            new(Mix(a, W, 0.4), 0), new(a, 0.55), new(Mix(a, grass, 0.5), 0.86), new(grass, 1),
        }, 90);

        d["GlassFill"] = Gloss(W, A(W, 0xE0), A(pale, 0xE0), A(Mix(a, W, 0.75), 0xF0));
        d["GlassFillHover"] = Gloss(W, Mix(a, W, 0.8), Mix(a, W, 0.55), Mix(a, W, 0.7));
        d["GlassFillPressed"] = V(Mix(a, W, 0.55), Mix(a, W, 0.7));
        d["GlassStroke"] = B(A(Shade(a, -0.2), 0x90));
        d["GlassStrokeHover"] = B(Shade(a, -0.05));
        d["GlassGlow"] = B(A(Mix(a, C("#7FF0FF"), 0.5), 0x70));
        d["GlassInner"] = B(A(W, 0xB0));
        d["InputBack"] = B(A(W, 0xF4));
        d["InputBackRO"] = B(A(W, 0x80));
        d["InputStroke"] = B(A(Shade(a, -0.15), 0x80));
        d["InputFocus"] = B(a);
        d["ThumbFill"] = H(Mix(a, W, 0.55), Mix(a, W, 0.25));
        d["ThumbHover"] = H(Mix(a, W, 0.35), a);
        d["TrackFill"] = B(A(W, 0x50));
        d["TabFill"] = Gloss(A(W, 0xD8), A(W, 0x90), A(pale, 0x80), A(pale, 0xB0));
        d["TabSelected"] = V(W, A(W, 0xD0));
        d["TabStroke"] = B(A(Shade(a, -0.1), 0x90));
        d["CheckMark"] = B(Shade(a, -0.25));
        d["MenuBack"] = V(A(W, 0xF4), A(pale, 0xF0));
        return d;
    }

    // ---------- Aero scenery: canvas 300x600, painted bottom-right aligned ----------

    static DrawingBrush Canvas(DrawingGroup g)
    {
        g.Children.Insert(0, new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 300, 600))));
        g.Freeze();
        var brush = new DrawingBrush(g) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right, AlignmentY = AlignmentY.Bottom };
        brush.Freeze();
        return brush;
    }

    static void Add(DrawingGroup g, Brush? fill, Geometry geo, Pen? pen = null) => g.Children.Add(new GeometryDrawing(fill, pen, geo));
    static void Add(DrawingGroup g, Brush? fill, string path, Pen? pen = null) => Add(g, fill, Geometry.Parse(path), pen);
    static EllipseGeometry Dot(double x, double y, double r) => new(new Point(x, y), r, r);
    static RadialGradientBrush Soft(Color c, byte alpha) => new(A(c, alpha), A(c, 0));

    static void Ribbon(DrawingGroup g, string path, Color c, byte alpha)
    {
        var fill = new LinearGradientBrush(new GradientStopCollection
        {
            new(A(c, 0), 0), new(A(c, alpha), 0.3), new(A(Mix(c, W, 0.45), alpha), 0.55), new(A(c, (byte)(alpha * 0.7)), 0.75), new(A(c, 0), 1),
        }, 0);
        Add(g, fill, path);
    }

    /// <summary>Four-point sparkle, the little glints all over Aero wallpapers.</summary>
    static void Sparkle(DrawingGroup g, double x, double y, double r, byte alpha)
    {
        var s = r * 0.18;
        Add(g, B(A(W, alpha)),
            FormattableString.Invariant($"M{x},{y - r} L{x + s},{y - s} L{x + r},{y} L{x + s},{y + s} L{x},{y + r} L{x - s},{y + s} L{x - r},{y} L{x - s},{y - s} Z"));
        Add(g, Soft(W, (byte)(alpha / 2)), Dot(x, y, r * 0.7));
    }

    /// <summary>A soap bubble: clear body tinted by the sky, bright rim, window highlight, rainbow sheen.</summary>
    static void Bubble(DrawingGroup g, double x, double y, double r, Color tint)
    {
        var body = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.35, 0.3),
            GradientStops =
            {
                new GradientStop(A(W, 0x20), 0),
                new GradientStop(A(Mix(tint, W, 0.5), 0x30), 0.65),
                new GradientStop(A(Mix(tint, C("#C8FFF0"), 0.5), 0x70), 0.9),
                new GradientStop(A(W, 0xE0), 1),
            },
        };
        Add(g, body, Dot(x, y, r), new Pen(B(A(W, 0xE8)), Math.Max(0.8, r * 0.05)));
        Add(g, new LinearGradientBrush(A(W, 0xF0), A(W, 0x40), 90),
            new EllipseGeometry(new Point(x - r * 0.33, y - r * 0.45), r * 0.38, r * 0.2) { Transform = new RotateTransform(-28, x - r * 0.33, y - r * 0.45) });
        Add(g, B(A(W, 0x90)), Dot(x + r * 0.45, y + r * 0.42, r * 0.09));
        Add(g, null, new EllipseGeometry(new Point(x, y), r * 0.86, r * 0.86),
            new Pen(new LinearGradientBrush(new GradientStopCollection
            {
                new(A(C("#FFB0E0"), 0), 0), new(A(C("#FFB0E0"), 0x50), 0.6), new(A(C("#A0FFE0"), 0x60), 0.8), new(A(C("#A0D8FF"), 0), 1),
            }, 90), Math.Max(0.6, r * 0.06)));
    }

    static DrawingBrush AeroDecor(Color a, Color grass)
    {
        var g = new DrawingGroup();
        // sun and lens flare
        Add(g, Soft(W, 0xC0), new EllipseGeometry(new Point(250, 30), 200, 150));
        Add(g, Soft(Mix(a, C("#FFF6C8"), 0.6), 0x70), Dot(250, 30, 70));
        foreach (var (x, y, r, al) in new[] { (205.0, 85.0, 9.0, 0x40), (178.0, 118.0, 5.0, 0x50), (140.0, 165.0, 15.0, 0x28), (118.0, 192.0, 4.0, 0x50) })
            Add(g, B(A(Mix(a, W, 0.6), (byte)al)), Dot(x, y, r));

        Ribbon(g, "M-40,130 C40,60 120,150 200,80 C250,40 300,70 340,40 L340,92 C290,122 250,96 200,136 C120,202 40,112 -40,182 Z",
            Mix(a, C("#3EF0C8"), 0.5), 0x58);
        Ribbon(g, "M-40,178 C50,124 140,198 220,132 C270,97 310,117 340,97 L340,121 C300,141 266,126 220,162 C140,226 60,152 -40,212 Z",
            C("#8CF07A"), 0x40);

        Add(g, H3(A(W, 0), A(W, 0xA0), A(W, 0)), "M-40,430 C80,330 200,370 340,250 L340,280 C210,394 90,366 -40,464 Z");
        Add(g, H3(A(a, 0), A(Mix(a, W, 0.35), 0xA0), A(a, 0)), "M-40,480 C100,406 215,430 340,324 L340,339 C220,443 100,422 -40,495 Z");
        Add(g, H3(A(W, 0), A(W, 0x60), A(W, 0)), "M-40,505 C110,440 230,460 340,372 L340,378 C230,468 110,448 -40,511 Z");

        foreach (var (x, y, r) in new[] { (60.0, 260.0, 48.0), (250.0, 330.0, 40.0), (150.0, 470.0, 32.0), (30.0, 140.0, 26.0) })
            Add(g, Soft(W, 0x48), Dot(x, y, r));

        foreach (var (x, y, r, al) in new[] { (225.0, 60.0, 9.0, 0xF0), (60.0, 95.0, 5.0, 0xC0), (282.0, 215.0, 6.0, 0xD0), (110.0, 340.0, 4.0, 0xB0), (200.0, 300.0, 3.5, 0xB0) })
            Sparkle(g, x, y, r, (byte)al);

        // hills with a glossy rim and grass blades
        Add(g, new LinearGradientBrush(A(Mix(grass, W, 0.25), 0xD0), A(Shade(grass, -0.3), 0xF0), 90),
            "M-20,600 L-20,552 C60,520 150,538 220,526 C262,520 292,533 320,542 L320,600 Z");
        Add(g, new LinearGradientBrush(A(Mix(grass, W, 0.5), 0xC0), A(grass, 0xF0), 90),
            "M-20,600 L-20,574 C70,556 170,570 320,560 L320,600 Z");
        Add(g, new LinearGradientBrush(A(W, 0xA0), A(W, 0), 90),
            "M-20,558 C60,526 150,544 220,532 C262,526 292,538 320,547 L320,554 C292,546 262,534 220,540 C150,552 60,534 -20,566 Z");
        var blade = new Pen(B(A(Mix(grass, W, 0.35), 0xC0)), 1.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (var i = 0; i < 26; i++)
        {
            var x = 4 + i * 12.0 + (i % 3) * 3;
            var y = 556 - Math.Sin(i * 0.55) * 8 + (i > 18 ? 8 : 0);
            Add(g, null, FormattableString.Invariant($"M{x},{y + 8} Q{x + 1},{y + 2} {x + 4 - i % 2 * 6},{y - 4}"), blade);
        }
        return Canvas(g);
    }

    static DrawingBrush Bubbles(Color a)
    {
        var g = new DrawingGroup();
        foreach (var (x, y, r) in new[]
        {
            (248.0, 165.0, 30.0), (196.0, 232.0, 13.0), (270.0, 262.0, 9.0), (36.0, 315.0, 21.0), (78.0, 362.0, 8.0),
            (232.0, 395.0, 17.0), (118.0, 430.0, 11.0), (276.0, 462.0, 25.0), (28.0, 492.0, 10.0), (182.0, 512.0, 7.0),
            (140.0, 300.0, 5.0), (300.0, 210.0, 6.0), (90.0, 220.0, 7.0), (160.0, 120.0, 4.0), (20.0, 400.0, 5.0),
        })
            Bubble(g, x, y, r, a);
        return Canvas(g);
    }

    // ---------- color math ----------

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static Color A(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Positive amount lightens toward white, negative darkens toward black.</summary>
    static Color Shade(Color c, double amount) => amount >= 0 ? Mix(c, W, amount) : Mix(c, Colors.Black, -amount);

    static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    static LinearGradientBrush V(Color top, Color bottom) { var b = new LinearGradientBrush(top, bottom, 90); b.Freeze(); return b; }
    static LinearGradientBrush H(Color left, Color right) { var b = new LinearGradientBrush(left, right, 0); b.Freeze(); return b; }

    static LinearGradientBrush H3(Color a, Color b, Color c)
    {
        var br = new LinearGradientBrush(new GradientStopCollection { new(a, 0), new(b, 0.5), new(c, 1) }, 0);
        br.Freeze();
        return br;
    }

    /// <summary>The Aero gloss: two halves with a hard edge in the middle.</summary>
    static LinearGradientBrush Gloss(Color top, Color mid, Color midLow, Color bottom)
    {
        var b = new LinearGradientBrush(new GradientStopCollection { new(top, 0), new(mid, 0.49), new(midLow, 0.51), new(bottom, 1) }, 90);
        b.Freeze();
        return b;
    }

    /// <summary>Hover light that rises from the bottom edge.</summary>
    static RadialGradientBrush Glow(Color inner, Color outer)
    {
        var b = new RadialGradientBrush(inner, outer) { GradientOrigin = new Point(0.5, 1), Center = new Point(0.5, 1), RadiusX = 0.9, RadiusY = 1 };
        b.Freeze();
        return b;
    }
}
