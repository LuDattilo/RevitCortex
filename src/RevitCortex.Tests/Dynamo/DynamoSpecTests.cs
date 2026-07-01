using System.Collections.Generic;
using RevitCortex.Tools.Dynamo.Building;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynamoSpecTests
    {
        [Fact]
        public void GraphPort_StoresNameAndType()
        {
            var p = new GraphPort("folderPath", "String");
            Assert.Equal("folderPath", p.Name);
            Assert.Equal("String", p.Type);
        }

        [Fact]
        public void DynamoGraphSpec_DefaultsEngineToCPython3()
        {
            var spec = new DynamoGraphSpec(
                "G", "OUT = 1",
                new List<GraphPort>(), new List<GraphPort>());
            Assert.Equal("CPython3", spec.Engine);
        }

        [Fact]
        public void DynamoValidationResult_OkHasNoErrors()
        {
            var r = DynamoValidationResult.Ok();
            Assert.True(r.IsValid);
            Assert.Empty(r.Errors);
        }

        [Fact]
        public void DynamoValidationResult_FailCarriesErrors()
        {
            var r = DynamoValidationResult.Fail("bad name", "empty code");
            Assert.False(r.IsValid);
            Assert.Equal(2, r.Errors.Count);
        }
    }
}
