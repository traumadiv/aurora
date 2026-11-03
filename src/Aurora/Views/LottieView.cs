using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;
using SkiaSharp.Skottie;

namespace Aurora.Views;

/// <summary>
/// Plays a Lottie animation into a WriteableBitmap. One shared 30 fps timer drives every view on screen,
/// so a chat full of animated emoji costs one tick, and views that are hidden or minimized skip their frame.
/// </summary>
public sealed class LottieView : Image
{
    static readonly HashSet<LottieView> Live = new();
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly DispatcherTimer Timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };

    static LottieView()
    {
        Timer.Tick += (_, _) =>
        {
            foreach (var v in Live.ToArray()) v.Frame();
        };
    }

    readonly Animation _anim;
    readonly int _px;
    readonly WriteableBitmap _bmp;
    readonly SKColor? _tint;
    double _start;
    Window? _window;

    /// <param name="tint">Telegram's "needs repainting" emoji are drawn in the text color.</param>
    public LottieView(Animation anim, double size, bool play = true, Color? tint = null)
    {
        _anim = anim;
        Width = Height = size;
        Stretch = Stretch.Uniform;
        _px = Math.Max(16, (int)Math.Ceiling(size * 1.5)); // sharp at 150% scaling, still cheap to draw
        _bmp = new WriteableBitmap(_px, _px, 96, 96, PixelFormats.Pbgra32, null);
        if (tint is { } c) _tint = new SKColor(c.R, c.G, c.B, c.A);
        Source = _bmp;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Draw(0);
        if (!play) return;
        Loaded += (_, _) =>
        {
            _window = Window.GetWindow(this);
            _start = Clock.Elapsed.TotalSeconds;
            Live.Add(this);
            if (!Timer.IsEnabled) Timer.Start();
        };
        Unloaded += (_, _) =>
        {
            Live.Remove(this);
            if (Live.Count == 0) Timer.Stop();
        };
    }

    void Frame()
    {
        if (!IsVisible || _window?.WindowState == WindowState.Minimized) return;
        var d = _anim.Duration.TotalSeconds;
        Draw(d > 0 ? (Clock.Elapsed.TotalSeconds - _start) % d : 0);
    }

    void Draw(double t)
    {
        _anim.SeekFrameTime(t, null);
        _bmp.Lock();
        try
        {
            var info = new SKImageInfo(_px, _px, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, _bmp.BackBuffer, _bmp.BackBufferStride);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            var rect = new SKRect(0, 0, _px, _px);
            if (_tint is { } tint)
            {
                using var paint = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(tint, SKBlendMode.SrcIn) };
                canvas.SaveLayer(paint);
                _anim.Render(canvas, rect);
                canvas.Restore();
            }
            else _anim.Render(canvas, rect);
            canvas.Flush();
            _bmp.AddDirtyRect(new Int32Rect(0, 0, _px, _px));
        }
        finally { _bmp.Unlock(); }
    }
}
