using Autodesk.Revit.DB;
using RevitCortex.Core.Discovery;
using System.Linq;

namespace RevitCortex.Plugin.Discovery;

public class DocumentAnalyzer : IDocumentAnalyzer
{
    public void Analyze(object document, DocumentCapabilities caps)
    {
        if (!(document is Document doc)) return;

        caps.Reset();

        caps.HasWorksets = doc.IsWorkshared;
        caps.HasPhases = doc.Phases.Size > 0;

        caps.HasDesignOptions = new FilteredElementCollector(doc)
            .OfClass(typeof(DesignOption))
            .GetElementCount() > 0;

        caps.HasLinkedModels = new FilteredElementCollector(doc)
            .OfClass(typeof(RevitLinkInstance))
            .GetElementCount() > 0;

        var elements = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .ToElements();

        foreach (var elem in elements)
        {
            var cat = elem.Category;
            if (cat != null)
            {
#if REVIT2025_OR_GREATER
                caps.PresentCategories.Add(cat.BuiltInCategory.ToString());
#else
                var bic = (BuiltInCategory)cat.Id.IntegerValue;
                caps.PresentCategories.Add(bic.ToString());
#endif
            }
        }

        if (caps.HasWorksets)
        {
            caps.EnableTool("get_worksets");
            caps.EnableTool("set_element_workset");
            caps.EnableTool("create_workset");
        }

        if (caps.HasPhases)
        {
            caps.EnableTool("get_phases");
            caps.EnableTool("get_phase_filter");
            caps.EnableTool("set_element_phase");
        }

        if (caps.HasDesignOptions)
        {
            caps.EnableTool("get_design_options");
        }

        if (caps.HasLinkedModels)
        {
            caps.EnableTool("get_linked_file_instances");
            caps.EnableTool("get_link_transform");
            caps.EnableTool("reload_linked_file_from");
            caps.EnableTool("pin_unpin_link_instance");
            caps.EnableTool("move_link_instance");
            caps.EnableTool("align_link_to_host");
            caps.EnableTool("highlight_linked_element");
            caps.EnableTool("show_cross_model_elements");
            caps.EnableTool("get_selected_linked_elements");
        }

        if (caps.PresentCategories.Contains("OST_Rooms"))
        {
            caps.EnableTool("get_room_openings");
            caps.EnableTool("create_room_finish_schedule");
            caps.EnableTool("create_door_schedule_by_room");
            caps.EnableTool("create_window_schedule_by_room");
        }

        if (caps.PresentCategories.Contains("OST_Walls") ||
            caps.PresentCategories.Contains("OST_Floors") ||
            caps.PresentCategories.Contains("OST_Roofs"))
        {
            caps.EnableTool("set_material_properties");
            caps.EnableTool("create_material_takeoff_schedule");
        }
    }
}
