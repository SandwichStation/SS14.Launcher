namespace SS14.Launcher.Utility;

/// <summary>
/// Stores command-line arguments passed during application startup,
/// particularly for handling deep link authentication callbacks.
/// </summary>
public static class LauncherArgs
{
    /// <summary>
    /// Auth callback URL if the app was launched via ss14-auth:// deep link.
    /// </summary>
    public static string? AuthCallbackUrl { get; set; }

    /// <summary>
    /// Clears stored values after they've been processed.
    /// </summary>
    public static void Clear() => AuthCallbackUrl = null;
}
