using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Microsoft.Win32;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using Splat;
using SS14.Launcher.Api;
using SS14.Launcher.Localization;
using SS14.Launcher.Models;
using SS14.Launcher.Models.ContentManagement;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.ServerStatus;
using SS14.Launcher.Models.EngineManager;
using SS14.Launcher.Models.Logins;
using SS14.Launcher.Models.OverrideAssets;
using SS14.Launcher.Utility;
using TerraFX.Interop.Windows;
using LogEventLevel = Serilog.Events.LogEventLevel;

namespace SS14.Launcher;

internal static class Program
{
    private const string AuthProtocolName = "ss14-auth";

    /// <summary>
    /// Holds a pending auth code from a deep link (ss14-auth://) that will be
    /// consumed by the login UI after the app initializes.
    /// </summary>
    internal static string? PendingAuthCode { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
#if DEBUG
        Console.OutputEncoding = Encoding.UTF8;
#endif

#if USE_SYSTEM_SQLITE
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
#endif

        // --- Register the ss14-auth:// protocol handler on Windows ---
        RegisterAuthProtocol();

        var msgr = new LauncherMessaging();
        Locator.CurrentMutable.RegisterConstant(msgr);

        // Parse arguments as early as possible for launcher messaging reasons.
        string[] commands = { LauncherCommands.PingCommand };
        var commandSendAnyway = false;
        if (args.Length == 1)
        {
            // Check if this is a valid Uri, since that indicates re-invocation.
            if (Uri.TryCreate(args[0], UriKind.Absolute, out var result))
            {
                // --- INTERCEPT ss14-auth:// deep links ---
                if (result.Scheme.Equals(AuthProtocolName, StringComparison.OrdinalIgnoreCase))
                {
                    HandleAuthDeepLink(result);
                    // If the launcher is NOT already running, we continue startup.
                    // The PendingAuthCode will be picked up by LoginViewModel after init.
                    // If the launcher IS already running, we need to forward this.
                    // For now, let the new instance handle it (common for portable apps).
                    commands = new string[] { LauncherCommands.PingCommand };
                }
                else
                {
                    commands = new string[]
                        { LauncherCommands.BlankReasonCommand, LauncherCommands.ConstructConnectCommand(result) };
                    commandSendAnyway = true;
                }
            }
        }
        else if (args.Length >= 2)
        {
            if (args[0] == "--commands")
            {
                commands = new string[args.Length - 1];
                for (var i = 0; i < commands.Length; i++)
                    commands[i] = args[i + 1];
                commandSendAnyway = true;
            }
        }

        // Note: This MUST occur before we do certain actions like:
        // + Open the launcher log file (and therefore wipe a user's existing launcher log)
        // + Initialize Avalonia (and therefore waste whatever time it takes to do that)
        if (msgr.SendCommandsOrClaim(commands, commandSendAnyway))
            return;

        var logCfg = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(theme: AnsiConsoleTheme.Sixteen);

        Log.Logger = logCfg.CreateLogger();

        VcRedistCheck.Check();
        LauncherPaths.CreateDirs();

        var cfg = new DataManager();
        cfg.Load();
        Locator.CurrentMutable.RegisterConstant(cfg);

        CheckWindowsVersion();
        CheckWine(cfg);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(cfg.GetCVar(CVars.LogLauncherVerbose) ? LogEventLevel.Verbose : LogEventLevel.Debug)
            .WriteTo.Console(theme: AnsiConsoleTheme.Sixteen)
            .WriteTo.File(LauncherPaths.PathLauncherLog, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, fileSizeLimitBytes: 100L * 1024 * 1024)
            .CreateLogger();

        LauncherDiagnostics.LogDiagnostics();

        // Log if we have a pending auth code from deep link
        if (!string.IsNullOrEmpty(PendingAuthCode))
        {
            Log.Information("Pending auth code detected from deep link: {Code}...", PendingAuthCode.Substring(0, Math.Min(4, PendingAuthCode.Length)));
        }

#if DEBUG
        Logger.Sink = new AvaloniaSeriLogger(new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Area} {Level:u3}] {Message} ({SourceType} #{SourceHash})\n")
            .CreateLogger());
#endif

