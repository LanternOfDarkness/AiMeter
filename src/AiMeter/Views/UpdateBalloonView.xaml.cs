using System.Windows.Controls;

namespace AiMeter.Views;

/// <summary>
/// The tray balloon card. A fresh instance must be created for every show (H.NotifyIcon
/// rejects re-parenting an existing control).
/// </summary>
public partial class UpdateBalloonView : UserControl
{
    public UpdateBalloonView()
    {
        InitializeComponent();
    }
}
