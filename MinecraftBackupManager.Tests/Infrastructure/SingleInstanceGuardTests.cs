using MinecraftBackupManager.Infrastructure;

namespace MinecraftBackupManager.Tests.Infrastructure
{
    [TestClass]
    public sealed class SingleInstanceGuardTests
    {
        [TestMethod]
        public void IsFirstInstance_WithNoOtherInstance_ShouldBeTrue()
        {
            using var guard = new SingleInstanceGuard(CreateName());

            Assert.IsTrue(guard.IsFirstInstance);
        }

        [TestMethod]
        public void IsFirstInstance_WhileAnotherIsOpen_ShouldBeFalse()
        {
            string name = CreateName();

            using var first = new SingleInstanceGuard(name);
            using var second = new SingleInstanceGuard(name);

            Assert.IsFalse(second.IsFirstInstance);
        }

        [TestMethod]
        public void IsFirstInstance_AfterThePreviousOneIsDisposed_ShouldBeTrue()
        {
            string name = CreateName();

            new SingleInstanceGuard(name).Dispose();

            using var next = new SingleInstanceGuard(name);

            Assert.IsTrue(next.IsFirstInstance);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void Constructor_WithInvalidName_ShouldThrowArgumentException(string? name)
        {
            TestAssert.Throws<ArgumentException>(() => new SingleInstanceGuard(name!));
        }

        // Un nombre distinto en cada prueba, para no chocar con la app ni entre sí.
        private static string CreateName()
        {
            return $@"Local\MinecraftBackupManagerTests-{Guid.NewGuid():N}";
        }
    }
}
