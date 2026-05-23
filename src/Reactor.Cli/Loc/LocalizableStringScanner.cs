using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Cli.Loc;

/// <summary>
/// Roslyn-based scanner that walks C# syntax trees to find localizable strings
/// in Reactor DSL patterns: Text(), Button(), Heading(), .Placeholder(), .Header(),
/// .ToolTip(), .Title(), etc.
/// </summary>
internal static class LocalizableStringScanner
{
    // DSL factory methods whose first string argument is localizable
    private static readonly HashSet<string> LocalizableDslMethods = new(StringComparer.Ordinal)
    {
        "TextBlock", "Heading", "SubHeading", "Caption",
        "Button", "HyperlinkButton", "RepeatButton",
        "ToggleButton", "DropDownButton", "SplitButton", "ToggleSplitButton",
        "RadioButton",
        "InfoBar",
    };

    // DSL factory methods with named string parameters that are localizable
    private static readonly Dictionary<string, HashSet<string>> LocalizableNamedParams = new(StringComparer.Ordinal)
    {
        ["TextBox"] = new(StringComparer.Ordinal) { "placeholder", "header" },
        ["PasswordBox"] = new(StringComparer.Ordinal) { "placeholderText" },
        ["NumberBox"] = new(StringComparer.Ordinal) { "header" },
        ["CheckBox"] = new(StringComparer.Ordinal) { "label" },
        ["Expander"] = new(StringComparer.Ordinal) { "header" },
    };

    // Extension/modifier methods whose string argument is localizable
    private static readonly HashSet<string> LocalizableModifiers = new(StringComparer.Ordinal)
    {
        "ToolTip", "Header", "Placeholder", "PaneTitle", "Subtitle",
    };

    // Extension methods whose string argument is NOT localizable
    private static readonly HashSet<string> NonLocalizableModifiers = new(StringComparer.Ordinal)
    {
        "AutomationId", "Name", "Key",
    };