        try
        {
            BuildAvaloniaApp(cfg).StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Log.CloseAndFlush();
            cfg.Close();
        }
    }

    // ==========================================
    // AUTH PROTOCOL HANDLING
    // ==========================================

    /// <summary>
    /// Registers the ss14-auth:// custom URL scheme in the Windows registry (HKCU).
    /// This allows browsers to open the launcher when a ss14-auth:// link is clicked.
    /// Uses HKCU so no admin rights are needed (portable app friendly).
    /// </summary>
    private static void RegisterAuthProtocol()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                Log.Debug("Could not get exe path for protocol registration.");
                return;
            }

            // Don't re-register every single time - check if it's already pointing to us
            try
            {
                using var existingKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{AuthProtocolName}\shell\open\command", false);
                if (existingKey != null)
                {
                    var existingCmd = existingKey.GetValue("") as string;
                    if (existingCmd != null && existingCmd.Contains(exePath.Replace("\\", "\\\\")))
                    {
                        // Already registered and points to us, skip
                        return;
                    }
                }
            }
            catch
            {
                // Key doesn't exist yet, proceed to create it
            }

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{AuthProtocolName}");
            key.SetValue("", $"URL:{AuthProtocolName} Protocol");
            key.SetValue("URL Protocol", "");

            using var iconKey = key.CreateSubKey("DefaultIcon");
            iconKey.SetValue("", $"{exePath},0");

            using var commandKey = key.CreateSubKey(@"shell\open\command");
            commandKey.SetValue("", $"\"{exePath}\" \"%1\"");

            Log.Information("Registered {Protocol}:// protocol handler", AuthProtocolName);
        }
        catch (Exception e)
        {
            // Non-critical - deep links just won't work, manual code entry still functions
            Log.Warning(e, "Failed to register {Protocol}:// protocol handler (deep links won't work automatically)", AuthProtocolName);
        }
    }

    /// <summary>
    /// Parses a ss14-auth:// deep link URI and extracts the verification code.
    /// Expected format: ss14-auth://complete?code=XXXXX&uid=UUID&name=PlayerName
    /// </summary>
    private static void HandleAuthDeepLink(Uri uri)
    {
        try
        {
            // Parse query parameters from the URI
            var query = uri.Query;
            if (string.IsNullOrEmpty(query) && !string.IsNullOrEmpty(uri.OriginalString))
            {
                // Fallback: some OSes pass the full URI including fragment/query differently
                var fullUri = new Uri(uri.OriginalString);
                query = fullUri.Query;
            }

            if (!string.IsNullOrEmpty(query))
            {
                // Manual query string parsing (avoid System.Web dependency)
                var queryParams = ParseQueryString(query);

                if (queryParams.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code))
                {
                    PendingAuthCode = code.Trim();
                    Console.WriteLine($"[AUTH] Deep link received - auth code: {PendingAuthCode.Substring(0, Math.Min(4, PendingAuthCode.Length))}...");
                    return;
                }
            }

            Console.WriteLine("[AUTH] Deep link received but no 'code' parameter found in URI.");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[AUTH] Failed to parse deep link: {e.Message}");
        }
    }

    /// <summary>
    /// Simple query string parser that doesn't require System.Web.HttpUtility.
    /// </summary>
    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (query.StartsWith("?"))
            query = query.Substring(1);

        foreach (var pair in query.Split('&'))
        {
            if (string.IsNullOrEmpty(pair)) continue;

            var eqIndex = pair.IndexOf('=');
            if (eqIndex < 0) continue;

            var key = Uri.UnescapeDataString(pair.Substring(0, eqIndex));
            var value = Uri.UnescapeDataString(pair.Substring(eqIndex + 1));
            result[key] = value;
        }

        return result;
    }

    private static unsafe void CheckWindowsVersion()
    {
        // 14393 is Windows 10 version 1607, minimum we currently support.
        if (!OperatingSystem.IsWindows() || Environment.OSVersion.Version.Build >= 14393)
            return;

        var text =
            "You are using an old version of Windows that is no longer supported by Space Station 14.\n\n" +
            "If anything breaks, DO NOT ASK FOR HELP OR SUPPORT.";

        var caption = "Unsupported Windows version";

        uint type = MB.MB_OK | MB.MB_ICONWARNING;

        if (Language.UserHasLanguage("ru"))
        {
            text = "Вы используете старую версию Windows которая больше не поддерживается Space Station 14.\n\n" +
                   "При возникновении ошибок НЕ БУДЕТ ОКАЗАНО НИКАКОЙ ПОДДЕРЖКИ.";

            caption = "Неподдерживаемая версия Windows";
        }

        Helpers.MessageBoxHelper(text, caption, type);
    }

    private static unsafe void CheckBadAntivirus()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var badPrograms =
            new Dictionary<string, (string shortName, string longName)>(StringComparer.InvariantCultureIgnoreCase)
            {
                {"AvastSvc", ("Avast", "Avast Free Antivirus")},
                {"AVGSvc",   ("AVG",   "AVG Antivirus")},
            };

        var badFound = Process.GetProcesses()
            .Select(x => x.ProcessName)
            .FirstOrDefault(x => badPrograms.ContainsKey(x));

        if (badFound == null)
            return;

        var (shortName, longName) = badPrograms[badFound];

        var text = $"{longName} is detected on your system.\n\n{shortName} is known to cause the game to crash while loading. If the game fails to start, uninstall {shortName}.\n\nThis is {shortName}'s fault, do not ask us for help or support.";
        var caption = $"{longName} detected!";
        uint type = MB.MB_OK | MB.MB_ICONWARNING;

        Helpers.MessageBoxHelper(text, caption, type);
    }

    private static void CheckWine(DataManager dataManager)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (dataManager.GetCVar(CVars.WineWarningShown))
            return;

        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wine", false);

        if (key != null)
        {
            Log.Debug("Wine detected");
            var text =
                $"You seem to be running the launcher under Wine.\n\nWe recommend you run the native Linux version instead.\n\nThis is the only time you will see this message.";
            var caption = $"Wine detected!";
            uint type = MB.MB_OK | MB.MB_ICONWARNING;

            Helpers.MessageBoxHelper(text, caption, type);
            dataManager.SetCVar(CVars.WineWarningShown, true);
        }
    }

    private static AppBuilder BuildAvaloniaApp(DataManager cfg)
    {
        var locator = Locator.CurrentMutable;

        var http = HappyEyeballsHttp.CreateHttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(LauncherVersion.Name, LauncherVersion.Version?.ToString()));
        http.DefaultRequestHeaders.Add("SS14-Launcher-Fingerprint", cfg.Fingerprint.ToString());
        Locator.CurrentMutable.RegisterConstant(http);

        var loc = new LocalizationManager(cfg);
        var authApi = new AuthApi(http);
        var hubApi = new HubApi(http);
        var launcherInfo = new LauncherInfoManager(http);
        var overrideAssets = new OverrideAssetsManager(cfg, http, launcherInfo);
        var loginManager = new LoginManager(cfg, authApi);
        var engineManager = new EngineManagerDynamic();

        locator.RegisterConstant(loc);
        locator.RegisterConstant(new ContentManager());
        locator.RegisterConstant<IEngineManager>(engineManager);
        locator.RegisterConstant(new Updater());
        locator.RegisterConstant(authApi);
        locator.RegisterConstant(hubApi);
        locator.RegisterConstant(new ServerListCache());
        locator.RegisterConstant(loginManager);
        locator.RegisterConstant(overrideAssets);
        locator.RegisterConstant(launcherInfo);

        CheckLauncherArchitecture(cfg, engineManager);

        return AppBuilder.Configure(() => new App(overrideAssets))
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://SS14.Launcher/Assets/Fonts/noto_sans/*.ttf#Noto Sans"
            })
            .UseReactiveUI();
    }

    private static void CheckLauncherArchitecture(DataManager cfg, EngineManagerDynamic engineManager)
    {
        var curArchitecture = RuntimeInformation.ProcessArchitecture;
        var previousArchitecture = (Architecture)cfg.GetCVar(CVars.CurrentArchitecture);
        if (previousArchitecture == curArchitecture)
            return;

        Log.Information(
            "CPU architecture has changed since last process run, clearing engine builds. Previously: {PreviousArchitecture}, now: {CurrentArchitecture}",
            previousArchitecture,
            curArchitecture);

        engineManager.ClearAllEngines();
        cfg.SetCVar(CVars.CurrentArchitecture, (int)curArchitecture);
        cfg.CommitConfig();
    }

    internal static string? ConsumeAuthCode()
    {
        var code = PendingAuthCode;
        PendingAuthCode = null;
        return code;
    }
}
