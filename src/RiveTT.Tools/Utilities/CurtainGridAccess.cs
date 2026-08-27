using System.Linq;
using Autodesk.Revit.DB;
using RiveTT.Core.Results;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Shared by get_curtain_grid_info, add_curtain_grid_line, and add_curtain_mullions so the
/// three cannot drift into three different readings of what a host's grid is.
/// </summary>
public static class CurtainGridAccess
{
    /// <summary>
    /// Curtain grids are read from the host, not looked up by id. A Wall has ONE grid
    /// (<c>CurtainGrid</c>); a CurtainSystem has one PER FACE (<c>CurtainGrids</c>, a
    /// CurtainGridSet that is null when the system carries none), so the singular property
    /// does not exist there. Only the first face is addressed here — a multi-face system
    /// needs a face selector, which none of these tools publish.
    /// </summary>
    public static CurtainGrid? GetCurtainGrid(Element? host)
    {
        return host switch
        {
            Wall wall => wall.CurtainGrid,
            CurtainSystem system => system.CurtainGrids?.Cast<CurtainGrid>().FirstOrDefault(),
            _ => null
        };
    }

    public static CortexResult<object> DescribeGrid(CurtainGrid grid)
    {
        var uLines = grid.GetUGridLineIds().Select(ToolHelpers.GetElementIdValue).ToList();
        var vLines = grid.GetVGridLineIds().Select(ToolHelpers.GetElementIdValue).ToList();
        var panelIds = grid.GetPanelIds().Select(ToolHelpers.GetElementIdValue).ToList();
        var mullionIds = grid.GetMullionIds().Select(ToolHelpers.GetElementIdValue).ToList();

        return CortexResult<object>.Ok(new
        {
            uGridLineIds = uLines,
            vGridLineIds = vLines,
            panelIds,
            mullionIds
        });
    }
}
