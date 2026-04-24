using System.Drawing;
using System.Windows.Forms;
using LucasScreentime.Tracking;

namespace LucasScreentime.UI;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ScreentimeTracker _tracker;
    private readonly Icon _normalIcon;
    private readonly Icon _errorIcon;
    private readonly System.Windows.Forms.Timer _tooltipTimer;
    private readonly ToolStripMenuItem _todayItem;
    private bool _hasError;

    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIcon(ScreentimeTracker tracker)
    {
        _tracker = tracker;
        _normalIcon = CreateCircleIcon(Color.FromArgb(52, 152, 219)); // blue
        _errorIcon = CreateCircleIcon(Color.FromArgb(231, 76, 60));   // red

        _todayItem = new ToolStripMenuItem("Today: —") { Enabled = false };
        var settingsItem = new ToolStripMenuItem("Settings...");
        var exitItem = new ToolStripMenuItem("Exit");

        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_todayItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        menu.Opening += (_, _) => RefreshTodayLabel();

        _notifyIcon = new NotifyIcon
        {
            Icon = _normalIcon,
            ContextMenuStrip = menu,
            Visible = true,
            Text = "Lucas Screentime"
        };

        _tooltipTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _tooltipTimer.Tick += (_, _) => RefreshTooltip();
        _tooltipTimer.Start();

        tracker.StateChanged += RefreshTooltip;
    }

    public void SetError(bool hasError)
    {
        if (_hasError == hasError) return;
        _hasError = hasError;
        _notifyIcon.Icon = hasError ? _errorIcon : _normalIcon;
    }

    private void RefreshTodayLabel()
    {
        var total = _tracker.GetTodayTotal();
        _todayItem.Text = $"Today: {FormatTime(total)}";
    }

    private void RefreshTooltip()
    {
        var total = _tracker.GetTodayTotal();
        // NotifyIcon.Text max is 63 chars
        var text = $"Lucas Screentime — {FormatTime(total)} today";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private static string FormatTime(TimeSpan t)
    {
        int h = (int)t.TotalHours;
        int m = t.Minutes;
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }

    private static Icon CreateCircleIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 1, 1, 14, 14);
        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void Dispose()
    {
        _tooltipTimer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _normalIcon.Dispose();
        _errorIcon.Dispose();
    }
}
