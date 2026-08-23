namespace CloudSource.Playnite.Providers
{
    internal interface IKnownLengthStream
    {
        long? ContentLength { get; }
    }
}
