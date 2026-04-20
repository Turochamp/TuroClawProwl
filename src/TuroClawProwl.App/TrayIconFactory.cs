using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using TuroClawProwl.Domain;

namespace TuroClawProwl.App;

internal static class TrayIconFactory
{
    public static Icon ForColor(TrayColor color)
    {
        var fill = color switch
        {
            TrayColor.Grey => Color.FromArgb(150, 150, 150),
            TrayColor.Red => Color.FromArgb(220, 50, 47),
            TrayColor.Yellow => Color.FromArgb(240, 180, 0),
            TrayColor.Green => Color.FromArgb(40, 180, 60),
            _ => Color.Gray,
        };

        using var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(fill);
            g.FillEllipse(brush, 1, 1, 14, 14);
            using var pen = new Pen(Color.FromArgb(60, 0, 0, 0), 1f);
            g.DrawEllipse(pen, 1, 1, 14, 14);
        }

        var hIcon = bitmap.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
