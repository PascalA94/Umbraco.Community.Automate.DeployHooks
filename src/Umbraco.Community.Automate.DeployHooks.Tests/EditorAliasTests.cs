using System.Reflection;
using Umbraco.Automate.Core.Settings;
using Umbraco.Community.Automate.DeployHooks.Connection;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

public class EditorAliasTests
{
    [Fact]
    public void ProviderField_UsesTheSingleChoiceDropdownOfItsLine()
    {
        // CMS 19's plain Dropdown is always multi-select and Dropdown.Single only exists from 19.
#if AUTOMATE_19
        const string expected = "Umb.PropertyEditorUi.Dropdown.Single";
#else
        const string expected = "Umb.PropertyEditorUi.Dropdown";
#endif
        var field = typeof(DeployHookConnectionSettings).GetProperty(nameof(DeployHookConnectionSettings.Provider))!
            .GetCustomAttribute<FieldAttribute>()!;
        Assert.Equal(expected, field.EditorUiAlias);
    }
}
