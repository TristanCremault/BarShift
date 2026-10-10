using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BarShift;

public partial class SettingsWindow : Window
{
    private const string DragFormat = "BarShiftApp";

    private sealed record DragPayload(Profile Source, AppEntry App);

    private readonly BarConfig _config;
    private readonly Action<BarConfig> _onSave;
    private bool _loading = true;
    private Point _dragStart;
    private int _dragIndex = -1;

    public SettingsWindow(BarConfig source, Action<BarConfig> onSave)
    {
        InitializeComponent();
        _onSave = onSave;

        // On travaille sur une copie : "Annuler" ne change rien
        _config = JsonSerializer.Deserialize<BarConfig>(JsonSerializer.Serialize(source))
                  ?? new BarConfig();

        NextHotkeyBox.Text = _config.NextProfileHotkey ?? "";
        PrevHotkeyBox.Text = _config.PreviousProfileHotkey ?? "";

        WidgetOptions w = _config.Widgets;
        ChkStart.IsChecked = w.ShowStart;
        ChkSearch.IsChecked = w.ShowSearch;
        ChkTaskView.IsChecked = w.ShowTaskView;
        ChkWifi.IsChecked = w.ShowWifi;
        ChkVolume.IsChecked = w.ShowVolume;
        ChkBattery.IsChecked = w.ShowBattery;
        ChkClock.IsChecked = w.ShowClock;
        ChkDesktop.IsChecked = w.ShowDesktop;
        ChkHideTaskbar.IsChecked = _config.HideWindowsTaskbar;
        ChkFullscreen.IsChecked = _config.HideInFullscreen;
        ChkAutoStart.IsChecked = StartupManager.IsEnabled();

        BuildPalette();
        RefreshBarsList(0);
    }

    private Profile? CurrentProfile =>
        BarsList.SelectedIndex >= 0 && BarsList.SelectedIndex < _config.Profiles.Count
            ? _config.Profiles[BarsList.SelectedIndex]
            : null;

    private AppEntry? CurrentApp
    {
        get
        {
            Profile? profile = CurrentProfile;
            int index = AppsList.SelectedIndex;
            return profile != null && index >= 0 && index < profile.Apps.Count
                ? profile.Apps[index]
                : null;
        }
    }

    // ---------- Palette de couleurs ----------

    private void BuildPalette()
    {
        double[] lightness = { 0.10, 0.17, 0.25, 0.35, 0.50, 0.65, 0.80 };

        foreach (double l in lightness)
        {
            for (int hue = 0; hue < 360; hue += 30)
            {
                AddSwatch(FromHsl(hue, 0.65, l));
            }
        }

        // Dernière ligne : nuances de gris, du noir au blanc
        for (int i = 0; i < 12; i++)
        {
            byte v = (byte)Math.Round(255.0 * i / 11);
            AddSwatch(Color.FromRgb(v, v, v));
        }
    }

