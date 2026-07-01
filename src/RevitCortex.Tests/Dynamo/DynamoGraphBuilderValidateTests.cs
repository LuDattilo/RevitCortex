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
            Assert.Contains(r.Errors, e => e.Contains("pythonCode"));
        }

        [Fact]
        public void ValidateSpec_RejectsDuplicateInputNames()
        {
            var b = new DynamoGraphBuilder();
            var ins = new List<GraphPort> { new GraphPort("x", "String"), new GraphPort("x", "Integer") };
            var r = b.ValidateSpec(Spec("G", "OUT = IN[0]", ins));
            Assert.False(r.IsValid);
            Assert.Contains(r.Errors, e => e.Contains("duplicate"));
        }

        [Fact]
        public void ValidateSpec_RejectsUnknownPortType()
        {
            var b = new DynamoGraphBuilder();
            var ins = new List<GraphPort> { new GraphPort("x", "Banana") };
            var r = b.ValidateSpec(Spec("G", "OUT = IN[0]", ins));
            Assert.False(r.IsValid);
            Assert.Contains(r.Errors, e => e.Contains("unknown"));
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

        [Fact]
        public void ValidateSpec_RejectsNullSpec()
        {
            var b = new DynamoGraphBuilder();
            var r = b.ValidateSpec(null);
            Assert.False(r.IsValid);
            Assert.Contains(r.Errors, e => e.Contains("null"));
        }

        [Fact]
        public void ValidateSpec_RejectsEmptyPortName()
        {
            var b = new DynamoGraphBuilder();
            var ins = new List<GraphPort> { new GraphPort(" ", "String") };
            var r = b.ValidateSpec(Spec("G", "OUT = IN[0]", ins));
            Assert.False(r.IsValid);
            Assert.Contains(r.Errors, e => e.Contains("empty name"));
        }

        [Fact]
        public void ValidateSpec_RejectsDuplicateOutputNames()
        {
            var b = new DynamoGraphBuilder();
            var outs = new List<GraphPort> { new GraphPort("result", "String"), new GraphPort("result", "String") };
            var r = b.ValidateSpec(Spec("G", "OUT = 1", null, outs));
            Assert.False(r.IsValid);
            Assert.Contains(r.Errors, e => e.Contains("duplicate"));
        }

        [Fact]
        public void ValidateSpec_TreatsCaseInsensitiveDuplicatesAsDuplicate()
        {
            var b = new DynamoGraphBuilder();
            var ins = new List<GraphPort> { new GraphPort("x", "String"), new GraphPort("X", "Integer") };
            var r = b.ValidateSpec(Spec("G", "OUT = IN[0]", ins));
            Assert.False(r.IsValid);
            Assert.Contains(r.Errors, e => e.Contains("duplicate"));
        }
    }
}
