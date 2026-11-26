using System.ComponentModel;
using System.Diagnostics;
using MinecraftBackupManager.Restic;

namespace MinecraftBackupManager.Tests.Restic
{
    [TestClass]
    public sealed class ResticProcessRunnerTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

        [TestMethod]
        [DataRow(0)]
        [DataRow(3)]
        [DataRow(42)]
        public async Task RunAsync_ShouldReturnExitCodeWithoutThrowing(int exitCode)
        {
            var runner = new ResticProcessRunner();

            var result = await runner
                .RunAsync(GetCommandInterpreterPath(), new[] { "/c", "exit", exitCode.ToString() })
                .WaitAsync(TestTimeout);

            Assert.AreEqual(exitCode, result.ExitCode);
        }

        // ============================================================
        // SALIDA
        // ============================================================

        [TestMethod]
        public async Task RunAsync_ShouldCaptureStandardOutput()
        {
            var runner = new ResticProcessRunner();

            var result = await runner
                .RunAsync(GetCommandInterpreterPath(), new[] { "/c", "echo", "hello" })
                .WaitAsync(TestTimeout);

            Assert.AreEqual("hello", result.StandardOutput.TrimEnd());
            Assert.AreEqual(string.Empty, result.StandardError);
        }

        [TestMethod]
        public async Task RunAsync_ShouldCaptureStandardErrorSeparately()
        {
            var runner = new ResticProcessRunner();

            var result = await runner
                .RunAsync(GetCommandInterpreterPath(), new[] { "/c", "echo", "problem", "1>&2" })
                .WaitAsync(TestTimeout);

            Assert.AreEqual("problem", result.StandardError.TrimEnd());
            Assert.AreEqual(string.Empty, result.StandardOutput);
        }

        [TestMethod]
        public async Task RunAsync_WithLargeErrorOutput_ShouldNotDeadlock()
        {
            const int lineCount = 10_000;

            var runner = new ResticProcessRunner();

            var result = await runner
                .RunAsync(
                    GetCommandInterpreterPath(),
                    new[] { "/c", "for", "/L", "%i", "in", $"(1,1,{lineCount})", "do", "@echo", "line", "%i", "1>&2" })
                .WaitAsync(TestTimeout);

            string[] lines = result.StandardError.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            Assert.HasCount(lineCount, lines);
            Assert.AreEqual("line 1", lines[0].TrimEnd());
            Assert.AreEqual($"line {lineCount}", lines[^1].TrimEnd());
            Assert.AreEqual(string.Empty, result.StandardOutput);
        }

        // ============================================================
        // VARIABLES DE ENTORNO
        // ============================================================

        [TestMethod]
        public async Task RunAsync_ShouldPassEnvironmentVariablesToProgram()
        {
            const string variableName = "MBM_TEST_VARIABLE";

            var runner = new ResticProcessRunner();

            var result = await runner
                .RunAsync(
                    GetCommandInterpreterPath(),
                    new[] { "/c", "echo", $"%{variableName}%" },
                    new Dictionary<string, string> { [variableName] = "secret-value" })
                .WaitAsync(TestTimeout);

            Assert.AreEqual("secret-value", result.StandardOutput.TrimEnd());

            // La variable pertenece solo al proceso lanzado, no a la aplicación.
            Assert.IsNull(Environment.GetEnvironmentVariable(variableName));
        }

        [TestMethod]
        public async Task RunAsync_ShouldKeepInheritedEnvironmentVariables()
        {
            string? expected = Environment.GetEnvironmentVariable("SystemRoot");

            Assert.IsFalse(string.IsNullOrEmpty(expected), "SystemRoot debería existir en Windows.");

            var runner = new ResticProcessRunner();

            var result = await runner
                .RunAsync(
                    GetCommandInterpreterPath(),
                    new[] { "/c", "echo", "%SystemRoot%" },
                    new Dictionary<string, string> { ["MBM_TEST_VARIABLE"] = "value" })
                .WaitAsync(TestTimeout);

            Assert.AreEqual(expected, result.StandardOutput.TrimEnd());
        }

        // ============================================================
        // ARGUMENTOS
        // ============================================================

        [TestMethod]
        public async Task RunAsync_ShouldQuoteArgumentsContainingSpaces()
        {
            var runner = new ResticProcessRunner();

            var result = await runner
                .RunAsync(GetCommandInterpreterPath(), new[] { "/c", "echo", "hello world" })
                .WaitAsync(TestTimeout);

            Assert.AreEqual("\"hello world\"", result.StandardOutput.TrimEnd());
        }

        // ============================================================
        // TIEMPO DE ESPERA
        // ============================================================

        [TestMethod]
        public async Task RunAsync_WhenTimeoutExpires_ShouldThrowTimeoutExceptionQuickly()
        {
            var runner = new ResticProcessRunner();
            var stopwatch = Stopwatch.StartNew();

            await TestAssert.ThrowsAsync<TimeoutException>(() => runner
                .RunAsync(
                    GetCommandInterpreterPath(),
                    new[] { "/c", "ping", "-n", "30", "127.0.0.1" },
                    timeout: TimeSpan.FromMilliseconds(500))
                .WaitAsync(TestTimeout));

            stopwatch.Stop();

            // El ping dura ~30 s: un valor muy por debajo demuestra que se cortó a tiempo.
            // Se deja margen porque, con muchas pruebas en paralelo, matar el proceso puede tardar.
            Assert.IsTrue(
                stopwatch.Elapsed < TimeSpan.FromSeconds(8),
                $"El corte tardó {stopwatch.Elapsed}; debería ser mucho menor que los 30 s del ping.");
        }

        // ============================================================
        // CANCELACIÓN
        // ============================================================

