using Newtonsoft.Json.Linq;
using RevitCortex.Server.Tools;
using RevitCortex.Tools.LinkedFiles;
using Xunit;

namespace RevitCortex.Tests.LinkedFiles;

public class GetCoordinationModelsToolTests
{
    [Fact]
    public void Metadata_DescribesReadOnlyLinkedFilesTool()
    {
        var tool = new GetCoordinationModelsTool();

        Assert.Equal("get_coordination_models", tool.Name);
        Assert.Equal("LinkedFiles", tool.Category);
        Assert.True(tool.RequiresDocument);
        Assert.False(tool.IsDynamic);
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(0, 0)]
    [InlineData(12, 12)]
    [InlineData(500, 250)]
    public void NormalizeMaxInstances_UsesDefaultAndCap(int? rawValue, int expected)
    {
        Assert.Equal(expected, GetCoordinationModelsTool.NormalizeMaxInstances(rawValue));
    }

    [Theory]
    [InlineData(null, "Coordination A", true)]
    [InlineData("", "Coordination A", true)]
    [InlineData("coord", "Coordination A", true)]
    [InlineData("MODEL", "Coordination Model", true)]
    [InlineData("navis", "Coordination Model", false)]
    public void MatchesNameFilter_IsCaseInsensitive(string? filter, string candidate, bool expected)
    {
        Assert.Equal(expected, GetCoordinationModelsTool.MatchesNameFilter(filter, candidate));
    }

    [Fact]
    public void MatchesAnyNameFilter_MatchesInstanceNames()
    {
        var modelAndTypeNames = new[] { "Campus Model", "CM Type 01" };
        var instanceNames = new[] { "North Wing NWC", "South Wing NWC" };

        Assert.True(GetCoordinationModelsTool.MatchesAnyNameFilter("south", modelAndTypeNames, instanceNames));
        Assert.False(GetCoordinationModelsTool.MatchesAnyNameFilter("east", modelAndTypeNames, instanceNames));
    }

    [Fact]
    public void MatchesAnyNameFilter_MatchesModelAndTypeNames()
    {
        var modelAndTypeNames = new[] { "Campus Model", "CM Type 01" };
        var instanceNames = new[] { "North Wing NWC" };

        Assert.True(GetCoordinationModelsTool.MatchesAnyNameFilter("campus", modelAndTypeNames, instanceNames));
        Assert.True(GetCoordinationModelsTool.MatchesAnyNameFilter("type 01", modelAndTypeNames, instanceNames));
    }

    [Fact]
    public void ShapeCoordinationModelsCompact_PreservesCountersAndIdentifiersDropsVerboseFields()
    {
        // Safety contract per CLAUDE.md "Compact Responses (per-call)":
        //   counters and identifiers MUST survive; verbose per-item metadata MUST be stripped.
        var payload = JObject.Parse("""
        {
          "apiAvailable": true,
          "modelCount": 1,
          "totalInstances": 2,
          "matchedInstances": 2,
          "models": [
            {
              "typeId": 12345,
              "typeName": "Campus Model",
              "pathType": "Cloud",
              "isCloud": true,
              "path": "https://docs.b360.autodesk.com/projects/abc/folders/xyz/models/campus.nwc",
              "instanceCount": 2,
              "instances": [
                { "instanceId": 999001, "name": "Campus Model : 1", "origin": { "x": 0.0, "y": 0.0, "z": 0.0 } },
                { "instanceId": 999002, "name": "Campus Model : 2", "origin": { "x": 1500.5, "y": 0.0, "z": 0.0 } }
              ]
            }
          ],
          "message": "Found 1 coordination model type(s)."
        }
        """);

        var shaped = ToolResponseShaper.Shape("get_coordination_models", payload, compact: true, summaryOnly: false);

        // Top-level counters preserved (safety contract).
        Assert.True(shaped["apiAvailable"]!.Value<bool>());
        Assert.Equal(1, shaped["modelCount"]!.Value<int>());
        Assert.Equal(2, shaped["totalInstances"]!.Value<int>());
        Assert.Equal(2, shaped["matchedInstances"]!.Value<int>());

        var models = (JArray)shaped["models"]!;
        Assert.Single(models);

        // Per-model identifiers preserved.
        Assert.Equal(12345, models[0]!["typeId"]!.Value<long>());
        Assert.Equal("Campus Model", models[0]!["typeName"]!.Value<string>());
        Assert.True(models[0]!["isCloud"]!.Value<bool>());
        Assert.Equal(2, models[0]!["instanceCount"]!.Value<int>());

        // Per-model verbose fields stripped.
        Assert.Null(models[0]!["pathType"]);
        Assert.Null(models[0]!["path"]);

        var instances = (JArray)models[0]!["instances"]!;
        Assert.Equal(2, instances.Count);

        // Per-instance identifier preserved.
        Assert.Equal(999001, instances[0]!["instanceId"]!.Value<long>());
        Assert.Equal(999002, instances[1]!["instanceId"]!.Value<long>());

        // Per-instance verbose fields stripped.
        Assert.Null(instances[0]!["name"]);
        Assert.Null(instances[0]!["origin"]);
    }

    [Fact]
    public void ShapeCoordinationModelsNonCompact_ReturnsPayloadUnchanged()
    {
        var payload = JObject.Parse("""
        {
          "apiAvailable": true,
          "modelCount": 1,
          "totalInstances": 1,
          "matchedInstances": 1,
          "models": [
            { "typeId": 1, "typeName": "M", "pathType": "Cloud", "isCloud": true, "path": "p", "instanceCount": 1,
              "instances": [ { "instanceId": 9, "name": "n", "origin": { "x": 0, "y": 0, "z": 0 } } ] }
          ],
          "message": "ok"
        }
        """);

        var shaped = ToolResponseShaper.Shape("get_coordination_models", payload, compact: false, summaryOnly: false);

        // No shaping applied: verbose fields retained.
        Assert.Equal("Cloud", shaped["models"]![0]!["pathType"]!.Value<string>());
        Assert.Equal("p", shaped["models"]![0]!["path"]!.Value<string>());
        Assert.Equal("n", shaped["models"]![0]!["instances"]![0]!["name"]!.Value<string>());
        Assert.NotNull(shaped["models"]![0]!["instances"]![0]!["origin"]);
    }
}
