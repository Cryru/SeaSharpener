#region Using

using ClangSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SeaSharpener.Clang;

#endregion

namespace SeaSharpener.Roslyn
{
    public class RoslynCodeOutput
    {
        public List<EnumDeclarationSyntax> Enums = new();
        public List<FieldDeclarationSyntax> GlobalConstants = new();
        public List<TypeDeclarationSyntax> Structs = new();
        public List<DelegateDeclarationSyntax> FunctionTypes = new();

        private List<(string, RecordDecl)> _structs = new();
        private Dictionary<string, List<string>> _structDependencies = new();
        private HashSet<string> _classes = new();
        private int _unnamedCounter;

        private HashSet<ClangTypeMeta> _functionTypes = new();
        private Dictionary<string, string> _functionTypeAlias = new();

        public void RegisterStruct(string name, RecordDecl cursor)
        {
            _structs.Add((name, cursor));
        }

        public bool IsStructRegistered(string name)
        {
            for (var i = 0; i < _structs.Count; i++)
            {
                (string, RecordDecl) structData = _structs[i];
                if (structData.Item1 == name) return true;
            }

            return false;
        }

        public int GetUnnamedIndex()
        {
            int next = _unnamedCounter;
            _unnamedCounter++;
            return next;
        }

        public IEnumerable<(string, RecordDecl)> EnumerateStructs()
        {
            foreach ((string, RecordDecl) structData in _structs)
            {
                yield return structData;
            }
        }

        public void RecordStructDependency(string name, string dependsOn)
        {
            if (_structDependencies.TryGetValue(name, out List<string>? dependencies))
            {
                dependencies.Add(dependsOn);
                return;
            }

            var newList = new List<string> {dependsOn};
            _structDependencies.Add(name, newList);
        }

        public bool DependencyTreeContainsClass(string name)
        {
            if (IsStructAClass(name)) return true;

            if (_structDependencies.TryGetValue(name, out List<string>? dependencies))
                for (var i = 0; i < dependencies.Count; i++)
                {
                    string dependency = dependencies[i];
                    if (DependencyTreeContainsClass(dependency)) return true;
                }

            return false;
        }

        /// <summary>
        /// Structs that contain function references and/or arrays are promoted to C# classes
        /// due to limitations of the C# struct. Sadly this could mean difference in behavior due to them
        /// not being considered value types anymore.
        /// </summary>
        /// <param name="name"></param>
        public void PromoteStructToClass(string name)
        {
            _classes.Add(name);
        }

        public bool IsStructAClass(string name)
        {
            return _classes.Contains(name);
        }

        public void RegisterFunctionType(ClangTypeMeta meta)
        {
            if (_functionTypes.Contains(meta)) return;
            _functionTypes.Add(meta);
        }

        public string GetFunctionTypeAlias(string name)
        {
            if (_functionTypeAlias.TryGetValue(name, out string? val)) return val;

            string newAlias = $"delegateType{_functionTypeAlias.Count}";
            _functionTypeAlias.Add(name, newAlias);
            return newAlias;
        }

        public IEnumerable<ClangTypeMeta> EnumerateFunctionTypes()
        {
            foreach (ClangTypeMeta typeInfo in _functionTypes)
            {
                yield return typeInfo;
            }
        }
    }
}