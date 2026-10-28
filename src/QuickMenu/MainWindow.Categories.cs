using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using QuickMenu.Models;
using QuickMenu.Services;

namespace QuickMenu;

/// <summary>
/// Categories: the user's own categories (rules plus items marked by hand) on top of the automatic classification,
/// their tabs and menus, and the rule editor in settings. Changes save at once, like the game library.
/// </summary>
public partial class MainWindow
{
    private static readonly DesktopCategory[] ExtensionCategories = [DesktopCategory.Documents, DesktopCategory.Spreadsheets, DesktopCategory.Presentations, DesktopCategory.Media];
    private static string[] BuiltInGroups => [Loc.T("Group.Programs"), Loc.T("Group.Games"), Loc.T("Group.Office"), Loc.T("Group.Media")];
    private readonly CategoryService _categoryService = new();
    private CategorySettings _categories = new();
    // What the editor is changing: a built-in rule ("Games", "AiTools", "Documents"…), a custom category, or a new one.
    private string? _editingBuiltIn;
    private CustomCategory? _editingCategory;

    internal CategorySettings Categories => _categories;

    private void InitializeCategories()
    {
        _categories = _isQa ? new() : _categoryService.Load();
        DesktopCategories.Configure(_categories);
        RenderCategoryTabs();
    }

    /// <summary>
    /// The custom category an item is in: one it was marked with by hand, otherwise the first whose rules match, unless
    /// it was marked with a built-in category by hand.
    /// </summary>
    internal CustomCategory? CustomCategoryOf(DesktopItem item)
    {
        if (_categories.Custom.Count == 0) return null;
        var preference = _desktopPreferences.Get(item.Id);
        if (preference.Custom is { } id && _categories.Custom.FirstOrDefault(category => category.Id == id) is { } marked) return marked;
        if (preference.Category != null) return null;
        var target = item.Location ?? DesktopCategoryService.GetShortcutTarget(item.ParsingName);
        return _categories.Custom.FirstOrDefault(category => category.HasRules && category.Matches(item.Name, item.ParsingName, target));
    }

    internal bool IsAiTool(DesktopItem item) => _desktopPreferences.Get(item.Id).AiTool ?? item.IsAiTool;

    private bool InDesktopFilter(DesktopItem item) => _session.CustomCategory is { } custom
        ? CustomCategoryOf(item)?.Id == custom
        : DesktopCategories.Matches(_session.DesktopCategory, _desktopPreferences.Category(item), IsAiTool(item));

    private string GroupOf(DesktopItem item) => CustomCategoryOf(item)?.Name ?? _desktopPreferences.Category(item) switch
    {
        DesktopCategory.Office or DesktopCategory.Documents or DesktopCategory.Spreadsheets or DesktopCategory.Presentations => Loc.T("Group.Office"),
        DesktopCategory.Media => Loc.T("Group.Media"), DesktopCategory.Games => Loc.T("Group.Games"), _ => Loc.T("Group.Programs")
    };

    /// <summary>Built-in tabs that can be hidden, then one tab per custom category shown as a tab, after 文档+.</summary>
    private void RenderCategoryTabs()
    {
        foreach (var old in CategoryBar.Children.OfType<Button>().Where(button => button.Tag is string tag && tag.StartsWith("custom:", StringComparison.Ordinal)).ToArray())
            CategoryBar.Children.Remove(old);
        GamesCategory.Visibility = _categories.ShowGamesTab ? Visibility.Visible : Visibility.Collapsed;
        AiCategory.Visibility = _categories.ShowAiTab ? Visibility.Visible : Visibility.Collapsed;
        foreach (var category in _categories.Custom.Where(category => category.ShowAsTab))
        {
            var tab = new Button { Content = category.Name, Tag = "custom:" + category.Id };
            System.Windows.Automation.AutomationProperties.SetName(tab, category.Name + Loc.T("Settings.Categories"));
            tab.Click += Category_Click;
            CategoryBar.Children.Add(tab);
        }
        _desktopGroups.GroupNames.Clear();
        foreach (var name in BuiltInGroups.Concat(_categories.Custom.Select(category => category.Name)).Distinct()) _desktopGroups.GroupNames.Add(name);
        if (_session.CustomCategory is { } selected && _categories.Custom.All(category => category.Id != selected)) _session.CustomCategory = null;
        UpdateCategoryButtons();
    }

    internal void SelectCustomCategory(string id)
    {
        var changed = _session.CustomCategory != id;
        _session.DesktopCategory = DesktopCategory.All;
        _session.CustomCategory = id;
        _session.Selections[MenuPage.Desktop] = 0;
        _desktopView.Refresh(); UpdateCategoryButtons(); RestoreSelection(); UpdateEmptyState();
        if (changed) PlayCategoryTransition();
    }

