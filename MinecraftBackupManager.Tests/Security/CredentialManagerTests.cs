
using System.Text;
using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.Security;
using MinecraftBackupManager.Security.Models;

namespace MinecraftBackupManager.Tests.Security
{
    [TestClass]
    public class CredentialManagerTests
    {
        [TestMethod]
        public void SaveAndLoad_ShouldPreserveBothPasswords()
        {
            using TemporaryDirectory directory = new();

            CredentialManager manager = CreateManager(directory);

            CredentialsData original = new()
            {
                RconPassword = "Test_Rcon_12345",
                ResticRepositoryPassword = "Test_Restic_67890"
            };

            manager.Save(original);

            CredentialsData? loaded = manager.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(original.RconPassword, loaded.RconPassword);
            Assert.AreEqual(
                original.ResticRepositoryPassword,
                loaded.ResticRepositoryPassword);
        }

        [TestMethod]
        public void Save_ShouldNotStorePasswordsInPlainText()
        {
            using TemporaryDirectory directory = new();

            CredentialManager manager = CreateManager(directory);

            const string rconPassword = "Plain_Rcon_Test_123";
            const string resticPassword = "Plain_Restic_Test_456";

            CredentialsData credentials = new()
            {
                RconPassword = rconPassword,
                ResticRepositoryPassword = resticPassword
            };

            manager.Save(credentials);

            byte[] fileData = File.ReadAllBytes(Path.Combine(directory.Path, "credentials.dat"));

            string fileContent = Encoding.UTF8.GetString(fileData);

            Assert.IsFalse(fileContent.Contains(rconPassword));
            Assert.IsFalse(fileContent.Contains(resticPassword));
        }

        [TestMethod]
        public void Load_ShouldReturnNull_WhenFileDoesNotExist()
        {
            using TemporaryDirectory directory = new();

            CredentialManager manager = CreateManager(directory);

            CredentialsData? credentials = manager.Load();

            Assert.IsNull(credentials);
        }

        [TestMethod]
        public void Load_ShouldThrowInvalidOperationException_WhenFileIsCorrupted()
        {
            using TemporaryDirectory directory = new();

            CredentialManager manager = CreateManager(directory);

            WriteCorruptedFile(directory);

            bool exceptionThrown = false;

            try
            {
                manager.Load();
            }
            catch (InvalidOperationException)
            {
                exceptionThrown = true;
            }

            Assert.IsTrue(exceptionThrown);
        }

        [TestMethod]
        public void Exists_ShouldReturnCorrectResult()
        {
            using TemporaryDirectory directory = new();

            CredentialManager manager = CreateManager(directory);

            Assert.IsFalse(manager.Exists());

            manager.Save(new CredentialsData
            {
                RconPassword = "Test_Rcon",
                ResticRepositoryPassword = "Test_Restic"
            });

            Assert.IsTrue(manager.Exists());
        }

        [TestMethod]
        public void Delete_ShouldRemoveCredentialsFile()
        {
            using TemporaryDirectory directory = new();

            CredentialManager manager = CreateManager(directory);

            manager.Save(new CredentialsData
            {
                RconPassword = "Test_Rcon",
                ResticRepositoryPassword = "Test_Restic"
            });

            Assert.IsTrue(manager.Exists());

            manager.Delete();

            Assert.IsFalse(manager.Exists());
            Assert.IsNull(manager.Load());
        }

        private static CredentialManager CreateManager(
            TemporaryDirectory directory)
        {
            ApplicationPaths paths = new(directory.Path);

            return new CredentialManager(paths);
        }

        private static void WriteCorruptedFile(
            TemporaryDirectory directory)
        {
            string filePath = Path.Combine(
                directory.Path,
                "credentials.dat");

            Directory.CreateDirectory(directory.Path);

            File.WriteAllBytes(
                filePath,
                Encoding.UTF8.GetBytes("This is not a valid DPAPI file."));
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
