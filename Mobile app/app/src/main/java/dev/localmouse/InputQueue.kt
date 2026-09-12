package dev.localmouse

sealed class InputCommand {
    data class Action(val action: String, val confirmed: Boolean = false) : InputCommand()
    data class Move(val dx: Int, val dy: Int) : InputCommand()
    data class Click(val button: String) : InputCommand()
    data class Scroll(val dx: Int, val dy: Int) : InputCommand()
    data class Button(val down: Boolean) : InputCommand()
    data class Text(val text: String) : InputCommand()
    data class Key(val key: String, val modifiers: List<String>) : InputCommand()
}

/** One ordered, bounded queue. Adjacent moves merge; clicks remain ordering barriers. */
class InputQueue(private val clock: () -> Long = { System.nanoTime() / 1_000_000 }) {
    private data class Entry(val command: InputCommand, val time: Long)
    private val monitor = Object()
    private val pending = java.util.ArrayDeque<Entry>()
    private var enabled = false

    fun setEnabled(value: Boolean) = synchronized(monitor) {
        enabled = value
        if (!value) pending.clear()
        monitor.notifyAll()
    }

    fun isEnabled(): Boolean = synchronized(monitor) { enabled }
    fun offerBatch(commands: List<InputCommand>): Boolean = synchronized(monitor) {
        if (!enabled || pending.size + commands.size > 32) return false
        for (command in commands) pending.addLast(Entry(command, clock()))
        monitor.notifyAll()
        true
    }
    fun offer(command: InputCommand): Boolean = synchronized(monitor) {
        if (!enabled) return false
        discardExpired()
        val tail = pending.peekLast()
        if (command is InputCommand.Move && tail?.command is InputCommand.Move) {
            val previous = tail.command
            pending.removeLast()
            pending.addLast(Entry(InputCommand.Move(
                (previous.dx + command.dx).coerceIn(-512, 512),
                (previous.dy + command.dy).coerceIn(-512, 512)), tail.time))
        } else if (command is InputCommand.Scroll && tail?.command is InputCommand.Scroll) {
            val previous = tail.command
            pending.removeLast()
            pending.addLast(Entry(InputCommand.Scroll((previous.dx + command.dx).coerceIn(-512,512),
                (previous.dy + command.dy).coerceIn(-512,512)), tail.time))
        } else {
            if (pending.size >= 32) {
                if (command is InputCommand.Button && !command.down) {
                    if (pending.any { it.command is InputCommand.Text || it.command is InputCommand.Key }) return false
                    pending.clear()
                }
                else return false
            }
            pending.addLast(Entry(command, clock()))
        }
        monitor.notifyAll()
        true
    }

    fun take(timeoutMillis: Long): InputCommand? = synchronized(monitor) {
        val deadline = System.nanoTime() + timeoutMillis * 1_000_000
        while (true) {
            discardExpired()
            if (pending.isNotEmpty()) {
                val entry = pending.removeFirst()
                if ((entry.command is InputCommand.Text || entry.command is InputCommand.Key) && clock() - entry.time > 2000)
                    throw IllegalStateException("Typing stalled; unsent input discarded. Reconnect and check the PC text.")
                return entry.command
            }
            val remaining = (deadline - System.nanoTime()) / 1_000_000
            if (remaining <= 0) return null
            monitor.wait(remaining)
        }
        @Suppress("UNREACHABLE_CODE") null
    }

    private fun discardExpired() {
        while (true) {
            val first = pending.peekFirst() ?: break
            if (first.command is InputCommand.Button && !first.command.down) break
            if (first.command is InputCommand.Text || first.command is InputCommand.Key) break
            if (clock() - first.time <= 250) break
            pending.removeFirst()
        }
    }
}

/** Preserve sub-pixel movement; never round every tiny touch sample down to zero. */
class MotionAccumulator {
    private var x = 0.0
    private var y = 0.0
    fun add(dx: Double, dy: Double) {
        x = (x + dx).coerceIn(-512.0, 512.0)
        y = (y + dy).coerceIn(-512.0, 512.0)
    }
    fun drain(): InputCommand.Move {
        val dx = x.toInt(); val dy = y.toInt()
        x -= dx; y -= dy
        return InputCommand.Move(dx, dy)
    }
    fun reset() { x = 0.0; y = 0.0 }
}