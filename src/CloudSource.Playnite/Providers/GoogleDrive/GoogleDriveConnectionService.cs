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

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    internal sealed class GoogleDriveConnectionService
    {
        private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string RevocationEndpoint = "https://oauth2.googleapis.com/revoke";
        private const string AboutEndpoint = "https://www.googleapis.com/drive/v3/about?fields=user(permissionId,displayName,emailAddress)";
        internal const string RequiredScope = "https://www.googleapis.com/auth/drive.readonly";
        private static readonly TimeSpan AuthorizationTimeout = TimeSpan.FromMinutes(5);

        private readonly HttpClient httpClient;
        private readonly IGoogleDriveTokenStore tokenStore;
        private readonly GoogleOAuthClientCredentials credentials;
        private readonly SemaphoreSlim tokenLock = new SemaphoreSlim(1, 1);

        public bool HasStoredAuthorization
        {
            get
            {
                if (!tokenStore.Exists) return false;
                try
                {
                    return HasRequiredScope(tokenStore.Load());
                }
                catch
                {
                    return false;
                }
            }
        }

        public GoogleDriveConnectionService(
            HttpClient httpClient,
            IGoogleDriveTokenStore tokenStore,
            GoogleOAuthClientCredentials credentials)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
            this.credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        }

        public async Task<GoogleDriveAuthorization> AuthorizeAsync(
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
                var redirectUri = $"http://127.0.0.1:{port}/oauth2/callback";
                var authorizationUri = BuildAuthorizationUri(
                    clientId,
                    redirectUri,
                    state,
                    codeChallenge);

                Process.Start(new ProcessStartInfo(authorizationUri)
                {
                    UseShellExecute = true
                });

                var callback = await ReceiveCallbackAsync(listener, state, cancellationToken).ConfigureAwait(false);
                var token = await ExchangeCodeAsync(
                    redirectUri,
                    callback,
                    codeVerifier,
                    cancellationToken).ConfigureAwait(false);
                EnsureRequiredScope(token);

                if (string.IsNullOrWhiteSpace(token.RefreshToken))
                {
                    throw new InvalidOperationException(
                        "Google did not return a refresh token. Revoke the app authorization and connect again.");
                }

                var identity = await GetIdentityAsync(token.AccessToken, cancellationToken).ConfigureAwait(false);
                return new GoogleDriveAuthorization(
                    token,
                    identity.PermissionId,
                    string.IsNullOrWhiteSpace(identity.EmailAddress) ? identity.DisplayName : identity.EmailAddress);
            }
            finally
            {
                listener.Stop();
            }
        }

        public void Commit(GoogleDriveAuthorization authorization)
        {
            if (authorization == null)
            {
                throw new ArgumentNullException(nameof(authorization));
            }

            EnsureRequiredScope(authorization.Token);
            tokenStore.Save(authorization.Token);
        }

        public void ClearIncompatibleAuthorization()
        {
            if (!tokenStore.Exists || HasStoredAuthorization) return;
            Disconnect();
        }

        public void Disconnect()
        {
            GoogleDriveToken token = null;
            try
            {
                if (tokenStore.Exists)
                {
                    token = tokenStore.Load();
                }
            }
            catch
            {
                // A corrupt local authorization must not prevent local cleanup.
            }

            try
            {
                var tokenValue = string.IsNullOrWhiteSpace(token?.RefreshToken)
                    ? token?.AccessToken
                    : token.RefreshToken;
                if (!string.IsNullOrWhiteSpace(tokenValue))
                {
                    RevokeAsync(tokenValue).GetAwaiter().GetResult();
                }
            }
            catch
            {
                // Revocation is best effort. The privacy policy also links to
                // Google's connected-app controls for manual revocation.
            }
            finally
            {
                tokenStore.Clear();
            }
        }

        private async Task RevokeAsync(string token)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = token
            }))
            using (var response = await httpClient
                .PostAsync(RevocationEndpoint, content, timeout.Token)
                .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
            }
        }

        public async Task<string> GetAccessTokenAsync(
            GoogleDriveAccountConfiguration configuration,
            CancellationToken cancellationToken)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            await tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var token = tokenStore.Load();
                EnsureRequiredScope(token);
                if (!string.IsNullOrWhiteSpace(token.AccessToken) &&
                    token.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(1))
                {
                    return token.AccessToken;
                }

                if (string.IsNullOrWhiteSpace(token.RefreshToken))
                {
                    throw new InvalidOperationException("Google Drive authorization cannot be refreshed. Reconnect the account.");
                }

                var refreshed = await RefreshAsync(token.RefreshToken, cancellationToken).ConfigureAwait(false);
                refreshed.RefreshToken = token.RefreshToken;
                refreshed.Scope = FirstNonEmpty(refreshed.Scope, token.Scope);
                EnsureRequiredScope(refreshed);
                tokenStore.Save(refreshed);
                return refreshed.AccessToken;
            }
            finally
            {
                tokenLock.Release();
            }
        }

        public async Task<string> GetAccessTokenAsync(
            GoogleDriveAccountConfiguration configuration,
            GoogleDriveAuthorization draftAuthorization,
            CancellationToken cancellationToken)
        {
            if (draftAuthorization == null)
            {
                return await GetAccessTokenAsync(configuration, cancellationToken).ConfigureAwait(false);
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (!string.Equals(
                configuration.AccountId,
                draftAuthorization.AccountId,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The draft Google Drive authorization belongs to a different account.");
            }

            await tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var token = draftAuthorization.Token;
                EnsureRequiredScope(token);
                if (!string.IsNullOrWhiteSpace(token.AccessToken) &&
                    token.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(1))
                {
                    return token.AccessToken;
                }

                if (string.IsNullOrWhiteSpace(token.RefreshToken))
                {
                    throw new InvalidOperationException(
                        "The draft Google Drive authorization cannot be refreshed. Connect the account again.");
                }

                var refreshed = await RefreshAsync(token.RefreshToken, cancellationToken).ConfigureAwait(false);
                refreshed.RefreshToken = token.RefreshToken;
                refreshed.Scope = FirstNonEmpty(refreshed.Scope, token.Scope);
                EnsureRequiredScope(refreshed);
                draftAuthorization.ReplaceToken(refreshed);
                return refreshed.AccessToken;
            }
            finally
            {
                tokenLock.Release();
            }
        }

        internal async Task<GoogleDriveToken> ExchangeCodeAsync(
            string redirectUri,
            string code,
            string codeVerifier,
            CancellationToken cancellationToken)
        {
            var fields = new Dictionary<string, string>
            {
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            };
            credentials.AddTo(fields);

            return await RequestTokenAsync(fields, cancellationToken).ConfigureAwait(false);
        }

        internal async Task<GoogleDriveToken> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken)
        {
            var fields = new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            };
            credentials.AddTo(fields);

            return await RequestTokenAsync(fields, cancellationToken).ConfigureAwait(false);
        }

        private async Task<GoogleDriveToken> RequestTokenAsync(
            IReadOnlyDictionary<string, string> fields,
            CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token"))
            {
                request.Content = new FormUrlEncodedContent(fields);
                using (var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(
                            $"Google OAuth token request failed ({(int)response.StatusCode}): {body}");
                    }

                    var token = GoogleDriveJson.Deserialize<GoogleDriveToken>(body);
                    if (string.IsNullOrWhiteSpace(token.AccessToken) || token.ExpiresInSeconds <= 0)
                    {
                        throw new InvalidDataException("Google returned an incomplete OAuth token response.");
                    }

                    token.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds);
                    return token;
                }
            }
        }

        private async Task<GoogleDriveUser> GetIdentityAsync(
            string accessToken,
            CancellationToken cancellationToken)
        {
            using (var request = CreateAuthorizedRequest(HttpMethod.Get, AboutEndpoint, accessToken))
            using (var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"Google Drive identity request failed ({(int)response.StatusCode}): {body}");
                }

                var result = GoogleDriveJson.Deserialize<GoogleDriveAboutResponse>(body);
                if (result?.User == null || string.IsNullOrWhiteSpace(result.User.PermissionId))
                {
                    throw new InvalidDataException("Google Drive did not return a stable account identity.");
                }

                return result.User;
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
                throw new TimeoutException("Timed out waiting for Google authorization.");
            }

            using (var client = await acceptTask.ConfigureAwait(false))
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true))
            {
                var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    await WriteBrowserResponseAsync(stream, false, "The OAuth callback was empty.").ConfigureAwait(false);
                    throw new InvalidDataException("Google OAuth callback was empty.");
                }

                var parts = requestLine.Split(' ');
                if (parts.Length < 2)
                {
                    await WriteBrowserResponseAsync(stream, false, "The OAuth callback was invalid.").ConfigureAwait(false);
                    throw new InvalidDataException("Google OAuth callback request was invalid.");
                }

                var callbackUri = new Uri("http://127.0.0.1" + parts[1]);
                if (!string.Equals(callbackUri.AbsolutePath, "/oauth2/callback", StringComparison.Ordinal))
                {
                    await WriteBrowserResponseAsync(stream, false, "The OAuth callback path was invalid.").ConfigureAwait(false);
                    throw new InvalidDataException("Google OAuth callback path was invalid.");
                }

                var query = ParseQuery(callbackUri.Query);
                if (!query.TryGetValue("state", out var actualState) ||
                    !FixedTimeEquals(expectedState, actualState))
                {
                    await WriteBrowserResponseAsync(stream, false, "OAuth state validation failed.").ConfigureAwait(false);
                    throw new InvalidOperationException("Google OAuth state validation failed.");
                }

                if (query.TryGetValue("error", out var oauthError))
                {
                    await WriteBrowserResponseAsync(stream, false, "Google authorization was not completed.").ConfigureAwait(false);
                    throw new InvalidOperationException($"Google authorization failed: {oauthError}");
                }

                if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                {
                    await WriteBrowserResponseAsync(stream, false, "Google did not return an authorization code.").ConfigureAwait(false);
                    throw new InvalidOperationException("Google did not return an authorization code.");
                }

                await WriteBrowserResponseAsync(
                    stream,
                    true,
                    "Google Drive is connected. You can close this tab and return to Playnite.").ConfigureAwait(false);
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

        internal static string BuildAuthorizationUri(
            string clientId,
            string redirectUri,
            string state,
            string codeChallenge)
        {
            var query = new Dictionary<string, string>
            {
                ["access_type"] = "offline",
                ["client_id"] = clientId,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["prompt"] = "consent select_account",
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = RequiredScope,
                ["state"] = state
            };

            var builder = new StringBuilder(AuthorizationEndpoint).Append('?');
            foreach (var pair in query)
            {
                if (builder[builder.Length - 1] != '?')
                {
                    builder.Append('&');
                }

                builder.Append(Uri.EscapeDataString(pair.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(pair.Value));
            }

            return builder.ToString();
        }

        private static void EnsureRequiredScope(GoogleDriveToken token)
        {
            if (!HasRequiredScope(token))
            {
                throw new InvalidOperationException(
                    "Google Drive authorization does not grant read-only library access. Disconnect and reconnect the account.");
            }
        }

        private static bool HasRequiredScope(GoogleDriveToken token)
        {
            if (token == null || string.IsNullOrWhiteSpace(token.Scope)) return false;
            var scopes = new HashSet<string>(
                token.Scope.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
            return scopes.Contains(RequiredScope) &&
                !scopes.Contains("https://www.googleapis.com/auth/drive");
        }

        private static string FirstNonEmpty(string primary, string fallback)
        {
            return string.IsNullOrWhiteSpace(primary) ? fallback : primary;
        }

        private static HttpRequestMessage CreateAuthorizedRequest(
            HttpMethod method,
            string uri,
            string accessToken)
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
                if (string.IsNullOrWhiteSpace(pair))
                {
                    continue;
                }

                var parts = pair.Split(new[] { '=' }, 2);
                var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
                var value = parts.Length == 2
                    ? Uri.UnescapeDataString(parts[1].Replace('+', ' '))
                    : string.Empty;
                result[key] = value;
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
                var expectedByte = index < expectedBytes.Length ? expectedBytes[index] : (byte)0;
                var actualByte = index < actualBytes.Length ? actualBytes[index] : (byte)0;
                difference |= expectedByte ^ actualByte;
            }

            return difference == 0;
        }

        private static string CreateRandomUrlSafeValue(int byteCount)
        {
            var bytes = new byte[byteCount];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return Base64Url(bytes);
        }

        private static string CreateCodeChallenge(string verifier)
        {
            using (var sha256 = SHA256.Create())
            {
                return Base64Url(sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            }
        }

        private static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value is required.", parameterName);
            }

            return value.Trim();
        }
    }
}
