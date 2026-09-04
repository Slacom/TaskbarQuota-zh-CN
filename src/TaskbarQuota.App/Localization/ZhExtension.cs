using Microsoft.UI.Xaml.Markup;

namespace TaskbarQuota.Localization;

[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class ZhExtension : MarkupExtension
{
    public string Text { get; set; } = string.Empty;

    protected override object ProvideValue()
        => UiText.Get(Text);
}
