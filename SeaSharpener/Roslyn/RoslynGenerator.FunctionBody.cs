#region Using

using System.Diagnostics;
using System.Text;
using ClangSharp;
using ClangSharp.Interop;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SeaSharpener.Clang;
using SeaSharpener.Meta;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

#endregion

namespace SeaSharpener.Roslyn
{
    public static partial class RoslynGenerator
    {
        public class ClangFunctionScope
        {
            public string FunctionName { get; protected set; }
            public RoslynCodeOutput OutputContext { get; protected set; }
            public ClangTypeMeta ReturnType { get; protected set; }

            public ClangFunctionScope(RoslynCodeOutput outputContext, string name, ClangTypeMeta returnType)
            {
                OutputContext = outputContext;
                FunctionName = name;
                ReturnType = returnType;
            }
        }

        private static void GenerateFunction(RoslynCodeOutput output, FunctionDecl cursor)
        {
            string functionName = RoslynHelpers.FixReservedWords(cursor.Spelling);
            Logger.Log($"    Generating function {functionName}");

            ClangTypeMeta returnTypeMeta = ClangTypeMeta.FromClangType(cursor.ReturnType);
            TypeSyntax returnTypeParsed = ParseTypeName(returnTypeMeta.GetRoslynName());
            MethodDeclarationSyntax methodDecl = MethodDeclaration(returnTypeParsed, cursor.Spelling).MakePublic().MakeStatic();

            var functionScope = new ClangFunctionScope(output, functionName, returnTypeMeta);

            foreach (ParmVarDecl? argCursor in cursor.Parameters)
            {
                string name = RoslynHelpers.FixReservedWords(argCursor.Name);
                ClangTypeMeta argTypeMeta = ClangTypeMeta.FromClangType(argCursor.Type);
                TypeSyntax argTypeParsed = ParseTypeName(argTypeMeta.GetRoslynName());
                methodDecl = methodDecl.AddParameterListParameters(Parameter(Identifier(name)).WithType(argTypeParsed));
            }

            foreach (Stmt? child in cursor.Body.Children)
            {
                string? result = ProcessFunctionChildStatement(child, functionScope);
                result = RoslynHelpers.EnsureSemicolonEnding(result);
                if (result == null) continue;

                StatementSyntax statement = ParseStatement(result);
                methodDecl = methodDecl.AddBodyStatements(statement);
            }

            output.Functions.Add(methodDecl);
        }

        private static string? ProcessChildCursorIfExists(Cursor cursor, ClangFunctionScope functionScope, int idx, out Cursor? childCursor)
        {
            childCursor = null;
            if (cursor.CursorChildren.Count <= idx) return null;

            childCursor = cursor.CursorChildren[idx];
            return ProcessFunctionChildStatement(childCursor, functionScope);
        }