    private string CategoryMenuLabel() => _session.CustomCategory is { } id
        ? (_categories.Custom.FirstOrDefault(category => category.Id == id) is { ShowAsTab: false } hidden ? hidden.Name + "⌄" : Loc.T("Tab.Categories"))
        : _session.DesktopCategory switch
        {
            DesktopCategory.Office => Loc.T("Menu.Office"), DesktopCategory.Documents => Loc.T("Menu.Documents"), DesktopCategory.Spreadsheets => Loc.T("Menu.Spreadsheets"), DesktopCategory.Presentations => Loc.T("Menu.Presentations"),
            DesktopCategory.Media => Loc.T("Menu.Media"), DesktopCategory.Programs => Loc.T("Menu.Programs"), DesktopCategory.Other => Loc.T("Menu.Other"),
            DesktopCategory.Games when !_categories.ShowGamesTab => Loc.T("Menu.Games"), DesktopCategory.AiTools when !_categories.ShowAiTab => Loc.T("Menu.AiTools"), _ => Loc.T("Tab.Categories")
        };

    private string SelectedCategoryTag => _session.CustomCategory is { } id ? "custom:" + id : _session.DesktopCategory.ToString();

    // Items of the 分类 menu beyond the fixed ones: hidden built-in tabs and custom categories without a tab.
    private void AddCategoryMenuExtras(ContextMenu menu)
    {
        if (!_categories.ShowGamesTab) menu.Items.Add(MenuAction(Loc.T("Category.Games"), () => SelectCategory(DesktopCategory.Games), _session.CustomCategory is null && _session.DesktopCategory == DesktopCategory.Games));
        if (!_categories.ShowAiTab) menu.Items.Add(MenuAction(Loc.T("Category.AiTools"), () => SelectCategory(DesktopCategory.AiTools), _session.CustomCategory is null && _session.DesktopCategory == DesktopCategory.AiTools));
        var hidden = _categories.Custom.Where(category => !category.ShowAsTab).ToArray();
        if (hidden.Length == 0) return;
        menu.Items.Add(new Separator());
        foreach (var category in hidden) menu.Items.Add(MenuAction(category.Name, () => SelectCustomCategory(category.Id), _session.CustomCategory == category.Id));
    }

    // Right-click: custom categories join 标记分类, and AI tools can be marked or unmarked by hand.
    private void AddItemCategoryActions(ContextMenu menu, MenuItem categories, DesktopItem item)
    {
        var preference = _desktopPreferences.Get(item.Id);
        if (_categories.Custom.Count > 0)
        {
            categories.Items.Add(new Separator());
            foreach (var category in _categories.Custom)
                categories.Items.Add(MenuAction(category.Name, () => ApplyDesktopPreference(_desktopPreferences.SetCustom(item.Id, category.Id)), preference.Custom == category.Id));
        }
        var ai = IsAiTool(item);
        menu.Items.Add(MenuAction(Loc.T("Item.AiTool"), () => ApplyDesktopPreference(_desktopPreferences.SetAiTool(item.Id, !ai)), ai));
    }

    internal void PreviewCategoryEditor(bool open) { if (open) EditCustom(null); else CloseOverlay(); }

    internal void PreviewCategories(CategorySettings settings)
    {
        _categories = settings;
        DesktopCategories.Configure(settings);
        RenderCategoryTabs();
        _desktopView.Refresh(); UpdateEmptyState();
        if (_session.Page == MenuPage.Settings) RenderCategorySettings();
    }

    private void SaveCategories(string? done = null)
    {
        if (!_isQa)
        {
            var saved = _categoryService.Save(_categories);
            if (!saved.Success) { SetStatus(saved.Message); return; }
        }
        DesktopCategories.Configure(_categories);
        DesktopCategoryService.ClearCache();
        RenderCategoryTabs();
        RenderCategorySettings();
        _desktopView.Refresh(); UpdateEmptyState();
        SetStatus(done ?? Loc.T("Category.Saved"));
        // Built-in rules are applied while reading the desktop, so it is read again.
        if (!_isQa) _ = WarmDesktopAsync();
    }

    // ---- Settings: the 分类 section --------------------------------------------------------------------------------

