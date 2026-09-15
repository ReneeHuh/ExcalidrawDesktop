namespace ExcalidrawDesktop.Core;

public sealed record DesktopFileStamp(DateTimeOffset ModifiedAt, ulong Size)
{
    public bool DiffersFrom(DesktopFileStamp other) =>
        ModifiedAt != other.ModifiedAt || Size != other.Size;
}
