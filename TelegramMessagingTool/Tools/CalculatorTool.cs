using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TelegramMessagingTool.Tools;

public sealed partial class CalculatorTool : IAgentTool
{
    public string Name => "calculator";

    public string Description => "Evaluates safe arithmetic expressions using numbers, parentheses, and + - * / %.";

    public bool RequiresApproval => false;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Task.FromResult(ToolResult.Fail("Usage: provide a math expression, for example: 25 * 19"));
        }

        string expression = input.Trim();
        if (expression.Length > 200)
        {
            return Task.FromResult(ToolResult.Fail("Expression is too long. Keep calculations under 200 characters."));
        }

        if (!SafeMathExpressionRegex().IsMatch(expression))
        {
            return Task.FromResult(ToolResult.Fail("Rejected: only numbers, spaces, decimal points, parentheses, and + - * / % are allowed."));
        }

        try
        {
            using var table = new DataTable { Locale = CultureInfo.InvariantCulture };
            object? value = table.Compute(expression, string.Empty);
            return Task.FromResult(ToolResult.Ok($"{expression} = {Convert.ToString(value, CultureInfo.InvariantCulture)}"));
        }
        catch (Exception ex) when (ex is EvaluateException or SyntaxErrorException or DivideByZeroException or FormatException)
        {
            return Task.FromResult(ToolResult.Fail($"Could not evaluate expression: {ex.Message}"));
        }
    }

    [GeneratedRegex("^[0-9\\s\\.\\+\\-\\*\\/\\%\\(\\)]+$")]
    private static partial Regex SafeMathExpressionRegex();
}
