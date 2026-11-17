using System.Buffers.Binary;
using System.Text;
using MinecraftBackupManager.Minecraft.Models;

namespace MinecraftBackupManager.Minecraft.Protocol
{
    internal static class RconPacketSerializer
    {
        private const int HeaderSize = 8;
        private const int TerminatorSize = 2;
        private const int LengthPrefixSize = 4;

        private const int MinimumPacketLength = HeaderSize + TerminatorSize;

        internal const int MaximumPacketLength = 1_048_576; // 1 MiB

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static byte[] Serialize(RconPacket packet)
        {
            ArgumentNullException.ThrowIfNull(packet);
            ArgumentNullException.ThrowIfNull(packet.Payload);

            byte[] payload = Encoding.UTF8.GetBytes(packet.Payload);

            int packetLength = HeaderSize + payload.Length + TerminatorSize;

            if (packetLength > MaximumPacketLength)
            {
                throw new ArgumentException($"El paquete RCON supera el tamaño máximo permitido.", nameof(packet));
            }

            byte[] buffer = new byte[LengthPrefixSize + packetLength];

            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, LengthPrefixSize), packetLength);

            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(LengthPrefixSize, 4), packet.RequestId);

            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(LengthPrefixSize + 4, 4), packet.Type);

            payload.CopyTo(buffer.AsSpan(LengthPrefixSize + HeaderSize));

            // Los dos bytes finales son siempre 0
            return buffer;
        }

        public static RconPacket Deserialize(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < LengthPrefixSize + MinimumPacketLength)
            {
                throw new InvalidDataException("El paquete RCON es demasiado pequeño.");
            }

            int packetLength = BinaryPrimitives.ReadInt32LittleEndian(buffer[..LengthPrefixSize]);

            if (packetLength < MinimumPacketLength || packetLength > MaximumPacketLength)
            {
                throw new InvalidDataException("El paquete RCON tiene un tamaño inválido.");
            }

            if (buffer.Length != LengthPrefixSize + packetLength)
            {
                throw new InvalidDataException("La longitud recibida no coincide con la longitud declarada.");
            }

            if (buffer[^1] != 0 || buffer[^2] != 0)
            {
                throw new InvalidDataException("El paquete RCON tiene un terminador de cadena inválido.");
            }

            int requestId = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(LengthPrefixSize, 4));
            int type = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(LengthPrefixSize + 4, 4));
            int payloadLength = packetLength - MinimumPacketLength;

            string payload = StrictUtf8.GetString(buffer.Slice(LengthPrefixSize + HeaderSize, payloadLength));

            return new RconPacket(requestId, type, payload);
        }
    }
}