        // https://clang.llvm.org/doxygen/group__CINDEX.html#ggaaccc432245b4cd9f2d470913f9ef0013a3cd4b745869f56b1e2ba5c8c6d053347
        private static string ProcessFunctionChildStatement(Cursor cursor, ClangFunctionScope functionScope)
        {
            switch (cursor.Handle.Kind)
            {
                case CXCursorKind.CXCursor_EnumConstantDecl:
                {
                    string? expr = ProcessChildCursorIfExists(cursor, functionScope, 0, out _);
                    Debug.Assert(expr != null);
                    return $"{cursor.Spelling} = {expr}";
                }

                // unary expressions such as sizeof and alignof
                case CXCursorKind.CXCursor_UnaryExpr:
                {
                    CX_UnaryOperatorKind opCode = clangsharp.Cursor_getUnaryOpcode(cursor.Handle);
                    string? expr = ProcessChildCursorIfExists(cursor, functionScope, 0, out Cursor? childCursor);

                    string[] tokens = ClangHelpers.Tokenize(cursor.Handle);
                    if (opCode == CX_UnaryOperatorKind.CX_UO_Invalid && expr != null && childCursor != null)
                    {
                        ClangTypeMeta typeMeta = ClangTypeMeta.FromCursor(childCursor.Handle);

                        // 4 is default alignment
                        if (tokens.Length > 0 && tokens[0] == "__alignof")
                            return "4";

                        // Assuming sizeof
                        {
                            if (typeMeta.ConstantArraySizes != null)
                            {
                                if (typeMeta.ConstantArraySizes.Length == 1) return $"{typeMeta.ConstantArraySizes[0]} * sizeof({typeMeta.GetRoslynName()})";
                                Logger.LogError("Sizeof for multidimensional arrays is unsupported");
                                return "1";
                            }

                            return $"sizeof({typeMeta.GetRoslynName()})";
                        }
                    }

                    return string.Join(string.Empty, tokens);
                }

                // Value declaration
                case CXCursorKind.CXCursor_DeclRefExpr:
                {
                    return RoslynHelpers.FixReservedWords(cursor.Spelling);
                }

                // Binary operator - Child0+Child1
                // Child1-Child2
                case CXCursorKind.CXCursor_CompoundAssignOperator:
                case CXCursorKind.CXCursor_BinaryOperator:
                {
                    string? a = ProcessChildCursorIfExists(cursor, functionScope, 0, out _);
                    string? b = ProcessChildCursorIfExists(cursor, functionScope, 1, out _);
                    CX_BinaryOperatorKind operatorKind = clangsharp.Cursor_getBinaryOpcode(cursor.Handle);

                    Logger.Log($"      Generating binary operator {operatorKind}");

                    string operatorString = clangsharp.Cursor_getBinaryOpcodeSpelling(clangsharp.Cursor_getBinaryOpcode(cursor.Handle)).CString;
                    return $"{a}{operatorString}{b}";
                }

                // Unary expressions other than sizeof and alignof, such as dereferencing, negation etc - OperatorChild0 but can be Child0Operator
                case CXCursorKind.CXCursor_UnaryOperator:
                {
                    string? child = ProcessChildCursorIfExists(cursor, functionScope, 0, out Cursor? _);
                    string operatorString = clangsharp.Cursor_getUnaryOpcodeSpelling(clangsharp.Cursor_getUnaryOpcode(cursor.Handle)).CString;

                    ClangTypeMeta cursorType = ClangTypeMeta.FromCursor(cursor.Handle);
                    CX_UnaryOperatorKind unaryOperator = clangsharp.Cursor_getUnaryOpcode(cursor.Handle);
                    if (functionScope.OutputContext.IsStructAClass(cursorType.Name) && unaryOperator is CX_UnaryOperatorKind.CX_UO_AddrOf or CX_UnaryOperatorKind.CX_UO_Deref)
                        operatorString = string.Empty;

                    bool leftSideOperator = IsUnaryOperatorPre(unaryOperator);
                    return leftSideOperator ? $"{operatorString}{child}" : $"{child}{operatorString}";
                }

                // Function calls - Child0(Child1, Child2...)
                case CXCursorKind.CXCursor_CallExpr:
                {
                    string? functionExpr = ProcessChildCursorIfExists(cursor, functionScope, 0, out Cursor? _);
                    Debug.Assert(functionExpr != null);
                    string functionName = RoslynHelpers.Deparentize(functionExpr);

                    Logger.Log($"      Generating function call {functionName}");

                    // Retrieve arguments
                    var args = new List<string>();
                    for (var i = 1; i < cursor.CursorChildren.Count; i++)
                    {
                        string? argExpr = ProcessChildCursorIfExists(cursor, functionScope, i, out Cursor? _);
                        Debug.Assert(argExpr != null);
                        args.Add(argExpr);
                    }

                    return $"{functionName}({string.Join(", ", args)})";
                }

                // Return statement - return Child0
                case CXCursorKind.CXCursor_ReturnStmt:
                {
                    string? ret = ProcessChildCursorIfExists(cursor, functionScope, 0, out _);
                    Debug.Assert(ret != null);
                    string exp = string.IsNullOrEmpty(ret) ? "return" : $"return {ret}";
                    return exp;
                }

                // if statement - if(Child0) { Child1 } else { Child2 }
                case CXCursorKind.CXCursor_IfStmt:
                {
                    string? conditionExpr = ProcessChildCursorIfExists(cursor, functionScope, 0, out _);
                    string? executionExpr = ProcessChildCursorIfExists(cursor, functionScope, 1, out _);
                    string? elseExpr = ProcessChildCursorIfExists(cursor, functionScope, 2, out _);

                    if (!string.IsNullOrEmpty(executionExpr))
                        executionExpr = RoslynHelpers.EnsureSemicolonEnding(executionExpr);

                    var expr = $"if ({conditionExpr}) {executionExpr}";
                    if (elseExpr != null) expr += $" else {elseExpr}";
                    return expr;
                }

                // For loop - For(Child0;Child1;Child2) Child3
                // For() Child0
                // For(Child0) Child1
                // For(Child0;Child1) Child2
                case CXCursorKind.CXCursor_ForStmt:
                {
                    //CursorProcessResult execution = null, start = null, condition = null, it = null;
                    string? condition = "", start = "", iterator = "", execution = "";
                    switch (cursor.CursorChildren.Count)
                    {
                        case 1:
                            execution = ProcessChildCursorIfExists(cursor, functionScope, 0, out _);
                            break;
                        case 2:
                            iterator = ProcessChildCursorIfExists(cursor, functionScope, 0, out _);
                            execution = ProcessChildCursorIfExists(cursor, functionScope, 1, out _);
                            break;
                        case 3:
                            string? expr = ProcessChildCursorIfExists(cursor, functionScope, 0, out Cursor? expCursor);
                            Debug.Assert(expCursor != null);
                            if (expCursor.CursorKind == CXCursorKind.CXCursor_BinaryOperator && IsBooleanOperator(clangsharp.Cursor_getBinaryOpcode(expCursor.Handle)))
                                condition = expr;
                            else
                                start = expr;

                            expr = ProcessChildCursorIfExists(cursor, functionScope, 1, out expCursor);
                            Debug.Assert(expCursor != null);
                            if (expCursor.CursorKind == CXCursorKind.CXCursor_BinaryOperator && IsBooleanOperator(clangsharp.Cursor_getBinaryOpcode(expCursor.Handle)))
                                condition = expr;
                            else
                                iterator = expr;

                            execution = ProcessChildCursorIfExists(cursor, functionScope, 2, out _);
                            break;
                        case 4:
                            start = ProcessChildCursorIfExists(cursor, functionScope, 0, out _);
                            condition = ProcessChildCursorIfExists(cursor, functionScope, 1, out _);
                            iterator = ProcessChildCursorIfExists(cursor, functionScope, 2, out _);
                            execution = ProcessChildCursorIfExists(cursor, functionScope, 3, out _);
                            break;
                    }

                    string forDecl = $"for({start};{condition};{iterator})";
                    execution = RoslynHelpers.EnsureCurlyBraces(execution ?? "");
                    return $"{forDecl} {execution}";
                }

                // goto Child0
                case CXCursorKind.CXCursor_GotoStmt:
                {
                    // CXCursor_LabelRef
                    string? label = ProcessChildCursorIfExists(cursor, functionScope, 0, out _);
                    Debug.Assert(label != null);
                    return $"goto {label}";
                }

                // label in a goto statement
                case CXCursorKind.CXCursor_LabelRef:
                {
                    return cursor.Spelling;
                }

                // Goto label declaration - Cursor: 
                case CXCursorKind.CXCursor_LabelStmt:
                {
                    var label = $"{cursor.Spelling}:";

                    // For some reason statements after the label are grouped as its children.
                    if (cursor.CursorChildren.Count > 0)
                    {
                        var sb = new StringBuilder();
                        for (var i = 0; i < cursor.CursorChildren.Count; i++)
                        {
                            string? exp = ProcessChildCursorIfExists(cursor, functionScope, i, out Cursor? _);
                            exp = RoslynHelpers.EnsureSemicolonEnding(exp);
                            sb.Append(exp);
                        }

                        label = $"{label}\n{sb.ToString().Trim()}";
                    }
                   
                    return label;
                }

                // Ternary conditional - Child0 ? Child1 : Child2
                case CXCursorKind.CXCursor_ConditionalOperator:
                {
                    string? condition = ProcessChildCursorIfExists(cursor, functionScope, 0, out Cursor? conditionCursor);
                    Debug.Assert(condition != null && conditionCursor != null);
                    ClangTypeMeta conditionType = ClangTypeMeta.FromCursor(conditionCursor.Handle);

                    string? trueExpression = ProcessChildCursorIfExists(cursor, functionScope, 1, out _);
                    string? elseExpression = ProcessChildCursorIfExists(cursor, functionScope, 2, out _);

                    if (conditionType.Kind == ClangTypeKind.Primitive)
                    {
                        var implicitZeroCheck = true;
                        if (conditionCursor.CursorKind == CXCursorKind.CXCursor_ParenExpr)
                        {
                            implicitZeroCheck = false;
                        }
                        else if (conditionCursor.CursorKind == CXCursorKind.CXCursor_BinaryOperator)
                        {
                            CX_BinaryOperatorKind op = clangsharp.Cursor_getBinaryOpcode(conditionCursor.Handle);
                            if (op != CX_BinaryOperatorKind.CX_BO_Or && op != CX_BinaryOperatorKind.CX_BO_And) implicitZeroCheck = false;
                        }

                        if (implicitZeroCheck) condition = $"{RoslynHelpers.Parentize(condition)} != 0";
                    }

                    return $"{condition}?{trueExpression}:{elseExpression}";
                }

                // Member reference - Child0.Cursor/Child0->Cursor
                case CXCursorKind.CXCursor_MemberRefExpr:
                {
                    string? structExpression = ProcessChildCursorIfExists(cursor, functionScope, 0, out Cursor? structCursor);
                    Debug.Assert(structCursor != null);
                    ClangTypeMeta structType = ClangTypeMeta.FromCursor(structCursor.Handle);

                    var op = ".";
                    if (structExpression != "this" && !functionScope.OutputContext.IsStructAClass(structType.Name) && structType.PointerCount > 0) op = "->";
                    return structExpression + op + RoslynHelpers.FixReservedWords(cursor.Spelling);
                }

                case CXCursorKind.CXCursor_CharacterLiteral:
                case CXCursorKind.CXCursor_IntegerLiteral:
                case CXCursorKind.CXCursor_FloatingLiteral:
                case CXCursorKind.CXCursor_StringLiteral:
                {
                    string literal = ClangHelpers.GetLiteralAsString(cursor);
                    if (cursor.CursorKind == CXCursorKind.CXCursor_StringLiteral) literal = $"\"{literal}\"";
                    return literal;
                }

                // Variable declaration - Cursor = Child0
                case CXCursorKind.CXCursor_VarDecl:
                {
                    var varDecl = (VarDecl) cursor;
                    string expr = GenerateVariableDeclaration(functionScope, varDecl);

                    Logger.Log($"      Generating variable declaration {expr}");

                    if (varDecl.StorageClass == CX_StorageClass.CX_SC_Static)
                    {
                        if (ParseMemberDeclaration($"public static {expr}") is FieldDeclarationSyntax staticFieldDecl)
                            functionScope.OutputContext.GlobalConstants.Add(staticFieldDecl);
                        return string.Empty;
                    }

                    return expr;
                }

                // Variable declaration mixed with an expression
                case CXCursorKind.CXCursor_DeclStmt:
                {
                    var sb = new StringBuilder();
                    for (var i = 0; i < cursor.CursorChildren.Count; i++)
                    {
                        string? exp = ProcessChildCursorIfExists(cursor, functionScope, i, out _);
                        Debug.Assert(exp != null);
                        exp = RoslynHelpers.EnsureSemicolonEnding(exp);
                        sb.Append(exp);
                    }

                    return sb.ToString();
                }

                // Compound statement (function bodies and such) - { Child0 Child1 }
                case CXCursorKind.CXCursor_CompoundStmt:
                {
                    var sb = new StringBuilder();
                    sb.Append("{\n");

                    for (var i = 0; i < cursor.CursorChildren.Count; i++)
                    {
                        string? exp = ProcessChildCursorIfExists(cursor, functionScope, i, out Cursor? _);
                        exp = RoslynHelpers.EnsureSemicolonEnding(exp);
                        sb.Append(exp);
                    }

                    sb.Append("}\n");
                    return sb.ToString();
                }

                // Array indexing - Child0[Child1]
                case CXCursorKind.CXCursor_ArraySubscriptExpr:
                {
                    string? var = ProcessChildCursorIfExists(cursor, functionScope, 0, out Cursor? _);
                    string? expr = ProcessChildCursorIfExists(cursor, functionScope, 1, out Cursor? _);
                    return $"{var}[{expr}]";
                }

                case CXCursorKind.CXCursor_BreakStmt:
                    return "break";
                case CXCursorKind.CXCursor_ContinueStmt:
                    return "continue";

                // Simple cast - (type) Child0
                case CXCursorKind.CXCursor_CStyleCastExpr:
                {
                    string? child = ProcessChildCursorIfExists(cursor, functionScope, cursor.CursorChildren.Count - 1, out Cursor? _);
                    Debug.Assert(child != null);

                    ClangTypeMeta cursorType = ClangTypeMeta.FromCursor(cursor.Handle);

                    return $"({cursorType.GetRoslynName()}) {child}";
                }

                // Statement in parenthesis.
                case CXCursorKind.CXCursor_ParenExpr:
                {
                    string? expr = ProcessChildCursorIfExists(cursor, functionScope, 0, out Cursor? _);
                    Debug.Assert(expr != null);
                    return RoslynHelpers.Parentize(expr);;
                }

                case CXCursorKind.CXCursor_FirstExpr:
                {
                    string? lastChildExpr = ProcessChildCursorIfExists(cursor, functionScope, cursor.CursorChildren.Count - 1, out Cursor? _);
                    Debug.Assert(lastChildExpr != null);
                    return lastChildExpr;
                }
            }

            return "";
        }

