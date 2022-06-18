#region Using

using System.Diagnostics;
using System.Text;
using ClangSharp;
using ClangSharp.Interop;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SeaSharpener.Clang;
using SeaSharpener.Meta;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Type = ClangSharp.Type;

#endregion

namespace SeaSharpener.Roslyn
{
    /// <summary>
    /// Generates Roslyn expressions based on the Clang translation unit.
    /// </summary>
    public static class RoslynGenerator
    {
        public static RoslynCodeOutput Generate(SeaProject project, TranslationUnit translationUnit)
        {
            var output = new RoslynCodeOutput();

            Logger.Log("  Generating code");

            // 1. Record enums and structs found.
            foreach (Cursor cursor in translationUnit.EnumerateCursors())
            {
                switch (cursor.CursorKind)
                {
                    case CXCursorKind.CXCursor_EnumDecl:
                    {
                        GenerateEnum(output, cursor);
                        break;
                    }
                    case CXCursorKind.CXCursor_StructDecl:
                    case CXCursorKind.CXCursor_UnionDecl:
                    {
                        RecordStruct(output, cursor);
                        break;
                    }
                }
            }

            // 2. Generate code for all found structs.
            GenerateStructs(output);

            // 2.5. Generate function types.
            GenerateFunctionTypeDefs(output);

            // 3. Generate global variables. They might depend on structs so they need to be after.
            foreach (Cursor cursor in translationUnit.EnumerateCursors())
            {
                var varDecl = cursor as VarDecl;
                if (varDecl == null) continue;

                GenerateGlobalVariable(output, varDecl);
            }

            // 4. Finally generate function code.
            foreach (Cursor cursor in translationUnit.EnumerateCursors())
            {
                var funcDecl = cursor as FunctionDecl;
                if (funcDecl == null || !funcDecl.HasBody) continue;
            }

            return output;
        }

        /// <summary>
        /// C enums are converted to C# enums
        /// </summary>
        private static void GenerateEnum(RoslynCodeOutput output, Cursor cursor)
        {
            string enumName = cursor.Spelling;
            if (string.IsNullOrEmpty(enumName))
            {
                GenerateUnnamedEnum(output, cursor);
                return;
            }

            Logger.Log($"    Generating enum {enumName}");
            EnumDeclarationSyntax declSyntax = EnumDeclaration(enumName).MakePublic();

            foreach (Cursor? child in cursor.CursorChildren)
            {
                EnumMemberDeclarationSyntax enumMemberDeclaration = EnumMemberDeclaration(child.Spelling);
                if (child.CursorChildren.Count > 0)
                {
                    Cursor? literalCursor = child.CursorChildren[0];
                    string asString = ClangHelpers.GetLiteralAsString(literalCursor);

                    var value = 0;
                    if (!string.IsNullOrEmpty(asString) && !int.TryParse(asString, out value)) Logger.LogError($"      Couldn't resolve enum literal value [{asString}]");

                    enumMemberDeclaration = enumMemberDeclaration.WithEqualsValue(EqualsValueClause(IdentifierName(value.ToString())));
                }

                declSyntax = declSyntax.AddMembers(enumMemberDeclaration);
            }

            output.Enums.Add(declSyntax);
        }

        /// <summary>
        /// Unnamed enums are converted to global constants.
        /// </summary>
        private static void GenerateUnnamedEnum(RoslynCodeOutput output, Cursor cursor)
        {
            Logger.Log("    Generating unnamed enum");

            var defaultValue = 0; // We need to manually increment enum values
            foreach (Cursor? child in cursor.CursorChildren)
            {
                int value = defaultValue;
                defaultValue++;

                // Check if literal assignment.
                if (child.CursorChildren.Count > 0)
                {
                    Cursor? literalData = child.CursorChildren[0];
                    string asString = ClangHelpers.GetLiteralAsString(literalData);

                    if (!string.IsNullOrEmpty(asString) && !int.TryParse(asString, out value)) Logger.LogError($"      Couldn't resolve enum literal value [{asString}]");
                }

                // public const int ChildName = value
                LiteralExpressionSyntax assignmentExpr = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(value));
                string valueName = child.Spelling;
                SeparatedSyntaxList<VariableDeclaratorSyntax> variableExpression = SeparatedList(new[]
                {
                    VariableDeclarator(Identifier(valueName), null, EqualsValueClause(assignmentExpr))
                });
                FieldDeclarationSyntax fieldDeclaration = FieldDeclaration(VariableDeclaration(IdentifierName("int"), variableExpression));
                fieldDeclaration.MakePublic().MakeConst();

                output.GlobalConstants.Add(fieldDeclaration);
            }
        }

