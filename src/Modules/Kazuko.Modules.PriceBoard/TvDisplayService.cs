using System.ComponentModel;
using System.Runtime.InteropServices;
using Kazuko.Core.PriceBoard;

namespace Kazuko.Modules.PriceBoard;

/// <summary>Windows presentation controls; the user chooses and connects a TV in the native Cast picker.</summary>
public interface ITvDisplayService
{
    IReadOnlyList<TvMonitor> GetMonitors();
    void ShowMiracastDevicePicker();
    void ShowProjectionModePicker();
}

public interface IPriceBoardTvSettingsService
{
    event Action? Changed;
    string LocalUrl { get; }
    string LanAddresses { get; }
    PriceBoardTvSettings Settings { get; }
    IReadOnlyList<GoldPriceRecord> Prices { get; }
    Task InitializeAsync();
    Task ApplyAsync(PriceBoardTvSettings settings);
    Task OpenDisplayAsync(TvMonitor monitor);
    string BackgroundStatus => "";
}

public sealed class WindowsTvDisplayService : ITvDisplayService
{
    public IReadOnlyList<TvMonitor> GetMonitors() => TvMonitor.GetAll();
    public void ShowMiracastDevicePicker() => ShowWindowsShortcut(0x4B, "Windows + K");
    public void ShowProjectionModePicker() => ShowWindowsShortcut(0x50, "Windows + P");

    private static void ShowWindowsShortcut(ushort key, string shortcut)
    {
        Input[] inputs = [Keyboard(0x5B, false), Keyboard(key, false), Keyboard(key, true), Keyboard(0x5B, true)];
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent == inputs.Length) return;
        var error = Marshal.GetLastWin32Error();
        // Release only keys introduced by this operation after a partial insertion.
        if (sent > 0)
            SendInput(2, [Keyboard(key, true), Keyboard(0x5B, true)], Marshal.SizeOf<Input>());
        throw new Win32Exception(error,
            $"Không mở được bảng chọn của Windows. Hãy nhấn {shortcut} để mở và chọn thiết bị/chế độ chiếu.");
    }

    private static Input Keyboard(ushort key, bool up) => new()
    {
        Type = 1,
        Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = up ? 2u : 0u } }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public HardwareInput Hardware;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput { public uint Message; public ushort Low, High; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);
}
