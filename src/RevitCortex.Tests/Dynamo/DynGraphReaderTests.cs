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
    }
}
