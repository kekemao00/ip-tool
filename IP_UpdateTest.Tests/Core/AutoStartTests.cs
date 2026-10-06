using IP_UpdateTest.Core;
using Xunit;

namespace IP_UpdateTest.Tests.Core
{
    public class AutoStartTests
    {
        [Theory]
        [InlineData("\"C:\\Program Files\\IP Tool\\IP_UpdateTest.exe\" --tray", "C:\\Program Files\\IP Tool\\IP_UpdateTest.exe")]
        [InlineData("\"D:\\tools\\IPTool.exe\"", "D:\\tools\\IPTool.exe")]
        [InlineData("D:\\tools\\IPTool.exe --tray", "D:\\tools\\IPTool.exe")]
        [InlineData("  D:\\tools\\IPTool.exe", "D:\\tools\\IPTool.exe")]
        public void ParseExecutable_ReturnsProgramPath(string command, string expected)
        {
            Assert.Equal(expected, AutoStart.ParseExecutable(command));
        }

        [Fact]
        public void ParseExecutable_UnclosedQuote_ReturnsNull()
        {
            Assert.Null(AutoStart.ParseExecutable("\"D:\\tools\\IPTool.exe --tray"));
        }
    }
}
