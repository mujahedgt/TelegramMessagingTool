using System.Text;

namespace TelegramMessagingTool.Services;

public sealed class TelegramStreamEditService
{
    private const string DraftText = "Generating response...";
    private readonly Func<DateTimeOffset> _utcNow;

    public TelegramStreamEditService(Func<DateTimeOffset>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<string> RunAsync(
        Func<CancellationToken, Task<int>> sendDraftAsync,
        Func<int, string, CancellationToken, Task> editDraftAsync,
        Func<Func<string, CancellationToken, Task>, CancellationToken, Task<string>> generateAsync,
        TimeSpan editInterval,
        CancellationToken cancellationToken)
    {
        if (editInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(editInterval), "Edit interval must be positive.");
        }

        int draftMessageId = await sendDraftAsync(cancellationToken);
        var streamedText = new StringBuilder();
        string lastEditedText = DraftText;
        DateTimeOffset? lastEditAt = null;

        async Task OnDeltaAsync(string delta, CancellationToken token)
        {
            if (string.IsNullOrEmpty(delta))
            {
                return;
            }

            streamedText.Append(delta);
            DateTimeOffset now = _utcNow();
            if (lastEditAt is not null && now - lastEditAt.Value < editInterval)
            {
                return;
            }

            string nextText = TrimForTelegramEdit(streamedText.ToString());
            if (string.Equals(nextText, lastEditedText, StringComparison.Ordinal))
            {
                return;
            }

            await editDraftAsync(draftMessageId, nextText, token);
            lastEditedText = nextText;
            lastEditAt = now;
        }

        string finalAnswer = await generateAsync(OnDeltaAsync, cancellationToken);
        string finalEditText = TrimForTelegramEdit(finalAnswer);
        if (!string.Equals(finalEditText, lastEditedText, StringComparison.Ordinal))
        {
            await editDraftAsync(draftMessageId, finalEditText, cancellationToken);
        }

        return finalAnswer;
    }

    public static string TrimForTelegramEdit(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return DraftText;
        }

        string normalized = text.Trim();
        if (normalized.Length <= TelegramMessageFormatter.TelegramMessageLimit)
        {
            return normalized;
        }

        return normalized[..TelegramMessageFormatter.TelegramMessageLimit];
    }
}
