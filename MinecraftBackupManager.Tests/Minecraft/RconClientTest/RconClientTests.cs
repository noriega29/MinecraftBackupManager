using System.Net;
using System.Net.Sockets;
using MinecraftBackupManager.Minecraft;

namespace MinecraftBackupManager.Tests.Minecraft.RconClientTest
{
    [TestClass]
    public sealed class RconClientTests
    {
        [TestMethod]
        public async Task ConnectAsync_ShouldEstablishTcpConnection()
        {
            // Arrange
            TcpListener listener = CreateStartedListener(out int port);

            try
            {
                using var client = new RconClient("127.0.0.1", port);

                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

                // Act
                await client.ConnectAsync();

                using TcpClient acceptedClient =
                    await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));

                // Assert
                Assert.IsTrue(client.IsConnected);
                Assert.IsTrue(acceptedClient.Connected);
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public async Task ConnectAsync_ShouldRejectSecondConnection()
        {
            // Arrange
            TcpListener listener = CreateStartedListener(out int port);

            try
            {
                using var client = new RconClient("127.0.0.1", port);

                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

                await client.ConnectAsync();

                using TcpClient acceptedClient =
                    await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));

                // Act & Assert
                await AssertThrowsAsync<InvalidOperationException>(
                    () => client.ConnectAsync());

                Assert.IsTrue(client.IsConnected);
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public async Task Disconnect_ShouldCloseConnectionAndUpdateState()
        {
            // Arrange
            TcpListener listener = CreateStartedListener(out int port);

            try
            {
                using var client = new RconClient("127.0.0.1", port);

                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

                await client.ConnectAsync();

                using TcpClient acceptedClient =
                    await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));

                // Act
                client.Disconnect();

                // Assert
                Assert.IsFalse(client.IsConnected);

                byte[] buffer = new byte[1];

                int bytesRead = await acceptedClient
                    .GetStream()
                    .ReadAsync(buffer)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5));

                Assert.AreEqual(0, bytesRead);
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public async Task ConnectAsync_ShouldAllowReconnectionAfterDisconnect()
        {
            // Arrange
            TcpListener listener = CreateStartedListener(out int port);

            try
            {
                using var client = new RconClient("127.0.0.1", port);

                Task<TcpClient> firstAcceptTask =
                    listener.AcceptTcpClientAsync();

                await client.ConnectAsync();

                using TcpClient firstConnection =
                    await firstAcceptTask.WaitAsync(TimeSpan.FromSeconds(5));

                client.Disconnect();

                Task<TcpClient> secondAcceptTask =
                    listener.AcceptTcpClientAsync();

                // Act
                await client.ConnectAsync();

                using TcpClient secondConnection =
                    await secondAcceptTask.WaitAsync(TimeSpan.FromSeconds(5));

                // Assert
                Assert.IsTrue(client.IsConnected);
                Assert.IsTrue(secondConnection.Connected);
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public async Task ConnectAsync_ShouldRespectCancellation()
        {
            // Arrange
            TcpListener listener = CreateStartedListener(out int port);

            try
            {
                using var client = new RconClient("127.0.0.1", port);
                using var cancellationTokenSource = new CancellationTokenSource();

                cancellationTokenSource.Cancel();

                // Act & Assert
                await AssertThrowsAsync<OperationCanceledException>(
                    () => client.ConnectAsync(cancellationTokenSource.Token));

                Assert.IsFalse(client.IsConnected);
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public async Task ConnectAsync_ShouldRejectUseAfterDispose()
        {
            // Arrange
            TcpListener listener = CreateStartedListener(out int port);
            var client = new RconClient("127.0.0.1", port);

            try
            {
                client.Dispose();

                // Act & Assert
                await AssertThrowsAsync<ObjectDisposedException>(
                    () => client.ConnectAsync());
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public void Dispose_ShouldBeIdempotent()
        {
            // Arrange
            var client = new RconClient("127.0.0.1", 25575);

            // Act
            client.Dispose();
            client.Dispose();

            // Assert
            Assert.IsFalse(client.IsConnected);
        }

        [TestMethod]
        public void Constructor_ShouldRejectEmptyHost()
        {
            // Act & Assert
            AssertThrows<ArgumentException>(
                () => new RconClient(string.Empty, 25575));
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(-1)]
        [DataRow(65536)]
        public void Constructor_ShouldRejectInvalidPort(int port)
        {
            // Act & Assert
            AssertThrows<ArgumentOutOfRangeException>(
                () => new RconClient("127.0.0.1", port));
        }

        private static TcpListener CreateStartedListener(out int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);

            listener.Start();

            port = ((IPEndPoint)listener.LocalEndpoint).Port;

            return listener;
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

        private static async Task AssertThrowsAsync<TException>(Func<Task> action)
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
    }
}
