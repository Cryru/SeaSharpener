﻿#region Using

using System.Diagnostics;
using ClangSharp;
using SeaSharpener.Clang;
using SeaSharpener.Meta;
using SeaSharpener.Roslyn;

#endregion

namespace SeaSharpener
{
    public static class Converter
    {
        public static bool Convert(SeaProject project)
        {
            var timer = Stopwatch.StartNew();

            Logger.Log($"Starting project {project.ProjectName}");

            string outputDirectory = project.OutputDirectory;
            outputDirectory = outputDirectory.Replace("//", "$");
            outputDirectory = outputDirectory.Replace("\\", "$");
            outputDirectory = outputDirectory.Replace('$', Path.DirectorySeparatorChar);
            outputDirectory = outputDirectory.Replace("[ProjectName]", project.ProjectName);
            project.OutputDirectory = outputDirectory;
            Directory.CreateDirectory(outputDirectory);

            string[]? fileList = project.SourceFiles;
            if (fileList == null || fileList.Length == 0)
            {
                Logger.LogError("No source files present.");
                return false;
            }

            for (var i = 0; i < fileList.Length; i++)
            {
                string fileName = fileList[i];
                if (!File.Exists(fileName))
                {
                    Logger.LogError($"File {fileName} not found.");
                    continue;
                }

                Logger.Log($"Converting file {fileName}");
                ConvertFile(project, fileName);
            }

            Logger.Log("Copying runtime");
            File.Copy(Path.Join("Runtime", "CRuntime.cs"), Path.Join(outputDirectory, "CRuntime.cs"), true);
            File.Copy(Path.Join("Runtime", "ProjectTemplate.csproj"), Path.Join(outputDirectory, $"{project.ProjectName}.csproj"), true);
            Logger.Log("Runtime copied.");

            Logger.Log($"Done in {timer.ElapsedMilliseconds}ms!");
            Logger.Log($"======================================");

            return true;
        }

        private static void ConvertFile(SeaProject project, string file)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            using var fileStream = new FileStream($"{project.OutputDirectory}{Path.DirectorySeparatorChar}{fileName}.cs", FileMode.Create, FileAccess.ReadWrite);
            using var sw = new StreamWriter(fileStream);

            sw.WriteLine("// Generated by SeaSharpener");
            sw.WriteLine($"// From: {file} @ {DateTime.Now}");
            sw.WriteLine();

            sw.WriteLine("using System;");
            sw.WriteLine("using System.Runtime.InteropServices;");
            sw.WriteLine("using static SeaSharpener.CRuntime;");
            sw.WriteLine();

            sw.WriteLine($"namespace {project.ProjectName}");
            sw.WriteLine("{");
            sw.WriteLine($"\tpublic unsafe partial class {project.ProjectName}Class");
            sw.WriteLine("\t{");

            // write file here
            TranslationUnit? translationUnit = ClangCompiler.Compile(project, file);
            if (translationUnit != null)
            {
                RoslynCodeOutput output = RoslynGenerator.Generate(project, translationUnit);
                RoslynWriter.WriteOutput(output, sw);
            }
            else
            {
                Logger.LogError("Code generation failed, compilation wasn't successful.");
            }

            sw.WriteLine("\t}");
            sw.WriteLine("}");
        }
    }
}