using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Logging;
using MinecraftBackupManager.Logging.Models;
using MinecraftBackupManager.Security;
using MinecraftBackupManager.Backup;

namespace MinecraftBackupManager.Minecraft
{
    /// <summary>
    /// Representa el servidor Minecraft configurado y expone únicamente las
    /// operaciones RCON necesarias para realizar respaldos consistentes.
    /// </summary>
    public sealed class MinecraftServer : IBackupServer, IAsyncDisposable
    {
        private const string SaveOffCommand = "save-off";
        private const string SaveAllCommand = "save-all flush";
        private const string SaveOnCommand = "save-on";

        private const int ResumeMaxAttempts = 3;
        private static readonly TimeSpan ResumeRetryDelay = TimeSpan.FromSeconds(1);

        private readonly MinecraftServerConfig _config;
        private readonly CredentialManager _credentialManager;
        private readonly LogManager _logManager;
        private readonly SemaphoreSlim _operationLock = new(1, 1);

        private RconClient? _client;
        private bool _autosaveDisabled;
        private bool _disposed;

        private TimeSpan _commandTimeout = TimeSpan.FromSeconds(10);
        private TimeSpan _saveTimeout = TimeSpan.FromMinutes(2);

        /// <summary>Tiempo máximo para comandos rápidos (save-off, save-on).</summary>
        public TimeSpan CommandTimeout
        {
            get => _commandTimeout;
            set => _commandTimeout = ValidateTimeout(value, nameof(CommandTimeout));
        }

        /// <summary>Tiempo máximo para "save-all flush", que en mundos grandes puede tardar.</summary>
        public TimeSpan SaveTimeout
        {
            get => _saveTimeout;
            set => _saveTimeout = ValidateTimeout(value, nameof(SaveTimeout));
        }

        /// <summary>
        /// Indica si este objeto desactivó el guardado automático y aún no ha
        /// confirmado su reactivación.
        /// </summary>
        public bool IsAutosaveDisabled => _autosaveDisabled;

