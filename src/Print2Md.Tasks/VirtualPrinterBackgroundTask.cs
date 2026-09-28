using Print2Md.Core;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Graphics.Printing.Workflow;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Print2Md.Tasks;

public sealed class VirtualPrinterBackgroundTask : IBackgroundTask
{
    private BackgroundTaskDeferral deferral;
    private CancellationTokenSource cancellation;
    private readonly string diagnosticJob = Guid.NewGuid().ToString("N");
    private PrintProgressNotification progressNotification;

    public void Run(IBackgroundTaskInstance taskInstance)
    {
        deferral = taskInstance.GetDeferral();
        cancellation = new CancellationTokenSource();
        progressNotification = new PrintProgressNotification(diagnosticJob);
        taskInstance.Canceled += OnCanceled;

        // Run must stay synchronous through session.Start(). Awaiting here returns
        // control to the print system before the session is started, and the
        // VirtualPrinterDataAvailable event is then never delivered. Diagnostics are
        // therefore queued rather than awaited on this path.
        try
        {
            LogProgress("activated");
            var details = taskInstance.TriggerDetails as PrintWorkflowVirtualPrinterTriggerDetails;
            if (details == null)
            {
                LogProgress("unsupported-trigger");
                CompleteTask();
                return;
            }

            var session = details.VirtualPrinterSession;
            session.VirtualPrinterDataAvailable += OnDataAvailable;
            session.Start();
            LogProgress("session-started");
        }
        catch (Exception exception)
        {
            LogFailure(exception, "session-start");
            CompleteTask();
        }
    }

    private static async Task<IRandomAccessStream> CreateOcrPdfAsync(
        PrintWorkflowVirtualPrinterDataAvailableEventArgs args, IRandomAccessStream source, bool isPdf, CancellationToken token)
    {
        if (isPdf) return source.CloneStream();
        var pdf = new InMemoryRandomAccessStream();
        try
        {
            using (var input = source.GetInputStreamAt(0))
            using (var output = pdf.GetOutputStreamAt(0))
            {
                var converter = args.GetPdlConverter(PrintWorkflowPdlConversionType.XpsToPdf);
                await converter.ConvertPdlAsync(args.GetJobPrintTicket(), input, output).AsTask(token);
                await output.FlushAsync().AsTask(token);
            }
            pdf.Seek(0);
            return pdf;
        }
        catch
        {
            pdf.Dispose();
            throw;
        }
    }

    private void LogProgress(string stage)
    {
        _ = WriteProgressAsync(stage);
    }

    private void LogFailure(Exception exception, string stage)
    {
        _ = WriteDiagnosticAsync(exception, stage);
    }

