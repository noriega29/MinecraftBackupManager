using MinecraftBackupManager.Restic;
using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Tests.Restic
{
    [TestClass]
    public sealed class ResticBackupKindExtensionsTests
    {
        [TestMethod]
        [DataRow(ResticBackupKind.Initial, "initial")]
        [DataRow(ResticBackupKind.Manual, "manual")]
        [DataRow(ResticBackupKind.Hourly, "hourly")]
        [DataRow(ResticBackupKind.Daily, "daily")]
        [DataRow(ResticBackupKind.Weekly, "weekly")]
        public void ToTag_ShouldReturnExpectedEnglishTag(ResticBackupKind kind, string expectedTag)
        {
            Assert.AreEqual(expectedTag, kind.ToTag());
        }

        [TestMethod]
        public void ToTag_ShouldCoverEveryKindWithUniqueTags()
        {
            string[] tags = Enum.GetValues<ResticBackupKind>().Select(kind => kind.ToTag()).ToArray();

            Assert.AreEqual(tags.Length, tags.Distinct().Count());
        }

        [TestMethod]
        public void ToTag_WithUndefinedKind_ShouldThrowArgumentOutOfRangeException()
        {
            TestAssert.Throws<ArgumentOutOfRangeException>(() => ((ResticBackupKind)99).ToTag());
        }
    }
}
