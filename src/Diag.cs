using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace Shengyi;

// How much to record. "Standard" is the default: errors, warnings and a small number of
// key events (startup, a successful clipboard write, a changed suppression reason). The
// 30 ms reveal timer never writes at its own frequency — high-traffic paths use Debug or
// Throttled(), so a normal day stays in the tens of KB.
enum DiagMode { Off = 0, Errors = 1, Standard = 2, Verbose = 3 }
enum DiagLevel { Error = 1, Warn = 2, Info = 3, Debug = 4 }

// Zero-dependency diagnostic log. Everything here is BCL only, so _verify can compile this
// file into a plain console app and exercise the pure helpers. Deliberately does NOT reference
// Settings, Theme or any WPF/WinForms type: the data directory and the mode are injected by
// Init()/Configure(), which also means --shots / --preview never write anything (they simply
// never call Init).
static class Diag
{
    public const string LogFileName = "shengyi.log";
    public const long MaxFileBytes = 2L * 1024 * 1024;   // rotate at 2 MB
    public const int ArchiveCount = 2;                    // shengyi.log + .1.log + .2.log
    public const int MaxFieldChars = 160;
    public const int DefaultThrottleSeconds = 60;

    // ---------------- pure logic (asserted by _verify) ----------------

    public static bool ShouldLog(DiagMode mode, DiagLevel level) => mode switch
    {
        DiagMode.Errors => level == DiagLevel.Error,
        DiagMode.Standard => level <= DiagLevel.Info,
        DiagMode.Verbose => true,
        _ => false,
    };

    public static DiagMode ParseMode(bool enabled, string? level) => !enabled
        ? DiagMode.Off
        : level switch { "errors" => DiagMode.Errors, "verbose" => DiagMode.Verbose, _ => DiagMode.Standard };

    public static string ModeId(DiagMode mode) => mode switch
    {
        DiagMode.Off => "off",
        DiagMode.Errors => "errors",
        DiagMode.Verbose => "verbose",
        _ => "standard",
    };

    public static bool Due(DateTime? lastUtc, DateTime nowUtc, int seconds) =>
        !lastUtc.HasValue || nowUtc < lastUtc.Value || (nowUtc - lastUtc.Value).TotalSeconds >= seconds;

    public static bool NeedsRotation(long currentBytes, long incomingBytes, long maxBytes) =>
        currentBytes > 0 && currentBytes + incomingBytes > maxBytes;

