using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SaniStock.App.ViewModels;

namespace SaniStock.App.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += (_, _) => UsernameBox.Focus();
    }

    private void SignIn_Click(object sender, RoutedEventArgs e) => TryLogin();

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryLogin();
    }

    private void TryLogin()
    {
        if (!_vm.TryLogin(PasswordBox.Password)) return;

        var shell = App.Services.GetRequiredService<ShellWindow>();
        Application.Current.MainWindow = shell;
        shell.Show();
        Close();
    }
}
