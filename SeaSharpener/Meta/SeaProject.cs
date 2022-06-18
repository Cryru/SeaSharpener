namespace SeaSharpener.Meta
{
    public class SeaProject
    {
        /// <summary>
        /// The name of the conversion project.
        /// </summary>
        public string ProjectName = "Untitled";

        /// <summary>
        /// List of files to convert.
        /// </summary>
        public string[]? SourceFiles;

        /// <summary>
        /// List of defines to add to the compilation.
        /// </summary>
        public string[]? Defines;

        /// <summary>
        /// List of additional include directories to use
        /// when resolving includes.
        /// </summary>
        public string[]? IncludeDirectories;

        /// <summary>
        /// The directory to output converted files to.
        /// </summary>
        public string OutputDirectory = "Output-[ProjectName]";
    }
}