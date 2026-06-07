#nullable enable
#pragma warning disable VSTHRD010 // EditorTracker is constructed and disposed through the package JTF on the UI thread.

using System;
using EnvDTE;
using Microsoft.UI.Reactor.VsExtension.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.UI.Reactor.VsExtension.UI
{
    internal sealed class EditorTracker : IVsRunningDocTableEvents, IDisposable
    {
        private readonly IVsRunningDocumentTable _rdt;
        private readonly DTE _dte;
        private readonly JoinableTaskFactory _jtf;
        private readonly Func<string?> _getActiveDocumentPath;
        private readonly EnvDTE.WindowEvents _windowEvents;
        private readonly uint _cookie;
        private string? _lastRaisedPath;
        private bool _disposed;

        public EditorTracker(IVsRunningDocumentTable rdt, DTE dte, JoinableTaskFactory jtf)
            : this(rdt, dte, jtf, () => GetActiveDocumentPathOnMainThread(dte))
        {
        }

        internal EditorTracker(IVsRunningDocumentTable rdt, DTE dte, JoinableTaskFactory jtf, Func<string?> getActiveDocumentPath)
        {
            _rdt = rdt ?? throw new ArgumentNullException(nameof(rdt));
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _jtf = jtf ?? throw new ArgumentNullException(nameof(jtf));
            _getActiveDocumentPath = getActiveDocumentPath ?? throw new ArgumentNullException(nameof(getActiveDocumentPath));
            ThreadHelper.ThrowIfNotOnUIThread();
            ErrorHandler.ThrowOnFailure(_rdt.AdviseRunningDocTableEvents(this, out _cookie));
            _windowEvents = _dte.Events.WindowEvents;
            _windowEvents.WindowActivated += OnWindowActivated;
        }

        public event EventHandler<string?>? ActiveDocumentChanged;

        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            RaiseActiveDocumentChanged("RDT.FirstLock");
            return VSConstants.S_OK;
        }

        public int OnAfterSave(uint docCookie)
        {
            RaiseActiveDocumentChanged("RDT.Save");
            return VSConstants.S_OK;
        }

        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs)
        {
            RaiseActiveDocumentChanged("RDT.AttributeChange");
            return VSConstants.S_OK;
        }

        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            return VSConstants.S_OK;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _jtf.Run(async () =>
                {
                    await _jtf.SwitchToMainThreadAsync();
                    _windowEvents.WindowActivated -= OnWindowActivated;
                    if (_cookie != 0)
                    {
                        ErrorHandler.ThrowOnFailure(_rdt.UnadviseRunningDocTableEvents(_cookie));
                    }
                });
            }
            catch (Exception ex)
            {
                SafeAsync.Run(() => OutputChannel.WriteLineAsync("[EditorTracker.Dispose] " + ex.Message).GetAwaiter().GetResult(), "EditorTracker.Dispose.Log");
            }
        }

        private void OnWindowActivated(Window gotFocus, Window lostFocus)
        {
            RaiseActiveDocumentChanged("WindowActivated");
        }

        private static string? GetActiveDocumentPathOnMainThread(DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return dte.ActiveDocument?.FullName;
        }

        private void RaiseActiveDocumentChanged(string reason)
        {
            if (_disposed)
            {
                return;
            }

            SafeAsync.Run(_jtf, async () =>
            {
                await _jtf.SwitchToMainThreadAsync();
                RaiseIfChanged(_getActiveDocumentPath());
            }, "EditorTracker." + reason);
        }

        private void RaiseIfChanged(string? path)
        {
            if (string.Equals(_lastRaisedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastRaisedPath = path;
            ActiveDocumentChanged?.Invoke(this, path);
        }
    }
}
