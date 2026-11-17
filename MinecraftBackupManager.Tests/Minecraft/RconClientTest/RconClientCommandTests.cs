using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using MinecraftBackupManager.Minecraft;
using MinecraftBackupManager.Minecraft.Models;
using MinecraftBackupManager.Minecraft.Protocol;

namespace MinecraftBackupManager.Tests.Minecraft.RconClientTest
{
    [TestClass]
    public sealed class RconClientCommandTests
    {
        [TestMethod]
        public async Task ExecuteCommand_ShouldReturnSinglePacketResponse()
        {
            // Arrange
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            RconPacket request = await ReadPacketAsync(context.Stream);

            Assert.AreEqual(
                (int)RconPacketType.ExecuteCommand,
                request.Type);

            Assert.AreEqual("list", request.Payload);

            // El servidor envía una única respuesta.
            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    request.RequestId,
                    (int)RconPacketType.ResponseValue,
                    "There are 2 players online."));

            // El cliente envía el marcador.
            RconPacket marker = await ReadPacketAsync(context.Stream);

            Assert.AreEqual(100, marker.Type);
            Assert.AreEqual("probe", marker.Payload);

            // El servidor responde al marcador.
            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    marker.RequestId,
                    (int)RconPacketType.ResponseValue,
                    "Unknown request 64"));

            // Act
            string result = await commandTask;

            // Assert
            Assert.AreEqual("There are 2 players online.", result);
            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsTrue(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommand_ShouldConcatenateMultiplePackets()
        {
            // Arrange
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("help");

            RconPacket request = await ReadPacketAsync(context.Stream);

            // Primera respuesta.
            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    request.RequestId,
                    0,
                    "Primera parte "));

            // El cliente debe enviar el marcador después de recibir
            // la primera respuesta.
            RconPacket marker = await ReadPacketAsync(context.Stream);

            Assert.AreEqual(100, marker.Type);

            // Respuestas restantes del comando.
            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    request.RequestId,
                    0,
                    "segunda parte "));

            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    request.RequestId,
                    0,
                    "tercera parte"));

            // Respuesta del marcador.
            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    marker.RequestId,
                    0,
                    "Unknown request 64"));

            // Act
            string result = await commandTask;

            // Assert
            Assert.AreEqual(
                "Primera parte segunda parte tercera parte",
                result);
        }

        [TestMethod]
        public async Task ExecuteCommand_ShouldSendMarkerAfterFirstResponse()
        {
            // Arrange
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            RconPacket request = await ReadPacketAsync(context.Stream);

            // El servidor envía la primera respuesta.
            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    request.RequestId,
                    0,
                    "Respuesta"));

            // Act: esperar el paquete marcador.
            RconPacket marker = await ReadPacketAsync(context.Stream);

            // Assert
            Assert.AreEqual(100, marker.Type);
            Assert.AreEqual("probe", marker.Payload);

            Assert.AreNotEqual(
                request.RequestId,
                marker.RequestId);

            // Completar la ejecución.
            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    marker.RequestId,
                    0,
                    "Unknown request 64"));

            await commandTask;
        }

        [TestMethod]
        public async Task ExecuteCommand_ShouldStopWhenMarkerIsReceived()
        {
            // Arrange
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            RconPacket request = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(request.RequestId, 0, "Resultado"));

            RconPacket marker = await ReadPacketAsync(context.Stream);

            // El servidor envía el marcador.
            await WritePacketAsync(
                context.Stream,
                new RconPacket(marker.RequestId, 0, "Unknown request 64"));

            // Act
            string result = await commandTask.WaitAsync(
                TimeSpan.FromSeconds(5));

            // Assert
            Assert.AreEqual("Resultado", result);
            Assert.IsTrue(context.Client.IsConnected);
        }

        [TestMethod]
        public async Task ExecuteCommand_ShouldIgnoreMarkerPayload()
        {
            // Arrange
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            RconPacket request = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(request.RequestId, 0, "Respuesta válida"));

            RconPacket marker = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    marker.RequestId,
                    0,
                    "Unknown request 64"));

            // Act
            string result = await commandTask;

            // Assert
            Assert.AreEqual("Respuesta válida", result);
            Assert.IsFalse(result.Contains("Unknown request"));
        }

        [TestMethod]
        public async Task ExecuteCommand_WithUnexpectedRequestId_ThrowsInvalidDataException()
        {
            // Arrange
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            RconPacket request = await ReadPacketAsync(context.Stream);

            // El servidor responde con un ID incorrecto.
            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    request.RequestId + 100,
                    0,
                    "Respuesta inesperada"));

            // Act & Assert
            await AssertThrowsAsync<InvalidDataException>(
                () => commandTask);

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommand_WithUnexpectedPacketType_ThrowsInvalidDataException()
        {
            // Arrange
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            RconPacket request = await ReadPacketAsync(context.Stream);

            // El servidor responde con un tipo incorrecto.
            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    request.RequestId,
                    3,
                    "Respuesta inesperada"));

            // Act & Assert
            await AssertThrowsAsync<InvalidDataException>(
                () => commandTask);

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommand_WhenCancelled_ThrowsOperationCanceledException()
        {
            // Arrange
            using var context = await CreateAuthenticatedConnectionAsync();
            using var cancellationTokenSource = new CancellationTokenSource();

            Task<string> commandTask = context.Client.ExecuteCommandAsync(
                "list",
                cancellationTokenSource.Token);

            RconPacket request = await ReadPacketAsync(context.Stream);

            // El servidor responde al comando.
            await WritePacketAsync(
                context.Stream,
                new RconPacket(request.RequestId, 0, "Respuesta"));

            // Esperar a que el cliente envíe el marcador.
            await ReadPacketAsync(context.Stream);

            // Cancelar mientras el cliente espera la respuesta del marcador.
            await cancellationTokenSource.CancelAsync();

            // Act & Assert
            await AssertThrowsAsync<OperationCanceledException>(
                () => commandTask);

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommand_WhenServerDisconnects_ThrowsIOException()
        {
            // Arrange
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            RconPacket request = await ReadPacketAsync(context.Stream);

            // El servidor envía la primera respuesta.
            await WritePacketAsync(
                context.Stream,
                new RconPacket(request.RequestId, 0, "Respuesta"));

            // El cliente envía el marcador.
            await ReadPacketAsync(context.Stream);

            // El servidor cierra la conexión sin responder al marcador.
            context.ServerClient.Close();

            // Act & Assert
            await AssertThrowsAsync<IOException>(
                () => commandTask);

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommand_WithoutAuthentication_ThrowsInvalidOperationException()
        {
            // Arrange
            var listener = CreateStartedListener(out int port);

            try
            {
                using var client = new RconClient("127.0.0.1", port);

                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

                await client.ConnectAsync();

                using TcpClient serverClient =
                    await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));

                // Act & Assert
                await AssertThrowsAsync<InvalidOperationException>(
                    () => client.ExecuteCommandAsync("list"));

                Assert.IsTrue(client.IsConnected);
                Assert.IsFalse(client.IsAuthenticated);
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public async Task ExecuteCommand_WithEmptyCommand_ThrowsArgumentException()
        {
            // Arrange
            using var context = await CreateAuthenticatedConnectionAsync();

            // Act & Assert
            await AssertThrowsAsync<ArgumentException>(
                () => context.Client.ExecuteCommandAsync(string.Empty));

            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsTrue(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommand_WhenServerDisconnectsBeforeFirstResponse()
        {
            // Arrange
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            // El servidor recibe el comando.
            RconPacket request = await ReadPacketAsync(context.Stream);

            Assert.AreEqual(
                (int)RconPacketType.ExecuteCommand,
                request.Type);

            // El servidor se desconecta sin enviar ninguna respuesta.
            context.ServerClient.Close();

            // Act & Assert
            await AssertThrowsAsync<IOException>(
                () => commandTask.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommand_WhenServerSendsIncompletePacket()
        {
            // Arrange
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            // El servidor recibe el comando.
            RconPacket request = await ReadPacketAsync(context.Stream);

            Assert.AreEqual(
                (int)RconPacketType.ExecuteCommand,
                request.Type);

            // Anunciar que el paquete contiene 20 bytes.
            byte[] lengthBuffer = new byte[4];

            BinaryPrimitives.WriteInt32LittleEndian(
                lengthBuffer,
                20);

            await context.Stream.WriteAsync(lengthBuffer.AsMemory());

            // Enviar solamente 5 de los 20 bytes anunciados.
            byte[] partialPacket = { 1, 2, 3, 4, 5 };

            await context.Stream.WriteAsync(partialPacket.AsMemory());

            // Cerrar la conexión antes de completar el paquete.
            context.ServerClient.Close();

            // Act & Assert
            await AssertThrowsAsync<IOException>(
                () => commandTask.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommand_WhenCancelledBeforeFirstResponse()
        {
            // Arrange
            using var context = await CreateAuthenticatedConnectionAsync();

            using var cancellationTokenSource = new CancellationTokenSource();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync(
                    "list",
                    cancellationTokenSource.Token);

            // El servidor recibe el comando, pero no responde.
            RconPacket request = await ReadPacketAsync(context.Stream);

            Assert.AreEqual(
                (int)RconPacketType.ExecuteCommand,
                request.Type);

            // Cancelar mientras el cliente espera la primera respuesta.
            await cancellationTokenSource.CancelAsync();

            // Act & Assert
            await AssertThrowsAsync<OperationCanceledException>(
                () => commandTask.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        private static async Task<TestConnection> CreateAuthenticatedConnectionAsync()
        {
            TcpListener listener = CreateStartedListener(out int port);

            var client = new RconClient("127.0.0.1", port);

            TcpClient? serverClient = null;

            try
            {
                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

                await client.ConnectAsync();

                serverClient = await acceptTask.WaitAsync(
                    TimeSpan.FromSeconds(5));

                NetworkStream stream = serverClient.GetStream();

                // Iniciar autenticación.
                Task authenticationTask =
                    client.AuthenticateAsync("test-password");

                // Recibir solicitud de autenticación.
                RconPacket request = await ReadPacketAsync(stream);

                Assert.AreEqual(
                    (int)RconPacketType.Authentication,
                    request.Type);

                Assert.AreEqual("test-password", request.Payload);

                // Confirmar autenticación.
                await WritePacketAsync(
                    stream,
                    new RconPacket(
                        request.RequestId,
                        (int)RconPacketType.AuthenticationResponse,
                        string.Empty));

                await authenticationTask;

                return new TestConnection(
                    listener,
                    client,
                    serverClient,
                    stream);
            }
            catch
            {
                serverClient?.Dispose();
                client.Dispose();
                listener.Stop();

                throw;
            }
        }

        private static TcpListener CreateStartedListener(out int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);

            listener.Start();

            port = ((IPEndPoint)listener.LocalEndpoint).Port;

            return listener;
        }

        private static async Task<RconPacket> ReadPacketAsync(
            NetworkStream stream)
        {
            byte[] lengthBuffer = new byte[4];

            await stream.ReadExactlyAsync(
                lengthBuffer.AsMemory())
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));

            int packetLength = BinaryPrimitives.ReadInt32LittleEndian(
                lengthBuffer);

            byte[] packetBuffer = new byte[4 + packetLength];

            lengthBuffer.CopyTo(packetBuffer, 0);

            await stream.ReadExactlyAsync(
                packetBuffer.AsMemory(4, packetLength))
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));

            return RconPacketSerializer.Deserialize(packetBuffer);
        }

        private static async Task WritePacketAsync(
            NetworkStream stream,
            RconPacket packet)
        {
            byte[] data = RconPacketSerializer.Serialize(packet);

            await stream.WriteAsync(data.AsMemory());
        }

        private static async Task AssertThrowsAsync<TException>(
            Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action();
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

        private sealed class TestConnection : IDisposable
        {
            private readonly TcpListener _listener;

            public RconClient Client { get; }
            public TcpClient ServerClient { get; }
            public NetworkStream Stream { get; }

            public TestConnection(
                TcpListener listener,
                RconClient client,
                TcpClient serverClient,
                NetworkStream stream)
            {
                _listener = listener;
                Client = client;
                ServerClient = serverClient;
                Stream = stream;
            }

            public void Dispose()
            {
                Client.Dispose();
                ServerClient.Dispose();
                _listener.Stop();
            }
        }
    }
}
