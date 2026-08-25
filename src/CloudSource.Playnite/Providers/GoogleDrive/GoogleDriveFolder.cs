using System;

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    internal enum GoogleDriveFolderKind
    {
        ProviderRoot,
        MyDrive,
        SharedWithMe,
        Folder
    }

    internal sealed class GoogleDriveFolder
    {
        public string Name { get; }
        public string ObjectId { get; }
        public string DisplayPath { get; }
        public GoogleDriveFolderKind Kind { get; }
        public bool CanBrowse => Kind != GoogleDriveFolderKind.ProviderRoot;
        public bool CanSelect => Kind == GoogleDriveFolderKind.Folder;
        public string Description
        {
            get
            {
                switch (Kind)
                {
                    case GoogleDriveFolderKind.MyDrive:
                        return "Folders owned by this Google account";
                    case GoogleDriveFolderKind.SharedWithMe:
                        return "Folders shared directly with this Google account";
                    default:
                        return "Open folder";
                }
            }
        }

        private GoogleDriveFolder(
            string name,
            string objectId,
            string displayPath,
            GoogleDriveFolderKind kind)
        {
            Name = Required(name, nameof(name));
            ObjectId = objectId?.Trim();
            DisplayPath = displayPath?.Trim();
            Kind = kind;

            if ((kind == GoogleDriveFolderKind.MyDrive || kind == GoogleDriveFolderKind.Folder) &&
                string.IsNullOrWhiteSpace(ObjectId))
            {
                throw new ArgumentException("A browsable Google Drive folder requires a stable object ID.", nameof(objectId));
            }

            if (kind != GoogleDriveFolderKind.ProviderRoot && string.IsNullOrWhiteSpace(DisplayPath))
            {
                throw new ArgumentException("A Google Drive location requires a display path.", nameof(displayPath));
            }
        }

        public static GoogleDriveFolder ProviderRoot()
        {
            return new GoogleDriveFolder("Google Drive", null, null, GoogleDriveFolderKind.ProviderRoot);
        }

        public static GoogleDriveFolder MyDrive()
        {
            return new GoogleDriveFolder("My Drive", "root", "My Drive", GoogleDriveFolderKind.MyDrive);
        }

        public static GoogleDriveFolder SharedWithMe()
        {
            return new GoogleDriveFolder("Shared with me", null, "Shared with me", GoogleDriveFolderKind.SharedWithMe);
        }

        public static GoogleDriveFolder CreateChild(GoogleDriveFolder parent, string objectId, string name)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (!parent.CanBrowse)
            {
                throw new InvalidOperationException("The parent Google Drive location cannot contain folders.");
            }

            var safeName = Required(name, nameof(name)).Replace('/', '\u2215').Replace('\\', '\u2215');
            var displayPath = parent.DisplayPath.TrimEnd('/') + "/" + safeName;
            return new GoogleDriveFolder(safeName, objectId, displayPath, GoogleDriveFolderKind.Folder);
        }

        public static GoogleDriveFolder CreatePickerSelection(string objectId, string name)
        {
            var safeName = Required(name, nameof(name)).Replace('/', '\u2215').Replace('\\', '\u2215');
            return new GoogleDriveFolder(
                safeName,
                objectId,
                "Google Drive/" + safeName,
                GoogleDriveFolderKind.Folder);
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
