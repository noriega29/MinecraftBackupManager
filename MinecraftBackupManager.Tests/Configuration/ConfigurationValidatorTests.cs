using MinecraftBackupManager.Configuration;
using MinecraftBackupManager.Configuration.Models;

namespace MinecraftBackupManager.Tests.Configuration
{
    [TestClass]
    public sealed class ConfigurationValidatorTests
    {
        private readonly ConfigurationValidator _validator = new();
        private string _root = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "MinecraftBackupManagerTests",
                Guid.NewGuid().ToString("N"));

            // Server\World y Server2 existen para que ServerDirectory sea válido.
            Directory.CreateDirectory(Path.Combine(_root, "Server", "World"));
            Directory.CreateDirectory(Path.Combine(_root, "Server2"));
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

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
                        Path.GetPathRoot(Path.GetTempPath())!,
                        "MinecraftBackupManagerRepository")
                }
            };

            ConfigurationValidationResult result = _validator.Validate(config);

            Assert.IsTrue(result.IsValid);
            Assert.IsEmpty(result.Errors);
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
            Assert.HasCount(5, result.Errors);
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

        [TestMethod]
        [DataRow(@"Server", @"Server")]
        [DataRow(@"Server", @"Server\Repo")]
        [DataRow(@"Server", @"Server\")]
        [DataRow(@"Server", @"SERVER\Repo")]
        public void Validate_RepositoryInsideServerDirectory_ReturnsError(string server, string repository)
        {
            ApplicationConfig config = CreateConfigurationInRoot(server, repository);

            ConfigurationValidationResult result = _validator.Validate(config);

            Assert.IsFalse(result.IsValid);
            Assert.HasCount(1, result.Errors);
            StringAssert.Contains(result.Errors[0], "dentro de la carpeta del servidor");
        }

        [TestMethod]
        [DataRow(@"Server\World", @"Server")]
        [DataRow(@"Server\World", @"Server\")]
        [DataRow(@"Server\World", @"SERVER")]
        public void Validate_ServerDirectoryInsideRepository_ReturnsError(string server, string repository)
        {
            ApplicationConfig config = CreateConfigurationInRoot(server, repository);

            ConfigurationValidationResult result = _validator.Validate(config);

            Assert.IsFalse(result.IsValid);
            Assert.HasCount(1, result.Errors);
            StringAssert.Contains(result.Errors[0], "dentro del repositorio");
        }

        [TestMethod]
        [DataRow(@"Server", @"Server2")]
        [DataRow(@"Server", @"Repository")]
        public void Validate_SeparateRepositoryAndServer_ReturnsValidResult(string server, string repository)
        {
            ApplicationConfig config = CreateConfigurationInRoot(server, repository);

            ConfigurationValidationResult result = _validator.Validate(config);

            Assert.IsTrue(result.IsValid, string.Join(" | ", result.Errors));
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
                        Path.GetPathRoot(Path.GetTempPath())!,
                        "MinecraftBackupManagerRepository")
                }
            };
        }

        // Construye una configuración válida con las rutas relativas a la carpeta temporal de la prueba.
        private ApplicationConfig CreateConfigurationInRoot(string server, string repository)
        {
            return new ApplicationConfig
            {
                MinecraftServer = new MinecraftServerConfig
                {
                    ServerDirectory = Path.Combine(_root, server),
                    RconHost = "localhost",
                    RconPort = 25575
                },

                Restic = new ResticConfig
                {
                    ExecutableSource = ResticExecutableSource.Bundled,
                    RepositoryPath = Path.Combine(_root, repository)
                }
            };
        }
    }
}
