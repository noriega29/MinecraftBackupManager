using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Restic
{
    internal static class ResticBackupKindExtensions
    {
        public static string ToTag(this ResticBackupKind kind)
        {
            return kind switch
            {
                ResticBackupKind.Initial => "initial",
                ResticBackupKind.Manual => "manual",
                ResticBackupKind.Hourly => "hourly",
                ResticBackupKind.Daily => "daily",
                ResticBackupKind.Weekly => "weekly",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "El tipo de respaldo no es válido.")
            };
        }
    }
}
