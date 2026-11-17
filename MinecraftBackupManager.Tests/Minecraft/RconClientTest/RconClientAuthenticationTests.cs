using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using MinecraftBackupManager.Minecraft;
using MinecraftBackupManager.Minecraft.Models;
using MinecraftBackupManager.Minecraft.Protocol;

namespace MinecraftBackupManager.Tests.Minecraft.RconClientTest
{
    [TestClass]
    public class RconClientAuthenticationTests
    {
        [TestMethod]
        public async Task AuthenticateAsync_WithValidPassword_AuthenticatesSuccessfully()
        {
            // Arrange: iniciar el servidor RCON simulado.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

                using var client = new RconClient("127.0.0.1", port);

                // Act: establecer la conexión.
                await client.ConnectAsync();

                using TcpClient serverClient = await acceptTask.WaitAsync(
                    TimeSpan.FromSeconds(5));

                NetworkStream stream = serverClient.GetStream();

                // Iniciar la autenticación sin esperar todavía su resultado.
                Task authenticationTask = client.AuthenticateAsync("test-password");

                // El servidor recibe la solicitud de autenticación.
                RconPacket request = await ReadPacketAsync(stream);

                // Assert: comprobar la solicitud recibida.
                Assert.AreEqual(
                    (int)RconPacketType.Authentication,
                    request.Type);

                Assert.IsGreaterThan(0, request.RequestId);

                Assert.AreEqual("test-password", request.Payload);

                // El servidor responde con autenticación exitosa.
                var response = new RconPacket(
                    request.RequestId,
                    (int)RconPacketType.AuthenticationResponse,
                    string.Empty);

                await WritePacketAsync(stream, response);

                // El cliente procesa la respuesta.
                await authenticationTask;

                // Assert: comprobar el estado del cliente.
                Assert.IsTrue(client.IsConnected);
                Assert.IsTrue(client.IsAuthenticated);
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public async Task AuthenticateAsync_WithEmptyPreliminaryResponse_AuthenticatesSuccessfully()
        {
            // Arrange
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

                using var client = new RconClient("127.0.0.1", port);

                await client.ConnectAsync();

                using TcpClient serverClient = await acceptTask.WaitAsync(
                    TimeSpan.FromSeconds(5));

                NetworkStream stream = serverClient.GetStream();

                // Iniciar la autenticación.
                Task authenticationTask = client.AuthenticateAsync("test-password");

                // El servidor recibe la solicitud.
                RconPacket request = await ReadPacketAsync(stream);

                Assert.AreEqual(
                    (int)RconPacketType.Authentication,
                    request.Type);

                Assert.AreEqual("test-password", request.Payload);

                // El servidor envía una respuesta preliminar vacía.
                var preliminaryResponse = new RconPacket(
                    request.RequestId,
                    (int)RconPacketType.ResponseValue,
                    string.Empty);

                await WritePacketAsync(stream, preliminaryResponse);

                // El servidor confirma la autenticación.
                var authenticationResponse = new RconPacket(
                    request.RequestId,
                    (int)RconPacketType.AuthenticationResponse,
                    string.Empty);

                await WritePacketAsync(stream, authenticationResponse);

                // El cliente procesa ambas respuestas.
                await authenticationTask;

                // Assert
                Assert.IsTrue(client.IsConnected);
                Assert.IsTrue(client.IsAuthenticated);
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public async Task AuthenticateAsync_WithInvalidPassword_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

                using var client = new RconClient("127.0.0.1", port);

                await client.ConnectAsync();

                using TcpClient serverClient = await acceptTask.WaitAsync(
                    TimeSpan.FromSeconds(5));

                NetworkStream stream = serverClient.GetStream();

                // Iniciar la autenticación.
                Task authenticationTask = client.AuthenticateAsync("wrong-password");

                // El servidor recibe la solicitud.
                RconPacket request = await ReadPacketAsync(stream);

                // El servidor rechaza la autenticación.
                var response = new RconPacket(
                    -1,
                    (int)RconPacketType.AuthenticationResponse,
                    string.Empty);

                await WritePacketAsync(stream, response);

                // Act & Assert
                await AssertThrowsAsync<UnauthorizedAccessException>(
                    () => authenticationTask);

                // Comprobar que la conexión se cerró.
                Assert.IsFalse(client.IsConnected);
                Assert.IsFalse(client.IsAuthenticated);
            }
            finally
            {
                listener.Stop();
            }
        }


