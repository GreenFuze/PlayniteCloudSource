using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CloudSource.Playnite.Providers.OneDrive
{
    internal sealed class OneDriveApiClient
    {
        private const string GraphBaseUri = "https://graph.microsoft.com/v1.0/";
        private const string ItemSelect = "id,name,size,eTag,cTag,lastModifiedDateTime,folder,file";
        private readonly HttpClient httpClient;
        private readonly OneDriveConnectionService connectionService;
        private readonly CloudPackageDiscovery packageDiscovery;

        public OneDriveApiClient(
            HttpClient httpClient,
            OneDriveConnectionService connectionService,
            CloudPackageDiscovery packageDiscovery)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            this.packageDiscovery = packageDiscovery ?? throw new ArgumentNullException(nameof(packageDiscovery));
        }

        public async Task<IReadOnlyList<SourcePackage>> ScanAsync(
            OneDriveAccountConfiguration configuration,
            SourceScanRequest request,
            CancellationToken cancellationToken)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!string.Equals(configuration.AccountId, request.AccountId, StringComparison.Ordinal))
                throw new InvalidOperationException("The scan request does not belong to the configured OneDrive account.");

            var accessToken = await connectionService.GetAccessTokenAsync(configuration, cancellationToken).ConfigureAwait(false);
            var packages = new List<SourcePackage>();
            var visitedFolders = new HashSet<string>(StringComparer.Ordinal);
            foreach (var location in request.Locations)
            {
                await ScanFolderAsync(
                    accessToken,
                    configuration.AccountId,
                    RequiredObjectId(location.ObjectId),
                    location.DisplayPath,
                    location.Recursive,
                    visitedFolders,
                    packages,
                    cancellationToken).ConfigureAwait(false);
            }
            return packages.OrderBy(package => package.LogicalPath, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public Task<Stream> OpenReadAsync(
            OneDriveAccountConfiguration configuration,
            SourcePackage package,
            CancellationToken cancellationToken)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            return OpenReadFileAsync(
                configuration,
                package,
                package.Files.Single(file => file.Role == SourcePackageFileRole.Primary),
                cancellationToken);
        }

        public async Task<Stream> OpenReadFileAsync(
            OneDriveAccountConfiguration configuration,
            SourcePackage package,
            SourcePackageFile file,
            CancellationToken cancellationToken)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (!string.Equals(package.ProviderId, OneDriveProvider.ProviderId, StringComparison.Ordinal) ||
                !string.Equals(package.AccountId, configuration.AccountId, StringComparison.Ordinal) ||
                !package.Files.Any(candidate => string.Equals(candidate.ObjectId, file.ObjectId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("The package file does not belong to the configured OneDrive account.");
            }

            var accessToken = await connectionService.GetAccessTokenAsync(configuration, cancellationToken).ConfigureAwait(false);
            var uri = GraphBaseUri + "me/drive/items/" + Uri.EscapeDataString(RequiredObjectId(file.ObjectId)) + "/content";
            var request = CreateAuthorizedRequest(HttpMethod.Get, uri, accessToken);
            var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            request.Dispose();
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new InvalidOperationException($"OneDrive download failed ({status}): {body}");
            }
            return new HttpResponseStream(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), response);
        }

        public async Task<IReadOnlyList<OneDriveFolder>> ListFoldersAsync(
            OneDriveAccountConfiguration configuration,
            OneDriveFolder parent,
            OneDriveAuthorization draftAuthorization,
            CancellationToken cancellationToken)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (!parent.CanBrowse) throw new InvalidOperationException($"OneDrive location '{parent.Name}' cannot be browsed.");
            var accessToken = await connectionService
                .GetAccessTokenAsync(configuration, draftAuthorization, cancellationToken)
                .ConfigureAwait(false);
            var items = await ListChildrenAsync(accessToken, parent.ObjectId, cancellationToken).ConfigureAwait(false);
            return items
                .Where(item => item.Folder != null)
                .Select(item => OneDriveFolder.CreateChild(parent, item.Id, item.Name))
                .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
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
            if (!visitedFolders.Add(folderId)) return;
            var items = await ListChildrenAsync(accessToken, folderId, cancellationToken).ConfigureAwait(false);
            foreach (var folder in items.Where(item => item.Folder != null))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateItem(folder);
                if (recursive)
                {
                    await ScanFolderAsync(
                        accessToken,
                        accountId,
                        folder.Id,
                        CombineLogicalPath(displayPath, folder.Name),
                        true,
                        visitedFolders,
                        packages,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            var files = new List<CloudFileEntry>();
            foreach (var item in items.Where(candidate => candidate.File != null))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateItem(item);
                var logicalPath = CombineLogicalPath(displayPath, item.Name);
                files.Add(new CloudFileEntry(
                    item.Id,
                    FirstNonEmpty(item.ETag, item.CTag, item.LastModifiedDateTime),
                    logicalPath,
                    item.Name,
                    item.Size,
                    ParseModifiedAt(logicalPath, item.LastModifiedDateTime)));
            }
            foreach (var package in packageDiscovery.Discover(OneDriveProvider.ProviderId, accountId, files))
                packages.Add(package);
        }

        private async Task<IReadOnlyList<OneDriveItem>> ListChildrenAsync(
            string accessToken,
            string folderId,
            CancellationToken cancellationToken)
        {
            folderId = RequiredObjectId(folderId);
            var uri = string.Equals(folderId, "root", StringComparison.OrdinalIgnoreCase)
                ? GraphBaseUri + "me/drive/root/children?$select=" + Uri.EscapeDataString(ItemSelect)
                : GraphBaseUri + "me/drive/items/" + Uri.EscapeDataString(folderId) + "/children?$select=" + Uri.EscapeDataString(ItemSelect);
            var items = new List<OneDriveItem>();
            var visitedPages = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrWhiteSpace(uri))
            {
                ValidateGraphUri(uri);
                if (!visitedPages.Add(uri)) throw new InvalidDataException("Microsoft Graph returned a cyclic paging link.");
                using (var request = CreateAuthorizedRequest(HttpMethod.Get, uri, accessToken))
                using (var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException($"OneDrive folder listing failed ({(int)response.StatusCode}): {body}");
                    var page = OneDriveJson.Deserialize<OneDriveItemsResponse>(body)
                        ?? throw new InvalidDataException("OneDrive returned an empty folder listing response.");
                    foreach (var item in page.Items ?? Enumerable.Empty<OneDriveItem>())
                    {
                        ValidateItem(item);
                        items.Add(item);
                    }
                    uri = page.NextLink;
                }
            }
            return items;
        }

        private static void ValidateItem(OneDriveItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidDataException("OneDrive returned an item without an ID or name.");
            RequiredObjectId(item.Id);
            if (item.Size < 0) throw new InvalidDataException($"OneDrive returned an invalid size for '{item.Name}'.");
        }

        private static void ValidateGraphUri(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
                !uri.AbsolutePath.StartsWith("/v1.0/", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Microsoft Graph returned an invalid paging link.");
            }
        }

        private static DateTimeOffset? ParseModifiedAt(string logicalPath, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                throw new InvalidDataException($"OneDrive returned an invalid modified time for '{logicalPath}'.");
            return parsed;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            var value = values.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
            if (value == null) throw new InvalidDataException("OneDrive returned a file without revision information.");
            return value.Trim();
        }

        private static string CombineLogicalPath(string parent, string name)
        {
            if (string.IsNullOrWhiteSpace(parent)) throw new ArgumentException("Logical parent path is required.", nameof(parent));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Logical child name is required.", nameof(name));
            return parent.Trim().TrimEnd('/') + "/" + name.Trim().Replace('/', '\u2215').Replace('\\', '\u2215');
        }

        private static string RequiredObjectId(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId) || objectId.Length > 512 || objectId.Any(char.IsControl))
                throw new ArgumentException("OneDrive object ID is invalid.", nameof(objectId));
            return objectId.Trim();
        }

        private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string uri, string accessToken)
        {
            var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            return request;
        }
    }
}
