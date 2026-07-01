using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Security;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class CortexSettingsDynamoTests
    {
        [Fact]
        public void EnableDynamo_DefaultsToFalse()
        {
            var s = new CortexSettings();
            Assert.False(s.EnableDynamo);
        }

        [Fact]
        public void EnableDynamo_RoundTripsThroughJson()
        {
            var path = Path.Combine(Path.GetTempPath(), "rc_dyn_settings_" + System.Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var s = new CortexSettings { EnableDynamo = true };
                s.Save(path);
                var loaded = CortexSettings.Load(path);
                Assert.True(loaded.EnableDynamo);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void EnableDynamo_SerializesWithExpectedJsonName()
        {
            var s = new CortexSettings { EnableDynamo = true };
            var json = JObject.FromObject(s);
            Assert.True((bool)json["EnableDynamo"]!);
        }
    }
}
