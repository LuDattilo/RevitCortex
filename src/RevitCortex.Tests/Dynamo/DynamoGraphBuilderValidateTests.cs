using System.Collections.Generic;
using RevitCortex.Tools.Dynamo.Building;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynamoGraphBuilderValidateTests
    {
        private static DynamoGraphSpec Spec(string name, string code,
            List<GraphPort> ins = null, List<GraphPort> outs = null)
            => new DynamoGraphSpec(name, code, ins ?? new List<GraphPort>(), outs ?? new List<GraphPort>());

        [Fact]
        public void ValidateSpec_RejectsEmptyPythonCode()
        {
            var b = new DynamoGraphBuilder();
            var r = b.ValidateSpec(Spec("G", "   "));
            Assert.False(r.IsValid);
        }

        [Fact]
        public void ValidateSpec_RejectsDuplicateInputNames()
        {
            var b = new DynamoGraphBuilder();
            var ins = new List<GraphPort> { new GraphPort("x", "String"), new GraphPort("x", "Integer") };
            var r = b.ValidateSpec(Spec("G", "OUT = IN[0]", ins));
            Assert.False(r.IsValid);
        }

        [Fact]
        public void ValidateSpec_RejectsUnknownPortType()
        {
            var b = new DynamoGraphBuilder();
            var ins = new List<GraphPort> { new GraphPort("x", "Banana") };
            var r = b.ValidateSpec(Spec("G", "OUT = IN[0]", ins));
            Assert.False(r.IsValid);
        }

        [Fact]
        public void ValidateSpec_AcceptsValidSpec()
        {
            var b = new DynamoGraphBuilder();
            var ins = new List<GraphPort> { new GraphPort("folder", "String"), new GraphPort("limit", "Integer") };
            var outs = new List<GraphPort> { new GraphPort("result", "String") };
            var r = b.ValidateSpec(Spec("Export", "OUT = IN[0]", ins, outs));
            Assert.True(r.IsValid);
        }
    }
}
