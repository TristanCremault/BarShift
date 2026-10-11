using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BarShift;

// ---------- Configuration des barres ----------

public class AppEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string IconPath { get; set; } = "";
    public string AppId { get; set; } = "";
}

public class Profile
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#0B0B1A";
    public string? Hotkey { get; set; }
    public List<AppEntry> Apps { get; set; } = new();
}

public class WidgetOptions
{
    public bool ShowStart { get; set; } = true;
    public bool ShowSearch { get; set; } = true;
    public bool ShowTaskView { get; set; } = true;
    public bool ShowWifi { get; set; } = true;
    public bool ShowVolume { get; set; } = true;
    public bool ShowBattery { get; set; } = true;
    public bool ShowClock { get; set; } = true;
    public bool ShowDesktop { get; set; } = true;
}

public class BarConfig
{
    public string? NextProfileHotkey { get; set; } = "Ctrl+Alt+N";
    public string? PreviousProfileHotkey { get; set; } = "Ctrl+Alt+P";
    public bool HideWindowsTaskbar { get; set; } = false;
    public bool HideInFullscreen { get; set; } = false;
    public WidgetOptions Widgets { get; set; } = new();
    public List<Profile> Profiles { get; set; } = new();
}

public static class ConfigLoader
{
    public static string ConfigPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BarShift", "profiles.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static BarConfig Load()
    {
        BarConfig? config = null;

        if (!File.Exists(ConfigPath))
        {
            config = CreateDefault();
            ApplyDefaults(config);
            Save(config);
            return config;
        }

        try
        {
            config = JsonSerializer.Deserialize<BarConfig>(File.ReadAllText(ConfigPath));
        }
        catch
        {
            config = null;
        }

        if (config == null || config.Profiles.Count == 0) config = CreateDefault();
        ApplyDefaults(config);
        return config;
    }

    public static void Save(BarConfig config)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, Options));
    }

    private static void ApplyDefaults(BarConfig config)
    {
        config.Widgets ??= new WidgetOptions();

        for (int i = 0; i < config.Profiles.Count; i++)
        {
            if (config.Profiles[i].Hotkey == null)
            {
                config.Profiles[i].Hotkey = i < 9 ? $"Ctrl+Alt+{i + 1}" : "";
            }
        }
    }

    private static BarConfig CreateDefault()
    {
        return new BarConfig
        {
            Profiles = new List<Profile>
            {
                new Profile
                {
                    Name = "Pro",
                    Color = "#0B1A2E",
                    Apps = new List<AppEntry>
                    {
                        new AppEntry { Name = "Bloc-notes", Path = @"%SystemRoot%\System32\notepad.exe" },
                        new AppEntry { Name = "Calculatrice", Path = @"%SystemRoot%\System32\calc.exe" },
                        new AppEntry { Name = "Explorateur", Path = @"%SystemRoot%\explorer.exe" }
                    }
                },
                new Profile
                {
                    Name = "Jeux",
                    Color = "#1E0B2E",
                    Apps = new List<AppEntry>
                    {
                        new AppEntry { Name = "Steam", Path = @"C:\Program Files (x86)\Steam\steam.exe" },
                        new AppEntry { Name = "Explorateur", Path = @"%SystemRoot%\explorer.exe" }
                    }
                },
                new Profile
                {
                    Name = "Code",
                    Color = "#0B2E1E",
                    Apps = new List<AppEntry>
                    {
                        new AppEntry { Name = "VS Code", Path = @"%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe" },
                        new AppEntry { Name = "PowerShell", Path = @"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" },
                        new AppEntry { Name = "Explorateur", Path = @"%SystemRoot%\explorer.exe" }
                    }
                }
            }
        };
    }
}

// ---------- Icônes ----------

internal static class IconHelper
{
    public static ImageSource? Load(string path, string iconPath, string appId = "")
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                string custom = Environment.ExpandEnvironmentVariables(iconPath);
                if (File.Exists(custom))
                {
                    string ext = System.IO.Path.GetExtension(custom).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".ico")
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(custom);
                        bitmap.DecodePixelWidth = 64;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        return bitmap;
                    }
                    return FromExe(custom);
                }
            }

            if (!string.IsNullOrWhiteSpace(appId))
            {
                ImageSource? shellIcon = ShellApps.GetIcon(appId);
                if (shellIcon != null) return shellIcon;
            }

            return FromExe(Environment.ExpandEnvironmentVariables(path));
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? FromExe(string exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;

        using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        if (icon == null) return null;

        BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
}

// ---------- Applications Windows (Microsoft Store) ----------

internal static class ShellApps
{
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_PIDL = 0x8;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext,
        out IntPtr pidl, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(IntPtr pidl, uint attributes,
        ref SHFILEINFO info, uint size, uint flags);

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags,
        IntPtr token, out IntPtr path);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pointer);

    private static readonly Dictionary<string, ImageSource?> IconCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIcon(string appId)
    {
        if (IconCache.TryGetValue(appId, out ImageSource? cached)) return cached;

        ImageSource? icon = null;
        IntPtr pidl = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName("shell:AppsFolder\\" + appId, IntPtr.Zero,
                    out pidl, 0, out _) == 0 && pidl != IntPtr.Zero)
            {
                var info = new SHFILEINFO();
                IntPtr result = SHGetFileInfo(pidl, 0, ref info,
                    (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_PIDL | SHGFI_ICON);

                if (result != IntPtr.Zero && info.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                            info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        source.Freeze();
                        icon = source;
                    }
                    finally
                    {
                        DestroyIcon(info.hIcon);
                    }
                }
            }
        }
        catch
        {
            icon = null;
        }
        finally
        {
            if (pidl != IntPtr.Zero) CoTaskMemFree(pidl);
        }

        IconCache[appId] = icon;
        return icon;
    }

    public static void Launch(string appId)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"shell:AppsFolder\\" + appId + "\"")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // lancement impossible : on ignore
        }
    }

    public static string ResolveKnownFolderPath(string appId)
    {
        if (!appId.StartsWith("{")) return appId;

        int end = appId.IndexOf('}');
        if (end < 0) return "";
        if (!Guid.TryParse(appId.Substring(0, end + 1), out Guid folderId)) return "";

        if (SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out IntPtr pointer) != 0) return "";
        try
        {
            string root = Marshal.PtrToStringUni(pointer) ?? "";
            string relative = appId.Substring(end + 1).TrimStart('\\');
            return System.IO.Path.Combine(root, relative);
        }
        finally
        {
            CoTaskMemFree(pointer);
        }
    }
}

// ---------- Profils des navigateurs ----------

internal static class BrowserProfiles
{
    internal sealed record ProfileInfo(string Directory, string Name);

    private static string? GetUserDataFolder(string exePath)
    {
        string exe = System.IO.Path.GetFileName(exePath).ToLowerInvariant();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return exe switch
        {
            "chrome.exe" => System.IO.Path.Combine(local, "Google", "Chrome", "User Data"),
            "msedge.exe" => System.IO.Path.Combine(local, "Microsoft", "Edge", "User Data"),
            "brave.exe" => System.IO.Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
            _ => null
        };
    }

    public static List<ProfileInfo> GetProfiles(string exePath)
    {
        var profiles = new List<ProfileInfo>();
        if (string.IsNullOrEmpty(exePath)) return profiles;

        string? folder = GetUserDataFolder(exePath);
        if (folder == null) return profiles;

        string stateFile = System.IO.Path.Combine(folder, "Local State");
        if (!File.Exists(stateFile)) return profiles;

        try
        {
            using var stream = new FileStream(stateFile, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);

            if (document.RootElement.TryGetProperty("profile", out JsonElement profile)
                && profile.TryGetProperty("info_cache", out JsonElement cache)
                && cache.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty entry in cache.EnumerateObject())
                {
                    string name = entry.Name;
                    if (entry.Value.TryGetProperty("name", out JsonElement nameElement))
                    {
                        string? value = nameElement.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) name = value;
                    }
                    profiles.Add(new ProfileInfo(entry.Name, name));
                }
            }
        }
        catch
        {
            return new List<ProfileInfo>();
        }

        return profiles
            .OrderBy(p => p.Directory == "Default" ? 0 : 1)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static void Launch(string exePath, string profileDirectory)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exePath, $"--profile-directory=\"{profileDirectory}\"")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // lancement impossible : on ignore
        }
    }
}

// ---------- Barre des tâches Windows ----------

