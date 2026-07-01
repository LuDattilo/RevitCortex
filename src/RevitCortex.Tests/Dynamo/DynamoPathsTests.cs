using System.IO;
using RevitCortex.Tools.Dynamo.Runtime;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynamoPathsTests
    {
        [Fact]
        public void DynamoForRevitDir_BuildsExpectedPath()
        {
            var p = DynamoPaths.DynamoForRevitDir(@"C:\Program Files\Autodesk", 2025);
            Assert.Equal(@"C:\Program Files\Autodesk\Revit 2025\AddIns\DynamoForRevit", p);
        }

        [Fact]
        public void Probe_ReportsAbsentWhenDllMissing()
        {
            var tempBase = Path.Combine(Path.GetTempPath(), "rc_no_dynamo_" + System.Guid.NewGuid().ToString("N"));
            var probe = new DynamoCapabilityProbe(tempBase);
            var caps = probe.Probe(2025);
            Assert.False(caps.IsPresent);
        }

        [Fact]
        public void Probe_ReportsPresentWhenDllsExist()
        {
            var tempBase = Path.Combine(Path.GetTempPath(), "rc_dynamo_" + System.Guid.NewGuid().ToString("N"));
            var dir = DynamoPaths.DynamoForRevitDir(tempBase, 2025);
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "DynamoRevitDS.dll"), "x");
                File.WriteAllText(Path.Combine(dir, "DynamoCore.dll"), "x");
                var probe = new DynamoCapabilityProbe(tempBase);
                var caps = probe.Probe(2025);
                Assert.True(caps.IsPresent);
                Assert.True(caps.CPython3Expected); // Revit 2025 -> Dynamo 3.x
            }
            finally { Directory.Delete(tempBase, true); }
        }

        [Fact]
        public void Probe_ReportsPresentWhenDllsInRevitSubfolder()
        {
            var tempBase = Path.Combine(Path.GetTempPath(), "rc_dynamo_sub_" + System.Guid.NewGuid().ToString("N"));
            var dir = DynamoPaths.DynamoForRevitDir(tempBase, 2025);
            var revitSub = Path.Combine(dir, "Revit");
            Directory.CreateDirectory(revitSub);
            try
            {
                // Real-install layout: DLLs live under {dir}\Revit\, not at the root.
                File.WriteAllText(Path.Combine(revitSub, "DynamoRevitDS.dll"), "x");
                File.WriteAllText(Path.Combine(revitSub, "DynamoCore.dll"), "x");
                var probe = new DynamoCapabilityProbe(tempBase);
                var caps = probe.Probe(2025);
                Assert.True(caps.IsPresent);
                Assert.True(caps.CPython3Expected); // Revit 2025 -> Dynamo 3.x
            }
            finally { Directory.Delete(tempBase, true); }
        }
    }
}
