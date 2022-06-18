namespace SeaSharpener.Meta
{
    public static class Logger
    {
        public static void Log(string text)
        {
            Console.WriteLine(text);
        }

        public static void LogError(string text)
        {
            Console.Error.WriteLine(text);
        }
    }
}