internal static class TaskbarControl
{
    private const int ABM_GETSTATE = 0x4;
    private const int ABM_SETSTATE = 0xA;
    private const int ABS_AUTOHIDE = 0x1;
    private const int ABS_ALWAYSONTOP = 0x2;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct TaskbarRect { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TaskbarData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uCallbackMessage;
        public int uEdge;
        public TaskbarRect rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll", EntryPoint = "SHAppBarMessage")]
    private static extern uint AppBarMessage(int message, ref TaskbarData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private static bool _hidden;
    private static bool _wasAutoHide;

    public static bool IsHidden => _hidden;

    private static IntPtr FindTaskbar() => FindWindow("Shell_TrayWnd", null);

    // Barres des tâches Windows des autres écrans
    private static List<IntPtr> FindSecondaryTaskbars()
    {
        var list = new List<IntPtr>();
        Native.EnumWindows((h, _) =>
        {
            if (Native.GetClass(h) == "Shell_SecondaryTrayWnd") list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static void HideSecondaryTaskbars()
    {
        foreach (IntPtr h in FindSecondaryTaskbars())
        {
            if (IsWindowVisible(h)) ShowWindow(h, SW_HIDE);
        }
    }

    private static void ShowSecondaryTaskbars()
    {
        foreach (IntPtr h in FindSecondaryTaskbars()) ShowWindow(h, SW_SHOW);
    }

    private static int GetState(IntPtr taskbar)
    {
        var data = new TaskbarData { cbSize = Marshal.SizeOf<TaskbarData>(), hWnd = taskbar };
        return (int)AppBarMessage(ABM_GETSTATE, ref data);
    }

    private static void SetState(IntPtr taskbar, int state)
    {
        var data = new TaskbarData
        {
            cbSize = Marshal.SizeOf<TaskbarData>(),
            hWnd = taskbar,
            lParam = (IntPtr)state
        };
        AppBarMessage(ABM_SETSTATE, ref data);
    }

    public static void Hide()
    {
        IntPtr taskbar = FindTaskbar();
        if (taskbar == IntPtr.Zero) return;

        if (!_hidden)
        {
            _wasAutoHide = (GetState(taskbar) & ABS_AUTOHIDE) != 0;
            _hidden = true;
        }

        SetState(taskbar, ABS_AUTOHIDE);
        ShowWindow(taskbar, SW_HIDE);
        HideSecondaryTaskbars();
    }

    public static void KeepHidden()
    {
        if (!_hidden) return;

        IntPtr taskbar = FindTaskbar();
        if (taskbar != IntPtr.Zero && IsWindowVisible(taskbar)) ShowWindow(taskbar, SW_HIDE);
        HideSecondaryTaskbars();
    }

    public static void Show()
    {
        IntPtr taskbar = FindTaskbar();
        if (taskbar != IntPtr.Zero) ShowWindow(taskbar, SW_SHOW);
    }

    public static void Restore()
    {
        IntPtr taskbar = FindTaskbar();
        if (taskbar == IntPtr.Zero) return;

        SetState(taskbar, _wasAutoHide ? ABS_AUTOHIDE : ABS_ALWAYSONTOP);
        ShowWindow(taskbar, SW_SHOW);
        ShowSecondaryTaskbars();

        _hidden = false;
        _wasAutoHide = false;
    }

    public static void RestoreIfHidden()
    {
        if (_hidden) Restore();
    }
}

// ---------- Raccourcis clavier globaux ----------

internal static class HotkeyHelper
{
    public const uint MOD_ALT = 0x1;
    public const uint MOD_CONTROL = 0x2;
    public const uint MOD_SHIFT = 0x4;
    public const uint MOD_WIN = 0x8;
    public const uint MOD_NOREPEAT = 0x4000;

    public static bool TryParse(string? text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        Key key = Key.None;
        foreach (string part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= MOD_ALT;
                    break;
                case "shift":
                    modifiers |= MOD_SHIFT;
                    break;
                case "win":
                    modifiers |= MOD_WIN;
                    break;
                default:
                    string name = part.Length == 1 && char.IsDigit(part[0]) ? "D" + part : part;
                    if (!Enum.TryParse<Key>(name, true, out key)) return false;
                    break;
            }
        }

        if (key == Key.None || modifiers == 0) return false;

        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        string name = key.ToString();
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) name = name[1].ToString();
        parts.Add(name);

        return string.Join("+", parts);
    }
}

// ---------- Fonctions Windows (fenêtres, écrans, miniatures) ----------

internal static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_APPWINDOW = 0x00040000;
    public const uint GW_OWNER = 4;
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_GETICON = 0x007F;
    public const int GCL_HICON = -14;
    public const int DWMWA_CLOAKED = 14;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int GWL_STYLE = -16;
    public const int WS_CAPTION = 0x00C00000;
    public const uint MONITORINFOF_PRIMARY = 1;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeRect lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeSize { public int cx, cy; }

    public readonly record struct MonitorEntry(NativeRect Rect, bool IsPrimary);

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, IntPtr value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, IntPtr value);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int cmd);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    public static extern IntPtr GetClassLongPtr(IntPtr hWnd, int index);

    [DllImport("kernel32.dll")]
    public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr process, int flags,
        StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder? id);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object propertyStore);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(IntPtr variant);

    public const int DWM_TNP_RECTDESTINATION = 0x1;
    public const int DWM_TNP_VISIBLE = 0x8;
    public const int DWM_TNP_SOURCECLIENTAREAONLY = 0x10;
    public const int DWM_TNP_OPACITY = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_THUMBNAIL_PROPERTIES
    {
        public int dwFlags;
        public NativeRect rcDestination;
        public NativeRect rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll")]
    public static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnailId);

    [DllImport("dwmapi.dll")]
    public static extern int DwmUnregisterThumbnail(IntPtr thumbnailId);

    [DllImport("dwmapi.dll")]
    public static extern int DwmUpdateThumbnailProperties(IntPtr thumbnailId, ref DWM_THUMBNAIL_PROPERTIES props);

    [DllImport("dwmapi.dll")]
    public static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnailId, out NativeSize size);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    public static string GetText(IntPtr hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        var sb = new StringBuilder(length + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetExePath(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        IntPtr process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero) return "";
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(process, 0, sb, ref size) ? sb.ToString() : "";
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public static bool IsFullscreenWindow(IntPtr hwnd, IntPtr self)
    {
        if (hwnd == IntPtr.Zero || hwnd == self) return false;
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;

        string windowClass = GetClass(hwnd);
        if (windowClass is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
            or "XamlExplorerHostIslandWindow" or "MultitaskingViewFrame" or "Windows.UI.Core.CoreWindow")
        {
            return false;
        }

        if (!GetWindowRect(hwnd, out NativeRect rect)) return false;

        IntPtr monitor = MonitorFromWindow(hwnd, 2);
        if (monitor != MonitorFromWindow(self, 2)) return false;

        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;

        return rect.left <= info.rcMonitor.left && rect.top <= info.rcMonitor.top
            && rect.right >= info.rcMonitor.right && rect.bottom >= info.rcMonitor.bottom;
    }

    public static string WorkAreaText(IntPtr hwnd)
    {
        IntPtr monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return "?";
        return $"{info.rcWork.left},{info.rcWork.top},{info.rcWork.right},{info.rcWork.bottom}";
    }

    public static string MonitorRectText(IntPtr hwnd)
    {
        IntPtr monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return "?";
        return $"{info.rcMonitor.left},{info.rcMonitor.top},{info.rcMonitor.right},{info.rcMonitor.bottom}";
    }

    // Vrai si la fenêtre la plus haute de l'écran de cette barre est en plein écran,
    // même quand elle n'a pas le focus (ex. vidéo en plein écran sur l'écran 2 pendant qu'on clique sur l'écran 1)
    public static bool HasFullscreenWindowOnMonitorOf(IntPtr barHandle)
    {
        IntPtr monitor = MonitorFromWindow(barHandle, 2);
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;

        uint ownPid = (uint)Environment.ProcessId;
        bool fullscreen = false;

        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h)) return true;

            GetWindowThreadProcessId(h, out uint pid);
            if (pid == ownPid) return true;

            string windowClass = GetClass(h);
            if (windowClass is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
                or "XamlExplorerHostIslandWindow" or "MultitaskingViewFrame" or "Windows.UI.Core.CoreWindow")
            {
                return true;
            }

            // Ignore les surcouches (overlays, widgets) qui ne sont pas de vraies fenêtres
            int ex = GetWindowLong(h, GWL_EXSTYLE);
            if ((ex & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT)) != 0) return true;

            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;

            if (!GetWindowRect(h, out NativeRect r)) return true;
            if (r.right <= info.rcMonitor.left || r.left >= info.rcMonitor.right
                || r.bottom <= info.rcMonitor.top || r.top >= info.rcMonitor.bottom) return true;

            // La première vraie fenêtre sur cet écran décide.
            // Sans barre de titre = plein écran (une fenêtre simplement agrandie en a une).
            bool covers = r.left <= info.rcMonitor.left && r.top <= info.rcMonitor.top
                && r.right >= info.rcMonitor.right && r.bottom >= info.rcMonitor.bottom;
            bool hasCaption = (GetWindowLong(h, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;
            fullscreen = covers && !hasCaption;
            return false;
        }, IntPtr.Zero);

        return fullscreen;
    }

    public static string GetPackageAppId(IntPtr hwnd, string exePath)
    {
        string id = exePath.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)
            ? GetWindowAppId(hwnd)
            : GetProcessAppId(hwnd);

        return id.Contains('!') ? id : "";
    }

    private static string GetProcessAppId(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        IntPtr process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero) return "";

        try
        {
            uint length = 0;
            int result = GetApplicationUserModelId(process, ref length, null);
            if (result != 122 || length == 0) return "";

            var sb = new StringBuilder((int)length);
            result = GetApplicationUserModelId(process, ref length, sb);
            return result == 0 ? sb.ToString() : "";
        }
        catch
        {
            return "";
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string GetWindowAppId(IntPtr hwnd)
    {
        IntPtr variant = IntPtr.Zero;
        try
        {
            Guid iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out object obj) != 0) return "";

            var store = (IPropertyStore)obj;
            var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };

            variant = Marshal.AllocCoTaskMem(32);
            for (int i = 0; i < 32; i += 8) Marshal.WriteInt64(variant, i, 0);

            if (store.GetValue(ref key, variant) != 0) return "";

            ushort type = (ushort)Marshal.ReadInt16(variant, 0);
            if (type != 31) return "";

            IntPtr text = Marshal.ReadIntPtr(variant, 8);
            return text == IntPtr.Zero ? "" : Marshal.PtrToStringUni(text) ?? "";
        }
        catch
        {
            return "";
        }
        finally
        {
            if (variant != IntPtr.Zero)
            {
                PropVariantClear(variant);
                Marshal.FreeCoTaskMem(variant);
            }
        }
    }

    public static bool IsTaskbarWindow(IntPtr hwnd, IntPtr self)
    {
        if (hwnd == self || !IsWindowVisible(hwnd)) return false;

        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        if ((ex & WS_EX_TOOLWINDOW) != 0) return false;
        if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero && (ex & WS_EX_APPWINDOW) == 0) return false;
        if (GetWindowTextLength(hwnd) == 0) return false;

        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;

        if (GetClass(hwnd) == "Progman") return false;
        return true;
    }

    public static List<IntPtr> GetTaskbarWindows(IntPtr self)
    {
        var list = new List<IntPtr>();
        EnumWindows((h, _) =>
        {
            if (IsTaskbarWindow(h, self)) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static IntPtr GetWindowIcon(IntPtr hwnd)
    {
        foreach (int type in new[] { 1, 0, 2 })
        {
            SendMessageTimeout(hwnd, WM_GETICON, (IntPtr)type, IntPtr.Zero, 2, 50, out IntPtr result);
            if (result != IntPtr.Zero) return result;
        }
        return GetClassLongPtr(hwnd, GCL_HICON);
    }

    public static List<MonitorEntry> GetAllMonitors()
    {
        var monitors = new List<MonitorEntry>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref NativeRect rect, IntPtr _) =>
        {
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            bool primary = GetMonitorInfo(hMonitor, ref info) && (info.dwFlags & MONITORINFOF_PRIMARY) != 0;
            monitors.Add(new MonitorEntry(rect, primary));
            return true;
        }, IntPtr.Zero);
        return monitors;
    }
}

