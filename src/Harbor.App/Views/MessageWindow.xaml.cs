using System.Windows;

namespace Harbor.App.Views;

/// <summary>What a <see cref="MessageWindow"/> asks or reports. SPEC FR-53</summary>
public enum MessageKind
{
    /// <summary>A question; the confirm button is the default (Enter).</summary>
    Question,

    /// <summary>A question whose "yes" has consequences (delete, take over a session, production): Cancel is the default.</summary>
    Risky,

    /// <summary>A problem to report; one OK button.</summary>
    Error,
}

/// <summary>The app's own message box (SPEC FR-53, ADR-0030), styled like the rest of the app.</summary>
public partial class MessageWindow : Window
{
    public MessageWindow(string title, string message, MessageKind kind, string confirmText = "Yes", string cancelText = "No")
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        Message.Text = message;

        (IconGlyph.Text, IconBadge.Background, IconGlyph.Foreground) = kind switch
        {
            MessageKind.Error => ("", Brush("Harbor.Env.Production.BackgroundBrush"), Brush("Harbor.Env.Production.ForegroundBrush")),
            MessageKind.Risky => ("", Brush("Harbor.InUseBackgroundBrush"), Brush("Harbor.InUseTextBrush")),
            _ => ("", Brush("Harbor.Env.Test.BackgroundBrush"), Brush("Harbor.Env.Test.ForegroundBrush")),
        };

        if (kind == MessageKind.Error)
        {
            ConfirmButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "OK";
            CancelButton.IsDefault = true;
            CancelButton.Style = (Style)FindResource("AccentButtonStyle");
            Loaded += (_, _) => CancelButton.Focus();
            return;
        }

        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
        // Risky: Enter (and Esc) choose Cancel; the action needs a deliberate click, Tab + Space or its access key.
        var safe = kind == MessageKind.Risky;
        ConfirmButton.IsDefault = !safe;
        CancelButton.IsDefault = safe;
        // The accent marks what Enter does.
        (safe ? CancelButton : ConfirmButton).Style = (Style)FindResource("AccentButtonStyle");
        Loaded += (_, _) => (safe ? CancelButton : ConfirmButton).Focus();
    }

    private System.Windows.Media.Brush Brush(string key) => (System.Windows.Media.Brush)FindResource(key);

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>Shows the message over <paramref name="owner"/>; <c>true</c> when the confirm button was chosen.</summary>
    public static bool Show(Window? owner, string title, string message, MessageKind kind, string confirmText = "Yes", string cancelText = "No")
    {
        var window = new MessageWindow(title, message, kind, confirmText, cancelText);
        if (owner is { IsLoaded: true }) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window.ShowDialog() == true;
    }
}