        [TestMethod]
        public async Task RunAsync_WhenCallerCancels_ShouldThrowOperationCanceledExceptionQuickly()
        {
            var runner = new ResticProcessRunner();
            using var cts = new CancellationTokenSource();
            var stopwatch = Stopwatch.StartNew();

            var task = runner.RunAsync(
                GetCommandInterpreterPath(),
                new[] { "/c", "ping", "-n", "30", "127.0.0.1" },
                cancellationToken: cts.Token);

            // Se da medio segundo para que el programa esté corriendo.
            await Task.Delay(500);
            await cts.CancelAsync();

            await TestAssert.ThrowsAsync<OperationCanceledException>(() => task.WaitAsync(TestTimeout));

            stopwatch.Stop();

            // El ping dura ~30 s: un valor muy por debajo demuestra que se cortó a tiempo.
            // Se deja margen porque, con muchas pruebas en paralelo, matar el proceso puede tardar.
            Assert.IsTrue(
                stopwatch.Elapsed < TimeSpan.FromSeconds(8),
                $"La cancelación tardó {stopwatch.Elapsed}; debería ser mucho menor que los 30 s del ping.");
        }

        [TestMethod]
        public async Task RunAsync_WhenCallerCancelsBeforeTimeout_ShouldNotReportTimeout()
        {
            var runner = new ResticProcessRunner();
            using var cts = new CancellationTokenSource();

            var task = runner.RunAsync(
                GetCommandInterpreterPath(),
                new[] { "/c", "ping", "-n", "30", "127.0.0.1" },
                timeout: TimeSpan.FromSeconds(30),
                cancellationToken: cts.Token);

            await Task.Delay(500);
            await cts.CancelAsync();

            await TestAssert.ThrowsAsync<OperationCanceledException>(() => task.WaitAsync(TestTimeout));
        }

        [TestMethod]
        public async Task RunAsync_WhenTokenIsAlreadyCanceled_ShouldNotLaunchProgram()
        {
            var runner = new ResticProcessRunner();
            using var cts = new CancellationTokenSource();

            await cts.CancelAsync();

            // Si intentara lanzar este programa inexistente, se produciría una Win32Exception.
            string missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe");

            await TestAssert.ThrowsAsync<OperationCanceledException>(
                () => runner.RunAsync(missingPath, Array.Empty<string>(), cancellationToken: cts.Token));
        }

        // ============================================================
        // PROCESOS HIJOS
        // ============================================================

        [TestMethod]
        [DoNotParallelize]
        public async Task RunAsync_WhenCanceled_ShouldNotLeaveChildProcessesRunning()
        {
            HashSet<int> pingsBefore = GetRunningPingProcessIds();

            var runner = new ResticProcessRunner();
            using var cts = new CancellationTokenSource();

            var task = runner.RunAsync(
                GetCommandInterpreterPath(),
                new[] { "/c", "ping", "-n", "30", "127.0.0.1" },
                cancellationToken: cts.Token);

            await Task.Delay(500);

            // Comprobación previa: el ping hijo realmente existe. Sin ella, la prueba
            // pasaría igual si el proceso nunca se hubiera lanzado.
            Assert.IsTrue(
                GetRunningPingProcessIds().Except(pingsBefore).Any(),
                "No se encontró el proceso ping que lanza cmd.exe.");

            await cts.CancelAsync();

            await TestAssert.ThrowsAsync<OperationCanceledException>(() => task.WaitAsync(TestTimeout));

            bool noOrphans = await WaitUntilAsync(
                () => !GetRunningPingProcessIds().Except(pingsBefore).Any(),
                TimeSpan.FromSeconds(2));

            Assert.IsTrue(noOrphans, "Quedó un proceso ping huérfano después de cancelar.");
        }

        // ============================================================
        // VALIDACIÓN
        // ============================================================

        [TestMethod]
        [DataRow(0)]
        [DataRow(-1)]
        public async Task RunAsync_WithNonPositiveTimeout_ShouldThrowArgumentOutOfRangeException(int milliseconds)
        {
            var runner = new ResticProcessRunner();

            await TestAssert.ThrowsAsync<ArgumentOutOfRangeException>(() => runner
                .RunAsync(
                    GetCommandInterpreterPath(),
                    new[] { "/c", "exit", "0" },
                    timeout: TimeSpan.FromMilliseconds(milliseconds)));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public async Task RunAsync_WithEmptyExecutablePath_ShouldThrowArgumentException(string? path)
        {
            var runner = new ResticProcessRunner();

            await TestAssert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(path!, Array.Empty<string>()));
        }

        [TestMethod]
        public async Task RunAsync_WithNullArguments_ShouldThrowArgumentNullException()
        {
            var runner = new ResticProcessRunner();

            await TestAssert.ThrowsAsync<ArgumentNullException>(() => runner.RunAsync(GetCommandInterpreterPath(), null!));
        }

        [TestMethod]
        public async Task RunAsync_WithNonExistentProgram_ShouldThrowWin32Exception()
        {
            var runner = new ResticProcessRunner();
            string missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe");

            await TestAssert.ThrowsAsync<Win32Exception>(() => runner.RunAsync(missingPath, Array.Empty<string>()));
        }

        // ============================================================
        // AYUDANTES
        // ============================================================

        private static string GetCommandInterpreterPath()
        {
            return Environment.GetEnvironmentVariable("ComSpec")
                ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
        }

        private static HashSet<int> GetRunningPingProcessIds()
        {
            Process[] processes = Process.GetProcessesByName("PING");

            try
            {
                return processes.Select(process => process.Id).ToHashSet();
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }

        private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < timeout)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(50);
            }

            return condition();
        }
    }
}
