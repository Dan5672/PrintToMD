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
        try
        {
            var token = cancellation.Token;
            if (!string.Equals(args.SourceContent.ContentType, "application/oxps", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConversionException("Print2Md received an unsupported print data format.");
            }

            var target = await args.GetTargetFileAsync();
            if (target == null)
            {
                status = PrintWorkflowSubmittedStatus.Canceled;
                return;
            }

            var parent = await target.GetParentAsync();
            if (parent == null)
            {
                throw new IOException("The selected Markdown file has no writable parent folder.");
            }

            var stem = Path.GetFileNameWithoutExtension(target.Name);
            var assetFolderName = stem + ".assets";
            var assetSink = new StagedAssetSink(assetFolderName);
            ConversionResult result;
            using (var input = args.SourceContent.GetInputStream().AsStreamForRead())
            {
                result = await new OxpsToMarkdownConverter().ConvertAsync(input, ConversionOptions.Default, assetSink, token);
            }

            await assetSink.CommitAsync(parent, token);
            var temporary = await parent.CreateFileAsync(
                "." + stem + ".print2md-" + Guid.NewGuid().ToString("N") + ".tmp",
                CreationCollisionOption.FailIfExists);
            try
            {
                await FileIO.WriteTextAsync(temporary, result.Markdown, UnicodeEncoding.Utf8);
                await temporary.MoveAndReplaceAsync(target);
            }
            catch
            {
                await temporary.DeleteAsync(StorageDeleteOption.PermanentDelete);
                throw;
            }

            status = PrintWorkflowSubmittedStatus.Succeeded;
        }
        catch (OperationCanceledException)
        {
            status = PrintWorkflowSubmittedStatus.Canceled;
        }
        catch (Exception exception)
        {
            await WriteDiagnosticAsync(exception);
            ShowFailureNotification(exception.GetType().Name);
        }
        finally
        {
            args.CompleteJob(status);
            CompleteTask();
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

    private static async Task WriteDiagnosticAsync(Exception exception)
    {
        try
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("print2md.log", CreationCollisionOption.OpenIfExists);
            var entry = DateTimeOffset.UtcNow.ToString("O") + " conversion-failed " + exception.GetType().FullName + " 0x" + exception.HResult.ToString("X8") + Environment.NewLine;
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
