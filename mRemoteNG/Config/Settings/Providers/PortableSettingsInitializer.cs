using System.Configuration;
using System.Runtime.Versioning;

namespace mRemoteNG.Config.Settings.Providers;

/// <summary>
/// Forces all Properties settings classes onto one settings provider.
/// This bypasses SettingsProviderAttribute which may fail silently in .NET Core/5+/10 due to
/// AssemblyQualifiedName resolution issues.
/// Must be called BEFORE any settings class .Default property is accessed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same provider for both editions, and not by accident.</b>
/// <see cref="PortableSettingsProvider"/> is misnamed: it is a file-based provider that writes to
/// <c>SettingsFileInfo.SettingsPath</c>, and <i>that</i> is what follows the edition — beside the
/// executable when portable, under <c>%APPDATA%</c> (or <c>--cfg</c>) when installed. So the
/// edition already decides where settings live without the provider needing to know about it.
/// </para>
/// <para>
/// <b>Do not make this pick <see cref="LocalFileSettingsProvider"/> for the installed edition.</b>
/// It was tried and it silently persists nothing: no <c>user.config</c> is written and every
/// setting comes back at its default on the next start — which shows up as the update-settings
/// prompt reappearing at every launch however it is answered. That path had never run in this
/// application's history, because every build configuration defined <c>PORTABLE</c>, so the first
/// build that could reach it was also the first to be broken by it.
/// </para>
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

        _sharedProvider = CreateProvider();
        // Its own Name. PortableSettingsProvider overrides Name with a constant and ignores what it
        // is initialised with, so passing anything else would name it one thing and register it
        // under another — and WireProvider looks it up by Name.
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

    /// <summary>
    /// The provider every settings class is wired to. Both editions get the file-based one; where
    /// the file lands is <c>SettingsFileInfo.SettingsPath</c>'s decision, and it is edition-aware.
    /// </summary>
    /// <remarks>
    /// Exposed to tests so the choice can be asserted rather than discovered by a user finding the
    /// same prompt every time they start the application.
    /// </remarks>
    internal static SettingsProvider CreateProvider() => new PortableSettingsProvider();

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