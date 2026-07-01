using System.IO;
using RevitCortex.Tools.Dynamo.Runtime;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynamoRuntimeLoaderTests
    {
        [Fact]
        public void EnsureLoaded_ReturnsError_WhenDirNull()
        {
            var err = new DynamoRuntimeLoader(null).EnsureLoaded();
            Assert.NotNull(err); // no throw, error string
        }

        [Fact]
        public void EnsureLoaded_ReturnsError_WhenDirMissing()
        {
            var missing = Path.Combine(Path.GetTempPath(), "rc_no_dynamo_dir_" + System.Guid.NewGuid().ToString("N"));
            var err = new DynamoRuntimeLoader(missing).EnsureLoaded();
            Assert.NotNull(err);
            Assert.Contains("not found", err!);
        }

        [Fact]
        public void EnsureLoaded_ReturnsError_WhenDirExistsButNoDll()
        {
            var dir = Path.Combine(Path.GetTempPath(), "rc_empty_dynamo_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var err = new DynamoRuntimeLoader(dir).EnsureLoaded(); // exercises HookResolver too
                Assert.NotNull(err);
                Assert.Contains("DynamoRevitDS.dll", err!);
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
