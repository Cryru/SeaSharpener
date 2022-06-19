#region Using

using System.Diagnostics;
using SeaSharpener.Meta;

#endregion

namespace SeaSharpener.ExecTest
{
    public static class Program
    {
        public static void Main()
        {
            var mainWithPrint = new SeaProject
            {
                ProjectName = "MainWithPrint",
                SourceFiles = new[]
                {
                    "TestFiles/mainWithPrint.c"
                }
            };
            Converter.Convert(mainWithPrint);

            var referencing = new SeaProject
            {
                ProjectName = "Referencing",
                SourceFiles = new[]
                {
                    "TestFiles/headerReferencing.c"
                },
                IncludeDirectories = new[]
                {
                    "TestFiles",
                    "TestFiles/includeFolder", // Redundant, but adding for clarification.
                }
            };
            Converter.Convert(referencing);

            var enums = new SeaProject
            {
                ProjectName = "Enums",
                SourceFiles = new[]
                {
                    "TestFiles/enums.c"
                },
            };
            Converter.Convert(enums);

            var structs = new SeaProject
            {
                ProjectName = "Structs",
                SourceFiles = new[]
                {
                    "TestFiles/structs.c"
                },
            };
            Converter.Convert(structs);

            var functionPointers = new SeaProject
            {
                ProjectName = "FunctionPointers",
                SourceFiles = new[]
                {
                    "TestFiles/functionPointers.c"
                },
            };
            Converter.Convert(functionPointers);

            var other = new SeaProject
            {
                ProjectName = "Other",
                SourceFiles = new[]
                {
                    "TestFiles/other.c"
                },
            };
            Converter.Convert(other);

            DotNetBuild(mainWithPrint);
            DotNetBuild(referencing);
            DotNetBuild(enums);
            DotNetBuild(structs);
            DotNetBuild(functionPointers);
            DotNetBuild(other);
        }

        private static void DotNetBuild(SeaProject project)
        {
            var processStart = new ProcessStartInfo
            {
                WorkingDirectory = project.OutputDirectory,
                FileName = "dotnet",
                Arguments = "build"
            };
            Process.Start(processStart);
        }
    }
}