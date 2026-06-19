using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using DynamicData;
using ReactiveUI;
using Serilog;
using Splat;
using SS14.Launcher.Api;
using SS14.Launcher.Models.Data;

namespace SS14.Launcher.Models.Logins;

// This class manages complex logic like token refreshing and status tracking.
public sealed class LoginManager : ReactiveObject
{
    private readonly DataManager _cfg;
    private readonly AuthApi _authApi;
    private IDisposable? _timer;
    private Guid? _activeLoginId;
    private readonly IObservableCache<ActiveLoginData, Guid> _logins;

    public Guid? ActiveAccountId
    {
        get => _activeLoginId;
        set
        {
            if (value != null)
            {
                var lookup = _logins.Lookup(value.Value);
                if (!lookup.HasValue)
                    throw new ArgumentException("We do not have a login with that ID.");
            }
            this.RaiseAndSetIfChanged(ref _activeLoginId, value);
            this.RaisePropertyChanged(nameof(ActiveAccount));
            _cfg.SelectedLoginId = value;
        }
    }

    public LoggedInAccount? ActiveAccount
    {
        get => _activeLoginId == null ? null : _logins.Lookup(_activeLoginId.Value).Value;
        set => ActiveAccountId = value?.UserId;
    }

    public IObservableCache<LoggedInAccount, Guid> Logins { get; }

    public LoginManager(DataManager cfg, AuthApi authApi)
    {
        _cfg = cfg;
        _authApi = authApi;

        _logins = _cfg.Logins
            .Connect()
            .Transform(p => new ActiveLoginData(p))
            .OnItemRemoved(p =>
            {
                if (p.LoginInfo.UserId == _activeLoginId)
                    ActiveAccount = null;
            })
            .AsObservableCache();

        Logins = _logins
            .Connect()
            .Transform((data, guid) => (LoggedInAccount)data)
            .AsObservableCache();
    }

    public async Task Initialize()
    {
        _timer = DispatcherTimer.Run(() =>
        {
            _ = RefreshAllTokens();
            return true;
        }, ConfigConstants.TokenRefreshInterval, DispatcherPriority.Background);

        await RefreshAllTokens();
    }

    private async Task RefreshAllTokens()
    {
        Log.Debug("Refreshing all tokens.");
        const int delayStart = 2;
        const int delayValue = 200;

        await Task.WhenAll(_logins.Items.Select(async (l, i) =>
        {
            if (i > delayStart) await Task.Delay(delayValue * (i - delayStart));

            try { await UpdateSingleAccountStatus(l); }
            catch (AuthApiException e)
            {
                Log.Warning(e, "AuthApiException while trying to refresh token for {login}", l.LoginInfo);
            }
        }));
    }

    public void AddFreshLogin(LoginInfo info)
    {
        _cfg.AddLogin(info);
        _logins.Lookup(info.UserId).Value.SetStatus(AccountLoginStatus.Available);
    }

    public void UpdateToNewToken(LoggedInAccount account, LoginToken token)
    {
        var cast = (ActiveLoginData)account;
        cast.SetStatus(AccountLoginStatus.Available);

        var updatedInfo = new LoginInfo
        {
            UserId = account.LoginInfo.UserId,
            Username = account.LoginInfo.Username,
            Token = token
        };

        _cfg.UpdateLogin(updatedInfo);

        Log.Debug("Refreshed token saved for {UserId}", account.LoginInfo.UserId);
    }

    public Task UpdateSingleAccountStatus(LoggedInAccount account)
    {
        return UpdateSingleAccountStatus((ActiveLoginData)account);
    }

    private async Task UpdateSingleAccountStatus(ActiveLoginData data)
    {
        if (data.LoginInfo.Token.ShouldRefresh() || data.Status == AccountLoginStatus.Expired)
        {
            var newTokenHopefully = await _authApi.RefreshTokenAsync(data.LoginInfo.Token.Token);
            if (newTokenHopefully == null)
            {
                data.SetStatus(AccountLoginStatus.Expired);
            }
            else
            {
                data.LoginInfo.Token = newTokenHopefully.Value;
                data.SetStatus(AccountLoginStatus.Available);
                _cfg.CommitConfig();
            }
        }
        else if (data.Status == AccountLoginStatus.Unsure)
        {
            var valid = await _authApi.CheckTokenAsync(data.LoginInfo.Token.Token);
            data.SetStatus(valid ? AccountLoginStatus.Available : AccountLoginStatus.Expired);
        }
    }

    public async Task<LauncherTokenExchangeResult> ExchangeLauncherTokenAsync(string verificationCode)
    {
        if (string.IsNullOrWhiteSpace(verificationCode))
            return new LauncherTokenExchangeResult(new[] { "Verification code cannot be empty." });

        var result = await _authApi.GetLauncherTokenAsync(verificationCode.Trim());

        if (!result.IsSuccess)
            return new LauncherTokenExchangeResult(result.Errors);

        var loginInfo = result.LoginInfo;

        // FIX: Simply add or update — DataManager.AddLogin now uses AddOrUpdate
        _cfg.AddLogin(loginInfo);

        // Update the local ActiveLoginData status if it exists in our cache
        var existing = _logins.Lookup(loginInfo.UserId);
        if (existing.HasValue)
        {
            existing.Value.SetStatus(AccountLoginStatus.Available);
        }

        ActiveAccountId = loginInfo.UserId;
        _cfg.CommitConfig();

        Log.Information("Successfully authenticated via unified ID. User: {User}, ID: {Id}",
            loginInfo.Username, loginInfo.UserId);

        return new LauncherTokenExchangeResult(loginInfo);
    }

    // Result Class (No inheritance issues)
    public class LauncherTokenExchangeResult
    {
        public bool IsSuccess { get; }
        public LoginInfo Info { get; }
        public string[] Errors { get; }

        public LauncherTokenExchangeResult(LoginInfo info)
        {
            IsSuccess = true;
            Info = info;
            Errors = Array.Empty<string>();
        }

        public LauncherTokenExchangeResult(string[] errors)
        {
            IsSuccess = false;
            Info = null!;
            Errors = errors;
        }
    }

    private sealed class ActiveLoginData : LoggedInAccount
    {
        private AccountLoginStatus _status;
        public ActiveLoginData(LoginInfo info) : base(info) { }
        public override AccountLoginStatus Status => _status;

        public void SetStatus(AccountLoginStatus status)
        {
            this.RaiseAndSetIfChanged(ref _status, status, nameof(Status));
            Log.Debug("Setting status for login {account} to {status}", LoginInfo, status);
        }
    }
}
