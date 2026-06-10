namespace NotificationRelay;

// ApplicationContext is the WinForms way to keep an app alive with no visible main window.
// It acts like the "host" — the tray icon lives here, and when Quit is clicked
// it calls Application.Exit() which ends the message loop and closes the app.
class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly TrayForm _form;
    private readonly NotificationWatcher _watcher;
    private readonly FcmSender? _fcm;


    public TrayApplicationContext()
    {
        _form = new TrayForm();
        // below is magic, kinda
        _ = _form.Handle; // force native handle creation so BeginInvoke works before first Show()

        _tray = new NotifyIcon
        {
            Icon    = MakeTrayIcon(),
            Visible = true,
            Text    = "Notification Relay — starting…",
            ContextMenuStrip = BuildContextMenu(),
        };

        // MouseClick lets us check which mouse button was pressed.
        _tray.MouseClick += OnTrayClick;

        _fcm = FcmSender.TryCreate();

        _watcher = new NotificationWatcher();
        _watcher.NotificationReceived += OnNotification;

        // "_ =" discards the Task: fire-and-forget.
        // Errors inside StartAsync are shown via MessageBox, so nothing is silently lost
        _ = _watcher.StartAsync();
    }

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (_form.Visible)
            _form.Hide();
        else
        {
            _form.PositionNearTray();
            _form.Show();
            _form.Activate(); // bring to front
        }
    }

    // WinRT events fire on a thread pool thread; BeginInvoke marshals to the UI thread.
    private void OnNotification(NotifInfo notif)
    {
        _form.BeginInvoke((Action)(() =>
        {
            var tip = $"Relay ● {notif.App}: {notif.Title}";
            _tray.Text = tip.Length > 127 ? tip[..127] : tip;
            _form.AddNotification(notif);
        }));

        // Fire-and-forget FCM send on the thread-pool thread we're already on.
        if (_fcm is not null)
            _ = _fcm.SendAsync(notif);
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Notification Relay").Enabled = false; // non-clickable label
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) =>
        {
            _tray.Visible = false; // hide icon before exit so it doesn't linger in tray
            Application.Exit();
        });
        return menu;
    }

    // Draws a small purple circle as the tray icon — no .ico file needed.
    private static Icon MakeTrayIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(130, 80, 220));
            g.FillEllipse(brush, 1, 1, 13, 13);
        }
        // GetHicon() hands ownership of the handle to Icon.FromHandle().
        return Icon.FromHandle(bmp.GetHicon());
    }

    // Dispose is C#'s cleanup pattern — called when the object is being destroyed.
    // "base.Dispose(disposing)" calls the parent class's cleanup too.
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watcher.Stop();
            _tray.Dispose();
            _form.Dispose();
        }
        base.Dispose(disposing);
    }
}
