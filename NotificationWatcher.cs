using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NotificationRelay;

// The format 
record NotifInfo(uint Id, string App, string Title, string Body, DateTime Time);

class NotificationWatcher
{
    public event Action<NotifInfo>? NotificationReceived;

    //UserNotificationListener is part of the windows API, idk if this works for MAc or linux, will work on this later
    private UserNotificationListener? _listener;

    // WindowsApps is read-only; write the log to AppData\Local instead
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NotificationRelay", "NotificationRelay.log");

    // Logging...
    private static void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch { }
    }

    public async Task StartAsync()
    {
        Log("NotificationRelay starting ");
        try
        {
            _listener = UserNotificationListener.Current;

            Log("Requesting notification access...");
            // Requests access for notifications, returns Allowed or Denied (or unspecified but thats w/e).
            var status = await _listener.RequestAccessAsync();
            Log($"Access status: {status}");

            // if its not allowed, basically just warn the user
            if (status != UserNotificationListenerAccessStatus.Allowed)
            {
                Log("Access not granted — listener will not fire.");
                MessageBox.Show(
                    $"Notification access status: {status}\n\n" +
                    "To fix this:\n" +
                    "  Settings -> System -> Notifications & Actions\n" +
                    "  -> scroll down -> allow this app to access notifications.\n\n" +
                    "If the app doesn't appear in that list, send yourself one notification\n" +
                    "from the app first, then check Settings again.",
                    "Notification Relay — Access Denied",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }
            _listener.NotificationChanged += OnChanged;
            Log("Subscribed to NotificationChanged — listening.");
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION: {ex}");
            MessageBox.Show(
                $"Could not start notification listener.\n\n" +
                $"{ex.GetType().Name}: {ex.Message}\n\n" +
                $"Log: {LogPath}",
                "Notification Relay — Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private void OnChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        Log($"NotificationChanged: kind={args.ChangeKind}, id={args.UserNotificationId}");

        //if the notification is anything except added, ignore it
        if (args.ChangeKind != UserNotificationChangedKind.Added) return;

        var id = args.UserNotificationId;
        // get the id ^ and the notification that is related to that id \/
        UserNotification? userNotif;
        try   { userNotif = sender.GetNotification(id); }
        catch (Exception ex) { Log($"GetNotification threw: {ex.Message}"); return; }

        if (userNotif is null)
        {
            Log($"GetNotification({id}) returned null — transient notification already dismissed");
            return;
        }
        //try to get the name, if cannot, display "Unknown"
        string app;
        try   { app = userNotif.AppInfo?.DisplayInfo?.DisplayName ?? "Unknown"; }
        catch { app = "Unknown"; }

        var (title, body) = ParseContent(userNotif);

        Log($"→ [{app}] {title}{(body.Length > 0 ? " | " + body : "")}");
        // fire the event with the notification info, including the current timestamp
        NotificationReceived?.Invoke(new NotifInfo(id, app, title, body, DateTime.Now));
    }

    //just basically parse the content of the notif
    private static (string Title, string Body) ParseContent(UserNotification notif)
    {
        try
        {
            var binding = notif.Notification.Visual.GetBinding("ToastGeneric");
            if (binding is null)
            {
                Log("No ToastGeneric binding found");
                return ("(no content)", "");
            }

            var texts = binding.GetTextElements()
                .Select(t => t.Text.Trim())
                .Where(t => t.Length > 0)
                .ToList();

            return (
                texts.ElementAtOrDefault(0) ?? "(no title)",
                string.Join(" · ", texts.Skip(1))
            );
        }
        catch (Exception ex)
        {
            Log($"ParseContent threw: {ex.Message}");
            return ("(content unavailable)", "");
        }
    }

    public void Stop()
    {
        if (_listener != null)
            _listener.NotificationChanged -= OnChanged;
    }
}