    private void AddSwatch(Color color)
    {
        string hex = ToHex(color);

        var swatch = new Border
        {
            Width = 22,
            Height = 22,
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(color),
            Cursor = Cursors.Hand,
            ToolTip = hex
        };
        swatch.MouseLeftButtonUp += (_, _) =>
        {
            ColorBox.Text = hex;
            ColorPopup.IsOpen = false;
        };
        PalettePanel.Children.Add(swatch);
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        double c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = lightness - c / 2;

        double r, g, b;
        if (hue < 60) { r = c; g = x; b = 0; }
        else if (hue < 120) { r = x; g = c; b = 0; }
        else if (hue < 180) { r = 0; g = c; b = x; }
        else if (hue < 240) { r = 0; g = x; b = c; }
        else if (hue < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private void ColorSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ColorPopup.IsOpen = true;
    }

    // ---------- Liste des barres ----------

    private void RefreshBarsList(int select)
    {
        _loading = true;
        BarsList.Items.Clear();
        foreach (Profile profile in _config.Profiles)
        {
            BarsList.Items.Add(string.IsNullOrWhiteSpace(profile.Name) ? "(sans nom)" : profile.Name);
        }
        BarsList.SelectedIndex = Math.Clamp(select, 0, _config.Profiles.Count - 1);
        _loading = false;

        LoadProfileFields();
    }

    private void LoadProfileFields()
    {
        Profile? profile = CurrentProfile;

        _loading = true;
        DetailsPanel.IsEnabled = profile != null;
        NameBox.Text = profile?.Name ?? "";
        ColorBox.Text = profile?.Color ?? "";
        HotkeyBox.Text = profile?.Hotkey ?? "";
        UpdateSwatch();
        _loading = false;

        RefreshAppsList(0);
    }

    private void UpdateSwatch()
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(ColorBox.Text);
            ColorSwatch.Background = new SolidColorBrush(color);
        }
        catch
        {
            ColorSwatch.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x30));
        }
    }

    private void BarsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        LoadProfileFields();
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        Profile? profile = CurrentProfile;
        if (profile == null) return;

        profile.Name = NameBox.Text;

        int index = BarsList.SelectedIndex;
        _loading = true;
        BarsList.Items[index] = string.IsNullOrWhiteSpace(profile.Name) ? "(sans nom)" : profile.Name;
        BarsList.SelectedIndex = index;
        _loading = false;
    }

    private void ColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        Profile? profile = CurrentProfile;
        if (profile == null) return;

        profile.Color = ColorBox.Text;
        UpdateSwatch();
    }

    private void HotkeyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        Profile? profile = CurrentProfile;
        if (profile == null) return;

        profile.Hotkey = HotkeyBox.Text;
    }

    // Capture d'une combinaison de touches dans une case de raccourci
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var box = (TextBox)sender;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.LeftCtrl || key == Key.RightCtrl
            || key == Key.LeftAlt || key == Key.RightAlt
            || key == Key.LeftShift || key == Key.RightShift
            || key == Key.LWin || key == Key.RWin
            || key == Key.None)
        {
            return;
        }

        if (key == Key.Back || key == Key.Delete)
        {
            box.Text = "";
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None) return;

        box.Text = HotkeyHelper.Format(modifiers, key);
    }

    private void AddBar_Click(object sender, RoutedEventArgs e)
    {
        _config.Profiles.Add(new Profile
        {
            Name = $"Nouvelle barre {_config.Profiles.Count + 1}",
            Color = "#0B0B1A",
            Hotkey = ""
        });
        RefreshBarsList(_config.Profiles.Count - 1);
    }

    private void DeleteBar_Click(object sender, RoutedEventArgs e)
    {
        Profile? profile = CurrentProfile;
        if (profile == null) return;

        if (_config.Profiles.Count <= 1)
        {
            MessageBox.Show(this, "Il faut garder au moins une barre.", "BarShift",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(this, $"Supprimer la barre « {profile.Name} » ?", "BarShift",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        int index = BarsList.SelectedIndex;
        _config.Profiles.RemoveAt(index);
        RefreshBarsList(Math.Min(index, _config.Profiles.Count - 1));
    }

    private void BarUp_Click(object sender, RoutedEventArgs e) => MoveBar(-1);

    private void BarDown_Click(object sender, RoutedEventArgs e) => MoveBar(1);

    private void MoveBar(int delta)
    {
        int i = BarsList.SelectedIndex;
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _config.Profiles.Count) return;

        (_config.Profiles[i], _config.Profiles[j]) = (_config.Profiles[j], _config.Profiles[i]);
        RefreshBarsList(j);
    }

    // ---------- Glisser-déposer ----------

    private static int IndexAt(ListBox list, object? source)
    {
        if (source is DependencyObject element
            && ItemsControl.ContainerFromElement(list, element) is ListBoxItem item)
        {
            return list.ItemContainerGenerator.IndexFromContainer(item);
        }
        return -1;
    }

    private void AppsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragIndex = IndexAt(AppsList, e.OriginalSource);
    }

    private void AppsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragIndex < 0) return;

        Point position = e.GetPosition(null);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        Profile? profile = CurrentProfile;
        if (profile == null || _dragIndex >= profile.Apps.Count) return;

        var data = new DataObject(DragFormat, new DragPayload(profile, profile.Apps[_dragIndex]));
        _dragIndex = -1;
        DragDrop.DoDragDrop(AppsList, data, DragDropEffects.Move | DragDropEffects.Copy);
    }

    // Réorganiser les applications dans la même barre
    private void AppsList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void AppsList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is not DragPayload payload) return;

        Profile? profile = CurrentProfile;
        if (profile == null || payload.Source != profile) return;

        int from = profile.Apps.IndexOf(payload.App);
        if (from < 0) return;

        int to = IndexAt(AppsList, e.OriginalSource);
        if (to < 0) to = profile.Apps.Count - 1;
        if (from == to) return;

        profile.Apps.RemoveAt(from);
        profile.Apps.Insert(to, payload.App);
        RefreshAppsList(to);
        e.Handled = true;
    }

    // Déplacer (ou copier avec Ctrl) une application vers une autre barre
    private void BarsList_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragFormat))
        {
            e.Effects = DragDropEffects.None;
        }
        else
        {
            e.Effects = e.KeyStates.HasFlag(DragDropKeyStates.ControlKey)
                ? DragDropEffects.Copy
                : DragDropEffects.Move;
        }
        e.Handled = true;
    }

    private void BarsList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is not DragPayload payload) return;

        int target = IndexAt(BarsList, e.OriginalSource);
        if (target < 0 || target >= _config.Profiles.Count) return;

        Profile targetProfile = _config.Profiles[target];
        if (targetProfile == payload.Source) return;

        bool copy = e.KeyStates.HasFlag(DragDropKeyStates.ControlKey);
        if (copy)
        {
            targetProfile.Apps.Add(new AppEntry
            {
                Name = payload.App.Name,
                Path = payload.App.Path,
                IconPath = payload.App.IconPath,
                AppId = payload.App.AppId
            });
        }
        else
        {
            payload.Source.Apps.Remove(payload.App);
            targetProfile.Apps.Add(payload.App);
        }

        RefreshAppsList(0);
        e.Handled = true;
    }

    // ---------- Applications de la barre ----------

    private void RefreshAppsList(int select)
    {
        Profile? profile = CurrentProfile;

        _loading = true;
        AppsList.Items.Clear();
        if (profile != null)
        {
            foreach (AppEntry app in profile.Apps)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new Image
                {
                    Width = 20,
                    Height = 20,
                    Margin = new Thickness(0, 0, 10, 0),
                    Source = IconHelper.Load(app.Path, app.IconPath, app.AppId)
                });
                row.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(app.Name) ? "(sans nom)" : app.Name,
                    VerticalAlignment = VerticalAlignment.Center
                });
                AppsList.Items.Add(row);
            }

            AppsList.SelectedIndex = profile.Apps.Count == 0
                ? -1
                : Math.Clamp(select, 0, profile.Apps.Count - 1);
        }
        _loading = false;

        LoadAppFields();
    }

    private void LoadAppFields()
    {
        AppEntry? app = CurrentApp;

        _loading = true;
        AppDetailsPanel.IsEnabled = app != null;
        AppNameBox.Text = app?.Name ?? "";
        AppPathBox.Text = app == null
            ? ""
            : string.IsNullOrEmpty(app.AppId) ? app.Path : "Application Windows (Microsoft Store)";
        AppIconPreview.Source = app != null
            ? IconHelper.Load(app.Path, app.IconPath, app.AppId)
            : null;
        _loading = false;
    }

    private void AppsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        LoadAppFields();
    }

    private void AppNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        AppEntry? app = CurrentApp;
        if (app == null) return;

        app.Name = AppNameBox.Text;

        if (AppsList.SelectedItem is StackPanel row && row.Children.Count > 1
            && row.Children[1] is TextBlock label)
        {
            label.Text = string.IsNullOrWhiteSpace(app.Name) ? "(sans nom)" : app.Name;
        }
    }

    // Ouvre le répertoire des applications installées
    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        Profile? profile = CurrentProfile;
        if (profile == null) return;

        var picker = new AppPickerWindow(this);
        if (picker.ShowDialog() != true || picker.Selected.Count == 0) return;

        profile.Apps.AddRange(picker.Selected);
        RefreshAppsList(profile.Apps.Count - 1);
    }

    private void DeleteApp_Click(object sender, RoutedEventArgs e)
    {
        Profile? profile = CurrentProfile;
        int index = AppsList.SelectedIndex;
        if (profile == null || index < 0 || index >= profile.Apps.Count) return;

        profile.Apps.RemoveAt(index);
        RefreshAppsList(index);
    }

    private void AppUp_Click(object sender, RoutedEventArgs e) => MoveApp(-1);

    private void AppDown_Click(object sender, RoutedEventArgs e) => MoveApp(1);

    private void MoveApp(int delta)
    {
        Profile? profile = CurrentProfile;
        if (profile == null) return;

        int i = AppsList.SelectedIndex;
        int j = i + delta;
        if (i < 0 || j < 0 || j >= profile.Apps.Count) return;

        (profile.Apps[i], profile.Apps[j]) = (profile.Apps[j], profile.Apps[i]);
        RefreshAppsList(j);
    }

    private void BrowseApp_Click(object sender, RoutedEventArgs e)
    {
        AppEntry? app = CurrentApp;
        if (app == null) return;

        var dialog = new OpenFileDialog
        {
            Title = "Choisir une application",
            Filter = "Applications (*.exe)|*.exe|Tous les fichiers (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        app.Path = dialog.FileName;
        app.AppId = "";
        RefreshAppsList(AppsList.SelectedIndex);
    }

    private void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        AppEntry? app = CurrentApp;
        if (app == null) return;

        var dialog = new OpenFileDialog
        {
            Title = "Choisir une icône",
            Filter = "Images et icônes|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.exe|Tous les fichiers (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        app.IconPath = dialog.FileName;
        RefreshAppsList(AppsList.SelectedIndex);
    }

    private void ResetIcon_Click(object sender, RoutedEventArgs e)
    {
        AppEntry? app = CurrentApp;
        if (app == null) return;

        app.IconPath = "";
        RefreshAppsList(AppsList.SelectedIndex);
    }

    // ---------- Enregistrer / annuler ----------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _config.NextProfileHotkey = NextHotkeyBox.Text;
        _config.PreviousProfileHotkey = PrevHotkeyBox.Text;

        _config.Widgets = new WidgetOptions
        {
            ShowStart = ChkStart.IsChecked == true,
            ShowSearch = ChkSearch.IsChecked == true,
            ShowTaskView = ChkTaskView.IsChecked == true,
            ShowWifi = ChkWifi.IsChecked == true,
            ShowVolume = ChkVolume.IsChecked == true,
            ShowBattery = ChkBattery.IsChecked == true,
            ShowClock = ChkClock.IsChecked == true,
            ShowDesktop = ChkDesktop.IsChecked == true
        };

        _config.HideWindowsTaskbar = ChkHideTaskbar.IsChecked == true;
        _config.HideInFullscreen = ChkFullscreen.IsChecked == true;

        StartupManager.SetEnabled(ChkAutoStart.IsChecked == true);

        foreach (Profile profile in _config.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name)) profile.Name = "Barre";
        }

        _onSave(_config);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}

