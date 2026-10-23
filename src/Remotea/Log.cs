namespace Remotea;

internal static class Log
{
    private static readonly object Gate = new();

    public static bool Verbose { get; set; }

    public static void Info(string message) => Write("INFO", message, ConsoleColor.Gray);

    public static void Warn(string message) => Write("WARN", message, ConsoleColor.Yellow);

    public static void Error(string message) => Write("ERR ", message, ConsoleColor.Red);

    public static void Debug(string message)
    {
        if (Verbose)
        {
            Write("DBG ", message, ConsoleColor.DarkGray);
        }
    }

    private static void Write(string level, string message, ConsoleColor color)
    {
        lock (Gate)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {level} {message}");
            Console.ResetColor();
        }
    }
}
