using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    internal sealed class GoogleDriveApiClient
    {
        private const string ApiBaseUri = "https://www.googleapis.com/drive/v3/";
        private const string FolderMimeType = "application/vnd.google-apps.folder";
        private static readonly Regex ObjectIdPattern = new Regex(
            "^[A-Za-z0-9_-]+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly HttpClient httpClient;
        private readonly GoogleDriveConnectionService connectionService;

        public GoogleDriveApiClient(
            HttpClient httpClient,
            GoogleDriveConnectionService connectionService)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        }

        public async Task<IReadOnlyList<SourcePackage>> ScanAsync(
            GoogleDriveAccountConfiguration configuration,
            SourceScanRequest request,
            CancellationToken cancellationToken)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!string.Equals(configuration.AccountId, request.AccountId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The scan request does not belong to the configured Google Drive account.");
            }

            var accessToken = await connectionService
                .GetAccessTokenAsync(configuration, cancellationToken)
                .ConfigureAwait(false);
            var packages = new List<SourcePackage>();
            var visitedFolders = new HashSet<string>(StringComparer.Ordinal);

            foreach (var location in request.Locations)
            {
                ValidateObjectId(location.ObjectId);
                await ScanFolderAsync(
                    accessToken,
                    configuration.AccountId,
                    location.ObjectId,
                    location.DisplayPath,
                    location.Recursive,
                    visitedFolders,
                    packages,
                    cancellationToken).ConfigureAwait(false);
            }

            return packages
                .OrderBy(package => package.LogicalPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<Stream> OpenReadAsync(
            GoogleDriveAccountConfiguration configuration,
            SourcePackage package,
            CancellationToken cancellationToken)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            if (!string.Equals(package.ProviderId, GoogleDriveProvider.ProviderId, StringComparison.Ordinal) ||
                !string.Equals(package.AccountId, configuration.AccountId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The package does not belong to the configured Google Drive account.");
            }

            ValidateObjectId(package.ObjectId);
            var accessToken = await connectionService
                .GetAccessTokenAsync(configuration, cancellationToken)
                .ConfigureAwait(false);
            var uri = ApiBaseUri + "files/" + Uri.EscapeDataString(package.ObjectId) + "?alt=media&supportsAllDrives=true";
            var request = CreateAuthorizedRequest(HttpMethod.Get, uri, accessToken);
            var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            request.Dispose();

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new InvalidOperationException($"Google Drive download failed ({status}): {body}");
            }

            var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            return new HttpResponseStream(stream, response);
        }

        private async Task ScanFolderAsync(
            string accessToken,
            string accountId,
            string folderId,
            string displayPath,
            bool recursive,
            ISet<string> visitedFolders,
            ICollection<SourcePackage> packages,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visitedFolders.Add(folderId))
            {
                return;
            }

            string pageToken = null;
            do
            {
                var response = await ListFolderPageAsync(
                    accessToken,
                    folderId,
                    pageToken,
                    cancellationToken).ConfigureAwait(false);

                foreach (var file in response.Files ?? Enumerable.Empty<GoogleDriveFile>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(file.Id) || string.IsNullOrWhiteSpace(file.Name))
                    {
                        throw new InvalidDataException("Google Drive returned a file without an ID or name.");
                    }

                    ValidateObjectId(file.Id);
                    var logicalPath = CombineLogicalPath(displayPath, file.Name);
                    if (string.Equals(file.MimeType, FolderMimeType, StringComparison.Ordinal))
                    {
                        if (recursive)
                        {
                            await ScanFolderAsync(
                                accessToken,
                                accountId,
                                file.Id,
                                logicalPath,
                                true,
                                visitedFolders,
                                packages,
                                cancellationToken).ConfigureAwait(false);
                        }

                        continue;
                    }

                    if (!TryGetPackageKind(file.Name, out var kind))
                    {
                        continue;
                    }

                    if (!long.TryParse(file.Size, NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size < 0)
                    {
                        throw new InvalidDataException($"Google Drive returned an invalid size for '{logicalPath}'.");
                    }

                    DateTimeOffset? modifiedAt = null;
                    if (!string.IsNullOrWhiteSpace(file.ModifiedTime))
                    {
                        if (!DateTimeOffset.TryParse(
                            file.ModifiedTime,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal,
                            out var parsedModifiedAt))
                        {
                            throw new InvalidDataException($"Google Drive returned an invalid modified time for '{logicalPath}'.");
                        }

                        modifiedAt = parsedModifiedAt;
                    }

                    var revision = FirstNonEmpty(file.Md5Checksum, file.Version, file.ModifiedTime);
                    packages.Add(new SourcePackage(
                        GoogleDriveProvider.ProviderId,
                        accountId,
                        file.Id,
                        revision,
                        logicalPath,
                        file.Name,
                        size,
                        modifiedAt,
                        kind));
                }

                pageToken = response.NextPageToken;
            }
            while (!string.IsNullOrWhiteSpace(pageToken));
        }

        private async Task<GoogleDriveFileListResponse> ListFolderPageAsync(
            string accessToken,
            string folderId,
            string pageToken,
            CancellationToken cancellationToken)
        {
            var query = $"'{folderId}' in parents and trashed = false";
            var fields = "nextPageToken,files(id,name,mimeType,size,md5Checksum,modifiedTime,version)";
            var uri = ApiBaseUri + "files" +
                      "?q=" + Uri.EscapeDataString(query) +
                      "&fields=" + Uri.EscapeDataString(fields) +
                      "&includeItemsFromAllDrives=true" +
                      "&supportsAllDrives=true" +
                      "&pageSize=1000";
            if (!string.IsNullOrWhiteSpace(pageToken))
            {
                uri += "&pageToken=" + Uri.EscapeDataString(pageToken);
            }

            using (var request = CreateAuthorizedRequest(HttpMethod.Get, uri, accessToken))
            using (var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"Google Drive folder listing failed ({(int)response.StatusCode}): {body}");
                }

                return GoogleDriveJson.Deserialize<GoogleDriveFileListResponse>(body)
                    ?? throw new InvalidDataException("Google Drive returned an empty folder listing response.");
            }
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

        private static void ValidateObjectId(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId) || !ObjectIdPattern.IsMatch(objectId))
            {
                throw new ArgumentException("Google Drive object ID is invalid.", nameof(objectId));
            }
        }

        private static string CombineLogicalPath(string parent, string name)
        {
            if (string.IsNullOrWhiteSpace(parent))
            {
                throw new ArgumentException("Logical parent path is required.", nameof(parent));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Logical child name is required.", nameof(name));
            }

            var safeName = name.Trim().Replace('/', '\u2215').Replace('\\', '\u2215');
            return parent.Trim().TrimEnd('/') + "/" + safeName;
        }

        private static bool TryGetPackageKind(string name, out SourcePackageKind kind)
        {
            var extension = Path.GetExtension(name);
            if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
            {
                kind = SourcePackageKind.ZipArchive;
                return true;
            }

            if (string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase))
            {
                kind = SourcePackageKind.SevenZipArchive;
                return true;
            }

            if (string.Equals(extension, ".rar", StringComparison.OrdinalIgnoreCase))
            {
                kind = SourcePackageKind.RarArchive;
                return true;
            }

            kind = default(SourcePackageKind);
            return false;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            var value = values.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
            if (value == null)
            {
                throw new InvalidDataException("Google Drive returned a file without revision information.");
            }

            return value.Trim();
        }
    }
}
