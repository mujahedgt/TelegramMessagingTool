namespace TelegramMessagingTool.Commands;

public sealed record ParsedCommand(string Command, string? BotUsername, string Arguments);

public static class CommandParser
{
    public static bool TryParse(string? text, out ParsedCommand parsedCommand)
    {
        parsedCommand = new ParsedCommand(string.Empty, null, string.Empty);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        string token;
        string arguments;
        int whitespaceIndex = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        if (whitespaceIndex < 0)
        {
            token = trimmed;
            arguments = string.Empty;
        }
        else
        {
            token = trimmed[..whitespaceIndex];
            arguments = trimmed[whitespaceIndex..].Trim();
        }

        if (token.Length <= 1)
        {
            return false;
        }

        string command = token;
        string? botUsername = null;
        int mentionIndex = token.IndexOf('@', StringComparison.Ordinal);
        if (mentionIndex >= 0)
        {
            command = token[..mentionIndex];
            botUsername = token[(mentionIndex + 1)..];
            if (string.IsNullOrWhiteSpace(botUsername))
            {
                return false;
            }
        }

        if (command.Length <= 1 || command.Any(char.IsWhiteSpace))
        {
            return false;
        }

        parsedCommand = new ParsedCommand(command.ToLowerInvariant(), botUsername, arguments);
        return true;
    }

    public static bool Matches(string? text, string commandName)
    {
        return TryParse(text, out ParsedCommand parsedCommand)
            && parsedCommand.Command.Equals(NormalizeCommandName(commandName), StringComparison.OrdinalIgnoreCase);
    }

    public static string GetArguments(string? text, string commandName)
    {
        return TryParse(text, out ParsedCommand parsedCommand)
            && parsedCommand.Command.Equals(NormalizeCommandName(commandName), StringComparison.OrdinalIgnoreCase)
                ? parsedCommand.Arguments
                : string.Empty;
    }

    private static string NormalizeCommandName(string commandName)
    {
        return commandName.StartsWith("/", StringComparison.Ordinal)
            ? commandName.ToLowerInvariant()
            : "/" + commandName.ToLowerInvariant();
    }
}
