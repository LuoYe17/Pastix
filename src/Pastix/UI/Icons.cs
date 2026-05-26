using System.Drawing;
using System.Drawing.Drawing2D;

namespace Pastix.UI
{
    /// <summary>
    /// 所有 UI 图标的矢量绘制。Lucide 风格（描线、圆角末端、24×24 viewBox）。
    /// 所有方法在 Bounds 内居中绘制。
    /// </summary>
    internal static class Icons
    {
        public enum IconKind
        {
            Rectangle,
            Arrow,
            Pen,
            Text,
            Mosaic,
            Check,
            Close,
            Save,
            Undo,
            Redo,
            Search,
            Pin,
            PinFilled,
            Trash,
        }

        /// <summary>所有图标基于 24×24 网格设计，绘制时按 bounds 等比缩放。</summary>
        private const float ViewBox = 24f;

        public static void Draw(Graphics g, IconKind kind, RectangleF bounds, Color color, float strokeWidth = 1.8f)
        {
            var oldSmoothing = g.SmoothingMode;
            var oldState = g.Save();

            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // 居中并按比例缩放到 bounds
            float scale = System.Math.Min(bounds.Width, bounds.Height) / ViewBox;
            float dx = bounds.X + (bounds.Width - ViewBox * scale) / 2f;
            float dy = bounds.Y + (bounds.Height - ViewBox * scale) / 2f;
            g.TranslateTransform(dx, dy);
            g.ScaleTransform(scale, scale);

            using (var pen = new Pen(color, strokeWidth))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                using (var brush = new SolidBrush(color))
                {
                    switch (kind)
                    {
                        case IconKind.Rectangle: DrawRectangle(g, pen); break;
                        case IconKind.Arrow: DrawArrow(g, pen); break;
                        case IconKind.Pen: DrawPen(g, pen); break;
                        case IconKind.Text: DrawText(g, pen); break;
                        case IconKind.Mosaic: DrawMosaic(g, pen, brush); break;
                        case IconKind.Check: DrawCheck(g, pen); break;
                        case IconKind.Close: DrawClose(g, pen); break;
                        case IconKind.Save: DrawSave(g, pen); break;
                        case IconKind.Undo: DrawUndo(g, pen); break;
                        case IconKind.Redo: DrawRedo(g, pen); break;
                        case IconKind.Search: DrawSearch(g, pen); break;
                        case IconKind.Pin: DrawPin(g, pen, brush, fill: false); break;
                        case IconKind.PinFilled: DrawPin(g, pen, brush, fill: true); break;
                        case IconKind.Trash: DrawTrash(g, pen); break;
                    }
                }
            }

            g.Restore(oldState);
            g.SmoothingMode = oldSmoothing;
        }

        // 24x24 视图坐标系下的描线绘制 -----------------------------------

        private static void DrawRectangle(Graphics g, Pen pen)
        {
            // 圆角矩形
            using (var path = RoundRect(4, 4, 16, 16, 2))
                g.DrawPath(pen, path);
        }

        private static void DrawArrow(Graphics g, Pen pen)
        {
            // 从左下到右上的箭头（lucide arrow-up-right）
            g.DrawLine(pen, 5f, 19f, 19f, 5f);
            // 箭头尖
            g.DrawLine(pen, 19f, 5f, 11f, 5f);
            g.DrawLine(pen, 19f, 5f, 19f, 13f);
        }

        private static void DrawPen(Graphics g, Pen pen)
        {
            // 简化 pencil 路径
            var path = new GraphicsPath();
            path.AddLine(15f, 4f, 20f, 9f);
            path.StartFigure();
            path.AddLine(4f, 20f, 9f, 20f);
            path.AddLine(9f, 20f, 20f, 9f);
            path.AddLine(20f, 9f, 15f, 4f);
            path.AddLine(15f, 4f, 4f, 15f);
            path.AddLine(4f, 15f, 4f, 20f);
            g.DrawPath(pen, path);
            path.Dispose();
        }

        private static void DrawText(Graphics g, Pen pen)
        {
            // 字母 T
            g.DrawLine(pen, 5f, 6f, 19f, 6f);   // 顶横
            g.DrawLine(pen, 12f, 6f, 12f, 19f); // 中竖
        }

