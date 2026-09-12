package dev.localmouse

import android.content.Context
import android.text.Editable
import android.text.InputType
import android.text.TextWatcher
import android.view.KeyEvent
import android.view.inputmethod.*
import android.widget.EditText

/** Keeps native IME context so spaces, word boundaries and sentence caps work normally. */
class RemoteKeyboardEditor(context: Context) : EditText(context) {
    var onCommands: (List<InputCommand>) -> Boolean = { false }
    var onKey: (String) -> Boolean = { false }
    private val mirror = LiveComposition()
    private var epoch = 0
    private var internalEdit = false
    private var accepted = true
    init {
        inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE or InputType.TYPE_TEXT_FLAG_CAP_SENTENCES
        imeOptions = EditorInfo.IME_FLAG_NO_EXTRACT_UI or EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING or EditorInfo.IME_ACTION_NONE
        setSingleLine(false); maxLines = 2; isLongClickable = false
        contentDescription = "Remote keyboard. Typing is sent immediately."
        addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
            override fun afterTextChanged(s: Editable?) {
                if (internalEdit) return
                accepted = try { mirror.update(s.toString(), onCommands) } catch (_: IllegalArgumentException) { false }
            }
        })
    }
    private fun localContext(value: String) {
        internalEdit = true
        try {
            setText(value); setSelection(text.length)
            mirror.forget(); mirror.update(value) { true }
        } finally { internalEdit = false }
    }
    fun endComposition() {
        epoch++
        // We cannot read the PC caret context after a click. Avoid assuming a new sentence.
        localContext("x ")
        if (hasFocus()) (context.getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager).restartInput(this)
    }
    private fun trimContext() {
        if (text.length <= 1024 || BaseInputConnection.getComposingSpanStart(text) >= 0) return
        var start = text.length - 512
        if (Character.isLowSurrogate(text[start])) start++
        localContext(text.substring(start))
    }
    override fun onCreateInputConnection(outAttrs: EditorInfo): InputConnection? {
        val base = super.onCreateInputConnection(outAttrs) ?: return null
        val connectionEpoch = epoch
        return object : InputConnectionWrapper(base, true) {
            private fun current() = connectionEpoch == epoch && isEnabled
            override fun setComposingText(text: CharSequence?, newCursorPosition: Int): Boolean {
                if (!current()) return false
                accepted = true
                return super.setComposingText(text,newCursorPosition) && accepted
            }
            override fun commitText(text: CharSequence?, newCursorPosition: Int): Boolean {
                if (!current()) return false
                accepted = true
                val result = super.commitText(text,newCursorPosition) && accepted
                if (result) trimContext()
                return result
            }
            @android.annotation.TargetApi(33)
            override fun commitText(text: CharSequence, newCursorPosition: Int, textAttribute: TextAttribute?): Boolean = commitText(text,newCursorPosition)
            @android.annotation.TargetApi(33)
            override fun setComposingText(text: CharSequence, newCursorPosition: Int, textAttribute: TextAttribute?): Boolean = setComposingText(text,newCursorPosition)
            override fun finishComposingText(): Boolean {
                if (!current()) return false
                val result = super.finishComposingText()
                trimContext()
                return result
            }
            override fun deleteSurroundingText(beforeLength: Int, afterLength: Int): Boolean {
                if (!current() || beforeLength !in 0..32 || afterLength !in 0..32) return false
                if (this@RemoteKeyboardEditor.text.isEmpty()) {
                    repeat(beforeLength) { if (!onKey("BACKSPACE")) return false }
                    repeat(afterLength) { if (!onKey("DELETE")) return false }
                    return true
                }
                accepted = true
                return super.deleteSurroundingText(beforeLength,afterLength) && accepted
            }
            override fun deleteSurroundingTextInCodePoints(beforeLength: Int, afterLength: Int): Boolean {
                if (!current() || beforeLength !in 0..32 || afterLength !in 0..32) return false
                if (this@RemoteKeyboardEditor.text.isEmpty()) return deleteSurroundingText(beforeLength,afterLength)
                accepted = true
                return super.deleteSurroundingTextInCodePoints(beforeLength,afterLength) && accepted
            }
            override fun performEditorAction(actionCode: Int): Boolean = commitText("\n",1)
            override fun sendKeyEvent(event: KeyEvent): Boolean {
                if (!current()) return false
                if (event.keyCode == KeyEvent.KEYCODE_DEL && this@RemoteKeyboardEditor.text.isEmpty()) return event.action != KeyEvent.ACTION_DOWN || onKey("BACKSPACE")
                return super.sendKeyEvent(event)
            }
            @android.annotation.TargetApi(34)
            override fun replaceText(start: Int, end: Int, text: CharSequence, newCursorPosition: Int, textAttribute: TextAttribute?): Boolean {
                if (!current()) return false
                accepted = true
                return super.replaceText(start,end,text,newCursorPosition,textAttribute) && accepted
            }
        }
    }
}
