using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TaskbarQuota.Localization;

/// <summary>
/// Simplified Chinese UI copy for the localized Windows build.
/// Provider names, model names, and unknown server-provided labels are preserved.
/// </summary>
internal static partial class UiText
{
    private static readonly IReadOnlyDictionary<string, string> Values =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Session"] = "会话",
            ["Weekly"] = "每周",
            ["Daily"] = "每日",
            ["Code review"] = "代码审查",
            ["Credits"] = "额度",
            ["Enabled"] = "已启用",
            ["Not enabled"] = "未启用",
            ["Budget"] = "预算",
            ["Reset credits"] = "重置机会",
            ["Resets"] = "重置",
            ["Usage history"] = "使用记录",
            ["Refresh"] = "刷新",
            ["Pinned"] = "已固定",
            ["Pin"] = "固定",
            ["Widget"] = "小组件",
            ["Ignored"] = "已忽略",
            ["Usage dashboard"] = "用量仪表板",
            ["Usage"] = "用量",
            ["Total usage"] = "总用量",
            ["API Usage"] = "API 用量",
            ["Completions"] = "代码补全",
            ["Balance"] = "余额",
            ["Additional usage"] = "额外用量",
            ["Add'l usage"] = "额外用量",
            ["5h"] = "5 小时",
            ["Rolling"] = "滚动周期",
            ["Chat"] = "对话",
            ["Auto + Composer Usage"] = "自动 + Composer 用量",
            ["Auto + Composer"] = "自动 + Composer",
            ["API usage"] = "API 用量",
            ["Gemini Weekly"] = "Gemini 每周",
            ["Non-Gemini Weekly"] = "非 Gemini 每周",
            ["Gemini 5h"] = "Gemini 5 小时",
            ["Non-Gemini 5h"] = "非 Gemini 5 小时",
            ["Spark Session"] = "Spark 会话",
            ["Spark Weekly"] = "Spark 每周",
            ["Model weekly"] = "模型每周",
            ["Extra quota rows"] = "其他额度行",
            ["Extra weekly rows"] = "其他每周额度行",
            ["Extra model rows"] = "其他模型额度行",
            ["Spend limit"] = "支出限额",
            ["Monthly cap"] = "每月上限",
            ["Daily Routines"] = "每日例程",
            ["Spend"] = "支出",
            ["Monthly"] = "每月",
            ["Model"] = "模型",
            ["Day"] = "日期",
            ["Today"] = "今天",
            ["Yesterday"] = "昨天",
            ["Last 30 Days"] = "最近 30 天",
            ["Usage trend"] = "使用趋势",
            ["Settings"] = "设置",
            ["Appearance"] = "外观",
            ["Theme"] = "主题",
            ["Use system setting"] = "跟随系统",
            ["Light"] = "浅色",
            ["Dark"] = "深色",
            ["Widget layout"] = "小组件布局",
            ["Bars only"] = "仅进度条",
            ["Percentages only"] = "仅百分比",
            ["Bars and percentages"] = "进度条和百分比",
            ["Percentage display"] = "百分比显示",
            ["Consumed"] = "已用",
            ["Remaining"] = "剩余",
            ["Where to show usage"] = "用量显示位置",
            ["In the taskbar"] = "显示在任务栏",
            ["Floating always-on-top window"] = "悬浮置顶窗口",
            ["Providers"] = "服务",
            ["Behavior"] = "行为",
            ["Open at startup"] = "开机启动",
            ["Notifications"] = "通知",
            ["Quota alerts"] = "额度警报",
            ["Credentials"] = "凭据",
            ["About"] = "关于",
            ["Refresh usage"] = "刷新用量",
            ["Install update"] = "安装更新",
            ["No daily data"] = "暂无每日数据",
            ["No local usage history found"] = "未找到本地使用记录",
            ["Agent activity"] = "智能体活动",
            ["Monitoring"] = "监控中",
            ["Activity Widget"] = "活动小组件",
            ["No recent agent activity"] = "暂无最近的智能体活动",
            ["Cost"] = "成本",
            ["Open TaskbarQuota"] = "打开 TaskbarQuota",
            ["Move usage widget"] = "移动用量小组件",
            ["Reset widget positions"] = "重置小组件位置",
            ["Quit"] = "退出",
            ["1 day"] = "1 天",
            ["7 days"] = "7 天",
            ["30 days"] = "30 天",
            ["90 days"] = "90 天",
            ["Activity provider"] = "活动服务",
            ["Agent activity list"] = "智能体活动列表",
            ["Breakdown"] = "明细",
            ["Cache savings"] = "缓存节省",
            ["Cached input"] = "缓存输入",
            ["Can't pin this provider"] = "无法固定此服务",
            ["Changes since last session"] = "与上次运行相比",
            ["Critical threshold"] = "严重阈值",
            ["Daily usage by provider"] = "各服务每日用量",
            ["Dismiss onboarding"] = "关闭使用说明",
            ["Enable quota alerts"] = "启用额度警报",
            ["Fix"] = "修复",
            ["Fix credentials"] = "修复凭据",
            ["Floating"] = "悬浮",
            ["Floating acrylic strength"] = "悬浮窗亚克力强度",
            ["Floating usage content"] = "悬浮用量内容",
            ["Hide unavailable providers"] = "隐藏不可用服务",
            ["Hide widget when no AI app is focused"] = "未聚焦 AI 应用时隐藏小组件",
            ["Learn more"] = "了解更多",
            ["Login with Claude"] = "使用 Claude 登录",
            ["Monitor local agent activity"] = "监控本地智能体活动",
            ["Native WinUI widget for AI coding-tool usage."] = "原生 WinUI AI 编程工具用量小组件。",
            ["New update available!"] = "有可用更新！",
            ["Next agent"] = "下一个智能体",
            ["Notification cooldown"] = "通知冷却时间",
            ["Notify about quota increases since the last session"] = "通知自上次运行后的额度增加",
            ["Notify when quota is replenished"] = "额度恢复时通知",
            ["Open agent activity"] = "打开智能体活动",
            ["Open settings"] = "打开设置",
            ["Open usage dashboard"] = "打开用量仪表板",
            ["Output"] = "输出",
            ["Previous agent"] = "上一个智能体",
            ["Processed tokens"] = "已处理 Token",
            ["Provider summary"] = "服务摘要",
            ["Quota pricing"] = "额度计价",
            ["Quota replenishment"] = "额度恢复",
            ["Reading usage history…"] = "正在读取使用记录…",
            ["Share"] = "分享",
            ["Share cost summary"] = "分享成本摘要",
            ["Share cost summary as an image"] = "将成本摘要分享为图片",
            ["Share provider summary"] = "分享服务摘要",
            ["Show agent activity in usage widget"] = "在用量小组件中显示智能体活动",
            ["Show in usage widget"] = "在用量小组件中显示",
            ["Show usage as floating window"] = "以悬浮窗显示用量",
            ["Show usage as a floating always-on-top window instead of the taskbar"] = "以悬浮置顶窗口显示用量，而不是显示在任务栏",
            ["Keep this provider in the usage widget even when another tool is active"] = "即使切换到其他工具，也在用量小组件中保留此服务",
            ["Copy this provider summary as an image"] = "将此服务摘要复制为图片",
            ["Summary metric"] = "摘要指标",
            ["Taskbar screens"] = "任务栏屏幕",
            ["Tokens"] = "Token 数",
            ["Uncached input"] = "未缓存输入",
            ["Usage breakdown"] = "用量明细",
            ["Usage reporting period"] = "用量统计周期",
            ["Use AI normally"] = "照常使用 AI",
            ["Warning threshold"] = "警告阈值",
            ["Now"] = "现在",
        };

    public static string Get(string key)
        => Values.TryGetValue(key, out var value) ? value : key;

    public static string TranslateLabel(string? label)
        => string.IsNullOrWhiteSpace(label) ? string.Empty : Get(label);

    public static string FormatDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return string.Empty;

        if (string.Equals(duration.Trim(), "now", StringComparison.OrdinalIgnoreCase))
            return Get("Now");

        return DurationTokenRegex().Replace(
                duration,
                static match => match.Groups["value"].Value
                    + (match.Groups["unit"].Value.ToLowerInvariant() switch
                    {
                        "d" => "天",
                        "h" => "小时",
                        "m" => "分",
                        "s" => "秒",
                        _ => match.Value,
                    }))
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    public static string FormatResetDescription(string? duration)
    {
        var formatted = FormatDuration(duration);
        return string.IsNullOrEmpty(formatted)
            ? string.Empty
            : string.Equals(formatted, Get("Now"), StringComparison.Ordinal)
                ? "即将重置"
                : $"{formatted}后重置";
    }

    public static string FormatAvailability(int count)
        => $"{count.ToString("N0", CultureInfo.CurrentCulture)} 次可用";

    public static string FormatUpdatedAt(DateTime value)
        => $"更新于 {value:HH:mm:ss}";

    public static string FormatResetAt(DateTimeOffset value)
        => $"{value:yyyy年M月d日 HH:mm} 重置";

    public static string FormatTokens(long tokens)
    {
        if (tokens >= 1_000_000_000)
            return $"{tokens / 1_000_000_000d:0.##}B Token";
        if (tokens >= 1_000_000)
            return $"{tokens / 1_000_000d:0.##}M Token";
        if (tokens >= 1_000)
            return $"{tokens / 1_000d:0.##}K Token";
        return $"{tokens:N0} Token";
    }

    public static string FormatTokens(ulong tokens)
    {
        if (tokens >= 1_000_000_000)
            return $"{tokens / 1_000_000_000d:0.##}B Token";
        if (tokens >= 1_000_000)
            return $"{tokens / 1_000_000d:0.##}M Token";
        if (tokens >= 1_000)
            return $"{tokens / 1_000d:0.##}K Token";
        return $"{tokens:N0} Token";
    }

    public static string FormatDetectedVia(string source)
        => $"通过 {source} 检测";

    public static string FormatShortVia(string source)
        => $"经 {source}";

    [GeneratedRegex(@"(?<value>\d+)\s*(?<unit>[dhms])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationTokenRegex();
}
