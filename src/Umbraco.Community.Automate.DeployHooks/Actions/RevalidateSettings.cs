using Umbraco.Automate.Core.Settings;

namespace Umbraco.Community.Automate.DeployHooks.Actions;

/// <summary>Settings for <c>community.deployHooks.revalidate</c>. Property names are permanent once released.</summary>
public sealed class RevalidateSettings
{
    /// <summary>Site paths, one per line.</summary>
    [Field(
        Label = "Paths",
        Description = "One path per line, for example the Url output of a Get content step. A full URL is cut to its path. Anything that isn't a site path is dropped with a warning.",
        EditorUiAlias = "Umb.PropertyEditorUi.TextArea",
        SupportsBindings = true,
        SortOrder = 1)]
    public string? Paths { get; set; }

    /// <summary>Next.js cache tags, one per line.</summary>
    [Field(
        Label = "Tags",
        Description = "One cache tag per line. Letters, digits and : _ - . / only, up to 256 characters. Others are dropped with a warning.",
        EditorUiAlias = "Umb.PropertyEditorUi.TextArea",
        SupportsBindings = true,
        SortOrder = 2)]
    public string? Tags { get; set; }
}
