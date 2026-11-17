using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using MinecraftBackupManager.Minecraft;
using MinecraftBackupManager.Minecraft.Models;
using MinecraftBackupManager.Minecraft.Protocol;

namespace MinecraftBackupManager.Tests.Minecraft.RconClientTest
{
    [TestClass]
    public sealed class RconClientTimeoutTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(250);

        // ---------------------------------------------------------
        // Validación de propiedades
        // ---------------------------------------------------------

        [TestMethod]
        public void ConnectionTimeout_ShouldRejectZeroOrNegativeValues()
        {
            using var client = new RconClient("127.0.0.1", 25575);

            AssertThrows<ArgumentOutOfRangeException>(() => client.ConnectionTimeout = TimeSpan.Zero);

            AssertThrows<ArgumentOutOfRangeException>(
                () => client.ConnectionTimeout = TimeSpan.FromSeconds(-1));
        }

        [TestMethod]
        public void AuthenticationTimeout_ShouldRejectZeroOrNegativeValues()
        {
            using var client = new RconClient("127.0.0.1", 25575);

            AssertThrows<ArgumentOutOfRangeException>(
                () => client.AuthenticationTimeout = TimeSpan.Zero);

            AssertThrows<ArgumentOutOfRangeException>(
                () => client.AuthenticationTimeout = TimeSpan.FromSeconds(-1));
        }

        [TestMethod]
        public void CommandTimeout_ShouldRejectZeroOrNegativeValues()
        {
            using var client = new RconClient("127.0.0.1", 25575);

            AssertThrows<ArgumentOutOfRangeException>(
                () => client.CommandTimeout = TimeSpan.Zero);

            AssertThrows<ArgumentOutOfRangeException>(
                () => client.CommandTimeout = TimeSpan.FromSeconds(-1));
        }

        [TestMethod]
        public void TimeoutProperties_ShouldAcceptPositiveValues()
        {
            using var client = new RconClient("127.0.0.1", 25575);

            client.ConnectionTimeout = TimeSpan.FromSeconds(3);
            client.AuthenticationTimeout = TimeSpan.FromSeconds(4);
            client.CommandTimeout = TimeSpan.FromSeconds(6);

            Assert.AreEqual(TimeSpan.FromSeconds(3), client.ConnectionTimeout);
            Assert.AreEqual(TimeSpan.FromSeconds(4), client.AuthenticationTimeout);
            Assert.AreEqual(TimeSpan.FromSeconds(6), client.CommandTimeout);
        }

        // ---------------------------------------------------------
        // Timeout de autenticación
        // ---------------------------------------------------------

