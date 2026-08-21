using System.Windows.Controls;
using SaniStock.App.ViewModels;

namespace SaniStock.App.Views;

public partial class OrderDispatchView : UserControl
{
    public OrderDispatchView() => InitializeComponent();

    private void OpenOrdersGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        if (DataContext is OrderDispatchViewModel vm && OpenOrdersGrid.CurrentCell.IsValid)
            vm.SelectedOrder = OpenOrdersGrid.CurrentCell.Item as OpenOrderRow;
    }
}
