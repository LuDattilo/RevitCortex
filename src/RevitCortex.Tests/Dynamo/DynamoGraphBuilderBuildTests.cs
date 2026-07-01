using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitCortex.Tools.Dynamo.Building;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynamoGraphBuilderBuildTests
    {
        private static JObject Build(DynamoGraphSpec spec)
            => JObject.Parse(new DynamoGraphBuilder().BuildDynJson(spec));

        private static DynamoGraphSpec Sample()
            => new DynamoGraphSpec(
                "ExportRooms",
                "OUT = IN[0]",
                new List<GraphPort> { new GraphPort("folder", "String"), new GraphPort("limit", "Integer") },
                new List<GraphPort> { new GraphPort("result", "String") });

        [Fact]
        public void Build_ProducesParseableJson_WithMandatoryTopLevelKeys()
        {
            var j = Build(Sample());
            foreach (var key in new[] { "Uuid", "IsCustomNode", "Inputs", "Outputs", "Nodes", "Connectors", "View" })
                Assert.True(j[key] != null, $"missing top-level key {key}");
        }

        [Fact]
        public void Build_HasExactlyOnePythonNode_WithCPython3Engine()
        {
            var j = Build(Sample());
            var py = ((JArray)j["Nodes"]).Single(n =>
                (string)n["ConcreteType"] == DynJsonSchema.PythonNodeConcreteType);
            Assert.Equal("CPython3", (string)py["Engine"]);
            Assert.Equal("PythonScriptNode", (string)py["NodeType"]);
            Assert.True((bool)py["VariableInputPorts"]);
        }

        [Fact]
        public void Build_CreatesInputNodesAndWatchOutputNodes()
        {
            var j = Build(Sample());
            var nodes = (JArray)j["Nodes"];
            Assert.Equal(4, nodes.Count); // 2 inputs + 1 python + 1 watch
            Assert.Equal(1, nodes.Count(n => (string)n["ConcreteType"] == DynJsonSchema.WatchConcreteType));
            Assert.Equal(2, nodes.Count(n =>
                (string)n["ConcreteType"] == DynJsonSchema.StringInputConcreteType
                || (string)n["ConcreteType"] == DynJsonSchema.IntegerSliderConcreteType));
        }

        [Fact]
        public void Build_EveryNodeHasMatchingNodeView()
        {
            var j = Build(Sample());
            var nodeIds = ((JArray)j["Nodes"]).Select(n => (string)n["Id"]).ToHashSet();
            var viewIds = ((JArray)j["View"]["NodeViews"]).Select(v => (string)v["Id"]).ToHashSet();
            Assert.Equal(nodeIds, viewIds);
        }

        [Fact]
        public void Build_ConnectorsReferenceExistingPortIds_AndHaveNoConcreteType()
        {
            var j = Build(Sample());
            var portIds = new HashSet<string>();
            foreach (var n in (JArray)j["Nodes"])
            {
                foreach (var p in (JArray)n["Inputs"]) portIds.Add((string)p["Id"]);
                foreach (var p in (JArray)n["Outputs"]) portIds.Add((string)p["Id"]);
            }
            foreach (var c in (JArray)j["Connectors"])
            {
                Assert.Null(c["ConcreteType"]);
                Assert.Contains((string)c["Start"], portIds);
                Assert.Contains((string)c["End"], portIds);
            }
        }

        [Fact]
        public void Build_TopLevelInputsReferenceNodeIds()
        {
            var j = Build(Sample());
            var nodeIds = ((JArray)j["Nodes"]).Select(n => (string)n["Id"]).ToHashSet();
            foreach (var inp in (JArray)j["Inputs"])
                Assert.Contains((string)inp["Id"], nodeIds);
        }

        [Fact]
        public void Build_PythonNodeHasOneInputPortPerSpecInput()
        {
            var j = Build(Sample());
            var py = ((JArray)j["Nodes"]).Single(n =>
                (string)n["ConcreteType"] == DynJsonSchema.PythonNodeConcreteType);
            Assert.Equal(2, ((JArray)py["Inputs"]).Count);
            Assert.Equal(1, ((JArray)py["Outputs"]).Count);
        }

        [Fact]
        public void Build_WithZeroInputs_PythonNodeHasNoInputPorts_AndNoInputNodes()
        {
            var spec = new DynamoGraphSpec(
                "G",
                "OUT = 42",
                new List<GraphPort>(),
                new List<GraphPort> { new GraphPort("result", "String") });

            var j = Build(spec); // succeeding proves JSON still parses
            var nodes = (JArray)j["Nodes"];

            var py = nodes.Single(n =>
                (string)n["ConcreteType"] == DynJsonSchema.PythonNodeConcreteType);
            Assert.Equal(0, ((JArray)py["Inputs"]).Count);

            var inputNodeCount = nodes.Count(n =>
                (string)n["ConcreteType"] == DynJsonSchema.StringInputConcreteType
                || (string)n["ConcreteType"] == DynJsonSchema.IntegerSliderConcreteType);
            Assert.Equal(0, inputNodeCount);
        }

        [Fact]
        public void Build_WithZeroOutputs_ProducesNoWatchNodes_AndNoConnectors()
        {
            var spec = new DynamoGraphSpec(
                "G",
                "OUT = IN[0]",
                new List<GraphPort> { new GraphPort("x", "String") },
                new List<GraphPort>());

            var j = Build(spec);
            var nodes = (JArray)j["Nodes"];

            var watchCount = nodes.Count(n =>
                (string)n["ConcreteType"] == DynJsonSchema.WatchConcreteType);
            Assert.Equal(0, watchCount);

            Assert.Equal(1, ((JArray)j["Connectors"]).Count); // only input->python
        }

        [Fact]
        public void Build_ConnectorCount_EqualsInputsPlusOutputs()
        {
            var spec = Sample(); // 2 inputs + 1 output
            var j = Build(spec);

            Assert.Equal(spec.Inputs.Count + spec.Outputs.Count, ((JArray)j["Connectors"]).Count);
            Assert.Equal(3, ((JArray)j["Connectors"]).Count);
        }
    }
}