    private void RenderCategorySettings()
    {
        CategoryRows.Children.Clear();
        CategoryRows.Children.Add(CategoryRow(Loc.T("Category.Games"), Loc.T("Category.FolderRule") + Summary(_categories.GameFolders ?? DesktopCategories.DefaultGameFolders), _categories.ShowGamesTab,
            shown => { _categories.ShowGamesTab = shown; SaveCategories(); }, () => EditBuiltIn("Games")));
        CategoryRows.Children.Add(CategoryRow(Loc.T("Category.AiTools"), Loc.T("Category.WordRule") + Summary(_categories.AiKeywords ?? DesktopCategories.DefaultAiKeywords), _categories.ShowAiTab,
            shown => { _categories.ShowAiTab = shown; SaveCategories(); }, () => EditBuiltIn("AiTools")));
        foreach (var category in ExtensionCategories)
            CategoryRows.Children.Add(CategoryRow(Loc.T("Category." + category), Loc.T("Category.ExtensionRule") + Summary(BuiltInExtensions(category)), null, null, () => EditBuiltIn(category.ToString())));
        for (var index = 0; index < _categories.Custom.Count; index++)
        {
            var category = _categories.Custom[index];
            var position = index;
            var rules = new[] { category.Keywords.Count > 0 ? Loc.T("Category.NameRule") + Summary(category.Keywords) : null, category.Extensions.Count > 0 ? Loc.T("Category.ExtensionRule") + Summary(category.Extensions) : null, category.PathContains.Count > 0 ? Loc.T("Category.PathRule") + Summary(category.PathContains) : null }.OfType<string>().ToArray();
            CategoryRows.Children.Add(CategoryRow(category.Name, rules.Length > 0 ? string.Join(Loc.T("Category.RuleSeparator"), rules) : Loc.T("Category.ManualOnly"), category.ShowAsTab,
                shown => { category.ShowAsTab = shown; SaveCategories(); }, () => EditCustom(category),
                position > 0 ? () => MoveCategory(position, -1) : null, position < _categories.Custom.Count - 1 ? () => MoveCategory(position, 1) : null,
                () => { _categories.Custom.Remove(category); SaveCategories(Loc.F("Category.Deleted", category.Name)); }));
        }
    }

    private IReadOnlyList<string> BuiltInExtensions(DesktopCategory category) =>
        _categories.Extensions?.GetValueOrDefault(category.ToString()) ?? DesktopCategories.DefaultExtensions[category];

    private static string Summary(IEnumerable<string> values)
    {
        var list = values.ToArray();
        return list.Length == 0 ? Loc.T("Category.None") : string.Join(Loc.T("Category.ListSeparator"), list.Take(8)) + (list.Length > 8 ? Loc.F("Category.More", list.Length) : "");
    }