// ---------- Journal de diagnostic (%APPDATA%\BarShift\debug.log) ----------

internal static class DebugLog
{
    public static void Write(string text)
    {
        try
        {
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BarShift");
            Directory.CreateDirectory(dir);
            File.AppendAllText(System.IO.Path.Combine(dir, "debug.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {text}{Environment.NewLine}");
        }
        catch
        {
            // journal indisponible : sans importance
        }
    }
}

// ---------- Pastilles de notification (applications du Microsoft Store) ----------

internal static class NotificationBadges
{
    private const int SqliteRow = 100;
    private const int OpenReadWrite = 2;

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_bytes(IntPtr statement, int column);

    private static string _stamp = "";
    private static Dictionary<string, string>? _cache;
    private static bool _summaryLogged;

    // Renvoie, pour chaque application (identifiant AUMID), la valeur de sa pastille ("3", "newMessage"...)
    public static Dictionary<string, string>? Read(out string error)
    {
        error = "";

        string folder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Notifications");
        string source = System.IO.Path.Combine(folder, "wpndatabase.db");
        if (!File.Exists(source))
        {
            error = "wpndatabase.db introuvable";
            return null;
        }

        string stamp = StampOf(source) + "|" + StampOf(source + "-wal");
        if (stamp == _stamp && _cache != null) return _cache;

        string temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BarShift-wpn-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            string copy = System.IO.Path.Combine(temp, "wpndatabase.db");

            // On lit une copie : la base d'origine est utilisée en permanence par Windows
            foreach (string suffix in new[] { "", "-wal", "-shm" })
            {
                string from = source + suffix;
                if (!File.Exists(from)) continue;

                using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var output = new FileStream(copy + suffix, FileMode.Create, FileAccess.Write);
                input.CopyTo(output);
            }

            Dictionary<string, string>? badges = QueryBadges(copy, out error);
            if (badges != null)
            {
                _stamp = stamp;
                _cache = badges;
            }
            return badges;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { /* fichier encore utilisé : sans importance */ }
        }
    }

    private static string StampOf(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks + ":" + new FileInfo(path).Length : "-";

    private static Dictionary<string, string>? QueryBadges(string path, out string error)
    {
        error = "";

        if (sqlite3_open_v2(path, out IntPtr db, OpenReadWrite, IntPtr.Zero) != 0)
        {
            error = "ouverture de la base impossible";
            if (db != IntPtr.Zero) sqlite3_close(db);
            return null;
        }

        try
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string[] queries =
            {
                "SELECT nh.PrimaryId, n.Payload FROM Notification n JOIN NotificationHandler nh ON n.HandlerId = nh.RecordId WHERE n.Type = 'badge' ORDER BY n.ArrivalTime",
                "SELECT nh.PrimaryId, n.Payload FROM Notification n JOIN NotificationHandler nh ON n.HandlerId = nh.RecordId WHERE n.Type = 'badge'"
            };

            bool done = false;
            foreach (string sql in queries)
            {
                result.Clear();
                done = RunQuery(db, sql, statement =>
                {
                    string id = Text(statement, 0);
                    string value = ParseBadgeValue(Blob(statement, 1));
                    if (id.Length == 0) return;

                    if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase)) result.Remove(id);
                    else result[id] = value;
                });
                if (done) break;
            }

            if (!done)
            {
                error = "requete refusee par la base";
                return null;
            }

            if (!_summaryLogged)
            {
                _summaryLogged = true;
                var lines = new List<string>();
                RunQuery(db,
                    "SELECT nh.PrimaryId, n.Type, COUNT(*) FROM Notification n JOIN NotificationHandler nh ON n.HandlerId = nh.RecordId GROUP BY nh.PrimaryId, n.Type",
                    statement => lines.Add($"{Text(statement, 0)}/{Text(statement, 1)}={Text(statement, 2)}"));
                DebugLog.Write("Notifications par application : " + string.Join("; ", lines));
            }

            return result;
        }
        finally
        {
            sqlite3_close(db);
        }
    }

    private static bool RunQuery(IntPtr db, string sql, Action<IntPtr> onRow)
    {
        if (sqlite3_prepare_v2(db, sql, -1, out IntPtr statement, IntPtr.Zero) != 0)
        {
            if (statement != IntPtr.Zero) sqlite3_finalize(statement);
            return false;
        }

        try
        {
            while (sqlite3_step(statement) == SqliteRow) onRow(statement);
            return true;
        }
        finally
        {
            sqlite3_finalize(statement);
        }
    }

    private static string Text(IntPtr statement, int column) =>
        Marshal.PtrToStringUTF8(sqlite3_column_text(statement, column)) ?? "";

    private static string Blob(IntPtr statement, int column)
    {
        IntPtr pointer = sqlite3_column_blob(statement, column);
        int length = sqlite3_column_bytes(statement, column);
        if (pointer == IntPtr.Zero || length <= 0) return "";

        var bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);

        bool utf16 = length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || bytes[1] == 0);
        return utf16 ? System.Text.Encoding.Unicode.GetString(bytes) : System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static string ParseBadgeValue(string payload)
    {
        var match = System.Text.RegularExpressions.Regex.Match(payload, "value\\s*=\\s*[\"']([^\"']*)[\"']");
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }
}

// ---------- Réservation d'espace à l'écran ----------

internal static class AppBar
{
    private const int ABM_NEW = 0x0;
    private const int ABM_REMOVE = 0x1;
    private const int ABM_QUERYPOS = 0x2;
    private const int ABM_SETPOS = 0x3;
    private const int ABE_BOTTOM = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uCallbackMessage;
        public int uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    private static extern uint SHAppBarMessage(int msg, ref APPBARDATA data);

    [DllImport("user32.dll")]
    private static extern int RegisterWindowMessage(string msg);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);

    public static void Register(IntPtr hwnd, Native.NativeRect monitorRect, int barHeightPx, bool exact = false)
    {
        var data = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
            uCallbackMessage = RegisterWindowMessage("BarShiftAppBarMsg")
        };
        SHAppBarMessage(ABM_NEW, ref data);

        Reposition(hwnd, monitorRect, barHeightPx, exact);
    }

    public static void Reposition(IntPtr hwnd, Native.NativeRect monitorRect, int barHeightPx, bool exact = false)
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };

        data.uEdge = ABE_BOTTOM;
        data.rc = new RECT
        {
            left = monitorRect.left,
            top = monitorRect.bottom - barHeightPx,
            right = monitorRect.right,
            bottom = monitorRect.bottom
        };
        int queriedBottom = -1;
        if (!exact)
        {
            SHAppBarMessage(ABM_QUERYPOS, ref data);
            queriedBottom = data.rc.bottom;
            data.rc.top = data.rc.bottom - barHeightPx;
        }
        SHAppBarMessage(ABM_SETPOS, ref data);

        DebugLog.Write($"AppBar hwnd={hwnd} exact={exact} ecran=({monitorRect.left},{monitorRect.top},{monitorRect.right},{monitorRect.bottom}) "
                       + $"bas apres QUERYPOS={queriedBottom} SETPOS=({data.rc.left},{data.rc.top},{data.rc.right},{data.rc.bottom})");

        if (exact)
        {
            // On demande à Windows de réserver la place tout en bas de l'écran (sans passer par QUERYPOS,
            // qui nous repousse vers le haut) et on colle la barre exactement à cet endroit.
            MoveWindow(hwnd, monitorRect.left, monitorRect.bottom - barHeightPx,
                monitorRect.right - monitorRect.left, barHeightPx, true);
            return;
        }

        MoveWindow(hwnd, data.rc.left, data.rc.top, data.rc.right - data.rc.left, data.rc.bottom - data.rc.top, true);
    }

    public static void MoveTo(IntPtr hwnd, int x, int y, int w, int h) => MoveWindow(hwnd, x, y, w, h, true);

    public static void Unregister(IntPtr hwnd)
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
        SHAppBarMessage(ABM_REMOVE, ref data);
    }
}

// ---------- Barre secondaire (écrans autres que le principal) ----------

internal sealed class SecondaryBarWindow : Window
{
    private const int BarHeightDip = 48;
    private readonly Native.NativeRect _monitorRect;
    private readonly bool _exactPosition;

