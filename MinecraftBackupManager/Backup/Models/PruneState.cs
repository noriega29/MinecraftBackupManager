namespace MinecraftBackupManager.Backup.Models
{
    public sealed class PruneState
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        // Día del último prune exitoso (solo la fecha, sin hora).
        public DateOnly? LastSuccessDate { get; set; }

        // Día en que se anotaron los fallos y cuántos hubo ese día.
        public DateOnly? FailureDate { get; set; }

        public int FailureCount { get; set; }
    }
}
