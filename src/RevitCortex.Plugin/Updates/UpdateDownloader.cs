using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RevitCortex.Plugin.Updates;

public static class UpdateDownloader
{
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Downloads the zip at <paramref name="url"/> to <paramref name="destZipPath"/>,
    /// reporting (bytesReceived, totalBytes) via <paramref name="progress"/>.
    /// Pass a custom <paramref name="httpClient"/> for testing; production passes null.
    /// </summary>
    public static async Task<UpdateDownloadResult> DownloadAsync(
        string url,
        string destZipPath,
        IProgress<(long Received, long Total)>? progress,
        CancellationToken ct,
        HttpClient? httpClient = null)
    {
        bool ownsClient = httpClient is null;
        HttpClient http = httpClient ?? new HttpClient { Timeout = DownloadTimeout };

        try
        {
            ct.ThrowIfCancellationRequested();

            var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
                return Fail($"Server returned {(int)response.StatusCode} {response.ReasonPhrase}");

            long total = response.Content.Headers.ContentLength ?? -1;
            long received = 0;

            string? dir = Path.GetDirectoryName(destZipPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var stream = await response.Content.ReadAsStreamAsync();
            using var file = new FileStream(destZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await file.WriteAsync(buffer, 0, read, ct);
                received += read;
                progress?.Report((received, total));
            }

            return new UpdateDownloadResult(true, null, destZipPath);
        }
        catch (OperationCanceledException)
        {
            TryDelete(destZipPath);
            return Fail("Download cancelled");
        }
        catch (Exception ex)
        {
            TryDelete(destZipPath);
            return Fail(ex.Message);
        }
        finally
        {
            if (ownsClient) http.Dispose();
        }
    }

    /// <summary>
    /// Extracts <paramref name="zipPath"/> into <paramref name="destDir"/>.
    /// Throws <see cref="InvalidDataException"/> if the zip is empty or corrupt.
    /// Returns <paramref name="destDir"/> on success.
    /// </summary>
    public static async Task<string> ExtractAsync(string zipPath, string destDir, CancellationToken ct)
    {
        // Validate before extracting
        var info = new FileInfo(zipPath);
        if (!info.Exists || info.Length == 0)
            throw new InvalidDataException($"Zip file is missing or empty: {zipPath}");

        // ZipFile.OpenRead throws InvalidDataException on corrupt zips
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            if (zip.Entries.Count == 0)
                throw new InvalidDataException("Zip file contains no entries");
        }

        ct.ThrowIfCancellationRequested();

        if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        Directory.CreateDirectory(destDir);

        // Run synchronous extraction on thread pool to avoid blocking UI thread.
        // Extract entry-by-entry with a zip-slip guard: ZipFile.ExtractToDirectory on
        // net48 does NOT validate that entries stay inside destDir, so a crafted entry
        // name like "..\..\evil.ps1" could escape the target folder.
        await Task.Run(() => ExtractWithSlipGuard(zipPath, destDir), ct);

        return destDir;
    }

    private static void ExtractWithSlipGuard(string zipPath, string destDir)
    {
        // Canonical destination root, with a trailing separator so the prefix check
        // cannot be satisfied by a sibling folder whose name shares a prefix.
        string destRoot = Path.GetFullPath(destDir);
        if (!destRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            destRoot += Path.DirectorySeparatorChar;

        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            string targetPath = Path.GetFullPath(Path.Combine(destRoot, entry.FullName));

            if (!targetPath.StartsWith(destRoot, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Zip entry '{entry.FullName}' would extract outside the target directory (zip-slip).");

            // Directory entry (trailing slash, no file name): just ensure it exists.
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            string? entryDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(entryDir))
                Directory.CreateDirectory(entryDir);

            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private static UpdateDownloadResult Fail(string msg) => new(false, msg, null);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

public record UpdateDownloadResult(bool Success, string? ErrorMessage, string? ZipPath);