        private static void DrawMosaic(Graphics g, Pen pen, SolidBrush brush)
        {
            // 圆角矩形外框 + 内部 5 个交错的小色块（"打码"的标准视觉惯例）
            using (var path = RoundRect(3, 3, 18, 18, 2.5f))
                g.DrawPath(pen, path);

            // 棋盘式填充：5 个小方块表达"像素化"
            float s = 4.5f;
            var fills = new[] { (0, 0), (1, 1), (0, 2), (2, 1), (2, 2) };
            foreach (var (cx, cy) in fills)
            {
                float x = 5.5f + cx * 4.5f;
                float y = 5.5f + cy * 4.5f;
                g.FillRectangle(brush, x, y, s, s);
            }
        }

        private static void DrawCheck(Graphics g, Pen pen)
        {
            g.DrawLine(pen, 5f, 12f, 10f, 17f);
            g.DrawLine(pen, 10f, 17f, 19f, 7f);
        }

        private static void DrawClose(Graphics g, Pen pen)
        {
            g.DrawLine(pen, 6f, 6f, 18f, 18f);
            g.DrawLine(pen, 18f, 6f, 6f, 18f);
        }

        private static void DrawSave(Graphics g, Pen pen)
        {
            // 下载箭头
            g.DrawLine(pen, 12f, 4f, 12f, 16f);
            g.DrawLine(pen, 6f, 11f, 12f, 17f);
            g.DrawLine(pen, 18f, 11f, 12f, 17f);
            // 底盘横线
            g.DrawLine(pen, 5f, 20f, 19f, 20f);
        }

        private static void DrawUndo(Graphics g, Pen pen)
        {
            // 弯回箭头
            var path = new GraphicsPath();
            path.AddArc(5f, 6f, 14f, 12f, 200f, 140f);
            g.DrawPath(pen, path);
            // 箭头尖
            g.DrawLine(pen, 5f, 11f, 5f, 6f);
            g.DrawLine(pen, 5f, 6f, 10f, 6f);
            path.Dispose();
        }

        private static void DrawRedo(Graphics g, Pen pen)
        {
            // 镜像 undo
            var path = new GraphicsPath();
            path.AddArc(5f, 6f, 14f, 12f, 340f, -140f);
            g.DrawPath(pen, path);
            g.DrawLine(pen, 19f, 11f, 19f, 6f);
            g.DrawLine(pen, 19f, 6f, 14f, 6f);
            path.Dispose();
        }

        private static void DrawSearch(Graphics g, Pen pen)
        {
            // lucide search：圆 + 右下斜线
            g.DrawEllipse(pen, 4f, 4f, 12f, 12f);
            g.DrawLine(pen, 14.5f, 14.5f, 20f, 20f);
        }

        private static void DrawPin(Graphics g, Pen pen, SolidBrush brush, bool fill)
        {
            // 直立图钉：圆头 + 两侧小翼（卡圈）+ 向下针尖
            // 圆头：圆心 (12, 8)、半径 3.5
            // 卡圈：(7, 12) → (17, 12) 水平短线
            // 针：(12, 12) → (12, 20)
            const float headCx = 12f;
            const float headCy = 8f;
            const float headR = 3.5f;

            if (fill)
            {
                g.FillEllipse(brush, headCx - headR, headCy - headR, headR * 2, headR * 2);
            }
            g.DrawEllipse(pen, headCx - headR, headCy - headR, headR * 2, headR * 2);
            g.DrawLine(pen, 7f, 12f, 17f, 12f);
            g.DrawLine(pen, 12f, 12f, 12f, 20f);
        }

        private static void DrawTrash(Graphics g, Pen pen)
        {
            // lucide trash-2 简化版
            // 盖子横线
            g.DrawLine(pen, 4f, 7f, 20f, 7f);
            // 顶部把手 U
            g.DrawLine(pen, 9f, 7f, 9f, 4f);
            g.DrawLine(pen, 9f, 4f, 15f, 4f);
            g.DrawLine(pen, 15f, 4f, 15f, 7f);
            // 桶身（梯形微收）
            g.DrawLine(pen, 6.5f, 7f, 7.5f, 20f);
            g.DrawLine(pen, 17.5f, 7f, 16.5f, 20f);
            g.DrawLine(pen, 7.5f, 20f, 16.5f, 20f);
            // 内部两条短竖线
            g.DrawLine(pen, 10f, 11f, 10f, 17f);
            g.DrawLine(pen, 14f, 11f, 14f, 17f);
        }

        // ----------------------------------------------------------------

        private static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
        {
            var path = new GraphicsPath();
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
