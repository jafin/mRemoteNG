using System.Configuration;
using System.Runtime.Versioning;

namespace mRemoteNG.Config.Settings.Providers;

/// <summary>
/// Forces all Properties settings classes onto one settings provider, chosen from the edition.
/// This bypasses SettingsProviderAttribute which may fail silently in .NET Core/5+/10 due to
/// AssemblyQualifiedName resolution issues.
/// Must be called BEFORE any settings class .Default property is accessed.
/// </summary>
/// <remarks>
/// <b>This is where the edition stops being a compile-time property.</b> The provider used to be
/// selected by which base class <see cref="ChooseProvider"/> inherited, which is a decision only a
/// compiler can make. Selecting the instance here instead is what allows
/// <see cref="App.Info.PortableEdition"/> to answer at runtime, and it works because this already
/// ran before anything read a setting — the wiring it does was always runtime wiring.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class PortableSettingsInitializer
{
    private static bool _initialized;
    private static SettingsProvider? _sharedProvider;

    internal static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        _sharedProvider = App.Runtime.IsPortableEdition
            ? new PortableSettingsProvider()
            : new ChooseProvider();
        // Its own Name, as before. PortableSettingsProvider overrides Name with a constant and
        // ignores what it is initialised with, so passing anything else would name it one thing and
        // register it under another — and WireProvider looks it up by Name.
        _sharedProvider.Initialize(_sharedProvider.Name, null!);

        WireProvider(Properties.Settings.Default);
        WireProvider(Properties.App.Default);
        WireProvider(Properties.AppUI.Default);
        WireProvider(Properties.OptionsAdvancedPage.Default);
        WireProvider(Properties.OptionsAppearancePage.Default);
        WireProvider(Properties.OptionsBackupPage.Default);
        WireProvider(Properties.OptionsConnectionsPage.Default);
        WireProvider(Properties.OptionsCredentialsPage.Default);
        WireProvider(Properties.OptionsDBsPage.Default);
        WireProvider(Properties.OptionsGoogleDrivePage.Default);
        WireProvider(Properties.OptionsNotificationsPage.Default);
        WireProvider(Properties.OptionsRbac.Default);
        WireProvider(Properties.OptionsSecurityPage.Default);
        WireProvider(Properties.OptionsStartupExitPage.Default);
        WireProvider(Properties.OptionsTabsPanelsPage.Default);
        WireProvider(Properties.OptionsThemePage.Default);
        WireProvider(Properties.OptionsUpdatesPage.Default);
    }

    private static void WireProvider(ApplicationSettingsBase settings)
    {
        if (_sharedProvider == null) return;

        if (settings.Providers[_sharedProvider.Name] == null)
            settings.Providers.Add(_sharedProvider);

        foreach (SettingsProperty prop in settings.Properties)
        {
            prop.Provider = _sharedProvider;
        }
    }
}