    // Values are single-line and quoted-safe. Newlines, control characters and the three
    // characters used by the line format are replaced, and long values are truncated so a
    // stray clipboard excerpt can never end up in the log verbatim.
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(Math.Min(value.Length, MaxFieldChars + 1));
        foreach (char c in value)
        {
            sb.Append(c is '\r' or '\n' or '\t' or '|' or '=' or '"' or < ' ' ? ' ' : c);
            if (sb.Length < MaxFieldChars) continue;
            sb.Length = MaxFieldChars;
            sb.Append('~');
            break;
        }
        return sb.ToString().Trim();
    }

    // Longer variant used only for exception stacks: still neutralised, but not cut at 160.
    public static string SanitizeLong(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(Math.Min(value.Length, max));
        foreach (char c in value)
        {
            if (c is '\r' or '\n' or '\t' or '"') sb.Append(' ');
            else if (c >= ' ') sb.Append(c);
            if (sb.Length >= max) break;
        }
        return sb.ToString();
    }

    // Callers must never pass a full path. This returns ONLY the extension (or a shape
    // marker), so a directory name or a document name cannot leak into the log.
    public static string PathShape(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "none";
        int dot = path.LastIndexOf('.'), sep = path.LastIndexOfAny(new[] { '/', '\\' });
        if (dot <= sep || dot == path.Length - 1) return "noext";
        string ext = path[(dot + 1)..];
        if (ext.Length > 8) return "long";
        foreach (char c in ext) if (!char.IsLetterOrDigit(c)) return "noext";
        return ext.ToLowerInvariant();
    }

    public static string FormatValue(object? value) => value switch
    {
        null => "null",
        bool b => b ? "1" : "0",
        string s => Sanitize(s),
        IFormattable f => Sanitize(f.ToString(null, CultureInfo.InvariantCulture)),
        _ => Sanitize(value.ToString()),
    };

    public static string Line(DateTime utc, DiagLevel level, string category, string message,
        params (string Key, object? Value)[] fields)
    {
        var sb = new StringBuilder();
        sb.Append(utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        sb.Append(' ').Append(LevelName(level)).Append(' ');
        sb.Append(Sanitize(category)).Append(' ').Append(Sanitize(message));
        if (fields == null) return sb.ToString();
        foreach (var (key, value) in fields)
        {
            string v = FormatValue(value);
            sb.Append(' ').Append(Sanitize(key)).Append('=');
            if (v.Contains(' ')) sb.Append('"').Append(v).Append('"');
            else sb.Append(v);
        }
        return sb.ToString();
    }

    public static string LevelName(DiagLevel level) => level switch
    {
        DiagLevel.Error => "ERROR",
        DiagLevel.Warn => "WARN",
        DiagLevel.Info => "INFO",
        _ => "DEBUG",
    };

    public static string[] FileNames()
    {
        var list = new List<string> { LogFileName };
        for (int i = 1; i <= ArchiveCount; i++) list.Add($"shengyi.{i}.log");
        return list.ToArray();
    }

    // ---------------- runtime ----------------

    static readonly object gate = new();
    static readonly Queue<string> pending = new();
    static readonly Dictionary<string, DateTime> lastSeen = new();
    static readonly AutoResetEvent wake = new(false);
    static readonly UTF8Encoding Utf8 = new(false);
    static Thread? worker;
    static string dataDir = "";
    static string version = "";
    static volatile DiagMode mode = DiagMode.Off;
    static bool stopping;

    // No StreamWriter is kept open between writes. A long-lived handle made shengyi.log
    // unreadable by Notepad / Get-Content / the export routine (sharing violation). This log
    // is low-volume, so a short open-append-close per batch is both cheaper and friendlier.
    public static bool Enabled => mode != DiagMode.Off;
    public static DiagMode Mode => mode;
    public static string LogPath => Path.Combine(dataDir, LogFileName);

    // Called once from Program.Main for real runs only. Offline renderers (--shots/--preview)
    // return before this point, so they never create a log file or a background thread.
    public static void Init(string dir, DiagMode initial, string appVersion)
    {
        lock (gate)
        {
            dataDir = dir;
            version = appVersion;
            mode = initial;
            if (worker != null) return;
            stopping = false;
            worker = new Thread(Loop) { IsBackground = true, Name = "shengyi-diag" };
            worker.Start();
        }
    }

    public static void Configure(DiagMode next)
    {
        bool wasOff;
        lock (gate) { wasOff = mode == DiagMode.Off; mode = next; }
        if (next != DiagMode.Off)
            Info("diag", "logging configured", ("mode", ModeId(next)), ("was_off", wasOff));
    }

    public static void Error(string cat, string msg, params (string Key, object? Value)[] f) => Write(DiagLevel.Error, cat, msg, f);
    public static void Warn(string cat, string msg, params (string Key, object? Value)[] f) => Write(DiagLevel.Warn, cat, msg, f);
    public static void Info(string cat, string msg, params (string Key, object? Value)[] f) => Write(DiagLevel.Info, cat, msg, f);
    public static void Debug(string cat, string msg, params (string Key, object? Value)[] f) => Write(DiagLevel.Debug, cat, msg, f);

    public static void Write(DiagLevel level, string cat, string msg, params (string Key, object? Value)[] f)
    {
        if (!ShouldLog(mode, level)) return;
        Enqueue(Line(DateTime.UtcNow, level, cat, msg, f));
    }

    // Same reason repeating every 30 ms would bury the useful lines. One line per key per
    // window; the key is chosen by the caller (e.g. "edge:quns:4").
    public static void Throttled(DiagLevel level, string key, int seconds, string cat, string msg,
        params (string Key, object? Value)[] f)
    {
        if (!ShouldLog(mode, level)) return;
        var now = DateTime.UtcNow;
        lock (gate)
        {
            if (lastSeen.TryGetValue(key, out var last) && !Due(last, now, seconds)) return;
            lastSeen[key] = now;
            if (lastSeen.Count > 256)
            {
                var stale = new List<string>();
                foreach (var pair in lastSeen)
                    if (Due(pair.Value, now, seconds * 4)) stale.Add(pair.Key);
                foreach (var k in stale) lastSeen.Remove(k);
            }
        }
        Write(level, cat, msg, f);
    }

    public static void Throttled(string key, string cat, string msg, params (string Key, object? Value)[] f) =>
        Throttled(DiagLevel.Info, key, DefaultThrottleSeconds, cat, msg, f);

    public static void Crash(string source, Exception e)
    {
        if (!ShouldLog(mode, DiagLevel.Error)) return;
        string line = Line(DateTime.UtcNow, DiagLevel.Error, "crash", source,
            ("type", e.GetType().Name), ("msg", e.Message));
        line += " stack=\"" + SanitizeLong(e.ToString(), 1500) + "\"";
        Enqueue(line);
        Flush(); // a crash may be followed by process death
    }

    static void Enqueue(string line)
    {
        lock (gate)
        {
            if (stopping) return;
            pending.Enqueue(line);
            while (pending.Count > 4096) pending.Dequeue(); // bounded: never grow without limit
        }
        wake.Set();
    }

    static void Loop()
    {
        var batch = new List<string>(64);
        while (true)
        {
            wake.WaitOne(250);
            bool done;
            lock (gate)
            {
                while (pending.Count > 0) batch.Add(pending.Dequeue());
                done = stopping && pending.Count == 0;
            }
            if (batch.Count > 0)
            {
                try { lock (gate) { foreach (var line in batch) WriteOne(line); } }
                catch (Exception) { } // logging must never break the app
                batch.Clear();
            }
            if (done) break;
        }
    }

    static long FileLength() => File.Exists(LogPath) ? new FileInfo(LogPath).Length : 0;

    static void WriteOne(string line)
    {
        if (mode == DiagMode.Off) return;
        Directory.CreateDirectory(dataDir);
        long add = Utf8.GetByteCount(line) + 1;
        if (NeedsRotation(FileLength(), add, MaxFileBytes)) Rotate();
        File.AppendAllText(LogPath, line + "\n", Utf8);
    }

    static void Rotate()
    {
        string At(int i) => Path.Combine(dataDir, i == 0 ? LogFileName : $"shengyi.{i}.log");
        try
        {
            if (File.Exists(At(ArchiveCount))) File.Delete(At(ArchiveCount));
            for (int i = ArchiveCount - 1; i >= 1; i--)
                if (File.Exists(At(i))) File.Move(At(i), At(i + 1), true);
            if (File.Exists(At(0))) File.Move(At(0), At(1), true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Drain the queue synchronously: used before exporting so the bundle is never partial.
    public static void Flush()
    {
        try
        {
            lock (gate) { while (pending.Count > 0) WriteOne(pending.Dequeue()); }
        }
        catch (Exception) { } // a broken disk must never take the caller down
    }

    public static void Clear()
    {
        lock (gate)
        {
            pending.Clear();
            foreach (var name in FileNames())
                try { var p = Path.Combine(dataDir, name); if (File.Exists(p)) File.Delete(p); } catch (Exception) { }
            try { var legacy = Path.Combine(dataDir, "error.log"); if (File.Exists(legacy)) File.Delete(legacy); }
            catch (Exception) { }
        }
        Info("diag", "log cleared");
    }

    public static IReadOnlyList<string> Files()
    {
        var list = new List<string>();
        lock (gate)
            foreach (var name in FileNames())
            {
                var p = Path.Combine(dataDir, name);
                if (File.Exists(p)) list.Add(p);
            }
        return list;
    }

    public static void Shutdown()
    {
        lock (gate) { stopping = true; }
        wake.Set();
        var t = worker;
        if (t != null && t.IsAlive) t.Join(2000);
    }

    // Local-only. The caller supplies a whitelist of non-sensitive settings values; the log
    // files themselves never contain clipboard text, image bytes or full paths.
    public static string Export(string destZip, IReadOnlyDictionary<string, string> info)
    {
        Flush();
        // ZipArchive + an explicit FileShare.ReadWrite read: avoids the ZipFile facade (not
        // present in every trimmed runtime) and any sharing violation while reading logs.
        using var fs = new FileStream(destZip, FileMode.Create, FileAccess.Write, FileShare.None);
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var readme = new StringBuilder();
            readme.AppendLine("绳忆 (shengyi) 诊断包 / Diagnostic bundle");
            readme.AppendLine("Generated (UTC): " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            readme.AppendLine("App version: " + version);
            readme.AppendLine();
            readme.AppendLine("本文件包含运行状态与诊断日志，不含剪贴板正文、图片内容或完整文件路径。");
            readme.AppendLine("This bundle contains runtime state and diagnostic logs only: no clipboard text,");
            readme.AppendLine("no image bytes, no full file paths. It is never uploaded automatically.");
            AddText(zip, "README.txt", readme.ToString());

            var sys = new StringBuilder();
            foreach (var pair in info) sys.AppendLine(pair.Key + "=" + Sanitize(pair.Value));
            AddText(zip, "system.txt", sys.ToString());

            lock (gate)
                foreach (var name in FileNames())
                {
                    var p = Path.Combine(dataDir, name);
                    if (File.Exists(p)) AddFile(zip, p, name);
                }
        }
        return destZip;
    }

    static void AddText(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        var bytes = Utf8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    static void AddFile(ZipArchive zip, string path, string name)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        input.CopyTo(output);
    }
}
