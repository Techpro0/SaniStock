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

    /// <summary>
    /// Copies the live database — and any SQLite <c>-wal</c>/<c>-shm</c>/<c>-journal</c> siblings —
    /// into <paramref name="backupDir"/> before a migration runs. Mirrors the installer's own
    /// pre-install backup (<c>PrepareToInstall</c> in <c>SaniStock.iss</c>), but as an application-
    /// level safety net that fires regardless of how the update was deployed — the polished
    /// installer, a manual file copy, or a straight <c>dotnet publish</c> folder swap.
    /// <para>
    /// A crash between commits can leave committed data sitting only in a <c>-wal</c>/<c>-shm</c>
    /// file rather than the <c>.db</c> file itself, so the database alone is not always the whole
    /// story — every sibling gets backed up alongside it, under one shared timestamp so they stay
    /// recognisable as belonging to the same backup.
    /// </para>
    /// <para>
    /// Each copy is verified by file size before being trusted; a mismatch is deleted immediately
    /// and reported via <see cref="DomainException"/> rather than left on disk looking like a backup
    /// that is not actually usable. Returns the empty list — not an exception — when there is no
    /// database yet (a fresh install has nothing to protect).
    /// </para>
    /// </summary>
    public IReadOnlyList<string> BackUpBeforeUpgrade(string backupDir, string versionLabel)
    {
        if (!File.Exists(_dbPath))
            return Array.Empty<string>();

        Directory.CreateDirectory(backupDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var sourceDir = Path.GetDirectoryName(_dbPath) ?? ".";
        var dbFileName = Path.GetFileName(_dbPath);

        // The .db itself plus -wal/-shm/-journal siblings, never a backup left by an earlier upgrade.
        var sources = Directory.GetFiles(sourceDir, dbFileName + "*")
            .Where(f => !Path.GetFileName(f).Contains(".bak", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var backedUp = new List<string>();
        foreach (var source in sources)
        {
            var target = Path.Combine(backupDir, $"{Path.GetFileName(source)}.before-{versionLabel}-{stamp}.bak");
            File.Copy(source, target, overwrite: true);

            if (new FileInfo(source).Length != new FileInfo(target).Length)
            {
                File.Delete(target);
                throw new DomainException(
                    $"Safety backup of {Path.GetFileName(source)} did not match the original size — discarded.");
            }
            backedUp.Add(target);
        }
        return backedUp;
    }
}
