using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http;

namespace Aquamarine.Source.Management
{
    public partial class LoginManager
    {
        public class LoginResult
        {
            public bool Success { get; set; }
            public bool Requires2FA { get; set; }
            public string Error { get; set; }
        }

        public async Task<LoginResult> LoginAsync(string username, string password, string twoFactorCode = null)
        {
            try
            {
                var loginData = new
                {
                    Username = username,
                    Password = password,
                    TwoFactorCode = twoFactorCode
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(loginData),
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PostAsync("/api/user/login", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                // Always try to parse the response content first
                var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

                // Check for 2FA requirement
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    if (jsonResponse.TryGetProperty("title", out JsonElement titleElement) &&
                        titleElement.GetString() == "2FA Required")
                    {
                        _requires2FA = true;
                        _pendingUsername = username;
                        _pendingPassword = password;
                        return new LoginResult { Requires2FA = true };
                    }
                }

                // If response is not successful, get the error message from the response
                if (!response.IsSuccessStatusCode)
                {
                    string errorMessage = "Invalid credentials";
                    if (jsonResponse.TryGetProperty("title", out JsonElement errorTitle))
                    {
                        errorMessage = errorTitle.GetString();
                    }
                    return new LoginResult { Error = errorMessage };
                }

                // Success case - should have a token
                if (jsonResponse.TryGetProperty("token", out JsonElement tokenElement))
                {
                    var token = tokenElement.GetString();
                    if (!string.IsNullOrEmpty(token))
                    {
                        _authData = new UserAuthData
                        {
                            AuthToken = token,
                            Username = username,
                            TokenExpiry = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds()
                        };

                        LocalDatabase.Instance.SetValue(AUTH_KEY, _authData);
                        _httpClient.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authData.AuthToken);

                        await FetchUserProfile();
                        OnLoginStatusChanged?.Invoke(true);

                        return new LoginResult { Success = true };
                    }
                }

                return new LoginResult { Error = "Invalid response from server" };
            }
            catch (Exception ex)
            {
                return new LoginResult { Error = ex.Message };
            }
        }

        public async Task<RegisterResponse> RegisterAsync(string username, string email, string password)
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
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/api/user/register", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                return new RegisterResponse
                {
                    Success = response.IsSuccessStatusCode,
                    Message = response.IsSuccessStatusCode ? "Registration successful" : "Registration failed"
                };
            }
            catch (Exception ex)
            {
                return new RegisterResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        private async Task FetchUserProfile()
        {
            var profileResponse = await _httpClient.GetAsync("/api/user/me");
            if (profileResponse.IsSuccessStatusCode)
            {
                var profileContent = await profileResponse.Content.ReadAsStringAsync();
                _authData.Profile = JsonSerializer.Deserialize<UserProfile>(profileContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                LocalDatabase.Instance.SetValue(AUTH_KEY, _authData);
            }
        }

        public void Logout()
        {
            _authData = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;
            _requires2FA = false;
            _pendingUsername = null;
            _pendingPassword = null;

            if (LocalDatabase.Instance.HasKey(AUTH_KEY))
            {
                LocalDatabase.Instance.DeleteValue(AUTH_KEY);
            }

            OnLoginStatusChanged?.Invoke(false);
        }

        public bool IsTokenExpired()
        {
            if (_authData == null) return true;
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return currentTime > _authData.TokenExpiry;
        }
    }
}