        private static string GenerateVariableDeclaration(ClangFunctionScope functionScope, VarDecl cursor)
        {
            string name = RoslynHelpers.FixReservedWords(cursor.Spelling);
            bool globalVariable = cursor.StorageClass == CX_StorageClass.CX_SC_Static;
            if (globalVariable) name = $"{functionScope.FunctionName}_{name}";

            ClangTypeMeta variableType = ClangTypeMeta.FromClangType(cursor.Type);
            string typeName = variableType.GetRoslynName();

            var left = $"{typeName} {name}";
            var right = string.Empty;

            if (cursor.CursorChildren.Count > 0)
            {
                right = ProcessChildCursorIfExists(cursor, functionScope, cursor.CursorChildren.Count - 1, out _);
                Debug.Assert(right != null);
            }

            // var a = 0 -> var a = null
            if (variableType.PointerCount > 0 && RoslynHelpers.Deparentize(right) == "0") right = "null";
            if (string.IsNullOrEmpty(right) && variableType.Kind == ClangTypeKind.Struct && variableType.PointerCount == 0) right = $"new {typeName}()";

            if (!string.IsNullOrEmpty(right)) right = $"={right}";
            return $"{left}{right}";
        }

        public static bool IsLogicalBooleanOperator(CX_BinaryOperatorKind op)
        {
            return op is
                CX_BinaryOperatorKind.CX_BO_LAnd or
                CX_BinaryOperatorKind.CX_BO_LOr or
                CX_BinaryOperatorKind.CX_BO_EQ or
                CX_BinaryOperatorKind.CX_BO_GE or
                CX_BinaryOperatorKind.CX_BO_LE or
                CX_BinaryOperatorKind.CX_BO_GT or
                CX_BinaryOperatorKind.CX_BO_LT;
        }

