using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Microsoft.UI.Reactor.Wrappers.Generator;

public sealed partial class WrapperGenerator
{
    // Keep the single-controlled fast path unchanged. Composite wrappers use
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
            sb.AppendLine("        state.WriteDepth++;");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine($"            c.{p.ControlProp} = e.{p.Name}.Value;");
            sb.AppendLine("        }");
            sb.AppendLine("        finally");
            sb.AppendLine("        {");
            sb.AppendLine("            state.WriteDepth--;");
            // Read back every endpoint: a setter may coerce itself or siblings,
            // raise multiple events, defer its echo, or raise no event at all.
            sb.AppendLine("            __CaptureControlled(c, state);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine($"    private static void __SubscribeControlled({controlFqn} c)");
        sb.AppendLine("    {");
        sb.AppendLine("        var state = __GetControlledState(c);");
        sb.AppendLine("        if (state.Subscribed) return;");
        sb.AppendLine("        __CaptureControlled(c, state);");
        foreach (var ev in events)
            sb.AppendLine($"        c.{ev.Key} += __Controlled{ev.Key};");
        sb.AppendLine("        state.Subscribed = true;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    private static void __UnsubscribeControlled({controlFqn} c)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (c.GetValue(__ControlledState.Property) is not __ControlledState state) return;");
        sb.AppendLine("        if (state.Subscribed)");
        sb.AppendLine("        {");
        foreach (var ev in events)
            sb.AppendLine($"            c.{ev.Key} -= __Controlled{ev.Key};");
        sb.AppendLine("        }");
        sb.AppendLine("        c.ClearValue(__ControlledState.Property);");
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var ev in events)
        {
            sb.AppendLine($"    private static readonly {ev.First().Delegate} __Controlled{ev.Key} = static (sender, _) =>");
            sb.AppendLine("    {");
            sb.AppendLine($"        if (sender is not {controlFqn} c || c.GetValue(__ControlledState.Property) is not __ControlledState state) return;");
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
            sb.AppendLine("    };");
            sb.AppendLine();
        }
    }
}
