namespace MinecraftBackupManager.Configuration.Models
{
    public sealed class ApplicationConfig
    {
        public const int CurrentSchemaVersion = 1;
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public MinecraftServerConfig MinecraftServer { get; set; } = new();
        public ResticConfig Restic { get; set; } = new();
        public RetentionConfig Retention { get; set; } = new();
    }
}
