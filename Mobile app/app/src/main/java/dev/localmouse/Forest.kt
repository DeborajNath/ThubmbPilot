package dev.localmouse

import android.content.res.ColorStateList
import android.graphics.*
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.RippleDrawable
import android.graphics.drawable.Drawable
import android.widget.Button

/** Shared Forest palette and local vector icons; no bitmap assets. */
object Forest {
    val background = Color.rgb(16,23,20)
    val surface = Color.rgb(28,39,33)
    val text = Color.rgb(229,238,231)
    val muted = Color.rgb(150,170,156)
    val accent = Color.rgb(188,233,155)
    val line = Color.rgb(52,68,58)
    fun shape(density: Float, color: Int=surface, radius: Float=14f) = GradientDrawable().apply {
        setColor(color); cornerRadius=radius*density; setStroke(density.toInt().coerceAtLeast(1),line)
    }
    fun button(button: Button, selected: Boolean=false) = button.apply {
        val d=resources.displayMetrics.density
        isAllCaps=false; minWidth=0; minimumWidth=0; minHeight=0; minimumHeight=0
        setPadding((6*d).toInt(),(4*d).toInt(),(6*d).toInt(),(4*d).toInt())
        setTextColor(ColorStateList(arrayOf(intArrayOf(-android.R.attr.state_enabled),intArrayOf()),intArrayOf(muted,if(selected) Forest.background else Forest.text)))
        backgroundTintList=null
        background=RippleDrawable(ColorStateList.valueOf(0x337FAD66),shape(d,if(selected) accent else surface),null)
    }
    class Icon(private val kind: String, private val tint: Int, private val density: Float): Drawable() {
        private val p=Paint(Paint.ANTI_ALIAS_FLAG).apply { color=tint; style=Paint.Style.STROKE; strokeWidth=1.6f; strokeCap=Paint.Cap.ROUND; strokeJoin=Paint.Join.ROUND }
        override fun getIntrinsicWidth()=(20*density).toInt()
        override fun getIntrinsicHeight()=(20*density).toInt()
        override fun draw(c: Canvas) {
            c.save(); c.translate(bounds.left.toFloat(),bounds.top.toFloat()); c.scale(bounds.width()/24f,bounds.height()/24f)
            fun line(x:Float,y:Float,a:Float,b:Float)=c.drawLine(x,y,a,b,p)
            when(kind) {
                "Touchpad" -> { c.drawRoundRect(3f,4f,21f,20f,3f,3f,p); line(3f,15f,21f,15f); line(12f,15f,12f,20f) }
                "Keyboard" -> { c.drawRoundRect(2f,5f,22f,19f,3f,3f,p); for(y in listOf(9f,12f)) for(x in listOf(6f,10f,14f,18f)) line(x,y,x+.2f,y); line(7f,16f,17f,16f) }
                "Media" -> { val path=Path().apply { moveTo(9f,5f); lineTo(20f,12f); lineTo(9f,19f); close() }; c.drawPath(path,p); line(4f,5f,4f,19f) }
                else -> for(x in listOf(3f,14f)) for(y in listOf(3f,14f)) c.drawRoundRect(x,y,x+7,y+7,2f,2f,p)
            }
            c.restore()
        }
        override fun setAlpha(alpha:Int) { p.alpha=alpha }
        override fun setColorFilter(filter:ColorFilter?) { p.colorFilter=filter }
        @Deprecated("Deprecated in Android") override fun getOpacity()=PixelFormat.TRANSLUCENT
    }
}

