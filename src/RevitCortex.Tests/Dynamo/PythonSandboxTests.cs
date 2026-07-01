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

        [Fact]
        public void Validate_BlocksNamespaceInsideAddReferenceString()
        {
            // Idiomatic Dynamo Python assembly load with the forbidden namespace
            // ONLY inside the string argument — CodeSandboxV2 alone misses this.
            var err = PythonSandbox.Validate("clr.AddReference('System.IO')\nfrom System.IO import File");
            Assert.NotNull(err);
        }

        [Fact]
        public void Validate_BlocksNamespaceInsideDoubleQuotedAddReference()
        {
            var err = PythonSandbox.Validate("clr.AddReference(\"System.Net\")");
            Assert.NotNull(err);
        }

        [Fact]
        public void Validate_BlocksImportSystemDiagnostics()
        {
            var err = PythonSandbox.Validate("import System.Diagnostics");
            Assert.NotNull(err);
        }

        [Fact]
        public void Validate_AllowsCleanPythonWithStrings()
        {
            // A harmless string that merely contains a word must not false-positive.
            var err = PythonSandbox.Validate("name = 'hello world'\nOUT = name.upper()");
            Assert.Null(err);
        }
    }
}
