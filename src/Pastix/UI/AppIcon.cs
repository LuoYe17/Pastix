using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Pastix.UI
{
    /// <summary>
    /// 运行时生成的应用图标。极简风格：剪贴板背板 + 顶部弹片 + 中间文本横线。
    /// </summary>
    internal static class AppIcon
    {
        public static Icon Create(int size = 32, bool darkBackground = true)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                Color stroke = darkBackground ? Color.White : Color.FromArgb(30, 30, 32);
                float pen = Math.Max(1.5f, size * 0.08f);

                // 背板：圆角矩形，左右各留 18% 边距，顶部留 12% 给弹片
                float padX = size * 0.18f;
                float top = size * 0.20f;
                float bottom = size * 0.92f;
                var board = new RectangleF(padX, top, size - padX * 2, bottom - top);

                using (var p = new Pen(stroke, pen) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var path = RoundRect(board, size * 0.10f))
                {
                    g.DrawPath(p, path);
                }

                // 顶部弹片：居中小圆角矩形，跨过背板顶边
                float clipW = (size - padX * 2) * 0.46f;
                float clipH = size * 0.18f;
                float clipX = (size - clipW) / 2f;
                float clipY = size * 0.10f;
                var clip = new RectangleF(clipX, clipY, clipW, clipH);

                // 弹片底色与背景同：先用 Theme.Accent 实色块，再叠一圈 stroke 描边，使其跟背板形成层次
                using (var brush = new SolidBrush(Theme.Accent))
                using (var path = RoundRect(clip, size * 0.06f))
                    g.FillPath(brush, path);
                using (var p = new Pen(stroke, pen) { LineJoin = LineJoin.Round })
                using (var path = RoundRect(clip, size * 0.06f))
                    g.DrawPath(p, path);

                // 内容横线：3 条，居中，长度递减表达"文本"
                using (var p = new Pen(stroke, Math.Max(1.2f, size * 0.06f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float lx = board.Left + board.Width * 0.18f;
                    float rx = board.Right - board.Width * 0.18f;
                    float lineGap = board.Height * 0.18f;
                    float baseY = board.Top + board.Height * 0.42f;
                    g.DrawLine(p, lx, baseY, rx, baseY);
                    g.DrawLine(p, lx, baseY + lineGap, rx - board.Width * 0.12f, baseY + lineGap);
                    g.DrawLine(p, lx, baseY + lineGap * 2, rx - board.Width * 0.28f, baseY + lineGap * 2);
                }
            }

            return BitmapToIcon(bmp);
        }

        private static GraphicsPath RoundRect(RectangleF bounds, float radius)
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

        private static Icon BitmapToIcon(Bitmap bmp)
        {
            byte[] pngBytes;
            using (var pngStream = new MemoryStream())
            {
                bmp.Save(pngStream, ImageFormat.Png);
                pngBytes = pngStream.ToArray();
            }

            var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                // ICONDIR
                bw.Write((short)0);          // reserved
                bw.Write((short)1);          // type = icon
                bw.Write((short)1);          // count

                // ICONDIRENTRY
                bw.Write((byte)bmp.Width);
                bw.Write((byte)bmp.Height);
                bw.Write((byte)0);           // colors
                bw.Write((byte)0);           // reserved
                bw.Write((short)1);          // planes
                bw.Write((short)32);         // bpp
                bw.Write(pngBytes.Length);   // size
                bw.Write(22);                // offset (header size)

                // PNG payload
                bw.Write(pngBytes);
            }

            ms.Position = 0;
            var icon = new Icon(ms);
            ms.Dispose();
            return icon;
        }
    }
}