        [TestMethod]
        public async Task AuthenticateAsync_WhenServerDoesNotRespond_ShouldThrowTimeoutException()
        {
            using var context = await CreateUnauthenticatedConnectionAsync();

            context.Client.AuthenticationTimeout = ShortTimeout;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Iniciar la autenticación.
            Task authenticationTask = context.Client.AuthenticateAsync("password");

            // Recibir la solicitud de autenticación.
            RconPacket request = await ReadPacketAsync(context.Server);

            Assert.AreEqual(
                (int)RconPacketType.Authentication,
                request.Type);

            // Esperar a que expire el timeout.
            await AssertThrowsAsync<TimeoutException>(
                () => authenticationTask.WaitAsync(TestTimeout));

            stopwatch.Stop();

            Assert.IsTrue(stopwatch.Elapsed >= ShortTimeout);

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task AuthenticateAsync_WhenTimeoutOccurs_ShouldDisconnectClient()
        {
            using var context = await CreateUnauthenticatedConnectionAsync();

            context.Client.AuthenticationTimeout = ShortTimeout;

            // Iniciar la autenticación.
            Task authenticationTask = context.Client.AuthenticateAsync("password");

            await ReadPacketAsync(context.Server);

            await AssertThrowsAsync<TimeoutException>(
                () => authenticationTask.WaitAsync(TestTimeout));

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task AuthenticateAsync_WhenExternallyCancelled_ShouldThrowOperationCanceledException()
        {
            using var context = await CreateUnauthenticatedConnectionAsync();

            context.Client.AuthenticationTimeout = TimeSpan.FromSeconds(10);

            using var cancellationSource = new CancellationTokenSource();

            Task authenticationTask = context.Client.AuthenticateAsync(
                "password",
                cancellationSource.Token);

            await ReadPacketAsync(context.Server);

            cancellationSource.Cancel();

            await AssertThrowsAsync<OperationCanceledException>(
                () => authenticationTask.WaitAsync(TestTimeout));

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        // ---------------------------------------------------------
        // Timeout de ejecución de comandos
        // ---------------------------------------------------------

        [TestMethod]
        public async Task ExecuteCommandAsync_WhenServerDoesNotRespond_ShouldThrowTimeoutException()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            context.Client.CommandTimeout = ShortTimeout;

            // Iniciar el comando.
            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            // El servidor recibe el comando, pero no responde.
            RconPacket request = await ReadPacketAsync(context.Server);

            Assert.AreEqual(
                (int)RconPacketType.ExecuteCommand,
                request.Type);

            // Esperar a que expire el timeout.
            await AssertThrowsAsync<TimeoutException>(
                () => commandTask.WaitAsync(TestTimeout));

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommandAsync_WhenMarkerDoesNotRespond_ShouldThrowTimeoutException()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            context.Client.CommandTimeout = ShortTimeout;

            // Iniciar el comando.
            Task<string> commandTask = context.Client.ExecuteCommandAsync("list");

            // Recibir el comando.
            RconPacket commandRequest = await ReadPacketAsync(context.Server);

            Assert.AreEqual(
                (int)RconPacketType.ExecuteCommand,
                commandRequest.Type);

            // Enviar la primera respuesta del comando.
            await WritePacketAsync(
                context.Server,
                new RconPacket(
                    commandRequest.RequestId,
                    (int)RconPacketType.ResponseValue,
                    "Respuesta parcial"));

            // El servidor recibe el paquete marcador.
            RconPacket markerRequest = await ReadPacketAsync(context.Server);

            Assert.AreEqual(100, markerRequest.Type);
            Assert.AreEqual("probe", markerRequest.Payload);

            // No se envía la respuesta del marcador.
            await AssertThrowsAsync<TimeoutException>(
                () => commandTask.WaitAsync(TestTimeout));

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommandAsync_WhenExternallyCancelled_ShouldThrowOperationCanceledException()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            context.Client.CommandTimeout = TimeSpan.FromSeconds(10);

            using var cancellationSource = new CancellationTokenSource();

            Task<string> commandTask = context.Client.ExecuteCommandAsync(
                "list",
                cancellationSource.Token);

            // El servidor recibe el comando, pero no responde.
            await ReadPacketAsync(context.Server);

            cancellationSource.Cancel();

            await AssertThrowsAsync<OperationCanceledException>(() => commandTask.WaitAsync(TestTimeout));

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommandAsync_WhenResponseAndMarkerArriveBeforeTimeout_ShouldSucceed()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            context.Client.CommandTimeout = TimeSpan.FromSeconds(3);

            Task<string> commandTask = context.Client.ExecuteCommandAsync("list");

            RconPacket commandRequest = await ReadPacketAsync(context.Server);

            await WritePacketAsync(
                context.Server,
                new RconPacket(
                    commandRequest.RequestId,
                    (int)RconPacketType.ResponseValue,
                    "Jugadores conectados"));

            RconPacket markerRequest = await ReadPacketAsync(context.Server);

            await WritePacketAsync(
                context.Server,
                new RconPacket(
                    markerRequest.RequestId,
                    (int)RconPacketType.ResponseValue,
                    string.Empty));

            string result = await commandTask.WaitAsync(TestTimeout);

            Assert.AreEqual("Jugadores conectados", result);
            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsTrue(context.Client.IsAuthenticated);
        }

        // ---------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------

        private static async Task<TestConnection> CreateUnauthenticatedConnectionAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);

            listener.Start();

            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var client = new RconClient("127.0.0.1", port);

            try
            {
                await client.ConnectAsync();

                TcpClient server = await listener.AcceptTcpClientAsync()
                    .WaitAsync(TestTimeout);

                return new TestConnection(client, server, listener);
            }
            catch
            {
                client.Dispose();
                listener.Stop();
                throw;
            }
        }

