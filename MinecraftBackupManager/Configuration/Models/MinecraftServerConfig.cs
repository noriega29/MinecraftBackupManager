namespace MinecraftBackupManager.Configuration.Models
{
    public sealed class MinecraftServerConfig
    {
        public string ServerDirectory { get; set; } = string.Empty;
        public string RconHost { get; set; } = "127.0.0.1";
        public int RconPort { get; set; } = 25575;
    }
}
