using RiveTT.Core.Hosting;
using Xunit;

namespace RiveTT.Tests.Hosting;

public class CortexEnvironmentTests
{
    [Fact]
    public void CreateDefault_UsesLocalApplicationData()
    {
        var expected = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "RiveTT");

        Assert.Equal(expected, CortexEnvironment.CreateDefault().RootFolder);
    }

    [Fact]
    public void Paths_DeriveFromRootFolder()
    {
        var env = CortexEnvironment.ForTests(@"C:\temp\RiveTT");

        Assert.Equal(@"C:\temp\RiveTT\audit.jsonl", env.AuditLogPath);
        Assert.Equal(@"C:\temp\RiveTT\scripts", env.ScriptsFolder);
    }
}
