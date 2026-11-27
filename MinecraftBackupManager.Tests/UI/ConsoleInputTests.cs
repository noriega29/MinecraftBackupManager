using System.Text;
using MinecraftBackupManager.UI;

namespace MinecraftBackupManager.Tests.UI
{
    [TestClass]
    public sealed class ConsoleInputTests
    {
        [TestMethod]
        [DataRow(@"C:\Servidor", @"C:\Servidor")]
        [DataRow(@"""C:\Mi Servidor""", @"C:\Mi Servidor")]
        [DataRow(@"  ""C:\Servidor""  ", @"C:\Servidor")]
        [DataRow(@"""", @"""")]
        [DataRow(@"""""", "")]
        [DataRow("", "")]
        public void CleanPath_ShouldRemoveSpacesAndSurroundingQuotes(string input, string expected)
        {
            Assert.AreEqual(expected, ConsoleInput.CleanPath(input));
        }

        [TestMethod]
        public void ReadMasked_ShouldReturnTextAndShowOnlyAsterisks()
        {
            var screen = new StringBuilder();

            string result = Read(screen, Key('a'), Key(' '), Key('@'), Enter());

            Assert.AreEqual("a @", result);
            Assert.AreEqual("***", screen.ToString());
        }

        [TestMethod]
        public void ReadMasked_WithBackspace_ShouldRemoveTheLastCharacter()
        {
            var screen = new StringBuilder();

            string result = Read(screen, Key('a'), Key('b'), Backspace(), Key('c'), Enter());

            Assert.AreEqual("ac", result);
            Assert.AreEqual("**\b \b*", screen.ToString());
        }

        [TestMethod]
        public void ReadMasked_WithBackspaceOnEmptyText_ShouldWriteNothing()
        {
            var screen = new StringBuilder();

            string result = Read(screen, Backspace(), Enter());

            Assert.AreEqual(string.Empty, result);
            Assert.AreEqual(string.Empty, screen.ToString());
        }

        [TestMethod]
        public void ReadMasked_ShouldIgnoreKeysWithoutText()
        {
            var screen = new StringBuilder();

            var leftArrow = new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false);

            string result = Read(screen, leftArrow, Key('a'), Enter());

            Assert.AreEqual("a", result);
            Assert.AreEqual("*", screen.ToString());
        }

        // Entrega las teclas en orden, como si el usuario las pulsara.
        private static string Read(StringBuilder screen, params ConsoleKeyInfo[] keys)
        {
            var queue = new Queue<ConsoleKeyInfo>(keys);

            return ConsoleInput.ReadMasked(queue.Dequeue, text => screen.Append(text));
        }

        private static ConsoleKeyInfo Key(char character)
        {
            return new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false);
        }

        private static ConsoleKeyInfo Enter()
        {
            return new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
        }

        private static ConsoleKeyInfo Backspace()
        {
            return new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false);
        }
    }
}
