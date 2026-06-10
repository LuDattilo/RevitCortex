using RevitCortex.Tools.Elements;
using Xunit;

namespace RevitCortex.Tests.Elements;

/// <summary>
/// Round-trip contract for the import_from_powerbi CSV reader.
/// export_elements_data writes ';'-delimited CSV while push_to_powerbi writes
/// ','-delimited CSV — the parser must accept both (sniffing the header) or an
/// explicit delimiter, otherwise a re-import silently collapses every field
/// into a single column.
/// </summary>
public class CsvParsingTests
{
    // ── Delimiter sniffing ─────────────────────────────────────────────────

    [Fact]
    public void Sniff_SemicolonHeader_PicksSemicolon()
    {
        // Shape produced by ExportElementsDataTool.BuildCsv
        Assert.Equal(';', CsvParsing.SniffDelimiter("ElementId;Category;Name\n1;Walls;W1\n"));
    }

    [Fact]
    public void Sniff_CommaHeader_PicksComma()
    {
        // Shape produced by PushToPowerBiTool
        Assert.Equal(',', CsvParsing.SniffDelimiter("ElementId,Category,Name\n1,Walls,W1\n"));
    }

    [Fact]
    public void Sniff_QuotedCommaInsideSemicolonHeader_StillPicksSemicolon()
    {
        // A header cell legitimately containing a comma must not fool the sniffer.
        Assert.Equal(';', CsvParsing.SniffDelimiter("\"Width, nominal\";ElementId;Name\n"));
    }

    [Fact]
    public void Sniff_SingleColumnNoDelimiter_DefaultsToComma()
    {
        Assert.Equal(',', CsvParsing.SniffDelimiter("ElementId\n1\n2\n"));
    }

    [Fact]
    public void Sniff_OnlyLooksAtFirstLine()
    {
        // Semicolons in data rows must not override a comma-delimited header.
        Assert.Equal(',', CsvParsing.SniffDelimiter("ElementId,Name\n1,a;b;c;d\n"));
    }

    // ── Parsing: semicolon CSV (export_elements_data shape) ───────────────

    [Fact]
    public void Parse_SemicolonCsv_SplitsAllColumns()
    {
        var (headers, rows) = CsvParsing.Parse("ElementId;Category;Name\n123;Muri;Parete A\n456;Porte;Porta B\n");

        Assert.Equal(new[] { "ElementId", "Category", "Name" }, headers);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "123", "Muri", "Parete A" }, rows[0]);
        Assert.Equal(new[] { "456", "Porte", "Porta B" }, rows[1]);
    }

    [Fact]
    public void Parse_SemicolonCsv_UnquotedCommaStaysInsideField()
    {
        // ExportElementsDataTool.EscapeCsv does NOT quote values containing ','
        // (only ';', '"', '\n') — a comma-parser would split here.
        var (headers, rows) = CsvParsing.Parse("ElementId;Comments\n1;Rev A, da verificare\n");

        Assert.Equal(2, headers.Count);
        Assert.Equal("Rev A, da verificare", rows[0][1]);
    }

    [Fact]
    public void Parse_SemicolonCsv_QuotedFieldWithDelimiterQuotesAndNewline()
    {
        var (_, rows) = CsvParsing.Parse("ElementId;Name\n1;\"Basic Wall; \"\"Generic\"\"\nline2\"\n");

        Assert.Single(rows);
        Assert.Equal("Basic Wall; \"Generic\"\nline2", rows[0][1]);
    }

    // ── Parsing: comma CSV (push_to_powerbi shape, regression guard) ──────

    [Fact]
    public void Parse_CommaCsv_StillWorks()
    {
        var (headers, rows) = CsvParsing.Parse("ElementId,Category,Name\n123,Walls,\"Wall, Basic\"\n");

        Assert.Equal(new[] { "ElementId", "Category", "Name" }, headers);
        Assert.Equal(new[] { "123", "Walls", "Wall, Basic" }, rows[0]);
    }

    // ── Explicit delimiter override ────────────────────────────────────────

    [Fact]
    public void Parse_ExplicitDelimiter_OverridesSniffing()
    {
        // Header contains more semicolons than commas, but the caller knows better.
        var (headers, rows) = CsvParsing.Parse("a;b,c;d\n1;2,3;4\n", ',');

        Assert.Equal(new[] { "a;b", "c;d" }, headers);
        Assert.Equal(new[] { "1;2", "3;4" }, rows[0]);
    }

    // ── Row shaping (behavior preserved from the original reader) ─────────

    [Fact]
    public void Parse_ShortRowsArePaddedToHeaderWidth()
    {
        var (_, rows) = CsvParsing.Parse("a;b;c\n1;2\n");

        Assert.Equal(3, rows[0].Length);
        Assert.Equal("", rows[0][2]);
    }

    [Fact]
    public void Parse_BlankLinesAreSkipped()
    {
        var (_, rows) = CsvParsing.Parse("a;b\n1;2\n\n3;4\n");

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmpty()
    {
        var (headers, rows) = CsvParsing.Parse("");

        Assert.Empty(headers);
        Assert.Empty(rows);
    }

    [Fact]
    public void Parse_LastLineWithoutTrailingNewline_IsKept()
    {
        var (_, rows) = CsvParsing.Parse("a;b\n1;2");

        Assert.Single(rows);
        Assert.Equal(new[] { "1", "2" }, rows[0]);
    }
}
