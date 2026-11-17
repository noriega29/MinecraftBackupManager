using MinecraftBackupManager.Configuration;
using MinecraftBackupManager.Configuration.Models;

namespace MinecraftBackupManager.Tests.Configuration
{
    [TestClass]
    public sealed class ConfigurationValidatorTests
    {
        private readonly ConfigurationValidator _validator = new();

        [TestMethod]
        public void Validate_ValidConfiguration_ReturnsValidResult()
        {
            ApplicationConfig config = new()
            {
                MinecraftServer = new MinecraftServerConfig
                {
                    ServerDirectory = Path.GetTempPath(),
                    RconHost = "localhost",
                    RconPort = 25575
                },

                Restic = new ResticConfig
                {
                    ExecutableSource = ResticExecutableSource.Bundled,
                    RepositoryPath = Path.Combine(
                        Path.GetTempPath(),
                        "MinecraftBackupManagerRepository")
                }
            };

            ConfigurationValidationResult result = _validator.Validate(config);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(0, result.Errors.Count);
        }

        [TestMethod]
        public void Validate_InvalidConfiguration_ReturnsAllErrors()
        {
            ApplicationConfig config = new()
            {
                MinecraftServer = new MinecraftServerConfig
                {
                    ServerDirectory = string.Empty,
                    RconHost = string.Empty,
                    RconPort = 0
                },

                Restic = new ResticConfig
                {
                    ExecutableSource = ResticExecutableSource.Custom,
                    CustomExecutablePath = string.Empty,
                    RepositoryPath = string.Empty
                }
            };

            ConfigurationValidationResult result = _validator.Validate(config);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(5, result.Errors.Count);
        }

        [TestMethod]
        public void Validate_NullConfiguration_ThrowsArgumentNullException()
        {
            try
            {
                _validator.Validate(null!);
                Assert.Fail("Se esperaba una ArgumentNullException.");
            }
            catch (ArgumentNullException)
            {
                // Resultado esperado.
            }
        }

        [TestMethod]
        public void Validate_InvalidRconPort_ReturnsError()
        {
            ApplicationConfig config = CreateValidConfiguration();
            config.MinecraftServer.RconPort = 70000;

            ConfigurationValidationResult result = _validator.Validate(config);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(error =>
                error.Contains("puerto RCON")));
        }

        [TestMethod]
        public void Validate_InvalidRconHost_ReturnsError()
        {
            ApplicationConfig config = CreateValidConfiguration();
            config.MinecraftServer.RconHost = "servidor local ???";

            ConfigurationValidationResult result = _validator.Validate(config);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(error =>
                error.Contains("host RCON")));
        }

        [TestMethod]
        public void Validate_NonexistentServerDirectory_ReturnsError()
        {
            ApplicationConfig config = CreateValidConfiguration();

            config.MinecraftServer.ServerDirectory = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N"));

            ConfigurationValidationResult result = _validator.Validate(config);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(error =>
                error.Contains("no existe")));
        }

        [TestMethod]
        public void Validate_CustomResticWithoutExecutable_ReturnsError()
        {
            ApplicationConfig config = CreateValidConfiguration();

            config.Restic.ExecutableSource = ResticExecutableSource.Custom;
            config.Restic.CustomExecutablePath = string.Empty;

            ConfigurationValidationResult result = _validator.Validate(config);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(error =>
                error.Contains("ejecutable Restic personalizado")));
        }

        [TestMethod]
        public void Validate_InvalidResticExecutableSource_ReturnsError()
        {
            ApplicationConfig config = CreateValidConfiguration();

            config.Restic.ExecutableSource = (ResticExecutableSource)999;

            ConfigurationValidationResult result = _validator.Validate(config);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(error =>
                error.Contains("fuente del ejecutable Restic")));
        }

        [TestMethod]
        public void Validate_RelativeRepositoryPath_ReturnsError()
        {
            ApplicationConfig config = CreateValidConfiguration();

            config.Restic.RepositoryPath = "Backups/Repository";

            ConfigurationValidationResult result = _validator.Validate(config);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(error =>
                error.Contains("repositorio Restic")));
        }

        private static ApplicationConfig CreateValidConfiguration()
        {
            return new ApplicationConfig
            {
                MinecraftServer = new MinecraftServerConfig
                {
                    ServerDirectory = Path.GetTempPath(),
                    RconHost = "localhost",
                    RconPort = 25575
                },

                Restic = new ResticConfig
                {
                    ExecutableSource = ResticExecutableSource.Bundled,
                    RepositoryPath = Path.Combine(
                        Path.GetTempPath(),
                        "MinecraftBackupManagerRepository")
                }
            };
        }
    }
}
