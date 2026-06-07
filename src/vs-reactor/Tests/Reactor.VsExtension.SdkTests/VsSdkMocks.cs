#nullable enable

using System;
using System.Reflection;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

namespace Reactor.VsExtension.SdkTests
{
    public class VsSdkMocks : DispatchProxy
    {
        private static readonly IVsOutputWindowPane OutputPane = Create<IVsOutputWindowPane>();
        private static readonly EnvDTE.WindowEvents WindowEvents = Create<EnvDTE.WindowEvents>();
        private static readonly Events Events = Create<Events>();

        public static T Create<T>() where T : class => DispatchProxy.Create<T, VsSdkMocks>();

        public static IVsOutputWindow CreateOutputWindow() => Create<IVsOutputWindow>();

        public static IVsRunningDocumentTable CreateRunningDocumentTable() => Create<IVsRunningDocumentTable>();

        public static DTE CreateDte() => Create<DTE>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null)
            {
                return null;
            }

            args ??= Array.Empty<object?>();
            switch (targetMethod.Name)
            {
                case nameof(IVsOutputWindow.GetPane):
                    args[1] = OutputPane;
                    return VSConstants.S_OK;
                case nameof(IVsRunningDocumentTable.AdviseRunningDocTableEvents):
                    args[1] = 1u;
                    return VSConstants.S_OK;
                case "get_Events":
                    return Events;
                case "get_WindowEvents":
                    return WindowEvents;
            }

            for (var i = 0; i < args.Length; i++)
            {
                var parameter = targetMethod.GetParameters()[i];
                if (parameter.IsOut || parameter.ParameterType.IsByRef)
                {
                    args[i] = GetDefault(parameter.ParameterType.GetElementType() ?? parameter.ParameterType);
                }
            }

            return GetDefault(targetMethod.ReturnType);
        }

        private static object? GetDefault(Type type)
        {
            if (type == typeof(void))
            {
                return null;
            }

            if (type == typeof(int))
            {
                return VSConstants.S_OK;
            }

            if (type == typeof(uint))
            {
                return 0u;
            }

            if (type == typeof(bool))
            {
                return false;
            }

            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
