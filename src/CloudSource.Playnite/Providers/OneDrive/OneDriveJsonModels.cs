using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CloudSource.Playnite.Providers.OneDrive
{
    [DataContract]
    internal sealed class OneDriveToken
    {
        [DataMember(Name = "access_token")]
        public string AccessToken { get; set; }
        [DataMember(Name = "refresh_token")]
        public string RefreshToken { get; set; }
        [DataMember(Name = "token_type")]
        public string TokenType { get; set; }
        [DataMember(Name = "scope")]
        public string Scope { get; set; }
        [DataMember(Name = "expires_in")]
        public long ExpiresInSeconds { get; set; }
        [DataMember(Name = "expires_at_utc")]
        public DateTime ExpiresAtUtc { get; set; }
    }

    [DataContract]
    internal sealed class OneDriveUser
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }
        [DataMember(Name = "displayName")]
        public string DisplayName { get; set; }
        [DataMember(Name = "mail")]
        public string Mail { get; set; }
        [DataMember(Name = "userPrincipalName")]
        public string UserPrincipalName { get; set; }
    }

    [DataContract]
    internal sealed class OneDriveItemsResponse
    {
        [DataMember(Name = "value")]
        public List<OneDriveItem> Items { get; set; }
        [DataMember(Name = "@odata.nextLink")]
        public string NextLink { get; set; }
    }

    [DataContract]
    internal sealed class OneDriveItem
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }
        [DataMember(Name = "name")]
        public string Name { get; set; }
        [DataMember(Name = "size")]
        public long Size { get; set; }
        [DataMember(Name = "eTag")]
        public string ETag { get; set; }
        [DataMember(Name = "cTag")]
        public string CTag { get; set; }
        [DataMember(Name = "lastModifiedDateTime")]
        public string LastModifiedDateTime { get; set; }
        [DataMember(Name = "folder")]
        public OneDriveFolderFacet Folder { get; set; }
        [DataMember(Name = "file")]
        public OneDriveFileFacet File { get; set; }
    }

    [DataContract]
    internal sealed class OneDriveFolderFacet
    {
        [DataMember(Name = "childCount")]
        public long ChildCount { get; set; }
    }

    [DataContract]
    internal sealed class OneDriveFileFacet
    {
        [DataMember(Name = "mimeType")]
        public string MimeType { get; set; }
    }
}
