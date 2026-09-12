package dev.localmouse

/** A batch keeps a pasted string together in the bounded transport queue. */
object KeyboardCommands {
    @JvmStatic fun text(value: String): List<InputCommand> {
        require(value.length <= 2048) { "Paste at most 2048 characters at a time." }
        var index = 0
        while (index < value.length) {
            val c = value[index]
            require(!Character.isISOControl(c) || c == '\n' || c == '\r' || c == '\t') { "Unsupported control character." }
            if (Character.isHighSurrogate(c)) {
                require(index + 1 < value.length && Character.isLowSurrogate(value[index+1])) { "Invalid Unicode." }
                index++
            } else require(!Character.isLowSurrogate(c)) { "Invalid Unicode." }
            index++
        }
        val result = mutableListOf<InputCommand>()
        index = 0
        while (index < value.length) {
            var end = (index + 256).coerceAtMost(value.length)
            if (end < value.length && Character.isHighSurrogate(value[end-1])) end--
            // Keep CRLF together so the PC emits one Enter, even at a chunk boundary.
            if (end < value.length && value[end-1] == '\r' && value[end] == '\n') end--
            result.add(InputCommand.Text(value.substring(index,end)))
            index = end
        }
        return result
    }
    @JvmStatic fun modified(value: String, modifiers: List<String>): List<InputCommand> {
        if (modifiers.isEmpty()) return text(value)
        val special = when (value) { "\n", "\r\n" -> "ENTER"; "\t" -> "TAB"; " " -> "SPACE"; else -> null }
        if (special != null) return listOf(InputCommand.Key(special,modifiers.toList()))
        val letter = value.trim()
        require(letter.length == 1 && (letter[0] in 'A'..'Z' || letter[0] in 'a'..'z' || letter[0] in '0'..'9')) {
            "For a modifier shortcut, type one A–Z or 0–9 key, or use a special-key button."
        }
        return listOf(InputCommand.Key(letter.uppercase(java.util.Locale.ROOT), modifiers.toList()))
    }
}

/** IME composition is local; committing replaces the draft rather than appending it. */
class ImeDraft {
    var value: String = ""
        private set
    fun compose(text: String) { value = text }
    fun commit(text: String): String { value = ""; return text }
    fun finish(): String { val text = value; value = ""; return text }
    fun clear() { value = "" }
}
