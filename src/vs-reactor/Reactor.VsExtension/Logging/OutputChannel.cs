#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.VsExtension.Package;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.UI.Reactor.VsExtension.Logging
{
    internal static class OutputChannel
    {
        private const int BufferLimit = 200;
        private static readonly object Gate = new object();
        private static readonly Queue<string> PendingLines = new Queue<string>();

        private static IAsyncServiceProvider? _serviceProvider;
        private static JoinableTaskFactory? _jtf;
        private static IVsOutputWindowPane? _pane;
        private static Func<string, Task>? _testSink;

        public static void Initialize(IAsyncServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException(nameof(serviceProvider));
            }

            lock (Gate)
            {
                _serviceProvider = serviceProvider;
                _jtf = serviceProvider is AsyncPackage package ? package.JoinableTaskFactory : ThreadHelper.JoinableTaskFactory;
                _pane = null;
                _testSink = null;
            }
        }

        public static async Task WriteLineAsync(string message)
        {
            var line = FormatLine(message);
            Func<string, Task>? sink;
            JoinableTaskFactory? jtf;
            lock (Gate)
            {
                sink = _testSink;
                jtf = _jtf;
                if (sink == null && (_serviceProvider == null || jtf == null))
                {
                    EnqueueLocked(line);
                    return;
                }
            }

            if (sink != null)
            {
                await FlushToTestSinkAsync(sink, line).ConfigureAwait(false);
                return;
            }

            await jtf!.SwitchToMainThreadAsync();
            await EnsurePaneAsync().ConfigureAwait(true);
            FlushToPane(line);
        }

        internal static void ResetForTest()
        {
            lock (Gate)
            {
                PendingLines.Clear();
                _serviceProvider = null;
                _jtf = null;
                _pane = null;
                _testSink = null;
            }
        }

        internal static void InitializeForTest(Func<string, Task> sink)
        {
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            lock (Gate)
            {
                _testSink = sink;
            }
        }

        private static string FormatLine(string message)
        {
            return "[" + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "] " + (message ?? string.Empty);
        }

        private static void EnqueueLocked(string line)
        {
            if (PendingLines.Count >= BufferLimit)
            {
                PendingLines.Dequeue();
            }

            PendingLines.Enqueue(line);
        }

        private static async Task FlushToTestSinkAsync(Func<string, Task> sink, string currentLine)
        {
            List<string> lines;
            lock (Gate)
            {
                lines = new List<string>(PendingLines);
                PendingLines.Clear();
                lines.Add(currentLine);
            }

            foreach (var line in lines)
            {
                await sink(line).ConfigureAwait(false);
            }
        }

        private static async Task EnsurePaneAsync()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_pane != null)
            {
                return;
            }

            IAsyncServiceProvider? serviceProvider;
            lock (Gate)
            {
                serviceProvider = _serviceProvider;
            }

            if (serviceProvider == null)
            {
                return;
            }

            var outputWindow = await serviceProvider.GetServiceAsync(typeof(SVsOutputWindow)).ConfigureAwait(true) as IVsOutputWindow;
            if (outputWindow == null)
            {
                return;
            }

            var paneGuid = PackageGuids.OutputPaneGuid;
            ErrorHandler.ThrowOnFailure(outputWindow.CreatePane(ref paneGuid, "Reactor Preview", fInitVisible: 1, fClearWithSolution: 0));
            if (ErrorHandler.Succeeded(outputWindow.GetPane(ref paneGuid, out var pane)))
            {
                _pane = pane;
            }
        }

        private static void FlushToPane(string currentLine)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_pane == null)
            {
                lock (Gate)
                {
                    EnqueueLocked(currentLine);
                }

                return;
            }

            List<string> lines;
            lock (Gate)
            {
                lines = new List<string>(PendingLines);
                PendingLines.Clear();
                lines.Add(currentLine);
            }

            foreach (var line in lines)
            {
                ErrorHandler.ThrowOnFailure(_pane.OutputString(line + Environment.NewLine));
            }
        }
    }
}
