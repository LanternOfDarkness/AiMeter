using System.Windows;
using AiMeter.Interop;
using AiMeter.Services;

namespace AiMeter.Views;

/// <summary>
/// Collects the OpenAI Platform API key. The OpenAI provider authenticates by key rather than a
/// browser session, so this replaces the WebView2 login the other providers use with a simple
/// masked-entry dialog. Shown from the Settings ▸ Accounts row via the same factory pattern as the
/// browser auth windows; on Save it hands the key to <see cref="IOpenAiSession"/> (which encrypts it).
/// </summary>
public partial class OpenAiAuthWindow : Window
{
    private readonly IOpenAiSession _session;

    public OpenAiAuthWindow(IOpenAiSession session)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _session = session;
        keyBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var key = keyBox.Password?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            errorText.Text = "Please paste an API key, or press Cancel.";
            errorText.Visibility = Visibility.Visible;
            return;
        }

        _session.Store(key);
        this.Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => this.Close();
}
