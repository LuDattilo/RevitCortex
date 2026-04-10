using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Annotations;

/// <summary>
/// Creates one or more text notes in the active or specified view.
/// </summary>
public class CreateTextNoteTool : ICortexTool
{
    public string Name => "create_text_note";
    public string Category => "Annotations";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var textNotes = input["textNotes"] as JArray;
        if (textNotes == null || textNotes.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "textNotes array is required",
                suggestion: "Provide {\"textNotes\": [{\"text\": \"Hello\", \"position\": {\"x\":0,\"y\":0,\"z\":0}}]}");

        var createdIds = new List<long>();
        var warnings = new List<string>();

        using var tx = new Transaction(doc, "RevitCortex: Create Text Notes");
        tx.Start();

        try
        {
            // Find default text note type
            var defaultTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            if (defaultTypeId == ElementId.InvalidElementId)
            {
                var firstType = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .FirstOrDefault();
                if (firstType != null)
                    defaultTypeId = firstType.Id;
            }

            foreach (var noteSpec in textNotes)
            {
                try
                {
                    CreateSingleTextNote(doc, (JObject)noteSpec, defaultTypeId, createdIds, warnings);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to create text note: {ex.Message}");
                }
            }
            tx.Commit();
        }
        catch
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            throw;
        }

        return CortexResult<object>.Ok(new
        {
            createdCount = createdIds.Count,
            createdTextNoteIds = createdIds,
            warnings
        });
    }

    private static void CreateSingleTextNote(
        Document doc, JObject spec, ElementId defaultTypeId,
        List<long> createdIds, List<string> warnings)
    {
        var text = spec["text"]?.Value<string>();
        if (string.IsNullOrEmpty(text))
        {
            warnings.Add("text is required for each text note");
            return;
        }

        var posToken = spec["position"];
        if (posToken == null)
        {
            warnings.Add("position {x,y,z} is required");
            return;
        }

        var position = ParseXYZ(posToken);

        // Resolve view
        var viewId = spec["viewId"]?.Value<long>() ?? -1;
        View? view;
        if (viewId > 0)
        {
#if REVIT2024_OR_GREATER
            view = doc.GetElement(new ElementId(viewId)) as View;
#else
            view = doc.GetElement(new ElementId((int)viewId)) as View;
#endif
        }
        else
        {
            view = doc.ActiveView;
        }

        if (view == null)
        {
            warnings.Add("Could not resolve target view");
            return;
        }

        // Resolve type
        var typeId = spec["textNoteTypeId"]?.Value<long>() ?? -1;
        var noteTypeId = defaultTypeId;
        if (typeId > 0)
        {
#if REVIT2024_OR_GREATER
            var typeElem = doc.GetElement(new ElementId(typeId));
#else
            var typeElem = doc.GetElement(new ElementId((int)typeId));
#endif
            if (typeElem is TextNoteType)
                noteTypeId = typeElem.Id;
            else
                warnings.Add($"TextNoteType {typeId} not found, using default");
        }

        // Build options
        var options = new TextNoteOptions(noteTypeId);
        var alignment = spec["horizontalAlignment"]?.Value<string>() ?? "Left";
        options.HorizontalAlignment = alignment.ToLowerInvariant() switch
        {
            "center" => HorizontalTextAlignment.Center,
            "right"  => HorizontalTextAlignment.Right,
            _        => HorizontalTextAlignment.Left,
        };

        var textNote = TextNote.Create(doc, view.Id, position, text, options);

        // Set width if specified
        var widthMm = spec["width"]?.Value<double>() ?? 0;
        if (widthMm > 0)
            textNote.Width = widthMm / MmPerFoot;

        createdIds.Add(GetIdLong(textNote.Id));
    }

    private static XYZ ParseXYZ(JToken token)
    {
        var x = token["x"]?.Value<double>() ?? 0;
        var y = token["y"]?.Value<double>() ?? 0;
        var z = token["z"]?.Value<double>() ?? 0;
        return new XYZ(x / MmPerFoot, y / MmPerFoot, z / MmPerFoot);
    }

    private static long GetIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}
