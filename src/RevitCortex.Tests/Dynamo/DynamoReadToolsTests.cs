using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Tools.Dynamo.Building;
using RevitCortex.Tools.Dynamo.Tools;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynamoReadToolsTests
    {
        private static CortexSession NewSession() => new CortexSession(new SessionStore());

        [Fact]
        public void GetStatus_MetadataIsReadOnlyDynamicNoDoc()
        {
            var t = new DynamoGetStatusTool();
            Assert.Equal("dynamo_get_status", t.Name);
            Assert.True(t.IsDynamic);
            Assert.False(t.RequiresDocument);
        }

        [Fact]
        public void GetStatus_ReturnsEnableDynamoAndPresenceFields()
        {
            var t = new DynamoGetStatusTool();
            var res = t.Execute(new JObject { ["revitYear"] = 2025 }, NewSession());
            Assert.True(res.Success);
            var data = JObject.FromObject(res.Data!);
            Assert.NotNull(data["enableDynamo"]);
            Assert.NotNull(data["isPresent"]);
            Assert.Equal(2025, (int)data["revitYear"]!);
        }

        [Fact]
        public void ListGraphIo_IsStaticReadOnly()
        {
            var t = new DynamoListGraphIoTool();
            Assert.Equal("dynamo_list_graph_io", t.Name);
            Assert.False(t.IsDynamic);
        }

        [Fact]
        public void ListGraphIo_ReturnsIoForRealDyn()
        {
            var dyn = new DynamoGraphBuilder().BuildDynJson(new DynamoGraphSpec(
                "T", "OUT = IN[0]",
                new List<GraphPort> { new GraphPort("folder", "String") },
                new List<GraphPort> { new GraphPort("result", "String") }));
            var path = Path.Combine(Path.GetTempPath(), "rc_io_" + System.Guid.NewGuid().ToString("N") + ".dyn");
            File.WriteAllText(path, dyn);
            try
            {
                var t = new DynamoListGraphIoTool();
                var res = t.Execute(new JObject { ["dynPath"] = path }, NewSession());
                Assert.True(res.Success);
                var data = JObject.FromObject(res.Data!);
                Assert.Equal("T", (string)data["name"]!);
                Assert.Equal(1, ((JArray)data["inputs"]!).Count);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ListGraphIo_FailsOnMissingDynPath()
        {
            var t = new DynamoListGraphIoTool();
            var res = t.Execute(new JObject(), NewSession());
            Assert.False(res.Success);
            Assert.Equal(CortexErrorCode.InvalidInput, res.Error!.Code);
        }

        [Fact]
        public void ListGraphIo_FailsOnMissingFile()
        {
            var t = new DynamoListGraphIoTool();
            var res = t.Execute(new JObject { ["dynPath"] = @"C:\does\not\exist_" + System.Guid.NewGuid().ToString("N") + ".dyn" }, NewSession());
            Assert.False(res.Success);
            Assert.Equal(CortexErrorCode.ElementNotFound, res.Error!.Code);
        }
    }
}
