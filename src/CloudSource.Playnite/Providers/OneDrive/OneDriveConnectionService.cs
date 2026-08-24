using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CloudSource.Playnite.Providers.OneDrive
{
    internal sealed class OneDriveConnectionService
    {
        private const string AuthorizationEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";
        private const string TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
        private const string ProfileEndpoint = "https://graph.microsoft.com/v1.0/me?$select=id,displayName,mail,userPrincipalName";
        private const string Scopes = "offline_access User.Read Files.Read";
        private static readonly TimeSpan AuthorizationTimeout = TimeSpan.FromMinutes(5);

        private readonly HttpClient httpClient;
        private readonly IOneDriveTokenStore tokenStore;
        private readonly SemaphoreSlim tokenLock = new SemaphoreSlim(1, 1);

        public bool HasStoredAuthorization => tokenStore.Exists;

        public OneDriveConnectionService(HttpClient httpClient, IOneDriveTokenStore tokenStore)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        }

        public async Task<OneDriveAuthorization> AuthorizeAsync(
            string clientId,
            CancellationToken cancellationToken)
        {
            clientId = Required(clientId, nameof(clientId));
            var state = CreateRandomUrlSafeValue(32);
            var codeVerifier = CreateRandomUrlSafeValue(64);
            var codeChallenge = CreateCodeChallenge(codeVerifier);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var redirectUri = $"http://localhost:{port}";
                var authorizationUri = BuildAuthorizationUri(clientId, redirectUri, state, codeChallenge);
                Process.Start(new ProcessStartInfo(authorizationUri) { UseShellExecute = true });

                var code = await ReceiveCallbackAsync(listener, state, cancellationToken).ConfigureAwait(false);
                var token = await ExchangeCodeAsync(
                    clientId,
                    redirectUri,
                    code,
                    codeVerifier,
                    cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token.RefreshToken))
                    throw new InvalidOperationException("Microsoft did not return a refresh token. Remove the app consent and connect again.");
                var identity = await GetIdentityAsync(token.AccessToken, cancellationToken).ConfigureAwait(false);
                var displayName = FirstNonEmpty(identity.Mail, identity.UserPrincipalName, identity.DisplayName);
                return new OneDriveAuthorization(token, identity.Id, displayName);
            }
            finally
            {
                listener.Stop();
            }
        }

        public void Commit(OneDriveAuthorization authorization)
        {
            if (authorization == null) throw new ArgumentNullException(nameof(authorization));
            tokenStore.Save(authorization.Token);
        }

        public void Disconnect()
        {
            tokenStore.Clear();
        }

        public async Task<string> GetAccessTokenAsync(
            OneDriveAccountConfiguration configuration,
            CancellationToken cancellationToken)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            await tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var token = tokenStore.Load();
                if (!string.IsNullOrWhiteSpace(token.AccessToken) &&
                    token.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(1))
                {
                    return token.AccessToken;
                }

                var refreshed = await RefreshAsync(
                    configuration.ClientId,
                    token.RefreshToken,
                    cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(refreshed.RefreshToken)) refreshed.RefreshToken = token.RefreshToken;
                tokenStore.Save(refreshed);
                return refreshed.AccessToken;
            }
            finally
            {
                tokenLock.Release();
            }
        }

        public async Task<string> GetAccessTokenAsync(
            OneDriveAccountConfiguration configuration,
            OneDriveAuthorization draftAuthorization,
            CancellationToken cancellationToken)
        {
            if (draftAuthorization == null)
                return await GetAccessTokenAsync(configuration, cancellationToken).ConfigureAwait(false);
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (!string.Equals(configuration.AccountId, draftAuthorization.AccountId, StringComparison.Ordinal))
                throw new InvalidOperationException("The draft OneDrive authorization belongs to a different account.");

            await tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var token = draftAuthorization.Token;
                if (!string.IsNullOrWhiteSpace(token.AccessToken) &&
                    token.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(1))
                {
                    return token.AccessToken;
                }

                var refreshed = await RefreshAsync(
                    configuration.ClientId,
                    token.RefreshToken,
                    cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(refreshed.RefreshToken)) refreshed.RefreshToken = token.RefreshToken;
                draftAuthorization.ReplaceToken(refreshed);
                return refreshed.AccessToken;
            }
            finally
            {
                tokenLock.Release();
            }
        }

        private Task<OneDriveToken> ExchangeCodeAsync(
            string clientId,
            string redirectUri,
            string code,
            string codeVerifier,
            CancellationToken cancellationToken)
        {
            return RequestTokenAsync(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code",
                ["scope"] = Scopes,
                ["code_verifier"] = codeVerifier
            }, cancellationToken);
        }

        private Task<OneDriveToken> RefreshAsync(
            string clientId,
            string refreshToken,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new InvalidOperationException("OneDrive authorization cannot be refreshed. Connect the account again.");
            return RequestTokenAsync(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token",
                ["scope"] = Scopes
            }, cancellationToken);
        }

        private async Task<OneDriveToken> RequestTokenAsync(
            IReadOnlyDictionary<string, string> fields,
            CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint))
            {
                request.Content = new FormUrlEncodedContent(fields);
                using (var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException($"Microsoft OAuth token request failed ({(int)response.StatusCode}): {body}");
                    var token = OneDriveJson.Deserialize<OneDriveToken>(body);
                    if (string.IsNullOrWhiteSpace(token.AccessToken) || token.ExpiresInSeconds <= 0)
                        throw new InvalidDataException("Microsoft returned an incomplete OAuth token response.");
                    token.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds);
                    return token;
                }
            }
        }

        private async Task<OneDriveUser> GetIdentityAsync(
            string accessToken,
            CancellationToken cancellationToken)
        {
            using (var request = CreateAuthorizedRequest(HttpMethod.Get, ProfileEndpoint, accessToken))
            using (var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Microsoft account lookup failed ({(int)response.StatusCode}): {body}");
                var user = OneDriveJson.Deserialize<OneDriveUser>(body);
                if (user == null || string.IsNullOrWhiteSpace(user.Id))
                    throw new InvalidDataException("Microsoft Graph did not return a stable account identity.");
                return user;
            }
        }

        private static async Task<string> ReceiveCallbackAsync(
            TcpListener listener,
            string expectedState,
            CancellationToken cancellationToken)
        {
            var acceptTask = listener.AcceptTcpClientAsync();
            var timeoutTask = Task.Delay(AuthorizationTimeout, cancellationToken);
            var completed = await Task.WhenAny(acceptTask, timeoutTask).ConfigureAwait(false);
            if (completed != acceptTask)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("Timed out waiting for Microsoft authorization.");
            }

            using (var client = await acceptTask.ConfigureAwait(false))
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true))
            {
                var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                var parts = requestLine?.Split(' ');
                if (parts == null || parts.Length < 2)
                {
                    await WriteBrowserResponseAsync(stream, false, "The OAuth callback was invalid.").ConfigureAwait(false);
                    throw new InvalidDataException("Microsoft OAuth callback request was invalid.");
                }

                var callbackUri = new Uri("http://localhost" + parts[1]);
                if (!string.Equals(callbackUri.AbsolutePath, "/", StringComparison.Ordinal))
                {
                    await WriteBrowserResponseAsync(stream, false, "The OAuth callback path was invalid.").ConfigureAwait(false);
                    throw new InvalidDataException("Microsoft OAuth callback path was invalid.");
                }

                var query = ParseQuery(callbackUri.Query);
                if (!query.TryGetValue("state", out var actualState) || !FixedTimeEquals(expectedState, actualState))
                {
                    await WriteBrowserResponseAsync(stream, false, "OAuth state validation failed.").ConfigureAwait(false);
                    throw new InvalidOperationException("Microsoft OAuth state validation failed.");
                }
                if (query.TryGetValue("error", out var oauthError))
                {
                    await WriteBrowserResponseAsync(stream, false, "Microsoft authorization was not completed.").ConfigureAwait(false);
                    throw new InvalidOperationException($"Microsoft authorization failed: {oauthError}");
                }
                if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                {
                    await WriteBrowserResponseAsync(stream, false, "Microsoft did not return an authorization code.").ConfigureAwait(false);
                    throw new InvalidOperationException("Microsoft did not return an authorization code.");
                }

                await WriteBrowserResponseAsync(
                    stream,
                    true,
                    "OneDrive is connected. You can close this tab and return to Playnite.").ConfigureAwait(false);
                return code;
            }
        }

        private static async Task WriteBrowserResponseAsync(Stream stream, bool success, string message)
        {
            var title = success ? "Cloud Storage connected" : "Cloud Storage connection failed";
            var body = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(title)}</title></head>" +
                       $"<body style=\"font:16px system-ui;margin:3rem\"><h1>{WebUtility.HtmlEncode(title)}</h1>" +
                       $"<p>{WebUtility.HtmlEncode(message)}</p></body></html>";
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var headers = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, 0, headers.Length).ConfigureAwait(false);
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private static string BuildAuthorizationUri(
            string clientId,
            string redirectUri,
            string state,
            string codeChallenge)
        {
            var query = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["response_type"] = "code",
                ["redirect_uri"] = redirectUri,
                ["response_mode"] = "query",
                ["scope"] = Scopes,
                ["state"] = state,
                ["prompt"] = "select_account",
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256"
            };
            var builder = new StringBuilder(AuthorizationEndpoint).Append('?');
            foreach (var pair in query)
            {
                if (builder[builder.Length - 1] != '?') builder.Append('&');
                builder.Append(Uri.EscapeDataString(pair.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(pair.Value));
            }
            return builder.ToString();
        }

        private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string uri, string accessToken)
        {
            var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            return request;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in query.TrimStart('?').Split('&'))
            {
                if (string.IsNullOrWhiteSpace(pair)) continue;
                var parts = pair.Split(new[] { '=' }, 2);
                result[Uri.UnescapeDataString(parts[0].Replace('+', ' '))] = parts.Length == 2
                    ? Uri.UnescapeDataString(parts[1].Replace('+', ' '))
                    : string.Empty;
            }
            return result;
        }

        private static bool FixedTimeEquals(string expected, string actual)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(expected ?? string.Empty);
            var actualBytes = Encoding.UTF8.GetBytes(actual ?? string.Empty);
            var difference = expectedBytes.Length ^ actualBytes.Length;
            var length = Math.Max(expectedBytes.Length, actualBytes.Length);
            for (var index = 0; index < length; index++)
            {
                difference |= (index < expectedBytes.Length ? expectedBytes[index] : (byte)0) ^
                              (index < actualBytes.Length ? actualBytes[index] : (byte)0);
            }
            return difference == 0;
        }

        private static string CreateRandomUrlSafeValue(int byteCount)
        {
            var bytes = new byte[byteCount];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Base64Url(bytes);
        }

        private static string CreateCodeChallenge(string verifier)
        {
            using (var sha256 = SHA256.Create())
                return Base64Url(sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        private static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            throw new InvalidDataException("Microsoft Graph returned no account display name.");
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
            return value.Trim();
        }
    }
}
