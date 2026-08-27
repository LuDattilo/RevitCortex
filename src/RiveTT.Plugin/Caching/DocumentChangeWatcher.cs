using System;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB.Events;
using RiveTT.Core.Caching;
using RiveTT.Core.Session;

namespace RiveTT.Plugin.Caching;

/// <summary>
/// Subscribes to Revit document events and forwards them to a
/// <see cref="CacheInvalidator"/>. Tests cover the invalidation logic via
/// <c>CacheInvalidator</c> directly; this class is just the Revit glue.
/// </summary>
public class DocumentChangeWatcher : IDisposable
{
    private readonly CacheInvalidator _invalidator;
    private ControlledApplication? _attachedTo;
    private bool _disposed;

    public DocumentChangeWatcher(CortexSession session)
    {
        _invalidator = new CacheInvalidator(session);
    }

    /// <summary>
    /// Subscribe to Revit document events. Idempotent.
    /// </summary>
    public void Attach(ControlledApplication application)
    {
        if (_attachedTo != null) return;
        application.DocumentChanged += OnRevitDocumentChanged;
        application.DocumentSaved += OnRevitDocumentSaved;
        // Save As does NOT raise DocumentSaved. Without this subscription every
        // cached read kept describing the file the project was saved *from*.
        application.DocumentSavedAs += OnRevitDocumentSavedAs;
        application.DocumentSynchronizedWithCentral += OnRevitDocumentSynchronized;
        _attachedTo = application;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var app = _attachedTo;
        _attachedTo = null;
        if (app == null) return;

        try
        {
            app.DocumentChanged -= OnRevitDocumentChanged;
            app.DocumentSaved -= OnRevitDocumentSaved;
            app.DocumentSavedAs -= OnRevitDocumentSavedAs;
            app.DocumentSynchronizedWithCentral -= OnRevitDocumentSynchronized;
        }
        catch
        {
            // Detach is best-effort — Revit may have already torn down the app.
        }
    }

    private void OnRevitDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        try { _invalidator.OnDocumentChanged(); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RiveTT] Cache invalidation failed on DocumentChanged: {ex.Message}");
        }
    }

    private void OnRevitDocumentSaved(object? sender, DocumentSavedEventArgs e)
    {
        try { _invalidator.OnDocumentSaved(); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RiveTT] Cache invalidation failed on DocumentSaved: {ex.Message}");
        }
    }

    private void OnRevitDocumentSavedAs(object? sender, DocumentSavedAsEventArgs e)
    {
        try { _invalidator.OnActiveDocumentReplaced(); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RiveTT] Cache invalidation failed on DocumentSavedAs: {ex.Message}");
        }
    }

    private void OnRevitDocumentSynchronized(object? sender, DocumentSynchronizedWithCentralEventArgs e)
    {
        try { _invalidator.OnDocumentSynchronized(); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RiveTT] Cache invalidation failed on DocumentSynchronized: {ex.Message}");
        }
    }
}
