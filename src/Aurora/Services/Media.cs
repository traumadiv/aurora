using System.IO;
using System.Windows.Media.Imaging;
using TdLib;

namespace Aurora.Services;

/// <summary>
/// Photos and stickers: downloads files through TDLib (they land in %APPDATA%\Aurora\td\files)
/// and decodes them into small cached bitmaps. Static stickers are WebP, which Windows decodes itself;
/// animated stickers (TGS, WebM) are shown by their still thumbnail.
/// </summary>
public static class Media
{
    static readonly Dictionary<string, BitmapSource> _cache = new();

    /// <summary>Local path of a TDLib file, downloading it first if needed. Null when it can't be had.</summary>
    public static async Task<string?> Path(TdApi.File? f)
    {
        if (f == null) return null;
        if (f.Local is { IsDownloadingCompleted: true } l && File.Exists(l.Path)) return l.Path;
        if (Demo.On || Tg.Client == null) return null;
        try
        {
            var done = await Tg.Client.ExecuteAsync(new TdApi.DownloadFile { FileId = f.Id, Priority = 16, Synchronous = true });
            return done.Local is { IsDownloadingCompleted: true } dl ? dl.Path : null;
        }
        catch { return null; }
    }

    /// <summary>Decodes an image at a width that fits the chat; WPF would otherwise keep full-size bitmaps in memory.</summary>
    public static BitmapSource? Load(string path, int width)
    {
        var key = path + "|" + width;
        if (_cache.TryGetValue(key, out var hit)) return hit;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // read now and release the file
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = width;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            if (_cache.Count > 300) _cache.Clear();
            _cache[key] = bmp;
            return bmp;
        }
        catch { return null; }
    }

    public static async Task<BitmapSource?> Image(TdApi.File? f, int width) =>
        await Path(f) is { } p ? Load(p, width) : null;

    /// <summary>The blurry 40px preview TDLib sends inline with every photo: shown until the real one is in.</summary>
    public static BitmapSource? Mini(TdApi.Minithumbnail? m)
    {
        if (m?.Data is not { Length: > 0 } data) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(data);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>The photo size to show in a chat: the smallest one at least as wide as the slot.</summary>
    public static TdApi.PhotoSize? Fit(TdApi.Photo p, int want) =>
        p.Sizes.Where(s => s.Width >= want).OrderBy(s => s.Width).FirstOrDefault() ?? p.Sizes.OrderByDescending(s => s.Width).FirstOrDefault();

    public static TdApi.PhotoSize? Largest(TdApi.Photo p) => p.Sizes.OrderByDescending(s => s.Width).FirstOrDefault();

    /// <summary>A file that can be shown as a still picture of the sticker: the WebP itself, or the thumbnail of an animated one.</summary>
    public static TdApi.File? StickerStill(TdApi.Sticker s, bool preferThumb = false)
    {
        var thumbOk = s.Thumbnail?.Format is TdApi.ThumbnailFormat.ThumbnailFormatWebp or TdApi.ThumbnailFormat.ThumbnailFormatJpeg
            or TdApi.ThumbnailFormat.ThumbnailFormatPng;
        if (s.Format is TdApi.StickerFormat.StickerFormatWebp && !(preferThumb && thumbOk)) return s.Sticker_;
        return thumbOk ? s.Thumbnail!.File : null;
    }

    public static TdApi.File? ThumbFile(TdApi.Thumbnail? t) =>
        t?.Format is TdApi.ThumbnailFormat.ThumbnailFormatWebp or TdApi.ThumbnailFormat.ThumbnailFormatJpeg or TdApi.ThumbnailFormat.ThumbnailFormatPng
            ? t.File : null;

    // ---------- custom emoji ----------

    static readonly Dictionary<long, Task<TdApi.Sticker?>> _emoji = new();
    static List<(long Id, TaskCompletionSource<TdApi.Sticker?> Done)>? _emojiBatch;

    /// <summary>The sticker behind a custom emoji. Requests from one render are sent to TDLib together.</summary>
    public static Task<TdApi.Sticker?> CustomEmoji(long id)
    {
        if (_emoji.TryGetValue(id, out var known)) return known;
        if (Demo.On || Tg.Client == null) return _emoji[id] = Task.FromResult(Demo.CustomEmoji(id));
        var done = new TaskCompletionSource<TdApi.Sticker?>();
        _emoji[id] = done.Task;
        if (_emojiBatch == null)
        {
            _emojiBatch = new();
            _ = FlushEmoji();
        }
        _emojiBatch.Add((id, done));
        return done.Task;
    }

    static async Task FlushEmoji()
    {
        await Task.Delay(30);
        var batch = _emojiBatch!;
        _emojiBatch = null;
        foreach (var chunk in batch.Chunk(200))
        {
            try
            {
                var r = await Tg.Client.ExecuteAsync(new TdApi.GetCustomEmojiStickers { CustomEmojiIds = chunk.Select(x => x.Id).ToArray() });
                var byId = new Dictionary<long, TdApi.Sticker>();
                foreach (var s in r.Stickers_)
                    if (s.FullType is TdApi.StickerFullType.StickerFullTypeCustomEmoji ce) byId[ce.CustomEmojiId] = s;
                foreach (var (id, done) in chunk) done.TrySetResult(byId.GetValueOrDefault(id));
            }
            catch
            {
                foreach (var (id, done) in chunk) { _emoji.Remove(id); done.TrySetResult(null); }
            }
        }
    }

    public static bool IsImageFile(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp";
}