    public StackPanel AppsPanel { get; } = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center
    };

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public bool HiddenForFullscreen { get; set; }

    public SecondaryBarWindow(Native.NativeRect monitorRect, Brush background, bool exactPosition)
    {
        _monitorRect = monitorRect;
        _exactPosition = exactPosition;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = background;
        Height = BarHeightDip;
        Width = 800;
        Left = 0;
        Top = 0;

        var grid = new Grid();
        grid.Children.Add(AppsPanel);
        Content = grid;

        SourceInitialized += (_, _) => RegisterAndPosition();
        // Closing (et non Closed) : la fenêtre existe encore, sinon Windows garde la place réservée
        Closing += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) AppBar.Unregister(hwnd);
        };
    }

    private void RegisterAndPosition()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        int barHeightPx = (int)(BarHeightDip * dpi.DpiScaleY);
        AppBar.Register(hwnd, _monitorRect, barHeightPx, _exactPosition);
    }

    // Re-demande la place à Windows une fois la fenêtre affichée (comme pour la barre principale)
    public void RefreshAppBar()
    {
        IntPtr hwnd = Handle;
        if (hwnd == IntPtr.Zero || HiddenForFullscreen) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        int barHeightPx = (int)(BarHeightDip * dpi.DpiScaleY);
        AppBar.Reposition(hwnd, _monitorRect, barHeightPx, _exactPosition);
    }

    public void UpdateBackground(Brush background) => Background = background;

    private static int _corrections;

    // Quand la barre Windows est masquée, la barre doit rester collée au bas de son écran
    public void EnforcePosition()
    {
        if (!_exactPosition || HiddenForFullscreen || !IsVisible) return;

        IntPtr hwnd = Handle;
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out Native.NativeRect actual)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        int height = (int)(BarHeightDip * dpi.DpiScaleY);
        int top = _monitorRect.bottom - height;
        int width = _monitorRect.right - _monitorRect.left;

        if (actual.left == _monitorRect.left && actual.top == top
            && actual.right == _monitorRect.right && actual.bottom == _monitorRect.bottom) return;

        if (_corrections++ < 20)
        {
            DebugLog.Write($"Correction barre secondaire : etait ({actual.left},{actual.top},{actual.right},{actual.bottom}) "
                           + $"-> ({_monitorRect.left},{top},{_monitorRect.right},{_monitorRect.bottom})");
        }
        AppBar.MoveTo(hwnd, _monitorRect.left, top, width, height);
    }
}

// ---------- Volume du système ----------

internal static class AudioHelper
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IAudioEndpointVolume endpoint);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    public static bool TryGetVolume(out float level, out bool muted)
    {
        level = 0;
        muted = false;
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice device) != 0) return false;

            Guid iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 23, IntPtr.Zero, out IAudioEndpointVolume volume) != 0) return false;

            volume.GetMasterVolumeLevelScalar(out level);
            volume.GetMute(out muted);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

// ---------- Auto-enregistrement (menu Démarrer, recherche, désinstallation) ----------

internal static class AppRegistration
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BarShift";

    private static string StartMenuAppLink => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "BarShift.lnk");

    public static void EnsureRegistered()
    {
        try
        {
            using RegistryKey? existing = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (existing != null && File.Exists(StartMenuAppLink)) return;

            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath)) return;

            CreateStartMenuShortcut(exePath);
            WriteUninstallEntry(exePath);
        }
        catch
        {
            // enregistrement impossible : BarShift continue de fonctionner normalement
        }
    }

    private static void CreateStartMenuShortcut(string exePath)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;

        dynamic link = shell.CreateShortcut(StartMenuAppLink);
        link.TargetPath = exePath;
        link.WorkingDirectory = System.IO.Path.GetDirectoryName(exePath);
        link.Description = "BarShift";
        link.Save();
    }

    private static void WriteUninstallEntry(string exePath)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
        key.SetValue("DisplayName", "BarShift");
        key.SetValue("DisplayIcon", exePath);
        key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
        key.SetValue("Publisher", "BarShift");
        key.SetValue("DisplayVersion", "1.0.0");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("InstallLocation", System.IO.Path.GetDirectoryName(exePath) ?? "");
    }

    public static void Unregister()
    {
        try
        {
            if (File.Exists(StartMenuAppLink)) File.Delete(StartMenuAppLink);
        }
        catch
        {
            // pas bloquant
        }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // pas bloquant
        }
    }
}

// ---------- Lancer au démarrage de Windows ----------

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BarShift";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            string? value = key?.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(value)) return false;

            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            return value.Trim('"').Equals(exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key == null) return;

            if (enabled)
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exePath)) key.SetValue(ValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // écriture impossible : le réglage ne sera pas appliqué
        }
    }
}

// ---------- Fenêtre d'aperçu au survol (miniatures en direct) ----------

internal sealed class WindowPreviewPopup : Window
{
    private const int ThumbWidth = 176;
    private const int ThumbHeight = 108;
    private const int TitleHeight = 24;
    private const int CardPadding = 6;
    private const int CardMargin = 3;
    private const int PanelMargin = 7;

    private readonly List<(IntPtr Window, Border Host)> _slots = new();
    private readonly List<IntPtr> _thumbnailIds = new();

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public WindowPreviewPopup(IReadOnlyList<IntPtr> windows, Action<IntPtr> onActivate, double maxWidth)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x24));
        // Pas de AllowsTransparency : Windows ne dessine pas les miniatures sur une fenêtre transparente.

        int cardWidth = ThumbWidth + CardPadding * 2 + CardMargin * 2;
        int cardHeight = ThumbHeight + TitleHeight + CardPadding * 2 + CardMargin * 2;

        int count = Math.Max(1, windows.Count);
        int maxColumns = Math.Max(1, (int)((maxWidth - PanelMargin * 2 - 2) / cardWidth));
        int columns = Math.Min(count, maxColumns);
        int rows = (int)Math.Ceiling(count / (double)columns);

        Width = columns * cardWidth + PanelMargin * 2 + 2;
        Height = rows * cardHeight + PanelMargin * 2 + 2;

        var grid = new UniformGrid { Columns = columns, Rows = rows, Margin = new Thickness(PanelMargin) };

        foreach (IntPtr hwnd in windows)
        {
            IntPtr window = hwnd;

            var thumbHost = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(4) };
            Grid.SetRow(thumbHost, 0);

            string title = Native.GetText(window);
            if (string.IsNullOrWhiteSpace(title)) title = "(sans titre)";

            var titleRow = new DockPanel { Margin = new Thickness(2, 5, 2, 0), LastChildFill = true };
            ImageSource? icon = LoadWindowIcon(window);
            if (icon != null)
            {
                var image = new Image { Source = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) };
                DockPanel.SetDock(image, Dock.Left);
                titleRow.Children.Add(image);
            }
            titleRow.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetRow(titleRow, 1);

            // Zone cliquable qui couvre la miniature et le titre
            var overlay = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            Grid.SetRowSpan(overlay, 2);
            overlay.MouseLeftButtonUp += (_, _) => onActivate(window);

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ThumbHeight) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleHeight) });
            content.Children.Add(thumbHost);
            content.Children.Add(titleRow);
            content.Children.Add(overlay);

            var card = new Border
            {
                Margin = new Thickness(CardMargin),
                Padding = new Thickness(CardPadding),
                CornerRadius = new CornerRadius(8),
                Background = Brushes.Transparent,
                Child = content
            };
            card.MouseEnter += (_, _) => card.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;

            grid.Children.Add(card);
            _slots.Add((window, thumbHost));
        }

        Content = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = grid
        };

        SourceInitialized += (_, _) => ApplyWindowStyle();
        Loaded += (_, _) => RegisterThumbnails();
        Closed += (_, _) => UnregisterThumbnails();
    }

    private static ImageSource? LoadWindowIcon(IntPtr hwnd)
    {
        try
        {
            IntPtr hIcon = Native.GetWindowIcon(hwnd);
            if (hIcon == IntPtr.Zero) return null;

            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    // Ne vole pas le focus, n'apparaît pas dans Alt+Tab, coins arrondis sous Windows 11
    private void ApplyWindowStyle()
    {
        IntPtr hwnd = Handle;
        if (hwnd == IntPtr.Zero) return;

        int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);

        int rounded = 2;
        Native.DwmSetWindowAttribute(hwnd, 33, ref rounded, sizeof(int));
    }

    private void RegisterThumbnails()
    {
        IntPtr destination = Handle;
        if (destination == IntPtr.Zero) return;

        UpdateLayout();
        DpiScale dpi = VisualTreeHelper.GetDpi(this);

        foreach ((IntPtr window, Border host) in _slots)
        {
            if (host.ActualWidth <= 0 || host.ActualHeight <= 0) continue;
            if (Native.DwmRegisterThumbnail(destination, window, out IntPtr thumbId) != 0) continue;
            _thumbnailIds.Add(thumbId);

            // Position réelle de la case, convertie en pixels
            Point origin = host.TransformToAncestor(this).Transform(new Point(0, 0));
            double boxX = origin.X * dpi.DpiScaleX;
            double boxY = origin.Y * dpi.DpiScaleY;
            double boxW = host.ActualWidth * dpi.DpiScaleX;
            double boxH = host.ActualHeight * dpi.DpiScaleY;

            // On garde les proportions de la fenêtre
            double width = boxW;
            double height = boxH;
            if (Native.DwmQueryThumbnailSourceSize(thumbId, out Native.NativeSize size) == 0
                && size.cx > 0 && size.cy > 0)
            {
                double scale = Math.Min(1.0, Math.Min(boxW / size.cx, boxH / size.cy));
                width = size.cx * scale;
                height = size.cy * scale;
            }

            int left = (int)Math.Round(boxX + (boxW - width) / 2);
            int top = (int)Math.Round(boxY + (boxH - height) / 2);

            var props = new Native.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_VISIBLE | Native.DWM_TNP_OPACITY,
                rcDestination = new Native.NativeRect
                {
                    left = left,
                    top = top,
                    right = left + (int)Math.Round(width),
                    bottom = top + (int)Math.Round(height)
                },
                fVisible = true,
                opacity = 255
            };
            Native.DwmUpdateThumbnailProperties(thumbId, ref props);
        }
    }

    private void UnregisterThumbnails()
    {
        foreach (IntPtr id in _thumbnailIds)
        {
            try { Native.DwmUnregisterThumbnail(id); } catch { /* déjà libéré */ }
        }
        _thumbnailIds.Clear();
    }
}

// ---------- Fenêtre principale ----------

public partial class MainWindow : Window
{
    private const int BarHeightDip = 48;
    private const int IconSize = 22;

    private const byte VK_LWIN = 0x5B;
    private const byte VK_TAB = 0x09;
    private const byte VK_RETURN = 0x0D;
    private const byte VK_A = 0x41;
    private const byte VK_B = 0x42;
    private const byte VK_D = 0x44;
    private const byte VK_N = 0x4E;
    private const byte VK_S = 0x53;
    private const uint KEYEVENTF_KEYUP = 0x2;

