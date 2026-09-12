using System.Text.Json;
namespace LocalMouse;
public interface IPCActionInput { void Execute(string action); }
public static class ActionProtocol {
    public static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) {
        "brave", "calculator", "notepad", "explorer", "lock", "sleep", "restart", "shutdown"
    };
    public static bool RequiresConfirmation(string action) => action is "sleep" or "restart" or "shutdown";
    public static string Read(JsonElement frame) {
        if (!frame.TryGetProperty("action",out var value) || value.ValueKind != JsonValueKind.String || !Allowed.Contains(value.GetString()!))
            throw new InvalidDataException("Unknown PC action");
        var action = value.GetString()!;
        if (RequiresConfirmation(action) && (!frame.TryGetProperty("confirmed",out var confirmed) || confirmed.ValueKind != JsonValueKind.True))
            throw new InvalidDataException("Power action requires confirmation");
        return action;
    }
}
