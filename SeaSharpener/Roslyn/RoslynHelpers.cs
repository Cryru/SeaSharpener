#region Using

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

#endregion

namespace SeaSharpener.Roslyn
{
    public static class RoslynHelpers
    {
        public static EnumDeclarationSyntax MakePublic(this EnumDeclarationSyntax decl)
        {
            return decl.AddModifiers(Token(SyntaxKind.PublicKeyword));
        }

        public static FieldDeclarationSyntax MakePublic(this FieldDeclarationSyntax decl)
        {
            return decl.AddModifiers(Token(SyntaxKind.PublicKeyword));
        }

        public static MethodDeclarationSyntax MakePublic(this MethodDeclarationSyntax decl)
        {
            return decl.AddModifiers(Token(SyntaxKind.PublicKeyword));
        }

        public static DelegateDeclarationSyntax MakePublic(this DelegateDeclarationSyntax decl)
        {
            return decl.AddModifiers(Token(SyntaxKind.PublicKeyword));
        }

        public static TypeDeclarationSyntax MakePublic(this TypeDeclarationSyntax decl)
        {
            return decl.AddModifiers(Token(SyntaxKind.PublicKeyword));
        }

        public static TypeDeclarationSyntax MakeUnsafe(this TypeDeclarationSyntax decl)
        {
            return decl.AddModifiers(Token(SyntaxKind.UnsafeKeyword));
        }

        public static ConstructorDeclarationSyntax MakePublic(this ConstructorDeclarationSyntax decl)
        {
            return decl.AddModifiers(Token(SyntaxKind.PublicKeyword));
        }

        public static MethodDeclarationSyntax MakeStatic(this MethodDeclarationSyntax decl)
        {
            return decl.AddModifiers(Token(SyntaxKind.StaticKeyword));
        }

        public static FieldDeclarationSyntax MakeConst(this FieldDeclarationSyntax decl)
        {
            return decl.AddModifiers(Token(SyntaxKind.ConstKeyword));
        }

        private static readonly HashSet<string> ReservedWords = new HashSet<string>(new[]
        {
            "out", "in", "base", "null", "string", "lock", "internal", "value", "params"
        });

        public static string FixReservedWords(string name)
        {
            if (ReservedWords.Contains(name)) name = "_" + name + "_";
            return name;
        }

        public static string? EnsureSemicolonEnding(string? statement)
        {
            if (statement == null) return null;

            string trimmed = statement.Trim();
            if (string.IsNullOrEmpty(trimmed)) return trimmed;
            if (!trimmed.EndsWith(";") && !trimmed.EndsWith("}")) return statement + ";";
            return statement;
        }
    }
}