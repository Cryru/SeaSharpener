# SeaSharpener

A C to C# code transpiler using LLVM's Clang and Roslyn.
It's more of a proof of concept really, but it works.

For information on usage check the ExecTest project.

# Supports

Check the test files in the ExecTest project.

- Structs
- Enums
    - Nameless enums
- Type aliases
- Includes
- Unions
    - Unions nested in structs
- Function pointers
- Most of the stdlib.h implemented

# Unsupported/ToDo

- Integral literal enum values
- Expression enum values
- Enums nested within structs
- Fixed arrays in structs
- Global variables

# Dependencies

https://github.com/dotnet/roslyn
https://github.com/dotnet/ClangSharp/