        public static bool IsLogicalBinaryOperator(CX_BinaryOperatorKind op)
        {
            return op is CX_BinaryOperatorKind.CX_BO_LAnd or CX_BinaryOperatorKind.CX_BO_LOr;
        }

        public static bool IsBinaryOperator(CX_BinaryOperatorKind op)
        {
            return op is CX_BinaryOperatorKind.CX_BO_And or CX_BinaryOperatorKind.CX_BO_Or;
        }

        public static bool IsAssign(CX_BinaryOperatorKind op)
        {
            return op is
                CX_BinaryOperatorKind.CX_BO_AddAssign or
                CX_BinaryOperatorKind.CX_BO_AndAssign or
                CX_BinaryOperatorKind.CX_BO_Assign or
                CX_BinaryOperatorKind.CX_BO_DivAssign or
                CX_BinaryOperatorKind.CX_BO_MulAssign or
                CX_BinaryOperatorKind.CX_BO_OrAssign or
                CX_BinaryOperatorKind.CX_BO_RemAssign or
                CX_BinaryOperatorKind.CX_BO_ShlAssign or
                CX_BinaryOperatorKind.CX_BO_ShrAssign or
                CX_BinaryOperatorKind.CX_BO_SubAssign or
                CX_BinaryOperatorKind.CX_BO_XorAssign;
        }

