using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Autodesk.AutoCAD.DatabaseServices;

namespace PyAcad.Runtime.Events
{
    /// <summary>
    /// The only subscriptions to AutoCAD's own events. Application-level ones are taken
    /// once; document-level ones (commands, selection, layout, database) are taken per
    /// drawing as it arrives and released as it closes. Every handler just calls
    /// <see cref="AcadEvents.Notify"/>; the hub decides about debouncing and consumers.
    /// </summary>
    internal static class AcadEventSource
    {
        private static readonly Dictionary<Document, DocumentHandlers> PerDocument =
            new Dictionary<Document, DocumentHandlers>();
        private static bool _subscribed;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            var docs = Application.DocumentManager;
            docs.DocumentCreated += OnDocumentCreated;
            docs.DocumentActivated += OnDocumentActivated;
            docs.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
            Application.SystemVariableChanged += OnSystemVariableChanged;
            Application.BeginQuit += OnBeginQuit;
            foreach (Document doc in docs) Attach(doc);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            var docs = Application.DocumentManager;
            docs.DocumentCreated -= OnDocumentCreated;
            docs.DocumentActivated -= OnDocumentActivated;
            docs.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
            Application.SystemVariableChanged -= OnSystemVariableChanged;
            Application.BeginQuit -= OnBeginQuit;
            foreach (var pair in PerDocument) pair.Value.Detach(pair.Key);
            PerDocument.Clear();
        }

        private static void OnDocumentCreated(object sender, DocumentCollectionEventArgs e)
        {
            var doc = e.Document;
            if (doc == null) return;
            Attach(doc);
            AcadEvents.Notify(AcadEventMap.DocumentArrival(doc.IsNamedDrawing));
        }

        private static void OnDocumentActivated(object sender, DocumentCollectionEventArgs e) =>
            AcadEvents.Notify(AcadEvent.DocActivated);

        private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
        {
            var doc = e.Document;
            if (doc != null && PerDocument.TryGetValue(doc, out var handlers))
            {
                handlers.Detach(doc);
                PerDocument.Remove(doc);
            }
            AcadEvents.Notify(AcadEvent.DocClosing);
        }

        private static void OnSystemVariableChanged(object sender, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
        {
            if (!e.Changed) return;
            AcadEvents.Notify(AcadEventMap.SystemVariable(e.Name));
        }

        private static void OnBeginQuit(object sender, BeginQuitEventArgs e) =>
            AcadEvents.Notify(AcadEvent.AppQuit);

        private static void Attach(Document doc)
        {
            if (PerDocument.ContainsKey(doc)) return;
            var handlers = new DocumentHandlers();
            handlers.Attach(doc);
            PerDocument[doc] = handlers;
        }

        /// <summary>One drawing's subscriptions, kept so they can be removed by instance.</summary>
        private sealed class DocumentHandlers
        {
            private CommandEventHandler _willStart, _ended, _cancelled, _failed;
            private EventHandler _selection;
            private LayoutSwitchedEventHandler _layout;
            private DatabaseIOEventHandler _saved;
            private ObjectEventHandler _appended, _modified;
            private ObjectErasedEventHandler _erased;
            private Database _db;

            public void Attach(Document doc)
            {
                _willStart = (s, e) => AcadEvents.Notify(AcadEvent.CommandWillStart);
                _ended = (s, e) => AcadEvents.Notify(AcadEvent.CommandEnded);
                _cancelled = (s, e) => AcadEvents.Notify(AcadEvent.CommandCancelled);
                _failed = (s, e) => AcadEvents.Notify(AcadEvent.CommandFailed);
                _selection = (s, e) => AcadEvents.Notify(AcadEvent.SelectionChanged);
                _layout = (s, e) => AcadEvents.Notify(AcadEvent.LayoutSwitched);
                _saved = (s, e) => AcadEvents.Notify(AcadEvent.DocSaved);
                _appended = (s, e) => AcadEvents.NotifyObject(AcadEvent.ObjectAppended);
                _modified = (s, e) => AcadEvents.NotifyObject(AcadEvent.ObjectModified);
                _erased = (s, e) => AcadEvents.NotifyObject(AcadEvent.ObjectErased);

                doc.CommandWillStart += _willStart;
                doc.CommandEnded += _ended;
                doc.CommandCancelled += _cancelled;
                doc.CommandFailed += _failed;
                doc.ImpliedSelectionChanged += _selection;
                doc.LayoutSwitched += _layout;

                _db = doc.TryGetDatabase();
                if (_db == null) return;
                _db.SaveComplete += _saved;
                _db.ObjectAppended += _appended;
                _db.ObjectModified += _modified;
                _db.ObjectErased += _erased;
            }

            public void Detach(Document doc)
            {
                try
                {
                    doc.CommandWillStart -= _willStart;
                    doc.CommandEnded -= _ended;
                    doc.CommandCancelled -= _cancelled;
                    doc.CommandFailed -= _failed;
                    doc.ImpliedSelectionChanged -= _selection;
                    doc.LayoutSwitched -= _layout;
                    if (_db == null) return;
                    _db.SaveComplete -= _saved;
                    _db.ObjectAppended -= _appended;
                    _db.ObjectModified -= _modified;
                    _db.ObjectErased -= _erased;
                }
                catch (System.Exception ex)
                {
                    Log.Error("Could not detach document events", ex);
                }
            }
        }
    }
}
