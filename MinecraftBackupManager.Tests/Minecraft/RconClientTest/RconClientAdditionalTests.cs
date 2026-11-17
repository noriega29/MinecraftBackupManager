using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using MinecraftBackupManager.Minecraft;
using MinecraftBackupManager.Minecraft.Models;
using MinecraftBackupManager.Minecraft.Protocol;

namespace MinecraftBackupManager.Tests.Minecraft.RconClientTest
{
    [TestClass]
    public sealed class RconClientAdditionalTests
    {
        private static readonly TimeSpan TestTimeout =
            TimeSpan.FromSeconds(5);

        private static readonly TimeSpan ConcurrencyCheckTimeout =
            TimeSpan.FromMilliseconds(150);

        // ============================================================
        // CONCURRENCIA DEL SEMÁFORO
        // ============================================================

        [TestMethod]
        public async Task ExecuteCommandAsync_ShouldSerializeConcurrentCommands()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> firstTask =
                context.Client.ExecuteCommandAsync("command1");

            RconPacket firstRequest = await ReadPacketAsync(context.Stream);

            Task<string> secondTask =
                context.Client.ExecuteCommandAsync("command2");

            // El segundo comando no debe llegar todavía.
            await AssertNoPacketAvailableAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(firstRequest.RequestId, 0, "Result 1"));

            RconPacket firstMarker = await ReadPacketAsync(context.Stream);

