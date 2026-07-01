using System.Collections.Generic;
using RevitCortex.Tools.Dynamo.Building;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynGraphReaderTests
    {
        private static string BuildSampleDyn()
            => new DynamoGraphBuilder().BuildDynJson(new DynamoGraphSpec(
                "RoundTrip",
                "OUT = IN[0]",
                new List<GraphPort> { new GraphPort("folder", "String") },
                new List<GraphPort> { new GraphPort("result", "String") }));

        [Fact]
        public void Read_ExtractsNameAndEngineAndCounts()
        {
            var info = DynGraphReader.Read(BuildSampleDyn());
            Assert.Equal("RoundTrip", info.Name);
            Assert.Equal("CPython3", info.PythonEngine);
            Assert.Equal(1, info.PythonNodeCount);
            Assert.Equal(3, info.TotalNodes); // 1 input + 1 python + 1 watch
        }

        [Fact]
        public void Read_ListsInputsAndOutputs()
        {
            var info = DynGraphReader.Read(BuildSampleDyn());
            Assert.Single(info.Inputs);
            Assert.Equal("folder", info.Inputs[0].Name);
            Assert.Single(info.Outputs);
            Assert.Equal("result", info.Outputs[0].Name);
        }

        [Fact]
        public void Read_WarnsOnMissingEngine_InterpretedAsIronPython2()
        {
            var dyn = "{\"Nodes\":[{\"ConcreteType\":\"PythonNodeModels.PythonNode, PythonNodeModels\",\"NodeType\":\"PythonScriptNode\"}],\"Inputs\":[],\"Outputs\":[],\"View\":{\"NodeViews\":[]}}";
            var info = DynGraphReader.Read(dyn);
            Assert.Contains(info.Warnings, w => w.Contains("IronPython2"));
        }

        [Fact]
        public void Read_ThrowsOnInvalidJson()
        {
            Assert.ThrowsAny<System.Exception>(() => DynGraphReader.Read("{ not json"));
        }

        [Fact]
        public void Read_DoesNotThrow_OnNodesArrayWithPrimitiveElement()
        {
            var info = DynGraphReader.Read("{\"Nodes\":[123, null],\"Inputs\":[],\"Outputs\":[],\"View\":{}}");
            Assert.Equal(2, info.TotalNodes); // raw element count preserved
            Assert.Equal(0, info.PythonNodeCount);
        }

        [Fact]
        public void Read_DoesNotThrow_OnEngineAsObject()
        {
            var dyn = "{\"Nodes\":[{\"ConcreteType\":\"PythonNodeModels.PythonNode, PythonNodeModels\",\"Engine\":{}}],\"Inputs\":[],\"Outputs\":[],\"View\":{}}";
            var info = DynGraphReader.Read(dyn);
            Assert.Equal("IronPython2", info.PythonEngine); // non-string Engine treated as missing
            Assert.Contains(info.Warnings, w => w.Contains("IronPython2"));
        }

        [Fact]
        public void Read_DoesNotThrow_OnInputsWithPrimitiveElement()
        {
            var info = DynGraphReader.Read("{\"Nodes\":[],\"Inputs\":[42],\"Outputs\":[],\"View\":{}}");
            Assert.Empty(info.Inputs); // primitive element skipped
        }

        [Fact]
        public void Read_ExtractsDynamoVersion()
        {
            var info = DynGraphReader.Read(BuildSampleDyn());
            Assert.Equal("3.0.0.0", info.DynamoVersion);
        }

        [Fact]
        public void Read_CountsMultiplePythonNodes_FirstEngineWins()
        {
            var dyn = "{\"Nodes\":[{\"ConcreteType\":\"PythonNodeModels.PythonNode, PythonNodeModels\",\"Engine\":\"CPython3\"},{\"ConcreteType\":\"PythonNodeModels.PythonNode, PythonNodeModels\"}],\"Inputs\":[],\"Outputs\":[],\"View\":{}}";
            var info = DynGraphReader.Read(dyn);
            Assert.Equal(2, info.PythonNodeCount);
            Assert.Equal("CPython3", info.PythonEngine);
        }

        [Fact]
        public void Read_MapsInputFieldsCorrectly()
        {
            var info = DynGraphReader.Read(BuildSampleDyn());
            Assert.Single(info.Inputs);
            Assert.Equal("folder", info.Inputs[0].Name);
            Assert.Equal("string", info.Inputs[0].Type); // builder lowercases top-level input Type
            Assert.False(string.IsNullOrEmpty(info.Inputs[0].NodeId)); // guards against positional ctor arg swap
        }
    }
}
