using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Microsoft.UI.Reactor.Wrappers.Generator;

public sealed partial class WrapperGenerator
{
    // Keep the single-controlled descriptor semantics. Composite wrappers use
    // public descriptor hooks and native attached storage, not RCW-keyed tables
    // or Reactor internals. State is lazy so constructing an element stays headless.
    private static void EmitControlledBindings(
        StringBuilder sb, string elementName, string controlFqn, ImmutableArray<PropInfo> props)
    {
        var events = props.SelectMany(p => p.ChangedEvents.Select((e, i) =>
                (Name: e, Delegate: p.ChangedDelegates[i], Prop: p)).Distinct())
            .GroupBy(e => e.Name).ToArray();
        const string comparer = "global::System.Collections.Generic.EqualityComparer";
        const string dp = "global::Microsoft.UI.Xaml.DependencyProperty";

        sb.AppendLine("    private sealed class __ControlledState");
        sb.AppendLine("    {");
        sb.AppendLine($"        internal static readonly {dp} Property = {dp}.RegisterAttached(");
        sb.AppendLine($"            \"ControlledState\", typeof(object), typeof(__ControlledState), new global::Microsoft.UI.Xaml.PropertyMetadata(null));");
        sb.AppendLine("        static __ControlledState() { }");
        sb.AppendLine("        internal int WriteDepth;");
        sb.AppendLine("        internal bool Subscribed;");
        foreach (var ev in events)
            sb.AppendLine($"        internal {ev.First().Delegate}? {ev.Key}Handler;");
        foreach (var p in props)
            sb.AppendLine($"        internal {p.ValueType} Last{p.Name} = default!;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    private static __ControlledState __GetControlledState({controlFqn} c)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (c.GetValue(__ControlledState.Property) is __ControlledState state) return state;");
        sb.AppendLine("        state = new __ControlledState();");
        sb.AppendLine("        __CaptureControlled(c, state);");
        sb.AppendLine("        c.SetValue(__ControlledState.Property, state);");
        sb.AppendLine("        return state;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    private static void __CaptureControlled({controlFqn} c, __ControlledState state)");
        sb.AppendLine("    {");
        foreach (var p in props)
            sb.AppendLine($"        state.Last{p.Name} = c.{p.ControlProp};");
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var p in props)
        {
            sb.AppendLine($"    private static void __Write{p.Name}({controlFqn} c, {elementName} e)");
            sb.AppendLine("    {");
            sb.AppendLine($"        if (!e.{p.Name}.HasValue || {comparer}<{p.ValueType}>.Default.Equals(c.{p.ControlProp}, e.{p.Name}.Value)) return;");
            sb.AppendLine("        var state = __GetControlledState(c);");
            foreach (var observed in props)
                sb.AppendLine($"        var __before{observed.Name} = c.{observed.ControlProp};");
            sb.AppendLine("        state.WriteDepth++;");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine($"            c.{p.ControlProp} = e.{p.Name}.Value;");
            sb.AppendLine("        }");
            sb.AppendLine("        finally");
            sb.AppendLine("        {");
            sb.AppendLine("            state.WriteDepth--;");
            // Only consume changes caused by this write. A sibling may already
            // carry a native edit whose deferred event has not arrived yet.
            foreach (var observed in props)
            {
                sb.AppendLine($"            var __after{observed.Name} = c.{observed.ControlProp};");
                sb.AppendLine($"            if (!{comparer}<{observed.ValueType}>.Default.Equals(__before{observed.Name}, __after{observed.Name}))");
                sb.AppendLine($"                state.Last{observed.Name} = __after{observed.Name};");
            }
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine($"    private static void __SubscribeControlled({controlFqn} c)");
        sb.AppendLine("    {");
        sb.AppendLine("        var state = __GetControlledState(c);");
        sb.AppendLine("        if (state.Subscribed) return;");
        sb.AppendLine("        __CaptureControlled(c, state);");
        sb.AppendLine($"        var owner = new global::System.WeakReference<{controlFqn}>(c);");
        foreach (var ev in events)
        {
            sb.AppendLine($"        state.{ev.Key}Handler = (_, _) =>");
            sb.AppendLine("        {");
            sb.AppendLine($"            if (owner.TryGetTarget(out var live)) __Controlled{ev.Key}Handler(live);");
            sb.AppendLine("        };");
            sb.AppendLine($"        c.{ev.Key} += state.{ev.Key}Handler;");
        }
        sb.AppendLine("        state.Subscribed = true;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    private static void __UnsubscribeControlled({controlFqn} c)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (c.GetValue(__ControlledState.Property) is not __ControlledState state) return;");
        sb.AppendLine("        if (state.Subscribed)");
        sb.AppendLine("        {");
        foreach (var ev in events)
            sb.AppendLine($"            c.{ev.Key} -= state.{ev.Key}Handler;");
        sb.AppendLine("        }");
        sb.AppendLine("        c.ClearValue(__ControlledState.Property);");
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var ev in events)
        {
            sb.AppendLine($"    private static void __Controlled{ev.Key}Handler({controlFqn} c)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (c.GetValue(__ControlledState.Property) is not __ControlledState state) return;");
            sb.AppendLine("        if (state.WriteDepth != 0) return;");
            // Snapshot every affected value before invoking any callback. This
            // also makes reentrant events observe the new baseline.
            foreach (var entry in ev)
            {
                var p = entry.Prop;
                sb.AppendLine($"        var __value{p.Name} = c.{p.ControlProp};");
                sb.AppendLine($"        var __changed{p.Name} = !{comparer}<{p.ValueType}>.Default.Equals(state.Last{p.Name}, __value{p.Name});");
                sb.AppendLine($"        state.Last{p.Name} = __value{p.Name};");
            }
            sb.AppendLine("        if (global::Microsoft.UI.Reactor.Core.ReactorBinding.ShouldSuppressEcho(c)) return;");
            sb.AppendLine($"        if (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(c) is not {elementName} live) return;");
            foreach (var entry in ev)
            {
                var p = entry.Prop;
                sb.AppendLine($"        if (__changed{p.Name}) live.On{p.Name}Changed?.Invoke(__value{p.Name});");
            }
            sb.AppendLine("    }");
            sb.AppendLine();
        }
    }

    // .NET's COM-aware WeakReference can recover a live native owner's RCW.
    // The existing attached payload still gates subscription by native identity;
    // neither the owner nor the current immutable element is strongly captured.
    private static void EmitEventOwner(StringBuilder sb, string controlFqn)
    {
        sb.AppendLine("    private sealed class __EventOwner");
        sb.AppendLine("    {");
        sb.AppendLine("        internal static readonly global::Microsoft.UI.Xaml.DependencyProperty Property =");
        sb.AppendLine("            global::Microsoft.UI.Xaml.DependencyProperty.RegisterAttached(");
        sb.AppendLine("                \"EventOwner\", typeof(object), typeof(__EventOwner), new global::Microsoft.UI.Xaml.PropertyMetadata(null));");
        sb.AppendLine("        static __EventOwner() { }");
        sb.AppendLine($"        internal readonly global::System.WeakReference<{controlFqn}> Reference;");
        sb.AppendLine("        internal bool Active;");
        sb.AppendLine($"        internal __EventOwner({controlFqn} c) => Reference = new global::System.WeakReference<{controlFqn}>(c);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    private static __EventOwner __GetEventOwner({controlFqn} c)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (c.GetValue(__EventOwner.Property) is __EventOwner owner) return owner;");
        sb.AppendLine("        owner = new __EventOwner(c);");
        sb.AppendLine("        c.SetValue(__EventOwner.Property, owner);");
        sb.AppendLine("        return owner;");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void EmitOwnerSubscription(
        StringBuilder sb, string controlFqn, global::System.Collections.Generic.IEnumerable<string> events, bool forwardArgs)
    {
        sb.AppendLine("            subscribe:   static (c, h) =>");
        sb.AppendLine("            {");
        sb.AppendLine($"                var owner = __GetEventOwner(({controlFqn})c);");
        foreach (var ev in events)
        {
            sb.AppendLine($"                (({controlFqn})c).{ev} += (_, args) =>");
            sb.AppendLine("                {");
            sb.AppendLine($"                    if (owner.Active && owner.Reference.TryGetTarget(out var live)) h(live{(forwardArgs ? ", args" : "")});");
            sb.AppendLine("                };");
        }
        sb.AppendLine("            },");
    }
}
