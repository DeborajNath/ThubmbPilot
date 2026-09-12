using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LocalMouse;

public interface IKeyboardInput
{
    void Text(string text);
    void Key(string key, IReadOnlyList<string> modifiers);
}

public static class KeyboardProtocol
{
    public static readonly IReadOnlyDictionary<string, ushort> Keys = BuildKeys();
    public static readonly IReadOnlyDictionary<string, ushort> Modifiers = new Dictionary<string, ushort> {
        ["CTRL"] = 0xA2, ["ALT"] = 0xA4, ["SHIFT"] = 0xA0, ["WIN"] = 0x5B
    };
    private static Dictionary<string, ushort> BuildKeys()
    {
        var keys = new Dictionary<string, ushort> {
            ["ENTER"] = 0x0D, ["BACKSPACE"] = 0x08, ["DELETE"] = 0x2E,
            ["TAB"] = 0x09, ["ESC"] = 0x1B, ["SPACE"] = 0x20,
            ["LEFT"] = 0x25, ["UP"] = 0x26, ["RIGHT"] = 0x27, ["DOWN"] = 0x28,
            ["HOME"] = 0x24, ["END"] = 0x23, ["PAGEUP"] = 0x21, ["PAGEDOWN"] = 0x22,
            ["WIN"] = 0x5B,
            ["VOLUME_MUTE"] = 0xAD, ["VOLUME_DOWN"] = 0xAE, ["VOLUME_UP"] = 0xAF,
            ["MEDIA_NEXT"] = 0xB0, ["MEDIA_PREVIOUS"] = 0xB1, ["MEDIA_PLAY_PAUSE"] = 0xB3
        };
        for (char c = 'A'; c <= 'Z'; c++) keys[c.ToString()] = c;
        for (char c = '0'; c <= '9'; c++) keys[c.ToString()] = c;
        for (int i = 1; i <= 12; i++) keys["F" + i] = (ushort)(0x6F + i);
        return keys;
    }
    public static void ValidateText(string text)
    {
        if (text.Length is < 1 or > 256) throw new InvalidDataException("Text length out of bounds");
        for (int i = 0; i < text.Length; i++) {
            var c = text[i];
            if (char.IsControl(c) && c is not ('\r' or '\n' or '\t')) throw new InvalidDataException("Unsupported control character");
            if (char.IsHighSurrogate(c)) {
                if (++i == text.Length || !char.IsLowSurrogate(text[i])) throw new InvalidDataException("Invalid Unicode");
            } else if (char.IsLowSurrogate(c)) throw new InvalidDataException("Invalid Unicode");
        }
    }
    public static (string Key, string[] Modifiers) ReadKey(JsonElement frame)
    {
        if (!frame.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String ||
            !Keys.ContainsKey(key.GetString()!) || !frame.TryGetProperty("modifiers", out var mods) ||
            mods.ValueKind != JsonValueKind.Array || mods.GetArrayLength() > 4) throw new InvalidDataException("Invalid key command");
        var result = new List<string>();
        foreach (var modifier in mods.EnumerateArray()) {
            if (modifier.ValueKind != JsonValueKind.String || !Modifiers.ContainsKey(modifier.GetString()!) || result.Contains(modifier.GetString()!))
                throw new InvalidDataException("Invalid modifier");
            result.Add(modifier.GetString()!);
        }
        if (key.GetString() == "WIN" && result.Contains("WIN")) throw new InvalidDataException("Duplicate Windows key");
        return (key.GetString()!, result.ToArray());
    }
}

public sealed class WindowsKeyboardInput : IKeyboardInput
{
    public void Text(string text) { KeyboardProtocol.ValidateText(text); Inject(TextEvents(text)); }
    public void Key(string key, IReadOnlyList<string> modifiers) => Inject(KeyEvents(key, modifiers));

    internal static NativeInput[] TextEvents(string text)
    {
        var result = new List<NativeInput>();
        for (int i = 0; i < text.Length; i++) {
            var c = text[i];
            if (c == '\r' || c == '\n' || c == '\t') {
                if (c == '\r' && i + 1 < text.Length && text[i+1] == '\n') i++;
                result.AddRange(KeyEvents(c == '\t' ? "TAB" : "ENTER", []));
            } else {
                result.Add(Input(0, c, 4));
                result.Add(Input(0, c, 6));
            }
        }
        return result.ToArray();
    }
    internal static NativeInput[] KeyEvents(string key, IReadOnlyList<string> modifiers)
    {
        var keys = new List<ushort>();
        foreach (var name in new[] { "CTRL", "ALT", "SHIFT", "WIN" })
            if (modifiers.Contains(name)) keys.Add(KeyboardProtocol.Modifiers[name]);
        keys.Add(KeyboardProtocol.Keys[key]);
        var events = keys.Select(vk => Input(vk, 0, Extended(vk))).ToList();
        events.AddRange(keys.AsEnumerable().Reverse().Select(vk => Input(vk, 0, Extended(vk) | 2)));
        return events.ToArray();
    }
    private static uint Extended(ushort vk) => vk is >= 0x21 and <= 0x2E or 0x5B or >= 0xAD and <= 0xB3 ? 1u : 0u;
    private static NativeInput Input(ushort key, ushort scan, uint flags) => new() {
        Type = 1, Value = new InputUnion { Keyboard = new KeyboardData { Key = key, Scan = scan, Flags = flags } }
    };
    private static void Inject(NativeInput[] input)
    {
        var count = SendInput((uint)input.Length, input, Marshal.SizeOf<NativeInput>());
        if (count == input.Length) return;
        var error = Marshal.GetLastWin32Error();
        var cleanup = CleanupForPrefix(input, (int)count);
        if (cleanup.Length > 0) SendInput((uint)cleanup.Length, cleanup, Marshal.SizeOf<NativeInput>());
        throw new Win32Exception(error, "Windows blocked keyboard input; a key-release cleanup was attempted.");
    }
    internal static NativeInput[] CleanupForPrefix(NativeInput[] input, int count)
    {
        var held = new List<NativeInput>();
        foreach (var item in input.Take(count)) {
            var k = item.Value.Keyboard;
            if ((k.Flags & 2) == 0) held.Add(item);
            else {
                int index = held.FindLastIndex(x => x.Value.Keyboard.Key == k.Key && x.Value.Keyboard.Scan == k.Scan &&
                    x.Value.Keyboard.Flags == (k.Flags & ~2u));
                if (index >= 0) held.RemoveAt(index);
            }
        }
        held.Reverse();
        return held.Select(x => Input(x.Value.Keyboard.Key, x.Value.Keyboard.Scan, x.Value.Keyboard.Flags | 2)).ToArray();
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeInput { public uint Type; public InputUnion Value; }
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion {
        [FieldOffset(0)] public WindowsMouseInput.MouseData Mouse;
        [FieldOffset(0)] public KeyboardData Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardData { public ushort Key, Scan; public uint Flags, Time; public nuint ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] NativeInput[] input, int size);
}
