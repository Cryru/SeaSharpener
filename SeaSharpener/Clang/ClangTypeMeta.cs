#region Using

using System.Diagnostics;
using System.Text;
using ClangSharp.Interop;
using Type = ClangSharp.Type;

#endregion

namespace SeaSharpener.Clang
{
    public enum ClangTypeKind
    {
        Enum,
        Primitive,
        Struct,
        Function,
    }

    /// <summary>
    /// A representation of a translated type.
    /// </summary>
    public class ClangTypeMeta
    {
        public string Name { get; protected set; } = "Unknown";
        public ClangTypeKind Kind { get; protected init; }
        public int PointerCount { get; protected init; }
        public int[]? ConstantArraySizes { get; protected set; }

        public ClangTypeMeta? FunctionReturnType { get; protected set; }
        public ClangTypeMeta[]? FunctionArgTypes { get; protected set; }

        public bool IsArray
        {
            get => ConstantArraySizes != null && ConstantArraySizes.Length > 0;
        }

        public bool IsFixed
        {
            get => ConstantArraySizes != null && ConstantArraySizes.Length == 1;
        }

        public ClangTypeMeta(ClangTypeKind kind, string name, int pointerCount, int[]? constantArraySizes)
        {
            Kind = kind;
            Name = name;
            PointerCount = pointerCount;
            ConstantArraySizes = constantArraySizes;
        }

        protected ClangTypeMeta()
        {
        }

        public string GetRoslynName()
        {
            var pointerSb = new StringBuilder();
            pointerSb.Append(Name);
            for (var i = 0; i < PointerCount; ++i)
            {
                pointerSb.Append("*");
            }

            return pointerSb.ToString();
            ;
        }

        public string GetArrayDimensionsString()
        {
            if (ConstantArraySizes == null) return "";

            var sb = new StringBuilder();
            for (var i = 0; i < ConstantArraySizes.Length; ++i)
            {
                sb.Append(ConstantArraySizes[i]);
                if (i < ConstantArraySizes.Length - 1) sb.Append(", ");
            }

            return sb.ToString();
        }

        // todo: maybe cache these?
        public static ClangTypeMeta FromCxType(CXType type)
        {
            var pointerCount = 0;
            var constantArraySizes = new List<int>();
            var kind = ClangTypeKind.Primitive;
            var run = true;
            while (run)
            {
                type = type.CanonicalType;

                string? primitiveType = ToPrimitiveType(type.kind);
                if (primitiveType != null) break;

                switch (type.kind)
                {
                    case CXTypeKind.CXType_Record:
                    {
                        kind = ClangTypeKind.Struct;
                        run = false;
                        break;
                    }

                    case CXTypeKind.CXType_IncompleteArray:
                    case CXTypeKind.CXType_ConstantArray:
                    {
                        kind = ClangTypeKind.Primitive;
                        constantArraySizes.Add((int) type.ArraySize);
                        type = clang.getArrayElementType(type);
                        pointerCount++;
                        continue;
                    }
                    case CXTypeKind.CXType_Pointer:
                    {
                        kind = ClangTypeKind.Primitive;
                        type = clang.getPointeeType(type);
                        pointerCount++;
                        continue;
                    }
                    case CXTypeKind.CXType_FunctionProto:
                        kind = ClangTypeKind.Function;
                        run = false;
                        break;
                    case CXTypeKind.CXType_Enum:
                        kind = ClangTypeKind.Enum;
                        run = false;
                        break;
                    default:
                        kind = ClangTypeKind.Primitive;
                        run = false;
                        break;
                }
            }

            var newMeta = new ClangTypeMeta
            {
                Kind = kind,
                PointerCount = pointerCount
            };
            if (constantArraySizes.Count > 0)
                newMeta.ConstantArraySizes = constantArraySizes.ToArray();

            switch (kind)
            {
                case ClangTypeKind.Enum:
                {
                    var name = clang.getTypeSpelling(type).ToString();
                    name = name.Replace("enum ", "");
                    newMeta.Name = name;
                    break;
                }
                case ClangTypeKind.Primitive:
                {
                    string? primitiveType = ToPrimitiveType(type.kind);
                    Debug.Assert(primitiveType != null);
                    newMeta.Name = primitiveType;
                    break;
                }
                case ClangTypeKind.Struct:
                {
                    var name = clang.getTypeSpelling(type).ToString();

                    // Const types don't exist in C#
                    bool constType = clang.isConstQualifiedType(type) != 0;
                    if (constType) name = name.Replace("const ", string.Empty);

                    name = name.Replace("struct ", string.Empty);
                    newMeta.Name = name;

                    break;
                }
                case ClangTypeKind.Function:
                {
                    ClangTypeMeta returnType = FromCxType(type.ResultType);
                    newMeta.FunctionReturnType = returnType;

                    var sb = new StringBuilder();
                    sb.Append(returnType.Name);
                    sb.Append("(");

                    var args = new List<ClangTypeMeta>();
                    for (var i = 0; i < type.NumArgTypes; i++)
                    {
                        // todo: could this loop around causing a stack overflow?
                        CXType arg = type.GetArgType((uint) i);
                        ClangTypeMeta typeMeta = FromCxType(arg);
                        args.Add(typeMeta);

                        sb.Append(typeMeta.Name);

                        if (i < type.NumArgTypes - 1) sb.Append(", ");
                    }

                    newMeta.FunctionArgTypes = args.ToArray();

                    sb.Append(")");
                    newMeta.Name = sb.ToString();

                    break;
                }
            }

            return newMeta;
        }

        public static ClangTypeMeta FromCursor(CXCursor cursor)
        {
            return FromCxType(cursor.Type);
        }

        public static ClangTypeMeta FromClangType(Type type)
        {
            return FromCxType(type.Handle);
        }

        private static string? ToPrimitiveType(CXTypeKind kind)
        {
            switch (kind)
            {
                case CXTypeKind.CXType_Bool:
                    return "bool";
                case CXTypeKind.CXType_UChar:
                case CXTypeKind.CXType_Char_U:
                    return "byte";
                case CXTypeKind.CXType_SChar:
                case CXTypeKind.CXType_Char_S:
                    return "sbyte";
                case CXTypeKind.CXType_UShort:
                    return "ushort";
                case CXTypeKind.CXType_Short:
                    return "short";
                case CXTypeKind.CXType_Float:
                    return "float";
                case CXTypeKind.CXType_Double:
                    return "double";
                case CXTypeKind.CXType_Long:
                case CXTypeKind.CXType_Int:
                    return "int";
                case CXTypeKind.CXType_ULong:
                case CXTypeKind.CXType_UInt:
                    return "uint";
                case CXTypeKind.CXType_LongLong:
                    return "long";
                case CXTypeKind.CXType_ULongLong:
                    return "ulong";
                case CXTypeKind.CXType_Void:
                    return "void";
            }

            return null;
        }
    }
}