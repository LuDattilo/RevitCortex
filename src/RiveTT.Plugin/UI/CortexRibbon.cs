using System;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace RiveTT.Plugin.UI;

/// <summary>
/// The RiveTT panel in the Add-Ins tab. It exists for two reasons that both
/// come from the connector being invisible until now: nothing in Revit said the
/// pipe was live, and nothing let a human take write access away from a
/// connected agent.
///
/// It is a display and a switch, never a start button — the pipe starts with
/// Revit whatever this panel does, and a failure to build the panel must not
/// stop it.
/// </summary>
internal static class CortexRibbon
{
    private const string PanelName = "RiveTT";

    public static void Build(UIControlledApplication application)
    {
        var panel = application.CreateRibbonPanel(Tab.AddIns, PanelName);
        var assemblyPath = typeof(CortexRibbon).Assembly.Location;

        // A radio group, not two independent buttons: the two modes are exclusive
        // and the ribbon then shows which one is current without us tracking it.
        var group = (RadioButtonGroup)panel.AddItem(
            new RadioButtonGroupData("RiveTT_Mode"));

        var locked = group.AddItem(Button(new ToggleButtonData(
            "RiveTT_Lock", "Lecture seule", assemblyPath,
            typeof(LockWritesCommand).FullName), "lock"));
        locked.ToolTip = "Refuser toute écriture dans le modèle";
        locked.LongDescription =
            "Les outils de lecture continuent de répondre. Tout outil susceptible de " +
            "modifier le modèle est refusé (PermissionDenied) sans être exécuté. " +
            "C'est l'état au démarrage de Revit.";

        var writable = group.AddItem(Button(new ToggleButtonData(
            "RiveTT_Write", "Écriture", assemblyPath,
            typeof(AllowWritesCommand).FullName), "unlock"));
        writable.ToolTip = "Autoriser le connecteur à modifier le modèle";
        writable.LongDescription =
            "Les outils d'écriture deviennent exécutables. Chaque appel reste " +
            "transactionnel et inscrit dans le journal d'audit. Aucun outil MCP ne " +
            "peut lui-même passer dans cet état : seul ce bouton le fait.";

        // Read-only is the startup state, so the group must open on that button.
        group.Current = locked;

        var status = (PushButton)panel.AddItem(Button(new PushButtonData(
            "RiveTT_Status", "État", assemblyPath,
            typeof(ShowStatusCommand).FullName), "status"));
        status.ToolTip = "État du connecteur RiveTT";
        status.LongDescription =
            "Version, état du canal nommé, mode d'écriture, document actif, " +
            "nombre d'outils publiés et chemin du journal d'audit.";
    }

    /// <summary>
    /// Icons and availability are set on the button DATA, before the item exists:
    /// AvailabilityClassName only lives there, and Revit greys out an external
    /// command while no document is open — which is exactly when a session
    /// begins and the write state matters most.
    /// </summary>
    private static T Button<T>(T data, string iconBaseName) where T : PushButtonData
    {
        data.AvailabilityClassName = typeof(AlwaysAvailable).FullName;

        var large = LoadIcon($"{iconBaseName}-32.png");
        var small = LoadIcon($"{iconBaseName}-16.png");
        if (large != null) data.LargeImage = large;
        if (small != null) data.Image = small;
        return data;
    }

    /// <summary>
    /// Icons travel as embedded resources, so there is no file to lose next to the
    /// DLL and no pack:// URI to register inside Revit's load context. The
    /// resource name is looked up by suffix rather than hard-coded: the assembly
    /// name (RiveTT.Plugin) and the root namespace (RiveTT.Plugin) differ
    /// here, which is exactly how a hard-coded name ends up silently iconless.
    /// </summary>
    private static ImageSource? LoadIcon(string fileName)
    {
        try
        {
            var assembly = typeof(CortexRibbon).Assembly;
            var resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
            if (resource == null)
            {
                System.Diagnostics.Trace.WriteLine($"[RiveTT] Ribbon icon not embedded: {fileName}");
                return null;
            }

            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream == null) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            // OnLoad: decode now, because the stream is disposed on leaving.
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RiveTT] Ribbon icon {fileName} failed to load: {exception.Message}");
            return null;
        }
    }
}
