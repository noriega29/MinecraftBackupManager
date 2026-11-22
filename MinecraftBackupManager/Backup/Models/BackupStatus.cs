namespace MinecraftBackupManager.Backup.Models
{
    public enum BackupStatus
    {
        Completed,
        CompletedWithWarnings,
        Failed,
        AlreadyRunning,

        /// <summary>
        /// El respaldo no se hizo a propósito: el servidor está apagado y la
        /// configuración no permite respaldar en ese caso. No es un error.
        /// </summary>
        Skipped
    }
}
