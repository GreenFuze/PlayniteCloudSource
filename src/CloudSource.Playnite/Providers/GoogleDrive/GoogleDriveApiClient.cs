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
        private readonly CloudPackageDiscovery packageDiscovery;

        public GoogleDriveApiClient(
            HttpClient httpClient,
            GoogleDriveConnectionService connectionService,
            CloudPackageDiscovery packageDiscovery)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            this.packageDiscovery = packageDiscovery ?? throw new ArgumentNullException(nameof(packageDiscovery));
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
            var files = new List<CloudFileEntry>();
            var visitedFolders = new HashSet<string>(StringComparer.Ordinal);

            foreach (var location in request.Locations)
            {
                ValidateObjectId(location.ObjectId);
                await ScanFolderAsync(
                    accessToken,
                    location.ObjectId,
                    location.DisplayPath,
                    location.Recursive,
                    visitedFolders,
                    files,
                    cancellationToken).ConfigureAwait(false);
            }

            return packageDiscovery
                .Discover(GoogleDriveProvider.ProviderId, configuration.AccountId, files)
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

            return await OpenReadFileAsync(
                configuration,
                package,
                package.Files.Single(file => file.Role == SourcePackageFileRole.Primary),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<Stream> OpenReadFileAsync(
            GoogleDriveAccountConfiguration configuration,
            SourcePackage package,
            SourcePackageFile file,
            CancellationToken cancellationToken)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (!string.Equals(package.ProviderId, GoogleDriveProvider.ProviderId, StringComparison.Ordinal) ||
                !string.Equals(package.AccountId, configuration.AccountId, StringComparison.Ordinal) ||
                !package.Files.Any(candidate => string.Equals(candidate.ObjectId, file.ObjectId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("The package file does not belong to the configured Google Drive account.");
            }

            ValidateObjectId(file.ObjectId);
            var accessToken = await connectionService
                .GetAccessTokenAsync(configuration, cancellationToken)
                .ConfigureAwait(false);
            var uri = ApiBaseUri + "files/" + Uri.EscapeDataString(file.ObjectId) + "?alt=media&supportsAllDrives=true";
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

        public async Task<IReadOnlyList<GoogleDriveFolder>> ListFoldersAsync(
            GoogleDriveAccountConfiguration configuration,
            GoogleDriveFolder parent,
            GoogleDriveAuthorization draftAuthorization,
            CancellationToken cancellationToken)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (!parent.CanBrowse)
            {
                throw new InvalidOperationException($"Google Drive location '{parent.Name}' cannot be browsed.");
            }

            var accessToken = await connectionService
                .GetAccessTokenAsync(configuration, draftAuthorization, cancellationToken)
                .ConfigureAwait(false);
            var query = parent.Kind == GoogleDriveFolderKind.SharedWithMe
                ? $"sharedWithMe = true and mimeType = '{FolderMimeType}' and trashed = false"
                : $"'{ValidateObjectId(parent.ObjectId)}' in parents and mimeType = '{FolderMimeType}' and trashed = false";
            var files = await ListFilesByQueryAsync(accessToken, query, cancellationToken).ConfigureAwait(false);

            return files
                .Select(file => GoogleDriveFolder.CreateChild(parent, file.Id, file.Name))
                .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task ScanFolderAsync(
            string accessToken,
            string folderId,
            string displayPath,
            bool recursive,
            ISet<string> visitedFolders,
            ICollection<CloudFileEntry> files,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visitedFolders.Add(folderId))
            {
                return;
            }

            var folderFiles = new List<GoogleDriveFile>();
            string pageToken = null;
            do
            {
                var response = await ListFolderPageAsync(
                    accessToken,
                    folderId,
                    pageToken,
                    cancellationToken).ConfigureAwait(false);

                folderFiles.AddRange(response.Files ?? Enumerable.Empty<GoogleDriveFile>());

                pageToken = response.NextPageToken;
            }
            while (!string.IsNullOrWhiteSpace(pageToken));

            foreach (var file in folderFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateListedFile(file);
                if (!string.Equals(file.MimeType, FolderMimeType, StringComparison.Ordinal)) continue;
                if (recursive)
                {
                    await ScanFolderAsync(
                        accessToken,
                        file.Id,
                        CombineLogicalPath(displayPath, file.Name),
                        true,
                        visitedFolders,
                        files,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            var regularFiles = folderFiles
                .Where(file => !string.Equals(file.MimeType, FolderMimeType, StringComparison.Ordinal))
                .ToList();
            foreach (var cloudFile in regularFiles.Select(file =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateListedFile(file);
                var logicalPath = CombineLogicalPath(displayPath, file.Name);
                return new CloudFileEntry(
                    file.Id,
                    FirstNonEmpty(file.Md5Checksum, file.Version, file.ModifiedTime),
                    logicalPath,
                    file.Name,
                    ParseSize(logicalPath, file.Size),
                    ParseModifiedAt(logicalPath, file));
            }))
            {
                files.Add(cloudFile);
            }
        }

        private static void ValidateListedFile(GoogleDriveFile file)
        {
            if (file == null || string.IsNullOrWhiteSpace(file.Id) || string.IsNullOrWhiteSpace(file.Name))
            {
                throw new InvalidDataException("Google Drive returned a file without an ID or name.");
            }

            ValidateObjectId(file.Id);
        }

        private static long ParseSize(string logicalPath, string value)
        {
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size < 0)
            {
                throw new InvalidDataException($"Google Drive returned an invalid size for '{logicalPath}'.");
            }

            return size;
        }

        private static DateTimeOffset? ParseModifiedAt(string logicalPath, GoogleDriveFile file)
        {
            if (string.IsNullOrWhiteSpace(file.ModifiedTime)) return null;
            if (!DateTimeOffset.TryParse(
                file.ModifiedTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsedModifiedAt))
            {
                throw new InvalidDataException($"Google Drive returned an invalid modified time for '{logicalPath}'.");
            }

            return parsedModifiedAt;
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

        private async Task<IReadOnlyList<GoogleDriveFile>> ListFilesByQueryAsync(
            string accessToken,
            string query,
            CancellationToken cancellationToken)
        {
            var files = new List<GoogleDriveFile>();
            string pageToken = null;
            do
            {
                var fields = "nextPageToken,files(id,name,mimeType)";
                var uri = ApiBaseUri + "files" +
                          "?q=" + Uri.EscapeDataString(query) +
                          "&fields=" + Uri.EscapeDataString(fields) +
                          "&includeItemsFromAllDrives=true" +
                          "&supportsAllDrives=true" +
                          "&orderBy=" + Uri.EscapeDataString("name") +
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
                            $"Google Drive folder browsing failed ({(int)response.StatusCode}): {body}");
                    }

                    var page = GoogleDriveJson.Deserialize<GoogleDriveFileListResponse>(body)
                        ?? throw new InvalidDataException("Google Drive returned an empty folder browsing response.");
                    foreach (var file in page.Files ?? Enumerable.Empty<GoogleDriveFile>())
                    {
                        if (string.IsNullOrWhiteSpace(file.Id) || string.IsNullOrWhiteSpace(file.Name))
                        {
                            throw new InvalidDataException("Google Drive returned a folder without an ID or name.");
                        }

                        ValidateObjectId(file.Id);
                        if (!string.Equals(file.MimeType, FolderMimeType, StringComparison.Ordinal))
                        {
                            throw new InvalidDataException($"Google Drive returned non-folder object '{file.Name}' while browsing folders.");
                        }

                        files.Add(file);
                    }

                    pageToken = page.NextPageToken;
                }
            }
            while (!string.IsNullOrWhiteSpace(pageToken));

            return files;
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

        private static string ValidateObjectId(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId) || !ObjectIdPattern.IsMatch(objectId))
            {
                throw new ArgumentException("Google Drive object ID is invalid.", nameof(objectId));
            }

            return objectId;
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
