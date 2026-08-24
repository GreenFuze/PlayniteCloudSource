using System;

namespace CloudSource.Playnite.Providers.OneDrive
{
    internal enum OneDriveFolderKind
    {
        ProviderRoot,
        MyFiles,
        Folder
    }

    internal sealed class OneDriveFolder
    {
        public string Name { get; }
        public string ObjectId { get; }
        public string DisplayPath { get; }
        public OneDriveFolderKind Kind { get; }
        public bool CanBrowse => Kind != OneDriveFolderKind.ProviderRoot;
        public bool CanSelect => Kind == OneDriveFolderKind.Folder;
        public string Description => Kind == OneDriveFolderKind.MyFiles
            ? "Folders in this Microsoft account's OneDrive"
            : "Open folder";

        private OneDriveFolder(string name, string objectId, string displayPath, OneDriveFolderKind kind)
        {
            Name = Required(name, nameof(name));
            ObjectId = objectId?.Trim();
            DisplayPath = displayPath?.Trim();
            Kind = kind;
            if (kind != OneDriveFolderKind.ProviderRoot && string.IsNullOrWhiteSpace(ObjectId))
                throw new ArgumentException("A browsable OneDrive folder requires a stable object ID.", nameof(objectId));
            if (kind != OneDriveFolderKind.ProviderRoot && string.IsNullOrWhiteSpace(DisplayPath))
                throw new ArgumentException("A OneDrive location requires a display path.", nameof(displayPath));
        }

        public static OneDriveFolder ProviderRoot()
        {
            return new OneDriveFolder("OneDrive", null, null, OneDriveFolderKind.ProviderRoot);
        }

        public static OneDriveFolder MyFiles()
        {
            return new OneDriveFolder("My files", "root", "OneDrive", OneDriveFolderKind.MyFiles);
        }

        public static OneDriveFolder CreateChild(OneDriveFolder parent, string objectId, string name)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (!parent.CanBrowse) throw new InvalidOperationException("The parent OneDrive location cannot contain folders.");
            var safeName = Required(name, nameof(name)).Replace('/', '\u2215').Replace('\\', '\u2215');
            return new OneDriveFolder(
                safeName,
                objectId,
                parent.DisplayPath.TrimEnd('/') + "/" + safeName,
                OneDriveFolderKind.Folder);
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
            return value.Trim();
        }
    }
}
