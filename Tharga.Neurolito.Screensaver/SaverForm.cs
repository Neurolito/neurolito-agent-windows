using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Tharga.Neurolito.Screensaver;

/// <summary>
/// Draws the scene: full screen on one monitor, or inside the preview of the screen saver settings. Any key, click or
/// real mouse movement ends a full-screen saver.
/// </summary>
internal sealed class SaverForm : Form
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int MouseSlack = 10;

    private static readonly Color Background = Color.Black;
    private static readonly Color Title = Color.White;
    private static readonly Color Quiet = Color.FromArgb(140, 140, 150);
    private static readonly Color Incoming = Color.FromArgb(110, 170, 255);
    private static readonly Color Answer = Color.FromArgb(235, 225, 205);
    private static readonly Color Working = Color.FromArgb(255, 190, 90);
    private static readonly Color Ready = Color.FromArgb(120, 220, 150);

    private readonly TrafficScene _scene;
    private readonly Func<TimeSpan> _clock;
    private readonly IntPtr _previewParent;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly Dictionary<(string, float), float> _widths = new();
    private Point? _firstMouse;
    private PointF _drift;
    private PointF _driftTarget;
    private double _nextDrift;
    private Fonts _fonts;

    /// <summary>Full screen on one monitor.</summary>
    public SaverForm(TrafficScene scene, Func<TimeSpan> clock, Rectangle bounds) : this(scene, clock)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        Cursor.Hide();
        KeyDown += (_, _) => Close();
        MouseDown += (_, _) => Close();
        MouseMove += (_, e) =>
        {
            //NOTE: Windows sends a move when the window appears; only a real move ends the screensaver.
            _firstMouse ??= e.Location;
            if (Math.Abs(e.X - _firstMouse.Value.X) + Math.Abs(e.Y - _firstMouse.Value.Y) > MouseSlack) Close();
        };
    }

    /// <summary>Inside the little monitor of the screen saver settings.</summary>
    public SaverForm(TrafficScene scene, Func<TimeSpan> clock, IntPtr previewParent) : this(scene, clock)
    {
        _previewParent = previewParent;
        GetClientRect(previewParent, out var r);
        Bounds = new Rectangle(0, 0, r.Right, r.Bottom);
    }

    private SaverForm(TrafficScene scene, Func<TimeSpan> clock)
    {
        _scene = scene;
        _clock = clock;
        BackColor = Background;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        _timer.Tick += (_, _) =>
        {
            //NOTE: The settings dialog starts a new preview whenever it redraws; an old one ends when its window is hidden.
            if (_previewParent != IntPtr.Zero && !IsWindowVisible(_previewParent)) Close();
            Invalidate();
        };
        _timer.Start();
    }

    private bool IsPreview => _previewParent != IntPtr.Zero;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (_previewParent != IntPtr.Zero)
            {
                cp.Parent = _previewParent;
                cp.Style = WsChild | WsVisible;
                cp.ExStyle = 0;
            }

            return cp;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _fonts?.Dispose();
        _fonts = null;
        _widths.Clear();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var now = _clock();
        var frame = _scene.Frame(now);
        _fonts ??= new Fonts(ClientSize.Height, IsPreview);

        if (IsPreview)
        {
            DrawPreview(g, frame);
            return;
        }

        var h = ClientSize.Height;
        var w = ClientSize.Width;
        var margin = h * 0.06f;
        Drift(now.TotalSeconds, margin);

        var x = margin + _drift.X;
        var y = margin + _drift.Y;
        y = Line(g, "Neurolito", _fonts.Title, Title, 1, x, y);
        if (frame.Connected) y = Line(g, string.Join("  ·  ", new[] { frame.Machine, frame.Engine }.Where(s => !string.IsNullOrEmpty(s))), _fonts.Small, Quiet, 1, x, y);
        y = Line(g, frame.Status, _fonts.Status, StatusColor(frame), 1, x, y + h * 0.01f);
        if (!string.IsNullOrEmpty(frame.Model)) y = Line(g, frame.Model, _fonts.Small, Quiet, 1, x, y);
        if (frame.Summary != null && frame.SummaryAlpha > 0) Line(g, frame.Summary, _fonts.Small, Quiet, frame.SummaryAlpha, x, y + h * 0.01f);

        var top = h * 0.32f;
        var bottom = h - margin * 1.6f;
        DrawIncoming(g, frame, w, top, bottom);
        DrawAnswer(g, frame, w * 0.47f, w - margin, top, bottom);

        if (frame.Totals != null) Line(g, frame.Totals, _fonts.Small, Quiet, 0.8f, margin, h - margin);
    }

    private void DrawPreview(Graphics g, SceneFrame frame)
    {
        var y = Line(g, "Neurolito", _fonts.Title, Title, 1, 4, 4);
        Line(g, frame.Status, _fonts.Small, StatusColor(frame), 1, 4, y + 2);
    }

    /// <summary>The prompt, flying in from the left edge and fading as it lands mid-screen.</summary>
    private void DrawIncoming(Graphics g, SceneFrame frame, float width, float top, float bottom)
    {
        var landing = width * 0.40f;
        foreach (var word in frame.Incoming)
        {
            var eased = 1 - (1 - word.Progress) * (1 - word.Progress);
            var wordWidth = TextWidth(g, word.Text, _fonts.Word);
            var x = -wordWidth + (landing + wordWidth) * eased;
            //NOTE: On rows, so words in flight overlap less.
            var rows = Math.Max(1, (int)((bottom - top) / (_fonts.Word.Height * 1.15f)));
            var y = top + (int)(word.Lane * rows) * (bottom - top) / rows;
            var alpha = 0.15f + 0.85f * (1 - word.Progress);
            using var brush = new SolidBrush(Fade(Incoming, alpha));
            g.DrawString(word.Text, _fonts.Word, brush, x, y, StringFormat.GenericTypographic);
        }
    }

    /// <summary>The answer, a paragraph that grows word by word; when it fills its column the oldest lines scroll away.</summary>
    private void DrawAnswer(Graphics g, SceneFrame frame, float left, float right, float top, float bottom)
    {
        if (frame.Answer.Count == 0 || frame.AnswerAlpha <= 0) return;

        var font = _fonts.Word;
        //NOTE: The typographic format measures a lone space as nothing; a space is what it adds between two letters.
        var space = TextWidth(g, "x x", font) - TextWidth(g, "xx", font);
        var lines = new List<List<string>> { new() };
        var lineWidth = 0f;
        foreach (var word in frame.Answer)
        {
            var wordWidth = TextWidth(g, word, font);
            if (lineWidth > 0 && lineWidth + space + wordWidth > right - left)
            {
                lines.Add([]);
                lineWidth = 0;
            }

            lines[^1].Add(word);
            lineWidth += (lineWidth > 0 ? space : 0) + wordWidth;
        }

        var lineHeight = font.Height * 1.15f;
        var fit = Math.Max(1, (int)((bottom - top) / lineHeight));
        var shown = lines.Skip(Math.Max(0, lines.Count - fit)).ToArray();

        using var brush = new SolidBrush(Fade(Answer, frame.AnswerAlpha));
        var y = top;
        foreach (var line in shown)
        {
            //NOTE: Word by word, at the positions measured, so a line ends where it was measured to end.
            var x = left;
            foreach (var word in line)
            {
                g.DrawString(word, font, brush, x, y, StringFormat.GenericTypographic);
                x += TextWidth(g, word, font) + space;
            }

            y += lineHeight;
        }
    }

    private float Line(Graphics g, string text, Font font, Color color, float alpha, float x, float y)
    {
        if (string.IsNullOrEmpty(text)) return y;
        using var brush = new SolidBrush(Fade(color, alpha));
        g.DrawString(text, font, brush, x, y);
        return y + font.Height * 1.1f;
    }

    /// <summary>The text block wanders a little every half minute, so nothing burns into a screen left on for hours.</summary>
    private void Drift(double seconds, float margin)
    {
        if (seconds >= _nextDrift)
        {
            _driftTarget = new PointF((float)(Random.Shared.NextDouble() - 0.5) * margin, (float)(Random.Shared.NextDouble() - 0.5) * margin * 0.6f);
            _nextDrift = seconds + 30;
        }

        _drift = new PointF(_drift.X + (_driftTarget.X - _drift.X) * 0.01f, _drift.Y + (_driftTarget.Y - _drift.Y) * 0.01f);
    }

    private float TextWidth(Graphics g, string word, Font font)
    {
        var key = (word, font.Size);
        if (_widths.TryGetValue(key, out var width)) return width;
        width = g.MeasureString(word, font, PointF.Empty, StringFormat.GenericTypographic).Width;
        _widths[key] = width;
        return width;
    }

    private static Color StatusColor(SceneFrame frame) => !frame.Connected ? Quiet
        : frame.Status == "Working" ? Working
        : frame.Status.StartsWith("Ready", StringComparison.Ordinal) ? Ready
        : Quiet;

    private static Color Fade(Color color, float alpha) => Color.FromArgb((int)(Math.Clamp(alpha, 0, 1) * 255), color);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _fonts?.Dispose();
        base.OnFormClosed(e);
    }

    private sealed class Fonts : IDisposable
    {
        public Fonts(int height, bool preview)
        {
            float Size(float share, float min) => Math.Max(min, height * share);
            Title = new Font("Segoe UI Light", preview ? 11 : Size(0.055f, 18), GraphicsUnit.Pixel);
            Status = new Font("Segoe UI", Size(0.024f, 9), GraphicsUnit.Pixel);
            Small = new Font("Segoe UI", preview ? 7 : Size(0.017f, 8), GraphicsUnit.Pixel);
            Word = new Font("Segoe UI", Size(0.019f, 8), GraphicsUnit.Pixel);
        }

        public Font Title { get; }
        public Font Status { get; }
        public Font Small { get; }
        public Font Word { get; }

        public void Dispose()
        {
            Title.Dispose();
            Status.Dispose();
            Small.Dispose();
            Word.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
}