        private static void RecordStruct(RoslynCodeOutput output, Cursor cursor)
        {
            var recordDecl = (RecordDecl) cursor;

            string name = ClangHelpers.GetStructName(recordDecl);
            name = RoslynHelpers.FixReservedWords(name);

            Logger.Log($"    Analyzing struct {name}");
            output.RegisterStruct(name, recordDecl);

            // Record all fields in the struct.
            // Find struct dependencies.
            foreach (Cursor? child in cursor.CursorChildren)
            {
                if (child is not FieldDecl asField) // Nested struct
                    continue;

                ClangTypeMeta typeMeta = ClangTypeMeta.FromClangType(asField.Type);

                // If field is a struct type, add to dependencies.
                if (typeMeta.Kind == ClangTypeKind.Struct)
                {
                    output.RecordStructDependency(name, typeMeta.Name);
                    Logger.Log($"      Struct {name} depends on {typeMeta.Name}.");

                    if (typeMeta.IsArray)
                    {
                        Logger.Log($"   Struct {name} is marked as a class because it contains array of type {typeMeta.Name}");
                        output.PromoteStructToClass(name);
                        break;
                    }
                }

                if (typeMeta.Kind == ClangTypeKind.Function)
                {
                    Logger.Log($"   Struct {name} is marked as a class because it contains function pointers");
                    output.PromoteStructToClass(name);
                    break;
                }

                if (typeMeta.IsArray && typeMeta.ConstantArraySizes!.Length > 1)
                {
                    Logger.Log($"   Struct {name} is marked as a class because it contains multidimensional arrays");
                    output.PromoteStructToClass(name);
                    break;
                }
            }
        }

        private static void GenerateStructs(RoslynCodeOutput output)
        {
            Logger.Log("  Checking struct relations");
            foreach ((string? name, RecordDecl _) in output.EnumerateStructs())
            {
                // If a struct references a class, it should be promoted to a class as well.
                if (!output.IsStructAClass(name) && output.DependencyTreeContainsClass(name))
                {
                    Logger.Log($"   Struct {name} is marked as a class because it references a class");
                    output.PromoteStructToClass(name);
                }
            }

            Logger.Log("  Generating structs");
            foreach ((string? name, RecordDecl cursor) in output.EnumerateStructs())
            {
                GenerateStruct(output, name, cursor);
            }
        }

        private static void GenerateStruct(RoslynCodeOutput output, string name, RecordDecl cursor)
        {
            Logger.Log($"    Generating struct {name}");

            var union = false;
            TypeDeclarationSyntax typeDecl;
            if (output.IsStructAClass(name))
            {
                typeDecl = ClassDeclaration(name);
            }
            else if (cursor.CursorKind == CXCursorKind.CXCursor_UnionDecl)
            {
                typeDecl = StructDeclaration(name.Replace("union ", ""));
                AttributeArgumentListSyntax attributeArgument = ParseAttributeArgumentList("(LayoutKind.Explicit)");
                AttributeSyntax attribute = Attribute(ParseName("StructLayout"), attributeArgument);
                AttributeListSyntax attributeList = AttributeList(SeparatedList<AttributeSyntax>().Add(attribute));
                typeDecl = typeDecl.AddAttributeLists(attributeList);
                union = true;
            }
            else
            {
                typeDecl = StructDeclaration(name);
            }

            typeDecl = typeDecl.MakePublic().MakeUnsafe();
            typeDecl = GenerateStructMembers(output, name, cursor, typeDecl, union);

            output.Structs.Add(typeDecl);
        }