    private const int WM_HOTKEY = 0x0312;
    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int HotkeyProfileBase = 100;
    private const int HotkeyNext = 200;
    private const int HotkeyPrevious = 201;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    private class TaskItem
    {
        public string Key = "";
        public string Name = "";
        public string ExePath = "";
        public string IconPath = "";
        public string AppId = "";
        public bool Pinned;
        public bool IsActive;
        public string Badge = "";
        public List<IntPtr> Windows = new();
    }

    private BarConfig _config = new();
    private readonly List<string> _runningOrder = new();
    private readonly List<int> _hotkeyIds = new();
    private readonly List<SecondaryBarWindow> _secondaryBars = new();
    private List<TaskItem> _lastItems = new();
    private Dictionary<string, string> _badges = new(StringComparer.OrdinalIgnoreCase);
    private bool _badgeReadRunning;
    private string _badgeLogKey = "";
    private string _badgeError = "";
    private int _currentProfile;
    private IntPtr _lastForeground = IntPtr.Zero;
    private string _lastSignature = "";
    private bool _hasBattery;
    private bool _peeking;
    private bool _hiddenForFullscreen;
    private DispatcherTimer? _peekTimer;
    private SettingsWindow? _settings;

    private DispatcherTimer? _previewShowTimer;
    private DispatcherTimer? _previewCloseTimer;
    private WindowPreviewPopup? _previewPopup;
    private Button? _previewButton;
    private string _previewKey = "";
    private string _monitorSignature = "";

