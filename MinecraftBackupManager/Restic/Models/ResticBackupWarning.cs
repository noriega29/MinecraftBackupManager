namespace MinecraftBackupManager.Restic.Models
{
    public sealed record ResticBackupWarning
    (
        string Item,
        string Message
    );
}
