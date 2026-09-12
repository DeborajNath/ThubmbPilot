using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LocalMouse;

public interface IMouseInput
{
    void Move(int dx, int dy);
    void Click(string button);
    void Scroll(int dx, int dy);
    void SetLeft(bool down);
}

public sealed class WindowsMouseInput : IMouseInput
{
    public void Move(int dx, int dy) => Inject([Movement(dx, dy)]);

    public void SetLeft(bool down) => Inject([new() { Mouse = new MouseData { Flags = down ? 2u : 4u } }]);
    public void Scroll(int dx, int dy)
    {
        if (dy != 0) Inject([Wheel(dy, false)]);
        if (dx != 0) Inject([Wheel(dx, true)]);
    }
    internal static NativeInput Wheel(int delta, bool horizontal) => new() {
        Mouse = new MouseData { Data = unchecked((uint)delta), Flags = horizontal ? 0x1000u : 0x0800u }
    };
    public void Click(string button)
    {
        var pair = ClickPair(button);
        var count = SendInput((uint)pair.Length, pair, Marshal.SizeOf<NativeInput>());
        if (count == 2) return;
        var error = Marshal.GetLastWin32Error();
        // A click never intentionally leaves a button down, even after partial insertion.
        if (count == 1) SendInput(1, [pair[1]], Marshal.SizeOf<NativeInput>());
        throw new Win32Exception(error, "Windows could not insert the mouse click. Elevated windows may block input.");
    }

    private static void Inject(NativeInput[] input)
    {
        if (SendInput((uint)input.Length, input, Marshal.SizeOf<NativeInput>()) != input.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not insert mouse movement. Elevated windows may block input.");
    }

    internal static NativeInput Movement(int dx, int dy) => new() {
        Mouse = new MouseData { Dx = dx, Dy = dy, Flags = 0x0001 }
    };
    internal static NativeInput[] ClickPair(string button)
    {
        var down = button switch { "left" => 0x0002u, "right" => 0x0008u, _ => throw new ArgumentException("Unknown button") };
        return [new() { Mouse = new MouseData { Flags = down } }, new() { Mouse = new MouseData { Flags = down << 1 } }];
    }

    // INPUT's union has MOUSEINPUT as its largest member. Sequential layout supplies
    // the native 8-byte alignment on x64 (40 bytes total), and 28 bytes on x86.
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeInput { public uint Type; public MouseData Mouse; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseData
    {
        public int Dx, Dy;
        public uint Data, Flags, Time;
        public nuint ExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] NativeInput[] inputs, int size);
}