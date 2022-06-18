#region Using

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SeaSharpener.Meta;

#endregion

namespace SeaSharpener.Roslyn
{
    /// <summary>
    /// Handles writing generated output to a file as code.
    /// </summary>
    public static class RoslynWriter
    {
        public static void WriteOutput(RoslynCodeOutput output, StreamWriter writer)
        {
            Logger.Log("  Writing code");

            Logger.Log($"    Writing {output.GlobalConstants.Count} constants");
            for (var i = 0; i < output.GlobalConstants.Count; i++)
            {
                FieldDeclarationSyntax syntax = output.GlobalConstants[i];
                writer.WriteLine(WriteSyntaxIndented(syntax));
            }

            if (output.GlobalConstants.Count > 0) writer.WriteLine();

            Logger.Log($"    Writing {output.FunctionTypes.Count} function types");
            for (var i = 0; i < output.FunctionTypes.Count; i++)
            {
                DelegateDeclarationSyntax syntax = output.FunctionTypes[i];
                writer.WriteLine(WriteSyntaxIndented(syntax));

                if (i != output.FunctionTypes.Count - 1) writer.WriteLine();
            }

            if (output.FunctionTypes.Count > 0) writer.WriteLine();

            Logger.Log($"    Writing {output.Enums.Count} enums");
            for (var i = 0; i < output.Enums.Count; i++)
            {
                EnumDeclarationSyntax syntax = output.Enums[i];
                writer.WriteLine(WriteSyntaxIndented(syntax));

                if (i != output.Enums.Count - 1) writer.WriteLine();
            }

            if (output.Enums.Count > 0) writer.WriteLine();

            Logger.Log($"    Writing {output.Structs.Count} structs");
            for (var i = 0; i < output.Structs.Count; i++)
            {
                TypeDeclarationSyntax syntax = output.Structs[i];
                writer.WriteLine(WriteSyntaxIndented(syntax));

                if (i != output.Structs.Count - 1) writer.WriteLine();
            }

            if (output.Structs.Count > 0) writer.WriteLine();
        }

        /// <summary>
        /// Since tokens are stringified one by one their indentation is messed up.
        /// This is a purely cosmetic process.
        /// </summary>
        private static string WriteSyntaxIndented(SyntaxNode syntaxNode)
        {
            string asString = syntaxNode.NormalizeWhitespace().ToFullString();
            asString = asString.Replace("\r\n", "\n");
            asString = asString.Replace("\n", "\n\t\t");
            asString = "\t\t" + asString;
            return asString;
        }
    }
}