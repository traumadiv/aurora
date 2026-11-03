using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Aurora.Services;

/// <summary>
/// Turns emoji and text smileys (":)", ";-)") into little pictures, the way old IM clients did.
///
/// The built-in set is drawn in code. A smiley pack in %APPDATA%\Aurora\smileys overrides it:
/// put the images (animated GIFs work) next to a smileys.txt with lines like
///     smile.gif   🙂 :) :-)
/// The first code after the file name is what the picker inserts, so make it an emoji:
/// other Telegram apps will see that.
/// </summary>
public static class Smileys
{
    public sealed record Smiley(string Key, string Insert, ImageSource Still, AnimatedFrames? Anim);

    public sealed record AnimatedFrames(BitmapSource[] Frames, TimeSpan[] Delays);

    static readonly Dictionary<string, Smiley> ByCode = new();
    static readonly List<Smiley> Ordered = new();
    static string[] _codes = [];
    static HashSet<char> _first = new();

    public static IReadOnlyList<Smiley> All => Ordered;
    public static bool Enabled => App.Cfg?.UseSmileys ?? true;
    public static string PackDir => Path.Combine(Config.Dir, "smileys");

    public static void Init()
    {
        ByCode.Clear();
        Ordered.Clear();
        if (!LoadPack()) LoadBuiltIn();
        // Longest first so ":-)" wins over ":)" and "😂" is not split.
        _codes = ByCode.Keys.OrderByDescending(c => c.Length).ToArray();
        _first = _codes.Select(c => c[0]).ToHashSet();
    }

    // ---------- tokenizing ----------

    /// <summary>Splits text into plain pieces and smileys.</summary>
    public static IEnumerable<(string Text, Smiley? Smiley)> Split(string s)
    {
        if (!Enabled || string.IsNullOrEmpty(s) || _codes.Length == 0) { yield return (s, null); yield break; }
        var start = 0;
        var i = 0;
        while (i < s.Length)
        {
            if (_first.Contains(s[i]) && Match(s, i) is { } code)
            {
                if (i > start) yield return (s[start..i], null);
                yield return (code, ByCode[code]);
                i += code.Length;
                if (i < s.Length && s[i] == '️') i++; // emoji variation selector
                start = i;
                continue;
            }
            i++;
        }
        if (start < s.Length) yield return (s[start..], null);
    }

    static string? Match(string s, int i)
    {
        foreach (var code in _codes)
        {
            if (string.CompareOrdinal(s, i, code, 0, code.Length) != 0) continue;
            if (IsAsciiCode(code))
            {
                // ":/" inside "https://" or "8)" inside "(page 8)" are not smileys.
                var before = i == 0 || char.IsWhiteSpace(s[i - 1]);
                var end = i + code.Length;
                var after = end >= s.Length || char.IsWhiteSpace(s[end]) || ".,!?".Contains(s[end]);
                if (!before || !after) continue;
            }
            return code;
        }
        return null;
    }

    static bool IsAsciiCode(string code) => code.All(ch => ch < 128);

