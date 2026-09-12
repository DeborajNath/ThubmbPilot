package dev.localmouse

import android.content.Context
import android.view.inputmethod.InputMethodManager
import android.widget.*

/** Compact shortcuts and a focus target for the phone keyboard. */
class KeyboardPanel(context: Context) : LinearLayout(context) {
    var send: (List<InputCommand>) -> Boolean = { false }
    var beforeShortcut: () -> Unit = {}
    private val editor = RemoteKeyboardEditor(context)
    private val row = LinearLayout(context)
    private val controls = mutableListOf<Button>()
    private var allowed = false
    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()
    init {
        orientation = VERTICAL
        editor.alpha = 0f
        addView(editor, LayoutParams(-1, 1))
        editor.onCommands = { commands ->
            val sent = allowed && send(commands)
            if (!sent) Toast.makeText(context,"Typing was not queued. Check the connection and PC text.",Toast.LENGTH_LONG).show()
            sent
        }
        editor.onKey = { key -> allowed && send(listOf(InputCommand.Key(key, emptyList()))) }
        row.visibility = GONE
        for ((label,key,mods) in listOf(
            Triple("Copy","C",listOf("CTRL")),
            Triple("Paste","V",listOf("CTRL")),
            Triple("Task manager","ESC",listOf("CTRL","SHIFT")))) {
            val button = Button(context).apply {
                text = label; textSize = 11f; Forest.button(this); isAllCaps = false; isFocusable = false
                setOnClickListener { beforeShortcut(); pointerBoundary(); if (allowed) send(listOf(InputCommand.Key(key,mods))) }
            }
            controls.add(button)
            row.addView(button, LayoutParams(0,dp(48),1f).apply { setMargins(dp(2),dp(3),dp(2),dp(3)) })
        }
        addView(row)
        setControlEnabled(false)
    }
    fun pointerBoundary() { editor.endComposition() }
    fun setControlEnabled(value: Boolean) {
        if (allowed && !value) { editor.endComposition(); hideIme() }
        allowed = value; editor.isEnabled = value
        controls.forEach { it.isEnabled = value }
    }
    fun open() {
        if (!allowed) return
        row.visibility = VISIBLE
        editor.requestFocus()
        editor.post { if (allowed && isShown) (context.getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager).showSoftInput(editor,InputMethodManager.SHOW_IMPLICIT) }
    }
    fun finishTyping(): Boolean { editor.endComposition(); return true }
    fun hideIme() {
        row.visibility = GONE
        editor.endComposition()
        (context.getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager).hideSoftInputFromWindow(editor.windowToken,0)
        editor.clearFocus()
    }
}
