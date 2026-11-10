#if !NET11_0_OR_GREATER
namespace System.Runtime.CompilerServices;

// The C# 15 compiler emits this on `closed` classes so consuming compilers can
// recognise the hierarchy. It ships in the .NET 11 runtime; Reactor still targets
// net10.0, so declare it here. Compiler-only metadata — no runtime behaviour.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class IsClosedTypeAttribute : Attribute;
#endif