        public MinecraftServer(MinecraftServerConfig config, CredentialManager credentialManager, LogManager logManager)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
            _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));
        }

        /// <summary>
        /// Abre una sesión RCON nueva y la autentica. Si había una sesión
        /// anterior, la descarta primero.
        /// </summary>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            await _operationLock.WaitAsync(cancellationToken);

            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                // Temporal: se abre siempre una sesión nueva, porque la anterior
                // puede haberse cortado sin que el cliente lo sepa.
                // Más adelante se desconectará al terminar cada respaldo.
                DiscardClient();

                await EnsureConnectedAsync(cancellationToken);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>
        /// Desactiva el guardado automático y fuerza la escritura de los datos
        /// pendientes. Si algo falla después de haber enviado "save-off",
        /// intenta reactivar el guardado antes de propagar el error.
        /// </summary>
        public async Task PrepareForBackupAsync(CancellationToken cancellationToken = default)
        {
            await _operationLock.WaitAsync(cancellationToken);

            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                try
                {
                    // Se marca antes de enviar el comando: si "save-off" agota el
                    // tiempo de espera, el servidor pudo haberlo procesado igualmente.
                    _autosaveDisabled = true;

                    TryLog(log => log.Information(LogCategory.Backup, "Desactivando el guardado automático del servidor."));

                    await SendCommandAsync(SaveOffCommand, CommandTimeout, cancellationToken);

                    TryLog(log => log.Information(LogCategory.Backup, "Guardando los datos pendientes del mundo (save-all flush)."));

                    await SendCommandAsync(SaveAllCommand, SaveTimeout, cancellationToken);

                    TryLog(log => log.Information(LogCategory.Backup, "El servidor está listo para el respaldo."));
                }
                catch (Exception exception)
                {
                    TryLog(log => log.Error(LogCategory.Backup, $"Falló la preparación del servidor para el respaldo: {exception.Message}"));

                    // Reversión: no se debe dejar el guardado desactivado.
                    // Se usa CancellationToken.None para que funcione aunque
                    // la operación original haya sido cancelada.
                    try
                    {
                        await ResumeAutosaveCoreAsync(CancellationToken.None);
                    }
                    catch
                    {
                        // El fallo ya fue registrado como crítico dentro de
                        // ResumeAutosaveCoreAsync. Se propaga el error original.
                    }

                    throw;
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>
        /// Reactiva el guardado automático. Reconecta si la sesión RCON se
        /// cerró y reintenta unas pocas veces. Lanza una excepción si no se
        /// pudo confirmar la reactivación.
        /// </summary>
        /// <remarks>
        /// Desde un bloque finally conviene llamarlo con CancellationToken.None
        /// para que una cancelación previa no impida enviar "save-on".
        /// </remarks>
        public async Task ResumeAutosaveAsync(CancellationToken cancellationToken = default)
        {
            await _operationLock.WaitAsync(cancellationToken);

            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                await ResumeAutosaveCoreAsync(cancellationToken);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _operationLock.WaitAsync();

            try
            {
                if (_disposed)
                {
                    return;
                }

                if (_autosaveDisabled)
                {
                    TryLog(log => log.Warning(LogCategory.Backup, "Se liberó el servidor con el guardado automático desactivado. Intentando reactivarlo."));

                    try
                    {
                        await ResumeAutosaveCoreAsync(CancellationToken.None);
                    }
                    catch
                    {
                        // Ya registrado como crítico.
                    }
                }

                DiscardClient();
                _disposed = true;
            }
            finally
            {
                _operationLock.Release();
            }

            GC.SuppressFinalize(this);
        }

        // Debe llamarse con _operationLock tomado.
        private async Task ResumeAutosaveCoreAsync(CancellationToken cancellationToken)
        {
            Exception? lastException = null;

            for (int attempt = 1; attempt <= ResumeMaxAttempts; attempt++)
            {
                try
                {
                    // Primero se envía el comando; el registro va después y
                    // nunca puede impedirlo.
                    await SendCommandAsync(SaveOnCommand, CommandTimeout, cancellationToken);

                    _autosaveDisabled = false;

                    TryLog(log => log.Information(LogCategory.Backup, "Guardado automático reactivado."));

                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    lastException = exception;

                    int failedAttempt = attempt;

                    TryLog(log => log.Warning(LogCategory.Backup, $"Intento {failedAttempt} de {ResumeMaxAttempts} para reactivar el guardado automático falló: {exception.Message}"));

                    if (attempt < ResumeMaxAttempts)
                    {
                        await Task.Delay(ResumeRetryDelay, cancellationToken);
                    }
                }
            }

            TryLog(log => log.Critical(LogCategory.Backup, "No se pudo reactivar el guardado automático. Ejecuta 'save-on' manualmente en el servidor."));

            throw new InvalidOperationException(
                "No se pudo reactivar el guardado automático del servidor. Ejecuta 'save-on' manualmente.",
                lastException);
        }

        // Debe llamarse con _operationLock tomado.
        private async Task<string> SendCommandAsync(string command, TimeSpan timeout, CancellationToken cancellationToken)
        {
            await EnsureConnectedAsync(cancellationToken);

            RconClient client = _client!;

            client.CommandTimeout = timeout;

            string response = await client.ExecuteCommandAsync(command, cancellationToken);

            // RconClient cierra la conexión ante cualquier error, por lo que
            // el siguiente EnsureConnectedAsync reconectará si hace falta.
            return response;
        }

        // Debe llamarse con _operationLock tomado.
        private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (_client is { IsConnected: true, IsAuthenticated: true })
            {
                return;
            }

            DiscardClient();

            // La contraseña solo se carga cuando hace falta conectar y no se
            // conserva como campo.
            string password = LoadRconPassword();

            var client = new RconClient(_config.RconHost, _config.RconPort);

            try
            {
                TryLog(log => log.Information(LogCategory.Application, $"Conectando con el servidor RCON en {_config.RconHost}:{_config.RconPort}."));

                await client.ConnectAsync(cancellationToken);
                await client.AuthenticateAsync(password, cancellationToken);

                TryLog(log => log.Information(LogCategory.Application, "Conexión RCON establecida y autenticada."));
            }
            catch (Exception exception)
            {
                client.Dispose();

                TryLog(log => log.Error(LogCategory.Application, $"No se pudo establecer la conexión RCON: {exception.Message}"));

                throw;
            }

            _client = client;
        }

        private string LoadRconPassword()
        {
            var credentials = _credentialManager.Load();

            if (credentials is null)
            {
                throw new InvalidOperationException("No hay credenciales guardadas. Configura la contraseña RCON antes de continuar.");
            }

            if (string.IsNullOrEmpty(credentials.RconPassword))
            {
                throw new InvalidOperationException("La contraseña RCON no está configurada.");
            }

            return credentials.RconPassword;
        }

        private void DiscardClient()
        {
            _client?.Dispose();
            _client = null;
        }

        // Un fallo del registro nunca debe interrumpir una operación de respaldo.
        private void TryLog(Action<LogManager> write)
        {
            try
            {
                write(_logManager);
            }
            catch
            {
                // Se ignora deliberadamente.
            }
        }

        private static TimeSpan ValidateTimeout(TimeSpan timeout, string parameterName)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(parameterName, "El tiempo de espera debe ser mayor que cero.");
            }

            return timeout;
        }
    }
}