    private FrameworkElement CategoryRow(string name, string detail, bool? tab, Action<bool>? setTab, Action edit, Action? up = null, Action? down = null, Action? delete = null)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // A fixed width keeps every row's 编辑 button, and so the 标签 switches, in one column.
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        var title = new TextBlock { Text = name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = name };
        var text = new TextBlock { Text = detail, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = detail, Margin = new Thickness(0, 0, 10, 0) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Grid.SetColumn(text, 1);
        row.Children.Add(title); row.Children.Add(text);
        if (tab is { } shown && setTab != null)
        {
            var toggle = new CheckBox { Content = Loc.T("Category.Tab"), IsChecked = shown, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), ToolTip = Loc.T("Category.TabTip") };
            System.Windows.Automation.AutomationProperties.SetName(toggle, Loc.F("Category.TabName", name));
            toggle.Click += (_, _) => setTab(toggle.IsChecked == true);
            Grid.SetColumn(toggle, 2);
            row.Children.Add(toggle);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actions, 3);
        var editButton = new Button { Content = Loc.T("Category.Edit"), Padding = new Thickness(12, 4, 12, 4), FontSize = 12 };
        System.Windows.Automation.AutomationProperties.SetName(editButton, Loc.F("Category.EditName", name));
        editButton.Click += (_, _) => edit();
        actions.Children.Add(editButton);
        foreach (var (glyph, label, action) in new[] { ("", Loc.T("Category.Up"), up), ("", Loc.T("Category.Down"), down), ("", Loc.T("Category.Delete"), delete) })
        {
            if (delete is null) break;
            var button = new Button { Style = (Style)FindResource("ToolbarButton"), Width = 28, Height = 28, Margin = new Thickness(4, 0, 0, 0), IsEnabled = action != null, ToolTip = label,
                Content = new TextBlock { Text = glyph, Style = (Style)FindResource("Glyph"), FontSize = 12 } };
            System.Windows.Automation.AutomationProperties.SetName(button, $"{label}{name}");
            if (action != null) button.Click += (_, _) => action();
            actions.Children.Add(button);
        }
        row.Children.Add(actions);
        return row;
    }

    private void MoveCategory(int index, int delta)
    {
        var category = _categories.Custom[index];
        _categories.Custom.RemoveAt(index);
        _categories.Custom.Insert(index + delta, category);
        SaveCategories();
    }

    // ---- The rule editor (an overlay dialog) ---------------------------------------------------------------------

    private void NewCategory_Click(object sender, RoutedEventArgs e) => EditCustom(null);

    private void EditCustom(CustomCategory? category)
    {
        _editingBuiltIn = null; _editingCategory = category;
        CategoryEditorTitle.Text = category is null ? Loc.T("Category.NewTitle") : Loc.F("Category.EditTitle", category.Name);
        CategoryNameBox.Text = category?.Name ?? "";
        CategoryKeywordsBox.Text = string.Join(", ", category?.Keywords ?? []);
        CategoryExtensionsBox.Text = string.Join(", ", category?.Extensions ?? []);
        CategoryPathBox.Text = string.Join(", ", category?.PathContains ?? []);
        CategoryTabCheck.IsChecked = category?.ShowAsTab ?? true;
        ShowCategoryEditor(name: true, keywords: true, extensions: true, path: true, restore: false);
    }

    private void EditBuiltIn(string key)
    {
        _editingBuiltIn = key; _editingCategory = null;
        CategoryEditorTitle.Text = Loc.F("Category.RulesTitle", Loc.T("Category." + key));
        CategoryKeywordsBox.Text = string.Join(", ", _categories.AiKeywords ?? DesktopCategories.DefaultAiKeywords);
        CategoryPathBox.Text = string.Join(", ", _categories.GameFolders ?? DesktopCategories.DefaultGameFolders);
        CategoryExtensionsBox.Text = Enum.TryParse<DesktopCategory>(key, out var category) && DesktopCategories.DefaultExtensions.ContainsKey(category) ? string.Join(", ", BuiltInExtensions(category)) : "";
        ShowCategoryEditor(name: false, keywords: key == "AiTools", extensions: key is not ("Games" or "AiTools"), path: key == "Games", restore: true);
    }

    private void ShowCategoryEditor(bool name, bool keywords, bool extensions, bool path, bool restore)
    {
        CategoryNameRow.Visibility = name ? Visibility.Visible : Visibility.Collapsed;
        CategoryKeywordsRow.Visibility = keywords ? Visibility.Visible : Visibility.Collapsed;
        CategoryExtensionsRow.Visibility = extensions ? Visibility.Visible : Visibility.Collapsed;
        CategoryPathRow.Visibility = path ? Visibility.Visible : Visibility.Collapsed;
        CategoryPathLabel.Text = _editingBuiltIn == "Games" ? Loc.T("Category.GamesPathLabel") : Loc.T("Category.PathLabel");
        CategoryKeywordsLabel.Text = _editingBuiltIn == "AiTools" ? Loc.T("Category.AiWordsLabel") : Loc.T("Category.WordsLabel");
        CategoryTabCheck.Visibility = name ? Visibility.Visible : Visibility.Collapsed;
        CategoryRestore.Visibility = restore ? Visibility.Visible : Visibility.Collapsed;
        ShowOverlay(CategoryEditor);
        (name ? CategoryNameBox : keywords ? CategoryKeywordsBox : extensions ? CategoryExtensionsBox : CategoryPathBox).Focus();
    }

    private void CategoryEditorSave_Click(object sender, RoutedEventArgs e)
    {
        var keywords = CategoryService.SplitList(CategoryKeywordsBox.Text);
        var extensions = CategoryService.SplitList(CategoryExtensionsBox.Text, spacesSeparate: true).Select(extension => extension.TrimStart('.').ToLowerInvariant()).Where(extension => extension.Length > 0).Distinct().ToList();
        var paths = CategoryService.SplitList(CategoryPathBox.Text);
        switch (_editingBuiltIn)
        {
            case "Games": _categories.GameFolders = paths; break;
            case "AiTools": _categories.AiKeywords = keywords; break;
            case { } key: (_categories.Extensions ??= new())[key] = extensions; break;
            default:
                var name = CategoryNameBox.Text.Trim();
                if (name.Length == 0) { SetStatus(Loc.T("Category.NameRequired")); CategoryNameBox.Focus(); return; }
                var category = _editingCategory ?? new CustomCategory();
                category.Name = name; category.Keywords = keywords; category.Extensions = extensions; category.PathContains = paths;
                category.ShowAsTab = CategoryTabCheck.IsChecked == true;
                if (_editingCategory is null) _categories.Custom.Add(category);
                break;
        }
        CloseOverlay();
        SaveCategories();
    }

    private void CategoryEditorRestore_Click(object sender, RoutedEventArgs e)
    {
        switch (_editingBuiltIn)
        {
            case "Games": _categories.GameFolders = null; break;
            case "AiTools": _categories.AiKeywords = null; break;
            case { } key: _categories.Extensions?.Remove(key); break;
        }
        CloseOverlay();
        SaveCategories(Loc.T("Category.Restored"));
    }

    private void CategoryEditorCancel_Click(object sender, RoutedEventArgs e) => CloseOverlay();
}
