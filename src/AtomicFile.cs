using System;
using System.IO;

namespace Shengyi;

// 0.11.0 · crash-safe writes and corrupt-file quarantine. Both settings.json and clips.json
// now use tmp + atomic replace, so a power loss or forced kill can never leave a truncated file
// that the next launch silently mistakes for "everything default / no history". When a file is
// already unreadable it is renamed aside instead of being overwritten, so the user can recover.
static class AtomicFile
{
    public static string TempOf(string path) => path + ".tmp";

    // Corrupt files are kept, not deleted. A UTC stamp keeps successive failures distinct.
    public static string BackupName(string path, DateTime utc) =>
        path + ".bad." + utc.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

    public static bool Quarantine(string path, DateTime utc)
    {
        if (!File.Exists(path)) return false;
        try { File.Move(path, BackupName(path, utc), true); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public static void WriteText(string path, string content)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = TempOf(path);
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, true); // atomic on NTFS; no reader ever observes a partial file
    }
}
