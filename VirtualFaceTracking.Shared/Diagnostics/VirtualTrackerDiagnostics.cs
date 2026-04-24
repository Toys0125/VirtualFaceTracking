using System.Globalization;
using System.Text;

namespace VirtualFaceTracking.Shared.Diagnostics;

public static class VirtualTrackerDiagnostics
{
    private static readonly object Sync = new();
    private static string? _logPath;

    public static string LogPath => _logPath ?? Path.Combine(AppContext.BaseDirectory, "virtual-tracker.log");

    public static void Configure(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        lock (Sync)
        {
            Directory.CreateDirectory(directoryPath);
            _logPath = Path.Combine(directoryPath, "virtual-tracker.log");
        }
    }

    public static void Write(string source, string message)
    {
        try
        {
            var path = LogPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{Environment.ProcessId}] {source}: {message}{Environment.NewLine}");

            lock (Sync)
            {
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.Write(line);
            }
        }
        catch
        {
        }
    }

    public static string ReadAll()
    {
        try
        {
            using var stream = new FileStream(LogPath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    public static void Clear()
    {
        try
        {
            File.WriteAllText(LogPath, string.Empty, Encoding.UTF8);
        }
        catch
        {
        }
    }
}
