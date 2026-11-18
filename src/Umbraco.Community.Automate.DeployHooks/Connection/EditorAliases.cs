namespace Umbraco.Community.Automate.DeployHooks.Connection;

internal static class EditorAliases
{
    // On CMS 19 the plain Dropdown editor is always multi-select and Dropdown.Single only exists from 19, so the single
    // choice provider field needs a different alias per line. Attributes need a constant, hence the compile-time switch.
#if AUTOMATE_19
    public const string SingleDropdown = "Umb.PropertyEditorUi.Dropdown.Single";
#else
    public const string SingleDropdown = "Umb.PropertyEditorUi.Dropdown";
#endif
}
