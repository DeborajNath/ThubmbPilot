package dev.localmouse

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.drawable.GradientDrawable
import android.view.MotionEvent
import android.view.View

class TouchpadView(context: Context) : View(context) {
    var onMove: (Int, Int) -> Unit = { _, _ -> }
    var onGestureStart: () -> Unit = {}
    var onTap: () -> Unit = {}
    var onCommand: (InputCommand) -> Unit = {}
    private val engine = GestureEngine { command ->
        when (command) {
            is InputCommand.Move -> onMove(command.dx, command.dy)
            is InputCommand.Click -> if (command.button == "left") onTap() else onCommand(command)
            else -> onCommand(command)
        }
    }
    var sensitivity: Double
        get() = engine.sensitivity
        set(value) { engine.sensitivity = value }
    var naturalScroll: Boolean
        get() = engine.naturalScroll
        set(value) { engine.naturalScroll = value }
    private val density = resources.displayMetrics.density
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { textAlign = Paint.Align.CENTER }
    var controlEnabled = false
        set(value) { if (field != value) reset(); field = value; invalidate() }
    init {
        isClickable = true; isFocusable = false
        background = GradientDrawable().apply {
            setColor(Forest.surface); cornerRadius = 24*density
            setStroke(density.toInt().coerceAtLeast(1), Forest.line)
        }
        contentDescription = "Touchpad. Slide to move, tap to click, two fingers to scroll or right tap. Tap then touch and hold to drag."
    }
    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        paint.color=Color.argb(26,150,170,156)
        var x=20*density
        while(x<width-20*density) {
            var y=20*density
            while(y<height-20*density) { canvas.drawCircle(x,y,0.7f*density,paint); y+=20*density }
            x+=20*density
        }
        val center=height/2f
        paint.color=if(controlEnabled) Forest.accent else Forest.muted
        paint.style=Paint.Style.STROKE; paint.strokeWidth=1.5f*density
        if(height>160*density) canvas.drawRoundRect(width/2f-12*density,center-57*density,width/2f+12*density,center-29*density,7*density,7*density,paint)
        paint.style=Paint.Style.FILL; paint.textSize=15*density
        canvas.drawText(if(controlEnabled) "Make your move" else "Touchpad paused",width/2f,center,paint)
        paint.color=Forest.muted; paint.textSize=11*density
        canvas.drawText(if(controlEnabled) "Slide to move · tap to click" else "Connect and allow control on your PC",width/2f,center+23*density,paint)
        if(controlEnabled && height>200*density) {
            paint.textSize=10*density
            canvas.drawText("Two fingers to scroll · tap then hold to drag",width/2f,height-22*density,paint)
        }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        if (!controlEnabled) return true
        fun x(i: Int) = event.getX(i).toDouble()/density
        fun y(i: Int) = event.getY(i).toDouble()/density
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> { onGestureStart(); parent?.requestDisallowInterceptTouchEvent(true); engine.down(x(0),y(0),event.eventTime) }
            MotionEvent.ACTION_POINTER_DOWN -> if (event.pointerCount == 2) engine.secondFinger(x(0),y(0),x(1),y(1),event.eventTime) else engine.cancel()
            MotionEvent.ACTION_MOVE -> {
                for (i in 0 until event.historySize) {
                    if (event.pointerCount == 1) engine.moveOne(event.getHistoricalX(0,i).toDouble()/density,event.getHistoricalY(0,i).toDouble()/density)
                    else if (event.pointerCount == 2) engine.moveTwo(event.getHistoricalX(0,i).toDouble()/density,event.getHistoricalY(0,i).toDouble()/density,event.getHistoricalX(1,i).toDouble()/density,event.getHistoricalY(1,i).toDouble()/density)
                }
                if (event.pointerCount == 1) engine.moveOne(x(0),y(0))
                else if (event.pointerCount == 2) engine.moveTwo(x(0),y(0),x(1),y(1))
            }
            MotionEvent.ACTION_POINTER_UP -> {
                if (event.pointerCount == 2) engine.moveTwo(x(0),y(0),x(1),y(1))
                engine.fingerLift(event.eventTime)
            }
            MotionEvent.ACTION_UP -> { engine.up(x(0),y(0),event.eventTime); parent?.requestDisallowInterceptTouchEvent(false) }
            MotionEvent.ACTION_CANCEL -> reset()
        }
        return true
    }
    fun reset() { engine.cancel(); parent?.requestDisallowInterceptTouchEvent(false) }
    override fun performClick(): Boolean { super.performClick(); if (controlEnabled) onTap(); return true }
    override fun onDetachedFromWindow() { reset(); super.onDetachedFromWindow() }
}
