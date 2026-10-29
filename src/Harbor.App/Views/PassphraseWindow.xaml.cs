using System.Windows;

namespace Harbor.App.Views;

public partial class PassphraseWindow : Window
{
    private const int MinLength = 12;
    private readonly bool _isNew;

    public PassphraseWindow(bool isNewVault)
    {
        InitializeComponent();
        _isNew = isNewVault;
        Prompt.Text = isNewVault
            ? $"This shared database has no vault key yet. Choose a passphrase (at least {MinLength} characters) and share it " +
              "securely with your team. Each user enters it once; afterwards it is cached for their Windows account on their machine."
            : "Enter the shared vault passphrase. It is needed once on this machine; afterwards the key is cached for your Windows account.";
        ConfirmLabel.Visibility = Second.Visibility = isNewVault ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => First.Focus();
    }

    public string? Passphrase { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_isNew && First.Password.Length < MinLength)
        {
            ErrorText.Text = $"Use at least {MinLength} characters.";
            return;
        }

        if (_isNew && First.Password != Second.Password)
        {
            ErrorText.Text = "The passphrases do not match.";
            return;
        }

        Passphrase = First.Password;
        DialogResult = true;
    }
}
