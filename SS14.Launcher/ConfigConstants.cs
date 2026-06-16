using System;
using SS14.Launcher.Utility;

namespace SS14.Launcher;

public static class ConfigConstants
{
    public const string CurrentLauncherVersion = "S1";
    public static readonly bool DoVersionCheck = false;

    // Refresh login tokens if they're within <this much> of expiry.
    public static readonly TimeSpan TokenRefreshThreshold = TimeSpan.FromDays(15);

    // If the user leaves the launcher running for absolute ages, this is how often we'll update his login tokens.
    public static readonly TimeSpan TokenRefreshInterval = TimeSpan.FromDays(7);

    // The amount of time before a server is considered timed out for status checks.
    public static readonly TimeSpan ServerStatusTimeout = TimeSpan.FromSeconds(5);

    // Check the command queue this often.
    public static readonly TimeSpan CommandQueueCheckInterval = TimeSpan.FromSeconds(1);

    public const string LauncherCommandsNamedPipeName = "SS14.Launcher.CommandPipe";
    // Amount of time to wait before the launcher decides to ignore named pipes entirely to keep the rest of the launcher functional.
    public const int LauncherCommandsNamedPipeTimeout = 150;
    // Amount of time to wait to let a redialling client properly die
    public const int LauncherCommandsRedialWaitTimeout = 1000;

    private static readonly UrlFallbackSetStats StatsHubInfra = new(2);

    public static readonly UrlFallbackSet AuthUrl = new(["https://auth.playss14.com/", "https://auth.fallback.playss14.com/"], StatsHubInfra);
    public static readonly UrlFallbackSet[] DefaultHubUrls = [new(["https://hub.sandwich14.com/", "https://backuphub.sandwich14.com/"], StatsHubInfra)];
    public const string DiscordUrl = "https://discord.sandwich14.com";
    public const string AccountBaseUrl = "https://account.playss14.com/Identity/Account/";
    public const string AccountManagementUrl = $"{AccountBaseUrl}Manage";
    public const string AccountRegisterUrl = $"{AccountBaseUrl}Register";
    public const string AccountResendConfirmationUrl = $"{AccountBaseUrl}ResendEmailConfirmation";
    public const string WebsiteUrl = "https://github.com/SandwichStation";
    public const string DownloadUrl = "https://github.com/SandwichStation/SS14.Launcher/releases/";
    //public const string NewsFeedUrl = "https://playss14.com/post/index.xml";
    //public const string TranslateUrl = "https://docs.playss14.com/en/general-development/contributing-translations.html";

    private static readonly UrlFallbackSet RobustBuildsBaseUrl = new([
        "https://robust-builds.cdn.spacestation14.com/",
        "https://robust-builds.fallback.cdn.spacestation14.com/"
    ]);

    private static readonly UrlFallbackSet LauncherDataBaseUrl = new([
        "https://hub.sandwich14.com/",
        "https://backuphub.sandwich14.com/"
    ]);

    public static readonly UrlFallbackSet RobustBuildsManifest = RobustBuildsBaseUrl + "manifest.json";
    public static readonly UrlFallbackSet RobustModulesManifest = RobustBuildsBaseUrl + "modules.json";

    // How long to keep cached copies of Robust manifests.
    // TODO: Take this from Cache-Control header responses instead.
    public static readonly TimeSpan RobustManifestCacheTime = TimeSpan.FromMinutes(15);

    public static readonly UrlFallbackSet UrlLauncherInfo = LauncherDataBaseUrl + "info.json";
    public static readonly UrlFallbackSet UrlAssetsBase = LauncherDataBaseUrl + "assets/";

    public const string FallbackUsername = "PlainBread";

    static ConfigConstants()
    {
        var envVarAuthUrl = Environment.GetEnvironmentVariable("SS14_LAUNCHER_OVERRIDE_AUTH");
        if (!string.IsNullOrEmpty(envVarAuthUrl))
            AuthUrl = new UrlFallbackSet([envVarAuthUrl]);
    }
}
