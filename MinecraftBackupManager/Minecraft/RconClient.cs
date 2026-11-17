using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using MinecraftBackupManager.Minecraft.Models;
using MinecraftBackupManager.Minecraft.Protocol;

namespace MinecraftBackupManager.Minecraft
{
    public sealed class RconClient : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly SemaphoreSlim _operationLock = new(1, 1);

        private TcpClient? _tcpClient;
        private bool _disposed;
        private int _nextRequestId;
        private TimeSpan _connectionTimeout = TimeSpan.FromSeconds(10);
        private TimeSpan _authenticationTimeout = TimeSpan.FromSeconds(5);
        private TimeSpan _commandTimeout = TimeSpan.FromSeconds(10);
        private const int MarkerPacketType = 100;
        private const string MarkerPayload = "probe";

        public bool IsConnected { get; private set; }
        public bool IsAuthenticated { get; private set; }
        public TimeSpan ConnectionTimeout
        {
            get => _connectionTimeout;
            set => _connectionTimeout = ValidateTimeout(value, nameof(ConnectionTimeout));
        }

        public TimeSpan AuthenticationTimeout
        {
            get => _authenticationTimeout;
            set => _authenticationTimeout = ValidateTimeout(value, nameof(AuthenticationTimeout));
        }

        public TimeSpan CommandTimeout
        {
            get => _commandTimeout;
            set => _commandTimeout = ValidateTimeout(value, nameof(CommandTimeout));
        }

        public RconClient(string host, int port)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(host);

