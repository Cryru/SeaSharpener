#region Using

using ClangSharp;
using ClangSharp.Interop;
using SeaSharpener.Meta;
using Index = ClangSharp.Index;

#endregion

namespace SeaSharpener.Clang
{
    /// <summary>
    /// Compiles the C code to a syntax tree which will be used to generate the C# code.
    /// </summary>
    public static class ClangCompiler
    {
        public static unsafe TranslationUnit? Compile(SeaProject project, string filePath)
        {
            var compilerArguments = new List<string>();

            if (project.Defines != null)
                for (var i = 0; i < project.Defines.Length; i++)
                {
                    string define = project.Defines[i];
                    compilerArguments.Add($"-D{define}");
                }

            if (project.IncludeDirectories != null)
                for (var i = 0; i < project.IncludeDirectories.Length; i++)
                {
                    string includeFolder = project.IncludeDirectories[i];
                    compilerArguments.Add($"-I{includeFolder}");
                }

            Logger.Log("  Compiling");

            // Try to compile the code.
            var index = Index.Create();
            CXErrorCode res = CXTranslationUnit.TryParse(
                index.Handle,
                filePath,
                compilerArguments.ToArray(),
                Array.Empty<CXUnsavedFile>(),
                CXTranslationUnit_Flags.CXTranslationUnit_None,
                out CXTranslationUnit cxTranslationUnit
            );

            // Print 
            uint numDiagnostics = clang.getNumDiagnostics(cxTranslationUnit);
            for (uint i = 0; i < numDiagnostics; i++)
            {
                void* diagnosticsPtr = clang.getDiagnostic(cxTranslationUnit, i);
                try
                {
                    // ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
                    const uint displayFlags = (uint) (CXDiagnosticDisplayOptions.CXDiagnostic_DisplaySourceLocation | CXDiagnosticDisplayOptions.CXDiagnostic_DisplaySourceRanges);
                    var str = clang.formatDiagnostic(diagnosticsPtr, displayFlags).ToString();
                    Logger.Log(str);
                }
                finally
                {
                    clang.disposeDiagnostic(diagnosticsPtr);
                }
            }

            if (res != CXErrorCode.CXError_Success)
            {
                Logger.LogError($"Compilation failure {res}");
                return null;
            }

            var translationUnit = TranslationUnit.GetOrCreate(cxTranslationUnit);

            // Dump compiled data to a log.
            using var fileStream = new FileStream(Path.Combine(project.OutputDirectory, "ClangOutput.txt"), FileMode.Create, FileAccess.ReadWrite);
            using var writer = new StreamWriter(fileStream);
            foreach (Cursor cursor in translationUnit.EnumerateCursors())
            {
                WriteCursor(writer, cursor);
            }

            return translationUnit;
        }

        public static void WriteCursor(StreamWriter writer, Cursor cursor, int indent = 0)
        {
            var cursorDesc = $"{cursor.CursorKindSpelling}";

            string spelling = cursor.Spelling;
            if (!string.IsNullOrEmpty(spelling)) cursorDesc += $" [{spelling}]";

            CXString cursorType = clang.getTypeSpelling(clang.getCursorType(cursor.Handle));
            var cursorTypeStr = cursorType.ToString();
            if (!string.IsNullOrEmpty(cursorTypeStr)) cursorDesc += $" {cursorTypeStr}";

            var addition = string.Empty;
            switch (cursor.CursorKind)
            {
                case CXCursorKind.CXCursor_UnaryExpr:
                case CXCursorKind.CXCursor_UnaryOperator:
                {
                    CX_UnaryOperatorKind opCode = clangsharp.Cursor_getUnaryOpcode(cursor.Handle);
                    addition = $"Unary Operator: {opCode} {clangsharp.Cursor_getUnaryOpcodeSpelling(opCode)}";
                    break;
                }
                case CXCursorKind.CXCursor_BinaryOperator:
                {
                    CX_BinaryOperatorKind opCode = clangsharp.Cursor_getBinaryOpcode(cursor.Handle);
                    addition = $"Binary Operator: {opCode} {clangsharp.Cursor_getBinaryOpcodeSpelling(opCode)}";
                    break;
                }
                case CXCursorKind.CXCursor_IntegerLiteral:
                case CXCursorKind.CXCursor_FloatingLiteral:
                case CXCursorKind.CXCursor_CharacterLiteral:
                case CXCursorKind.CXCursor_StringLiteral:
                case CXCursorKind.CXCursor_CXXBoolLiteralExpr:
                    addition = $"Literal: {ClangHelpers.GetLiteralAsString(cursor.Handle)}";
                    break;
            }

            if (!string.IsNullOrEmpty(addition)) cursorDesc += " (" + addition + ")";

            for (var i = 0; i < indent; i++)
            {
                writer.Write("\t");
            }

            writer.WriteLine(cursorDesc);

            foreach (Cursor? child in cursor.CursorChildren)
            {
                WriteCursor(writer, child, indent + 1);
            }
        }
    }
}