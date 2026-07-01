using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Tools.Dynamo.Tools;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynamoRunGraphToolGateTests
    {
        private static CortexSession NewSession() => new CortexSession(new SessionStore());

        private static string TempSettings(bool enableDynamo)
        {
            var path = Path.Combine(Path.GetTempPath(), "rc_run_settings_" + System.Guid.NewGuid().ToString("N") + ".json");
            new CortexSettings { EnableDynamo = enableDynamo }.Save(path);
            return path;
        }

        [Fact]
        public void Run_MetadataIsDynamicWriteRequiresDoc()
        {
            var t = new DynamoRunGraphTool();
            Assert.Equal("dynamo_run_graph", t.Name);
            Assert.True(t.IsDynamic);
            Assert.True(t.RequiresDocument);
        }

        [Fact]
        public void Run_RefusedWhenEnableDynamoFalse()
        {
            var t = new DynamoRunGraphTool { SettingsPathForTests = TempSettings(false) };
            var res = t.Execute(new JObject { ["dynPath"] = @"C:\x.dyn" }, NewSession());
            Assert.False(res.Success);
            Assert.Equal(CortexErrorCode.PermissionDenied, res.Error!.Code);
        }

        [Fact]
        public void Run_RequiresDynPath()
        {
            var t = new DynamoRunGraphTool { SettingsPathForTests = TempSettings(true), SkipConfirmationForTests = true };
            var res = t.Execute(new JObject(), NewSession());
            Assert.False(res.Success);
            Assert.Equal(CortexErrorCode.InvalidInput, res.Error!.Code);
        }

        [Fact]
        public void Run_FailsCleanWhenFileMissing()
        {
            var t = new DynamoRunGraphTool { SettingsPathForTests = TempSettings(true), SkipConfirmationForTests = true };
            var res = t.Execute(new JObject { ["dynPath"] = @"C:\does\not\exist_" + System.Guid.NewGuid().ToString("N") + ".dyn" }, NewSession());
            Assert.False(res.Success);
            Assert.Equal(CortexErrorCode.ElementNotFound, res.Error!.Code);
        }

        [Fact]
        public void Run_CancelledWhenUserDeclines()
        {
            var real = Path.Combine(Path.GetTempPath(), "rc_run_real_" + System.Guid.NewGuid().ToString("N") + ".dyn");
            File.WriteAllText(real, "{}");
            var t = new DynamoRunGraphTool { SettingsPathForTests = TempSettings(true) };
            var session = NewSession();
            session.ConfirmAction = (a, c, d) => false;
            try
            {
                var res = t.Execute(new JObject { ["dynPath"] = real }, session);
                Assert.False(res.Success);
                Assert.Equal(CortexErrorCode.Cancelled, res.Error!.Code);
            }
            finally { File.Delete(real); }
        }
    }
}
