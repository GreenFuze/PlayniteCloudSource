namespace CloudSource.Playnite.Providers.GoogleDrive
{
    internal interface IGoogleDriveTokenStore
    {
        bool Exists { get; }
        GoogleDriveToken Load();
        void Save(GoogleDriveToken token);
        void Clear();
    }
}
