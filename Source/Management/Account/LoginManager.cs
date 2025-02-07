using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace Aquamarine.Source.Management;

public partial class LoginManager : Node
{
    public static LoginManager Instance;
    private const string ApiBaseUrl = "https://api.lumoravr.com";
    private readonly System.Net.Http.HttpClient _httpClient;

    public event Action<bool> OnLoginStatusChanged;

    public bool IsLoggedIn => _authData?.AuthToken != null;
    private UserAuthData _authData;

    private const string AUTH_KEY = "auth_data";

    public LoginManager()
    {
        _httpClient = new System.Net.Http.HttpClient
        {
            BaseAddress = new Uri(ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public override void _Ready()
    {
        Instance = this;
        LoadSavedSession();
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        try
        {
            var loginData = new
            {
                Username = username,
                Password = password
            };

            var content = new StringContent(
                JsonSerializer.Serialize(loginData),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("/api/user/login", content);

            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            LoginResponse loginResponse;
            try
            {
                loginResponse = JsonSerializer.Deserialize<LoginResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrEmpty(loginResponse?.Token))
            {
                return false;
            }

            _authData = new UserAuthData
            {
                AuthToken = loginResponse.Token,
                Username = username,
                TokenExpiry = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds()
            };

            LocalDatabase.Instance.SetValue(AUTH_KEY, _authData);

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authData.AuthToken);

            var profileResponse = await _httpClient.GetAsync("/api/user/me");
            if (profileResponse.IsSuccessStatusCode)
            {
                var profileContent = await profileResponse.Content.ReadAsStringAsync();
                _authData.Profile = JsonSerializer.Deserialize<UserProfile>(profileContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                LocalDatabase.Instance.SetValue(AUTH_KEY, _authData);
            }

            OnLoginStatusChanged?.Invoke(true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RegisterAsync(string username, string email, string password)
    {
        try
        {
            var registerData = new
            {
                Username = username,
                Email = email,
                Password = password
            };

            var json = JsonSerializer.Serialize(registerData);

            var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("/api/user/register", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Logout()
    {
        _authData = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;

        if (LocalDatabase.Instance.HasKey(AUTH_KEY))
        {
            LocalDatabase.Instance.DeleteValue(AUTH_KEY);
        }

        OnLoginStatusChanged?.Invoke(false);
    }

    private void LoadSavedSession()
    {
        try
        {
            _authData = LocalDatabase.Instance.GetValue<UserAuthData>(AUTH_KEY);

            if (_authData != null)
            {
                var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (currentTime > _authData.TokenExpiry)
                {
                    Logout();
                    return;
                }

                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authData.AuthToken);

                OnLoginStatusChanged?.Invoke(true);
            }
        }
        catch
        {
            Logout();
        }
    }

    public string GetCurrentUsername()
    {
        return _authData?.Username;
    }

    public UserProfile GetUserProfile()
    {
        return _authData?.Profile;
    }

    public async Task RefreshUserProfile()
    {
        if (!IsLoggedIn) return;

        try
        {
            var response = await _httpClient.GetAsync("/api/user/me");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                _authData.Profile = JsonSerializer.Deserialize<UserProfile>(content);
                LocalDatabase.Instance.SetValue(AUTH_KEY, _authData);
            }
        }
        catch
        {
            // Silently fail
        }
    }

    public bool IsTokenExpired()
    {
        if (_authData == null) return true;
        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return currentTime > _authData.TokenExpiry;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (IsLoggedIn && IsTokenExpired())
        {
            Logout();
        }
    }

    public override void _ExitTree()
    {
        _httpClient.Dispose();
        base._ExitTree();
    }
}