using System.IO;
using System.IO.Compression;
using SkiaSharp.Skottie;

namespace Aurora.Services;

/// <summary>
/// Animated Telegram stickers and custom emoji come as .tgs: a gzipped Lottie JSON.
/// Skottie (Skia's Lottie player) parses it once; every view of the same file shares the animation.
/// </summary>
public static class Lottie
{
    static readonly Dictionary<string, Animation?> _cache = new();

    public static Animation? Load(string path)
    {
        if (_cache.TryGetValue(path, out var hit)) return hit;
        Animation? anim = null;
        try
        {
            using var fs = File.OpenRead(path);
            var gz = fs.ReadByte() == 0x1F;
            fs.Position = 0;
            using Stream s = gz ? new GZipStream(fs, CompressionMode.Decompress) : fs;
            using var reader = new StreamReader(s);
            if (Animation.TryParse(reader.ReadToEnd(), out var a)) anim = a;
        }
        catch { }
        // views may still hold dropped animations, so they are left to the GC rather than disposed
        if (_cache.Count > 400) _cache.Clear();
        _cache[path] = anim;
        return anim;
    }
}
