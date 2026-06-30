using System.IO;
using RevitCortex.Core.Security;
using Xunit;

namespace RevitCortex.Tests.Security;

public class CortexSettingsTests
{
    [Fact]
    public void Load_MissingFile_ReturnsDefaultsAllDisabled()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"rc-test-{System.Guid.NewGuid():N}.json");
        var settings = CortexSettings.Load(tempPath);
        Assert.False(settings.EnableCodeExecution);
        Assert.Equal(8080, settings.Port);
    }

    [Fact]
    public void Load_ExistingFile_ParsesEnableCodeExecution()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"rc-test-{System.Guid.NewGuid():N}.json");
        File.WriteAllText(tempPath, "{\"EnableCodeExecution\": true, \"Port\": 9090}");
        try
        {
            var settings = CortexSettings.Load(tempPath);
            Assert.True(settings.EnableCodeExecution);
            Assert.Equal(9090, settings.Port);
        }
        finally { File.Delete(tempPath); }
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"rc-test-{System.Guid.NewGuid():N}.json");
        File.WriteAllText(tempPath, "{ not valid json");
        try
        {
            var settings = CortexSettings.Load(tempPath);
            Assert.False(settings.EnableCodeExecution);
        }
        finally { File.Delete(tempPath); }
    }

    [Fact]
    public void SaveThenLoad_RoundTrip_PreservesValues()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"rc-test-{System.Guid.NewGuid():N}.json");
        try
        {
            var settings = new CortexSettings { EnableCodeExecution = true, Port = 9999 };
            settings.Save(tempPath);
            var loaded = CortexSettings.Load(tempPath);
            Assert.True(loaded.EnableCodeExecution);
            Assert.Equal(9999, loaded.Port);
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    [Fact]
    public void SetEnableCodeExecution_PreservesUnmodeledKeys()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"rc-test-{System.Guid.NewGuid():N}.json");
        File.WriteAllText(tempPath,
            "{\"EnableCodeExecution\": false, \"Port\": 9090, \"DisabledTools\": [\"foo\",\"bar\"]}");
        try
        {
            CortexSettings.SetEnableCodeExecution(true, tempPath);

            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(tempPath));
            Assert.True(json.Value<bool>("EnableCodeExecution"));
            // keys not modeled by CortexSettings must survive the write
            Assert.Equal(9090, json.Value<int>("Port"));
            Assert.Equal(new[] { "foo", "bar" }, json["DisabledTools"]!.ToObject<string[]>());
            // and the strongly-typed loader still parses the flag
            Assert.True(CortexSettings.Load(tempPath).EnableCodeExecution);
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    [Fact]
    public void SetEnableCodeExecution_MissingFile_CreatesFileWithFlag()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"rc-test-{System.Guid.NewGuid():N}.json");
        try
        {
            CortexSettings.SetEnableCodeExecution(true, tempPath);
            Assert.True(CortexSettings.Load(tempPath).EnableCodeExecution);

            CortexSettings.SetEnableCodeExecution(false, tempPath);
            Assert.False(CortexSettings.Load(tempPath).EnableCodeExecution);
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }
}
