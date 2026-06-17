using System;
using System.Threading.Tasks;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using SS14.Launcher.Api;
using SS14.Launcher.Localization;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.Logins;
using SS14.Launcher.ViewModels.Login;

namespace SS14.Launcher.ViewModels.Login;

public class LoginViewModel : BaseLoginViewModel
{
    private readonly AuthApi _authApi;
    private readonly LoginManager _loginMgr;
    private readonly DataManager _dataManager;
    private readonly LocalizationManager _loc = LocalizationManager.Instance;

    [Reactive] public string VerificationCode { get; set; } = "";
    [Reactive] public bool IsCodeValid { get; private set; }

    [Reactive] public string StatusMessage { get; set; } = "";
    [Reactive] public bool ShowStatusError { get; set; }

    public LoginViewModel(MainWindowLoginViewModel parentVm, AuthApi authApi,
        LoginManager loginMgr, DataManager dataManager) : base(parentVm)
    {
        BusyText = _loc.GetString("login-linking-busy");
        _authApi = authApi;
        _loginMgr = loginMgr;
        _dataManager = dataManager;

        this.WhenAnyValue(x => x.VerificationCode)
            .Subscribe(s =>
            {
                IsCodeValid = !string.IsNullOrWhiteSpace(s) && s.Length >= 4;
            });
    }

    public async void OnLinkButtonPressed()
    {
        if (!IsCodeValid || Busy)
        {
            return;
        }

        Busy = true;
        ShowStatusError = false;
        StatusMessage = _loc.GetString("login-status-exchanging-token");

        try
        {
            var resp = await _authApi.GetLauncherTokenAsync(VerificationCode);

            if (resp.IsSuccess)
            {
                var loginInfo = resp.LoginInfo;

                // Check if we already have this login
                var existing = _loginMgr.Logins.Lookup(loginInfo.UserId);
                if (existing.HasValue)
                {
                    await _authApi.LogoutTokenAsync(existing.Value.LoginInfo.Token.Token);
                    _loginMgr.UpdateToNewToken(existing.Value, loginInfo.Token);  // Use the actual looked-up account
                }
                else
                {
                    _loginMgr.AddFreshLogin(loginInfo);
                }

                _loginMgr.ActiveAccountId = loginInfo.UserId;
                _dataManager.CommitConfig();

                StatusMessage = _loc.GetString("login-status-success");
                ShowStatusError = false;

                ParentVM.SwitchToMain();
            }
            else
            {
                ShowStatusError = true;
                StatusMessage = string.Join("\n", resp.Errors);
                Log.Error("Login failed: {Errors}", resp.Errors);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Unexpected error during token exchange");
            ShowStatusError = true;
            StatusMessage = _loc.GetString("login-error-network");
        }
        finally
        {
            Busy = false;
        }
    }

    public void OpenWebLinker()
    {
        // Open the web dashboard where users link their SS14 and Discord accounts
        Helpers.OpenUri(ConfigConstants.AccountManagementUrl);

        StatusMessage = _loc.GetString("login-status-web-opened");
    }

    public void OnLogInButtonPressed()
    {
        // In the new unified auth flow, pressing Enter in the login view 
        // should trigger the link button (verification code exchange)
        OnLinkButtonPressed();
    }

    public static async Task DoLogin(BaseLoginViewModel sourceVm, AuthApi.AuthenticateRequest request, 
        AuthApi.AuthenticateResult authResult, LoginManager loginMgr, AuthApi authApi)
    {
        if (!authResult.IsSuccess)
        {
            var errors = authResult.Errors;
            sourceVm.OverlayControl = new AuthErrorsOverlayViewModel(sourceVm, "Login failed", errors);
            return;
        }

        var loginInfo = authResult.LoginInfo;

        var existing = loginMgr.Logins.Lookup(loginInfo.UserId);
        if (existing.HasValue)
        {
            await authApi.LogoutTokenAsync(existing.Value.LoginInfo.Token.Token);
            loginMgr.UpdateToNewToken(existing.Value, loginInfo.Token);
        }
        else
        {

            loginMgr.AddFreshLogin(loginInfo);
        }

        loginMgr.ActiveAccountId = loginInfo.UserId;


        sourceVm.ParentVM.SwitchToMain();
    }


}
