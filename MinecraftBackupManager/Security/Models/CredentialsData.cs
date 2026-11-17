namespace MinecraftBackupManager.Security.Models
{
    public sealed class CredentialsData
    {
        public string RconPassword { get; set; } = string.Empty;
        public string ResticRepositoryPassword { get; set; } = string.Empty;
    }
}
