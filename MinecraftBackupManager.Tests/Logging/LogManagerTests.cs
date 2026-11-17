using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.Logging;
using MinecraftBackupManager.Logging.Models;

namespace MinecraftBackupManager.Tests.Logging
{
    [TestClass]
    public class LogManagerTests
    {
        [TestMethod]
        public void Information_ShouldWriteToApplicationLog()
        {
            using TemporaryDirectory directory = new();

            LogManager manager = CreateManager(directory);

            manager.Information(LogCategory.Application, "Aplicación iniciada.");

            string content = ReadLogFile(directory, "application.log");

            StringAssert.Contains(content, "[Information]");
            StringAssert.Contains(content, "[Application]");
            StringAssert.Contains(content, "Aplicación iniciada.");
        }

        [TestMethod]
        public void Write_ShouldUseCorrectFileForEachCategory()
        {
            using TemporaryDirectory directory = new();

            LogManager manager = CreateManager(directory);

            manager.Information(LogCategory.Application, "Mensaje de aplicación.");

            manager.Information(LogCategory.Backup, "Mensaje de backup.");

            manager.Information(LogCategory.Restoration, "Mensaje de restauración.");

            Assert.IsTrue(File.Exists(GetLogFilePath(directory, "application.log")));

            Assert.IsTrue(File.Exists(GetLogFilePath(directory, "backup.log")));

            Assert.IsTrue(File.Exists(GetLogFilePath( directory, "restoration.log")));

            StringAssert.Contains(ReadLogFile(directory, "application.log"), "Mensaje de aplicación.");

            StringAssert.Contains(ReadLogFile(directory, "backup.log"), "Mensaje de backup.");

            StringAssert.Contains(ReadLogFile(directory, "restoration.log"), "Mensaje de restauración.");
        }

        [TestMethod]
        public void Write_ShouldIncludeCorrectLogLevel()
        {
            using TemporaryDirectory directory = new();

            LogManager manager = CreateManager(directory);

            manager.Information(LogCategory.Application, "Información.");
            manager.Warning(LogCategory.Application, "Advertencia.");
            manager.Error(LogCategory.Application, "Error.");
            manager.Critical(LogCategory.Application, "Crítico.");

            string content = ReadLogFile(directory, "application.log");

            StringAssert.Contains(content, "[Information]");
            StringAssert.Contains(content, "[Warning]");
            StringAssert.Contains(content, "[Error]");
            StringAssert.Contains(content, "[Critical]");
        }

        [TestMethod]
        public void Write_ShouldAppendEntriesWithoutOverwriting()
        {
            using TemporaryDirectory directory = new();

            LogManager manager = CreateManager(directory);

            manager.Information(LogCategory.Application, "Primer mensaje.");

            manager.Information(LogCategory.Application, "Segundo mensaje.");

            string content = ReadLogFile(directory, "application.log");

            StringAssert.Contains(content, "Primer mensaje.");
            StringAssert.Contains(content, "Segundo mensaje.");

            Assert.HasCount(2, content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        }


        [TestMethod]
        public void Write_ShouldThrowException_WhenMessageIsEmpty()
        {
            using TemporaryDirectory directory = new();

            LogManager manager = CreateManager(directory);

            // Comprobar mensaje vacío.
            try
            {
                manager.Information(
                    LogCategory.Application,
                    string.Empty);

                Assert.Fail("Se esperaba una ArgumentException.");
            }
            catch (ArgumentException)
            {
                // Resultado esperado.
            }

            // Comprobar mensaje compuesto por espacios.
            try
            {
                manager.Information(
                    LogCategory.Application,
                    "   ");

                Assert.Fail("Se esperaba una ArgumentException.");
            }
            catch (ArgumentException)
            {
                // Resultado esperado.
            }
        }



        [TestMethod]
        public void Write_ShouldThrowException_WhenCategoryIsInvalid()
        {
            using TemporaryDirectory directory = new();

            LogManager manager = CreateManager(directory);

            try
            {
                manager.Information(
                    (LogCategory)999,
                    "Mensaje de prueba.");

                Assert.Fail(
                    "Se esperaba una ArgumentOutOfRangeException.");
            }
            catch (ArgumentOutOfRangeException)
            {
                // Resultado esperado.
            }
        }


        private static LogManager CreateManager(
            TemporaryDirectory directory)
        {
            ApplicationPaths paths = new(directory.Path);

            return new LogManager(paths);
        }

        private static string ReadLogFile(
            TemporaryDirectory directory,
            string fileName)
        {
            return File.ReadAllText(
                GetLogFilePath(directory, fileName));
        }

        private static string GetLogFilePath(
            TemporaryDirectory directory,
            string fileName)
        {
            return Path.Combine(
                directory.Path,
                "Logs",
                fileName);
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "MinecraftBackupManagerTests",
                    Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
        }
    }
}