        [TestMethod]
        public async Task AuthenticateAsync_WithUnexpectedRequestId_ThrowsInvalidDataException()
        {
            // Arrange
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

                using var client = new RconClient("127.0.0.1", port);

                await client.ConnectAsync();

                using TcpClient serverClient = await acceptTask.WaitAsync(
                    TimeSpan.FromSeconds(5));

                NetworkStream stream = serverClient.GetStream();

                Task authenticationTask = client.AuthenticateAsync("test-password");

                RconPacket request = await ReadPacketAsync(stream);

                // El servidor responde con un identificador incorrecto.
                var response = new RconPacket(
                    request.RequestId + 100,
                    (int)RconPacketType.AuthenticationResponse,
                    string.Empty);

                await WritePacketAsync(stream, response);

                // Act & Assert
                await AssertThrowsAsync<InvalidDataException>(
                    () => authenticationTask);

                Assert.IsFalse(client.IsConnected);
                Assert.IsFalse(client.IsAuthenticated);
            }
            finally
            {
                listener.Stop();
            }
        }


        [TestMethod]
        public async Task AuthenticateAsync_WithUnexpectedPreliminaryPayload_ThrowsInvalidDataException()
        {
            // Arrange
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

                using var client = new RconClient("127.0.0.1", port);

                await client.ConnectAsync();

                using TcpClient serverClient = await acceptTask.WaitAsync(
                    TimeSpan.FromSeconds(5));

                NetworkStream stream = serverClient.GetStream();

                Task authenticationTask = client.AuthenticateAsync("test-password");

                RconPacket request = await ReadPacketAsync(stream);

                // El servidor envía una respuesta preliminar que no está vacía.
                var response = new RconPacket(
                    request.RequestId,
                    (int)RconPacketType.ResponseValue,
                    "unexpected payload");

                await WritePacketAsync(stream, response);

                // Act & Assert
                await AssertThrowsAsync<InvalidDataException>(
                    () => authenticationTask);

                Assert.IsFalse(client.IsConnected);
                Assert.IsFalse(client.IsAuthenticated);
            }
            finally
            {
                listener.Stop();
            }
        }


        [TestMethod]
        public async Task AuthenticateAsync_WhenServerDisconnects_ThrowsIOException()
        {
            // Arrange
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

                using var client = new RconClient("127.0.0.1", port);

                await client.ConnectAsync();

                using TcpClient serverClient = await acceptTask.WaitAsync(
                    TimeSpan.FromSeconds(5));

                NetworkStream stream = serverClient.GetStream();

                Task authenticationTask = client.AuthenticateAsync("test-password");

                // El servidor recibe la solicitud.
                RconPacket request = await ReadPacketAsync(stream);

                Assert.AreEqual(
                    (int)RconPacketType.Authentication,
                    request.Type);

                // El servidor cierra la conexión sin responder.
                serverClient.Close();

                // Act & Assert
                await AssertThrowsAsync<IOException>(
                    () => authenticationTask);

                Assert.IsFalse(client.IsConnected);
                Assert.IsFalse(client.IsAuthenticated);
            }
            finally
            {
                listener.Stop();
            }
        }


        [TestMethod]
        public async Task AuthenticateAsync_WhenCancelled_ThrowsOperationCanceledException()
        {
            // Arrange
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            using var cancellationTokenSource = new CancellationTokenSource();

            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

                using var client = new RconClient("127.0.0.1", port);

                await client.ConnectAsync();

                using TcpClient serverClient = await acceptTask.WaitAsync(
                    TimeSpan.FromSeconds(5));

                NetworkStream stream = serverClient.GetStream();

                Task authenticationTask = client.AuthenticateAsync(
                    "test-password",
                    cancellationTokenSource.Token);

                // El servidor recibe la solicitud, pero no responde.
                RconPacket request = await ReadPacketAsync(stream);

                Assert.AreEqual(
                    (int)RconPacketType.Authentication,
                    request.Type);

                // Cancelar la autenticación.
                await cancellationTokenSource.CancelAsync();

                // Act & Assert
                await AssertThrowsAsync<OperationCanceledException>(
                    () => authenticationTask);

                Assert.IsFalse(client.IsConnected);
                Assert.IsFalse(client.IsAuthenticated);
            }
            finally
            {
                listener.Stop();
            }
        }


        [TestMethod]
        public async Task AuthenticateAsync_WithoutConnection_ThrowsInvalidOperationException()
        {
            // Arrange
            using var client = new RconClient("127.0.0.1", 25575);

            // Act & Assert
            await AssertThrowsAsync<InvalidOperationException>(
                () => client.AuthenticateAsync("test-password"));

            Assert.IsFalse(client.IsConnected);
            Assert.IsFalse(client.IsAuthenticated);
        }


        [TestMethod]
        public async Task AuthenticateAsync_WhenAlreadyAuthenticated_ThrowsInvalidOperationException()
        {
            // Arrange
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

                using var client = new RconClient("127.0.0.1", port);

                await client.ConnectAsync();

                using TcpClient serverClient = await acceptTask.WaitAsync(
                    TimeSpan.FromSeconds(5));

                NetworkStream stream = serverClient.GetStream();

                Task authenticationTask = client.AuthenticateAsync("test-password");

                RconPacket request = await ReadPacketAsync(stream);

                var response = new RconPacket(
                    request.RequestId,
                    (int)RconPacketType.AuthenticationResponse,
                    string.Empty);

                await WritePacketAsync(stream, response);

                await authenticationTask;

                // Act & Assert
                await AssertThrowsAsync<InvalidOperationException>(
                    () => client.AuthenticateAsync("test-password"));

                // La conexión original debe seguir activa.
                Assert.IsTrue(client.IsConnected);
                Assert.IsTrue(client.IsAuthenticated);
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public async Task AuthenticateAsync_WithEmptyPassword_ThrowsArgumentException()
        {
            // Arrange
            using var client = new RconClient("127.0.0.1", 25575);

            // Act & Assert
            await AssertThrowsAsync<ArgumentException>(
                () => client.AuthenticateAsync(string.Empty));

            Assert.IsFalse(client.IsConnected);
            Assert.IsFalse(client.IsAuthenticated);
        }


        [TestMethod]
        public async Task AuthenticateAsync_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            var client = new RconClient("127.0.0.1", 25575);

            client.Dispose();

            // Act & Assert
            await AssertThrowsAsync<ObjectDisposedException>(
                () => client.AuthenticateAsync("test-password"));

            Assert.IsFalse(client.IsConnected);
            Assert.IsFalse(client.IsAuthenticated);
        }


        private static async Task<RconPacket> ReadPacketAsync(
            NetworkStream stream)
        {
            byte[] lengthBuffer = new byte[4];

            await stream.ReadExactlyAsync(lengthBuffer.AsMemory());

            int packetLength = BinaryPrimitives.ReadInt32LittleEndian(
                lengthBuffer);

            byte[] packetBuffer = new byte[4 + packetLength];

            lengthBuffer.CopyTo(packetBuffer, 0);

            await stream.ReadExactlyAsync(
                packetBuffer.AsMemory(4, packetLength));

            return RconPacketSerializer.Deserialize(packetBuffer);
        }

        private static async Task WritePacketAsync(
            NetworkStream stream,
            RconPacket packet)
        {
            byte[] data = RconPacketSerializer.Serialize(packet);

            await stream.WriteAsync(data.AsMemory());
        }

        private static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
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

            Assert.Fail($"Se esperaba una excepción de tipo {typeof(TException).Name}.");
        }
    }
}