    public static List<LocalizableString> Scan(string sourceText, string filePath)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText, path: filePath);
        var root = tree.GetRoot();
        var results = new List<LocalizableString>();

        var walker = new DslWalker(filePath, results);
        walker.Visit(root);

        return results;
    }

    private sealed class DslWalker : CSharpSyntaxWalker
    {
        private readonly string _filePath;
        private readonly List<LocalizableString> _results;

        public DslWalker(string filePath, List<LocalizableString> results)
        {
            _filePath = filePath;
            _results = results;
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var methodName = GetMethodName(node);

            if (methodName != null)
            {
                // Skip if this is inside a t.Message() call
                if (IsInsideMessageCall(node))
                {
                    base.VisitInvocationExpression(node);
                    return;
                }

                // Check DSL factory methods: Text("Hello"), Button("Save", handler)
                if (LocalizableDslMethods.Contains(methodName))
                {
                    ProcessDslFactoryCall(node, methodName);
                }
                // Check DSL factory methods with named params: TextBox(value, placeholder: "Search")
                else if (LocalizableNamedParams.TryGetValue(methodName, out var paramNames))
                {
                    ProcessNamedParamCall(node, methodName, paramNames);
                }
                // Check modifier/extension methods: .ToolTip("Copy"), .Header("Name")
                else if (LocalizableModifiers.Contains(methodName) && IsExtensionCall(node))
                {
                    ProcessModifierCall(node, methodName);
                }
            }

            base.VisitInvocationExpression(node);
        }

        private void ProcessDslFactoryCall(InvocationExpressionSyntax node, string methodName)
        {
            var args = node.ArgumentList.Arguments;
            if (args.Count == 0) return;

            var firstArg = args[0].Expression;

            // Skip if first arg is already t.Message(...)
            if (IsMessageCall(firstArg)) return;

            var className = GetEnclosingClassName(node);
            ProcessExpression(firstArg, className, methodName);
        }

        private void ProcessNamedParamCall(InvocationExpressionSyntax node, string methodName, HashSet<string> paramNames)
        {
            var args = node.ArgumentList.Arguments;
            var className = GetEnclosingClassName(node);

            foreach (var arg in args)
            {
                // Check named arguments
                if (arg.NameColon != null)
                {
                    var paramName = arg.NameColon.Name.Identifier.Text;
                    if (paramNames.Contains(paramName) && !IsMessageCall(arg.Expression))
                    {
                        ProcessExpression(arg.Expression, className, $"{methodName}.{paramName}");
                    }
                }
            }

            // Also check positional arguments that map to localizable params
            // For TextBox: placeholder is arg index 2, header is arg index 3
            if (methodName == "TextBox")
            {
                if (args.Count >= 3 && args[2].NameColon == null)
                {
                    var placeholderArg = args[2].Expression;
                    if (!IsMessageCall(placeholderArg))
                        ProcessExpression(placeholderArg, className, $"{methodName}.placeholder");
                }
                if (args.Count >= 4 && args[3].NameColon == null)
                {
                    var headerArg = args[3].Expression;
                    if (!IsMessageCall(headerArg))
                        ProcessExpression(headerArg, className, $"{methodName}.header");
                }
            }
            if (methodName == "PasswordBox" && args.Count >= 3 && args[2].NameColon == null)
            {
                var placeholderArg = args[2].Expression;
                if (!IsMessageCall(placeholderArg))
                    ProcessExpression(placeholderArg, className, "PasswordBox.placeholderText");
            }
            if (methodName == "NumberBox" && args.Count >= 3 && args[2].NameColon == null)
            {
                var headerArg = args[2].Expression;
                if (!IsMessageCall(headerArg))
                    ProcessExpression(headerArg, className, "NumberBox.header");
            }
            if (methodName == "CheckBox" && args.Count >= 3 && args[2].NameColon == null)
            {
                var labelArg = args[2].Expression;
                if (!IsMessageCall(labelArg))
                    ProcessExpression(labelArg, className, "CheckBox.label");
            }
            // Expander: header is arg index 0
            if (methodName == "Expander" && args.Count >= 1 && args[0].NameColon == null)
            {
                var headerArg = args[0].Expression;
                if (!IsMessageCall(headerArg))
                    ProcessExpression(headerArg, className, "Expander");
            }
        }

        private void ProcessModifierCall(InvocationExpressionSyntax node, string methodName)
        {
            var args = node.ArgumentList.Arguments;
            if (args.Count == 0) return;

            var firstArg = args[0].Expression;
            if (IsMessageCall(firstArg)) return;

            var className = GetEnclosingClassName(node);
            ProcessExpression(firstArg, className, methodName);
        }

        private void ProcessExpression(ExpressionSyntax expr, string className, string context)
        {
            switch (expr)
            {
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    ProcessStringLiteral(literal, className, context);
                    break;

                case InterpolatedStringExpressionSyntax interpolated:
                    ProcessInterpolatedString(interpolated, className, context);
                    break;

                case ConditionalExpressionSyntax ternary:
                    ProcessTernary(ternary, className, context);
                    break;

                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression):
                    ProcessNullCoalescing(binary, className, context);
                    break;

                case ParenthesizedExpressionSyntax parens:
                    ProcessExpression(parens.Expression, className, context);
                    break;

                // Variable references, method calls, etc. — not extractable
                default:
                    break;
            }
        }

        private void ProcessStringLiteral(LiteralExpressionSyntax literal, string className, string context)
        {
            var value = literal.Token.ValueText;
            if (string.IsNullOrWhiteSpace(value)) return;

            _results.Add(new LocalizableString
            {
                FilePath = _filePath,
                ClassName = className,
                Context = context,
                Value = value,
                SpanStart = literal.SpanStart,
                SpanLength = literal.Span.Length,
            });
        }

        private void ProcessInterpolatedString(InterpolatedStringExpressionSyntax interpolated, string className, string context)
        {
            var (icuMessage, argumentMap, warnings) = InterpolationConverter.Convert(interpolated);

            if (icuMessage == null) return;

            var ls = new LocalizableString
            {
                FilePath = _filePath,
                ClassName = className,
                Context = context,
                Value = icuMessage,
                SpanStart = interpolated.SpanStart,
                SpanLength = interpolated.Span.Length,
                IsInterpolation = true,
                ArgumentMap = argumentMap,
            };

            if (warnings.Count > 0)
                ls.Warning = string.Join("; ", warnings);

            _results.Add(ls);
        }

        private void ProcessTernary(ConditionalExpressionSyntax ternary, string className, string context)
        {
            // Extract both branches as separate keys
            ProcessTernaryBranch(ternary.WhenTrue, className, context, 0, ternary);
            ProcessTernaryBranch(ternary.WhenFalse, className, context, 1, ternary);
        }

        private void ProcessTernaryBranch(ExpressionSyntax branch, string className, string context, int branchIndex, ConditionalExpressionSyntax parent)
        {
            switch (branch)
            {
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    var value = literal.Token.ValueText;
                    if (string.IsNullOrWhiteSpace(value)) return;
                    _results.Add(new LocalizableString
                    {
                        FilePath = _filePath,
                        ClassName = className,
                        Context = context,
                        Value = value,
                        SpanStart = literal.SpanStart,
                        SpanLength = literal.Span.Length,
                        TernaryBranch = branchIndex,
                    });
                    break;

                case InterpolatedStringExpressionSyntax interpolated:
                    var (icuMessage, argumentMap, warnings) = InterpolationConverter.Convert(interpolated);
                    if (icuMessage != null)
                    {
                        _results.Add(new LocalizableString
                        {
                            FilePath = _filePath,
                            ClassName = className,
                            Context = context,
                            Value = icuMessage,
                            SpanStart = interpolated.SpanStart,
                            SpanLength = interpolated.Span.Length,
                            IsInterpolation = true,
                            ArgumentMap = argumentMap,
                            TernaryBranch = branchIndex,
                        });
                    }
                    break;
            }
        }

        private void ProcessNullCoalescing(BinaryExpressionSyntax binary, string className, string context)
        {
            // Extract only the literal side (the fallback value)
            if (binary.Right is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                ProcessStringLiteral(literal, className, context);
            }
            else if (binary.Right is InterpolatedStringExpressionSyntax interpolated)
            {
                ProcessInterpolatedString(interpolated, className, context);
            }
            // Left side could also be a literal (unusual but possible)
            else if (binary.Left is LiteralExpressionSyntax leftLiteral && leftLiteral.IsKind(SyntaxKind.StringLiteralExpression))
            {
                ProcessStringLiteral(leftLiteral, className, context);
            }
        }

        private static string? GetMethodName(InvocationExpressionSyntax node)
        {
            return node.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
                _ => null,
            };
        }

        private static bool IsExtensionCall(InvocationExpressionSyntax node)
        {
            return node.Expression is MemberAccessExpressionSyntax;
        }

        private static bool IsMessageCall(ExpressionSyntax expr)
        {
            // Check if expr is t.Message(...) or something.Message(...)
            if (expr is InvocationExpressionSyntax invocation)
            {
                var name = GetMethodName(invocation);
                return name == "Message";
            }
            return false;
        }

        private static bool IsInsideMessageCall(SyntaxNode node)
        {
            var parent = node.Parent;
            while (parent != null)
            {
                if (parent is InvocationExpressionSyntax inv)
                {
                    var name = GetMethodName(inv);
                    if (name == "Message") return true;
                }
                parent = parent.Parent;
            }
            return false;
        }

        private static string GetEnclosingClassName(SyntaxNode node)
        {
            var current = node.Parent;
            while (current != null)
            {
                if (current is ClassDeclarationSyntax classDecl)
                    return classDecl.Identifier.Text;
                if (current is RecordDeclarationSyntax recordDecl)
                    return recordDecl.Identifier.Text;
                current = current.Parent;
            }
            return "Global";
        }
    }
}