    private async void OnDataAvailable(
        PrintWorkflowVirtualPrinterSession sender,
        PrintWorkflowVirtualPrinterDataAvailableEventArgs args)
    {
        var status = PrintWorkflowSubmittedStatus.Failed;
        var stage = "validate-format";
        try
        {
            // Queued, not awaited: the handler must read from args before it yields.
            LogProgress("data-available");
            var token = cancellation.Token;
            var sourceFormat = args.SourceContent.ContentType ?? string.Empty;
            LogProgress("source-format type=" + sourceFormat);

            var isOxps = string.Equals(sourceFormat, "application/oxps", StringComparison.OrdinalIgnoreCase);
            var isPdf = string.Equals(sourceFormat, "application/pdf", StringComparison.OrdinalIgnoreCase);
            if (!isOxps && !isPdf)
            {
                throw new ConversionException(ConversionFailure.UnsupportedFormat, "Print2Md received an unsupported print data format.");
            }

            stage = "get-target";
            await WriteProgressAsync(stage);
            var target = await args.GetTargetFileAsync();
            if (target == null)
            {
                status = PrintWorkflowSubmittedStatus.Canceled;
                await WriteProgressAsync("target-canceled");
                return;
            }

            string markdown;
            progressNotification.Start();
            stage = "open-input";
            await WriteProgressAsync(stage);
            using (var buffered = new InMemoryRandomAccessStream())
            {
                // Consume the live spool stream once. Both extraction and the optional
                // renderer can then read it independently without synchronous spool reads.
                stage = "buffer-input";
                await WriteProgressAsync(stage);
                using (var source = args.SourceContent.GetInputStream())
                using (var destination = buffered.GetOutputStreamAt(0))
                {
                    await RandomAccessStream.CopyAsync(source, destination).AsTask(token);
                    await destination.FlushAsync().AsTask(token);
                }
                await WriteProgressAsync("input-buffered bytes=" + buffered.Size);
                using (var recognizer = new WindowsPageTextRecognizer(
                    () => CreateOcrPdfAsync(args, buffered, isPdf, token), WriteProgressAsync))
                using (var input = buffered.GetInputStreamAt(0).AsStreamForRead())
                {
                    stage = isPdf ? "convert-pdf" : "convert-oxps";
                    await WriteProgressAsync(stage);
                    var result = isPdf
                        ? await new PdfToMarkdownConverter().ConvertAsync(input, ConversionOptions.Default, token, recognizer)
                        : await new OxpsToMarkdownConverter().ConvertAsync(input, ConversionOptions.Default, new OmittedAssetSink(), token, recognizer);
                    await WriteProgressAsync("converted pages=" + result.PageCount +
                        " glyphs=" + result.GlyphRunCount +
                        " glyphs-no-text=" + result.GlyphRunsWithoutText +
                        " images=" + result.ImageCount +
                        " chars=" + result.Markdown.Length);
                    markdown = result.Markdown;
                    foreach (var warning in result.Warnings.GroupBy(item => item.Code))
                        await WriteProgressAsync("warning code=" + warning.Key + " count=" + warning.Count());
                }
            }

            stage = "write-target";
            await WriteProgressAsync(stage);
            token.ThrowIfCancellationRequested();
            // FileIO.WriteTextAsync uses a replace-file transaction. The print
            // system grants this file, so write through its stream directly.
            using (var output = await target.OpenAsync(FileAccessMode.ReadWrite))
            {
                stage = "store-output";
                await WriteProgressAsync(stage);
                output.Size = 0;
                using (var writer = new DataWriter(output.GetOutputStreamAt(0)))
                {
                    writer.UnicodeEncoding = UnicodeEncoding.Utf8;
                    writer.WriteString(markdown);
                    await writer.StoreAsync();
                    stage = "flush-output";
                    await WriteProgressAsync(stage);
                    await writer.FlushAsync();
                }
            }
            status = PrintWorkflowSubmittedStatus.Succeeded;
            await WriteProgressAsync("output-written");
            progressNotification.Finish("File ready", "Your Markdown file has been saved: " + target.Name);
        }
        catch (OperationCanceledException)
        {
            status = PrintWorkflowSubmittedStatus.Canceled;
            await WriteProgressAsync("operation-canceled");
            progressNotification.Finish("Print to Markdown canceled", "The Markdown file was not completed.");
        }
        catch (Exception exception)
        {
            await WriteDiagnosticAsync(exception, stage);
            var failure = exception is ConversionException conversion ? conversion.Failure.ToString() : exception.GetType().Name;
            var detail = failure == "NoExtractableText" ? "No readable text found, even after OCR"
                : failure == "OcrUnavailable" ? "A Windows OCR language must be installed"
                : stage + ": " + failure;
            progressNotification.Finish("Print to Markdown failed", "The file is not ready. " + detail);
        }
        finally
        {
            try
            {
                await WriteProgressAsync("complete-job-" + status);
                args.CompleteJob(status);
                await WriteProgressAsync("job-completed-" + status);
            }
            finally
            {
                CompleteTask();
            }
        }
    }

    private async void OnCanceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
    {
        cancellation?.Cancel();
        progressNotification?.Finish("Print to Markdown stopped", "The Markdown file was not completed. Please print again.");
        await WriteProgressAsync("background-canceled-" + reason);
    }

    private void CompleteTask()
    {
        var taskDeferral = deferral;
        deferral = null;
        taskDeferral?.Complete();
        cancellation?.Dispose();
        cancellation = null;
    }

    private async Task WriteProgressAsync(string stage)
    {
        if (stage == "buffer-input") progressNotification?.Update("Receiving document");
        else if (stage == "convert-oxps" || stage == "convert-pdf") progressNotification?.Update("Extracting text");
        else if (stage == "prepare-ocr-pdf") progressNotification?.Update("Preparing text recognition");
        else if (stage.StartsWith("ocr-page-", StringComparison.Ordinal))
            progressNotification?.Update("Recognizing page " + stage.Substring("ocr-page-".Length).Replace("-of-", " of "));
        else if (stage == "write-target") progressNotification?.Update("Saving Markdown");
        try
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("print2md.log", CreationCollisionOption.OpenIfExists);
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            await FileIO.AppendTextAsync(file, DateTimeOffset.UtcNow.ToString("O") +
                " job=" + diagnosticJob + " stage=" + stage +
                " version=" + version.Major + "." + version.Minor + "." + version.Build + "." + version.Revision + Environment.NewLine);
        }
        catch
        {
            // Never interfere with printing if diagnostics cannot be written.
        }
    }

    private async Task WriteDiagnosticAsync(Exception exception, string stage)
    {
        try
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("print2md.log", CreationCollisionOption.OpenIfExists);
            // Log only fixed codes and types: exception messages can contain document part names.
            var failure = exception is ConversionException conversion ? conversion.Failure.ToString() : "Unknown";
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            var entry = DateTimeOffset.UtcNow.ToString("O") + " conversion-failed " + exception.GetType().FullName + " 0x" + exception.HResult.ToString("X8") +
                " job=" + diagnosticJob + " stage=" + stage + " reason=" + failure +
                " version=" + version.Major + "." + version.Minor + "." + version.Build + "." + version.Revision;
            if (exception.InnerException != null)
            {
                entry += " inner=" + exception.InnerException.GetType().FullName + " 0x" + exception.InnerException.HResult.ToString("X8");
            }
            entry += Environment.NewLine;
            await FileIO.AppendTextAsync(file, entry);
        }
        catch
        {
            // Diagnostics must never hide the original print failure.
        }
    }

}
