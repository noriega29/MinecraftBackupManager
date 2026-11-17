using System.Buffers.Binary;
using MinecraftBackupManager.Minecraft.Models;
using MinecraftBackupManager.Minecraft.Protocol;

namespace MinecraftBackupManager.Tests.Minecraft.Protocol
{
    [TestClass]
    public sealed class RconPacketSerializerTests
    {
        [TestMethod]
        public void Serialize_ShouldGenerateCorrectPacket()
        {
            // Arrange
            var packet = new RconPacket(1, 2, "hi");

            byte[] expected =
            [
                12, 0, 0, 0,             // Longitud: 12 bytes
                1, 0, 0, 0,              // Request ID: 1
                2, 0, 0, 0,              // Tipo: 2
                (byte)'h', (byte)'i',    // Payload
                0, 0                     // Terminadores
            ];

            // Act
            byte[] result = RconPacketSerializer.Serialize(packet);

            // Assert
            CollectionAssert.AreEqual(expected, result);
        }

        [TestMethod]
        public void Deserialize_ShouldRecoverPacketData()
        {
            // Arrange
            var original = new RconPacket(25, 2, "say Hello");

            byte[] serialized = RconPacketSerializer.Serialize(original);

            // Act
            RconPacket result = RconPacketSerializer.Deserialize(serialized);

            // Assert
            Assert.AreEqual(original.RequestId, result.RequestId);
            Assert.AreEqual(original.Type, result.Type);
            Assert.AreEqual(original.Payload, result.Payload);
        }

        [TestMethod]
        public void Deserialize_ShouldPreserveNegativeRequestId()
        {
            // Arrange
            var packet = new RconPacket(-1, 2, string.Empty);
            byte[] serialized = RconPacketSerializer.Serialize(packet);

            // Act
            RconPacket result = RconPacketSerializer.Deserialize(serialized);

            // Assert
            Assert.AreEqual(-1, result.RequestId);
        }

        [TestMethod]
        public void Serialize_ShouldSupportEmptyPayload()
        {
            // Arrange
            var packet = new RconPacket(1, 2, string.Empty);

            // Act
            byte[] result = RconPacketSerializer.Serialize(packet);

            // Assert
            Assert.AreEqual(14, result.Length);
            Assert.AreEqual(10, BinaryPrimitives.ReadInt32LittleEndian(result));
            Assert.AreEqual(0, result[^1]);
            Assert.AreEqual(0, result[^2]);
        }

        [TestMethod]
        public void Deserialize_ShouldSupportUnicodePayload()
        {
            // Arrange
            var original = new RconPacket(10, 0, "¡Hola, Minecraft! 世界");

            byte[] serialized = RconPacketSerializer.Serialize(original);

            // Act
            RconPacket result = RconPacketSerializer.Deserialize(serialized);

            // Assert
            Assert.AreEqual(original.Payload, result.Payload);
        }

        [TestMethod]
        public void Deserialize_ShouldRejectBufferTooSmall()
        {
            // Arrange
            byte[] buffer = new byte[13];

            // Act & Assert
            AssertThrows<InvalidDataException>(
                () => RconPacketSerializer.Deserialize(buffer));
        }

        [TestMethod]
        public void Deserialize_ShouldRejectPacketLengthTooSmall()
        {
            // Arrange
            byte[] buffer = new byte[14];

            BinaryPrimitives.WriteInt32LittleEndian(
                buffer.AsSpan(0, 4), 9);

            // Act & Assert
            AssertThrows<InvalidDataException>(
                () => RconPacketSerializer.Deserialize(buffer));
        }

        [TestMethod]
        public void Deserialize_ShouldRejectPacketLengthTooLarge()
        {
            // Arrange
            byte[] buffer = new byte[14];

            BinaryPrimitives.WriteInt32LittleEndian(
                buffer.AsSpan(0, 4), 1_048_577);

            // Act & Assert
            AssertThrows<InvalidDataException>(
                () => RconPacketSerializer.Deserialize(buffer));
        }

        [TestMethod]
        public void Deserialize_ShouldRejectIncorrectDeclaredLength()
        {
            // Arrange
            var packet = new RconPacket(1, 2, "test");
            byte[] buffer = RconPacketSerializer.Serialize(packet);

            // Modificamos la longitud declarada.
            BinaryPrimitives.WriteInt32LittleEndian(
                buffer.AsSpan(0, 4), 10);

            // Act & Assert
            AssertThrows<InvalidDataException>(
                () => RconPacketSerializer.Deserialize(buffer));
        }

        [TestMethod]
        public void Deserialize_ShouldRejectMissingTerminators()
        {
            // Arrange
            var packet = new RconPacket(1, 2, "test");
            byte[] buffer = RconPacketSerializer.Serialize(packet);

            // Alteramos el último byte.
            buffer[^1] = 1;

            // Act & Assert
            AssertThrows<InvalidDataException>(
                () => RconPacketSerializer.Deserialize(buffer));
        }

        [TestMethod]
        public void Deserialize_ShouldRejectInvalidUtf8()
        {
            // Arrange
            byte[] buffer = new byte[15];

            BinaryPrimitives.WriteInt32LittleEndian(
                buffer.AsSpan(0, 4), 11);

            BinaryPrimitives.WriteInt32LittleEndian(
                buffer.AsSpan(4, 4), 1);

            BinaryPrimitives.WriteInt32LittleEndian(
                buffer.AsSpan(8, 4), 0);

            buffer[12] = 0xFF; // Byte que no representa UTF-8 válido.
            buffer[13] = 0;
            buffer[14] = 0;

            // Act & Assert
            AssertThrows<System.Text.DecoderFallbackException>(
                () => RconPacketSerializer.Deserialize(buffer));
        }

        [TestMethod]
        public void Serialize_ShouldRejectPayloadExceedingMaximumSize()
        {
            // Arrange
            const int maximumPacketLength = 1_048_576;
            const int packetOverhead = 10;

            string oversizedPayload = new('A', maximumPacketLength - packetOverhead + 1);
            var packet = new RconPacket(1, 2, oversizedPayload);

            // Act & Assert
            AssertThrows<ArgumentException>(
                () => RconPacketSerializer.Serialize(packet));
        }

        private static void AssertThrows<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception exception)
            {
                Assert.Fail(
                    $"Se esperaba {typeof(TException).Name}, " +
                    $"pero se produjo {exception.GetType().Name}.");
            }

            Assert.Fail(
                $"Se esperaba una excepción de tipo {typeof(TException).Name}.");
        }
    }
}
