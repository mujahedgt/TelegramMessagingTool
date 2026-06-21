using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using TelegramMessagingTool.Models;
using UglyToad.PdfPig;

namespace TelegramMessagingTool.Services;

public sealed class DocumentStorageService
{
    private static readonly HashSet<string> DefaultAllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".md",
        ".json",
        ".csv",
        ".pdf",
        ".docx",
        ".xlsx"
    };

    private static readonly HashSet<string> TextBasedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".md",
        ".json",
        ".csv"
    };

    private readonly string _rootDirectory;
    private readonly long _maxFileBytes;
    private readonly IReadOnlySet<string> _allowedExtensions;

    public DocumentStorageService(string rootDirectory, long maxFileBytes = 100L * 1024 * 1024, IReadOnlySet<string>? allowedExtensions = null)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _maxFileBytes = maxFileBytes;
        _allowedExtensions = allowedExtensions ?? DefaultAllowedExtensions;
        Directory.CreateDirectory(_rootDirectory);
    }

    public string RootDirectory => _rootDirectory;

    public string AllowedExtensionsText => string.Join(", ", _allowedExtensions.OrderBy(x => x));

    public long MaxFileBytes => _maxFileBytes;

    public bool IsAllowedFileName(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        return !string.IsNullOrWhiteSpace(extension) && _allowedExtensions.Contains(extension);
    }

    public static string SanitizeFileName(string fileName)
    {
        string safeName = Path.GetFileName(fileName.Replace('\\', '/'));
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalid, '_');
        }

        safeName = safeName.Trim();
        return string.IsNullOrWhiteSpace(safeName) ? "document.txt" : safeName;
    }

    public string GetUserDirectory(long chatId)
    {
        string directory = Path.Combine(_rootDirectory, chatId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public Task<UploadedFile> CreateTextFileAsync(
        ConnectedUser user,
        string requestedFileName,
        string content,
        CancellationToken cancellationToken)
    {
        return CreateFileAsync(user, requestedFileName, content, cancellationToken);
    }

    public async Task<UploadedFile> CreateFileAsync(
        ConnectedUser user,
        string requestedFileName,
        string content,
        CancellationToken cancellationToken)
    {
        string safeName = SanitizeFileName(requestedFileName);
        if (!IsAllowedFileName(safeName))
        {
            throw new InvalidOperationException($"Unsupported file type. Allowed: {AllowedExtensionsText}");
        }

        string storedFileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}-{safeName}";
        string userDirectory = GetUserDirectory(user.ChatId);
        string absolutePath = Path.GetFullPath(Path.Combine(userDirectory, storedFileName));
        EnsureInsideRoot(absolutePath);

        string extension = Path.GetExtension(safeName).ToLowerInvariant();
        if (TextBasedExtensions.Contains(extension))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            EnsureSizeAllowed(bytes.LongLength, "File content");
            await File.WriteAllBytesAsync(absolutePath, bytes, cancellationToken);
        }
        else if (extension == ".docx")
        {
            CreateDocxFile(absolutePath, content);
        }
        else if (extension == ".xlsx")
        {
            CreateXlsxFile(absolutePath, content);
        }
        else if (extension == ".pdf")
        {
            await CreateSimplePdfFileAsync(absolutePath, content, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported file type. Allowed: {AllowedExtensionsText}");
        }

        long size = new FileInfo(absolutePath).Length;
        if (size > _maxFileBytes)
        {
            File.Delete(absolutePath);
            throw new InvalidOperationException($"File content is too large. Maximum allowed size is {_maxFileBytes} bytes.");
        }

        return new UploadedFile
        {
            ConnectedUserId = user.Id,
            ChatId = user.ChatId,
            OriginalFileName = safeName,
            StoredFileName = storedFileName,
            AbsolutePath = absolutePath,
            RelativePath = Path.GetRelativePath(_rootDirectory, absolutePath),
            ContentType = GetContentType(safeName),
            SizeBytes = size,
            Source = "created",
            CreatedAt = DateTime.UtcNow
        };
    }

    public async Task<UploadedFile> SaveUploadedFileAsync(
        ConnectedUser user,
        string originalFileName,
        string telegramFileId,
        string contentType,
        Stream source,
        long? fileSize,
        CancellationToken cancellationToken)
    {
        string safeName = SanitizeFileName(originalFileName);
        if (!IsAllowedFileName(safeName))
        {
            throw new InvalidOperationException($"Unsupported document type. Allowed: {AllowedExtensionsText}");
        }

        if (fileSize.HasValue)
        {
            EnsureSizeAllowed(fileSize.Value, "Document");
        }

        string storedFileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}-{safeName}";
        string userDirectory = GetUserDirectory(user.ChatId);
        string absolutePath = Path.GetFullPath(Path.Combine(userDirectory, storedFileName));
        EnsureInsideRoot(absolutePath);

        await using FileStream destination = File.Create(absolutePath);
        await source.CopyToAsync(destination, cancellationToken);

        long size = destination.Length;
        if (size > _maxFileBytes)
        {
            destination.Close();
            File.Delete(absolutePath);
            throw new InvalidOperationException($"Document is too large. Maximum allowed size is {_maxFileBytes} bytes.");
        }

        return new UploadedFile
        {
            ConnectedUserId = user.Id,
            ChatId = user.ChatId,
            OriginalFileName = safeName,
            StoredFileName = storedFileName,
            AbsolutePath = absolutePath,
            RelativePath = Path.GetRelativePath(_rootDirectory, absolutePath),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? GetContentType(safeName) : contentType,
            SizeBytes = size,
            Source = string.IsNullOrWhiteSpace(telegramFileId) ? "upload" : "telegram_upload",
            CreatedAt = DateTime.UtcNow
        };
    }

    public async Task<string> ExtractTextAsync(UploadedFile uploadedFile, CancellationToken cancellationToken, int maxCharacters = 12000)
    {
        string absolutePath = Path.GetFullPath(uploadedFile.AbsolutePath);
        EnsureInsideRoot(absolutePath);

        if (!File.Exists(absolutePath))
        {
            return "File is missing on disk.";
        }

        if (!IsAllowedFileName(uploadedFile.OriginalFileName))
        {
            return "Unsupported file type for text extraction.";
        }

        string extension = Path.GetExtension(uploadedFile.OriginalFileName).ToLowerInvariant();
        string text = extension switch
        {
            ".pdf" => ExtractPdfText(absolutePath),
            ".docx" => ExtractDocxText(absolutePath),
            ".xlsx" => ExtractXlsxText(absolutePath),
            _ => await File.ReadAllTextAsync(absolutePath, Encoding.UTF8, cancellationToken)
        };

        return Truncate(text, maxCharacters);
    }

    public FileDeletionResult DeleteStoredFile(UploadedFile uploadedFile)
    {
        try
        {
            string absolutePath = Path.GetFullPath(uploadedFile.AbsolutePath);
            EnsureInsideRoot(absolutePath);

            if (!File.Exists(absolutePath))
            {
                return FileDeletionResult.Ok("File was already missing on disk; database record was removed.");
            }

            File.Delete(absolutePath);
            return FileDeletionResult.Ok("File was removed from the document sandbox and database metadata was removed.");
        }
        catch (InvalidOperationException ex)
        {
            return FileDeletionResult.Failed($"Execution refused: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return FileDeletionResult.Failed($"Execution failed: could not delete file from disk. {ex.Message}");
        }
    }

    private static void CreateDocxFile(string absolutePath, string content)
    {
        using WordprocessingDocument document = WordprocessingDocument.Create(absolutePath, WordprocessingDocumentType.Document);
        MainDocumentPart mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());

        Body body = mainPart.Document.Body!;
        foreach (string line in SplitLines(content))
        {
            body.AppendChild(new Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text(line))));
        }

        mainPart.Document.Save();
    }

    private static void CreateXlsxFile(string absolutePath, string content)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Create(absolutePath, SpreadsheetDocumentType.Workbook);
        WorkbookPart workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        SheetData sheetData = new();
        worksheetPart.Worksheet = new Worksheet(sheetData);

        string[] lines = SplitLines(content).ToArray();
        if (lines.Length == 0)
        {
            lines = [string.Empty];
        }

        uint rowIndex = 1;
        foreach (string line in lines)
        {
            Row row = new() { RowIndex = rowIndex++ };
            string[] columns = line.Contains(',') ? line.Split(',') : [line];
            foreach (string column in columns)
            {
                row.AppendChild(new Cell
                {
                    DataType = CellValues.String,
                    CellValue = new CellValue(column.Trim())
                });
            }

            sheetData.AppendChild(row);
        }

        Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "Sheet1"
        });

        workbookPart.Workbook.Save();
    }

    private static async Task CreateSimplePdfFileAsync(string absolutePath, string content, CancellationToken cancellationToken)
    {
        string sanitized = string.Join("\n", SplitLines(content).Take(35));
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Created by TelegramMessagingTool";
        }

        StringBuilder textCommands = new();
        textCommands.AppendLine("BT");
        textCommands.AppendLine("/F1 12 Tf");
        textCommands.AppendLine("72 760 Td");
        bool first = true;
        foreach (string line in SplitLines(sanitized))
        {
            if (!first)
            {
                textCommands.AppendLine("0 -18 Td");
            }

            textCommands.AppendLine($"({EscapePdfText(line)}) Tj");
            first = false;
        }
        textCommands.AppendLine("ET");

        string stream = textCommands.ToString();
        byte[] streamBytes = Encoding.ASCII.GetBytes(stream);

        List<string> objects =
        [
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n",
            "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n",
            $"5 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n{stream}endstream\nendobj\n"
        ];

        await using FileStream file = File.Create(absolutePath);
        await WriteAsciiAsync(file, "%PDF-1.4\n", cancellationToken);
        List<long> offsets = [0];
        foreach (string obj in objects)
        {
            offsets.Add(file.Position);
            await WriteAsciiAsync(file, obj, cancellationToken);
        }

        long xrefOffset = file.Position;
        await WriteAsciiAsync(file, "xref\n0 6\n0000000000 65535 f \n", cancellationToken);
        for (int i = 1; i < offsets.Count; i++)
        {
            await WriteAsciiAsync(file, $"{offsets[i]:D10} 00000 n \n", cancellationToken);
        }

        await WriteAsciiAsync(file, $"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n", cancellationToken);
    }

    private static async Task WriteAsciiAsync(Stream stream, string text, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static string ExtractPdfText(string absolutePath)
    {
        StringBuilder text = new();
        using PdfDocument pdf = PdfDocument.Open(absolutePath);
        foreach (var page in pdf.GetPages())
        {
            text.AppendLine(page.Text);
        }

        return text.ToString().Trim();
    }

    private static string ExtractDocxText(string absolutePath)
    {
        using WordprocessingDocument document = WordprocessingDocument.Open(absolutePath, false);
        Body? body = document.MainDocumentPart?.Document?.Body;
        IEnumerable<DocumentFormat.OpenXml.Wordprocessing.Text> textNodes =
            body?.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>()
            ?? Enumerable.Empty<DocumentFormat.OpenXml.Wordprocessing.Text>();

        return string.Join(Environment.NewLine, textNodes.Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
    }

    private static string ExtractXlsxText(string absolutePath)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(absolutePath, false);
        WorkbookPart? workbookPart = document.WorkbookPart;
        if (workbookPart is null)
        {
            return string.Empty;
        }

        SharedStringTable? sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        StringBuilder text = new();
        foreach (WorksheetPart worksheetPart in workbookPart.WorksheetParts)
        {
            Worksheet? worksheet = worksheetPart.Worksheet;
            if (worksheet is null)
            {
                continue;
            }

            foreach (Row row in worksheet.Descendants<Row>())
            {
                List<string> values = [];
                foreach (Cell cell in row.Elements<Cell>())
                {
                    values.Add(GetCellText(cell, sharedStrings));
                }

                if (values.Count > 0)
                {
                    text.AppendLine(string.Join("\t", values));
                }
            }
        }

        return text.ToString().Trim();
    }

    private static string GetCellText(Cell cell, SharedStringTable? sharedStrings)
    {
        string rawValue = cell.CellValue?.Text ?? string.Empty;
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(rawValue, out int sharedStringIndex))
        {
            return sharedStrings?.ElementAtOrDefault(sharedStringIndex)?.InnerText ?? string.Empty;
        }

        return rawValue;
    }

    private void EnsureInsideRoot(string absolutePath)
    {
        string normalized = Path.GetFullPath(absolutePath);
        if (!normalized.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved file path escaped the document sandbox.");
        }
    }

    private void EnsureSizeAllowed(long sizeBytes, string label)
    {
        if (sizeBytes > _maxFileBytes)
        {
            throw new InvalidOperationException($"{label} is too large. Maximum allowed size is {_maxFileBytes} bytes.");
        }
    }

    private static string GetContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".md" => "text/markdown",
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "text/plain"
        };
    }

    private static IEnumerable<string> SplitLines(string content)
    {
        return content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    private static string EscapePdfText(string text)
    {
        StringBuilder escaped = new();
        foreach (char character in text)
        {
            escaped.Append(character switch
            {
                '(' => "\\(",
                ')' => "\\)",
                '\\' => "\\\\",
                >= ' ' and <= '~' => character,
                _ => ' '
            });
        }

        return escaped.ToString();
    }

    private static string Truncate(string text, int maxCharacters)
    {
        return text.Length <= maxCharacters ? text : text[..maxCharacters] + "\n...[truncated]";
    }
}


public sealed record FileDeletionResult(bool Success, string Message)
{
    public static FileDeletionResult Ok(string message) => new(true, message);

    public static FileDeletionResult Failed(string message) => new(false, message);
}
