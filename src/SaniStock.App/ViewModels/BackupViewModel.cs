using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using SaniStock.App.Infrastructure;
using SaniStock.Domain.Services;

namespace SaniStock.App.ViewModels;

public partial class BackupViewModel : ViewModelBase
{
    private readonly IDomainScopeFactory _scopes;
    private readonly IDialogService _dialogs;
    private readonly BackupService _backup = new(AppPaths.DbPath);

    public BackupViewModel(IDomainScopeFactory scopes, IDialogService dialogs)
    {
        _scopes = scopes;
        _dialogs = dialogs;
        Title = "Backup & Restore";
    }

    public string DbPath => AppPaths.DbPath;

    [RelayCommand]
    private void Backup()
    {
        var path = _dialogs.SaveFile("SaniStock backup (*.db)|*.db", $"sanistock_backup_{DateTime.Now:yyyyMMdd_HHmm}.db");
        if (path is null) return;
        try
        {
            SqliteConnection.ClearAllPools(); // release the file so it can be copied cleanly
            _backup.Backup(path);
            _dialogs.Info("Backup saved to:\n" + path);
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private void Restore()
    {
        var path = _dialogs.OpenFile("SaniStock backup (*.db)|*.db");
        if (path is null) return;
        if (!_dialogs.Confirm("Restoring will replace the current database with the selected backup. "
            + "A copy of the current database is kept as .bak. Continue?")) return;
        try
        {
            SqliteConnection.ClearAllPools();
            _backup.Restore(path);
            // Rebuild cached balances from the restored ledger for safety.
            using var scope = _scopes.Create();
            scope.Stock.ReconcileAll();
            _dialogs.Info("Database restored. Please close and reopen SaniStock to reload all data.");
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private void Reconcile()
    {
        try
        {
            using var scope = _scopes.Create();
            var count = scope.Stock.ReconcileAll();
            _dialogs.Info($"Recalculated {count} stock balance(s) from the movement ledger.");
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }
}
