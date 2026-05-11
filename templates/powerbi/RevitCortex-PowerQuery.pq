// ════════════════════════════════════════════════════════════════════════
// RevitCortex → Power BI starter query
//
// Reads all CSV files from a RevitCortex export folder, combines them,
// adds a drillthrough URL column for the revitcortex:// protocol handler.
//
// HOW TO USE:
//   1. Open Power BI Desktop
//   2. Home → Trasforma dati (Transform data) → Editor avanzato (Advanced Editor)
//   3. Paste this entire file
//   4. Change FolderPath below to your actual RevitCortex export folder
//   5. Fine (Done) → Chiudi e applica (Close & Apply)
// ════════════════════════════════════════════════════════════════════════

let
    // ┌─ CHANGE THIS to your actual folder path ─┐
    FolderPath = "C:\Users\luigi.dattilo\OneDrive - GPA Ingegneria Srl\RevitCortex\TestProject",
    // └──────────────────────────────────────────┘

    // 1. Read every CSV in the folder, ignore subfolders
    Source = Folder.Files(FolderPath),
    OnlyCsv = Table.SelectRows(Source, each [Extension] = ".csv"),

    // 2. Combine all CSVs (each one becomes its own batch with file name preserved)
    AddedSourceFile = Table.AddColumn(OnlyCsv, "SourceFile", each [Name], type text),
    KeepCols = Table.SelectColumns(AddedSourceFile, {"Content","SourceFile"}),
    Parsed = Table.AddColumn(KeepCols, "Data", each
        Csv.Document([Content], [Delimiter=",", Encoding=65001, QuoteStyle=QuoteStyle.Csv])),
    Promoted = Table.AddColumn(Parsed, "Promoted", each Table.PromoteHeaders([Data], [PromoteAllScalars=true])),
    Combined = Table.Combine(List.Transform(Promoted[Promoted], each _)),

    // 3. Type the ElementId column (long) so PBI can use it as relation key
    TypedElementId = Table.TransformColumnTypes(Combined,{{"ElementId", Int64.Type}}),

    // 4. Add a Drillthrough URL column. Uses the revitcortex:// protocol handler
    //    so a single click in PBI selects + zooms the element in Revit.
    AddDrillthrough = Table.AddColumn(TypedElementId, "RevitCortex Drillthrough",
        each "revitcortex://select?ids=" & Text.From([ElementId]), type text),

    // 5. Type Volume / Area as numbers. They may have unit suffixes ("12.34 m³")
    //    so we strip non-numeric characters before parsing.
    StripUnits = (txt as nullable text) =>
        if txt = null or txt = "" then null
        else try Number.FromText(Text.Select(txt, {"0".."9","-",".",","})) otherwise null,
    AddVolumeNum = Table.AddColumn(AddDrillthrough, "Volume_num", each StripUnits([Volume]), type number),
    AddAreaNum = Table.AddColumn(AddVolumeNum, "Area_num", each StripUnits([Area]), type number)

in
    AddAreaNum
