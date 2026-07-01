using RevitCortex.Tools.Dynamo.Security;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class PythonSandboxTests
    {
        [Fact]
        public void Validate_AllowsCleanPython()
        {
            var err = PythonSandbox.Validate("OUT = IN[0] + 1");
            Assert.Null(err);
        }

        [Fact]
        public void Validate_BlocksSystemIo()
        {
            var err = PythonSandbox.Validate("import System.IO\nSystem.IO.File.Delete('x')");
            Assert.NotNull(err);
        }

        [Fact]
        public void Validate_BlocksSystemNet()
        {
            var err = PythonSandbox.Validate("clr.AddReference('System.Net')\nSystem.Net.WebClient()");
            Assert.NotNull(err);
        }

        [Fact]
        public void Validate_BlocksProcessStart()
        {
            var err = PythonSandbox.Validate("System.Diagnostics.Process.Start('cmd')");
            Assert.NotNull(err);
        }
    }
}