// ---------- Répertoire des applications installées ----------

internal sealed class CatalogApp
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string AppId { get; set; } = "";
}

internal static class AppCatalog
{
    private static readonly Dictionary<string, ImageSource?> IconCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIcon(CatalogApp app)
    {
        string key = app.AppId.Length > 0 ? "aumid:" + app.AppId : app.Path;

        if (!IconCache.TryGetValue(key, out ImageSource? icon))
        {
            icon = IconHelper.Load(app.Path, "", app.AppId);
            IconCache[key] = icon;
        }
        return icon;
    }

    public static List<CatalogApp> Scan()
    {
        var apps = new Dictionary<string, CatalogApp>(StringComparer.OrdinalIgnoreCase);

        // 1. Outils Windows courants
        string sys = Environment.SystemDirectory;
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        AddIfExists(apps, "Bloc-notes", System.IO.Path.Combine(sys, "notepad.exe"));
        AddIfExists(apps, "Calculatrice", System.IO.Path.Combine(sys, "calc.exe"));
        AddIfExists(apps, "Paint", System.IO.Path.Combine(sys, "mspaint.exe"));
        AddIfExists(apps, "Explorateur de fichiers", System.IO.Path.Combine(win, "explorer.exe"));
        AddIfExists(apps, "Gestionnaire des tâches", System.IO.Path.Combine(sys, "taskmgr.exe"));
        AddIfExists(apps, "Invite de commandes", System.IO.Path.Combine(sys, "cmd.exe"));
        AddIfExists(apps, "PowerShell",
            System.IO.Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe"));
        AddIfExists(apps, "Outil Capture d'écran", System.IO.Path.Combine(sys, "SnippingTool.exe"));
        AddIfExists(apps, "Panneau de configuration", System.IO.Path.Combine(sys, "control.exe"));

        // 2. Programmes du menu Démarrer
        AddStartMenuShortcuts(apps);

        // 3. Toutes les applications de Windows (dont Microsoft Store)
        AddShellApps(apps);

        // 4. On garde la version Store quand un outil Windows existe aussi en version classique
        var storeNames = new HashSet<string>(
            apps.Values.Where(a => a.AppId.Length > 0).Select(a => a.Name),
            StringComparer.CurrentCultureIgnoreCase);

        var duplicates = apps
            .Where(pair => pair.Value.AppId.Length == 0
                           && pair.Value.Path.StartsWith(win, StringComparison.OrdinalIgnoreCase)
                           && storeNames.Contains(pair.Value.Name))
            .Select(pair => pair.Key)
            .ToList();
        foreach (string key in duplicates) apps.Remove(key);

        return apps.Values
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool IsUninstaller(string name)
    {
        string lower = name.ToLowerInvariant();
        return lower.Contains("uninstall") || lower.Contains("désinstall") || lower.Contains("desinstall");
    }

    private static void AddStartMenuShortcuts(Dictionary<string, CatalogApp> apps)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        object? shell = null;
        try
        {
            if (shellType != null) shell = Activator.CreateInstance(shellType);
        }
        catch
        {
            shell = null;
        }
        if (shell == null) return;

        string[] roots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
        };

        foreach (string root in roots)
        {
            string programs = System.IO.Path.Combine(root, "Programs");
            if (!Directory.Exists(programs)) continue;

            List<string> shortcuts;
            try
            {
                shortcuts = Directory.EnumerateFiles(programs, "*.lnk",
                    new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                    .ToList();
            }
            catch
            {
                continue;
            }

            foreach (string lnk in shortcuts)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(lnk);
                if (IsUninstaller(name)) continue;

                string target = ResolveShortcut(shell, lnk);
                if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (target.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase)) continue;

                AddIfExists(apps, name, target);
            }
        }
    }

    // Liste "Toutes les applications" de Windows : programmes classiques et applications du Store
    private static void AddShellApps(Dictionary<string, CatalogApp> apps)
    {
        try
        {
            Type? type = Type.GetTypeFromProgID("Shell.Application");
            if (type == null) return;

            object? shellObject = Activator.CreateInstance(type);
            if (shellObject == null) return;

            dynamic shell = shellObject;
            dynamic folder = shell.NameSpace("shell:AppsFolder");
            if (folder == null) return;

            dynamic items = folder.Items();
            int count = items.Count;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic item = items.Item(i);
                    string name = item.Name;
                    string id = item.Path;

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
                    if (IsUninstaller(name)) continue;

                    if (id.Contains('!'))
                    {
                        // Application du Microsoft Store : "Famille!App"
                        string key = "aumid:" + id.ToLowerInvariant();
                        if (!apps.ContainsKey(key))
                        {
                            apps[key] = new CatalogApp { Name = name, AppId = id };
                        }
                    }
                    else
                    {
                        // Programme classique : on retrouve son .exe
                        string path = ShellApps.ResolveKnownFolderPath(id);
                        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            AddIfExists(apps, name, path);
                        }
                    }
                }
                catch
                {
                    // élément illisible : on passe au suivant
                }
            }
        }
        catch
        {
            // liste indisponible : on garde ce qu'on a déjà
        }
    }

    private static void AddIfExists(Dictionary<string, CatalogApp> apps, string name, string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        if (apps.ContainsKey(path)) return;

        apps[path] = new CatalogApp { Name = name, Path = path };
    }

    private static string ResolveShortcut(object shell, string lnkPath)
    {
        try
        {
            dynamic wsh = shell;
            dynamic shortcut = wsh.CreateShortcut(lnkPath);
            string? target = shortcut.TargetPath;
            return target ?? "";
        }
        catch
        {
            return "";
        }
    }
}

