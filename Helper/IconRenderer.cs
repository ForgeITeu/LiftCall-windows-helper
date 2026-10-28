using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LiftCall.WindowsHelper;

internal static class IconRenderer
{
    private static readonly Color Green = Color.FromArgb(22, 163, 74);
    private static readonly Color Yellow = Color.FromArgb(202, 132, 0);
    private static readonly Color Red = Color.FromArgb(220, 38, 38);
    private static readonly Color Neutral = Color.FromArgb(100, 116, 139);

    public static Icon CreateTrayIcon(TrayStatus status)
    {
        using var bitmap = Render(32, status);
        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static Bitmap Render(int size, TrayStatus status)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        var scale = size / 16f;
        var statusColor = GetStatusColor(status);
        var inset = size <= 24 ? 0.7f * scale : 0.45f * scale;
        var background = new RectangleF(inset, inset, size - 2 * inset, size - 2 * inset);
        var radius = size <= 24 ? size * 0.28f : size * 0.25f;

        using (var backgroundPath = RoundedRectangle(background, radius))
        using (var backgroundBrush = new SolidBrush(statusColor))
        {
            graphics.FillPath(backgroundBrush, backgroundPath);
        }

        DrawElevatorDoors(graphics, size, scale, statusColor);
        return bitmap;
    }

    private static void DrawElevatorDoors(Graphics graphics, int size, float scale, Color backgroundColor)
    {
        var outer = size <= 24
            ? new RectangleF(4.0f * scale, 4.35f * scale, 8.0f * scale, 8.15f * scale)
            : new RectangleF(3.55f * scale, 3.9f * scale, 8.9f * scale, 8.95f * scale);
        var outerRadius = Math.Max(0.6f * scale, size <= 24 ? 0.8f * scale : 0.9f * scale);

        using var whiteBrush = new SolidBrush(Color.White);
        using var outerPath = RoundedRectangle(outer, outerRadius);
        graphics.FillPath(whiteBrush, outerPath);

        var inner = size <= 24
            ? new RectangleF(5.35f * scale, 5.7f * scale, 5.3f * scale, 6.8f * scale)
            : new RectangleF(4.9f * scale, 5.25f * scale, 6.2f * scale, 7.0f * scale);

        using var backgroundBrush = new SolidBrush(backgroundColor);
        graphics.FillRectangle(backgroundBrush, inner);

        var dividerWidth = size <= 24 ? Math.Max(0.9f, 0.95f * scale) : Math.Max(1.0f, 0.85f * scale);
        var dividerX = inner.Left + inner.Width / 2f - dividerWidth / 2f;
        graphics.FillRectangle(whiteBrush, dividerX, inner.Top, dividerWidth, inner.Height);
    }

    private static Color GetStatusColor(TrayStatus status) => status switch
    {
        TrayStatus.Green => Green,
        TrayStatus.Yellow => Yellow,
        TrayStatus.Red => Red,
        _ => Neutral,
    };

    private static GraphicsPath RoundedRectangle(RectangleF rectangle, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2f;
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
