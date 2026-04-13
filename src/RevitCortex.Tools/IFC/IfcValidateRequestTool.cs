using System;
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Validates an IFC file path: checks existence, extension, file size,
/// and reads the IFC header line to detect schema version.
/// </summary>
public class IfcValidateRequestTool : ICortexTool
{
    public string Name => "ifc_validate_request";
    public string Category => "IFC";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Validate an IFC file path, check format, and report basic metadata";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var filePath = input["filePath"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "filePath is required",
                suggestion: "Provide the full path to an IFC file");

        if (!File.Exists(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"File not found: {filePath}",
                suggestion: "Check the file path and ensure it exists");

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext != ".ifc" && ext != ".ifczip" && ext != ".ifcxml")
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unsupported extension: {ext}",
                suggestion: "Supported extensions: .ifc, .ifczip, .ifcxml");

        var fileInfo = new FileInfo(filePath);
        var fileSizeMb = Math.Round(fileInfo.Length / (1024.0 * 1024.0), 2);

        string? detectedSchema = null;
        try
        {
            using var reader = new StreamReader(filePath);
            for (int i = 0; i < 50; i++)
            {
                var line = reader.ReadLine();
                if (line == null) break;
                if (line.Contains("FILE_SCHEMA"))
                {
                    var start = line.IndexOf("'", StringComparison.Ordinal);
                    var end = start >= 0 ? line.IndexOf("'", start + 1, StringComparison.Ordinal) : -1;
                    if (start >= 0 && end > start)
                        detectedSchema = line.Substring(start + 1, end - start - 1);
                    break;
                }
            }
        }
        catch
        {
            // Non-critical — just can't read header
        }

        return CortexResult<object>.Ok(new
        {
            valid = true,
            filePath,
            extension = ext,
            fileSizeMb,
            detectedSchema,
            lastModified = fileInfo.LastWriteTimeUtc.ToString("o"),
        });
    }
}
