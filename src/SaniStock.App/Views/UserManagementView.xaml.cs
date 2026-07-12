using System.Windows;
using System.Windows.Controls;
using SaniStock.App.ViewModels;

namespace SaniStock.App.Views;

public partial class UserManagementView : UserControl
{
    public UserManagementView() => InitializeComponent();

    private UserManagementViewModel? Vm => DataContext as UserManagementViewModel;

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        Vm?.CreateUser(NewPasswordBox.Password);
        NewPasswordBox.Clear();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        Vm?.ResetPassword(ResetPasswordBox.Password);
        ResetPasswordBox.Clear();
    }
}