        public static bool IsBooleanOperator(CX_BinaryOperatorKind op)
        {
            return op is
                CX_BinaryOperatorKind.CX_BO_LAnd or
                CX_BinaryOperatorKind.CX_BO_LOr or
                CX_BinaryOperatorKind.CX_BO_EQ or
                CX_BinaryOperatorKind.CX_BO_NE or
                CX_BinaryOperatorKind.CX_BO_GE or
                CX_BinaryOperatorKind.CX_BO_LE or
                CX_BinaryOperatorKind.CX_BO_GT or
                CX_BinaryOperatorKind.CX_BO_LT or
                CX_BinaryOperatorKind.CX_BO_And or
                CX_BinaryOperatorKind.CX_BO_Or;
        }

        public static bool IsUnaryOperatorPre(CX_UnaryOperatorKind type)
        {
            switch (type)
            {
                case CX_UnaryOperatorKind.CX_UO_PreInc:
                case CX_UnaryOperatorKind.CX_UO_PreDec:
                case CX_UnaryOperatorKind.CX_UO_Plus:
                case CX_UnaryOperatorKind.CX_UO_Minus:
                case CX_UnaryOperatorKind.CX_UO_Not:
                case CX_UnaryOperatorKind.CX_UO_LNot:
                case CX_UnaryOperatorKind.CX_UO_AddrOf:
                case CX_UnaryOperatorKind.CX_UO_Deref:
                    return true;
            }

            return false;
        }
    }
}