    public MainWindow()
    {
        string[] args = Environment.GetCommandLineArgs();

        if (args.Any(a => a.Equals("--restore-taskbar", StringComparison.OrdinalIgnoreCase)))
        {
            TaskbarControl.Restore();
            Environment.Exit(0);
        }

        if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            RunSelfUninstall();
            Environment.Exit(0);
        }

        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            RegisterBar();
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
            RegisterHotkeys();
            RebuildSecondaryBars();
            RepositionSoon();
        };
        Closing += (_, _) => UnregisterBar();
        Closed += (_, _) =>
        {
            HidePreview();
            foreach (var bar in _secondaryBars) bar.Close();
            _secondaryBars.Clear();
            _settings?.Close();
            TaskbarControl.RestoreIfHidden();
            UnregisterBar();
        };
        KeyDown += OnKeyDown;

        AppDomain.CurrentDomain.ProcessExit += (_, _) => TaskbarControl.RestoreIfHidden();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => TaskbarControl.RestoreIfHidden();

        _config = ConfigLoader.Load();
        if (_config.HideWindowsTaskbar) TaskbarControl.Hide();

        AppRegistration.EnsureRegistered();

        BuildProfileMenu();
        SelectProfile(0);
        StartClock();
        StartStatusTimer();
        StartTaskbarTimer();
    }

    // ---------- Désinstallation depuis BarShift lui-même ----------

    private static void RunSelfUninstall()
    {
        var answer = MessageBox.Show(
            "Désinstaller BarShift ?\n\nSes réglages (%APPDATA%\\BarShift) seront conservés ; "
            + "supprime-les toi-même si tu veux repartir de zéro.",
            "BarShift", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return;

        TaskbarControl.RestoreIfHidden();
        AppRegistration.Unregister();

        MessageBox.Show(
            "BarShift a été désinscrit de Windows.\n\nTu peux maintenant supprimer son dossier d'installation manuellement.",
            "BarShift", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------- Paramètres ----------

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settings != null)
        {
            if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow(_config, ApplyAndSave);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
    }

    private void ApplyAndSave(BarConfig newConfig)
    {
        ConfigLoader.Save(newConfig);
        ApplyConfig(newConfig);
    }

    private void ApplyConfig(BarConfig config)
    {
        _config = config;

        SelectProfile(_currentProfile);
        ApplyTaskbarSetting();
        UpdateFullscreenState();

        List<string> failed = RegisterHotkeys();
        if (failed.Count > 0)
        {
            Window owner = this;
            if (_settings != null) owner = _settings;

            MessageBox.Show(owner,
                "Ces raccourcis n'ont pas pu être enregistrés (déjà utilisés par Windows ou une autre application) :\n\n"
                + string.Join("\n", failed),
                "BarShift", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveConfigQuietly()
    {
        try { ConfigLoader.Save(_config); }
        catch { /* le changement reste actif pour cette session */ }
    }

    // ---------- Écrans multiples ----------

    private void RebuildSecondaryBars()
    {
        List<Native.MonitorEntry> monitors;
        try { monitors = Native.GetAllMonitors(); }
        catch { return; }

        // On ne reconstruit les barres que si la disposition des écrans a changé
        string signature = string.Join("|", monitors.Select(m =>
            $"{m.Rect.left},{m.Rect.top},{m.Rect.right},{m.Rect.bottom},{m.IsPrimary}"))
            + "#" + _config.HideWindowsTaskbar;
        if (signature == _monitorSignature) return;
        _monitorSignature = signature;

        HidePreview();
        foreach (var bar in _secondaryBars) bar.Close();
        _secondaryBars.Clear();

        foreach (Native.MonitorEntry monitor in monitors)
        {
            if (monitor.IsPrimary) continue;

            var bar = new SecondaryBarWindow(monitor.Rect, Background, _config.HideWindowsTaskbar);
            bar.Show();
            _secondaryBars.Add(bar);
        }

        SyncSecondaryPanels();
    }

    private void SyncSecondaryPanels()
    {
        foreach (var bar in _secondaryBars)
        {
            bar.AppsPanel.Children.Clear();
            foreach (var item in _lastItems) bar.AppsPanel.Children.Add(BuildTaskButton(item));
        }
    }

    // ---------- Plein écran ----------

    private void UpdateFullscreenState()
    {
        IntPtr self = new WindowInteropHelper(this).Handle;
        if (self == IntPtr.Zero) return;

        IntPtr foreground = Native.GetForegroundWindow();
        LogFullscreenState(foreground, self);

        bool shouldHide = _config.HideInFullscreen
            && (Native.IsFullscreenWindow(foreground, self) || Native.HasFullscreenWindowOnMonitorOf(self));
        if (shouldHide != _hiddenForFullscreen)
        {
            _hiddenForFullscreen = shouldHide;
            Native.ShowWindow(self, shouldHide ? Native.SW_HIDE : Native.SW_SHOWNOACTIVATE);
        }

        // Chaque barre secondaire se masque seulement si le plein écran est sur son écran
        foreach (var bar in _secondaryBars)
        {
            IntPtr handle = bar.Handle;
            if (handle == IntPtr.Zero) continue;

            bool hide = _config.HideInFullscreen
                && (Native.IsFullscreenWindow(foreground, handle) || Native.HasFullscreenWindowOnMonitorOf(handle));
            if (hide == bar.HiddenForFullscreen) continue;

            bar.HiddenForFullscreen = hide;
            Native.ShowWindow(handle, hide ? Native.SW_HIDE : Native.SW_SHOWNOACTIVATE);
        }
    }

    private string _fullscreenLogKey = "";

    // Écrit dans debug.log ce que BarShift voit, à chaque changement de fenêtre ou de taille
    private void LogFullscreenState(IntPtr foreground, IntPtr self)
    {
        string rectText = Native.GetWindowRect(foreground, out Native.NativeRect r)
            ? $"{r.left},{r.top},{r.right},{r.bottom}" : "?";
        string key = $"{foreground}|{rectText}|{_config.HideInFullscreen}|{_secondaryBars.Count}";
        if (key == _fullscreenLogKey) return;
        _fullscreenLogKey = key;

        var parts = new List<string>
        {
            $"Plein ecran : option={_config.HideInFullscreen} fenetre=[{Native.GetClass(foreground)} | {Native.GetText(foreground)}] rect=({rectText})"
        };

        var bars = new List<(string Name, IntPtr Handle)> { ("principale", self) };
        for (int i = 0; i < _secondaryBars.Count; i++) bars.Add(($"secondaire{i + 1}", _secondaryBars[i].Handle));

        foreach (var (name, handle) in bars)
        {
            parts.Add($"{name}: ecran=({Native.MonitorRectText(handle)}) focusPlein={Native.IsFullscreenWindow(foreground, handle)} "
                      + $"scanPlein={Native.HasFullscreenWindowOnMonitorOf(handle)}");
        }

        DebugLog.Write(string.Join(" || ", parts));
    }

    private void SetHideInFullscreen(bool hide)
    {
        _config.HideInFullscreen = hide;
        SaveConfigQuietly();
        UpdateFullscreenState();
    }

    // ---------- Barre des tâches Windows ----------

    private void SetHideWindowsTaskbar(bool hide)
    {
        if (hide)
        {
            var answer = MessageBox.Show(this,
                "La barre des tâches Windows va être cachée : BarShift devient ta seule barre.\n\n"
                + "Le bouton ^ à droite de BarShift affiche la zone de notification. "
                + "Pour revenir en arrière : décoche cette option, ou lance "
                + "« BarShift.exe --restore-taskbar ».\n\nContinuer ?",
                "BarShift", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }

        _config.HideWindowsTaskbar = hide;
        SaveConfigQuietly();
        ApplyTaskbarSetting();
    }

    private void ApplyTaskbarSetting()
    {
        if (_config.HideWindowsTaskbar)
        {
            if (!_peeking) TaskbarControl.Hide();
        }
        else
        {
            EndPeek();
            if (TaskbarControl.IsHidden) TaskbarControl.Restore();
        }

        ApplyWidgets();
        RepositionSoon();
    }

    private void TrayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_peeking) { EndPeek(); return; }

        _peeking = true;
        TaskbarControl.Show();
        SendWinShortcut(VK_B);

        var openTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        openTimer.Tick += (_, _) =>
        {
            openTimer.Stop();
            if (!_peeking) return;

            IntPtr foreground = Native.GetForegroundWindow();
            if (Native.GetClass(foreground) == "Shell_TrayWnd") SendKey(VK_RETURN);
        };
        openTimer.Start();

        _peekTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _peekTimer.Tick -= PeekTimer_Tick;
        _peekTimer.Tick += PeekTimer_Tick;
        _peekTimer.Stop();
        _peekTimer.Start();
    }

    private void PeekTimer_Tick(object? sender, EventArgs e) => EndPeek();

    private void EndPeek()
    {
        if (!_peeking) return;
        _peeking = false;
        _peekTimer?.Stop();
        if (_config.HideWindowsTaskbar) TaskbarControl.Hide();
    }

    // ---------- Menu de l'engrenage ----------

    private void BuildProfileMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) => FillProfileMenu(menu);
        SettingsButton.ContextMenu = menu;
    }

    private void FillProfileMenu(ContextMenu menu)
    {
        menu.Items.Clear();

        for (int i = 0; i < _config.Profiles.Count; i++)
        {
            int index = i;
            Profile profile = _config.Profiles[i];

            var item = new MenuItem
            {
                Header = string.IsNullOrEmpty(profile.Hotkey) ? profile.Name : $"{profile.Name}    ({profile.Hotkey})",
                IsChecked = i == _currentProfile
            };
            item.Click += (_, _) => SelectProfile(index);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        var fullscreenItem = new MenuItem
        {
            Header = "Masquer la barre en plein écran",
            IsCheckable = true,
            IsChecked = _config.HideInFullscreen
        };
        fullscreenItem.Click += (_, _) => SetHideInFullscreen(fullscreenItem.IsChecked);
        menu.Items.Add(fullscreenItem);

        var hideItem = new MenuItem
        {
            Header = "Masquer la barre des tâches Windows",
            IsCheckable = true,
            IsChecked = _config.HideWindowsTaskbar
        };
        hideItem.Click += (_, _) => SetHideWindowsTaskbar(hideItem.IsChecked);
        menu.Items.Add(hideItem);

        menu.Items.Add(new Separator());

        var settingsItem = new MenuItem { Header = "Paramètres…" };
        settingsItem.Click += (_, _) => Settings_Click(this, new RoutedEventArgs());
        menu.Items.Add(settingsItem);

        var uninstallItem = new MenuItem { Header = "Désinstaller BarShift…" };
        uninstallItem.Click += (_, _) => RunSelfUninstall();
        menu.Items.Add(uninstallItem);
    }

    // ---------- Éléments Windows affichés ----------

    private static Visibility Vis(bool show) => show ? Visibility.Visible : Visibility.Collapsed;

    private void ApplyWidgets()
    {
        WidgetOptions w = _config.Widgets;

        StartButton.Visibility = Vis(w.ShowStart);
        SearchButton.Visibility = Vis(w.ShowSearch);
        TaskViewButton.Visibility = Vis(w.ShowTaskView);

        WifiIcon.Visibility = Vis(w.ShowWifi);
        VolumeIcon.Visibility = Vis(w.ShowVolume);
        BatteryPanel.Visibility = Vis(w.ShowBattery && _hasBattery);

        VolumeIcon.Margin = new Thickness(w.ShowWifi ? 10 : 0, 0, 0, 0);
        BatteryPanel.Margin = new Thickness(w.ShowWifi || w.ShowVolume ? 10 : 0, 0, 0, 0);

        QuickSettingsButton.Visibility = Vis(w.ShowWifi || w.ShowVolume || (w.ShowBattery && _hasBattery));

        ClockButton.Visibility = Vis(w.ShowClock);
        ShowDesktopButton.Visibility = Vis(w.ShowDesktop);

        TrayButton.Visibility = Vis(_config.HideWindowsTaskbar);
    }

    // ---------- Raccourcis globaux ----------

    private List<string> RegisterHotkeys()
    {
        var failed = new List<string>();
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return failed;

        foreach (int id in _hotkeyIds) UnregisterHotKey(hwnd, id);
        _hotkeyIds.Clear();

        void Register(int id, string? text, string label)
        {
            if (!HotkeyHelper.TryParse(text, out uint modifiers, out uint vk)) return;

            if (RegisterHotKey(hwnd, id, modifiers | HotkeyHelper.MOD_NOREPEAT, vk))
                _hotkeyIds.Add(id);
            else
                failed.Add($"{label} ({text})");
        }

        for (int i = 0; i < _config.Profiles.Count && i < 100; i++)
        {
            Register(HotkeyProfileBase + i, _config.Profiles[i].Hotkey, _config.Profiles[i].Name);
        }
        Register(HotkeyNext, _config.NextProfileHotkey, "Barre suivante");
        Register(HotkeyPrevious, _config.PreviousProfileHotkey, "Barre précédente");

        return failed;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE)
        {
            RepositionSoon();
            handled = false;
            return IntPtr.Zero;
        }

        if (msg != WM_HOTKEY) return IntPtr.Zero;

        int id = wParam.ToInt32();
        int count = _config.Profiles.Count;

        if (id >= HotkeyProfileBase && id < HotkeyProfileBase + 100)
        {
            int index = id - HotkeyProfileBase;
            if (index < count) SelectProfile(index);
            handled = true;
        }
        else if (id == HotkeyNext && count > 0)
        {
            SelectProfile((_currentProfile + 1) % count);
            handled = true;
        }
        else if (id == HotkeyPrevious && count > 0)
        {
            SelectProfile((_currentProfile - 1 + count) % count);
            handled = true;
        }

        return IntPtr.Zero;
    }

    // ---------- Raccourcis Windows (boutons de la barre) ----------

    private static void SendKey(byte key)
    {
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static void SendWinShortcut(byte key)
    {
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) => SendWinShortcut(VK_S);

    private void TaskView_Click(object sender, RoutedEventArgs e) => SendWinShortcut(VK_TAB);

    private void QuickSettings_Click(object sender, RoutedEventArgs e) => SendWinShortcut(VK_A);

    private void Notifications_Click(object sender, RoutedEventArgs e) => SendWinShortcut(VK_N);

    private void ShowDesktop_Click(object sender, RoutedEventArgs e) => SendWinShortcut(VK_D);

    // ---------- Horloge ----------

    private void StartClock()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => UpdateClock();
        timer.Start();
        UpdateClock();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm") + "\n" + now.ToString("dd/MM/yyyy");
    }

    // ---------- État du système ----------

    private void StartStatusTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => UpdateStatus();
        timer.Start();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        WifiIcon.Text = GetNetworkGlyph();

        if (AudioHelper.TryGetVolume(out float level, out bool muted))
        {
            VolumeIcon.Text = (muted || level <= 0f) ? "\uE74F" : "\uE767";
        }

        _hasBattery = GetSystemPowerStatus(out SYSTEM_POWER_STATUS power)
                      && power.BatteryFlag != 128 && power.BatteryLifePercent <= 100;
        if (_hasBattery)
        {
            BatteryText.Text = power.BatteryLifePercent + "%";
            BatteryIcon.Text = power.ACLineStatus == 1 ? "\uE83E" : "\uE83F";
        }

        ApplyWidgets();
    }

    private static string GetNetworkGlyph()
    {
        bool wifiConnected = false;
        bool wiredConnected = false;

        string[] virtualHints =
        {
            "virtual", "hyper-v", "vethernet", "vmware", "virtualbox", "vbox",
            "tap-windows", "tap adapter", "wan miniport", "loopback", "bluetooth",
            "tunnel", "wireguard", "nordvpn", "openvpn", "tailscale"
        };

        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                string description = (nic.Description + " " + nic.Name).ToLowerInvariant();
                if (virtualHints.Any(hint => description.Contains(hint))) continue;

                bool hasGateway;
                try
                {
                    hasGateway = nic.GetIPProperties().GatewayAddresses
                        .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                  && !g.Address.Equals(System.Net.IPAddress.Any));
                }
                catch
                {
                    hasGateway = false;
                }
                if (!hasGateway) continue;

                if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) wifiConnected = true;
                else if (nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                         or NetworkInterfaceType.GigabitEthernet
                         or NetworkInterfaceType.FastEthernetT
                         or NetworkInterfaceType.FastEthernetFx) wiredConnected = true;
            }
        }
        catch
        {
            // état réseau indisponible
        }

        if (wifiConnected) return "\uE701";
        if (wiredConnected) return "\uE839";
        return "\uE774";
    }

    // ---------- Clavier ----------

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    // ---------- Barre active ----------

    private void SelectProfile(int index)
    {
        if (_config.Profiles.Count == 0) return;

        index = Math.Clamp(index, 0, _config.Profiles.Count - 1);
        _currentProfile = index;
        var profile = _config.Profiles[index];

        BarNameText.Text = profile.Name;
        BarNameText.ToolTip = string.IsNullOrEmpty(profile.Hotkey)
            ? $"Barre « {profile.Name} »"
            : $"Barre « {profile.Name} » ({profile.Hotkey})";

        try
        {
            Background = (Brush)new BrushConverter().ConvertFromString(profile.Color)!;
        }
        catch
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x1A));
        }

        foreach (var bar in _secondaryBars) bar.UpdateBackground(Background);

        _lastSignature = "";
        RefreshTaskbar();
    }

    // ---------- Applications épinglées et ouvertes ----------

    private void StartTaskbarTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            UpdateFullscreenState();
            foreach (var bar in _secondaryBars) bar.EnforcePosition();
            RefreshTaskbar();
            if (_config.HideWindowsTaskbar && !_peeking) TaskbarControl.KeepHidden();
        };
        timer.Start();

        StartBadgeWatcher();
    }

    private void RefreshTaskbar()
    {
        if (_config.Profiles.Count == 0) return;

        IntPtr self = new WindowInteropHelper(this).Handle;

        IntPtr foreground = Native.GetForegroundWindow();
        if (foreground != IntPtr.Zero && !IsBarWindow(foreground)) _lastForeground = foreground;

        var groups = new Dictionary<string, TaskItem>(StringComparer.OrdinalIgnoreCase);
        foreach (IntPtr hwnd in Native.GetTaskbarWindows(self))
        {
            string exe = Native.GetExePath(hwnd);
            string appId = Native.GetPackageAppId(hwnd, exe);

            string key;
            if (!string.IsNullOrEmpty(appId)) key = "aumid:" + appId.ToLowerInvariant();
            else if (string.IsNullOrEmpty(exe)
                     || exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                key = "hwnd:" + hwnd;
            else key = exe.ToLowerInvariant();

            if (!groups.TryGetValue(key, out var group))
            {
                group = new TaskItem { Key = key, Name = Native.GetText(hwnd), ExePath = exe, AppId = appId };
                groups[key] = group;
            }
            group.Windows.Add(hwnd);
        }

        _runningOrder.RemoveAll(k => !groups.ContainsKey(k));
        foreach (string key in groups.Keys)
        {
            if (!_runningOrder.Contains(key)) _runningOrder.Add(key);
        }

        var items = new List<TaskItem>();
        var pinnedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in _config.Profiles[_currentProfile].Apps)
        {
            string key;
            string path = "";

            if (!string.IsNullOrEmpty(app.AppId))
            {
                key = "aumid:" + app.AppId.ToLowerInvariant();
            }
            else
            {
                path = Environment.ExpandEnvironmentVariables(app.Path);
                if (!File.Exists(path)) continue;
                key = path.ToLowerInvariant();
            }

            if (!pinnedKeys.Add(key)) continue;

            if (groups.TryGetValue(key, out var running))
            {
                running.Pinned = true;
                running.Name = app.Name;
                running.IconPath = app.IconPath;
                if (!string.IsNullOrEmpty(app.AppId)) running.AppId = app.AppId;
                items.Add(running);
            }
            else
            {
                items.Add(new TaskItem
                {
                    Key = key, Name = app.Name, ExePath = path,
                    IconPath = app.IconPath, AppId = app.AppId, Pinned = true
                });
            }
        }

        foreach (string key in _runningOrder)
        {
            if (!pinnedKeys.Contains(key)) items.Add(groups[key]);
        }

        bool foregroundMinimized = Native.IsIconic(_lastForeground);
        foreach (var item in items) item.IsActive = !foregroundMinimized && item.Windows.Contains(_lastForeground);

        foreach (var item in items) item.Badge = GetBadgeFor(item);

        _lastItems = items;

        string signature = _currentProfile + "#" +
            string.Join("|", items.Select(i => $"{i.Key}:{i.Windows.Count}:{i.IsActive}:{i.Badge}"));
        if (signature != _lastSignature)
        {
            _lastSignature = signature;
            HidePreview();
            AppsPanel.Children.Clear();
            foreach (var item in items) AppsPanel.Children.Add(BuildTaskButton(item));
            SyncSecondaryPanels();
        }
    }

    // ---------- Pastilles de notification ----------

    private void StartBadgeWatcher()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += async (_, _) =>
        {
            if (_badgeReadRunning) return;
            _badgeReadRunning = true;
            try
            {
                var (badges, error) = await System.Threading.Tasks.Task.Run(() =>
                {
                    var read = NotificationBadges.Read(out string message);
                    return (read, message);
                });

                if (!string.IsNullOrEmpty(error) && error != _badgeError)
                {
                    _badgeError = error;
                    DebugLog.Write("Pastilles : erreur de lecture : " + error);
                }

                if (badges == null) return;
                _badges = badges;

                string key = string.Join(";", badges.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value));
                if (key != _badgeLogKey)
                {
                    _badgeLogKey = key;
                    DebugLog.Write("Pastilles : " + (key.Length == 0 ? "(aucune)" : key));
                }
            }
            finally
            {
                _badgeReadRunning = false;
            }
        };
        timer.Start();
    }

    private string GetBadgeFor(TaskItem item)
    {
        if (string.IsNullOrEmpty(item.AppId) || _badges.Count == 0) return "";

        if (!_badges.TryGetValue(item.AppId, out string? value))
        {
            // Même application, identifiant légèrement différent : on compare le nom du paquet
            string family = item.AppId.Split('!')[0] + "!";
            value = _badges.FirstOrDefault(p => p.Key.StartsWith(family, StringComparison.OrdinalIgnoreCase)).Value;
        }

        if (string.IsNullOrEmpty(value)) return "";
        if (int.TryParse(value, out int number)) return number > 0 ? number.ToString() : "";
        return value;   // symbole (ex. newMessage) : on affichera un simple point
    }

    private static FrameworkElement BuildBadge(string value)
    {
        bool numeric = int.TryParse(value, out int number);
        string text = numeric ? (number > 99 ? "99+" : number.ToString()) : "";

        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x24)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            MinWidth = text.Length == 0 ? 10 : 16,
            Height = text.Length == 0 ? 10 : 16,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -1, -4, 0),
            IsHitTestVisible = false
        };

        if (text.Length > 0)
        {
            badge.Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0)
            };
        }

        return badge;
    }

    private static Border BuildIndicatorBar(double width, bool active)
    {
        return new Border
        {
            Height = 3,
            Width = width,
            CornerRadius = new CornerRadius(2),
            Background = active
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF))
        };
    }

    private Button BuildTaskButton(TaskItem item)
    {
        bool running = item.Windows.Count > 0;

        var grid = new Grid { Width = 28, Height = 32 };

        ImageSource? iconSource = GetIconSource(item);
        if (iconSource != null)
        {
            var image = new Image
            {
                Width = IconSize, Height = IconSize, Source = iconSource,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 0, 0)
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            grid.Children.Add(image);
        }

        if (running)
        {
            var indicator = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            indicator.Children.Add(BuildIndicatorBar(item.IsActive ? 14 : 5, item.IsActive));

            if (item.Windows.Count > 1)
            {
                Border second = BuildIndicatorBar(5, false);
                second.Margin = new Thickness(2, 0, 0, 0);
                indicator.Children.Add(second);
            }

            grid.Children.Add(indicator);
        }

        if (!string.IsNullOrEmpty(item.Badge)) grid.Children.Add(BuildBadge(item.Badge));

        var button = new Button
        {
            Content = grid,
            ToolTip = running ? null : item.Name,
            Width = 36,
            Height = 38,
            Margin = new Thickness(2, 0, 2, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        button.Click += (_, _) => OnTaskClick(item, button);

        if (running)
        {
            button.MouseEnter += (_, _) => OnTaskHoverEnter(button, item);
            button.MouseLeave += (_, _) => OnTaskHoverLeave();
        }

        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            HidePreview();
            FillTaskMenu(menu, item);
        };
        button.ContextMenu = menu;

        return button;
    }

    // ---------- Aperçu au survol ----------

    private bool IsBarWindow(IntPtr hwnd)
    {
        if (hwnd == new WindowInteropHelper(this).Handle) return true;
        if (_previewPopup != null && hwnd == _previewPopup.Handle) return true;

        foreach (var bar in _secondaryBars)
        {
            if (hwnd == bar.Handle) return true;
        }
        return false;
    }

    private void OnTaskHoverEnter(Button button, TaskItem item)
    {
        _previewCloseTimer?.Stop();

        if (_previewPopup != null)
        {
            if (_previewKey == item.Key && _previewButton == button) return;

            // Un aperçu est déjà ouvert : on passe tout de suite à cette icône
            ShowPreview(button, item);
            return;
        }

        _previewShowTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ShowPreview(button, item);
        };
        _previewShowTimer = timer;
        timer.Start();
    }

    private void OnTaskHoverLeave()
    {
        _previewShowTimer?.Stop();
        StartPreviewClose();
    }

    // Petit délai avant de fermer : laisse le temps d'aller de l'icône vers l'aperçu
    private void StartPreviewClose()
    {
        if (_previewPopup == null) return;

        _previewCloseTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _previewCloseTimer.Tick -= PreviewCloseTick;
        _previewCloseTimer.Tick += PreviewCloseTick;
        _previewCloseTimer.Stop();
        _previewCloseTimer.Start();
    }

    private void PreviewCloseTick(object? sender, EventArgs e)
    {
        _previewCloseTimer?.Stop();

        bool overButton = _previewButton != null && _previewButton.IsMouseOver;
        bool overPopup = _previewPopup != null && _previewPopup.IsMouseOver;
        if (!overButton && !overPopup) HidePreview();
    }

    private void HidePreview()
    {
        _previewShowTimer?.Stop();
        _previewCloseTimer?.Stop();

        WindowPreviewPopup? popup = _previewPopup;
        _previewPopup = null;
        _previewButton = null;
        _previewKey = "";

        popup?.Close();
    }

    private void ShowPreview(Button button, TaskItem item)
    {
        HidePreview();

        List<IntPtr> windows = item.Windows.Where(Native.IsWindowVisible).ToList();
        if (windows.Count == 0) return;

        Window? host = Window.GetWindow(button);
        PresentationSource? source = PresentationSource.FromVisual(button);
        if (host == null || source?.CompositionTarget == null) return;

        Matrix fromDevice = source.CompositionTarget.TransformFromDevice;

        // Zone de travail de l'écran qui porte cette barre (principal ou secondaire)
        Point workTopLeft;
        Point workBottomRight;
        var info = new Native.MonitorInfo { cbSize = Marshal.SizeOf<Native.MonitorInfo>() };
        IntPtr monitor = Native.MonitorFromWindow(new WindowInteropHelper(host).Handle, 2);
        if (monitor != IntPtr.Zero && Native.GetMonitorInfo(monitor, ref info))
        {
            workTopLeft = fromDevice.Transform(new Point(info.rcWork.left, info.rcWork.top));
            workBottomRight = fromDevice.Transform(new Point(info.rcWork.right, info.rcWork.bottom));
        }
        else
        {
            workTopLeft = new Point(0, 0);
            workBottomRight = new Point(SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        var popup = new WindowPreviewPopup(windows, hwnd =>
        {
            HidePreview();
            ActivateWindow(hwnd);
        }, workBottomRight.X - workTopLeft.X - 16);

        // PointToScreen donne des pixels : on les convertit en unités WPF
        Point buttonTopLeft = fromDevice.Transform(button.PointToScreen(new Point(0, 0)));
        double left = buttonTopLeft.X + button.ActualWidth / 2 - popup.Width / 2;
        left = Math.Max(workTopLeft.X + 8, Math.Min(left, workBottomRight.X - popup.Width - 8));
        double top = Math.Max(workTopLeft.Y, buttonTopLeft.Y - popup.Height - 4);

        popup.Left = left;
        popup.Top = top;
        popup.MouseEnter += (_, _) => _previewCloseTimer?.Stop();
        popup.MouseLeave += (_, _) => StartPreviewClose();

        _previewPopup = popup;
        _previewButton = button;
        _previewKey = item.Key;
        popup.Show();
    }

    private static bool CanLaunch(TaskItem item)
    {
        if (!string.IsNullOrEmpty(item.AppId)) return true;

        return !string.IsNullOrEmpty(item.ExePath)
               && !item.ExePath.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static void LaunchNew(TaskItem item)
    {
        if (!string.IsNullOrEmpty(item.AppId)) { ShellApps.Launch(item.AppId); return; }
        if (!CanLaunch(item)) return;

        try
        {
            Process.Start(new ProcessStartInfo(item.ExePath) { UseShellExecute = true });
        }
        catch
        {
            // lancement impossible : on ignore
        }
    }

    private static void ActivateWindow(IntPtr hwnd)
    {
        if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
        Native.SetForegroundWindow(hwnd);
    }

    // ---------- Épingler / détacher ----------

    private static string GetFriendlyName(TaskItem item)
    {
        bool frameHost = item.ExePath.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);

        if (!frameHost && !string.IsNullOrEmpty(item.ExePath) && File.Exists(item.ExePath))
        {
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(item.ExePath);
                if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription.Trim();
                if (!string.IsNullOrWhiteSpace(info.ProductName)) return info.ProductName.Trim();
            }
            catch
            {
                // informations de version illisibles : on utilise le nom du fichier
            }

            return System.IO.Path.GetFileNameWithoutExtension(item.ExePath);
        }

        return string.IsNullOrWhiteSpace(item.Name) ? "Application" : item.Name;
    }

    private static bool MatchesItem(AppEntry app, TaskItem item)
    {
        if (!string.IsNullOrEmpty(app.AppId) || !string.IsNullOrEmpty(item.AppId))
            return string.Equals(app.AppId, item.AppId, StringComparison.OrdinalIgnoreCase);

        string path = Environment.ExpandEnvironmentVariables(app.Path);
        return string.Equals(path, item.ExePath, StringComparison.OrdinalIgnoreCase);
    }

    private void PinToCurrentBar(TaskItem item)
    {
        if (_config.Profiles.Count == 0 || !CanLaunch(item)) return;

        Profile profile = _config.Profiles[_currentProfile];
        if (profile.Apps.Any(app => MatchesItem(app, item))) return;

        profile.Apps.Add(new AppEntry
        {
            Name = GetFriendlyName(item),
            Path = string.IsNullOrEmpty(item.AppId) ? item.ExePath : "",
            AppId = item.AppId
        });

        SaveConfigQuietly();
        _lastSignature = "";
        RefreshTaskbar();
    }

    private void UnpinFromCurrentBar(TaskItem item)
    {
        if (_config.Profiles.Count == 0) return;

        Profile profile = _config.Profiles[_currentProfile];
        profile.Apps.RemoveAll(app => MatchesItem(app, item));

        SaveConfigQuietly();
        _lastSignature = "";
        RefreshTaskbar();
    }

    private static ImageSource? GetWindowIconSource(IntPtr hwnd)
    {
        try
        {
            IntPtr hIcon = Native.GetWindowIcon(hwnd);
            if (hIcon == IntPtr.Zero) return null;
            return Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch
        {
            return null;
        }
    }

    private MenuItem BuildWindowMenuItem(IntPtr hwnd)
    {
        string title = Native.GetText(hwnd);
        if (string.IsNullOrWhiteSpace(title)) title = "(sans titre)";
        if (title.Length > 60) title = title.Substring(0, 57) + "…";

        var menuItem = new MenuItem { Header = title.Replace("_", "__"), IsChecked = hwnd == _lastForeground };

        ImageSource? icon = GetWindowIconSource(hwnd);
        if (icon != null) menuItem.Icon = new Image { Source = icon, Width = 16, Height = 16 };

        menuItem.Click += (_, _) => ActivateWindow(hwnd);
        return menuItem;
    }

    private void ShowWindowList(Button button, TaskItem item)
    {
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Top };
        foreach (IntPtr hwnd in item.Windows) menu.Items.Add(BuildWindowMenuItem(hwnd));
        menu.IsOpen = true;
    }

    private void FillTaskMenu(ContextMenu menu, TaskItem item)
    {
        menu.Items.Clear();

        if (item.Windows.Count > 0)
        {
            foreach (IntPtr hwnd in item.Windows) menu.Items.Add(BuildWindowMenuItem(hwnd));
            menu.Items.Add(new Separator());
        }

        if (CanLaunch(item) && string.IsNullOrEmpty(item.AppId))
        {
            List<BrowserProfiles.ProfileInfo> profiles = BrowserProfiles.GetProfiles(item.ExePath);
            if (profiles.Count > 0)
            {
                foreach (BrowserProfiles.ProfileInfo profile in profiles)
                {
                    string exePath = item.ExePath;
                    string directory = profile.Directory;

                    var profileItem = new MenuItem
                    {
                        Header = ("Ouvrir le profil « " + profile.Name + " »").Replace("_", "__")
                    };
                    profileItem.Click += (_, _) => BrowserProfiles.Launch(exePath, directory);
                    menu.Items.Add(profileItem);
                }
                menu.Items.Add(new Separator());
            }
        }

        if (CanLaunch(item))
        {
            var newItem = new MenuItem { Header = "Nouvelle fenêtre" };
            newItem.Click += (_, _) => LaunchNew(item);
            menu.Items.Add(newItem);

            if (item.Pinned)
            {
                var unpinItem = new MenuItem { Header = "Détacher de la barre" };
                unpinItem.Click += (_, _) => UnpinFromCurrentBar(item);
                menu.Items.Add(unpinItem);
            }
            else if (_config.Profiles.Count > 0)
            {
                string barName = _config.Profiles[_currentProfile].Name;
                var pinItem = new MenuItem { Header = ("Épingler à la barre « " + barName + " »").Replace("_", "__") };
                pinItem.Click += (_, _) => PinToCurrentBar(item);
                menu.Items.Add(pinItem);
            }
        }

        if (item.Windows.Count > 0)
        {
            var closeItem = new MenuItem
            {
                Header = item.Windows.Count > 1 ? "Fermer toutes les fenêtres" : "Fermer la fenêtre"
            };
            closeItem.Click += (_, _) =>
            {
                foreach (IntPtr hwnd in item.Windows)
                    Native.PostMessage(hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            };
            menu.Items.Add(closeItem);
        }
    }

    private ImageSource? GetIconSource(TaskItem item)
    {
        try
        {
            if (item.Pinned) return IconHelper.Load(item.ExePath, item.IconPath, item.AppId);

            if (!string.IsNullOrEmpty(item.AppId))
            {
                ImageSource? shellIcon = ShellApps.GetIcon(item.AppId);
                if (shellIcon != null) return shellIcon;
            }

            if (item.Windows.Count > 0)
            {
                IntPtr hIcon = Native.GetWindowIcon(item.Windows[0]);
                if (hIcon != IntPtr.Zero)
                    return Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }

            return IconHelper.Load(item.ExePath, "");
        }
        catch
        {
            return null;
        }
    }

    private void OnTaskClick(TaskItem item, Button button)
    {
        if (item.Windows.Count == 0) { HidePreview(); LaunchNew(item); return; }

        // Plusieurs fenêtres : le clic ouvre l'aperçu tout de suite (sinon liste simple)
        if (item.Windows.Count > 1)
        {
            if (_previewPopup != null && _previewButton == button) return;

            ShowPreview(button, item);
            if (_previewPopup == null) ShowWindowList(button, item);
            return;
        }

        HidePreview();

        if (item.IsActive && !Native.IsIconic(_lastForeground))
        {
            Native.ShowWindow(_lastForeground, Native.SW_MINIMIZE);
            return;
        }

        ActivateWindow(item.Windows[0]);
    }

    // ---------- Réservation de l'espace à l'écran (barre principale) ----------

    private void RegisterBar()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;

        var dpi = VisualTreeHelper.GetDpi(this);
        int screenW = (int)(SystemParameters.PrimaryScreenWidth * dpi.DpiScaleX);
        int screenH = (int)(SystemParameters.PrimaryScreenHeight * dpi.DpiScaleY);
        int barH = (int)(BarHeightDip * dpi.DpiScaleY);

        var monitorRect = new Native.NativeRect { left = 0, top = 0, right = screenW, bottom = screenH };
        AppBar.Register(hwnd, monitorRect, barH);
    }

    private void PositionBar()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        int screenW = (int)(SystemParameters.PrimaryScreenWidth * dpi.DpiScaleX);
        int screenH = (int)(SystemParameters.PrimaryScreenHeight * dpi.DpiScaleY);
        int barH = (int)(BarHeightDip * dpi.DpiScaleY);

        var monitorRect = new Native.NativeRect { left = 0, top = 0, right = screenW, bottom = screenH };
        AppBar.Reposition(hwnd, monitorRect, barH);
    }

    private void RepositionSoon()
    {
        foreach (int delay in new[] { 400, 1500 })
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                PositionBar();
                RebuildSecondaryBars();
                foreach (var bar in _secondaryBars) bar.RefreshAppBar();
                LogWorkAreas();
            };
            timer.Start();
        }
    }

    private void LogWorkAreas()
    {
        var parts = new List<string>();
        IntPtr self = new WindowInteropHelper(this).Handle;
        parts.Add($"principale ecran=({Native.MonitorRectText(self)}) travail=({Native.WorkAreaText(self)})");
        for (int i = 0; i < _secondaryBars.Count; i++)
        {
            IntPtr handle = _secondaryBars[i].Handle;
            parts.Add($"secondaire{i + 1} ecran=({Native.MonitorRectText(handle)}) travail=({Native.WorkAreaText(handle)})");
        }
        DebugLog.Write("Zones de travail : " + string.Join(" || ", parts));
    }

    private void UnregisterBar()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;

        foreach (int id in _hotkeyIds) UnregisterHotKey(hwnd, id);
        _hotkeyIds.Clear();

        AppBar.Unregister(hwnd);
    }
}
