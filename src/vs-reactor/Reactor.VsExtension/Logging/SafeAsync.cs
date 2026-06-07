#nullable enable

using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.UI.Reactor.VsExtension.Logging
{
    internal static class SafeAsync
    {
        public static void Run(JoinableTaskFactory jtf, Func<Task> action, string operationName)
        {
            if (jtf == null)
            {
                throw new ArgumentNullException(nameof(jtf));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            _ = jtf.RunAsync(async delegate
            {
                try
                {
                    await action().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    _ = jtf.RunAsync(() => LogExceptionAsync(operationName, ex));
                }
            });
        }

        public static void Run(Action action, string operationName)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                _ = LogExceptionAsync(operationName, ex);
            }
        }

        private static async Task LogExceptionAsync(string operationName, Exception ex)
        {
            try
            {
                var prefix = string.IsNullOrWhiteSpace(operationName) ? "Operation" : operationName;
                await OutputChannel.WriteLineAsync("[" + prefix + "] " + ex.GetType().Name + ": " + ex.Message).ConfigureAwait(false);
                await OutputChannel.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
}
