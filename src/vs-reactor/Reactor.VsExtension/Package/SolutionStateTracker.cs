#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using EnvDTE;
using Microsoft.UI.Reactor.VsExtension.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.UI.Reactor.VsExtension.Package
{
    internal sealed class SolutionStateTracker : IVsSolutionEvents, IDisposable
    {
        private readonly IVsSolution _solution;
        private readonly DTE _dte;
        private readonly JoinableTaskFactory _jtf;
        private readonly HashSet<string> _loadedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private uint _cookie;
        private bool _disposed;

        public SolutionStateTracker(IVsSolution solution, DTE dte, JoinableTaskFactory jtf)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _jtf = jtf ?? throw new ArgumentNullException(nameof(jtf));

            ThreadHelper.ThrowIfNotOnUIThread();
            ErrorHandler.ThrowOnFailure(_solution.AdviseSolutionEvents(this, out _cookie));
            RefreshFromSolution();
        }

        public event EventHandler? SolutionClosing;

        public event EventHandler? SolutionReadyChanged;

        public event EventHandler<string>? ProjectUnloading;

        public bool IsSolutionReady { get; private set; }

        public bool CanPreviewProject(string csprojPath, out string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RefreshFromSolution();

            if (!IsSolutionReady)
            {
                message = "The solution is still loading. Wait for project load to finish, then preview the active file again.";
                return false;
            }

            var normalized = Normalize(csprojPath);
            if (_loadedProjects.Contains(normalized))
            {
                message = string.Empty;
                return true;
            }

            message = "The project is not currently loaded in the Visual Studio solution. Reload the project or use a file from a loaded solution project.";
            return false;
        }

        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            AddProject(pHierarchy);
            RaiseReadyChangedIfNeeded();
            return VSConstants.S_OK;
        }

        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel) => VSConstants.S_OK;

        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RemoveProjectAndNotify(pHierarchy);
            return VSConstants.S_OK;
        }

        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            AddProject(pRealHierarchy);
            RaiseReadyChangedIfNeeded();
            return VSConstants.S_OK;
        }

        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel) => VSConstants.S_OK;

        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RemoveProjectAndNotify(pRealHierarchy);
            return VSConstants.S_OK;
        }

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RefreshFromSolution(forceReady: true);
            SolutionReadyChanged?.Invoke(this, EventArgs.Empty);
            return VSConstants.S_OK;
        }

        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel) => VSConstants.S_OK;

        public int OnBeforeCloseSolution(object pUnkReserved)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IsSolutionReady = false;
            _loadedProjects.Clear();
            SolutionClosing?.Invoke(this, EventArgs.Empty);
            SolutionReadyChanged?.Invoke(this, EventArgs.Empty);
            return VSConstants.S_OK;
        }

        public int OnAfterCloseSolution(object pUnkReserved)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IsSolutionReady = false;
            _loadedProjects.Clear();
            SolutionReadyChanged?.Invoke(this, EventArgs.Empty);
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
                    if (_cookie != 0)
                    {
                        ErrorHandler.ThrowOnFailure(_solution.UnadviseSolutionEvents(_cookie));
                        _cookie = 0;
                    }
                });
            }
            catch (Exception ex)
            {
                SafeAsync.Run(() => OutputChannel.WriteLineAsync("[SolutionStateTracker.Dispose] " + ex.Message).GetAwaiter().GetResult(), "SolutionStateTracker.Dispose.Log");
            }
        }

        private void RefreshFromSolution(bool forceReady = false)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _loadedProjects.Clear();

            var solution = _dte.Solution;
            if (solution == null || !solution.IsOpen)
            {
                IsSolutionReady = false;
                return;
            }

            foreach (Project project in solution.Projects)
            {
                AddProject(project);
            }

            IsSolutionReady = forceReady || _loadedProjects.Count > 0;
        }

        private void RaiseReadyChangedIfNeeded()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var wasReady = IsSolutionReady;
            RefreshFromSolution();
            if (wasReady != IsSolutionReady)
            {
                SolutionReadyChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void AddProject(IVsHierarchy hierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (hierarchy == null)
            {
                return;
            }

            if (ErrorHandler.Succeeded(hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_ExtObject, out var extObject))
                && extObject is Project project)
            {
                AddProject(project);
            }
        }

        private void AddProject(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (!string.IsNullOrWhiteSpace(project.FullName) && project.FullName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    _loadedProjects.Add(Normalize(project.FullName));
                }

                if (project.ProjectItems == null)
                {
                    return;
                }

                foreach (ProjectItem item in project.ProjectItems)
                {
                    if (item.SubProject is { } subProject)
                    {
                        AddProject(subProject);
                    }
                }
            }
            catch
            {
                // Some unloaded/virtual projects throw from DTE properties. Solution events
                // will refresh the concrete project set once those projects are available.
            }
        }

        private void RemoveProjectAndNotify(IVsHierarchy hierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var path = GetProjectPath(hierarchy);
            if (path == null)
            {
                return;
            }

            var normalized = Normalize(path);
            _loadedProjects.Remove(normalized);
            ProjectUnloading?.Invoke(this, normalized);
        }

        private static string? GetProjectPath(IVsHierarchy hierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (hierarchy != null
                && ErrorHandler.Succeeded(hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_ExtObject, out var extObject))
                && extObject is Project project
                && !string.IsNullOrWhiteSpace(project.FullName))
            {
                return project.FullName;
            }

            return null;
        }

        private static string Normalize(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
