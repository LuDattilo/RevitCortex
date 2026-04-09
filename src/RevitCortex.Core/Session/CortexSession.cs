using RevitCortex.Core.Discovery;

namespace RevitCortex.Core.Session;

/// <summary>
/// Facade passed to every tool. Provides access to shared state,
/// document capabilities, and detected locale. Does NOT hold a
/// direct Revit Document reference — that lives in the Plugin layer.
/// Core has no Revit dependency.
/// </summary>
public class CortexSession
{
    public ISessionStore Store { get; }
    public DocumentCapabilities Capabilities { get; private set; }
    public string DetectedLocale { get; private set; }

    public CortexSession(ISessionStore store)
    {
        Store = store;
        Capabilities = new DocumentCapabilities();
        DetectedLocale = "en";
    }

    public void Reinitialize(DocumentCapabilities capabilities, string locale)
    {
        Store.Clear();
        Capabilities = capabilities;
        DetectedLocale = locale;
    }
}
