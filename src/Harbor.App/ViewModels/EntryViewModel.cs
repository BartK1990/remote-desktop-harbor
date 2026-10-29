using CommunityToolkit.Mvvm.ComponentModel;
using Harbor.Core.Model;

namespace Harbor.App.ViewModels;

public sealed partial class EntryViewModel(ConnectionEntry model) : ObservableObject
{
    public const string NoFolder = "(No folder)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(GroupName), nameof(Subtitle), nameof(KindLabel), nameof(HasPassword), nameof(CanLaunch))]
    private ConnectionEntry _model = model;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInUse), nameof(PresenceText), nameof(PresenceDetails))]
    private IReadOnlyList<ActiveSession> _activeSessions = [];

    public string Name => Model.Name;
    public string GroupName => string.IsNullOrWhiteSpace(Model.Folder) ? NoFolder : Model.Folder;
    public bool HasPassword => Model.HasPassword;
    public bool CanLaunch => Model.CanLaunch;
    public string KindLabel => Model.Kind == EntryKind.RdpConnection ? "RDP connection" : "Credentials only";

    public string Subtitle
    {
        get
        {
            var host = Model.Host is null ? null : Model.Port == ConnectionEntry.DefaultRdpPort ? Model.Host : $"{Model.Host}:{Model.Port}";
            return (Model.QualifiedUserName, host) switch
            {
                (null, null) => KindLabel,
                (null, { } h) => h,
                ({ } u, null) => u,
                ({ } u, { } h) => $"{u} @ {h}",
            };
        }
    }

    public bool IsInUse => ActiveSessions.Count > 0;

    public string PresenceText => ActiveSessions.Count switch
    {
        0 => "",
        1 => $"In use by {ActiveSessions[0].DisplayName ?? ActiveSessions[0].UserName}",
        var n => $"In use by {n} users",
    };

    public string PresenceDetails => string.Join(Environment.NewLine, ActiveSessions.Select(s =>
        $"{s.Describe()} on {s.MachineName}, since {s.StartedUtc.ToLocalTime():t}"));

    public bool Matches(string filter) =>
        string.IsNullOrWhiteSpace(filter)
        || Contains(Model.Name, filter) || Contains(Model.Host, filter) || Contains(Model.UserName, filter)
        || Contains(Model.Folder, filter) || Contains(Model.Notes, filter);

    private static bool Contains(string? value, string filter) =>
        value is not null && value.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
}
