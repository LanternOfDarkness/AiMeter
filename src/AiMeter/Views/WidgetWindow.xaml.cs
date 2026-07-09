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

        // SizeToContent means the window is still small (no metrics loaded yet) the
        // first time it's shown, then grows once data/format changes arrive - so the
        // bottom-right anchor has to be recomputed on every size change, not just once.
        Loaded += (s, e) => AnchorToBottomRight();
        SizeChanged += (s, e) => AnchorToBottomRight();
    }

    private void AnchorToBottomRight()
    {
        var workArea = System.Windows.SystemParameters.WorkArea;
        this.Left = workArea.Right - this.ActualWidth - 20;
        this.Top = workArea.Bottom - this.ActualHeight - 20;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
}
