namespace MinecraftBackupManager.Configuration.Models
{
    public sealed class MinecraftServerConfig
    {
        public const bool DefaultAllowBackupWhenServerIsOff = true;

        public string ServerDirectory { get; set; } = string.Empty;
        public string RconHost { get; set; } = "127.0.0.1";
        public int RconPort { get; set; } = 25575;

        // Si es false, los respaldos automáticos se omiten mientras el servidor
        // está apagado. Los respaldos manual e initial se hacen siempre.
        public bool AllowBackupWhenServerIsOff { get; set; } = DefaultAllowBackupWhenServerIsOff;
    }
}
