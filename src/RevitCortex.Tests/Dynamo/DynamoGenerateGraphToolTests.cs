using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Tools.Dynamo.Building;
using RevitCortex.Tools.Dynamo.Tools;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynamoGenerateGraphToolTests
    {
        private static CortexSession NewSession() => new CortexSession(new SessionStore());

        private static string TempSettings(bool enableDynamo)
        {
            var path = Path.Combine(Path.GetTempPath(), "rc_gen_settings_" + System.Guid.NewGuid().ToString("N") + ".json");
            new CortexSettings { EnableDynamo = enableDynamo }.Save(path);
            return path;
        }

        [Fact]
        public void Generate_IsStaticWriteTool()
        {
            var t = new DynamoGenerateGraphTool();
            Assert.Equal("dynamo_generate_graph", t.Name);
            Assert.False(t.IsDynamic);
        }

        [Fact]
        public void Generate_RefusedWhenEnableDynamoFalse()
        {
            var t = new DynamoGenerateGraphTool { SettingsPathForTests = TempSettings(false) };
            var res = t.Execute(new JObject
            {
                ["name"] = "G",
                ["pythonCode"] = "OUT = 1",
                ["outputs"] = new JArray { new JObject { ["name"] = "result" } }
            }, NewSession());
            Assert.False(res.Success);
            Assert.Equal(CortexErrorCode.PermissionDenied, res.Error!.Code);
        }

        [Fact]
        public void Generate_BlocksUnsafePython()
        {
            var t = new DynamoGenerateGraphTool
            {
                SettingsPathForTests = TempSettings(true),
                SkipConfirmationForTests = true
            };
            var res = t.Execute(new JObject
            {
                ["name"] = "G",
                ["pythonCode"] = "import System.IO\nSystem.IO.File.Delete('x')"
            }, NewSession());
            Assert.False(res.Success);
            Assert.Equal(CortexErrorCode.PermissionDenied, res.Error!.Code);
        }

        [Fact]
        public void Generate_RequiresPythonCode()
        {
            var t = new DynamoGenerateGraphTool
            {
                SettingsPathForTests = TempSettings(true),
                SkipConfirmationForTests = true
            };
            var res = t.Execute(new JObject { ["name"] = "G" }, NewSession());
            Assert.False(res.Success);
            Assert.Equal(CortexErrorCode.InvalidInput, res.Error!.Code);
        }

        [Fact]
        public void Generate_WritesValidDynToGivenPath()
        {
            var outPath = Path.Combine(Path.GetTempPath(), "rc_gen_" + System.Guid.NewGuid().ToString("N") + ".dyn");
            var t = new DynamoGenerateGraphTool
            {
                SettingsPathForTests = TempSettings(true),
                SkipConfirmationForTests = true
            };
            try
            {
                var res = t.Execute(new JObject
                {
                    ["name"] = "ExportRooms",
                    ["pythonCode"] = "OUT = IN[0]",
                    ["inputs"] = new JArray { new JObject { ["name"] = "folder", ["type"] = "String" } },
                    ["outputs"] = new JArray { new JObject { ["name"] = "result" } },
                    ["savePath"] = outPath,
                    ["execute"] = false
                }, NewSession());
                Assert.True(res.Success);
                Assert.True(File.Exists(outPath));
                var info = DynGraphReader.Read(File.ReadAllText(outPath));
                Assert.Equal("ExportRooms", info.Name);
                Assert.Equal("CPython3", info.PythonEngine);
            }
            finally { if (File.Exists(outPath)) File.Delete(outPath); }
        }

        [Fact]
        public void Generate_BlockedUnsafePython_WritesNoFile()
        {
            var outPath = Path.Combine(Path.GetTempPath(), "rc_gen_blocked_" + System.Guid.NewGuid().ToString("N") + ".dyn");
            var t = new DynamoGenerateGraphTool
            {
                SettingsPathForTests = TempSettings(true),
                SkipConfirmationForTests = true
            };
            try
            {
                var res = t.Execute(new JObject
                {
                    ["name"] = "G",
                    ["pythonCode"] = "import System.IO\nSystem.IO.File.Delete('x')",
                    ["savePath"] = outPath
                }, NewSession());
                Assert.False(res.Success);
                Assert.Equal(CortexErrorCode.PermissionDenied, res.Error!.Code);
                Assert.False(File.Exists(outPath)); // fail-safety: no file written when a gate blocks
            }
            finally { if (File.Exists(outPath)) File.Delete(outPath); }
        }

        [Fact]
        public void Generate_CancelledConfirmation_FailsAndWritesNoFile()
        {
            var outPath = Path.Combine(Path.GetTempPath(), "rc_gen_cancel_" + System.Guid.NewGuid().ToString("N") + ".dyn");
            var t = new DynamoGenerateGraphTool
            {
                SettingsPathForTests = TempSettings(true)
                // NOTE: do NOT set SkipConfirmationForTests — we want the confirmation gate to run
            };
            var session = NewSession();
            session.ConfirmAction = (action, count, description) => false; // simulate user clicking "No"
            try
            {
                var res = t.Execute(new JObject
                {
                    ["name"] = "G",
                    ["pythonCode"] = "OUT = 1",
                    ["outputs"] = new JArray { new JObject { ["name"] = "result" } },
                    ["savePath"] = outPath
                }, session);
                Assert.False(res.Success);
                Assert.Equal(CortexErrorCode.Cancelled, res.Error!.Code);
                Assert.False(File.Exists(outPath)); // fail-safety: cancelled → no file
            }
            finally { if (File.Exists(outPath)) File.Delete(outPath); }
        }
    }
}
