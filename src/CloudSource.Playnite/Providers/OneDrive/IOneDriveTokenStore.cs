namespace CloudSource.Playnite.Providers.OneDrive
{
    internal interface IOneDriveTokenStore
    {
        bool Exists { get; }
        OneDriveToken Load();
        void Save(OneDriveToken token);
        void Clear();
    }
}
