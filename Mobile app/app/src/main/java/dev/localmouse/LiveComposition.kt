package dev.localmouse

/** Tracks only the composing suffix already sent to the PC, not the PC document. */
class LiveComposition {
    var value = ""
        private set
    fun update(next: String, send: (List<InputCommand>) -> Boolean): Boolean {
        KeyboardCommands.text(next) // Validate before making any changes.
        var prefix = 0
        while (prefix < value.length && prefix < next.length && value[prefix] == next[prefix]) prefix++
        if (prefix > 0 && prefix < value.length && Character.isLowSurrogate(value[prefix])) prefix--
        val deleted = value.codePointCount(prefix, value.length)
        val commands = MutableList<InputCommand>(deleted) { InputCommand.Key("BACKSPACE", emptyList()) }
        commands.addAll(KeyboardCommands.text(next.substring(prefix)))
        if (commands.isNotEmpty() && !send(commands)) return false
        value = next
        return true
    }
    fun forget() { value = "" }
}
