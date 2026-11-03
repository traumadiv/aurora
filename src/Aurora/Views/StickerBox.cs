using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TdLib;
using Aurora.Services;

namespace Aurora.Views;

/// <summary>
/// A sticker or a custom emoji: the emoji as a smiley until the file is in, then the still picture,
/// and for animated (TGS) stickers the animation itself.
/// </summary>
public sealed class StickerBox : Grid
{
    readonly TextBlock _fallback;
    readonly Image _still = new() { Stretch = Stretch.Uniform };
    readonly double _size;
    TdApi.Sticker? _sticker;
    LottieView? _live;
    bool _wantPlay;

    public StickerBox(Task<TdApi.Sticker?> sticker, double w, double h, string? emoji, bool play, double fontSize = 0)
    {
        Width = w;
        Height = h;
        _size = Math.Min(w, h);
        _wantPlay = play;
        Background = Brushes.Transparent;
        _fallback = new TextBlock
        {
            FontSize = fontSize > 0 ? fontSize : Math.Max(12, _size * 0.45),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Smileys.SetText(_fallback, emoji ?? "");
        RenderOptions.SetBitmapScalingMode(_still, BitmapScalingMode.HighQuality);
        Children.Add(_fallback);
        Children.Add(_still);
        _ = Load(sticker);
    }

    public StickerBox(TdApi.Sticker sticker, double w, double h, bool play)
        : this(Task.FromResult<TdApi.Sticker?>(sticker), w, h, sticker.Emoji, play) { }

    public bool IsAnimated => _sticker?.Format is TdApi.StickerFormat.StickerFormatTgs;

    async Task Load(Task<TdApi.Sticker?> pending)
    {
        _sticker = await pending;
        if (_sticker == null) return;
        if (_wantPlay && IsAnimated && await Start()) return;
        if (await Media.Image(Media.StickerStill(_sticker, preferThumb: _size < 100), (int)(_size * 1.5)) is { } bmp)
        {
            _still.Source = bmp;
            _fallback.Visibility = Visibility.Collapsed;
        }
        else if (IsAnimated && await Start(play: false)) { } // no still thumbnail: the first frame of the animation
    }

    /// <summary>Starts the animation (the sticker panel does this on hover).</summary>
    public async void Play()
    {
        _wantPlay = true;
        if (_live == null && IsAnimated) await Start();
    }

    public void Pause()
    {
        _wantPlay = false;
        if (_live == null || _still.Source == null) return;
        Children.Remove(_live);
        _live = null;
        _still.Visibility = Visibility.Visible;
    }

    async Task<bool> Start(bool play = true)
    {
        if (await Media.Path(_sticker!.Sticker_) is not { } path || Lottie.Load(path) is not { } anim) return false;
        if (play && !_wantPlay) return false; // the mouse left while the file was downloading
        Color? tint = null;
        if (_sticker.FullType is TdApi.StickerFullType.StickerFullTypeCustomEmoji { NeedsRepainting: true }
            && TryFindResource("Ink") is SolidColorBrush ink)
            tint = ink.Color;
        if (_live != null) Children.Remove(_live);
        _live = new LottieView(anim, _size, play, tint);
        Children.Add(_live);
        _fallback.Visibility = Visibility.Collapsed;
        _still.Visibility = Visibility.Collapsed;
        return true;
    }
}
