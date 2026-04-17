using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Global hotkey registration using Win32 RegisterHotKey.
/// Default: Alt+Ctrl+C (mirrors macOS Option+Cmd+C).
/// </summary>
public class HotkeyService : IDisposable
{
    private const int HOTKEY_ID = 0x434C; // "CL"
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_C = 0x43;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _windowHandle;
    private HwndSource? _source;
    public event Action? HotkeyPressed;

    public void Register(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.Handle;

        if (_windowHandle == IntPtr.Zero)
        {
            // Window not yet shown, defer
            window.SourceInitialized += (_, _) =>
            {
                _windowHandle = new WindowInteropHelper(window).Handle;
                RegisterInternal();
            };
            return;
        }

        RegisterInternal();
    }

    private void RegisterInternal()
    {
        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(HwndHook);
        RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_ALT | MOD_CONTROL | MOD_NOREPEAT, VK_C);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _source?.RemoveHook(HwndHook);
        if (_windowHandle != IntPtr.Zero)
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
    }
}
