namespace SaniStock.Domain.Services;

/// <summary>Copies the SQLite database file for backup and restore.</summary>
public class BackupService
{
    private readonly string _dbPath;

    public BackupService(string dbPath) => _dbPath = dbPath;

    /// <summary>Copies the live database to <paramref name="destinationPath"/>.</summary>
    public void Backup(string destinationPath)
    {
        if (!File.Exists(_dbPath))
            throw new DomainException("Database file not found; nothing to back up yet.");
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.Copy(_dbPath, destinationPath, overwrite: true);
    }

    /// <summary>
    /// Replaces the live database with a backup. The caller must ensure no DbContext is
    /// actively using the file (the app restarts after restore). Keeps a .bak of the current db.
    /// </summary>
    public void Restore(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new DomainException("Selected backup file does not exist.");
        if (File.Exists(_dbPath))
            File.Copy(_dbPath, _dbPath + ".bak", overwrite: true);
        File.Copy(sourcePath, _dbPath, overwrite: true);
    }
}
