using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    internal sealed class GoogleDrivePickerClient
    {
        private const string PickerPathPrefix = "/google-picker/";
        private static readonly TimeSpan SelectionTimeout = TimeSpan.FromMinutes(10);

        private readonly GoogleDriveConnectionService connectionService;
        private readonly GoogleDrivePickerConfiguration pickerConfiguration;

        public GoogleDrivePickerClient(
            GoogleDriveConnectionService connectionService,
            GoogleDrivePickerConfiguration pickerConfiguration)
        {
            this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            this.pickerConfiguration = pickerConfiguration ?? throw new ArgumentNullException(nameof(pickerConfiguration));
        }

        public async Task<GoogleDriveFolder> SelectFolderAsync(
            GoogleDriveAccountConfiguration configuration,
            GoogleDriveAuthorization draftAuthorization,
            CancellationToken cancellationToken)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            var accessToken = await connectionService
                .GetAccessTokenAsync(configuration, draftAuthorization, cancellationToken)
                .ConfigureAwait(false);
            var state = CreateRandomUrlSafeValue(32);
            var pickerPath = PickerPathPrefix + state;
            var callbackPath = pickerPath + "/callback";
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var pickerUri = new Uri($"http://127.0.0.1:{port}{pickerPath}");
                Process.Start(new ProcessStartInfo(pickerUri.AbsoluteUri)
                {
                    UseShellExecute = true
                });

                return await ServePickerAsync(
                    listener,
                    accessToken,
                    state,
                    pickerPath,
                    callbackPath,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                listener.Stop();
            }
        }

        internal static string RenderPickerPage(
            GoogleDrivePickerConfiguration configuration,
            string accessToken,
            string state,
            string callbackPath)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            var tokenJson = JsonString(Required(accessToken, nameof(accessToken)));
            var apiKeyJson = JsonString(configuration.ApiKey);
            var projectNumberJson = JsonString(configuration.ProjectNumber);
            var stateJson = JsonString(Required(state, nameof(state)));
            var callbackPathJson = JsonString(Required(callbackPath, nameof(callbackPath)));

            return "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
                "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
                "<meta name=\"referrer\" content=\"strict-origin-when-cross-origin\">" +
                "<title>Choose a Google Drive folder</title>" +
                "<style>body{font:16px system-ui;margin:3rem;background:#101827;color:#e8eefc}" +
                ".card{max-width:42rem;margin:auto;padding:2rem;border:1px solid #34476c;border-radius:12px}" +
                "button{padding:.7rem 1rem}p{line-height:1.5}.error{color:#ffb4ab}</style></head>" +
                "<body><main class=\"card\"><h1>Choose a game archive folder</h1>" +
                "<p id=\"status\">Loading Google Picker…</p>" +
                "<button id=\"cancel\" type=\"button\">Cancel</button></main>" +
                "<script>\"use strict\";" +
                "const accessToken=" + tokenJson + ";" +
                "const apiKey=" + apiKeyJson + ";" +
                "const appId=" + projectNumberJson + ";" +
                "const expectedState=" + stateJson + ";" +
                "const callbackPath=" + callbackPathJson + ";" +
                "const folderMimeType='application/vnd.google-apps.folder';" +
                "function finish(values){const target=new URL(callbackPath,location.origin);" +
                "target.search=new URLSearchParams(Object.assign({state:expectedState},values)).toString();" +
                "location.replace(target.toString());}" +
                "function cancel(){finish({error:'cancelled'});}" +
                "document.getElementById('cancel').addEventListener('click',cancel);" +
                "function pickerCallback(data){if(data.action===google.picker.Action.PICKED){" +
                "const selected=data[google.picker.Response.DOCUMENTS][0];" +
                "finish({folderId:selected[google.picker.Document.ID],folderName:selected[google.picker.Document.NAME]});" +
                "}else if(data.action===google.picker.Action.CANCEL){cancel();}}" +
                "function loadPicker(){gapi.load('picker',{callback:function(){" +
                "const view=new google.picker.DocsView(google.picker.ViewId.FOLDERS)" +
                ".setIncludeFolders(true).setSelectFolderEnabled(true)" +
                ".setMode(google.picker.DocsViewMode.LIST).setMimeTypes(folderMimeType);" +
                "const picker=new google.picker.PickerBuilder().setDeveloperKey(apiKey).setAppId(appId)" +
                ".setOAuthToken(accessToken).setOrigin(location.origin).setTitle('Choose a Google Drive source folder')" +
                ".addView(view).setCallback(pickerCallback).build();" +
                "document.getElementById('status').textContent='Choose one concrete folder in Google Drive.';" +
                "picker.setVisible(true);}});}" +
                "</script><script async defer src=\"https://apis.google.com/js/api.js\" onload=\"loadPicker()\"" +
                " onerror=\"document.getElementById('status').textContent='Google Picker could not be loaded.'\"></script>" +
                "</body></html>";
        }

        private async Task<GoogleDriveFolder> ServePickerAsync(
            TcpListener listener,
            string accessToken,
            string expectedState,
            string pickerPath,
            string callbackPath,
            CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.Add(SelectionTimeout);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    throw new TimeoutException("Timed out waiting for Google Drive folder selection.");

                var acceptTask = listener.AcceptTcpClientAsync();
                var waitTask = Task.Delay(remaining, cancellationToken);
                if (await Task.WhenAny(acceptTask, waitTask).ConfigureAwait(false) != acceptTask)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("Timed out waiting for Google Drive folder selection.");
                }

                using (var client = await acceptTask.ConfigureAwait(false))
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 2048, leaveOpen: true))
                {
                    var requestUri = await ReadRequestUriAsync(reader).ConfigureAwait(false);
                    if (string.Equals(requestUri.AbsolutePath, pickerPath, StringComparison.Ordinal))
                    {
                        await WriteHtmlAsync(
                            stream,
                            HttpStatusCode.OK,
                            RenderPickerPage(pickerConfiguration, accessToken, expectedState, callbackPath),
                            includePickerPolicy: true).ConfigureAwait(false);
                        continue;
                    }

                    if (string.Equals(requestUri.AbsolutePath, "/favicon.ico", StringComparison.Ordinal))
                    {
                        await WriteHtmlAsync(stream, HttpStatusCode.NotFound, string.Empty, false).ConfigureAwait(false);
                        continue;
                    }

                    if (!string.Equals(requestUri.AbsolutePath, callbackPath, StringComparison.Ordinal))
                    {
                        await WriteHtmlAsync(stream, HttpStatusCode.NotFound, "Not found", false).ConfigureAwait(false);
                        continue;
                    }

                    return await CompleteSelectionAsync(stream, requestUri, expectedState).ConfigureAwait(false);
                }
            }
        }

        private static async Task<GoogleDriveFolder> CompleteSelectionAsync(
            Stream stream,
            Uri callbackUri,
            string expectedState)
        {
            var query = ParseQuery(callbackUri.Query);
            if (!query.TryGetValue("state", out var actualState) ||
                !FixedTimeEquals(expectedState, actualState))
            {
                return await FailAsync(stream, "Google Picker state validation failed.").ConfigureAwait(false);
            }

            if (query.TryGetValue("error", out var error))
            {
                await WriteResultPageAsync(stream, false, "No Google Drive folder was selected.")
                    .ConfigureAwait(false);
                if (string.Equals(error, "cancelled", StringComparison.OrdinalIgnoreCase)) return null;
                throw new InvalidOperationException($"Google Picker failed: {error}");
            }

            if (!query.TryGetValue("folderId", out var folderId) ||
                !query.TryGetValue("folderName", out var folderName) ||
                string.IsNullOrWhiteSpace(folderId) ||
                string.IsNullOrWhiteSpace(folderName))
            {
                return await FailAsync(stream, "Google Picker did not return a folder.").ConfigureAwait(false);
            }

            await WriteResultPageAsync(
                stream,
                true,
                "The folder was shared with Cloud Storage. You can close this tab and return to Playnite.")
                .ConfigureAwait(false);
            return GoogleDriveFolder.CreatePickerSelection(folderId, folderName);
        }

        private static async Task<GoogleDriveFolder> FailAsync(Stream stream, string message)
        {
            await WriteResultPageAsync(stream, false, message).ConfigureAwait(false);
            throw new InvalidDataException(message);
        }

        private static async Task<Uri> ReadRequestUriAsync(StreamReader reader)
        {
            var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestLine))
                throw new InvalidDataException("The Google Picker callback was empty.");
            var parts = requestLine.Split(' ');
            if (parts.Length < 2)
                throw new InvalidDataException("The Google Picker callback request was invalid.");
            return new Uri("http://127.0.0.1" + parts[1]);
        }

        private static Task WriteResultPageAsync(Stream stream, bool success, string message)
        {
            var title = success ? "Cloud Storage folder selected" : "Cloud Storage folder selection failed";
            var body = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(title)}</title></head>" +
                       $"<body style=\"font:16px system-ui;margin:3rem\"><h1>{WebUtility.HtmlEncode(title)}</h1>" +
                       $"<p>{WebUtility.HtmlEncode(message)}</p></body></html>";
            return WriteHtmlAsync(stream, HttpStatusCode.OK, body, false);
        }

        private static async Task WriteHtmlAsync(
            Stream stream,
            HttpStatusCode status,
            string body,
            bool includePickerPolicy)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
            var policy = includePickerPolicy
                ? "Content-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-inline' https://apis.google.com; " +
                  "frame-src https://*.google.com https://*.googleusercontent.com; connect-src https://*.googleapis.com https://*.google.com; " +
                  "img-src data: https://*.google.com https://*.googleusercontent.com; style-src 'self' 'unsafe-inline'\r\n"
                : string.Empty;
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(int)status} {status}\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                "Cache-Control: no-store\r\n" +
                "Referrer-Policy: strict-origin-when-cross-origin\r\n" +
                "X-Content-Type-Options: nosniff\r\n" +
                policy +
                $"Content-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, 0, headers.Length).ConfigureAwait(false);
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in query.TrimStart('?').Split('&'))
            {
                if (string.IsNullOrWhiteSpace(pair)) continue;
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

        private static string JsonString(string value)
        {
            return Encoding.UTF8.GetString(GoogleDriveJson.Serialize(value));
        }

        private static string CreateRandomUrlSafeValue(int byteCount)
        {
            var bytes = new byte[byteCount];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value is required.", parameterName);
            return value.Trim();
        }
    }
}
