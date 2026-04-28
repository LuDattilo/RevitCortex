using RevitCortex.Core.Interop;
using Xunit;

namespace RevitCortex.Tests.Interop
{
    public class CortexElementRefTests
    {
        [Fact]
        public void HoldsCrossAppIdentityFields()
        {
            var elementRef = new CortexElementRef
            {
                SourceApp = "Revit",
                SourceFile = "Architectural.rvt",
                NavisInstanceGuid = "navis-guid",
                RevitElementId = "42",
                RevitUniqueId = "revit-unique",
                IfcGuid = "ifc-1",
                Category = "Walls",
                Family = "Basic Wall",
                Type = "Generic - 200mm"
            };

            Assert.Equal("Revit", elementRef.SourceApp);
            Assert.Equal("Architectural.rvt", elementRef.SourceFile);
            Assert.Equal("navis-guid", elementRef.NavisInstanceGuid);
            Assert.Equal("42", elementRef.RevitElementId);
            Assert.Equal("revit-unique", elementRef.RevitUniqueId);
            Assert.Equal("ifc-1", elementRef.IfcGuid);
            Assert.Equal("Walls", elementRef.Category);
            Assert.Equal("Basic Wall", elementRef.Family);
            Assert.Equal("Generic - 200mm", elementRef.Type);
        }
    }
}
