using TelegramMessagingTool.ConsoleUi;

namespace TelegramMessagingTool.Runtime;

public static class ConsoleEventWriter
{
    public static void Write(string label, string actor, string detail, ConsoleEventLevel level)
    {
        ConsoleColor originalColor = Console.ForegroundColor;
        Console.ForegroundColor = level switch
        {
            ConsoleEventLevel.Success => ConsoleColor.Green,
            ConsoleEventLevel.Warning => ConsoleColor.Yellow,
            ConsoleEventLevel.Error => ConsoleColor.Red,
            _ => ConsoleColor.Cyan
        };

        Console.WriteLine(AgentConsoleRenderer.RenderEvent(label, actor, detail, level));
        Console.ForegroundColor = originalColor;
    }
}
