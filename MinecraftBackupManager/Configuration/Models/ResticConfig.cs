namespace MinecraftBackupManager.Configuration.Models
{
    public enum ResticExecutableSource
    {
        Bundled,
        Custom
    }

    public sealed class ResticConfig
    {
        public ResticExecutableSource ExecutableSource { get; set; } = ResticExecutableSource.Bundled;
        public string? CustomExecutablePath { get; set; }
        public string RepositoryPath { get; set; } = string.Empty;
    }
}