        private static async Task<TestConnection> CreateAuthenticatedConnectionAsync()
        {
            TestConnection context = await CreateUnauthenticatedConnectionAsync();

            try
            {
                Task authenticationTask = context.Client.AuthenticateAsync("password");

                RconPacket request = await ReadPacketAsync(context.Server);

                Assert.AreEqual(
                    (int)RconPacketType.Authentication,
                    request.Type);

                Assert.AreEqual("password", request.Payload);

                await WritePacketAsync(
                    context.Server,
                    new RconPacket(
                        request.RequestId,
                        (int)RconPacketType.AuthenticationResponse,
                        string.Empty));

                await authenticationTask.WaitAsync(TestTimeout);

                Assert.IsTrue(context.Client.IsConnected);
                Assert.IsTrue(context.Client.IsAuthenticated);

                return context;
            }
            catch
            {
                context.Dispose();
                throw;
            }
        }

        private static async Task<RconPacket> ReadPacketAsync(TcpClient tcpClient)
        {
            NetworkStream stream = tcpClient.GetStream();

            byte[] lengthBuffer = new byte[4];

            await stream.ReadExactlyAsync(lengthBuffer.AsMemory())
                .AsTask()
                .WaitAsync(TestTimeout);

            int packetLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

            if (packetLength < 10 ||
                packetLength > RconPacketSerializer.MaximumPacketLength)
            {
                throw new InvalidDataException("Tamaño de paquete inválido.");
            }

            byte[] packetBuffer = new byte[4 + packetLength];

            lengthBuffer.CopyTo(packetBuffer, 0);

            await stream.ReadExactlyAsync(
                    packetBuffer.AsMemory(4, packetLength))
                .AsTask()
                .WaitAsync(TestTimeout);

            return RconPacketSerializer.Deserialize(packetBuffer);
        }

        private static async Task WritePacketAsync(
            TcpClient tcpClient,
            RconPacket packet)
        {
            byte[] data = RconPacketSerializer.Serialize(packet);

            await tcpClient.GetStream()
                .WriteAsync(data.AsMemory())
                .AsTask()
                .WaitAsync(TestTimeout);
        }

        private sealed class TestConnection : IDisposable
        {
            public RconClient Client { get; }
            public TcpClient Server { get; }
            private readonly TcpListener _listener;

            public TestConnection(
                RconClient client,
                TcpClient server,
                TcpListener listener)
            {
                Client = client;
                Server = server;
                _listener = listener;
            }

            public void Dispose()
            {
                Client.Dispose();
                Server.Dispose();
                _listener.Stop();
            }
        }

        private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException exception)
            {
                return exception;
            }
            catch (Exception exception)
            {
                Assert.Fail(
                    $"Se esperaba {typeof(TException).Name}, " +
                    $"pero se produjo {exception.GetType().Name}.");
            }

            Assert.Fail(
                $"Se esperaba una excepción de tipo {typeof(TException).Name}, " +
                "pero no se produjo ninguna.");

            return null!;
        }

        private static TException AssertThrows<TException>(Action action)where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException exception)
            {
                return exception;
            }
            catch (Exception exception)
            {
                throw new AssertFailedException(
                    $"Se esperaba {typeof(TException).Name}, " +
                    $"pero se produjo {exception.GetType().Name}.");
            }

            throw new AssertFailedException(
                $"Se esperaba {typeof(TException).Name}, pero no se produjo ninguna excepción.");
        }

        [TestMethod]
        public void TimeoutProperties_ShouldUseDefaultValues()
        {
            using var client = new RconClient("127.0.0.1", 25575);

            Assert.AreEqual(TimeSpan.FromSeconds(10), client.ConnectionTimeout);
            Assert.AreEqual(TimeSpan.FromSeconds(5), client.AuthenticationTimeout);
            Assert.AreEqual(TimeSpan.FromSeconds(10), client.CommandTimeout);
        }

        [TestMethod]
        public async Task ExecuteCommandAsync_WhenClientIsDisconnected_ShouldThrowInvalidOperationException()
        {
            using var client = new RconClient("127.0.0.1", 25575);

            await AssertThrowsAsync<InvalidOperationException>(
                () => client.ExecuteCommandAsync("list"));
        }
    }
}