            if (port is < 1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port), "El puerto debe estar entre 1 y 65535.");
            }

            _host = host;
            _port = port;
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            await _operationLock.WaitAsync(cancellationToken);

            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (IsConnected)
                {
                    throw new InvalidOperationException("Ya existe una conexión con el servidor.");
                }

                DisconnectCore();

                using var timeoutSource = CreateTimeoutTokenSource(cancellationToken, ConnectionTimeout);

                var tcpClient = new TcpClient();

                try
                {
                    await tcpClient.ConnectAsync(_host, _port, timeoutSource.Token);

                    _tcpClient = tcpClient;
                    IsConnected = true;
                }
                catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    tcpClient.Dispose();

                    throw new TimeoutException("Se agotó el tiempo de espera al conectar con el servidor RCON.");
                }
                catch
                {
                    tcpClient.Dispose();
                    throw;
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task AuthenticateAsync(string password, CancellationToken cancellationToken = default)
        {
            await _operationLock.WaitAsync(cancellationToken);

            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                ArgumentNullException.ThrowIfNull(password);

                if (string.IsNullOrEmpty(password))
                {
                    throw new ArgumentException("La contraseña RCON no puede estar vacía.", nameof(password));
                }

                if (!IsConnected || _tcpClient is null)
                {
                    throw new InvalidOperationException("No existe una conexión TCP con el servidor.");
                }

                if (IsAuthenticated)
                {
                    throw new InvalidOperationException("El cliente ya está autenticado.");
                }

                using var timeoutSource = CreateTimeoutTokenSource(cancellationToken, AuthenticationTimeout);

                CancellationToken operationToken = timeoutSource.Token;

                try
                {
                    int requestId = Interlocked.Increment(ref _nextRequestId);

                    var authenticationPacket = new RconPacket(requestId, (int)RconPacketType.Authentication, password);

                    await WritePacketAsync(authenticationPacket, operationToken);

                    RconPacket response = await ReadPacketAsync(operationToken);

                    // Algunos servidores envían primero un paquete vacío
                    // de tipo ResponseValue antes de la respuesta de autenticación.
                    if (response.Type == (int)RconPacketType.ResponseValue)
                    {
                        if (response.RequestId != requestId)
                        {
                            throw new InvalidDataException("El paquete de respuesta RCON tiene un ID de solicitud inesperado.");
                        }

                        if (!string.IsNullOrEmpty(response.Payload))
                        {
                            throw new InvalidDataException("El paquete de respuesta RCON tiene una carga útil inesperada.");
                        }

                        response = await ReadPacketAsync(operationToken);
                    }

                    if (response.Type != (int)RconPacketType.AuthenticationResponse)
                    {
                        throw new InvalidDataException("El paquete de respuesta RCON no es una respuesta de autenticación válida.");
                    }

                    if (response.RequestId == -1)
                    {
                        throw new UnauthorizedAccessException("El servidor rechazó la contraseña RCON.");
                    }

                    if (response.RequestId != requestId)
                    {
                        throw new InvalidDataException("El paquete de respuesta RCON tiene un ID de solicitud inesperado.");
                    }

                    IsAuthenticated = true;
                }
                catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    DisconnectCore();

                    throw new TimeoutException("La operación RCON superó el tiempo de espera establecido.");
                }
                catch
                {
                    DisconnectCore();
                    throw;
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
        {
            await _operationLock.WaitAsync(cancellationToken);

            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                ArgumentNullException.ThrowIfNull(command);

                if (string.IsNullOrWhiteSpace(command))
                {
                    throw new ArgumentException("El comando RCON no puede estar vacío.", nameof(command));
                }

                if (!IsConnected || _tcpClient is null)
                {
                    throw new InvalidOperationException("No existe una conexión TCP con el servidor.");
                }

                if (!IsAuthenticated)
                {
                    throw new InvalidOperationException("El cliente no está autenticado en el servidor RCON.");
                }

                using var timeoutSource = CreateTimeoutTokenSource(cancellationToken, CommandTimeout);

                CancellationToken operationToken = timeoutSource.Token;

                try
                {
                    // Generar identificadores independientes.
                    int commandRequestId = Interlocked.Increment(ref _nextRequestId);
                    int markerRequestId = Interlocked.Increment(ref _nextRequestId);

                    // Enviar el comando.
                    var commandPacket = new RconPacket(commandRequestId, (int)RconPacketType.ExecuteCommand, command);

                    await WritePacketAsync(commandPacket, operationToken);

                    // Leer la primera respuesta.
                    RconPacket response = await ReadPacketAsync(operationToken);

                    if (response.RequestId != commandRequestId)
                    {
                        throw new InvalidDataException("El paquete de respuesta RCON tiene un ID de solicitud inesperado.");
                    }

                    if (response.Type != (int)RconPacketType.ResponseValue)
                    {
                        throw new InvalidDataException("El paquete de respuesta RCON tiene un tipo inesperado.");
                    }

                    // Acumular el contenido de las respuestas.
                    var result = new StringBuilder();

                    result.Append(response.Payload);

                    // Enviar el paquete marcador.
                    var markerPacket = new RconPacket(markerRequestId, MarkerPacketType, MarkerPayload);

                    await WritePacketAsync(markerPacket, operationToken);

                    // Leer hasta recibir la respuesta del marcador.
                    while (true)
                    {
                        response = await ReadPacketAsync(operationToken);

                        // Comprobar si hemos recibido el marcador.
                        if (response.RequestId == markerRequestId)
                        {
                            if (response.Type != (int)RconPacketType.ResponseValue)
                            {
                                throw new InvalidDataException("El paquete marcador RCON tiene un tipo de respuesta inesperado.");
                            }

                            break;
                        }

                        // Validar que la respuesta pertenece al comando original.
                        if (response.RequestId != commandRequestId)
                        {
                            throw new InvalidDataException("El paquete de respuesta RCON tiene un ID de solicitud inesperado.");
                        }

                        if (response.Type != (int)RconPacketType.ResponseValue)
                        {
                            throw new InvalidDataException("El paquete de respuesta RCON tiene un tipo inesperado.");
                        }

                        result.Append(response.Payload);
                    }

                    return result.ToString();
                }
                catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    DisconnectCore();

                    throw new TimeoutException("La operación RCON superó el tiempo de espera establecido.");
                }
                catch
                {
                    // Ante un error, cerramos la conexión para evitar
                    // conservar respuestas pendientes en el flujo TCP.
                    DisconnectCore();
                    throw;
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        private async Task WritePacketAsync(RconPacket packet, CancellationToken cancellationToken)
        {
            if (_tcpClient is null || !IsConnected)
            {
                throw new InvalidOperationException("No existe una conexión TCP con el servidor.");
            }

            byte[] data = RconPacketSerializer.Serialize(packet);

            NetworkStream stream = _tcpClient.GetStream();

            await stream.WriteAsync(data.AsMemory(), cancellationToken);
        }

        private async Task<RconPacket> ReadPacketAsync(CancellationToken cancellationToken)
        {
            if (_tcpClient is null || !IsConnected)
            {
                throw new InvalidOperationException("No existe una conexión TCP con el servidor.");
            }

            NetworkStream stream = _tcpClient.GetStream();

            byte[] lengthBuffer = new byte[4];

            await stream.ReadExactlyAsync(lengthBuffer.AsMemory(), cancellationToken);

            int packetLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

            if (packetLength < 10 || packetLength > RconPacketSerializer.MaximumPacketLength)
            {
                throw new InvalidDataException("El paquete RCON tiene un tamaño inválido.");
            }

            byte[] packetBuffer = new byte[4 + packetLength];

            lengthBuffer.CopyTo(packetBuffer, 0);

            await stream.ReadExactlyAsync(packetBuffer.AsMemory(4, packetLength), cancellationToken);

            return RconPacketSerializer.Deserialize(packetBuffer);
        }

        public void Disconnect()
        {
            _operationLock.Wait();

            try
            {
                DisconnectCore();
            }
            finally
            {
                _operationLock.Release();
            }
        }

        private void DisconnectCore()
        {
            IsConnected = false;
            IsAuthenticated = false;

            _tcpClient?.Dispose();
            _tcpClient = null;
        }

        public void Dispose()
        {
            _operationLock.Wait();

            try
            {
                if (_disposed) return;

                DisconnectCore();
                _disposed = true;
            }
            finally
            {
                _operationLock.Release();
            }

            GC.SuppressFinalize(this);
        }

        private static TimeSpan ValidateTimeout(TimeSpan timeout, string parameterName)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(parameterName, "El tiempo de espera debe ser mayor que cero.");
            }

            return timeout;
        }

        private static CancellationTokenSource CreateTimeoutTokenSource(CancellationToken cancellationToken, TimeSpan timeout)
        {
            var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            timeoutSource.CancelAfter(timeout);

            return timeoutSource;
        }
    }
}
