#region Using

using System.Diagnostics;
using System.Globalization;
using ClangSharp;
using ClangSharp.Interop;

#endregion

namespace SeaSharpener.Clang
{
    public static class ClangHelpers
    {
        public static IEnumerable<Cursor> EnumerateCursors(this TranslationUnit translationUnit)
        {
            foreach (Cursor? cursor in translationUnit.TranslationUnitDecl.CursorChildren)
            {
                var decl = cursor as Decl;
                if (decl == null) continue;
                if (decl.SourceRange.Start.IsInSystemHeader) continue;
                yield return cursor;
            }
        }

        public static string GetTokenLiteral(CXCursor cursor)
        {
            Span<CXToken> tokens = cursor.TranslationUnit.Tokenize(cursor.SourceRange);

            Debug.Assert(tokens.Length == 1);
            Debug.Assert(tokens[0].Kind == CXTokenKind.CXToken_Literal);

            var spelling = tokens[0].GetSpelling(cursor.TranslationUnit).ToString();
            spelling = spelling.Trim('\\', '\r', '\n');
            return spelling;
        }

        public static string GetLiteralAsString(CXCursor cursor)
        {
            // todo: implement IntegralCast
            // todo: implement binary operators

            switch (cursor.Kind)
            {
                case CXCursorKind.CXCursor_IntegerLiteral:

                    string tokenLiteral = GetTokenLiteral(cursor);
                    if (tokenLiteral.StartsWith("0x") && int.TryParse(tokenLiteral[2..], NumberStyles.HexNumber, null, out int value)) tokenLiteral = value.ToString();
                    return tokenLiteral;

                case CXCursorKind.CXCursor_FloatingLiteral:
                    return GetTokenLiteral(cursor);
                case CXCursorKind.CXCursor_CharacterLiteral:
                    return clangsharp.Cursor_getCharacterLiteralValue(cursor).ToString();
                case CXCursorKind.CXCursor_StringLiteral:
                    return clangsharp.Cursor_getStringLiteralValue(cursor).ToString();
                case CXCursorKind.CXCursor_CXXBoolLiteralExpr:
                    return clangsharp.Cursor_getBoolLiteralValue(cursor).ToString();
            }

            return string.Empty;
        }

        public static string GetLiteralAsString(Cursor cursor)
        {
            return GetLiteralAsString(cursor.Handle);
        }

        public static string GetStructName(RecordDecl decl)
        {
            return decl.TypeForDecl.AsString.Replace("struct ", string.Empty);
        }
    }
}