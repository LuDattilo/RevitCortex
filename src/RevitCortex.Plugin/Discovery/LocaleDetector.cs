using Autodesk.Revit.DB;
using System.Linq;

namespace RevitCortex.Plugin.Discovery;

public static class LocaleDetector
{
    public static string Detect(Document doc)
    {
        var element = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .FirstOrDefault(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS) != null);

        if (element == null) return "en";

        var param = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (param == null) return "en";

        var name = param.Definition.Name;

        if (name == "Commenti" || name == "Commento") return "it";
        if (name == "Commentaires") return "fr";
        if (name == "Kommentare") return "de";
        return "en";
    }
}
