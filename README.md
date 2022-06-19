# SeaSharpener

A C to C# code transpiler using LLVM's Clang and Roslyn.
It's more of a proof of concept really, but it works. Sadly the current C#
featureset doesn't allow for a full conversion, as some logic has no equivalent.

One example is that structs with function pointers or multidimensional arrays have to be converted to classes,
which are reference types and won't be copied when passed into a function.

For information on usage check the ExecTest project.

# Supports

Check the test files in the ExecTest project for examples. This list is a bit funny, but to fully translate everything you need to manually handle every CXCursorKind case.

- Structs
- Enums
    - Nameless enums
- Type aliases
- Includes
- Unions
    - Unions nested in structs
- Function pointers
- Most of the stdlib.h implemented
- If/Else
- For loops
- Goto
- Conditional ternary operator ?:
- Accessing struct members expressions
- Numerical and string literals
- Primitive and sturct variable declaration
- Static variable declaration
- Array indexing
- Break/Continue
- Simple binary operators
- CStyle casts

# Unsupported/ToDo

- Integral literal enum values
- Expression enum values
- Enums nested within structs
- Fixed arrays in structs
- Global variables
- Do/While
- Array variable declaration and initialization
- Prettify output

# Dependencies

https://github.com/dotnet/roslyn
https://github.com/dotnet/ClangSharp/

Inspired, and some code referenced from https://github.com/rds1983/Sichem