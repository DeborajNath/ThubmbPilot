package dev.localmouse

/** Pure gesture state machine; coordinates are density-independent pixels. */
class GestureEngine(private val emit: (InputCommand) -> Unit) {
    var sensitivity = 1.5
    var naturalScroll = true
    private enum class Mode { IDLE, ONE, DRAG, TWO, IGNORE }
    private var mode = Mode.IDLE
    private var x = 0.0; private var y = 0.0
    private var startX = 0.0; private var startY = 0.0
    private var startTime = 0L
    private var distance = 0.0
    private var lastTap = -10000L
    private var tapX = 0.0; private var tapY = 0.0
    private var twoMoved = false
    private var secondX = 0.0; private var secondY = 0.0
    private val motion = MotionAccumulator()
    private val wheel = MotionAccumulator()

    fun down(px: Double, py: Double, time: Long) {
        releaseDrag()
        motion.reset(); wheel.reset()
        x = px; y = py; startX = px; startY = py
        startTime = time; distance = 0.0
        val secondTap = time - lastTap in 0..300 && squared(px - tapX, py - tapY) <= 40 * 40
        lastTap = -10000
        mode = if (secondTap) Mode.DRAG else Mode.ONE
        if (secondTap) emit(InputCommand.Button(true))
    }
    fun moveOne(px: Double, py: Double) {
        if (mode != Mode.ONE && mode != Mode.DRAG) return
        distance = maxOf(distance, squared(px-startX, py-startY))
        motion.add((px-x)*sensitivity, (py-y)*sensitivity)
        x = px; y = py
        val move = motion.drain()
        if (move.dx != 0 || move.dy != 0) emit(move)
    }
    fun secondFinger(ax: Double, ay: Double, bx: Double, by: Double, time: Long) {
        if (mode != Mode.ONE || distance > 64 || time-startTime > 250) { cancel(); mode = Mode.IGNORE; return }
        lastTap = -10000
        mode = Mode.TWO
        x = (ax+bx)/2; y = (ay+by)/2
        startX = ax; startY = ay; secondX = bx; secondY = by
        twoMoved = false; motion.reset(); wheel.reset()
    }
    fun moveTwo(ax: Double, ay: Double, bx: Double, by: Double) {
        if (mode != Mode.TWO) return
        val cx = (ax+bx)/2; val cy = (ay+by)/2
        // Each finger's travel also cancels right-tap (including a pinch with fixed centroid).
        if (squared(ax-startX, ay-startY) > 64 || squared(bx-secondX, by-secondY) > 64) twoMoved = true
        if (twoMoved) {
            // 40 dp is one 120-unit wheel detent; horizontal and vertical are separate inputs.
            val direction = if (naturalScroll) 1.0 else -1.0
            wheel.add(-(cx-x)*3*direction, (cy-y)*3*direction)
            val delta = wheel.drain()
            if (delta.dx != 0 || delta.dy != 0) emit(InputCommand.Scroll(delta.dx, delta.dy))
        }
        x = cx; y = cy
    }
    fun fingerLift(time: Long) {
        if (mode == Mode.TWO && !twoMoved && time-startTime <= 250) emit(InputCommand.Click("right"))
        releaseDrag()
        mode = Mode.IGNORE; lastTap = -10000; motion.reset(); wheel.reset()
    }
    fun up(px: Double, py: Double, time: Long) {
        if (mode == Mode.ONE || mode == Mode.DRAG) moveOne(px, py)
        if (mode == Mode.ONE && distance <= 64 && time-startTime <= 250) {
            emit(InputCommand.Click("left")); lastTap = time; tapX = px; tapY = py
        }
        releaseDrag(); mode = Mode.IDLE; motion.reset(); wheel.reset()
    }
    fun cancel() { releaseDrag(); mode = Mode.IGNORE; lastTap = -10000; motion.reset(); wheel.reset() }
    private fun releaseDrag() { if (mode == Mode.DRAG) emit(InputCommand.Button(false)) }
    private fun squared(dx: Double, dy: Double) = dx*dx+dy*dy
}
