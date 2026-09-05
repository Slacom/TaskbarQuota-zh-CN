using TaskbarQuota.Localization;

namespace TaskbarQuota.Usage
{
    public enum ProviderSourceKind
    {
        Unknown,
        Browser,
        DesktopApp,
        Cli,
        HostApp,
    }

    public sealed record ProviderSource(
        ProviderSourceKind Kind,
        string? Name = null,
        string? IconKey = null)
    {
        public static ProviderSource Unknown { get; } = new(ProviderSourceKind.Unknown);

        public bool IsKnown => Kind != ProviderSourceKind.Unknown;

        public string DisplayName => string.IsNullOrWhiteSpace(Name)
            ? Kind switch
            {
                ProviderSourceKind.Browser => "浏览器",
                ProviderSourceKind.DesktopApp => "桌面应用",
                ProviderSourceKind.Cli => "终端",
                ProviderSourceKind.HostApp => "宿主应用",
                _ => string.Empty,
            }
            : Name!;

        public string SourceText => IsKnown ? UiText.FormatDetectedVia(DisplayName) : string.Empty;
        public string ShortViaText => IsKnown ? UiText.FormatShortVia(DisplayName) : string.Empty;
    }
}
