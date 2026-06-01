using RevitCortex.Tools.Elements;
using Xunit;

namespace RevitCortex.Tests.Elements;

/// <summary>
/// Unit tests for the pure cross-section classifier behind get_element_solid_geometry.
/// ClassifyShape touches no Revit types, so it loads and runs without a Revit host.
/// The "rectangular" regression these protect against: an extruded T/L/I profile is
/// all-planar, so an earlier face-count rule (planar>=5 => rectangular) mislabeled a
/// T-beam and an L-beam as rectangular. Real measured caps: T-beam cap = 8 vertices /
/// fill 0.714; L-beam (18LB48) cap = 6 vertices / fill 0.807.
/// </summary>
public class SectionShapeClassifierTests
{
    [Fact]
    public void Box_FourVertexFullFill_IsRectangular()
    {
        // A rectangular RC column/beam: 6 planar faces, cap is a filled 4-gon.
        Assert.Equal("rectangular",
            GetElementSolidGeometryTool.ClassifyShape(planar: 6, cylindrical: 0, other: 0, capVertexCount: 4, fillRatio: 1.0));
    }

    [Fact]
    public void Box_FourVertex_NoFillRatio_IsRectangular()
    {
        // If the fill ratio can't be computed, a 4-vertex cap still reads as a box.
        Assert.Equal("rectangular",
            GetElementSolidGeometryTool.ClassifyShape(6, 0, 0, capVertexCount: 4, fillRatio: null));
    }

    [Fact]
    public void TShape_EightVertices_IsNonRectangular()
    {
        // Measured live: "6 x 24 T-Shape" — 10 planar faces, cap loop 8 verts, fill 0.714.
        Assert.Equal("non_rectangular_polygonal",
            GetElementSolidGeometryTool.ClassifyShape(planar: 10, cylindrical: 0, other: 0, capVertexCount: 8, fillRatio: 0.714));
    }

    [Fact]
    public void LShape_SixVertices_IsNonRectangular()
    {
        // Measured live: "18LB48" Precast-L Shaped Beam — 8 planar faces, cap 6 verts, fill 0.807.
        Assert.Equal("non_rectangular_polygonal",
            GetElementSolidGeometryTool.ClassifyShape(planar: 8, cylindrical: 0, other: 0, capVertexCount: 6, fillRatio: 0.807));
    }

    [Fact]
    public void FourVertexCap_LowFill_IsNonRectangular()
    {
        // A 4-vertex cap that fills <95% of its 2D bbox is a parallelogram/trapezoid, not a box.
        Assert.Equal("non_rectangular_polygonal",
            GetElementSolidGeometryTool.ClassifyShape(6, 0, 0, capVertexCount: 4, fillRatio: 0.62));
    }

    [Fact]
    public void Cylinder_WithCurvedFace_IsCircular()
    {
        // Round pile: one cylindrical lateral face + two planar caps.
        Assert.Equal("circular",
            GetElementSolidGeometryTool.ClassifyShape(planar: 2, cylindrical: 1, other: 0, capVertexCount: 0, fillRatio: null));
    }

    [Fact]
    public void TaperedPile_WithRuledFace_IsCircularOrTapered()
    {
        Assert.Equal("circular_or_tapered",
            GetElementSolidGeometryTool.ClassifyShape(planar: 2, cylindrical: 0, other: 1, capVertexCount: 0, fillRatio: null));
    }

    [Fact]
    public void NoCapVertices_AllPlanar_IsComplex()
    {
        // Degenerate: planar solid but cap loop couldn't be read.
        Assert.Equal("complex",
            GetElementSolidGeometryTool.ClassifyShape(planar: 6, cylindrical: 0, other: 0, capVertexCount: 0, fillRatio: null));
    }

    [Fact]
    public void CurvedFace_DoesNotOverrideWhenManyPlanar()
    {
        // A mostly-planar solid with a single stray curved face and a polygonal cap is still
        // treated as a polygonal section (the circular branch needs planar<=3).
        Assert.Equal("non_rectangular_polygonal",
            GetElementSolidGeometryTool.ClassifyShape(planar: 10, cylindrical: 1, other: 0, capVertexCount: 8, fillRatio: 0.7));
    }
}