        private static TypeDeclarationSyntax GenerateStructMembers(RoslynCodeOutput output, string name, RecordDecl cursor, TypeDeclarationSyntax typeDecl, bool union)
        {
            var constructorStatements = new List<StatementSyntax>();
            foreach (Cursor? child in cursor.CursorChildren)
            {
                if (child is not FieldDecl asField) continue; // Nested struct, nested enum etc?

                Type? childType = asField.Type;
                string? childName = asField.Name;
                childName = RoslynHelpers.FixReservedWords(childName);

                ClangTypeMeta typeInfo = ClangTypeMeta.FromClangType(childType);
                bool enumType = childType.IsIntegralOrEnumerationType && typeInfo.Name.Contains("enum"); // Nested enum

                if (typeInfo.Kind == ClangTypeKind.Struct && !output.IsStructRegistered(typeInfo.Name) && !enumType || typeInfo.Name.Contains("unnamed "))
                {
                    // unnamed struct
                    string subName;
                    if (typeInfo.Name.Contains("unnamed "))
                        // Unnamed subtype
                        subName = "unnamed" + output.GetUnnamedIndex();
                    else
                        // Named subtype
                        subName = typeInfo.Name;

                    var sb = new StringBuilder();
                    var subIsUnion = false;
                    if (asField.Type.AsString.Contains("union "))
                    {
                        subIsUnion = true;
                        subName = subName.Replace("union ", string.Empty);
                        sb.Append("[StructLayout(LayoutKind.Explicit)]");
                    }

                    sb.Append("struct ");
                    sb.Append(subName);
                    sb.Append(" {}");

                    var subTypeDecl = ParseMemberDeclaration(sb.ToString()) as TypeDeclarationSyntax;
                    Debug.Assert(subTypeDecl != null);

                    Cursor? subCursor = child.CursorChildren[0];
                    subTypeDecl = GenerateStructMembers(output, subName, (RecordDecl) subCursor, subTypeDecl, subIsUnion);
                    subTypeDecl = subTypeDecl.MakePublic().MakeUnsafe();
                    typeDecl = typeDecl.AddMembers(subTypeDecl);

                    typeInfo = new ClangTypeMeta(ClangTypeKind.Struct, subName, typeInfo.PointerCount, typeInfo.ConstantArraySizes);
                }

                string? fieldDecl = null;
                bool fixedField = !output.IsStructAClass(name) && typeInfo.Kind == ClangTypeKind.Primitive && typeInfo.IsFixed;
                if (fixedField)
                {
                    fieldDecl = $"public fixed {typeInfo.GetRoslynName()} {childName}[{typeInfo.ConstantArraySizes![0]}];";
                }
                else if (!output.IsStructAClass(typeInfo.Name) || !typeInfo.IsArray)
                {
                    string declarationTypeName = typeInfo.GetRoslynName();

                    if (typeInfo.Kind == ClangTypeKind.Function)
                    {
                        output.RegisterFunctionType(typeInfo);
                        declarationTypeName = output.GetFunctionTypeAlias(declarationTypeName);
                    }

                    declarationTypeName = declarationTypeName.Replace("enum ", "");
                    declarationTypeName = declarationTypeName.Replace("union ", "");
                    fieldDecl = $"public {declarationTypeName} {childName}";

                    if (typeInfo.ConstantArraySizes != null && typeInfo.ConstantArraySizes.Length > 0)
                        // todo: implement
                        continue;
                }
                else if (typeInfo.IsArray)
                {
                    // Class array
                    string arrayTypeName = typeInfo.GetRoslynName();
                    string dimensions = typeInfo.GetArrayDimensionsString();
                    fieldDecl = $"public {arrayTypeName}[] {childName} = new {arrayTypeName}[{dimensions}];";

                    int d = typeInfo.ConstantArraySizes![0];
                    StatementSyntax stmt = ParseStatement(
                        $"for (var i = 0; i < {d}; i++)" +
                        "{{" +
                        $"	{childName}[i] = new {arrayTypeName}();" +
                        "}");

                    constructorStatements.Add(stmt);
                }

                if (union) fieldDecl = "[FieldOffset(0)]" + fieldDecl;

                fieldDecl = RoslynHelpers.EnsureSemicolonEnding(fieldDecl);
                Debug.Assert(fieldDecl != null);
                var fieldDecl2 = ParseMemberDeclaration(fieldDecl) as FieldDeclarationSyntax;
                Debug.Assert(fieldDecl2 != null);
                typeDecl = typeDecl.AddMembers(fieldDecl2);
            }

            // Add constructor if any constructor statements.
            if (constructorStatements.Count > 0)
            {
                ConstructorDeclarationSyntax constructor = ConstructorDeclaration(name).MakePublic();
                for (var i = 0; i < constructorStatements.Count; i++)
                {
                    StatementSyntax stmt = constructorStatements[i];
                    constructor = constructor.AddBodyStatements(stmt);
                }

                typeDecl = typeDecl.AddMembers(constructor);
            }

            return typeDecl;
        }

        private static void GenerateFunctionTypeDefs(RoslynCodeOutput output)
        {
            Logger.Log("  Generating function types");

            foreach (ClangTypeMeta functionType in output.EnumerateFunctionTypes())
            {
                Debug.Assert(functionType.Kind == ClangTypeKind.Function);

                string name = functionType.GetRoslynName();
                name = output.GetFunctionTypeAlias(name);

                ClangTypeMeta? returnType = functionType.FunctionReturnType;
                string returnTypeName = returnType!.GetRoslynName();
                if (returnType.Kind == ClangTypeKind.Function) returnTypeName = output.GetFunctionTypeAlias(returnTypeName);

                DelegateDeclarationSyntax decl = DelegateDeclaration(ParseTypeName(returnTypeName), name).MakePublic();

                if (functionType.FunctionArgTypes != null)
                    for (var i = 0; i < functionType.FunctionArgTypes.Length; ++i)
                    {
                        ClangTypeMeta arg = functionType.FunctionArgTypes[i];
                        string typeName = arg.Name;
                        if (arg.Kind == ClangTypeKind.Function) typeName = output.GetFunctionTypeAlias(typeName);

                        // (typeA arg0, typeB arg1...)
                        string argName = $"arg{i}";
                        decl = decl.AddParameterListParameters(Parameter(Identifier(argName)).WithType(ParseTypeName(typeName)));
                    }

                output.FunctionTypes.Add(decl);
            }
        }

        private static void GenerateGlobalVariable(RoslynCodeOutput output, VarDecl cursor)
        {
        }

        private static void GenerateFunctions(RoslynCodeOutput output)
        {
        }
    }
}