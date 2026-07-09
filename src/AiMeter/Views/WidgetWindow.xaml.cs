using System.Windows;
using System.Windows.Input;
using AiMeter.ViewModels;

namespace AiMeter.Views;

public partial class WidgetWindow : Window
{
    public WidgetWindow(WidgetViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
}
