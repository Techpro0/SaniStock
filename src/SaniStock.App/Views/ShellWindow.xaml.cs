using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SaniStock.App.ViewModels;
using SaniStock.Domain;

namespace SaniStock.App.Views;

public partial class ShellWindow : Window
{
    private readonly UserContext _user;

    public ShellWindow(ShellViewModel vm, UserContext user)
    {
        InitializeComponent();
        _user = user;
        DataContext = vm;
        vm.LogoutRequested += OnLogout;
    }

    private void OnLogout()
    {
        _user.SignOut();
        var login = App.Services.GetRequiredService<LoginWindow>();
        Application.Current.MainWindow = login;
        login.Show();
        Close();
    }
}
