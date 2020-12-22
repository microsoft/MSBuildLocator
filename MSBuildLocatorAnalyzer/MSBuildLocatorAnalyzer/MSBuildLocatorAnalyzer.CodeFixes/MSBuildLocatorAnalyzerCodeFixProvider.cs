using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MSBuildLocatorAnalyzer
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MSBuildLocatorAnalyzerCodeFixProvider)), Shared]
    public class MSBuildLocatorAnalyzerCodeFixProvider : CodeFixProvider
    {
        private const string title = "Move to separate method";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(MSBuildLocatorAnalyzerAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // TODO: Replace the following code with your own analysis, generating a CodeAction for each fix to suggest
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Find the type declaration identified by the diagnostic.
            var declaration = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().First();

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: c => AddBracesAsync(context.Document, diagnostic, root),
                    equivalenceKey: title),
                diagnostic);
        }

        Task<Document> AddBracesAsync(Document document, Diagnostic diagnostic, SyntaxNode root)
        {
            StatementSyntax statement = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<StatementSyntax>();
            SyntaxNode method;
            for (method = root.FindNode(diagnostic.Location.SourceSpan); !(method is MethodDeclarationSyntax); method = method.Parent);
            SyntaxTokenList modifiers = (method as MethodDeclarationSyntax).Modifiers.Any(mod => mod.ValueText.Equals("static")) ?
                SyntaxFactory.TokenList(SyntaxFactory.Identifier("private"), SyntaxFactory.Identifier("static")) :
                SyntaxFactory.TokenList(SyntaxFactory.Identifier("private"));
            MethodDeclarationSyntax toInsert = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName("void"), "UseMSBuild")
                .WithModifiers(modifiers)
                .WithBody(SyntaxFactory.Block(statement))
                .WithAdditionalAnnotations(Formatter.Annotation);
            var newRoot = root.InsertNodesAfter(method, new List<SyntaxNode>() { toInsert });

            statement = newRoot.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<StatementSyntax>();
            ExpressionStatementSyntax expression = SyntaxFactory.ExpressionStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(toInsert.Identifier.ValueText))
                .WithArgumentList(SyntaxFactory.ArgumentList()
                    .WithOpenParenToken(SyntaxFactory.Token(SyntaxKind.OpenParenToken))
                    .WithCloseParenToken(SyntaxFactory.Token(SyntaxKind.CloseParenToken))));
            newRoot = newRoot.ReplaceNode(statement, expression);
            return Task.FromResult(document.WithSyntaxRoot(newRoot));
        }
    }
}
