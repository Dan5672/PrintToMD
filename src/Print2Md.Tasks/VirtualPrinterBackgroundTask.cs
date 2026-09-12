using Print2Md.Core;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Data.Xml.Dom;
using Windows.Graphics.Printing.Workflow;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Notifications;

namespace Print2Md.Tasks;

public sealed class VirtualPrinterBackgroundTask : IBackgroundTask
{
    private BackgroundTaskDeferral deferral;
    private CancellationTokenSource cancellation;

    public void Run(IBackgroundTaskInstance taskInstance)
    {
        deferral = taskInstance.GetDeferral();
        cancellation = new CancellationTokenSource();
        taskInstance.Canceled += OnCanceled;

        var details = taskInstance.TriggerDetails as PrintWorkflowVirtualPrinterTriggerDetails;
        if (details == null)
        {
            CompleteTask();
            return;
        }

        var session = details.VirtualPrinterSession;
        session.VirtualPrinterDataAvailable += OnDataAvailable;
        session.Start();
    }

    private async void OnDataAvailable(
        PrintWorkflowVirtualPrinterSession sender,
        PrintWorkflowVirtualPrinterDataAvailableEventArgs args)
    {
        var status = PrintWorkflowSubmittedStatus.Failed;
        var stage = "validate-format";
        try
        {
            var token = cancellation.Token;
            if (!string.Equals(args.SourceContent.ContentType, "application/oxps", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConversionException(ConversionFailure.UnsupportedFormat, "Print2Md received an unsupported print data format.");
            }

            stage = "get-target";
            var target = await args.GetTargetFileAsync();
            if (target == null)
            {
                status = PrintWorkflowSubmittedStatus.Canceled;
                return;
            }

            ConversionResult result;
            stage = "open-input";
            using (var input = args.SourceContent.GetInputStream().AsStreamForRead())
            {
                stage = "convert";
                result = await new OxpsToMarkdownConverter().ConvertAsync(input, ConversionOptions.Default, new OmittedAssetSink(), token);
            }

            stage = "write-target";
            token.ThrowIfCancellationRequested();
            // FileIO.WriteTextAsync uses a replace-file transaction. The print
            // system grants this file, so write through its stream directly.
            using (var output = await target.OpenAsync(FileAccessMode.ReadWrite))
            {
                output.Size = 0;
                using (var writer = new DataWriter(output.GetOutputStreamAt(0)))
                {
                    writer.UnicodeEncoding = UnicodeEncoding.Utf8;
                    writer.WriteString(result.Markdown);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                }
            }
            status = PrintWorkflowSubmittedStatus.Succeeded;
        }
        catch (OperationCanceledException)
        {
            status = PrintWorkflowSubmittedStatus.Canceled;
        }
        catch (Exception exception)
        {
            await WriteDiagnosticAsync(exception, stage);
            var failure = exception is ConversionException conversion ? conversion.Failure.ToString() : exception.GetType().Name;
            ShowFailureNotification(stage + ": " + failure);
        }
        finally
        {
            try
            {
                args.CompleteJob(status);
            }
            finally
            {
                CompleteTask();
            }
        }
    }

    private void OnCanceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
    {
        cancellation?.Cancel();
    }

    private void CompleteTask()
    {
        var taskDeferral = deferral;
        deferral = null;
        taskDeferral?.Complete();
        cancellation?.Dispose();
        cancellation = null;
    }

    private static async Task WriteDiagnosticAsync(Exception exception, string stage)
    {
        try
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("print2md.log", CreationCollisionOption.OpenIfExists);
            // Log only fixed codes and types: exception messages can contain document part names.
            var failure = exception is ConversionException conversion ? conversion.Failure.ToString() : "Unknown";
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            var entry = DateTimeOffset.UtcNow.ToString("O") + " conversion-failed " + exception.GetType().FullName + " 0x" + exception.HResult.ToString("X8") +
                " stage=" + stage + " reason=" + failure +
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

    private static void ShowFailureNotification(string failureType)
    {
        try
        {
            var xml = new XmlDocument();
            xml.LoadXml(
                "<toast><visual><binding template='ToastGeneric'>" +
                "<text>Print to Markdown failed</text>" +
                "<text>The document could not be converted (" + EscapeXml(failureType) + ").</text>" +
                "</binding></visual></toast>");
            ToastNotificationManager.CreateToastNotifier().Show(new ToastNotification(xml));
        }
        catch
        {
            // Windows still receives the failed job status if toast delivery is unavailable.
        }
    }

    private static string EscapeXml(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
