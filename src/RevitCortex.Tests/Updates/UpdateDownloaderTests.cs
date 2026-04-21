using RevitCortex.Plugin.Updates;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RevitCortex.Tests.Updates;

public class UpdateDownloaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"rctest_{Guid.NewGuid():N}");

    public UpdateDownloaderTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    // Builds a valid zip byte array in memory (single entry "install.ps1")
    private static byte[] MakeFakeZip()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("install.ps1");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("# fake installer");
        }
        return ms.ToArray();
    }

    private static HttpClient MakeClient(byte[] responseBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHttpHandler(responseBody, status);
        return new HttpClient(handler);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsSuccess_WhenResponseIsValid()
    {
        var zipBytes = MakeFakeZip();
        var client = MakeClient(zipBytes);
        var destPath = Path.Combine(_tempDir, "update.zip");
        var progressEvents = new List<(long, long)>();
        // Use a synchronous IProgress<T> implementation so the callback fires inline
        // (Progress<T> posts to SynchronizationContext which may be null in xUnit and
        // defers the callback until after the assertion.)
        var progress = new SyncProgress<(long, long)>(p => progressEvents.Add(p));

        var result = await UpdateDownloader.DownloadAsync(
            "http://fake/latest.zip", destPath, progress, CancellationToken.None, client);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.True(File.Exists(destPath));
        Assert.True(new FileInfo(destPath).Length > 0);
        Assert.NotEmpty(progressEvents);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsFailure_WhenServerReturns404()
    {
        var client = MakeClient(Array.Empty<byte>(), HttpStatusCode.NotFound);
        var destPath = Path.Combine(_tempDir, "update.zip");

        var result = await UpdateDownloader.DownloadAsync(
            "http://fake/latest.zip", destPath, null, CancellationToken.None, client);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.False(File.Exists(destPath));
    }

    [Fact]
    public async Task DownloadAsync_ReturnsFailure_WhenCancelled()
    {
        var zipBytes = MakeFakeZip();
        var client = MakeClient(zipBytes);
        var destPath = Path.Combine(_tempDir, "update.zip");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await UpdateDownloader.DownloadAsync(
            "http://fake/latest.zip", destPath, null, cts.Token, client);

        Assert.False(result.Success);
        Assert.False(File.Exists(destPath));
    }

    [Fact]
    public async Task ExtractAsync_ExtractsFilesToDestination()
    {
        var zipBytes = MakeFakeZip();
        var zipPath = Path.Combine(_tempDir, "update.zip");
        await File.WriteAllBytesAsync(zipPath, zipBytes);
        var extractDir = Path.Combine(_tempDir, "extracted");

        var outPath = await UpdateDownloader.ExtractAsync(zipPath, extractDir, CancellationToken.None);

        Assert.Equal(extractDir, outPath);
        Assert.True(File.Exists(Path.Combine(extractDir, "install.ps1")));
    }

    [Fact]
    public async Task ExtractAsync_ThrowsOnEmptyZip()
    {
        var zipPath = Path.Combine(_tempDir, "empty.zip");
        await File.WriteAllBytesAsync(zipPath, Array.Empty<byte>());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdateDownloader.ExtractAsync(zipPath, _tempDir, CancellationToken.None));
    }

    // Synchronous IProgress<T> — invokes callback inline on the calling thread
    // so assertions can safely check events immediately after await.
    private class SyncProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    // Minimal fake HTTP handler
    private class FakeHttpHandler(byte[] body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(body)
            };
            response.Content.Headers.ContentLength = body.Length;
            return Task.FromResult(response);
        }
    }
}
