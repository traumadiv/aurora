using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aurora.Services;

/// <summary>All glyphs are drawn in code on a 16x16 grid, no bitmap assets.</summary>
public static class Icons
{
    static readonly Dictionary<string, DrawingImage> _cache = new();

    /// <summary>"icq" draws status flowers, the glass skins draw orbs. Set by App.ApplySkin.</summary>
    public static string Skin { get; set; } = "icq";

    public static DrawingImage Get(string key) => GetFor(Skin, key);

    /// <summary>The icon as a given skin draws it, e.g. for skin previews in Settings.</summary>
    public static DrawingImage GetFor(string skin, string key)
    {
        var ck = skin + ":" + key;
        if (_cache.TryGetValue(ck, out var img)) return img;
        var d = skin != "icq" ? AeroIcon(key) : null;
        d ??= key switch
        {
            "online" => Flower("#38B03A", "#1E7A20", "#F4D23B"),
            "away" => Flower("#F2BE1A", "#B4860A", "#FFF3B0"),
            "offline" => Flower("#E2392F", "#9E1A14", "#F7B0A8"),
            "invisible" => Flower("#C9CED6", "#8A93A0", "#FFFFFF"),
            "group" => Group(),
            "channel" => Channel(),
            "bot" => Bot(),
            "saved" => Saved(),
            "msg" => Envelope(),
            _ => Flower("#999", "#666", "#DDD"),
        };
        d.Freeze();
        img = new DrawingImage(d);
        img.Freeze();
        return _cache[ck] = img;
    }

    // ---------- Aero skin: glossy glass orbs ----------

    static DrawingGroup? AeroIcon(string key) => key switch
    {
        "online" => Orb(8, 8, 6.8, "#C6F58A", "#4FB81F", "#2E7D12"),
        "away" => Orb(8, 8, 6.8, "#FFE79A", "#F2A516", "#B26A06"),
        "offline" => Orb(8, 8, 6.8, "#E4ECF3", "#9FB2C4", "#6A7F93"),
        "invisible" => Orb(8, 8, 6.8, "#FFFFFF", "#D3E3EE", "#9BB4C6", alpha: 0x90),
        "group" => Frame(
            Orb(10.6, 6.2, 4.6, "#B9EBFF", "#3AA7E6", "#1C6FB0").Children.ToArray()
                .Concat(Orb(5.8, 10, 5.4, "#C6F58A", "#4FB81F", "#2E7D12").Children).ToArray()),
        "msg" => GlassEnvelope(),
        _ => null,
    };

