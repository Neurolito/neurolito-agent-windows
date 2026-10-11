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

    /// <summary>
    /// Input in the first moment does not count: the key that started the screensaver from a prompt is still down and
    /// repeating, and a window appearing gets a mouse move of its own. Without this it ended as it opened.
    /// </summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(1.5);

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
        var shown = DateTime.UtcNow;
        bool Settled() => DateTime.UtcNow - shown > Grace;
        KeyDown += (_, _) => { if (Settled()) Close(); };
        MouseDown += (_, _) => { if (Settled()) Close(); };
        MouseMove += (_, e) =>
        {
            //NOTE: Measured from where the mouse was once the screensaver settled; only a real move ends it.
            if (!Settled() || _firstMouse == null)
            {
                _firstMouse = e.Location;
                return;
            }

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
        //NOTE: A model loading is the long wait before a job's first word; it breathes so the screen does not look stuck.
        if (frame.ModelActivity != null) y = Line(g, frame.ModelActivity, _fonts.Small, Working, 0.55f + 0.45f * (float)Math.Abs(Math.Sin(now.TotalSeconds * 2)), x, y);
        else if (frame.ModelNote != null && frame.ModelNoteAlpha > 0) y = Line(g, frame.ModelNote, _fonts.Small, Quiet, frame.ModelNoteAlpha, x, y);
        else if (frame.InMemory != null) y = Line(g, frame.InMemory, _fonts.Small, Quiet, 0.7f, x, y);
        if (frame.Summary != null && frame.SummaryAlpha > 0) Line(g, frame.Summary, _fonts.Small, Quiet, frame.SummaryAlpha, x, y + h * 0.01f);

        DrawWork(g, frame, w * 0.40f + _drift.X, margin + _drift.Y, w * 0.22f, h);
        DrawQueue(g, frame, w - margin + _drift.X, margin + _drift.Y, h);

        var top = h * TrafficScene.LanesTop;
        var bottom = h - margin * 1.6f;
        DrawIncoming(g, frame, w, top, bottom);
        DrawSwirl(g, frame, w, h);
        DrawAnswer(g, frame, w * TrafficScene.AnswerX, w - margin, top, bottom);

        if (frame.Totals != null) Line(g, frame.Totals, _fonts.Small, Quiet, 0.8f, margin, h - margin);
        if (frame.Version != null) Line(g, frame.Version, _fonts.Small, Quiet, 0.6f, w - margin - TextWidth(g, frame.Version, _fonts.Small), h - margin);
    }

    private void DrawPreview(Graphics g, SceneFrame frame)
    {
        var y = Line(g, "Neurolito", _fonts.Title, Title, 1, 4, 4);
        y = Line(g, frame.Status, _fonts.Small, StatusColor(frame), 1, 4, y + 2);
        if (frame.Version != null) Line(g, frame.Version, _fonts.Small, Quiet, 1, 4, y);
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

    /// <summary>The prompt's letters while the model reads it, turning between the prompt and the answer, then joining the answer.</summary>
    private void DrawSwirl(Graphics g, SceneFrame frame, float width, float height)
    {
        foreach (var letter in frame.Swirl)
        {
            using var brush = new SolidBrush(Fade(Mix(Incoming, Answer, letter.Arrival), letter.Alpha));
            g.DrawString(letter.Letter.ToString(), _fonts.Word, brush, letter.X * width, letter.Y * height, StringFormat.GenericTypographic);
        }
    }

    /// <summary>Today's work units and jobs over a bar for each of the last 24 hours, the current hour lit.</summary>
    private void DrawWork(Graphics g, SceneFrame frame, float left, float top, float width, float height)
    {
        if (frame.WorkToday == null) return;

        var y = Line(g, frame.WorkToday, _fonts.Status, Title, 0.9f, left, top);
        var hours = frame.WorkHours;
        var chartHeight = height * 0.08f;
        var chartTop = y + height * 0.008f;
        var max = hours.Count == 0 ? 0 : hours.Max();
        if (hours.Count > 0)
        {
            var slot = width / hours.Count;
            for (var i = 0; i < hours.Count; i++)
            {
                var barHeight = max <= 0 ? 0 : (float)(hours[i] / max) * chartHeight;
                var current = i == hours.Count - 1;
                using var brush = new SolidBrush(Fade(current ? Ready : Incoming, current ? 0.9f : 0.55f));
                //NOTE: An hour without work keeps a hairline, so the chart reads as 24 hours rather than a gap.
                g.FillRectangle(brush, left + i * slot, chartTop + chartHeight - Math.Max(1, barHeight), Math.Max(1, slot * 0.7f), Math.Max(1, barHeight));
            }
        }

        Line(g, $"Last 24 hours · {frame.WorkWeek}", _fonts.Small, Quiet, 0.8f, left, chartTop + chartHeight + height * 0.006f);
    }

    /// <summary>The jobs this machine can take now, and those waiting for a model it lacks, right-aligned.</summary>
    private void DrawQueue(Graphics g, SceneFrame frame, float right, float top, float height)
    {
        if (frame.Queue is not { } queue) return;

        var y = RightLine(g, $"{queue.Pickable:N0}", _fonts.Title, queue.Pickable > 0 ? Ready : Quiet, 1, right, top);
        y = RightLine(g, queue.Pickable == 1 ? "job this machine can take" : "jobs this machine can take", _fonts.Small, Quiet, 0.9f, right, y);
        if (queue.Unpickable == 0) return;

        y = RightLine(g, $"{queue.Unpickable:N0} waiting for a model it does not have", _fonts.Status, Working, 0.9f, right, y + height * 0.012f);
        foreach (var (model, jobs) in queue.Missing.Take(3)) y = RightLine(g, $"{model} · {jobs:N0}", _fonts.Small, Quiet, 0.8f, right, y);
    }

    private float RightLine(Graphics g, string text, Font font, Color color, float alpha, float right, float y)
    {
        if (string.IsNullOrEmpty(text)) return y;
        return Line(g, text, font, color, alpha, right - g.MeasureString(text, font).Width, y);
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

    private static Color Mix(Color from, Color to, float share)
    {
        share = Math.Clamp(share, 0, 1);
        int Channel(int a, int b) => (int)(a + (b - a) * share);
        return Color.FromArgb(Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }

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
