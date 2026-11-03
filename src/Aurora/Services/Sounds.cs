using System.IO;
using System.Media;
using System.Windows.Media;

namespace Aurora.Services;

/// <summary>
/// The default notification sound is Assets\message.mp3, embedded in the exe.
/// Your own message.wav or message.mp3 in %APPDATA%\Aurora\sounds overrides it
/// (Settings → Notifications → "Выбрать свой звук…" puts it there).
/// A synthesized chirp is the last resort if neither can be played.
/// </summary>
public static class Sounds
{
    static SoundPlayer? _wav;
    static MediaPlayer? _mp3;
    static DateTime _last;

    public static string Dir => Path.Combine(Config.Dir, "sounds");
    static readonly string[] Exts = [".wav", ".mp3"];

    /// <summary>Path of the user's sound, if any.</summary>
    public static string? Custom => Exts.Select(e => Path.Combine(Dir, "message" + e)).FirstOrDefault(File.Exists);

    public static void Init()
    {
        _wav?.Dispose();
        _wav = null;
        _mp3?.Close();
        _mp3 = null;

        var file = Custom ?? BuiltIn();
        try
        {
            if (file?.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) == true)
            {
                _mp3 = new MediaPlayer { Volume = 1 };
                _mp3.Open(new Uri(file));
                return;
            }
            _wav = file != null
                ? new SoundPlayer(file)
                : new SoundPlayer(new MemoryStream(Synth((660, 0.07), (0, 0.03), (990, 0.11))));
            _wav.Load();
        }
        catch
        {
            // Unreadable custom file: fall back to the built-in chirp.
            _wav = new SoundPlayer(new MemoryStream(Synth((660, 0.07), (0, 0.03), (990, 0.11))));
            _wav.Load();
        }
    }

    /// <summary>
    /// MediaPlayer only plays files, so the embedded mp3 is written next to the config once
    /// (and rewritten when a new Aurora ships a different one).
    /// </summary>
    static string? BuiltIn()
    {
        try
        {
            using var res = typeof(Sounds).Assembly.GetManifestResourceStream("Aurora.message.mp3");
            if (res == null) return null;
            var path = Path.Combine(Config.Dir, "default-message.mp3");
            if (!File.Exists(path) || new FileInfo(path).Length != res.Length)
            {
                Directory.CreateDirectory(Config.Dir);
                using var fs = File.Create(path);
                res.CopyTo(fs);
            }
            return path;
        }
        catch { return null; }
    }

    /// <summary>Copies a picked file into the sounds folder and switches to it.</summary>
    public static void SetCustom(string file)
    {
        Directory.CreateDirectory(Dir);
        ResetCustom();
        var ext = Path.GetExtension(file).ToLowerInvariant();
        File.Copy(file, Path.Combine(Dir, "message" + ext), overwrite: true);
        Init();
    }

    public static void ResetCustom()
    {
        _mp3?.Close();
        _mp3 = null;
        _wav?.Dispose();
        _wav = null;
        foreach (var e in Exts)
        {
            var f = Path.Combine(Dir, "message" + e);
            if (File.Exists(f)) File.Delete(f);
        }
        Init();
    }

    public static void Message()
    {
        // A burst of messages should not machine-gun the speaker.
        if ((DateTime.Now - _last).TotalMilliseconds < 700) return;
        _last = DateTime.Now;
        try
        {
            if (_mp3 != null)
            {
                _mp3.Stop();
                _mp3.Position = TimeSpan.Zero;
                _mp3.Play();
            }
            else _wav?.Play();
        }
        catch { }
    }

    /// <summary>Square-ish chirps with a quick decay: retro pager feel, 16-bit mono 22 kHz.</summary>
    static byte[] Synth(params (double hz, double sec)[] notes)
    {
        const int rate = 22050;
        var samples = new List<short>();
        foreach (var (hz, sec) in notes)
        {
            var n = (int)(rate * sec);
            for (var i = 0; i < n; i++)
            {
                double v = 0;
                if (hz > 0)
                {
                    var t = (double)i / rate;
                    var env = Math.Exp(-t * 18) * Math.Min(1, i / 60.0);
                    v = (Math.Sin(2 * Math.PI * hz * t) + 0.35 * Math.Sin(2 * Math.PI * hz * 2 * t)) * env * 0.45;
                }
                samples.Add((short)(v * short.MaxValue));
            }
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var dataLen = samples.Count * 2;
        w.Write("RIFF"u8); w.Write(36 + dataLen); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataLen);
        foreach (var s in samples) w.Write(s);
        return ms.ToArray();
    }
}
