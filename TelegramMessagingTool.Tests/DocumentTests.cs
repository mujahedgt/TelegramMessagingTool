using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool;
using TelegramMessagingTool.Agent;
using TelegramMessagingTool.Commands;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Tools;

using static TestAssert;

static class DocumentTests
{
    public static async Task RunDocumentMediaCommandTestsAsync(
        CommandRouter commandRouter,
        ConnectedUser testUser,
        ConnectedUser nonAdminUser,
        TelegramDbContext dbContext,
        DocumentStorageService documentStorage,
        ScriptedChatClient documentQaChatClient,
        ScriptedChatClient documentSummaryChatClient,
        BotSettings adminTestSettings,
        string importDirectory)
    {
        CommandResult createFileResult = await commandRouter.TryHandleAsync(TextMessage("/createfile notes.md This is a saved note"), testUser, dbContext, CancellationToken.None);
        AssertTrue(createFileResult.Handled, "/createfile is handled");
        AssertEqual(1, await dbContext.UploadedFiles.CountAsync(x => x.ConnectedUserId == testUser.Id), "/createfile saves file metadata");

        CommandResult filesResult = await commandRouter.TryHandleAsync(TextMessage("/files"), testUser, dbContext, CancellationToken.None);
        AssertTrue(filesResult.Handled, "/files is handled");
        AssertTrue(filesResult.ReplyText?.Contains("notes.md") == true, "/files lists created file");

        CommandResult emptyImagesResult = await commandRouter.TryHandleAsync(TextMessage("/images"), testUser, dbContext, CancellationToken.None);
        AssertTrue(emptyImagesResult.Handled, "/images is handled without saved images");
        AssertTrue(emptyImagesResult.ReplyText?.Contains("No images", StringComparison.OrdinalIgnoreCase) == true, "/images reports no saved images before image upload");

        int uploadedFileId = await dbContext.UploadedFiles
            .Where(x => x.ConnectedUserId == testUser.Id)
            .Select(x => x.Id)
            .SingleAsync();

        CommandResult readFileResult = await commandRouter.TryHandleAsync(TextMessage($"/readfile {uploadedFileId}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(readFileResult.Handled, "/readfile is handled");
        AssertTrue(readFileResult.ReplyText?.Contains("This is a saved note") == true, "/readfile returns file contents");

        string sandboxSiblingDirectory = documentStorage.RootDirectory + "_sibling";
        Directory.CreateDirectory(sandboxSiblingDirectory);
        string siblingFilePath = Path.Combine(sandboxSiblingDirectory, "outside.txt");
        await File.WriteAllTextAsync(siblingFilePath, "outside sandbox secret", CancellationToken.None);
        string escapedSandboxText = await documentStorage.ExtractTextAsync(new UploadedFile
        {
            ConnectedUserId = testUser.Id,
            ChatId = testUser.ChatId,
            OriginalFileName = "outside.txt",
            StoredFileName = "outside.txt",
            AbsolutePath = siblingFilePath,
            RelativePath = "..\\outside.txt",
            ContentType = "text/plain",
            Source = "test"
        }, CancellationToken.None);
        AssertTrue(escapedSandboxText.Contains("outside the current document sandbox", StringComparison.OrdinalIgnoreCase), "DocumentStorageService rejects paths that only share the sandbox prefix");

        CommandResult indexFileResult = await commandRouter.TryHandleAsync(TextMessage($"/indexfile {uploadedFileId}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(indexFileResult.Handled, "/indexfile is handled");
        AssertTrue(indexFileResult.ReplyText?.Contains("Chunks created") == true, "/indexfile reports chunk count");
        AssertTrue(await dbContext.DocumentChunks.AnyAsync(x => x.UploadedFileId == uploadedFileId), "/indexfile stores document chunks");

        CommandResult embedFileResult = await commandRouter.TryHandleAsync(TextMessage($"/embedfile {uploadedFileId}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(embedFileResult.Handled, "/embedfile is handled");
        AssertTrue(embedFileResult.ReplyText?.Contains("Embedded chunks", StringComparison.OrdinalIgnoreCase) == true, "/embedfile reports embedded chunk count");
        AssertTrue(await dbContext.DocumentChunks.AnyAsync(x => x.UploadedFileId == uploadedFileId && x.EmbeddingJson != null), "/embedfile stores document embeddings");

        CommandResult embedDocsResult = await commandRouter.TryHandleAsync(TextMessage("/embeddocs"), testUser, dbContext, CancellationToken.None);
        AssertTrue(embedDocsResult.Handled, "/embeddocs is handled");
        AssertTrue(embedDocsResult.ReplyText?.Contains("Embedded chunks", StringComparison.OrdinalIgnoreCase) == true, "/embeddocs reports embedded chunk count");

        CommandResult docChunksResult = await commandRouter.TryHandleAsync(TextMessage($"/docchunks {uploadedFileId}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(docChunksResult.Handled, "/docchunks is handled");
        AssertTrue(docChunksResult.ReplyText?.Contains("Chunks:") == true, "/docchunks reports index status");

        CommandResult askFileResult = await commandRouter.TryHandleAsync(TextMessage($"/askfile {uploadedFileId} what does the note say?"), testUser, dbContext, CancellationToken.None);
        AssertTrue(askFileResult.Handled, "/askfile is handled");
        AssertTrue(askFileResult.ReplyText?.Contains("payment deadline", StringComparison.OrdinalIgnoreCase) == true, "/askfile returns model answer grounded in chunks");

        CommandResult askDocsResult = await commandRouter.TryHandleAsync(TextMessage("/askdocs what saved note do I have?"), testUser, dbContext, CancellationToken.None);
        AssertTrue(askDocsResult.Handled, "/askdocs is handled");
        AssertTrue(askDocsResult.ReplyText?.Contains("saved note", StringComparison.OrdinalIgnoreCase) == true, "/askdocs returns model answer across documents");
        AssertTrue(documentQaChatClient.ModelTaskKinds.All(x => x == ModelTaskKind.DocumentQuestionAnswering), "Document Q&A uses document QA model route");

        CommandResult summarizeFileResult = await commandRouter.TryHandleAsync(TextMessage($"/summarizefile {uploadedFileId}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(summarizeFileResult.Handled, "/summarizefile is handled");
        AssertTrue(summarizeFileResult.ReplyText?.Contains("Summary", StringComparison.OrdinalIgnoreCase) == true, "/summarizefile returns a document summary");

        CommandResult summarizeDocsResult = await commandRouter.TryHandleAsync(TextMessage("/summarizedocs"), testUser, dbContext, CancellationToken.None);
        AssertTrue(summarizeDocsResult.Handled, "/summarizedocs is handled");
        AssertTrue(summarizeDocsResult.ReplyText?.Contains("indexed documents", StringComparison.OrdinalIgnoreCase) == true, "/summarizedocs returns an all-documents summary");
        AssertTrue(documentSummaryChatClient.ModelTaskKinds.All(x => x == ModelTaskKind.DocumentSummary), "Document summaries use document summary model route");

        await using var imageStream = new MemoryStream([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]);
        UploadedFile uploadedImage = await documentStorage.SaveUploadedFileAsync(
            testUser,
            "sample.png",
            "telegram-image-file-id",
            "image/png",
            imageStream,
            imageStream.Length,
            CancellationToken.None);
        dbContext.UploadedFiles.Add(uploadedImage);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        CommandResult imagesResult = await commandRouter.TryHandleAsync(TextMessage("/images"), testUser, dbContext, CancellationToken.None);
        AssertTrue(imagesResult.Handled, "/images is handled");
        AssertTrue(imagesResult.ReplyText?.Contains("sample.png") == true, "/images lists saved image files");
        AssertFalse(imagesResult.ReplyText?.Contains("notes.md") == true, "/images does not list non-image documents");

        CommandResult imagesMentionResult = await commandRouter.TryHandleAsync(TextMessage("/images@red_eye_ghost_bot"), testUser, dbContext, CancellationToken.None);
        AssertTrue(imagesMentionResult.Handled, "/images@bot is handled");
        AssertFalse((await commandRouter.TryHandleAsync(TextMessage("/imagesx"), testUser, dbContext, CancellationToken.None)).Handled, "/imagesx is not treated as /images");

        CommandResult invalidDescribeImageResult = await commandRouter.TryHandleAsync(TextMessage("/describeimage nope"), testUser, dbContext, CancellationToken.None);
        AssertTrue(invalidDescribeImageResult.Handled, "/describeimage invalid input is handled");
        AssertTrue(invalidDescribeImageResult.ReplyText?.Contains("Usage: /describeimage <image-file-id>") == true, "/describeimage validates image file id input");

        CommandResult nonImageDescribeImageResult = await commandRouter.TryHandleAsync(TextMessage($"/describeimage {uploadedFileId}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(nonImageDescribeImageResult.Handled, "/describeimage non-image input is handled");
        AssertTrue(nonImageDescribeImageResult.ReplyText?.Contains("not an image", StringComparison.OrdinalIgnoreCase) == true, "/describeimage rejects non-image documents");

        CommandResult describeImageResult = await commandRouter.TryHandleAsync(TextMessage($"/describeimage {uploadedImage.Id}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(describeImageResult.Handled, "/describeimage is handled");
        AssertTrue(describeImageResult.ReplyText?.Contains("sample.png") == true, "/describeimage reports image filename");
        AssertTrue(describeImageResult.ReplyText?.Contains("llama3.2-vision:11b") == true, "/describeimage reports configured image route");
        AssertTrue(describeImageResult.ReplyText?.Contains("Image vision is disabled", StringComparison.OrdinalIgnoreCase) == true, "/describeimage stays metadata-only when image vision is disabled");

        var fakeImageDescriptionService = new FakeImageDescriptionService("A small test image fixture.");
        const string customImagePrompt = "Focus on UI labels and visible text.";
        var visionEnabledDescribeImageCommand = new DescribeImageCommand(
            adminTestSettings with { EnableImageVision = true, ImageDescriptionPrompt = customImagePrompt },
            documentStorage,
            fakeImageDescriptionService);
        CommandResult visionDescribeImageResult = await visionEnabledDescribeImageCommand.TryHandleAsync(TextMessage($"/describeimage {uploadedImage.Id}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(visionDescribeImageResult.Handled, "/describeimage is handled when vision is enabled");
        AssertTrue(visionDescribeImageResult.ReplyText?.Contains("Description:") == true, "/describeimage returns a vision description when enabled");
        AssertTrue(visionDescribeImageResult.ReplyText?.Contains("A small test image fixture.") == true, "/describeimage includes image service output");
        AssertEqual(uploadedImage.Id, fakeImageDescriptionService.LastImageId, "/describeimage passes selected image to image service");
        AssertEqual(customImagePrompt, fakeImageDescriptionService.LastPrompt, "/describeimage passes configured image prompt to image service");
        AssertFalse((await commandRouter.TryHandleAsync(TextMessage("/describeimagex 1"), testUser, dbContext, CancellationToken.None)).Handled, "/describeimagex is not treated as /describeimage");

        var imagePromptChatClient = new ScriptedChatClient([
            "Prompt: polished dark red dashboard UI, dramatic lighting\nNegative prompt: blurry, unreadable text\nNotes: keep text legible and do not invent logos.",
            "Prompt: product photo of a black and red backend system mascot\nNegative prompt: distorted hands\nNotes: no copyrighted logos."
        ]);
        var imagePromptCommand = new ImagePromptCommand(
            adminTestSettings with { EnableImageVision = true },
            documentStorage,
            new ImagePromptService(imagePromptChatClient),
            fakeImageDescriptionService);
        CommandResult imageFilePromptResult = await imagePromptCommand.TryHandleAsync(
            TextMessage($"/imageprompt {uploadedImage.Id}"),
            testUser,
            dbContext,
            CancellationToken.None);
        AssertTrue(imageFilePromptResult.Handled, "/imageprompt image-file is handled");
        AssertTrue(imageFilePromptResult.ReplyText?.Contains("Image prompt for", StringComparison.OrdinalIgnoreCase) == true, "/imageprompt labels image-file prompt output");
        AssertTrue(imageFilePromptResult.ReplyText?.Contains("Prompt:", StringComparison.OrdinalIgnoreCase) == true, "/imageprompt returns generated prompt output");
        AssertTrue(imageFilePromptResult.ReplyText?.Contains("does not generate or send images", StringComparison.OrdinalIgnoreCase) == true, "/imageprompt makes non-generation behavior explicit");
        AssertEqual(uploadedImage.Id, fakeImageDescriptionService.LastImageId, "/imageprompt describes the selected image when image vision is enabled");
        AssertEqual(ModelTaskKind.Image, imagePromptChatClient.ModelTaskKinds.First(), "/imageprompt uses the image model route for image-file prompts");

        CommandResult ideaPromptResult = await imagePromptCommand.TryHandleAsync(
            TextMessage("/imageprompt black and red backend system mascot"),
            testUser,
            dbContext,
            CancellationToken.None);
        AssertTrue(ideaPromptResult.Handled, "/imageprompt idea is handled");
        AssertTrue(ideaPromptResult.ReplyText?.Contains("Image prompt draft", StringComparison.OrdinalIgnoreCase) == true, "/imageprompt labels idea prompt output");
        AssertTrue(ideaPromptResult.ReplyText?.Contains("product photo", StringComparison.OrdinalIgnoreCase) == true, "/imageprompt returns idea prompt output");
        AssertEqual(ModelTaskKind.Image, imagePromptChatClient.ModelTaskKinds.Last(), "/imageprompt uses the image model route for idea prompts");
        AssertFalse((await imagePromptCommand.TryHandleAsync(TextMessage("/imagepromptx idea"), testUser, dbContext, CancellationToken.None)).Handled, "/imagepromptx is not treated as /imageprompt");

        var fakeImageOcrService = new FakeImageOcrService("Visible text from image fixture.");
        var ocrImageDisabledCommand = new OcrImageCommand(
            adminTestSettings,
            documentStorage,
            fakeImageOcrService);
        CommandResult disabledOcrImageResult = await ocrImageDisabledCommand.TryHandleAsync(
            TextMessage($"/ocrimage {uploadedImage.Id}"),
            testUser,
            dbContext,
            CancellationToken.None);
        AssertTrue(disabledOcrImageResult.Handled, "/ocrimage is handled when disabled");
        AssertTrue(disabledOcrImageResult.ReplyText?.Contains("disabled", StringComparison.OrdinalIgnoreCase) == true, "/ocrimage reports disabled OCR gate");
        AssertEqual(0, fakeImageOcrService.CallCount, "/ocrimage does not call provider while disabled");

        var ocrImageEnabledCommand = new OcrImageCommand(
            adminTestSettings with { EnableImageOcr = true },
            documentStorage,
            fakeImageOcrService);
        CommandResult ocrImageResult = await ocrImageEnabledCommand.TryHandleAsync(
            TextMessage($"/ocrimage {uploadedImage.Id}"),
            testUser,
            dbContext,
            CancellationToken.None);
        AssertTrue(ocrImageResult.Handled, "/ocrimage enabled command is handled");
        AssertTrue(ocrImageResult.ReplyText?.Contains("OCR text", StringComparison.OrdinalIgnoreCase) == true, "/ocrimage returns OCR output label");
        AssertTrue(ocrImageResult.ReplyText?.Contains("Visible text from image fixture.") == true, "/ocrimage includes OCR provider output");
        AssertTrue(ocrImageResult.ReplyText?.Contains("Saved OCR text file", StringComparison.OrdinalIgnoreCase) == true, "/ocrimage reports saved OCR text document");
        AssertEqual(uploadedImage.Id, fakeImageOcrService.LastImageId, "/ocrimage passes selected image to OCR provider");
        UploadedFile savedOcrFile = (await dbContext.UploadedFiles
            .Where(x => x.ConnectedUserId == testUser.Id)
            .ToListAsync(CancellationToken.None))
            .Where(x => x.Source.Equals("ocr", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Id)
            .First();
        AssertTrue(savedOcrFile.OriginalFileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase), "/ocrimage stores OCR output as a sandboxed text document");
        string savedOcrText = await documentStorage.ExtractTextAsync(savedOcrFile, CancellationToken.None);
        AssertTrue(savedOcrText.Contains("Visible text from image fixture."), "/ocrimage persists OCR text content");
        CommandResult nonImageOcrResult = await ocrImageEnabledCommand.TryHandleAsync(
            TextMessage($"/ocrimage {uploadedFileId}"),
            testUser,
            dbContext,
            CancellationToken.None);
        AssertTrue(nonImageOcrResult.Handled, "/ocrimage non-image input is handled");
        AssertTrue(nonImageOcrResult.ReplyText?.Contains("not an image", StringComparison.OrdinalIgnoreCase) == true, "/ocrimage rejects non-image files");
        AssertFalse((await ocrImageEnabledCommand.TryHandleAsync(TextMessage("/ocrimagex 1"), testUser, dbContext, CancellationToken.None)).Handled, "/ocrimagex is not treated as /ocrimage");

        CommandResult emptyVoiceFilesResult = await commandRouter.TryHandleAsync(TextMessage("/voicefiles"), testUser, dbContext, CancellationToken.None);
        AssertTrue(emptyVoiceFilesResult.Handled, "/voicefiles is handled without saved audio");
        AssertTrue(emptyVoiceFilesResult.ReplyText?.Contains("No audio files", StringComparison.OrdinalIgnoreCase) == true, "/voicefiles reports no saved audio before upload");

        await using var audioStream = new MemoryStream([0x4F, 0x67, 0x67, 0x53, 0x00, 0x02]);
        UploadedFile uploadedAudio = await documentStorage.SaveUploadedFileAsync(
            testUser,
            "voice-note.ogg",
            "telegram-audio-file-id",
            "audio/ogg",
            audioStream,
            audioStream.Length,
            CancellationToken.None);
        dbContext.UploadedFiles.Add(uploadedAudio);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        CommandResult voiceFilesResult = await commandRouter.TryHandleAsync(TextMessage("/voicefiles"), testUser, dbContext, CancellationToken.None);
        AssertTrue(voiceFilesResult.Handled, "/voicefiles is handled");
        AssertTrue(voiceFilesResult.ReplyText?.Contains("voice-note.ogg") == true, "/voicefiles lists saved audio files");
        AssertFalse(voiceFilesResult.ReplyText?.Contains("sample.png") == true, "/voicefiles does not list image files");
        AssertFalse(voiceFilesResult.ReplyText?.Contains("notes.md") == true, "/voicefiles does not list non-audio documents");

        CommandResult invalidSendAudioResult = await commandRouter.TryHandleAsync(TextMessage("/sendaudio nope"), testUser, dbContext, CancellationToken.None);
        AssertTrue(invalidSendAudioResult.Handled, "/sendaudio invalid input is handled");
        AssertTrue(invalidSendAudioResult.ReplyText?.Contains("Usage: /sendaudio <audio-file-id>") == true, "/sendaudio validates audio file id input");

        CommandResult nonAudioSendAudioResult = await commandRouter.TryHandleAsync(TextMessage($"/sendaudio {uploadedImage.Id}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(nonAudioSendAudioResult.Handled, "/sendaudio non-audio input is handled");
        AssertTrue(nonAudioSendAudioResult.ReplyText?.Contains("not an audio", StringComparison.OrdinalIgnoreCase) == true, "/sendaudio rejects non-audio files");

        CommandResult sendAudioResult = await commandRouter.TryHandleAsync(TextMessage($"/sendaudio {uploadedAudio.Id}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(sendAudioResult.Handled, "/sendaudio is handled");
        AssertTrue(sendAudioResult.ReplyText?.Contains("voice-note.ogg") == true, "/sendaudio reports selected audio filename");
        AssertEqual(uploadedAudio.Id, sendAudioResult.AudioFile?.Id, "/sendaudio returns the selected audio file for Telegram delivery");
        AssertTrue(sendAudioResult.SendAudioAsVoice, "/sendaudio marks OGG/Opus-compatible audio for Telegram voice-note delivery");
        AssertFalse((await commandRouter.TryHandleAsync(TextMessage("/sendaudiox 1"), testUser, dbContext, CancellationToken.None)).Handled, "/sendaudiox is not treated as /sendaudio");

        CommandResult voiceFilesMentionResult = await commandRouter.TryHandleAsync(TextMessage("/voicefiles@red_eye_ghost_bot"), testUser, dbContext, CancellationToken.None);
        AssertTrue(voiceFilesMentionResult.Handled, "/voicefiles@bot is handled");
        AssertFalse((await commandRouter.TryHandleAsync(TextMessage("/voicefilesx"), testUser, dbContext, CancellationToken.None)).Handled, "/voicefilesx is not treated as /voicefiles");

        CommandResult readAudioResult = await commandRouter.TryHandleAsync(TextMessage($"/readfile {uploadedAudio.Id}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(readAudioResult.Handled, "/readfile audio file is handled");
        AssertTrue(readAudioResult.ReplyText?.Contains("Transcription is not implemented", StringComparison.OrdinalIgnoreCase) == true, "/readfile audio reports transcription placeholder safely");

        CommandResult invalidTranscribeResult = await commandRouter.TryHandleAsync(TextMessage("/transcribe nope"), testUser, dbContext, CancellationToken.None);
        AssertTrue(invalidTranscribeResult.Handled, "/transcribe invalid input is handled");
        AssertTrue(invalidTranscribeResult.ReplyText?.Contains("Usage: /transcribe <audio-file-id>") == true, "/transcribe validates audio file id input");

        CommandResult nonAudioTranscribeResult = await commandRouter.TryHandleAsync(TextMessage($"/transcribe {uploadedImage.Id}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(nonAudioTranscribeResult.Handled, "/transcribe non-audio input is handled");
        AssertTrue(nonAudioTranscribeResult.ReplyText?.Contains("not an audio", StringComparison.OrdinalIgnoreCase) == true, "/transcribe rejects non-audio files");

        CommandResult transcribeResult = await commandRouter.TryHandleAsync(TextMessage($"/transcribe {uploadedAudio.Id}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(transcribeResult.Handled, "/transcribe is handled");
        AssertTrue(transcribeResult.ReplyText?.Contains("voice-note.ogg") == true, "/transcribe reports audio filename");
        AssertTrue(transcribeResult.ReplyText?.Contains("qwen3:0.6b") == true, "/transcribe reports configured voice route fallback");
        AssertTrue(transcribeResult.ReplyText?.Contains("Audio transcription is disabled", StringComparison.OrdinalIgnoreCase) == true, "/transcribe stays metadata-only when audio transcription is disabled");

        CommandResult noProviderTranscribeResult = await new TranscribeCommand(
            adminTestSettings with { EnableAudioTranscription = true },
            documentStorage).TryHandleAsync(TextMessage($"/transcribe {uploadedAudio.Id}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(noProviderTranscribeResult.Handled, "/transcribe is handled when transcription is enabled without provider");
        AssertTrue(noProviderTranscribeResult.ReplyText?.Contains("no transcription provider is configured", StringComparison.OrdinalIgnoreCase) == true, "/transcribe reports missing provider when enabled");

        var fakeAudioTranscriptionService = new FakeAudioTranscriptionService("Transcript text from fixture.");
        var transcriptionEnabledCommand = new TranscribeCommand(
            adminTestSettings with { EnableAudioTranscription = true },
            documentStorage,
            fakeAudioTranscriptionService);
        CommandResult enabledTranscribeResult = await transcriptionEnabledCommand.TryHandleAsync(TextMessage($"/transcribe {uploadedAudio.Id}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(enabledTranscribeResult.Handled, "/transcribe is handled when transcription service is configured");
        AssertTrue(enabledTranscribeResult.ReplyText?.Contains("Transcript:") == true, "/transcribe returns transcript label when configured");
        AssertTrue(enabledTranscribeResult.ReplyText?.Contains("Transcript text from fixture.") == true, "/transcribe includes transcription service output");
        AssertEqual(uploadedAudio.Id, fakeAudioTranscriptionService.LastAudioId, "/transcribe passes selected audio to transcription service");
        AssertTrue(enabledTranscribeResult.ReplyText?.Contains("Saved transcript file:", StringComparison.OrdinalIgnoreCase) == true, "/transcribe reports saved transcript document");
        UploadedFile savedTranscriptFile = (await dbContext.UploadedFiles
            .Where(x => x.ConnectedUserId == testUser.Id)
            .ToListAsync(CancellationToken.None))
            .Where(x => x.OriginalFileName.Contains("voice-note.ogg-transcript", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Id)
            .First();
        AssertTrue(savedTranscriptFile.OriginalFileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase), "/transcribe stores transcript as a sandboxed text document");
        AssertEqual("transcript", savedTranscriptFile.Source, "/transcribe marks transcript document source");
        string savedTranscriptText = await documentStorage.ExtractTextAsync(savedTranscriptFile, CancellationToken.None);
        AssertTrue(savedTranscriptText.Contains("Transcript text from fixture."), "/transcribe persists transcript text content");

        var voiceBriefChatClient = new ScriptedChatClient([
            "Voice summary: fixture voice note. Tasks: review the saved transcript."
        ]);
        var voiceBriefCommand = new VoiceBriefCommand(
            adminTestSettings with { EnableAudioTranscription = true },
            documentStorage,
            new FakeAudioTranscriptionService("Direct voice brief transcript."),
            new TranscriptInsightsService(voiceBriefChatClient));
        CommandResult voiceBriefResult = await voiceBriefCommand.TryHandleAsync(
            TextMessage($"/voicebrief {uploadedAudio.Id}"),
            testUser,
            dbContext,
            CancellationToken.None);
        AssertTrue(voiceBriefResult.Handled, "/voicebrief is handled");
        AssertTrue(voiceBriefResult.ReplyText?.Contains("Voice brief for", StringComparison.OrdinalIgnoreCase) == true, "/voicebrief labels direct audio brief output");
        AssertTrue(voiceBriefResult.ReplyText?.Contains("Voice summary", StringComparison.OrdinalIgnoreCase) == true, "/voicebrief returns transcript insight output");
        AssertTrue(voiceBriefResult.ReplyText?.Contains("Saved transcript file", StringComparison.OrdinalIgnoreCase) == true, "/voicebrief saves a transcript document for follow-up");
        AssertEqual(ModelTaskKind.Voice, voiceBriefChatClient.ModelTaskKinds.Single(), "/voicebrief uses the voice model route");

        var voicePlanChatClient = new ScriptedChatClient([
            "Proposed title: Direct voice plan\nDraft task list:\n- Review the voice plan\nSuggested /plan command: /plan Review direct voice plan\nMissing information: none"
        ]);
        var voicePlanCommand = new VoicePlanCommand(
            adminTestSettings with { EnableAudioTranscription = true },
            documentStorage,
            new FakeAudioTranscriptionService("Direct voice plan transcript."),
            new TranscriptInsightsService(voicePlanChatClient));
        CommandResult voicePlanResult = await voicePlanCommand.TryHandleAsync(
            TextMessage($"/voiceplan {uploadedAudio.Id}"),
            testUser,
            dbContext,
            CancellationToken.None);
        AssertTrue(voicePlanResult.Handled, "/voiceplan is handled");
        AssertTrue(voicePlanResult.ReplyText?.Contains("Voice plan draft", StringComparison.OrdinalIgnoreCase) == true, "/voiceplan labels direct audio plan output");
        AssertTrue(voicePlanResult.ReplyText?.Contains("Suggested /plan command", StringComparison.OrdinalIgnoreCase) == true, "/voiceplan returns suggested plan command output");
        AssertTrue(voicePlanResult.ReplyText?.Contains("No task was created automatically", StringComparison.OrdinalIgnoreCase) == true, "/voiceplan keeps plan creation review-only");
        AssertEqual(ModelTaskKind.Voice, voicePlanChatClient.ModelTaskKinds.Single(), "/voiceplan uses the voice model route");
        AssertFalse((await voiceBriefCommand.TryHandleAsync(TextMessage("/voicebriefx 1"), testUser, dbContext, CancellationToken.None)).Handled, "/voicebriefx is not treated as /voicebrief");
        AssertFalse((await voicePlanCommand.TryHandleAsync(TextMessage("/voiceplanx 1"), testUser, dbContext, CancellationToken.None)).Handled, "/voiceplanx is not treated as /voiceplan");

        var transcriptInsightsChatClient = new ScriptedChatClient([
            "Voice summary: user discussed the fixture transcript. Tasks: follow up on the action item."
        ]);
        var transcriptInsightsCommand = new TranscriptInsightsCommand(
            documentStorage,
            new TranscriptInsightsService(transcriptInsightsChatClient));
        CommandResult transcriptInsightsResult = await transcriptInsightsCommand.TryHandleAsync(
            TextMessage($"/transcriptinsights {savedTranscriptFile.Id}"),
            testUser,
            dbContext,
            CancellationToken.None);
        AssertTrue(transcriptInsightsResult.Handled, "/transcriptinsights is handled");
        AssertTrue(transcriptInsightsResult.ReplyText?.Contains("Voice summary", StringComparison.OrdinalIgnoreCase) == true, "/transcriptinsights returns voice summary output");
        AssertTrue(transcriptInsightsResult.ReplyText?.Contains("Tasks", StringComparison.OrdinalIgnoreCase) == true, "/transcriptinsights returns task extraction output");
        AssertEqual(ModelTaskKind.Voice, transcriptInsightsChatClient.ModelTaskKinds.Single(), "/transcriptinsights uses the voice model route");

        var transcriptTasksChatClient = new ScriptedChatClient([
            "Proposed title: Follow up on fixture transcript\nDraft task list:\n- Review the action item\nSuggested /plan command: /plan Follow up on fixture transcript action item\nMissing information: deadline"
        ]);
        var transcriptTasksCommand = new TranscriptTasksCommand(
            documentStorage,
            new TranscriptInsightsService(transcriptTasksChatClient));
        CommandResult transcriptTasksResult = await transcriptTasksCommand.TryHandleAsync(
            TextMessage($"/transcripttasks {savedTranscriptFile.Id}"),
            testUser,
            dbContext,
            CancellationToken.None);
        AssertTrue(transcriptTasksResult.Handled, "/transcripttasks is handled");
        AssertTrue(transcriptTasksResult.ReplyText?.Contains("Proposed title", StringComparison.OrdinalIgnoreCase) == true, "/transcripttasks returns proposed title output");
        AssertTrue(transcriptTasksResult.ReplyText?.Contains("Suggested /plan command", StringComparison.OrdinalIgnoreCase) == true, "/transcripttasks returns suggested plan command output");
        AssertTrue(transcriptTasksResult.ReplyText?.Contains("No task was created automatically", StringComparison.OrdinalIgnoreCase) == true, "/transcripttasks makes draft-only behavior explicit");
        AssertEqual(ModelTaskKind.Voice, transcriptTasksChatClient.ModelTaskKinds.Single(), "/transcripttasks uses the voice model route");
        List<AgentTask> tasksAfterTranscriptDraft = await dbContext.AgentTasks
            .Where(x => x.ConnectedUserId == testUser.Id)
            .ToListAsync(CancellationToken.None);
        AssertFalse(tasksAfterTranscriptDraft.Any(x => x.Goal.Contains("fixture transcript", StringComparison.OrdinalIgnoreCase)), "/transcripttasks does not create database tasks automatically");
        CommandResult invalidTranscriptTasksResult = await transcriptTasksCommand.TryHandleAsync(
            TextMessage("/transcripttasks nope"),
            testUser,
            dbContext,
            CancellationToken.None);
        AssertTrue(invalidTranscriptTasksResult.Handled, "/transcripttasks invalid input is handled");
        AssertTrue(invalidTranscriptTasksResult.ReplyText?.Contains("Usage: /transcripttasks <transcript-file-id>") == true, "/transcripttasks validates transcript file id input");
        CommandResult nonTranscriptTasksResult = await transcriptTasksCommand.TryHandleAsync(
            TextMessage($"/transcripttasks {uploadedAudio.Id}"),
            testUser,
            dbContext,
            CancellationToken.None);
        AssertTrue(nonTranscriptTasksResult.Handled, "/transcripttasks handles non-transcript file ids");
        AssertTrue(nonTranscriptTasksResult.ReplyText?.Contains("transcript", StringComparison.OrdinalIgnoreCase) == true, "/transcripttasks rejects non-transcript files clearly");
        AssertFalse((await transcriptTasksCommand.TryHandleAsync(TextMessage("/transcripttasksx 1"), testUser, dbContext, CancellationToken.None)).Handled, "/transcripttasksx is not treated as /transcripttasks");

        CommandResult nonTranscriptInsightsResult = await transcriptInsightsCommand.TryHandleAsync(
            TextMessage($"/transcriptinsights {uploadedAudio.Id}"),
            testUser,
            dbContext,
            CancellationToken.None);
        AssertTrue(nonTranscriptInsightsResult.Handled, "/transcriptinsights handles non-transcript file ids");
        AssertTrue(nonTranscriptInsightsResult.ReplyText?.Contains("transcript", StringComparison.OrdinalIgnoreCase) == true, "/transcriptinsights rejects non-transcript files clearly");

        var fakeTextToSpeechService = new FakeTextToSpeechService([0x49, 0x44, 0x33], ".mp3");
        var speakTextDisabledCommand = new SpeakTextCommand(
            adminTestSettings,
            documentStorage,
            fakeTextToSpeechService);
        CommandResult speakTextDisabledResult = await speakTextDisabledCommand.TryHandleAsync(
            TextMessage("/speaktext Hello from TTS"),
            testUser,
            dbContext,
            CancellationToken.None);
        AssertTrue(speakTextDisabledResult.Handled, "/speaktext is handled when disabled");
        AssertTrue(speakTextDisabledResult.ReplyText?.Contains("disabled", StringComparison.OrdinalIgnoreCase) == true, "/speaktext reports disabled TTS gate");
        AssertEqual(0, fakeTextToSpeechService.CallCount, "/speaktext does not call provider while disabled");

        var speakTextEnabledCommand = new SpeakTextCommand(
            adminTestSettings with { EnableTextToSpeech = true },
            documentStorage,
            fakeTextToSpeechService);
        CommandResult speakTextResult = await speakTextEnabledCommand.TryHandleAsync(
            TextMessage("/speaktext Hello from TTS"),
            testUser,
            dbContext,
            CancellationToken.None);
        AssertTrue(speakTextResult.Handled, "/speaktext enabled command is handled");
        AssertTrue(speakTextResult.ReplyText?.Contains("Saved TTS audio file:", StringComparison.OrdinalIgnoreCase) == true, "/speaktext reports saved TTS audio file");
        AssertTrue(speakTextResult.ReplyText?.Contains("not sent automatically", StringComparison.OrdinalIgnoreCase) == true, "/speaktext makes the output-storage gate explicit");
        AssertEqual("Hello from TTS", fakeTextToSpeechService.LastText, "/speaktext passes requested text to TTS provider");
        UploadedFile savedTtsFile = (await dbContext.UploadedFiles
            .Where(x => x.ConnectedUserId == testUser.Id)
            .ToListAsync(CancellationToken.None))
            .Where(x => x.Source.Equals("tts", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Id)
            .First();
        AssertTrue(savedTtsFile.OriginalFileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase), "/speaktext stores TTS output as an audio document");
        AssertTrue(DocumentStorageService.IsAudioFileName(savedTtsFile.OriginalFileName), "/speaktext saved output is listed as audio-capable");
        AssertTrue(File.Exists(savedTtsFile.AbsolutePath), "/speaktext writes generated audio bytes into the sandbox");
        AssertEqual(3L, new FileInfo(savedTtsFile.AbsolutePath).Length, "/speaktext stores provider bytes without sending audio automatically");

        var voiceReplyChatClient = new ScriptedChatClient(["This is my spoken reply."]);
        var voiceMessageProcessor = new VoiceMessageProcessor(
            adminTestSettings with { EnableAudioTranscription = true, EnableTextToSpeech = true },
            documentStorage,
            new ToolRegistry(Array.Empty<IAgentTool>()),
            new AgentRunner(voiceReplyChatClient, new ToolRegistry(Array.Empty<IAgentTool>()), searchRoutingClassifier: new OffSearchRoutingClassifier()),
            new ConversationService(),
            new FakeAudioTranscriptionService("Please answer this voice note."),
            new FakeTextToSpeechService([0x4F, 0x67, 0x67, 0x53], ".ogg"));
        VoiceMessageProcessResult voiceReplyResult = await voiceMessageProcessor.ProcessAsync(
            uploadedAudio,
            testUser,
            dbContext,
            CancellationToken.None);
        AssertTrue(voiceReplyResult.ReplyText.Contains("spoken reply", StringComparison.OrdinalIgnoreCase), "VoiceMessageProcessor returns the assistant text reply");
        AssertTrue(voiceReplyResult.ReplyAudioFile is not null, "VoiceMessageProcessor creates a TTS reply audio file when configured");
        AssertTrue(voiceReplyResult.SendReplyAudioAsVoice, "VoiceMessageProcessor marks OGG/Opus-compatible output for Telegram voice delivery");
        AssertEqual("tts_voice_reply", voiceReplyResult.ReplyAudioFile!.Source, "VoiceMessageProcessor stores generated voice replies with source metadata");
        List<UploadedFile> uploadedFilesAfterVoiceReply = await dbContext.UploadedFiles
            .Where(x => x.ConnectedUserId == testUser.Id)
            .ToListAsync(CancellationToken.None);
        List<ChatMessage> messagesAfterVoiceReply = await dbContext.Messages
            .Where(x => x.ConnectedUserId == testUser.Id)
            .ToListAsync(CancellationToken.None);
        AssertTrue(uploadedFilesAfterVoiceReply.Any(x => x.Source == "transcript" && x.OriginalFileName.Contains("voice-note.ogg-transcript", StringComparison.OrdinalIgnoreCase)), "VoiceMessageProcessor saves the inbound voice transcript as a sandboxed document");
        AssertTrue(messagesAfterVoiceReply.Any(x => x.Role == ChatRoles.User && x.Content.Contains("Please answer this voice note", StringComparison.OrdinalIgnoreCase)), "VoiceMessageProcessor stores the voice transcript in chat history");
        AssertTrue(messagesAfterVoiceReply.Any(x => x.Role == ChatRoles.Assistant && x.Content.Contains("spoken reply", StringComparison.OrdinalIgnoreCase)), "VoiceMessageProcessor stores the assistant reply in chat history");

        string transcriptScriptPath = Path.Combine(documentStorage.RootDirectory, "fake-transcribe.ps1");
        await File.WriteAllTextAsync(transcriptScriptPath, "param([string]$AudioPath)\nWrite-Output \"Transcript from local provider for $(Split-Path -Leaf $AudioPath)\"", CancellationToken.None);
        string powershellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powershellPath))
        {
            powershellPath = "powershell.exe";
        }

        var localAudioTranscriptionService = new LocalCommandAudioTranscriptionService(
            powershellPath,
            $"-NoProfile -ExecutionPolicy Bypass -File \"{transcriptScriptPath}\" \"{{file}}\"",
            timeout: TimeSpan.FromSeconds(30));
        AudioTranscriptionResult localTranscriptionResult = await localAudioTranscriptionService.TranscribeAsync(uploadedAudio, CancellationToken.None);
        AssertTrue(localTranscriptionResult.Success, "LocalCommandAudioTranscriptionService reports success for zero-exit local provider");
        AssertTrue(localTranscriptionResult.Output.Contains("Transcript from local provider", StringComparison.OrdinalIgnoreCase), "LocalCommandAudioTranscriptionService captures provider stdout transcript");
        AssertTrue(localTranscriptionResult.Output.Contains("voice-note.ogg", StringComparison.OrdinalIgnoreCase), "LocalCommandAudioTranscriptionService passes the selected audio file path to the provider");

        string ocrScriptPath = Path.Combine(documentStorage.RootDirectory, "fake-ocr.ps1");
        await File.WriteAllTextAsync(ocrScriptPath, "param([string]$ImagePath)\nWrite-Output \"OCR from local provider for $(Split-Path -Leaf $ImagePath)\"", CancellationToken.None);
        var localImageOcrService = new LocalCommandImageOcrService(
            powershellPath,
            $"-NoProfile -ExecutionPolicy Bypass -File \"{ocrScriptPath}\" \"{{file}}\"",
            timeout: TimeSpan.FromSeconds(30));
        ImageOcrResult localOcrResult = await localImageOcrService.ExtractTextAsync(uploadedImage, CancellationToken.None);
        AssertTrue(localOcrResult.Success, "LocalCommandImageOcrService reports success for zero-exit local provider");
        AssertTrue(localOcrResult.Output.Contains("OCR from local provider", StringComparison.OrdinalIgnoreCase), "LocalCommandImageOcrService captures provider stdout OCR text");
        AssertTrue(localOcrResult.Output.Contains("sample.png", StringComparison.OrdinalIgnoreCase), "LocalCommandImageOcrService passes the selected image file path to the provider");

        AssertFalse((await commandRouter.TryHandleAsync(TextMessage("/transcribex 1"), testUser, dbContext, CancellationToken.None)).Handled, "/transcribex is not treated as /transcribe");

        UploadedFile fileBeforeDelete = await dbContext.UploadedFiles.FirstAsync(x => x.Id == uploadedFileId, CancellationToken.None);
        string filePathBeforeDelete = fileBeforeDelete.AbsolutePath;
        AssertTrue(File.Exists(filePathBeforeDelete), "/deletefile test file exists before deletion approval");

        CommandResult invalidDeleteFileResult = await commandRouter.TryHandleAsync(TextMessage("/deletefile nope"), testUser, dbContext, CancellationToken.None);
        AssertTrue(invalidDeleteFileResult.Handled, "/deletefile invalid input is handled");
        AssertTrue(invalidDeleteFileResult.ReplyText?.Contains("Usage: /deletefile <file id>") == true, "/deletefile validates file id input");

        CommandResult nonAdminDeleteFileResult = await commandRouter.TryHandleAsync(TextMessage($"/deletefile {uploadedFileId}"), nonAdminUser, dbContext, CancellationToken.None);
        AssertTrue(nonAdminDeleteFileResult.Handled, "/deletefile non-admin attempt is handled");
        AssertTrue(nonAdminDeleteFileResult.ReplyText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "/deletefile requires admin");

        CommandResult deleteFileResult = await commandRouter.TryHandleAsync(TextMessage($"/deletefile {uploadedFileId}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(deleteFileResult.Handled, "/deletefile is handled");
        AssertTrue(deleteFileResult.ReplyText?.Contains("approval request", StringComparison.OrdinalIgnoreCase) == true, "/deletefile creates approval request");
        PendingAction deleteFilePendingAction = await dbContext.PendingActions.SingleAsync(x => x.ToolName == "delete_file", CancellationToken.None);
        AssertEqual("high", deleteFilePendingAction.RiskLevel, "/deletefile creates high risk pending action");
        AssertTrue(deleteFilePendingAction.PayloadJson.Contains(uploadedFileId.ToString()), "/deletefile stores target file id in payload");
        AssertTrue(File.Exists(filePathBeforeDelete), "/deletefile does not delete before approval");

        CommandResult approveDeleteFileResult = await commandRouter.TryHandleAsync(TextMessage($"/approve {deleteFilePendingAction.Id}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(approveDeleteFileResult.Handled, "/approve delete_file is handled");
        AssertTrue(approveDeleteFileResult.ReplyText?.Contains("Execution result", StringComparison.OrdinalIgnoreCase) == true, "/approve delete_file reports execution result");
        AssertFalse(File.Exists(filePathBeforeDelete), "/approve delete_file removes file from disk");
        AssertFalse(await dbContext.UploadedFiles.AnyAsync(x => x.Id == uploadedFileId, CancellationToken.None), "/approve delete_file removes file metadata");
        AssertFalse(await dbContext.DocumentChunks.AnyAsync(x => x.UploadedFileId == uploadedFileId, CancellationToken.None), "/approve delete_file removes indexed chunks");

        await File.WriteAllTextAsync(Path.Combine(importDirectory, "large-notes.md"), "Imported document content", CancellationToken.None);
        CommandResult importFilesResult = await commandRouter.TryHandleAsync(TextMessage("/importfiles"), testUser, dbContext, CancellationToken.None);
        AssertTrue(importFilesResult.Handled, "/importfiles is handled");
        AssertTrue(importFilesResult.ReplyText?.Contains("large-notes.md") == true, "/importfiles lists ImportInbox file");

        CommandResult nonAdminImportFileResult = await commandRouter.TryHandleAsync(TextMessage("/importfile large-notes.md"), nonAdminUser, dbContext, CancellationToken.None);
        AssertTrue(nonAdminImportFileResult.Handled, "/importfile non-admin attempt is handled");
        AssertTrue(nonAdminImportFileResult.ReplyText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "/importfile requires admin");

        CommandResult traversalImportFileResult = await commandRouter.TryHandleAsync(TextMessage("/importfile ../large-notes.md"), testUser, dbContext, CancellationToken.None);
        AssertTrue(traversalImportFileResult.Handled, "/importfile traversal attempt is handled");
        AssertTrue(traversalImportFileResult.ReplyText?.Contains("plain filename", StringComparison.OrdinalIgnoreCase) == true, "/importfile rejects paths");

        CommandResult importFileResult = await commandRouter.TryHandleAsync(TextMessage("/importfile large-notes.md"), testUser, dbContext, CancellationToken.None);
        AssertTrue(importFileResult.Handled, "/importfile is handled");
        AssertTrue(importFileResult.ReplyText?.Contains("Imported file", StringComparison.OrdinalIgnoreCase) == true, "/importfile reports imported file");
        UploadedFile importedFile = await dbContext.UploadedFiles.SingleAsync(x => x.OriginalFileName == "large-notes.md", CancellationToken.None);
        AssertEqual("local_import", importedFile.Source, "/importfile stores local import source");
        AssertTrue(File.Exists(importedFile.AbsolutePath), "/importfile copies file into sandbox");
        AssertTrue(importedFile.AbsolutePath.StartsWith(documentStorage.RootDirectory, StringComparison.OrdinalIgnoreCase), "/importfile stores file under document sandbox");

        UploadedFile legacyOutsideSandboxFile = new()
        {
            ConnectedUserId = testUser.Id,
            ChatId = testUser.ChatId,
            OriginalFileName = "legacy.md",
            StoredFileName = "legacy.md",
            AbsolutePath = Path.Combine(Path.GetTempPath(), "TelegramMessagingTool_LegacyOutside_" + Guid.NewGuid().ToString("N"), "legacy.md"),
            RelativePath = "LegacyOutsideSandbox/legacy.md",
            ContentType = "text/markdown",
            SizeBytes = 12,
            Source = "legacy_test",
            CreatedAt = DateTime.UtcNow
        };
        dbContext.UploadedFiles.Add(legacyOutsideSandboxFile);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        CommandResult legacyReadResult = await commandRouter.TryHandleAsync(TextMessage($"/readfile {legacyOutsideSandboxFile.Id}"), testUser, dbContext, CancellationToken.None);
        AssertTrue(legacyReadResult.Handled, "/readfile legacy outside-sandbox file is handled");
        AssertTrue(legacyReadResult.ReplyText?.Contains("outside the current document sandbox", StringComparison.OrdinalIgnoreCase) == true, "/readfile reports outside-sandbox legacy file safely");

        CommandResult indexDocsWithLegacyResult = await commandRouter.TryHandleAsync(TextMessage("/indexdocs"), testUser, dbContext, CancellationToken.None);
        AssertTrue(indexDocsWithLegacyResult.Handled, "/indexdocs with legacy outside-sandbox file is handled");
        AssertTrue(indexDocsWithLegacyResult.ReplyText?.Contains("Skipped", StringComparison.OrdinalIgnoreCase) == true, "/indexdocs skips outside-sandbox legacy file safely");
    }

    private static Message TextMessage(string text) => new()
    {
        Text = text,
        Chat = new Chat { Id = 123456789 }
    };
}

sealed class FakeImageDescriptionService : IImageDescriptionService
{
    private readonly string _output;

    public FakeImageDescriptionService(string output)
    {
        _output = output;
    }

    public int? LastImageId { get; private set; }

    public string? LastPrompt { get; private set; }

    public Task<ImageDescriptionResult> DescribeAsync(
        UploadedFile imageFile,
        string prompt,
        CancellationToken cancellationToken)
    {
        LastImageId = imageFile.Id;
        LastPrompt = prompt;
        return Task.FromResult(ImageDescriptionResult.Ok(_output));
    }
}

sealed class FakeImageOcrService : IImageOcrService
{
    private readonly string _output;

    public FakeImageOcrService(string output)
    {
        _output = output;
    }

    public int? LastImageId { get; private set; }

    public int CallCount { get; private set; }

    public Task<ImageOcrResult> ExtractTextAsync(
        UploadedFile imageFile,
        CancellationToken cancellationToken)
    {
        LastImageId = imageFile.Id;
        CallCount++;
        return Task.FromResult(ImageOcrResult.Ok(_output));
    }
}

sealed class FakeAudioTranscriptionService : IAudioTranscriptionService
{
    private readonly string _output;

    public FakeAudioTranscriptionService(string output)
    {
        _output = output;
    }

    public int? LastAudioId { get; private set; }
    public Task<AudioTranscriptionResult> TranscribeAsync(
        UploadedFile audioFile,
        CancellationToken cancellationToken)
    {
        LastAudioId = audioFile.Id;
        return Task.FromResult(new AudioTranscriptionResult(true, _output));
    }
}

sealed class FakeTextToSpeechService : ITextToSpeechService
{
    private readonly byte[] _audioBytes;
    private readonly string _extension;

    public FakeTextToSpeechService(byte[] audioBytes, string extension)
    {
        _audioBytes = audioBytes;
        _extension = extension;
    }

    public int CallCount { get; private set; }

    public string? LastText { get; private set; }

    public Task<TextToSpeechResult> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        CallCount++;
        LastText = text;
        return Task.FromResult(TextToSpeechResult.Ok(_audioBytes, _extension, "fake TTS audio generated"));
    }
}
