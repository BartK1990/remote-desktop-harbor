using System.Windows;
using Harbor.Core.Backup;

namespace Harbor.App.Views;

public partial class PassphraseWindow : Window
{
    private const int MinVaultLength = 12;
    private readonly bool _isNew;
    private readonly int _minLength;

    /// <summary>The shared vault passphrase (ADR-0006).</summary>
    public PassphraseWindow(bool isNewVault)
        : this("Shared vault passphrase",
            isNewVault
                ? $"This shared database has no vault key yet. Choose a passphrase (at least {MinVaultLength} characters) and share it " +
                  "securely with your team. Each user enters it once; afterwards it is cached for their Windows account on their machine."
                : "Enter the shared vault passphrase. It is needed once on this machine; afterwards the key is cached for your Windows account.",
            isNewVault, MinVaultLength)
    {
    }

    private PassphraseWindow(string title, string prompt, bool isNew, int minLength)
    {
        InitializeComponent();
        Title = title;
        _isNew = isNew;
        _minLength = minLength;
        Prompt.Text = prompt;
        ConfirmLabel.Visibility = Second.Visibility = isNew ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => First.Focus();
    }

    /// <summary>The passphrase of a backup file (SPEC FR-68, ADR-0046): a new one for an export, or the one to restore with.</summary>
    public static PassphraseWindow ForBackup(bool isNew) => new(
        isNew ? "Backup passphrase" : "Restore backup",
        isNew
            ? $"Choose a passphrase for the backup (at least {BackupFile.MinPassphraseLength} characters). It encrypts the whole file, " +
              "passwords included, and is needed to restore it on any PC. It cannot be recovered: keep it somewhere safe."
            : "Enter the passphrase the backup was saved with.",
        isNew, BackupFile.MinPassphraseLength);

    public string? Passphrase { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_isNew && First.Password.Length < _minLength)
        {
            ErrorText.Text = $"Use at least {_minLength} characters.";
            return;
        }

        if (_isNew && First.Password != Second.Password)
        {
            ErrorText.Text = "The passphrases do not match.";
            return;
        }

        if (First.Password.Length == 0)
        {
            ErrorText.Text = "Enter the passphrase.";
            return;
        }

        Passphrase = First.Password;
        DialogResult = true;
    }
}
