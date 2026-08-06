using SaniStock.Domain.Services;
using Xunit;

namespace SaniStock.Domain.Tests;

/// <summary>
/// Covers <see cref="BackupService.BackUpBeforeUpgrade"/> — the safety net <c>App.xaml.cs</c> calls
/// before applying any pending migration, so an upgrade never proceeds without a recoverable copy
/// of the client's existing data.
/// </summary>
public class BackupServiceTests : IDisposable
{
    private readonly string _root;

    public BackupServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SaniStockBackupTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Fresh_install_with_no_database_backs_up_nothing()
    {
        var dbPath = Path.Combine(_root, "sanistock.db");   // never created
        var backup = new BackupService(dbPath);

        var backedUp = backup.BackUpBeforeUpgrade(Path.Combine(_root, "Backups"), "2.3.0");

        Assert.Empty(backedUp);
    }

    [Fact]
    public void Backs_up_the_database_and_its_sqlite_siblings_under_one_stamp()
    {
        var dbPath = Path.Combine(_root, "sanistock.db");
        File.WriteAllText(dbPath, "fake db contents");
        File.WriteAllText(dbPath + "-wal", "wal contents");
        File.WriteAllText(dbPath + "-shm", "shm contents");
        var backup = new BackupService(dbPath);

        var backedUp = backup.BackUpBeforeUpgrade(Path.Combine(_root, "Backups"), "2.3.0");

        Assert.Equal(3, backedUp.Count);
        Assert.All(backedUp, p => Assert.True(File.Exists(p)));
        Assert.Contains(backedUp, p => p.Contains("sanistock.db.before-2.3.0-"));
        Assert.Contains(backedUp, p => p.Contains("sanistock.db-wal.before-2.3.0-"));
        Assert.Contains(backedUp, p => p.Contains("sanistock.db-shm.before-2.3.0-"));
    }

    [Fact]
    public void Does_not_back_up_a_previous_backup_sitting_beside_the_database()
    {
        var dbPath = Path.Combine(_root, "sanistock.db");
        File.WriteAllText(dbPath, "fake db contents");
        // Left behind either by an earlier run of this same method, or by BackupService.Restore's
        // own ".bak" — either way it must not be re-backed-up as though it were live data.
        File.WriteAllText(dbPath + ".before-2.2.0-20260101-000000.bak", "an earlier backup");
        File.WriteAllText(dbPath + ".bak", "a restore-time backup");
        var backup = new BackupService(dbPath);

        var backedUp = backup.BackUpBeforeUpgrade(Path.Combine(_root, "Backups"), "2.3.0");

        Assert.Single(backedUp);
        Assert.Contains("sanistock.db.before-2.3.0-", backedUp[0]);
    }

    [Fact]
    public void Backed_up_file_content_matches_the_source_exactly()
    {
        var dbPath = Path.Combine(_root, "sanistock.db");
        const string content = "some database bytes, arbitrary length, nothing SQLite-specific needed here";
        File.WriteAllText(dbPath, content);
        var backup = new BackupService(dbPath);

        var backedUp = backup.BackUpBeforeUpgrade(Path.Combine(_root, "Backups"), "2.3.0");

        Assert.Equal(content, File.ReadAllText(Assert.Single(backedUp)));
    }

    [Fact]
    public void Creates_the_backup_directory_if_it_does_not_exist_yet()
    {
        var dbPath = Path.Combine(_root, "sanistock.db");
        File.WriteAllText(dbPath, "fake db contents");
        var backupDir = Path.Combine(_root, "Backups", "nested", "deeper");
        var backup = new BackupService(dbPath);

        var backedUp = backup.BackUpBeforeUpgrade(backupDir, "2.3.0");

        Assert.True(Directory.Exists(backupDir));
        Assert.Single(backedUp);
    }
}
