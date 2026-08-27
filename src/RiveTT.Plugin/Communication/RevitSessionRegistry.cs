using System;
using System.IO;
using Newtonsoft.Json;

namespace RiveTT.Plugin.Communication;

internal sealed class RevitSessionRecord
{
    public int ProcessId { get; set; }
    public string PipeName { get; set; } = "";
    public string StartedAtUtc { get; set; } = "";
}

internal static class RevitSessionRegistry
{
    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RiveTT", "sessions");

    public static void Publish(string pipeName, int processId)
    {
        Directory.CreateDirectory(DirectoryPath);
        var record = new RevitSessionRecord
        {
            ProcessId = processId,
            PipeName = pipeName,
            StartedAtUtc = DateTime.UtcNow.ToString("O")
        };
        File.WriteAllText(Path.Combine(DirectoryPath, $"{processId}.json"),
            JsonConvert.SerializeObject(record));
    }

    public static void Remove(int processId)
    {
        try { File.Delete(Path.Combine(DirectoryPath, $"{processId}.json")); }
        catch { }
    }
}
