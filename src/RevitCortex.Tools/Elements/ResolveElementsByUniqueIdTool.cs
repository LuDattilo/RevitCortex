using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Interop;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements
{
    /// <summary>
    /// Resolves Revit UniqueId strings to ElementId records. This is the
    /// cross-app bridge used by NavisCortex when Navisworks exposes a
    /// Revit-derived InstanceGuid / UniqueId and the next call needs a
    /// Revit ElementId.
    /// </summary>
    public class ResolveElementsByUniqueIdTool : ICortexTool
    {
        public string Name => "get_elements_by_unique_id";
        public string Category => "Elements";
        public bool RequiresDocument => true;
        public bool IsDynamic => false;
        public string Description => "Resolve Revit UniqueId strings to ElementId records for cross-app workflows.";

        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            var uniqueIds = input["uniqueIds"]?.ToObject<string[]>();
            if (uniqueIds == null || uniqueIds.Length == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "uniqueIds is required and cannot be empty",
                    suggestion: "Provide an array of Revit UniqueId strings, e.g. {\"uniqueIds\":[\"...\"]}");

            var doc = session.Store.Get<object>("activeDocument") as Document;
            if (doc == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "No active document in session");

            var elements = new List<object>();
            var notFound = new List<object>();

            foreach (var raw in uniqueIds)
            {
                var uniqueId = raw == null ? "" : raw.Trim();
                if (string.IsNullOrEmpty(uniqueId))
                {
                    notFound.Add(new { uniqueId = (object)(raw ?? ""), reason = "empty UniqueId" });
                    continue;
                }

                try
                {
                    var element = doc.GetElement(uniqueId);
                    if (element == null)
                    {
                        notFound.Add(new { uniqueId = (object)uniqueId, reason = "element not found" });
                        continue;
                    }

                    elements.Add(new
                    {
                        uniqueId,
                        elementId = ToolHelpers.GetElementIdValue(element.Id),
                        name = element.Name,
                        category = element.Category?.Name,
                        cortexElementRef = new CortexElementRef
                        {
                            SourceApp = "Revit",
                            SourceFile = doc.PathName,
                            RevitElementId = ToolHelpers.GetElementIdValue(element.Id).ToString(),
                            RevitUniqueId = uniqueId,
                            Category = element.Category?.Name,
                            Family = GetFamilyName(element),
                            Type = GetTypeName(doc, element)
                        }
                    });
                }
                catch (Exception ex)
                {
                    notFound.Add(new { uniqueId = (object)uniqueId, reason = ex.Message });
                }
            }

            return CortexResult<object>.Ok(new
            {
                requested = uniqueIds.Length,
                resolved = elements.Count,
                elements,
                notFound
            });
        }

        private static string? GetFamilyName(Element element)
        {
            var familyInstance = element as FamilyInstance;
            if (familyInstance != null)
                return familyInstance.Symbol?.Family?.Name;
            return null;
        }

        private static string? GetTypeName(Document doc, Element element)
        {
            var typeId = element.GetTypeId();
            if (typeId == ElementId.InvalidElementId) return null;
            var typeElement = doc.GetElement(typeId);
            return typeElement?.Name;
        }
    }
}
