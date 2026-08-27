using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Reads the grid lines, panels, and mullions of an EXISTING curtain wall/system's grid.
/// CurtainGrid is obtained from the host element (Wall.CurtainGrid, CurtainSystem.CurtainGrids),
/// not looked up by its own id — this tool, add_curtain_grid_line, and add_curtain_mullions
/// used to be three actions of one manage_curtain_grid tool; they were split because a read
/// and two structurally different geometric writes are not CRUD on one object.
/// </summary>
[ToolSafety(true, false)]
public class GetCurtainGridInfoTool : ICortexTool
{
    public string Name => "get_curtain_grid_info";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Reads an existing curtain wall/system grid: U/V grid line ids, panel ids, mullion ids. "
        + "hostElementId is the curtain wall or curtain system element.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var hostIdLong = input["hostElementId"]?.Value<long?>() ?? 0;
        if (hostIdLong <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "hostElementId is required");

        var host = doc.GetElement(ToolHelpers.ToElementId(hostIdLong));
        var grid = CurtainGridAccess.GetCurtainGrid(host);
        if (grid == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"hostElementId {hostIdLong} has no curtain grid (not a curtain wall/system/roof, or its type has no automatic grid)");

        return CurtainGridAccess.DescribeGrid(grid);
    }
}