    /// <summary>A sphere: radial body, darker rim, a crisp highlight on top and a soft bounce light below.</summary>
    static DrawingGroup Orb(double cx, double cy, double r, string light, string mid, string rim, byte alpha = 0xFF)
    {
        Color C(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            c.A = alpha;
            return c;
        }

        var body = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.4, 0.3), Center = new Point(0.45, 0.4), RadiusX = 0.7, RadiusY = 0.7,
            GradientStops = { new GradientStop(C(light), 0), new GradientStop(C(mid), 0.65), new GradientStop(C(rim), 1) },
        };
        body.Freeze();
        var gloss = new LinearGradientBrush(Color.FromArgb(0xF0, 255, 255, 255), Color.FromArgb(0x10, 255, 255, 255), 90);
        gloss.Freeze();
        var bounce = new RadialGradientBrush(Color.FromArgb(0x90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255));
        bounce.Freeze();

        return Frame(
            new GeometryDrawing(body, new Pen(new SolidColorBrush(C(rim)), 0.5), new EllipseGeometry(new Point(cx, cy), r, r)),
            new GeometryDrawing(gloss, null, new EllipseGeometry(new Point(cx, cy - r * 0.45), r * 0.68, r * 0.42)),
            new GeometryDrawing(bounce, null, new EllipseGeometry(new Point(cx, cy + r * 0.55), r * 0.55, r * 0.3)));
    }

    static DrawingGroup GlassEnvelope()
    {
        var body = new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xA9, 0xDD, 0xF7), 90);
        body.Freeze();
        var gloss = new LinearGradientBrush(Color.FromArgb(0xC0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90);
        gloss.Freeze();
        return Frame(
            new GeometryDrawing(body, P("#2A8BD0"), new RectangleGeometry(new Rect(1.5, 3.5, 13, 9), 1.5, 1.5)),
            new GeometryDrawing(null, P("#2A8BD0"), Geometry.Parse("M1.8,4 L8,9 L14.2,4")),
            new GeometryDrawing(gloss, null, new RectangleGeometry(new Rect(2.2, 4, 11.6, 3.6), 1, 1)));
    }

    static Brush B(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    static Pen P(string hex, double w = 0.8) => new(B(hex), w);

    static DrawingGroup Frame(params Drawing[] parts)
    {
        var g = new DrawingGroup();
        // Invisible 16x16 rect keeps every icon on the same box regardless of its shape.
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        foreach (var p in parts) g.Children.Add(p);
        return g;
    }

    static DrawingGroup Flower(string petal, string edge, string center)
    {
        var parts = new List<Drawing>();
        for (var i = 0; i < 7; i++)
        {
            var a = i * (360.0 / 7) - 90;
            var rad = a * Math.PI / 180;
            var c = new Point(8 + Math.Cos(rad) * 4.3, 8 + Math.Sin(rad) * 4.3);
            var e = new EllipseGeometry(c, 2.6, 2.6);
            parts.Add(new GeometryDrawing(B(petal), P(edge, 0.7), e));
        }
        parts.Add(new GeometryDrawing(B(center), P(edge, 0.7), new EllipseGeometry(new Point(8, 8), 2.4, 2.4)));
        return Frame(parts.ToArray());
    }

    static DrawingGroup Envelope() => Frame(
        new GeometryDrawing(B("#FFE16B"), P("#A9801A"), new RectangleGeometry(new Rect(1.5, 3.5, 13, 9), 1, 1)),
        new GeometryDrawing(null, P("#A9801A"), Geometry.Parse("M1.8,4 L8,9 L14.2,4")));

    static DrawingGroup Group() => Frame(
        new GeometryDrawing(B("#7DA7E8"), P("#2D5BA8"), new EllipseGeometry(new Point(10.8, 5), 2.3, 2.3)),
        new GeometryDrawing(B("#7DA7E8"), P("#2D5BA8"), Geometry.Parse("M7,14 C7,9.5 14.6,9.5 14.6,14 Z")),
        new GeometryDrawing(B("#A9C8F5"), P("#2D5BA8"), new EllipseGeometry(new Point(5.6, 6), 2.5, 2.5)),
        new GeometryDrawing(B("#A9C8F5"), P("#2D5BA8"), Geometry.Parse("M1.4,15 C1.4,10 9.8,10 9.8,15 Z")));

    static DrawingGroup Channel() => Frame(
        new GeometryDrawing(B("#FFB347"), P("#B5651D"), Geometry.Parse("M2,6 L6,6 L12,2.5 L12,13.5 L6,10 L2,10 Z")),
        new GeometryDrawing(B("#FFB347"), P("#B5651D"), Geometry.Parse("M4,10 L5.5,14 L7.3,14 L6.3,10 Z")),
        new GeometryDrawing(null, P("#B5651D", 1), Geometry.Parse("M13.6,6 C14.6,7 14.6,9 13.6,10")));

    static DrawingGroup Bot() => Frame(
        new GeometryDrawing(B("#B8C4D6"), P("#4A5A73"), new RectangleGeometry(new Rect(2.5, 5, 11, 8.5), 2, 2)),
        new GeometryDrawing(null, P("#4A5A73"), Geometry.Parse("M8,5 L8,2.6")),
        new GeometryDrawing(B("#E2392F"), null, new EllipseGeometry(new Point(8, 2.2), 1.2, 1.2)),
        new GeometryDrawing(B("#1E2A3C"), null, new EllipseGeometry(new Point(5.8, 8.7), 1.2, 1.2)),
        new GeometryDrawing(B("#1E2A3C"), null, new EllipseGeometry(new Point(10.2, 8.7), 1.2, 1.2)),
        new GeometryDrawing(null, P("#1E2A3C"), Geometry.Parse("M6,11.4 L10,11.4")));

    static DrawingGroup Saved() => Frame(
        new GeometryDrawing(B("#5E9BEA"), P("#22528F"), Geometry.Parse("M4,1.8 L12,1.8 L12,14.5 L8,11.3 L4,14.5 Z")));

    /// <summary>Rasterizes a glyph, for window icons and the tray.</summary>
    public static BitmapSource Bitmap(string key, int size = 32)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawImage(Get(key), new Rect(0, 0, size, size));
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    public static System.Drawing.Icon TrayIcon(string key)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(Bitmap(key, 32)));
        using var ms = new MemoryStream();
        enc.Save(ms);
        ms.Position = 0;
        using var bmp = new System.Drawing.Bitmap(ms);
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }
}

/// <summary>XAML bridge: {Binding Icon, Converter={StaticResource Icon}}.</summary>
public sealed class IconConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c) => Icons.Get(value as string ?? "");
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
}
