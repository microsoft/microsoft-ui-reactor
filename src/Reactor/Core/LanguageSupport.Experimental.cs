// Licensed to the .NET Foundation under one or more agreements.
//
// EXPERIMENTAL — C# LDM 2026-07-15 "Declarative UI construction" working group.
//
// Opt-in attributes consumed by the experimental Roslyn build (feature flags
// `FactoryInitializers` / `FactoryInitializerContent`). They are ordinary attributes: with a
// stock compiler they are inert, so this file is safe to ship while the language feature is
// being evaluated.
//
// These are declared here rather than taken from the BCL because the feature is not shipped.
// If C# adopts the feature, these move to System.Runtime.CompilerServices in the framework.

using System;

namespace System.Runtime.CompilerServices;

/// <summary>
/// Marks a method as a factory: callers may apply a trailing object initializer to its result,
/// e.g. <c>Button("OK") { CornerRadius = 4 }</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class FactoryAttribute : Attribute;

/// <summary>
/// Marks a type as factory-initializable: a trailing object initializer may be applied to the
/// result of <em>any</em> call that produces this type, without the called method opting in.
/// </summary>
/// <remarks>
/// This is the composability-preserving opt-in. Extracting a subtree into a helper method does
/// not change how callers write the call site, because the permission travels with the type
/// rather than with each method.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = true)]
public sealed class FactoryInitializableAttribute : Attribute;

/// <summary>
/// Designates the member that receives bare content elements written inside an initializer
/// block, e.g. the <c>Children</c> of a <c>VStack { Spacing = 8, a, b }</c>.
/// </summary>
/// <remarks>
/// The direct analogue of XAML's <c>[ContentProperty]</c>, but resolved by the compiler and
/// assigned through an <c>init</c> member, so nothing is mutated.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = true)]
public sealed class ContentPropertyAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
