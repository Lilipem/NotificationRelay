namespace NotificationRelay;

// Form is the WinForms base class for any window.
class TrayForm : Form
{
    // Width of notification cards. 360px window - scrollbar (~17px) - padding (2×8px).
    private const int CardWidth = 320;

    private readonly FlowLayoutPanel _list;
    private readonly Label _emptyLabel;
    private bool _isEmpty = true;

    public TrayForm()
    {
        // Window properties 
        ClientSize      = new Size(360, 480);
        FormBorderStyle = FormBorderStyle.None;   // no title bar or border
        ShowInTaskbar   = false;                   // don't show in Alt+Tab or taskbar
        TopMost         = true;                    // always on top of other windows
        BackColor       = Color.FromArgb(26, 26, 46);

        // Header
        // TableLayoutPanel arranges children in a grid -> 2 columns: title | button
        var header = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            Height      = 44,
            BackColor   = Color.FromArgb(22, 33, 62),
            ColumnCount = 2,
            RowCount    = 1,
            Padding     = new Padding(12, 0, 8, 0),
        };
        // First column takes all remaining space; second column sizes to fit the button.
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            Text      = "Notification Relay",
            ForeColor = Color.FromArgb(167, 139, 250),
            Font      = new Font("Segoe UI", 10, FontStyle.Bold),
            Dock      = DockStyle.Fill,          // fills the table cell
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var clearBtn = new Button
        {
            Text      = "Clear",
            ForeColor = Color.FromArgb(167, 139, 250),
            BackColor = Color.FromArgb(22, 33, 62),
            FlatStyle = FlatStyle.Flat,
            Size      = new Size(54, 26),
            Cursor    = Cursors.Hand,
            Anchor    = AnchorStyles.None, // centers the button in its table cell
        };
        clearBtn.FlatAppearance.BorderColor = Color.FromArgb(76, 29, 149);
        clearBtn.Click += (_, _) => ClearAll();

        header.Controls.Add(titleLabel, 0, 0);
        header.Controls.Add(clearBtn,  1, 0);

        // Notification list 
        // FlowLayoutPanel stacks children top-to-bottom with automatic scrolling.
        _list = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            AutoScroll    = true,
            Padding       = new Padding(8),
            BackColor     = Color.FromArgb(26, 26, 46),
        };

        _emptyLabel = new Label
        {
            Text      = "No notifications yet",
            ForeColor = Color.FromArgb(75, 85, 99),
            Font      = new Font("Segoe UI", 11),
            AutoSize  = true,
            Margin    = new Padding(60, 140, 0, 0), // rough visual centering
        };
        _list.Controls.Add(_emptyLabel);

        // Controls added last with DockStyle.Top draw on top of Fill controls.
        Controls.Add(_list);
        Controls.Add(header);
    }

    // Public API 

    public void AddNotification(NotifInfo notif)
    {
        // InvokeRequired is true when called from a non-UI thread.
        // Invoke() marshals the call to the UI thread synchronously.
        // (TrayApplicationContext already does this via _ui.Post, but
        //  the check here is a safety net in case AddNotification is
        //  ever called from somewhere else.)
        if (InvokeRequired) { Invoke(() => AddNotification(notif)); return; }

        if (_isEmpty)
        {
            _isEmpty = false;
            _list.Controls.Remove(_emptyLabel);
        }

        var card = MakeCard(notif);
        _list.Controls.Add(card);
        _list.ScrollControlIntoView(card); // auto-scroll to the newest card
    }

    public void PositionNearTray()
    {
        // The system tray is always at the bottom-right of the working area
        // (the area that excludes the taskbar). Position our window just above it.
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
    }

    // Card builder

    private static Panel MakeCard(NotifInfo notif)
    {
        bool hasBody = !string.IsNullOrEmpty(notif.Body);

        var card = new Panel
        {
            Width     = CardWidth,
            Height    = hasBody ? 72 : 52,
            BackColor = Color.FromArgb(22, 33, 62),
            Margin    = new Padding(0, 0, 0, 6),
        };

        // Draw a subtle border around the card via the Paint event.
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(15, 52, 96));
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };

        var appLabel = new Label
        {
            Text      = notif.App.ToUpper(),
            ForeColor = Color.FromArgb(167, 139, 250),
            Font      = new Font("Segoe UI", 8, FontStyle.Bold),
            AutoSize  = true,
            Location  = new Point(10, 8),
        };

        var timeLabel = new Label
        {
            Text      = notif.Time.ToString("HH:mm"),
            ForeColor = Color.FromArgb(107, 114, 128),
            Font      = new Font("Segoe UI", 8),
            AutoSize  = true,
        };
        // Right-align time: compute position after AutoSize resolves the width.
        timeLabel.Location = new Point(CardWidth - timeLabel.PreferredWidth - 10, 8);

        var titleLabel = new Label
        {
            Text         = notif.Title,
            ForeColor    = Color.FromArgb(243, 244, 246),
            Font         = new Font("Segoe UI", 10, FontStyle.Bold),
            Size         = new Size(CardWidth - 20, 20),
            Location     = new Point(10, 28),
            AutoEllipsis = true, // adds "…" if text overflows
        };

        card.Controls.Add(appLabel);
        card.Controls.Add(timeLabel);
        card.Controls.Add(titleLabel);

        if (hasBody)
        {
            var bodyLabel = new Label
            {
                Text         = notif.Body,
                ForeColor    = Color.FromArgb(156, 163, 175),
                Font         = new Font("Segoe UI", 9),
                Size         = new Size(CardWidth - 20, 18),
                Location     = new Point(10, 50),
                AutoEllipsis = true,
            };
            card.Controls.Add(bodyLabel);
        }

        return card;
    }

    private void ClearAll()
    {
        _list.Controls.Clear();
        _isEmpty = true;
        _list.Controls.Add(_emptyLabel);
    }

    // Window behaviour 

    // Auto-hide when the user clicks anywhere outside the window.
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide();
    }

    // CreateParams lets us tweak the underlying Win32 window style.
    // WS_EX_TOOLWINDOW (0x80) hides this window from the Alt+Tab switcher.
    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x80;
            return p;
        }
    }
}
