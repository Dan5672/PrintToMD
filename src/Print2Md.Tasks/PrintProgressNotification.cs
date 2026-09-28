using System;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Print2Md.Tasks;

internal sealed class PrintProgressNotification
{
    private readonly string tag;
    private const string Group = "printing";
    private uint sequence;
    private bool started;
    private bool finished;

    internal PrintProgressNotification(string job) { tag = job.Substring(0, 16); }

    internal void Start()
    {
        if (started || finished) return;
        started = true;
        try
        {
            var xml = new XmlDocument();
            xml.LoadXml("<toast duration='long'><visual><binding template='ToastGeneric'>" +
                "<text>Creating your Markdown file</text>" +
                "<text>Please wait for File ready before opening it.</text>" +
                "<progress title='Print to Markdown' value='indeterminate' valueStringOverride='' status='{status}'/>" +
                "</binding></visual><audio silent='true'/></toast>");
            var toast = new ToastNotification(xml) { Tag = tag, Group = Group, Data = Data("Receiving document") };
            ToastNotificationManager.CreateToastNotifier().Show(toast);
        }
        catch { /* Notification settings must never prevent printing. */ }
    }

    internal void Update(string status)
    {
        if (!started || finished) return;
        try { ToastNotificationManager.CreateToastNotifier().Update(Data(status), tag, Group); }
        catch { }
    }

    internal void Finish(string title, string detail)
    {
        if (finished) return;
        finished = true;
        try
        {
            var xml = new XmlDocument();
            xml.LoadXml("<toast><visual><binding template='ToastGeneric'><text>" + Escape(title) +
                "</text><text>" + Escape(detail) + "</text></binding></visual><audio silent='true'/></toast>");
            ToastNotificationManager.CreateToastNotifier().Show(new ToastNotification(xml) { Tag = tag, Group = Group });
        }
        catch { }
    }

    private NotificationData Data(string status)
    {
        var data = new NotificationData { SequenceNumber = ++sequence };
        data.Values["status"] = status;
        return data;
    }

    private static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
