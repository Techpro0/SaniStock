using System.IO;

namespace SaniStock.App.Infrastructure;

/// <summary>Resolves the local data/log locations under %LOCALAPPDATA%\SaniStock.</summary>
public static class AppPaths
{
    public static string RootDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaniStock");

    public static string DbPath { get; } = Path.Combine(RootDir, "sanistock.db");

    public static string LogDir { get; } = Path.Combine(RootDir, "logs");

    public static string ConnectionString => $"Data Source={DbPath}";

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(LogDir);
    }
}
