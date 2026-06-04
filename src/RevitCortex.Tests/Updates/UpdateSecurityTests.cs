using RevitCortex.Plugin.Updates;
using System;
using System.IO;
using Xunit;

namespace RevitCortex.Tests.Updates;

/// <summary>
/// Security regressions for the auto-updater (ultrareview 2026-06-04, criticals C4/C5):
/// the download URL must be host-restricted, and the downloaded artifact must match the
/// SHA-256 published in the trusted manifest before the elevated installer runs.
/// </summary>
public class UpdateSecurityTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"rcsec_{Guid.NewGuid():N}");
    public UpdateSecurityTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }

    // SHA-256 of the ASCII bytes of "hello" (no newline).
    private const string HelloSha256 = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";

    // --- C5: download URL host allowlist ---

    [Theory]
    [InlineData("https://raw.githubusercontent.com/LuDattilo/revitcortex-releases/main/latest.zip")]
    [InlineData("https://github.com/LuDattilo/x/releases/download/v1/latest.zip")]
    [InlineData("https://1drv.ms/u/s!AbCdEf")]
    [InlineData("https://onedrive.live.com/download?cid=1&resid=2")]
    [InlineData("https://gpapartners.sharepoint.com/sites/x/latest.zip")]
    public void IsTrustedDownloadUrl_TrustedHosts_True(string url)
        => Assert.True(UpdateChecker.IsTrustedDownloadUrl(url));

    [Theory]
    [InlineData("http://1drv.ms/u/s!AbCdEf")]            // not HTTPS
    [InlineData("https://evil.example.com/latest.zip")]  // unknown host
    [InlineData("https://evilgithub.com/latest.zip")]    // suffix spoof
    [InlineData("https://github.com.evil.com/x.zip")]    // subdomain spoof
    [InlineData("https://notsharepoint.com/x.zip")]      // suffix spoof
    [InlineData("file:///C:/evil.zip")]                  // non-web scheme
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void IsTrustedDownloadUrl_UntrustedOrMalformed_False(string? url)
        => Assert.False(UpdateChecker.IsTrustedDownloadUrl(url));

    // --- C4: artifact integrity (SHA-256) ---

    [Fact]
    public void Sha256Matches_CorrectHashLowercase_True()
    {
        var path = Path.Combine(_tempDir, "a.bin");
        File.WriteAllText(path, "hello");
        Assert.True(UpdateChecker.Sha256Matches(path, HelloSha256));
    }

    [Fact]
    public void Sha256Matches_CorrectHashUppercase_True()
    {
        var path = Path.Combine(_tempDir, "b.bin");
        File.WriteAllText(path, "hello");
        Assert.True(UpdateChecker.Sha256Matches(path, HelloSha256.ToUpperInvariant()));
    }

    [Fact]
    public void Sha256Matches_WrongHash_False()
    {
        var path = Path.Combine(_tempDir, "c.bin");
        File.WriteAllText(path, "hello");
        Assert.False(UpdateChecker.Sha256Matches(path, new string('0', 64)));
    }

    [Fact]
    public void Sha256Matches_EmptyOrNullExpected_False()
    {
        var path = Path.Combine(_tempDir, "d.bin");
        File.WriteAllText(path, "hello");
        Assert.False(UpdateChecker.Sha256Matches(path, ""));
        Assert.False(UpdateChecker.Sha256Matches(path, null));
    }
}
