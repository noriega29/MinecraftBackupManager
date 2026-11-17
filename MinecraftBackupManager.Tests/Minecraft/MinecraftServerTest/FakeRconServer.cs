using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using MinecraftBackupManager.Minecraft.Models;
using MinecraftBackupManager.Minecraft.Protocol;

namespace MinecraftBackupManager.Tests.Minecraft.MinecraftServerTest
{
    internal sealed class FakeRconServer : IAsyncDisposable
    {
        public const string Password = "test-password";
        public const string CommandResponse = "OK";
        private int _connectionCount;

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly ConcurrentDictionary<string, TimeSpan> _commandDelays = new(StringComparer.Ordinal);
        private readonly List<string> _receivedCommands = new();
        private readonly object _commandsLock = new();
        private readonly Task _acceptLoop;
        private readonly List<TcpClient> _clients = new();
        private readonly object _clientsLock = new();

        public int Port { get; }

        public IReadOnlyList<string> ReceivedCommands
        {
            get
            {
                lock (_commandsLock)
                {
                    return _receivedCommands.ToArray();
                }
            }
        }

        public int ConnectionCount => Volatile.Read(ref _connectionCount);

        public FakeRconServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public void SetCommandDelay(string command, TimeSpan delay)
        {
            _commandDelays[command] = delay;
        }

        public void CloseAllConnections()
        {
            TcpClient[] clients;

            lock (_clientsLock)
            {
                clients = _clients.ToArray();
                _clients.Clear();
            }

            foreach (TcpClient client in clients)
            {
                client.Dispose();
            }
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_cancellation.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(_cancellation.Token);

                    lock (_clientsLock)
                    {
                        _clients.Add(client);
                    }

                    Interlocked.Increment(ref _connectionCount);

                    _ = HandleClientAsync(client, _cancellation.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();

                    while (true)
                    {
                        RconPacket packet = await ReadPacketAsync(stream, cancellationToken);

                        RconPacket reply;

                        switch (packet.Type)
                        {
                            case (int)RconPacketType.Authentication:
                                int authenticationId = packet.Payload == Password ? packet.RequestId : -1;
                                reply = new RconPacket(authenticationId, (int)RconPacketType.AuthenticationResponse, string.Empty);
                                break;

                            case (int)RconPacketType.ExecuteCommand:
                                lock (_commandsLock)
                                {
                                    _receivedCommands.Add(packet.Payload);
                                }

                                if (_commandDelays.TryGetValue(packet.Payload, out TimeSpan delay))
                                {
                                    await Task.Delay(delay, cancellationToken);
                                }

                                reply = new RconPacket(packet.RequestId, (int)RconPacketType.ResponseValue, CommandResponse);
                                break;

                            default:
                                // Paquete marcador (tipo 100): no se registra como comando.
                                reply = new RconPacket(packet.RequestId, (int)RconPacketType.ResponseValue, "Unknown request 64");
                                break;
                        }

                        byte[] data = RconPacketSerializer.Serialize(reply);

                        await stream.WriteAsync(data.AsMemory(), cancellationToken);
                    }
                }
                catch (OperationCanceledException) { }
                catch (EndOfStreamException) { }
                catch (IOException) { }
                catch (InvalidDataException) { }
                catch (ObjectDisposedException) { }
            }
        }

        private static async Task<RconPacket> ReadPacketAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            byte[] lengthBuffer = new byte[4];

            await stream.ReadExactlyAsync(lengthBuffer.AsMemory(), cancellationToken);

            int packetLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

            if (packetLength < 10 || packetLength > RconPacketSerializer.MaximumPacketLength)
            {
                throw new InvalidDataException("Tamaño de paquete inválido.");
            }

            byte[] packetBuffer = new byte[4 + packetLength];

            lengthBuffer.CopyTo(packetBuffer, 0);

            await stream.ReadExactlyAsync(packetBuffer.AsMemory(4, packetLength), cancellationToken);

            return RconPacketSerializer.Deserialize(packetBuffer);
        }

        public async ValueTask DisposeAsync()
        {
            await _cancellation.CancelAsync();

            _listener.Stop();

            await _acceptLoop;

            _cancellation.Dispose();
        }
    }
}