internal sealed class AppPickerWindow : Window
{
    private readonly TextBox _search = new();
    private readonly ListBox _list = new();
    private readonly List<CatalogApp> _all = new();

    public List<AppEntry> Selected { get; } = new();

    public AppPickerWindow(Window owner)
    {
        Owner = owner;
        Title = "Choisir des applications";
        Width = 580;
        Height = 660;
        MinWidth = 440;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x1F));
        Foreground = Brushes.White;
        FontSize = 13;

        // Même style que la fenêtre des paramètres
        Resources.MergedDictionaries.Add(owner.Resources);

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Applications installées (tape pour rechercher)",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        _search.Margin = new Thickness(0, 0, 0, 10);
        _search.ToolTip = "Rechercher une application";
        _search.TextChanged += (_, _) => ApplyFilter();
        Grid.SetRow(_search, 1);
        grid.Children.Add(_search);

        _list.SelectionMode = SelectionMode.Extended;
        _list.MouseDoubleClick += (_, _) => Confirm();
        _list.Items.Add(new TextBlock { Text = "Chargement des applications…" });
        Grid.SetRow(_list, 2);
        grid.Children.Add(_list);

        var browse = new Button { Content = "Parcourir un fichier…" };
        browse.Click += (_, _) => BrowseFile();

        var cancel = new Button { Content = "Annuler" };
        cancel.Click += (_, _) => DialogResult = false;

        var add = new Button
        {
            Content = "Ajouter la sélection",
            Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            Margin = new Thickness(0)
        };
        add.Click += (_, _) => Confirm();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        buttons.Children.Add(browse);
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        Grid.SetRow(buttons, 3);
        grid.Children.Add(buttons);

        Content = grid;

        // La liste se charge après l'affichage de la fenêtre
        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(LoadCatalog), DispatcherPriority.Background);
    }

    private void LoadCatalog()
    {
        _all.Clear();
        _all.AddRange(AppCatalog.Scan());
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = _search.Text.Trim();

        _list.Items.Clear();
        foreach (CatalogApp app in _all)
        {
            if (query.Length > 0
                && app.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0)
            {
                continue;
            }
            _list.Items.Add(BuildRow(app));
        }
    }

    private static StackPanel BuildRow(CatalogApp app)
    {
        var image = new Image { Width = 24, Height = 24, Margin = new Thickness(0, 0, 12, 0) };
        image.Loaded += (_, _) =>
        {
            if (image.Source == null) image.Source = AppCatalog.GetIcon(app);
        };

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = app.Name });
        texts.Children.Add(new TextBlock
        {
            Text = app.AppId.Length > 0 ? "Application Windows (Microsoft Store)" : app.Path,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xBC)),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Tag = app };
        row.Children.Add(image);
        row.Children.Add(texts);
        return row;
    }

    private void Confirm()
    {
        foreach (object item in _list.SelectedItems)
        {
            if (item is StackPanel { Tag: CatalogApp app })
            {
                Selected.Add(new AppEntry { Name = app.Name, Path = app.Path, AppId = app.AppId });
            }
        }

        if (Selected.Count == 0) return;
        DialogResult = true;
    }

    private void BrowseFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choisir une application",
            Filter = "Applications (*.exe)|*.exe|Tous les fichiers (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        Selected.Add(new AppEntry
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
            Path = dialog.FileName
        });
        DialogResult = true;
    }
}
