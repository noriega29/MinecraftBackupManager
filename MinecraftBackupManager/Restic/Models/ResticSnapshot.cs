namespace MinecraftBackupManager.Restic.Models
{
    public sealed record ResticSnapshot(
        string Id,
        string ShortId,
        DateTimeOffset Time,
        string Host,
        IReadOnlyList<string> Paths,
        IReadOnlyList<string> Tags,
        long? TotalBytesProcessed
    );
}
