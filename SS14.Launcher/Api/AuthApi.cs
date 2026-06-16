using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Serilog;
using SS14.Launcher.Models;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Utility;

namespace SS14.Launcher.Api;

public sealed class AuthApi
{
    private readonly HttpClient _httpClient;

    public AuthApi(HttpClient http)
    {
        _httpClient = http;
    }

    public async Task<AuthenticateResult> AuthenticateAsync(AuthenticateRequest request)
    {
        try
        {
            var authUrl = ConfigConstants.AuthUrl + "api/auth/authenticate";

            using var resp = await _httpClient.PostAsJsonAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                var respJson = await resp.Content.AsJson<AuthenticateResponse>();
                return new AuthenticateResult(new LoginInfo
                {
                    UserId = respJson.UserId,
                    Token = new LoginToken(respJson.Token, respJson.ExpireTime),
                    Username = respJson.Username
                });
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Login failure.
                var respJson = await resp.Content.AsJson<AuthenticateDenyResponse>();
                return new AuthenticateResult(respJson.Errors, respJson.Code);
            }

            Log.Error("Server returned unexpected HTTP status code: {responseCode}", resp.StatusCode);
            return new AuthenticateResult(
                new[] { "Server returned unknown error" },
                AuthenticateDenyResponseCode.UnknownError);
        }
        catch (Exception e)
        {
            Log.Error(e, "Exception in AuthenticateAsync");
            return new AuthenticateResult(
                new[] { "Connection error" },
                AuthenticateDenyResponseCode.UnknownError);
        }
    }

    public async Task<RegisterResult> RegisterAsync(string username, string email, string password)
    {
        try
        {
            var request = new RegisterRequest(username, email, password);
            var authUrl = ConfigConstants.AuthUrl + "api/auth/register";

            using var resp = await _httpClient.PostAsJsonAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                var respJson = await resp.Content.AsJson<RegisterResponse>();
                return new RegisterResult(respJson.Status);
            }

            if (resp.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // Register failure.
                var respJson = await resp.Content.AsJson<RegisterResponseError>();
                return new RegisterResult(respJson.Errors);
            }

            return new RegisterResult(new[] { "Server returned unknown error" });
        }
        catch (Exception e)
        {
            Log.Error(e, "Exception in RegisterAsync");
            return new RegisterResult(new[] { "Connection error" });
        }
    }

    public async Task<string[]?> ForgotPasswordAsync(string email)
    {
        try
        {
            var request = new ResetPasswordRequest(email);
            var authUrl = ConfigConstants.AuthUrl + "api/auth/resetPassword";
            using var resp = await _httpClient.PostAsJsonAsync(authUrl, request);
            return resp.IsSuccessStatusCode ? null : new[] { "Server returned unknown error" };
        }
        catch (Exception e)
        {
            Log.Error(e, "Exception in ForgotPasswordAsync");
            return new[] { "Connection error" };
        }
    }

    public async Task<string[]?> ResendConfirmationAsync(string email)
    {
        try
        {
            var request = new ResendConfirmationRequest(email);
            var authUrl = ConfigConstants.AuthUrl + "api/auth/resendConfirmation";
            using var resp = await _httpClient.PostAsJsonAsync(authUrl, request);
            return resp.IsSuccessStatusCode ? null : new[] { "Server returned unknown error" };
        }
        catch (Exception e)
        {
            Log.Error(e, "Exception in ResendConfirmationAsync");
            return new[] { "Connection error" };
        }
    }

    public async Task<LoginToken?> RefreshTokenAsync(string token)
    {
        try
        {
            var request = new RefreshRequest(token);
            var authUrl = ConfigConstants.AuthUrl + "api/auth/refresh";

            using var resp = await _httpClient.PostAsJsonAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                var response = await resp.Content.AsJson<RefreshResponse>();
                return new LoginToken(response.NewToken, response.ExpireTime);
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                Log.Warning("Token expired during refresh.");
                return null;
            }

            Log.Error("Unexpected status on refresh: {Status}", resp.StatusCode);
            throw new AuthApiException($"Unexpected status: {resp.StatusCode}");
        }
        catch (HttpRequestException e)
        {
            Log.Error(e, "Http error in RefreshTokenAsync");
            HttpSelfTest.StartSelfTest();
            throw new AuthApiException("Connection error", e);
        }
    }

    public async Task LogoutTokenAsync(string token)
    {
        try
        {
            var request = new LogoutRequest(token);
            var authUrl = ConfigConstants.AuthUrl + "api/auth/logout";
            using var resp = await _httpClient.PostAsJsonAsync(authUrl, request);
        }
        catch (Exception e)
        {
            Log.Error(e, "Exception in LogoutTokenAsync");
        }
    }

    public async Task<bool> CheckTokenAsync(string token)
    {
        try
        {
            var authUrl = ConfigConstants.AuthUrl.GetMostSuccessfulUrl() + "api/auth/ping";
            var request = new HttpRequestMessage(HttpMethod.Get, authUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("SS14Auth", token);

            using var resp = await _httpClient.SendAsync(request);

            if (resp.IsSuccessStatusCode) return true;
            if (resp.StatusCode == HttpStatusCode.Unauthorized) return false;

            Log.Error("Unexpected status on ping: {Status}", resp.StatusCode);
            throw new AuthApiException($"Unexpected status: {resp.StatusCode}");
        }
        catch (HttpRequestException e)
        {
            Log.Error(e, "Http error in CheckTokenAsync");
            HttpSelfTest.StartSelfTest();
            throw new AuthApiException("Connection error", e);
        }
    }


    // =================
    //  Unified ID Auth
    // =================

    public async Task<LauncherTokenResult> GetLauncherTokenAsync(string verificationCode)
    {
        try
        {
            var request = new { code = verificationCode };
            var authUrl = ConfigConstants.AuthUrl + "api/launcher/authenticate-with-code";

            using var resp = await _httpClient.PostAsJsonAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                var respJson = await resp.Content.AsJson<LauncherTokenResponse>();

                // Log the raw response for debugging
                Log.Information("GetLauncherTokenAsync success response: {@Response}", respJson);

                // Validate response data before using - log what we got for debugging
                if (string.IsNullOrEmpty(respJson?.UserId))
                {
                    Log.Warning("Server response missing UserId. Response: {@Response}", respJson);
                }
                if (string.IsNullOrEmpty(respJson?.Token))
                {
                    Log.Warning("Server response missing Token. Response: {@Response}", respJson);
                }
                if (string.IsNullOrEmpty(respJson?.Username))
                {
                    Log.Information("Server response missing Username (using fallback). Response: {@Response}", respJson);
                }

                if (string.IsNullOrEmpty(respJson?.UserId) || string.IsNullOrEmpty(respJson?.Token))
                {
                    Log.Error("Invalid launcher token response: missing required fields (UserId or Token). Full response: {@Response}", respJson);
                    return new LauncherTokenResult(new[] { "Server returned invalid response." });
                }

                return new LauncherTokenResult(new LoginInfo
                {
                    UserId = Guid.Parse(respJson.UserId),
                    Username = respJson.Username ?? "User",
                    Token = new LoginToken(respJson.Token, respJson.ExpireTime ?? DateTimeOffset.MaxValue)
                });
            }

            if (resp.StatusCode == HttpStatusCode.Forbidden)
            {
                return new LauncherTokenResult(new[] { "Accounts linked but not unified." });
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.BadRequest)
            {
                var respJson = await resp.Content.AsJson<ErrorResponse>();
                return new LauncherTokenResult(new[] { respJson?.Error ?? "Invalid or expired code." });
            }

            Log.Error("Server returned unexpected status: {Status}", resp.StatusCode);
            return new LauncherTokenResult(new[] { "Server error while fetching token." });
        }
        catch (Exception e)
        {
            Log.Error(e, "Exception in GetLauncherTokenAsync");
            return new LauncherTokenResult(new[] { $"Connection error: {e.Message}" });
        }
    }

    public sealed record AuthenticateResponse(string Token, string Username, Guid UserId, DateTimeOffset ExpireTime);


    public enum AuthenticateDenyResponseCode
    {
        None = 0,
        InvalidCredentials = 1,
        AccountUnconfirmed = 2,
        TfaRequired = 3,
        TfaInvalid = 4,
        AccountLocked = 5,
        UnknownError = -1
    }

    public sealed record AuthenticateRequest(string? Username, Guid? UserId, string Password, string? TfaCode = null);
    public sealed record AuthenticateDenyResponse(string[] Errors, AuthenticateDenyResponseCode Code);

    public sealed record RegisterRequest(string Username, string Email, string Password);
    public sealed record RegisterResponse(RegisterResponseStatus Status);
    public sealed record RegisterResponseError(string[] Errors);
    public sealed record ResetPasswordRequest(string Email);
    public sealed record ResendConfirmationRequest(string Email);
    public sealed record LogoutRequest(string Token);
    public sealed record RefreshRequest(string Token);
    public sealed record RefreshResponse(DateTimeOffset ExpireTime, string NewToken);

    public sealed record LauncherTokenResponse(string? Token = null, DateTimeOffset? ExpireTime = null, string? UserId = null, string? Username = null);
    public sealed record ErrorResponse(string Error);


    public readonly struct AuthenticateResult
    {
        private readonly LoginInfo? _loginInfo;
        private readonly string[]? _errors;
        public AuthenticateDenyResponseCode Code { get; }

        public AuthenticateResult(LoginInfo loginInfo)
        {
            _loginInfo = loginInfo;
            _errors = null;
            Code = default;
        }

        public AuthenticateResult(string[] errors, AuthenticateDenyResponseCode code)
        {
            _loginInfo = null;
            _errors = errors;
            Code = code;
        }

        public bool IsSuccess => _loginInfo != null;
        public LoginInfo LoginInfo => _loginInfo ?? throw new InvalidOperationException("Not a success.");
        public string[] Errors => _errors ?? throw new InvalidOperationException("Not a failure.");
    }

    public readonly struct RegisterResult
    {
        private readonly RegisterResponseStatus? _status;
        private readonly string[]? _errors;

        public RegisterResult(RegisterResponseStatus status) { _status = status; _errors = null; }
        public RegisterResult(string[] errors) { _status = null; _errors = errors; }

        public bool IsSuccess => _status != null;
        public RegisterResponseStatus Status => _status ?? throw new InvalidOperationException("Not a success.");
        public string[] Errors => _errors ?? throw new InvalidOperationException("Not a failure.");
    }

    public enum RegisterResponseStatus { Registered, RegisteredNeedConfirmation }

    public readonly struct LauncherTokenResult
    {
        private readonly LoginInfo? _loginInfo;
        private readonly string[]? _errors;

        public LauncherTokenResult(LoginInfo info) { _loginInfo = info; _errors = null; }
        public LauncherTokenResult(string[] errors) { _loginInfo = null; _errors = errors; }

        public bool IsSuccess => _loginInfo != null;
        public LoginInfo LoginInfo => _loginInfo ?? throw new InvalidOperationException("Not a success.");
        public string[] Errors => _errors ?? throw new InvalidOperationException("Not a failure.");
    }
}

[Serializable]
public class AuthApiException : Exception
{
    public AuthApiException() { }
    public AuthApiException(string message) : base(message) { }
    public AuthApiException(string message, Exception inner) : base(message, inner) { }
}
