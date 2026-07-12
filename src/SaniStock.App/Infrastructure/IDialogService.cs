using System.Windows;
using Microsoft.Win32;

namespace SaniStock.App.Infrastructure;

/// <summary>Abstraction over WPF message boxes and file dialogs (keeps view models UI-free).</summary>
public interface IDialogService
{
    void Info(string message, string title = "SaniStock");
    void Error(string message, string title = "Error");
    bool Confirm(string message, string title = "Please confirm");
    string? SaveFile(string filter, string defaultFileName);
    string? OpenFile(string filter);
}

public sealed class DialogService : IDialogService
{
    public void Info(string message, string title = "SaniStock") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Error(string message, string title = "Error") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message, string title = "Please confirm") =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? SaveFile(string filter, string defaultFileName)
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = defaultFileName, OverwritePrompt = true };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? OpenFile(string filter)
    {
        var dlg = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
