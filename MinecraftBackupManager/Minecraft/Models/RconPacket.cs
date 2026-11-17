namespace MinecraftBackupManager.Minecraft.Models
{
    public sealed record RconPacket(
        int RequestId,
        int Type,
        string Payload
    );
}
