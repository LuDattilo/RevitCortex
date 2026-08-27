namespace RiveTT.Core.Discovery;

public interface IDocumentAnalyzer
{
    /// <summary>
    /// Analyze a document and populate capabilities.
    /// Implementation lives in RiveTT.Plugin (has Revit dependency).
    /// </summary>
    void Analyze(object document, DocumentCapabilities capabilities);
}
