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

        // --- Adversarial: dynamic-dispatch bypass (must be blocked) ---

        [Fact]
        public void Validate_BlocksGetattr()
        {
            var err = PythonSandbox.Validate("io = getattr(System, 'IO')");
            Assert.NotNull(err);
        }

        [Fact]
        public void Validate_BlocksDunderImport()
        {
            var err = PythonSandbox.Validate("m = __import__('System.IO')");
            Assert.NotNull(err); // caught by namespace scan OR dynamic-dispatch block
        }

        [Fact]
        public void Validate_BlocksEval()
        {
            var err = PythonSandbox.Validate("eval('do_something()')");
            Assert.NotNull(err);
        }

        [Fact]
        public void Validate_BlocksExec()
        {
            var err = PythonSandbox.Validate("exec('import os')");
            Assert.NotNull(err);
        }

        [Fact]
        public void Validate_BlocksSplitStringAddReference()
        {
            var err = PythonSandbox.Validate("clr.AddReference('System.' + 'IO')");
            Assert.NotNull(err); // non-literal AddReference argument is suspicious → block
        }

        [Fact]
        public void Validate_BlocksFullSplitTokenGetattrExploit()
        {
            var code = "import clr\nclr.AddReference(\"System.\" + \"IO\")\nimport System\nio = getattr(System, \"I\" + \"O\")\nf = getattr(io, \"Fi\" + \"le\")\ngetattr(f, \"Del\" + \"ete\")(\"C:/victim.txt\")";
            var err = PythonSandbox.Validate(code);
            Assert.NotNull(err);
        }

        // --- Must NOT false-positive on clean generated Python ---

        [Fact]
        public void Validate_AllowsCleanRevitApiPython()
        {
            // Typical safe generated body: uses the Revit API via clr, no dynamic dispatch, no forbidden ns.
            var code = "import clr\nclr.AddReference('RevitAPI')\nfrom Autodesk.Revit.DB import FilteredElementCollector\nOUT = len(list(IN))";
            var err = PythonSandbox.Validate(code);
            Assert.Null(err);
        }
    }
}