            Assert.AreEqual(100, firstMarker.Type);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(firstMarker.RequestId, 0, "Marker"));

            string firstResult = await firstTask.WaitAsync(TestTimeout);

            RconPacket secondRequest = await ReadPacketAsync(context.Stream);

            Assert.AreEqual("command2", secondRequest.Payload);
            Assert.AreNotEqual(firstRequest.RequestId, secondRequest.RequestId);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(secondRequest.RequestId, 0, "Result 2"));

            RconPacket secondMarker = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(secondMarker.RequestId, 0, "Marker"));

            string secondResult = await secondTask.WaitAsync(TestTimeout);

            Assert.AreEqual("Result 1", firstResult);
            Assert.AreEqual("Result 2", secondResult);

            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsTrue(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommandAsync_ShouldSerializeMultipleConcurrentCommands()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            const int commandCount = 20;

            Task<string>[] commandTasks = Enumerable
                .Range(0, commandCount)
                .Select(i => context.Client.ExecuteCommandAsync($"command{i}"))
                .ToArray();

            for (int i = 0; i < commandCount; i++)
            {
                RconPacket request = await ReadPacketAsync(context.Stream);

                Assert.AreEqual(
                    (int)RconPacketType.ExecuteCommand,
                    request.Type);

                await WritePacketAsync(
                    context.Stream,
                    new RconPacket(
                        request.RequestId,
                        0,
                        $"Response: {request.Payload}"));

                RconPacket marker = await ReadPacketAsync(context.Stream);

                Assert.AreEqual(100, marker.Type);

                await WritePacketAsync(
                    context.Stream,
                    new RconPacket(marker.RequestId, 0, "Marker"));
            }

            string[] results = await Task.WhenAll(commandTasks)
                .WaitAsync(TestTimeout);

            Assert.AreEqual(commandCount, results.Length);

            for (int i = 0; i < commandCount; i++)
            {
                Assert.AreEqual(
                    $"Response: command{i}",
                    results[i]);
            }

            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsTrue(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task Disconnect_ShouldHandleConcurrentCalls()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            Task first = Task.Run(context.Client.Disconnect);
            Task second = Task.Run(context.Client.Disconnect);

            await Task.WhenAll(first, second).WaitAsync(TestTimeout);

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task Disconnect_ShouldWaitForActiveCommand()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            RconPacket request = await ReadPacketAsync(context.Stream);

            // Iniciar la desconexión mientras el comando mantiene el semáforo.
            Task disconnectTask = Task.Run(context.Client.Disconnect);

            await AssertTaskRemainsPendingAsync(disconnectTask);

            // Completar el comando.
            await WritePacketAsync(
                context.Stream,
                new RconPacket(request.RequestId, 0, "Players"));

            RconPacket marker = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(marker.RequestId, 0, "Marker"));

            string result = await commandTask.WaitAsync(TestTimeout);

            await disconnectTask.WaitAsync(TestTimeout);

            Assert.AreEqual("Players", result);
            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        // ============================================================
        // DISPOSE Y LIBERACIÓN DE RECURSOS
        // ============================================================

        [TestMethod]
        public async Task Dispose_ShouldWaitForActiveCommand()
        {
            var context = await CreateAuthenticatedConnectionAsync();

            try
            {
                Task<string> commandTask =
                    context.Client.ExecuteCommandAsync("list");

                RconPacket request = await ReadPacketAsync(context.Stream);

                // Dispose se ejecuta en otro hilo para no bloquear el test.
                Task disposeTask = Task.Run(context.Client.Dispose);

                await AssertTaskRemainsPendingAsync(disposeTask);

                await WritePacketAsync(
                    context.Stream,
                    new RconPacket(request.RequestId, 0, "Players"));

                RconPacket marker = await ReadPacketAsync(context.Stream);

                await WritePacketAsync(
                    context.Stream,
                    new RconPacket(marker.RequestId, 0, "Marker"));

                string result = await commandTask.WaitAsync(TestTimeout);

                await disposeTask.WaitAsync(TestTimeout);

                Assert.AreEqual("Players", result);
                Assert.IsFalse(context.Client.IsConnected);
                Assert.IsFalse(context.Client.IsAuthenticated);

                await AssertThrowsAsync<ObjectDisposedException>(
                    () => context.Client.ConnectAsync());
            }
            finally
            {
                context.Dispose();
            }
        }

        [TestMethod]
        public async Task Dispose_ShouldHandleConcurrentCalls()
        {
            var context = await CreateAuthenticatedConnectionAsync();

            try
            {
                Task first = Task.Run(context.Client.Dispose);
                Task second = Task.Run(context.Client.Dispose);

                await Task.WhenAll(first, second).WaitAsync(TestTimeout);

                Assert.IsFalse(context.Client.IsConnected);
                Assert.IsFalse(context.Client.IsAuthenticated);
            }
            finally
            {
                context.Dispose();
            }
        }

        [TestMethod]
        public async Task RconClient_ShouldRejectAllOperationsAfterDispose()
        {
            var context = await CreateAuthenticatedConnectionAsync();

            try
            {
                context.Client.Dispose();

                await AssertThrowsAsync<ObjectDisposedException>(
                    () => context.Client.ConnectAsync());

                await AssertThrowsAsync<ObjectDisposedException>(
                    () => context.Client.AuthenticateAsync("password"));

                await AssertThrowsAsync<ObjectDisposedException>(
                    () => context.Client.ExecuteCommandAsync("list"));

                Assert.IsFalse(context.Client.IsConnected);
                Assert.IsFalse(context.Client.IsAuthenticated);
            }
            finally
            {
                context.Dispose();
            }
        }

        // ============================================================
        // CANCELACIÓN MIENTRAS SE ESPERA EL SEMÁFORO
        // ============================================================

        [TestMethod]
        public async Task ExecuteCommandAsync_ShouldCancelWhileWaitingForSemaphore()
        {
            using var context = await CreateAuthenticatedConnectionAsync();
            using var cts = new CancellationTokenSource();

            Task<string> firstTask =
                context.Client.ExecuteCommandAsync("command1");

            RconPacket firstRequest = await ReadPacketAsync(context.Stream);

            Task<string> secondTask =
                context.Client.ExecuteCommandAsync("command2", cts.Token);

            // Cancelar mientras el primer comando mantiene el semáforo.
            await cts.CancelAsync();

            await AssertThrowsAsync<OperationCanceledException>(
                () => secondTask);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(firstRequest.RequestId, 0, "First result"));

            RconPacket marker = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(marker.RequestId, 0, "Marker"));

            string result = await firstTask.WaitAsync(TestTimeout);

            Assert.AreEqual("First result", result);
            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsTrue(context.Client.IsAuthenticated);

            // El segundo comando no debe haber sido enviado.
            Assert.IsFalse(context.Stream.DataAvailable);
        }

        [TestMethod]
        public async Task AuthenticateAsync_ShouldCancelWhileWaitingForSemaphore()
        {
            using var context = await CreateAuthenticatedConnectionAsync();
            using var cts = new CancellationTokenSource();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            RconPacket request = await ReadPacketAsync(context.Stream);

            Task authenticationTask =
                context.Client.AuthenticateAsync("another-password", cts.Token);

            await cts.CancelAsync();

            await AssertThrowsAsync<OperationCanceledException>(
                () => authenticationTask);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(request.RequestId, 0, "Players"));

            RconPacket marker = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(marker.RequestId, 0, "Marker"));

            await commandTask.WaitAsync(TestTimeout);

            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsTrue(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ConnectAsync_ShouldCancelWhileWaitingForSemaphore()
        {
            using var context = await CreateUnauthenticatedConnectionAsync();
            using var cts = new CancellationTokenSource();

            // La autenticación mantiene ocupado el semáforo.
            Task authenticationTask =
                context.Client.AuthenticateAsync("test-password");

            RconPacket request = await ReadPacketAsync(context.Stream);

            Task connectionTask =
                context.Client.ConnectAsync(cts.Token);

            await cts.CancelAsync();

            await AssertThrowsAsync<OperationCanceledException>(
                () => connectionTask);

            // Completar la autenticación original.
            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    request.RequestId,
                    (int)RconPacketType.AuthenticationResponse,
                    string.Empty));

            await authenticationTask.WaitAsync(TestTimeout);

            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsTrue(context.Client.IsAuthenticated);
        }

        // ============================================================
        // AUTENTICACIÓN
        // ============================================================

        [TestMethod]
        public async Task AuthenticateAsync_ShouldSetAuthenticatedState()
        {
            using var context = await CreateUnauthenticatedConnectionAsync();

            Task authenticationTask =
                context.Client.AuthenticateAsync("test-password");

            RconPacket request = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    request.RequestId,
                    (int)RconPacketType.AuthenticationResponse,
                    string.Empty));

            await authenticationTask.WaitAsync(TestTimeout);

            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsTrue(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task AuthenticateAsync_WithInvalidPassword_ShouldThrowUnauthorizedAccessException()
        {
            using var context = await CreateUnauthenticatedConnectionAsync();

            Task authenticationTask =
                context.Client.AuthenticateAsync("wrong-password");

            RconPacket request = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    -1,
                    (int)RconPacketType.AuthenticationResponse,
                    string.Empty));

            await AssertThrowsAsync<UnauthorizedAccessException>(
                () => authenticationTask);

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task AuthenticateAsync_WithoutConnection_ShouldThrowInvalidOperationException()
        {
            using var client = new RconClient("127.0.0.1", 25575);

            await AssertThrowsAsync<InvalidOperationException>(
                () => client.AuthenticateAsync("password"));

            Assert.IsFalse(client.IsConnected);
            Assert.IsFalse(client.IsAuthenticated);
        }

        [TestMethod]
        public async Task AuthenticateAsync_WhenAlreadyAuthenticated_ShouldThrowInvalidOperationException()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            await AssertThrowsAsync<InvalidOperationException>(
                () => context.Client.AuthenticateAsync("another-password"));

            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsTrue(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task AuthenticateAsync_ShouldHandleEmptyResponseValueBeforeAuthenticationResponse()
        {
            using var context = await CreateUnauthenticatedConnectionAsync();

            Task authenticationTask =
                context.Client.AuthenticateAsync("test-password");

            RconPacket request = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    request.RequestId,
                    (int)RconPacketType.ResponseValue,
                    string.Empty));

            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    request.RequestId,
                    (int)RconPacketType.AuthenticationResponse,
                    string.Empty));

            await authenticationTask.WaitAsync(TestTimeout);

            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsTrue(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task AuthenticateAsync_WithUnexpectedRequestId_ShouldThrowInvalidDataException()
        {
            using var context = await CreateUnauthenticatedConnectionAsync();

            Task authenticationTask =
                context.Client.AuthenticateAsync("test-password");

            RconPacket request = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    request.RequestId + 100,
                    (int)RconPacketType.AuthenticationResponse,
                    string.Empty));

            await AssertThrowsAsync<InvalidDataException>(
                () => authenticationTask);

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        // ============================================================
        // INTEGRIDAD DEL PROTOCOLO
        // ============================================================

        [TestMethod]
        public async Task ExecuteCommand_ShouldRejectInvalidPacketLength()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            await ReadPacketAsync(context.Stream);

            byte[] lengthBuffer = new byte[4];

            BinaryPrimitives.WriteInt32LittleEndian(
                lengthBuffer,
                9);

            await context.Stream.WriteAsync(lengthBuffer.AsMemory());

            await AssertThrowsAsync<InvalidDataException>(
                () => commandTask);

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommand_ShouldRejectPacketLargerThanMaximumLength()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            await ReadPacketAsync(context.Stream);

            byte[] lengthBuffer = new byte[4];

            BinaryPrimitives.WriteInt32LittleEndian(
                lengthBuffer,
                RconPacketSerializer.MaximumPacketLength + 1);

            await context.Stream.WriteAsync(lengthBuffer.AsMemory());

            await AssertThrowsAsync<InvalidDataException>(
                () => commandTask);

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommand_ShouldRejectUnexpectedMarkerResponseType()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            RconPacket request = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(request.RequestId, 0, "Result"));

            RconPacket marker = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(marker.RequestId, 3, "Invalid type"));

            await AssertThrowsAsync<InvalidDataException>(
                () => commandTask);

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommand_ShouldRejectUnexpectedMarkerRequestId()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            RconPacket request = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(request.RequestId, 0, "Result"));

            RconPacket marker = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(
                    marker.RequestId + 100,
                    0,
                    "Unexpected"));

            await AssertThrowsAsync<InvalidDataException>(
                () => commandTask);

            Assert.IsFalse(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommand_ShouldPreserveResponsePacketOrder()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("help");

            RconPacket request = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(request.RequestId, 0, "First "));

            RconPacket marker = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(request.RequestId, 0, "Second "));

            await WritePacketAsync(
                context.Stream,
                new RconPacket(request.RequestId, 0, "Third"));

            await WritePacketAsync(
                context.Stream,
                new RconPacket(marker.RequestId, 0, "Marker"));

            string result = await commandTask.WaitAsync(TestTimeout);

            Assert.AreEqual("First Second Third", result);
        }

        [TestMethod]
        public async Task ExecuteCommand_ShouldReturnEmptyResponse()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            Task<string> commandTask =
                context.Client.ExecuteCommandAsync("list");

            RconPacket request = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(request.RequestId, 0, string.Empty));

            RconPacket marker = await ReadPacketAsync(context.Stream);

            await WritePacketAsync(
                context.Stream,
                new RconPacket(marker.RequestId, 0, "Marker"));

            string result = await commandTask.WaitAsync(TestTimeout);

            Assert.AreEqual(string.Empty, result);
            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsTrue(context.Client.IsAuthenticated);
        }

        // ============================================================
        // VALIDACIÓN DE ARGUMENTOS
        // ============================================================

        [TestMethod]
        public void Constructor_ShouldRejectNullHost()
        {
            AssertThrows<ArgumentException>(
                () => new RconClient(null!, 25575));
        }

        [TestMethod]
        public async Task ExecuteCommandAsync_ShouldRejectNullCommand()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            await AssertThrowsAsync<ArgumentNullException>(
                () => context.Client.ExecuteCommandAsync(null!));

            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsTrue(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task AuthenticateAsync_ShouldRejectNullPassword()
        {
            using var context = await CreateUnauthenticatedConnectionAsync();

            await AssertThrowsAsync<ArgumentNullException>(
                () => context.Client.AuthenticateAsync(null!));

            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task AuthenticateAsync_ShouldRejectEmptyPassword()
        {
            using var context = await CreateUnauthenticatedConnectionAsync();

            await AssertThrowsAsync<ArgumentException>(
                () => context.Client.AuthenticateAsync(string.Empty));

            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsFalse(context.Client.IsAuthenticated);
        }

        [TestMethod]
        public async Task ExecuteCommandAsync_ShouldRejectWhitespaceCommand()
        {
            using var context = await CreateAuthenticatedConnectionAsync();

            await AssertThrowsAsync<ArgumentException>(
                () => context.Client.ExecuteCommandAsync("   "));

            Assert.IsTrue(context.Client.IsConnected);
            Assert.IsTrue(context.Client.IsAuthenticated);
        }

        // ============================================================
        // INFRAESTRUCTURA DE PRUEBAS
        // ============================================================

        private static async Task<TestConnection>
            CreateUnauthenticatedConnectionAsync()
        {
            TcpListener listener = CreateStartedListener(out int port);

            var client = new RconClient("127.0.0.1", port);

            TcpClient? serverClient = null;

            try
            {
                Task<TcpClient> acceptTask =
                    listener.AcceptTcpClientAsync();

                await client.ConnectAsync();

                serverClient = await acceptTask.WaitAsync(TestTimeout);

                return new TestConnection(
                    listener,
                    client,
                    serverClient,
                    serverClient.GetStream());
            }
            catch
            {
                serverClient?.Dispose();
                client.Dispose();
                listener.Stop();

                throw;
            }
        }

        private static async Task<TestConnection>
            CreateAuthenticatedConnectionAsync()
        {
            TestConnection context =
                await CreateUnauthenticatedConnectionAsync();

            try
            {
                Task authenticationTask =
                    context.Client.AuthenticateAsync("test-password");

                RconPacket request =
                    await ReadPacketAsync(context.Stream);

                Assert.AreEqual(
                    (int)RconPacketType.Authentication,
                    request.Type);

                Assert.AreEqual("test-password", request.Payload);

                await WritePacketAsync(
                    context.Stream,
                    new RconPacket(
                        request.RequestId,
                        (int)RconPacketType.AuthenticationResponse,
                        string.Empty));

                await authenticationTask.WaitAsync(TestTimeout);

                return context;
            }
            catch
            {
                context.Dispose();
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

            await stream.ReadExactlyAsync(lengthBuffer.AsMemory())
                .AsTask()
                .WaitAsync(TestTimeout);

            int packetLength =
                BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

            if (packetLength < 10 ||
                packetLength > RconPacketSerializer.MaximumPacketLength)
            {
                throw new InvalidDataException(
                    "El servidor simulado recibió un tamaño de paquete inválido.");
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
            NetworkStream stream,
            RconPacket packet)
        {
            byte[] data = RconPacketSerializer.Serialize(packet);

            await stream.WriteAsync(data.AsMemory());
        }

        private static async Task AssertNoPacketAvailableAsync(
            NetworkStream stream)
        {
            await Task.Delay(ConcurrencyCheckTimeout);

            Assert.IsFalse(
                stream.DataAvailable,
                "Se recibió un paquete cuando el semáforo debía impedirlo.");
        }

        private static async Task AssertTaskRemainsPendingAsync(Task task)
        {
            Task completed = await Task.WhenAny(
                task,
                Task.Delay(ConcurrencyCheckTimeout));

            Assert.AreNotSame(
                task,
                completed,
                "La operación terminó antes de que se liberara el semáforo.");
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

        private static async Task AssertThrowsAsync<TException>(
            Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action().WaitAsync(TestTimeout);
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
