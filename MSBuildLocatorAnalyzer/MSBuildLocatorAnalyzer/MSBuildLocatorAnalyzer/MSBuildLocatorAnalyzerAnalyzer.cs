using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace MSBuildLocatorAnalyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class MSBuildLocatorAnalyzerAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "MSBuildLocatorAnalyzer";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Localizing%20Analyzers.md for more on localization
        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Naming";

        private readonly string[] msBuildAssemblies =
        {
            "Microsoft.Build",
            "Microsoft.Build.Engine",
            "Microsoft.Build.Framework",
            "Microsoft.Build.Tasks.Core",
            "Microsoft.Build.Utilities.Core"
        };

        private readonly string[] msbuildLocatorRegisterMethods =
        {
            "RegisterDefaults",
            "RegisterInstance",
            "RegisterMSBuildPath"
        };

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterOperationAction(opact =>
            {
                var invocation = (IInvocationOperation)opact.Operation;
                var targetMethod = invocation.TargetMethod;
                if (targetMethod?.ContainingAssembly != null)
                {
                    if (targetMethod.ContainingAssembly.Name.Equals("Microsoft.Build.Locator") && msbuildLocatorRegisterMethods.Contains(targetMethod.Name))
                    {
                        MethodDeclarationSyntax method = (MethodDeclarationSyntax)invocation.Syntax.FirstAncestorOrSelf((Func<SyntaxNode, bool>)(a => a is MethodDeclarationSyntax));
                        if (method != null)
                        {
                            IOperation symbolParent;
                            for (symbolParent = invocation; symbolParent.Parent != null && symbolParent.Kind != OperationKind.MethodBody; symbolParent = symbolParent.Parent);
                            foreach (var child in symbolParent.Descendants())
                            {
                                if ((child is IInvocationOperation op && msBuildAssemblies.Contains(op.TargetMethod?.ContainingAssembly?.Name)) ||
                                (child is ITypeOfOperation toOp && msBuildAssemblies.Contains(toOp.TypeOperand.ContainingAssembly?.Name)) ||
                                (msBuildAssemblies.Contains(child.Type?.ContainingAssembly?.Name)))
                                {
                                    opact.ReportDiagnostic(Diagnostic.Create(Rule, child.Syntax.GetLocation()));
                                }
                            }
                        }
                    }
                }
            }, OperationKind.Invocation);
        }
    }
}
