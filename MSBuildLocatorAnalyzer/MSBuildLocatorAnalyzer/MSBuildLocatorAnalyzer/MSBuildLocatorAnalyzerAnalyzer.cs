using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

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

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxTreeAction(syntaxTreeContext =>
            {
                var root = syntaxTreeContext.Tree.GetRoot(syntaxTreeContext.CancellationToken);
                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (method != null)
                    {
                        bool msbuildLocatorRegister = false;
                        Location locOfMSBuildUse = Location.None;
                        string[] msbuildStatementNames = { "MSBuildLocator.RegisterMSBuildPath", "MSBuildLocator.RegisterInstance", "MSBuildLocator.RegisterDefaults" };
                        foreach (var statement in method.Body.Statements)
                        {
                            if (statement != null)
                            {
                                foreach (string s in msbuildStatementNames)
                                {
                                    if (statement.ToString().Contains(s))
                                    {
                                        msbuildLocatorRegister = true;
                                    }
                                }
                                if (statement.ToString().Contains("hi") && locOfMSBuildUse == Location.None)
                                {
                                    locOfMSBuildUse = statement.GetFirstToken().GetLocation();
                                }
                            }
                        }
                        if (msbuildLocatorRegister && locOfMSBuildUse != Location.None)
                        {
                            syntaxTreeContext.ReportDiagnostic(Diagnostic.Create(Rule, locOfMSBuildUse));
                        }
                    }
                }
            });
        }
    }
}
