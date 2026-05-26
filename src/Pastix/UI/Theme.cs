using System.Drawing;

namespace Pastix.UI
{
    /// <summary>
    /// 全局视觉规范，所有 UI 颜色/字体来源。
    /// </summary>
    internal static class Theme
    {
        // 工具栏背景：磨砂深色卡片
        public static readonly Color ToolbarBackground = Color.FromArgb(235, 40, 40, 42);
        public static readonly Color ToolbarBorder = Color.FromArgb(40, 255, 255, 255);

        // 分隔线
        public static readonly Color Divider = Color.FromArgb(35, 255, 255, 255);

        // 按钮
        public static readonly Color ButtonIdle = Color.Transparent;
        public static readonly Color ButtonHover = Color.FromArgb(35, 255, 255, 255);
        public static readonly Color ButtonPressed = Color.FromArgb(60, 255, 255, 255);

        // 强调色
        public static readonly Color Accent = Color.FromArgb(255, 10, 132, 255);    // macOS 蓝
        public static readonly Color Success = Color.FromArgb(255, 48, 209, 88);    // macOS 绿
        public static readonly Color Danger = Color.FromArgb(255, 255, 69, 58);     // macOS 红

        // 文字/图标
        public static readonly Color IconColor = Color.FromArgb(230, 255, 255, 255);
        public static readonly Color IconColorHover = Color.White;
        public static readonly Color IconColorActive = Color.White;

        // 选区
        public static readonly Color SelectionBorder = Color.FromArgb(255, 10, 132, 255);
        public static readonly Color OverlayDim = Color.FromArgb(110, 0, 0, 0);

        // 尺寸提示
        public static readonly Color SizeBadgeBackground = Color.FromArgb(220, 30, 30, 32);
        public static readonly Color SizeBadgeText = Color.White;

        // 颜色选择候选色
        public static readonly Color[] PaletteColors = new[]
        {
            Color.FromArgb(255, 255, 69, 58),   // 红
            Color.FromArgb(255, 255, 214, 10),  // 黄
            Color.FromArgb(255, 48, 209, 88),   // 绿
            Color.FromArgb(255, 10, 132, 255),  // 蓝
            Color.White,
        };

        public const string FontFamily = "Segoe UI Variable Display";
        public const string FontFamilyFallback = "Microsoft YaHei UI";

        public static Font UiFont(float size, FontStyle style = FontStyle.Regular)
        {
            try
            {
                return new Font(FontFamily, size, style);
            }
            catch
            {
                return new Font(FontFamilyFallback, size, style);
            }
        }
    }
}
