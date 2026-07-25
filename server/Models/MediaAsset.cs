namespace Server.Models;

public enum MediaKind
{
    Image,
    Audio,
    Video
}

public class MediaAsset
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public GameMaster? Owner { get; set; }

    public required string FileName { get; set; }
    public required string RelativePath { get; set; }
    public MediaKind Kind { get; set; }
    public long SizeBytes { get; set; }
    public DateTime UploadedAtUtc { get; set; }
}