    /// <summary>An inline picture for a FlowDocument / TextBlock.</summary>
    public static Inline Make(Smiley sm, double size, string code)
    {
        var img = new Image { Width = size, Height = size, Source = sm.Still, ToolTip = code, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        if (sm.Anim != null) Animate(img, sm.Anim);
        return new InlineUIContainer(img) { BaselineAlignment = BaselineAlignment.Center };
    }

    public static Image MakeImage(Smiley sm, double size)
    {
        var img = new Image { Width = size, Height = size, Source = sm.Still, Stretch = Stretch.Uniform };
        if (sm.Anim != null) Animate(img, sm.Anim);
        return img;
    }

    static void Animate(Image img, AnimatedFrames a)
    {
        var anim = new ObjectAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        var t = TimeSpan.Zero;
        for (var f = 0; f < a.Frames.Length; f++)
        {
            anim.KeyFrames.Add(new DiscreteObjectKeyFrame(a.Frames[f], KeyTime.FromTimeSpan(t)));
            t += a.Delays[f];
        }
        anim.Duration = t;
        img.BeginAnimation(Image.SourceProperty, anim);
    }

    // ---------- TextBlock support: <TextBlock s:Smileys.Text="{Binding Preview}" /> ----------

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Smileys), new PropertyMetadata(null, (d, e) =>
        {
            if (d is not TextBlock tb) return;
            tb.Inlines.Clear();
            var size = tb.FontSize + 3;
            foreach (var (text, sm) in Split(e.NewValue as string ?? ""))
                tb.Inlines.Add(sm == null ? new Run(text) : Make(sm, size, text));
        }));

    public static void SetText(DependencyObject d, string v) => d.SetValue(TextProperty, v);
    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);

    // ---------- user pack ----------

    static bool LoadPack()
    {
        var map = Path.Combine(PackDir, "smileys.txt");
        if (!File.Exists(map)) return false;
        foreach (var raw in File.ReadAllLines(map))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var file = Path.Combine(PackDir, parts[0]);
            if (!File.Exists(file)) continue;
            try
            {
                var (still, anim) = LoadImage(file);
                Register(new Smiley(parts[0], parts[1], still, anim), parts[1..]);
            }
            catch { /* skip broken images */ }
        }
        return Ordered.Count > 0;
    }

    static (BitmapSource still, AnimatedFrames? anim) LoadImage(string file)
    {
        using var fs = File.OpenRead(file);
        var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (dec is not GifBitmapDecoder || dec.Frames.Count < 2)
        {
            var f = dec.Frames[0];
            f.Freeze();
            return (f, null);
        }
        var anim = ComposeGif(dec);
        return (anim.Frames[0], anim);
    }

    /// <summary>GIF frames are deltas; compose them into full pictures honoring offsets and disposal.</summary>
    static AnimatedFrames ComposeGif(BitmapDecoder dec)
    {
        int W = 0, H = 0;
        try
        {
            if (dec.Metadata is BitmapMetadata m)
            {
                W = Convert.ToInt32(m.GetQuery("/logscrdesc/Width"));
                H = Convert.ToInt32(m.GetQuery("/logscrdesc/Height"));
            }
        }
        catch { }
        if (W == 0) { W = dec.Frames[0].PixelWidth; H = dec.Frames[0].PixelHeight; }

        var frames = new List<BitmapSource>();
        var delays = new List<TimeSpan>();
        BitmapSource? canvas = null;
        foreach (var f in dec.Frames)
        {
            var md = (BitmapMetadata)f.Metadata;
            int Q(string q, int def) { try { return Convert.ToInt32(md.GetQuery(q)); } catch { return def; } }
            var left = Q("/imgdesc/Left", 0);
            var top = Q("/imgdesc/Top", 0);
            var delay = Q("/grctlext/Delay", 10);
            var disposal = Q("/grctlext/Disposal", 0);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                if (canvas != null) dc.DrawImage(canvas, new Rect(0, 0, W, H));
                dc.DrawImage(f, new Rect(left, top, f.PixelWidth, f.PixelHeight));
            }
            var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            frames.Add(rtb);
            delays.Add(TimeSpan.FromMilliseconds(Math.Max(delay, 2) * 10));

            canvas = disposal switch
            {
                2 => ClearRect(rtb, left, top, f.PixelWidth, f.PixelHeight, W, H), // restore to background
                3 => canvas,                                                      // restore to previous
                _ => rtb,
            };
        }
        return new AnimatedFrames(frames.ToArray(), delays.ToArray());
    }

    static BitmapSource ClearRect(BitmapSource src, int x, int y, int w, int h, int W, int H)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0, 0, W, H)), new RectangleGeometry(new Rect(x, y, w, h))));
            dc.DrawImage(src, new Rect(0, 0, W, H));
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    static void Register(Smiley s, IEnumerable<string> codes)
    {
        Ordered.Add(s);
        foreach (var c in codes)
        {
            var code = c.Replace("️", "");
            if (code.Length > 0) ByCode.TryAdd(code, s);
        }
    }

    // ---------- built-in set, drawn on a 16x16 grid ----------

    static void LoadBuiltIn()
    {
        void Add(string key, Func<DrawingGroup> draw, params string[] codes)
        {
            var g = draw();
            g.Freeze();
            var img = new DrawingImage(g);
            img.Freeze();
            Register(new Smiley(key, codes[0], img, null), codes);
        }

        Add("smile", () => Face(Eyes(), Smile()), "🙂", ":)", ":-)", "=)");
        Add("grin", () => Face(Eyes(), Grin()), "😄", "😀", "😃", "😁", ":D", ":-D");
        Add("laugh", () => Face(Closed(), Grin(), Tears()), "😂", "🤣", "xD", "XD");
        Add("wink", () => Face(Wink(), Smile()), "😉", ";)", ";-)");
        Add("blush", () => Face(Closed(), Smile(), Cheeks()), "😊", "☺", "^^", "^_^");
        Add("love", () => Face(HeartEyes(), Grin()), "😍", "🥰");
        Add("kiss", () => Face(Closed(), KissMouth(), SmallHeart()), "😘", "😗", ":*", ":-*");
        Add("tongue", () => Face(Eyes(), Smile(), Tongue()), "😛", "😜", "😝", ":P", ":p", ":-P", ":-p");
        Add("cool", () => Face(Shades(), Smile()), "😎", "8)", "8-)", "B)");
        Add("surprised", () => Face(BigEyes(), OMouth()), "😮", "😲", "😯", ":O", ":o", ":-O", ":-o");
        Add("neutral", () => Face(Eyes(), Line()), "😐", "😑", ":|", ":-|");
        Add("confused", () => Face(Eyes(), Wavy()), "😕", "🫤", ":/", ":-/");
        Add("thinking", () => Face(Eyes(raised: true), Hmm()), "🤔");
        Add("sad", () => Face(Eyes(), Frown()), "🙁", "😞", "😔", "☹", ":(", ":-(");
        Add("cry", () => Face(Eyes(), Frown(), Tear()), "😢", "😭", "😿", ":'(");
        Add("angry", () => Face(Eyes(), Frown(), Brows(), true), "😠", "😡", "🤬", ">:(");
        Add("heart", Heart, "❤", "♥", "💖", "💕", "💗", "😻", "<3");
        Add("thumbsup", () => Thumb(false), "👍", "(y)");
        Add("thumbsdown", () => Thumb(true), "👎", "(n)");
        Add("rose", Rose, "🌹", "🌷", "@}->--");
        Add("beer", Beer, "🍺", "🍻", "(b)");
        Add("fire", Fire, "🔥");
    }

    static Brush B(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }
    static Pen P(string hex, double w = 0.9) => new(B(hex), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
    static Geometry G(string d) => Geometry.Parse(d);
    static GeometryDrawing D(Brush? fill, Pen? pen, Geometry g) => new(fill, pen, g);

    const string Ink = "#3A2400";
    const string Edge = "#A86E00";

    static DrawingGroup Face(params object[] parts)
    {
        var red = parts.OfType<bool>().FirstOrDefault();
        var fill = new RadialGradientBrush(
            (Color)ColorConverter.ConvertFromString(red ? "#FFC2A8" : "#FFF7B8"),
            (Color)ColorConverter.ConvertFromString(red ? "#E8402E" : "#F7C21B"))
        { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.4, 0.35), RadiusX = 0.75, RadiusY = 0.75 };
        fill.Freeze();
        var g = new DrawingGroup();
        g.Children.Add(D(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        g.Children.Add(D(fill, P(red ? "#9E1A14" : Edge, 0.8), new EllipseGeometry(new Point(8, 8.2), 7.2, 7.2)));
        // Glossy highlight, very 2003.
        g.Children.Add(D(B("#70FFFFFF"), null, new EllipseGeometry(new Point(6, 4.2), 3, 1.6)));
        foreach (var p in parts.OfType<Drawing[]>()) foreach (var d in p) g.Children.Add(d);
        return g;
    }

    static Drawing[] Eyes(bool raised = false) =>
    [
        D(B(Ink), null, new EllipseGeometry(new Point(5.7, 6.6), 0.95, 1.35)),
        D(B(Ink), null, new EllipseGeometry(new Point(10.3, 6.6), 0.95, 1.35)),
        .. (raised ? new Drawing[] { D(null, P(Ink, 0.8), G("M9.2,4.2 Q10.4,3.4 11.6,4.2")) } : Array.Empty<Drawing>()),
    ];

    static Drawing[] BigEyes() =>
    [
        D(B("White"), P(Ink, 0.6), new EllipseGeometry(new Point(5.7, 6.4), 1.5, 1.8)),
        D(B("White"), P(Ink, 0.6), new EllipseGeometry(new Point(10.3, 6.4), 1.5, 1.8)),
        D(B(Ink), null, new EllipseGeometry(new Point(5.7, 6.6), 0.7, 0.9)),
        D(B(Ink), null, new EllipseGeometry(new Point(10.3, 6.6), 0.7, 0.9)),
    ];

    static Drawing[] Closed() =>
    [
        D(null, P(Ink, 1), G("M4.5,7 Q5.7,5.4 6.9,7")),
        D(null, P(Ink, 1), G("M9.1,7 Q10.3,5.4 11.5,7")),
    ];

    static Drawing[] Wink() =>
    [
        D(B(Ink), null, new EllipseGeometry(new Point(5.7, 6.6), 0.95, 1.35)),
        D(null, P(Ink, 1), G("M9.1,6.8 Q10.3,6 11.5,6.8")),
    ];

    static Drawing[] HeartEyes() =>
    [
        D(B("#E2283A"), null, G("M5.7,8.4 C3.6,6.9 3.7,5.2 4.7,4.9 C5.3,4.7 5.6,5.1 5.7,5.5 C5.8,5.1 6.1,4.7 6.7,4.9 C7.7,5.2 7.8,6.9 5.7,8.4Z")),
        D(B("#E2283A"), null, G("M10.3,8.4 C8.2,6.9 8.3,5.2 9.3,4.9 C9.9,4.7 10.2,5.1 10.3,5.5 C10.4,5.1 10.7,4.7 11.3,4.9 C12.3,5.2 12.4,6.9 10.3,8.4Z")),
    ];

    static Drawing[] Shades() =>
    [
        D(B("#15151A"), null, G("M3.2,5.4 L7.4,5.4 L7,7.9 Q5.3,8.9 3.7,7.9 Z")),
        D(B("#15151A"), null, G("M8.6,5.4 L12.8,5.4 L12.3,7.9 Q10.7,8.9 9,7.9 Z")),
        D(null, P("#15151A", 0.8), G("M7.3,5.7 L8.7,5.7")),
        D(B("#80FFFFFF"), null, G("M4,5.9 L5.2,5.9 L4.4,7.2 Z")),
    ];

    static Drawing[] Brows() =>
    [
        D(null, P(Ink, 1.1), G("M4.2,4.6 L7,5.8")),
        D(null, P(Ink, 1.1), G("M11.8,4.6 L9,5.8")),
    ];

    static Drawing[] Smile() => [D(null, P(Ink, 1), G("M4.8,10 Q8,13.2 11.2,10"))];
    static Drawing[] Frown() => [D(null, P(Ink, 1), G("M5,12.4 Q8,9.6 11,12.4"))];
    static Drawing[] Line() => [D(null, P(Ink, 1), G("M5.4,11 L10.6,11"))];
    static Drawing[] Wavy() => [D(null, P(Ink, 1), G("M5,11.4 Q6.5,10.2 8,11.2 Q9.5,12.2 11,10.8"))];
    static Drawing[] Hmm() => [D(null, P(Ink, 1), G("M6.4,11.4 L10.4,10.6"))];
    static Drawing[] OMouth() => [D(B(Ink), null, new EllipseGeometry(new Point(8, 11.3), 1.4, 1.8))];
    static Drawing[] KissMouth() => [D(null, P(Ink, 0.9), G("M7.4,9.8 Q9,10.5 7.6,11.2 Q9,11.9 7.4,12.6"))];

    static Drawing[] Grin() =>
    [
        D(B("#5A1A00"), P(Ink, 0.8), G("M4.3,9.4 Q8,15 11.7,9.4 Z")),
        D(B("White"), null, G("M5,9.7 L11,9.7 Q10.6,10.8 8,10.9 Q5.4,10.8 5,9.7 Z")),
        D(B("#F06A7A"), null, G("M6.4,12.6 Q8,11.6 9.6,12.6 Q8,13.6 6.4,12.6 Z")),
    ];

    static Drawing[] Tongue() => [D(B("#EC5A72"), P("#A8283E", 0.5), G("M7,10.8 Q7.6,14.2 9.4,13.4 Q10.2,12.6 9.6,11"))];

    static Drawing[] Tears() =>
    [
        D(B("#5AB0FF"), P("#2672C8", 0.4), G("M2.4,7.6 Q1.2,9.8 2.4,10.6 Q3.6,9.8 2.4,7.6 Z")),
        D(B("#5AB0FF"), P("#2672C8", 0.4), G("M13.6,7.6 Q12.4,9.8 13.6,10.6 Q14.8,9.8 13.6,7.6 Z")),
    ];

    static Drawing[] Tear() => [D(B("#5AB0FF"), P("#2672C8", 0.4), G("M5.7,8.4 Q4.5,10.8 5.7,11.6 Q6.9,10.8 5.7,8.4 Z"))];

    static Drawing[] Cheeks() =>
    [
        D(B("#70FF6E86"), null, new EllipseGeometry(new Point(3.9, 9.4), 1.5, 0.9)),
        D(B("#70FF6E86"), null, new EllipseGeometry(new Point(12.1, 9.4), 1.5, 0.9)),
    ];

    static Drawing[] SmallHeart() => [D(B("#E2283A"), null, G("M13.2,13.6 C11.2,12.2 11.3,10.6 12.3,10.3 C12.8,10.2 13.1,10.5 13.2,10.9 C13.3,10.5 13.6,10.2 14.1,10.3 C15.1,10.6 15.2,12.2 13.2,13.6Z"))];

    static DrawingGroup Frame(params Drawing[] parts)
    {
        var g = new DrawingGroup();
        g.Children.Add(D(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        foreach (var p in parts) g.Children.Add(p);
        return g;
    }

    static DrawingGroup Heart()
    {
        var fill = new LinearGradientBrush(Color.FromRgb(0xFF, 0x6B, 0x7E), Color.FromRgb(0xC8, 0x10, 0x2A), 90);
        fill.Freeze();
        return Frame(
            D(fill, P("#8E0A1C", 0.8), G("M8,14.4 C1.8,10 1.2,6.2 3,4.2 C4.8,2.4 7.2,3.2 8,5.2 C8.8,3.2 11.2,2.4 13,4.2 C14.8,6.2 14.2,10 8,14.4Z")),
            D(B("#90FFFFFF"), null, new EllipseGeometry(new Point(5, 5.6), 1.4, 0.9)));
    }

    static DrawingGroup Thumb(bool down)
    {
        var g = Frame(
            D(B("#3B78D8"), P("#1D4A94", 0.7), G("M1.8,7.4 L4.6,7.4 L4.6,14.4 L1.8,14.4 Z")),
            D(B("#FFD84A"), P(Edge, 0.8), G("M4.6,8 L7.2,3.8 Q8.2,2 9.2,3.2 Q9.8,4.2 9,6.4 L12.8,6.4 Q14.4,6.6 13.9,8.2 L12.9,13.2 Q12.5,14.4 11.2,14.4 L4.6,14.4 Z")),
            D(null, P(Edge, 0.6), G("M9.6,9 L13.4,9 M9.6,11.4 L13.2,11.4")));
        if (down) g.Transform = new RotateTransform(180, 8, 8);
        return g;
    }

    static DrawingGroup Rose() => Frame(
        D(null, P("#2E7D32", 1.1), G("M8,8 Q7.2,11.6 8.4,15")),
        D(B("#4CAF50"), P("#2E7D32", 0.5), G("M7.8,11.4 Q4.6,10 4,12.4 Q6.4,13.4 7.8,11.4 Z")),
        D(B("#E5243B"), P("#8E0A1C", 0.7), new EllipseGeometry(new Point(8, 5.6), 4, 3.8)),
        D(null, P("#8E0A1C", 0.7), G("M6,5.4 Q8,3 10,5.4 Q8,7.6 6.6,6.2")),
        D(B("#70FFFFFF"), null, new EllipseGeometry(new Point(6.4, 3.9), 1.2, 0.7)));

    static DrawingGroup Beer() => Frame(
        D(null, P("#B5651D", 1.3), G("M11.6,6.6 Q14.6,6.6 14.4,9.6 Q14.2,12 11.6,12")),
        D(B("#F5B024"), P("#B5651D", 0.8), G("M3,4.6 L11.8,4.6 L11.4,14.6 L3.4,14.6 Z")),
        D(B("White"), P("#C8C8C8", 0.5), G("M2.4,5.6 Q2.2,2.2 5,3 Q6.4,1.2 8.4,2.6 Q11.2,1.6 12.2,4.4 Q12.6,6 11,5.8 L3.6,5.8 Q2.6,6.2 2.4,5.6 Z")),
        D(null, P("#FFE08A", 0.6), G("M5.4,7.4 L5.4,13 M8,7.4 L8,13")));

    static DrawingGroup Fire() => Frame(
        D(B("#FF7A1A"), P("#C2410C", 0.7), G("M8,1.4 Q11.4,5 12.4,8 Q13.4,12.4 8,14.8 Q2.6,12.4 3.6,8.4 Q4.2,6.2 6,4.8 Q5.8,7.2 7.2,7.8 Q6.8,4.4 8,1.4 Z")),
        D(B("#FFE14A"), null, G("M8,7.6 Q10.4,10 10,11.8 Q9.4,14 8,14 Q6.4,13.8 6,12 Q5.8,10.2 8,7.6 Z")));
}
