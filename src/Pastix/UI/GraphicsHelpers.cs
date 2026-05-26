using System.Drawing;
using System.Drawing.Drawing2D;

namespace Pastix.UI
{
    internal static class GraphicsHelpers
    {
        public static GraphicsPath RoundRect(RectangleF bounds, float radius)
        {
            float r = radius * 2f;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, r, r, 180, 90);
            path.AddArc(bounds.Right - r, bounds.Y, r, r, 270, 90);
            path.AddArc(bounds.Right - r, bounds.Bottom - r, r, r, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static GraphicsPath RoundRect(Rectangle bounds, float radius)
        {
            return RoundRect(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), radius);
        }
    }
}
