using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Pastix
{
    /// <summary>
    /// 极简 INI 风格配置，存于 exe 同目录（与 history.dat 同目录）。
    /// 加载时缺失字段使用默认值，保证旧版本 history.dat 不会因为新增字段失效。
    /// </summary>
    internal sealed class Settings
    {
        private const string FileName = "config.ini";

        public const int MinHistoryItems = 10;
        public const int MaxHistoryItemsLimit = 500;
        public const int DefaultHistoryItems = 100;

        // 图片相关上限：v1 不在 SettingsForm 暴露 UI，仅作为代码默认值。
        public const int DefaultMaxImageItems = 20;
        public const int MinMaxImageItems = 0;
        public const int MaxMaxImageItemsLimit = 200;
        public const int DefaultMaxTotalMB = 50;
        public const int MinMaxTotalMB = 10;
        public const int MaxMaxTotalMBLimit = 500;

        public bool FirstRunCompleted { get; set; }
        public int HotkeyKey { get; set; } = (int)Keys.V;
        public int HotkeyModifiers { get; set; } = (int)(HotkeyManager.MOD_CONTROL | HotkeyManager.MOD_SHIFT);
        public int MaxHistoryItems { get; set; } = DefaultHistoryItems;
        public int MaxImageItems { get; set; } = DefaultMaxImageItems;
        public int MaxTotalMB { get; set; } = DefaultMaxTotalMB;
        public bool AutoStart { get; set; } = false;
        public int LaunchCount { get; set; } = 0;
        public bool AutoStartPromptShown { get; set; } = false;

        public static string ConfigPath
        {
            get
            {
                string exeDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)
                    ?? AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(exeDir, FileName);
            }
        }

        public static Settings Load()
        {
            var s = new Settings();
            if (!File.Exists(ConfigPath)) return s;

            try
            {
                foreach (var line in File.ReadAllLines(ConfigPath))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith(";"))
                        continue;

                    int eq = trimmed.IndexOf('=');
                    if (eq <= 0) continue;

                    var key = trimmed.Substring(0, eq).Trim();
                    var value = trimmed.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "FirstRunCompleted":
                            s.FirstRunCompleted = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "HotkeyKey":
                            if (int.TryParse(value, out int k)) s.HotkeyKey = k;
                            break;
                        case "HotkeyModifiers":
                            if (int.TryParse(value, out int m)) s.HotkeyModifiers = m;
                            break;
                        case "MaxHistoryItems":
                            if (int.TryParse(value, out int n) && n >= MinHistoryItems && n <= MaxHistoryItemsLimit)
                                s.MaxHistoryItems = n;
                            break;
                        case "MaxImageItems":
                            if (int.TryParse(value, out int mi) && mi >= MinMaxImageItems && mi <= MaxMaxImageItemsLimit)
                                s.MaxImageItems = mi;
                            break;
                        case "MaxTotalMB":
                            if (int.TryParse(value, out int mt) && mt >= MinMaxTotalMB && mt <= MaxMaxTotalMBLimit)
                                s.MaxTotalMB = mt;
                            break;
                        case "AutoStart":
                            s.AutoStart = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "LaunchCount":
                            if (int.TryParse(value, out int lc) && lc >= 0) s.LaunchCount = lc;
                            break;
                        case "AutoStartPromptShown":
                            s.AutoStartPromptShown = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                            break;
                    }
                }
            }
            catch
            {
                // 配置损坏视为不存在
            }

            return s;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Pastix configuration");
                sb.AppendLine("FirstRunCompleted=" + (FirstRunCompleted ? "true" : "false"));
                sb.AppendLine("HotkeyKey=" + HotkeyKey);
                sb.AppendLine("HotkeyModifiers=" + HotkeyModifiers);
                sb.AppendLine("MaxHistoryItems=" + MaxHistoryItems);
                sb.AppendLine("MaxImageItems=" + MaxImageItems);
                sb.AppendLine("MaxTotalMB=" + MaxTotalMB);
                sb.AppendLine("AutoStart=" + (AutoStart ? "true" : "false"));
                sb.AppendLine("LaunchCount=" + LaunchCount);
                sb.AppendLine("AutoStartPromptShown=" + (AutoStartPromptShown ? "true" : "false"));
                File.WriteAllText(ConfigPath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // exe 所在目录无写权限时静默失败（U 盘只读、Program Files 等情况）
            }
        }
    }
}
