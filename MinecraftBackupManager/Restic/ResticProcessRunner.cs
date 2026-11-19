using System.Diagnostics;
using System.Text;
using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Restic
{
    public sealed class ResticProcessRunner
    {
        private static readonly TimeSpan StreamCloseTimeout = TimeSpan.FromSeconds(5);

        public async Task<ProcessExecutionResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environmentVariables = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
            ArgumentNullException.ThrowIfNull(arguments);

            if (timeout is not null && timeout.Value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "El tiempo de espera debe ser mayor que cero.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (environmentVariables is not null)
            {
                foreach (KeyValuePair<string, string> variable in environmentVariables)
                {
                    startInfo.Environment[variable.Key] = variable.Value;
                }
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (timeout is not null)
            {
                timeoutSource.CancelAfter(timeout.Value);
            }

            using var process = new Process { StartInfo = startInfo };

            process.Start();

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                // Si el programa terminó justo a tiempo, se devuelve su resultado normal.
                if (!process.HasExited)
                {
                    TryKillProcessTree(process);

                    // Esperar a que el proceso desaparezca de verdad y a que se cierren los flujos.
                    await process.WaitForExitAsync(CancellationToken.None);
                    await WaitForStreamsToCloseAsync(outputTask, errorTask);

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException("El programa externo superó el tiempo de espera establecido.");
                    }

                    throw;
                }
            }

            string standardOutput = await outputTask;
            string standardError = await errorTask;

            return new ProcessExecutionResult(process.ExitCode, standardOutput, standardError);
        }

        private static void TryKillProcessTree(Process process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // El proceso ya había terminado.
            }
        }

        private static async Task WaitForStreamsToCloseAsync(Task outputTask, Task errorTask)
        {
            Task bothStreams = Task.WhenAll(outputTask, errorTask);

            try
            {
                await bothStreams.WaitAsync(StreamCloseTimeout);
            }
            catch (TimeoutException)
            {
                // Se abandona la lectura: la salida parcial se descarta de todos modos.
                // Se observa el resultado para que un fallo posterior no quede sin atender.
                _ = bothStreams.ContinueWith(
                    static task => task.Exception,
                    TaskContinuationOptions.OnlyOnFaulted);
            }
        }
    }
}
