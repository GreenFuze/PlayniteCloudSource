using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    [DataContract]
    internal sealed class GoogleDriveToken
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
    internal sealed class GoogleDriveAboutResponse
    {
        [DataMember(Name = "user")]
        public GoogleDriveUser User { get; set; }
    }

    [DataContract]
    internal sealed class GoogleDriveUser
    {
        [DataMember(Name = "permissionId")]
        public string PermissionId { get; set; }

        [DataMember(Name = "displayName")]
        public string DisplayName { get; set; }

        [DataMember(Name = "emailAddress")]
        public string EmailAddress { get; set; }
    }

    [DataContract]
    internal sealed class GoogleDriveFileListResponse
    {
        [DataMember(Name = "nextPageToken")]
        public string NextPageToken { get; set; }

        [DataMember(Name = "files")]
        public List<GoogleDriveFile> Files { get; set; }
    }

    [DataContract]
    internal sealed class GoogleDriveFile
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "mimeType")]
        public string MimeType { get; set; }

        [DataMember(Name = "size")]
        public string Size { get; set; }

        [DataMember(Name = "md5Checksum")]
        public string Md5Checksum { get; set; }

        [DataMember(Name = "modifiedTime")]
        public string ModifiedTime { get; set; }

        [DataMember(Name = "version")]
        public string Version { get; set; }
    }
}
