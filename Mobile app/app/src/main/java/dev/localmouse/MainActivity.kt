package dev.localmouse

import android.app.Activity
import android.os.Bundle
import android.os.Build
import android.view.WindowInsets
import android.graphics.Color
import android.graphics.Typeface
import android.text.InputType
import android.view.View
import android.view.inputmethod.InputMethodManager
import android.widget.*

class MainActivity : Activity() {
    private lateinit var status: TextView
    private val navButtons = mutableListOf<Button>()
    private fun selectNav(title: String) {
        navButtons.forEach { b ->
            val selected=b.text.toString()==title
            Forest.button(b,selected); b.isSelected=selected
            b.setCompoundDrawablesWithIntrinsicBounds(null,Forest.Icon(b.text.toString(),if(selected) Forest.background else Forest.muted,resources.displayMetrics.density),null,null)
        }
    }
    private lateinit var touchpad: TouchpadView
    private lateinit var leftButton: Button
    private lateinit var rightButton: Button
    private lateinit var connectionPanel: ScrollView
    private lateinit var mousePanel: LinearLayout
    private lateinit var shortcutsPanel: ScrollView
    private val shortcutButtons = mutableListOf<Button>()
    private var powerDialog: android.app.AlertDialog? = null
    private lateinit var mediaPanel: LinearLayout
    private val mediaButtons = mutableListOf<Button>()
    private lateinit var keyboard: KeyboardPanel
    private var connection: LanConnection? = null
    @Volatile private var generation = 0
    private var manuallyDisconnected = false
    private var foreground = false
    private val phoneIdentity by lazy { PhoneIdentity() }
    private var phoneDiscovery: PhoneDiscovery? = null
    private var pairDialog: android.app.AlertDialog? = null
    private val pairings by lazy { PairingStore(this) }
    private val preferences by lazy { getSharedPreferences("connection", MODE_PRIVATE) }
    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Forest.background)
        }
        root.setOnApplyWindowInsetsListener { view, insets ->
            @Suppress("DEPRECATION")
            val padding = if (Build.VERSION.SDK_INT >= 30) {
                val edges = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout() or WindowInsets.Type.ime())
                intArrayOf(edges.left,edges.top,edges.right,edges.bottom)
            } else intArrayOf(insets.systemWindowInsetLeft,insets.systemWindowInsetTop,insets.systemWindowInsetRight,insets.systemWindowInsetBottom)
            view.setPadding(dp(16)+padding[0],dp(8)+padding[1],dp(16)+padding[2],dp(8)+padding[3]); insets
        }
        fun label(text: String, size: Float) = TextView(this).apply {
            this.text = text; textSize = size; setTextColor(Forest.text); setPadding(0,dp(4),0,dp(4))
        }
        val header=LinearLayout(this).apply { gravity=android.view.Gravity.CENTER_VERTICAL }
        header.addView(label("ThumbPilot.",24f).apply { setTypeface(null,Typeface.BOLD) },LinearLayout.LayoutParams(0,-2,1f))
        header.addView(Button(this).apply { text="●  Connection"; textSize=11f; Forest.button(this); isAllCaps=false; isFocusable=false; setOnClickListener { toggleConnection() } },LinearLayout.LayoutParams(dp(112),dp(48)))
        root.addView(header)
        status = label("Disconnected",12f).apply { setTextColor(Forest.muted); setPadding(0,dp(4),0,dp(12)); accessibilityLiveRegion=View.ACCESSIBILITY_LIVE_REGION_POLITE }; root.addView(status)
        val content = FrameLayout(this)
        root.addView(content,LinearLayout.LayoutParams(-1,0,1f))
        mousePanel = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        content.addView(mousePanel,FrameLayout.LayoutParams(-1,-1))
        touchpad = TouchpadView(this).apply {
            onMove = { dx,dy -> connection?.move(dx,dy) }; onTap = { connection?.click("left") }
            onCommand = { connection?.command(it) }
            onGestureStart = { keyboard.pointerBoundary() }
            naturalScroll = preferences.getBoolean("naturalScroll",true)
            sensitivity = preferences.getFloat("sensitivity",1.5f).coerceIn(0.5f,3f).toDouble()
        }
        mousePanel.addView(touchpad,LinearLayout.LayoutParams(-1,0,1f).apply { topMargin=dp(8); bottomMargin=dp(6) })
        val clicks = LinearLayout(this)
        leftButton = Button(this).apply { text="Left click"; textSize=12f; Forest.button(this); isEnabled=false; isFocusable=false; setOnClickListener { keyboard.pointerBoundary(); connection?.click("left") } }
        rightButton = Button(this).apply { text="Right click"; textSize=12f; Forest.button(this); isEnabled=false; isFocusable=false; setOnClickListener { keyboard.pointerBoundary(); connection?.click("right") } }
        clicks.addView(leftButton,LinearLayout.LayoutParams(0,dp(48),1f).apply { marginEnd=dp(3) }); clicks.addView(rightButton,LinearLayout.LayoutParams(0,dp(48),1f).apply { marginStart=dp(3) })
        clicks.setPadding(0,0,0,dp(8))
        mousePanel.addView(clicks)
        val sensitivity = label("",12f)
        fun sensitivityLabel() { sensitivity.text="Sensitivity · ${String.format(java.util.Locale.ROOT,"%.1f",touchpad.sensitivity)}×" }
        sensitivityLabel()
        val sensitivityControls = LinearLayout(this).apply { orientation=LinearLayout.VERTICAL; addView(sensitivity) }
        sensitivityControls.addView(SeekBar(this).apply {
            max=25; progress=((touchpad.sensitivity-0.5)*10).toInt(); contentDescription="Pointer sensitivity"
            setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(bar: SeekBar?,progress:Int,fromUser:Boolean) {
                    touchpad.sensitivity=0.5+progress/10.0; sensitivityLabel()
                    if(fromUser) preferences.edit().putFloat("sensitivity",touchpad.sensitivity.toFloat()).apply()
                }
                override fun onStartTrackingTouch(bar:SeekBar?) { touchpad.reset() }
                override fun onStopTrackingTouch(bar:SeekBar?) {}
            })
        },LinearLayout.LayoutParams(-1,dp(36)))
        keyboard=KeyboardPanel(this).apply { send={ connection?.keyboard(it) ?: false }; beforeShortcut={ touchpad.reset() } }
        mousePanel.addView(keyboard)
        mediaPanel = LinearLayout(this).apply { orientation=LinearLayout.VERTICAL; visibility=View.GONE }
        for (items in listOf(
            listOf("Previous" to "MEDIA_PREVIOUS", "Play / Pause" to "MEDIA_PLAY_PAUSE", "Next" to "MEDIA_NEXT"),
            listOf("Volume −" to "VOLUME_DOWN", "Mute" to "VOLUME_MUTE", "Volume +" to "VOLUME_UP"))) {
            val row = LinearLayout(this)
            for ((label,key) in items) {
                val button = Button(this).apply {
                    text=label; textSize=11f; Forest.button(this); isAllCaps=false; isFocusable=false; isEnabled=false
                    contentDescription=label
                    setOnClickListener {
                        touchpad.reset()
                        if (connection?.keyboard(listOf(InputCommand.Key(key,emptyList()))) != true)
                            Toast.makeText(this@MainActivity,"Control was not sent. Check the connection.",Toast.LENGTH_SHORT).show()
                    }
                }
                mediaButtons.add(button)
                row.addView(button,LinearLayout.LayoutParams(0,dp(48),1f).apply { setMargins(dp(2),dp(3),dp(2),dp(3)) })
            }
            mediaPanel.addView(row)
        }
        mousePanel.addView(mediaPanel)
        val shortcuts = LinearLayout(this).apply { orientation=LinearLayout.VERTICAL }
        val entries = listOf(
            "Switch windows" to InputCommand.Key("TAB",listOf("ALT")), "Task View" to InputCommand.Key("TAB",listOf("WIN")),
            "Show desktop" to InputCommand.Key("D",listOf("WIN")), "Start menu" to InputCommand.Key("WIN",emptyList()),
            "Brave" to InputCommand.Action("brave"), "Calculator" to InputCommand.Action("calculator"),
            "Notepad" to InputCommand.Action("notepad"), "File Explorer" to InputCommand.Action("explorer"),
            "Lock PC" to InputCommand.Action("lock"), "Sleep" to InputCommand.Action("sleep",true),
            "Restart" to InputCommand.Action("restart",true), "Shutdown" to InputCommand.Action("shutdown",true))
        for (items in entries.chunked(2)) {
            val row = LinearLayout(this)
            for ((title,command) in items) {
                val button = Button(this).apply {
                    text=title; textSize=12f; Forest.button(this); isAllCaps=false; isFocusable=false; isEnabled=false
                    setOnClickListener {
                        touchpad.reset(); keyboard.pointerBoundary()
                        fun send() { if (connection?.keyboard(listOf(command)) != true) Toast.makeText(this@MainActivity,"Action not sent. Check the connection.",Toast.LENGTH_SHORT).show() }
                        if (command is InputCommand.Action && command.confirmed) {
                            val current = generation
                            powerDialog?.dismiss()
                            powerDialog = android.app.AlertDialog.Builder(this@MainActivity)
                                .setTitle("$title PC?").setMessage("Save your work first. This will disconnect remote control.")
                                .setNegativeButton("Cancel",null).setPositiveButton(title) { _,_ -> if (generation==current && touchpad.controlEnabled) send() }.show()
                        } else send()
                    }
                }
                shortcutButtons.add(button); row.addView(button,LinearLayout.LayoutParams(0,dp(48),1f).apply { setMargins(dp(2),dp(3),dp(2),dp(3)) })
            }
            shortcuts.addView(row)
        }
        shortcutsPanel = ScrollView(this).apply { addView(shortcuts); visibility=View.GONE }
        mousePanel.addView(shortcutsPanel,LinearLayout.LayoutParams(-1,dp(200)))
        val fields=LinearLayout(this).apply { orientation=LinearLayout.VERTICAL; background=Forest.shape(resources.displayMetrics.density); setPadding(dp(12),dp(12),dp(12),dp(12)) }
        fields.addView(label("Your connection",18f).apply { setTypeface(null,Typeface.BOLD) })
        val discoveryRow=LinearLayout(this)
        discoveryRow.addView(Button(this).apply { text="Find PCs"; Forest.button(this,true); isAllCaps=false; setOnClickListener { discoverPCs() } },LinearLayout.LayoutParams(0,dp(48),1f))
        discoveryRow.addView(Button(this).apply { text="Saved PCs"; Forest.button(this); isAllCaps=false; setOnClickListener { savedPCs() } },LinearLayout.LayoutParams(0,dp(48),1f))
        fields.addView(discoveryRow)
        fields.addView(label("Keep both apps open on the same Wi-Fi. Select a PC to pair.",12f)); fields.addView(sensitivityControls)
        fields.addView(CheckBox(this).apply {
            text="Natural scrolling"; isChecked=touchpad.naturalScroll
            setOnCheckedChangeListener { _,checked -> touchpad.naturalScroll=checked; preferences.edit().putBoolean("naturalScroll",checked).apply() }
        })
        val actions=LinearLayout(this)
        actions.addView(Button(this).apply { text="Disconnect"; Forest.button(this); isAllCaps=false; setOnClickListener { manuallyDisconnected=true; disconnect() } },LinearLayout.LayoutParams(0,-2,1f))
        fields.addView(actions)
        connectionPanel=ScrollView(this).apply { addView(fields) }
        root.addView(connectionPanel,LinearLayout.LayoutParams(-1,dp(270)))
        val navigation=LinearLayout(this).apply { setPadding(0,dp(10),0,0) }
        fun nav(title:String,action:()->Unit) {
            val button=Button(this).apply { text=title; textSize=10f; isAllCaps=false; isFocusable=false; compoundDrawablePadding=dp(5); contentDescription=title; setOnClickListener { selectNav(title); action() } }
            navButtons.add(button)
            navigation.addView(button,LinearLayout.LayoutParams(0,dp(60),1f).apply { setMargins(dp(2),0,dp(2),0) })
        }
        nav("Touchpad") {
            shortcutsPanel.visibility=View.GONE; keyboard.hideIme(); mediaPanel.visibility=View.GONE; connectionPanel.visibility=View.GONE
        }
        nav("Keyboard") {
            if (!touchpad.controlEnabled) { selectNav("Touchpad"); Toast.makeText(this,"Connect to your PC to use the keyboard.",Toast.LENGTH_LONG).show(); return@nav }
            shortcutsPanel.visibility=View.GONE; touchpad.reset(); connectionPanel.visibility=View.GONE; mediaPanel.visibility=View.GONE; keyboard.open()
        }
        nav("Media") {
            shortcutsPanel.visibility=View.GONE; touchpad.reset(); keyboard.hideIme(); connectionPanel.visibility=View.GONE
            mediaPanel.visibility=View.VISIBLE
        }
        nav("Shortcuts") {
            touchpad.reset(); keyboard.hideIme(); mediaPanel.visibility=View.GONE; connectionPanel.visibility=View.GONE
            shortcutsPanel.visibility=View.VISIBLE
        }
        root.addView(navigation)
        selectNav("Touchpad")
        root.addView(label("DEVELOPED BY DEBORAJ",9f).apply { gravity=android.view.Gravity.CENTER; letterSpacing=0.12f; setTextColor(Forest.muted); setPadding(0,dp(12),0,dp(3)) })
        setContentView(root); root.requestApplyInsets()
    }
    private fun toggleConnection() {
        selectNav("Touchpad")
        shortcutsPanel.visibility=View.GONE; keyboard.hideIme(); mediaPanel.visibility=View.GONE
        connectionPanel.visibility=if(connectionPanel.visibility==View.VISIBLE) View.GONE else View.VISIBLE
    }
    private fun saved(): List<PairedPC> = try { pairings.all() } catch(ex: Exception) {
        Toast.makeText(this,"Could not read saved pairings: ${ex.message}",Toast.LENGTH_LONG).show(); emptyList()
    }
    private fun savedPCs() {
        val pcs=saved()
        if(pcs.isEmpty()) { Toast.makeText(this,"No saved PCs. Tap Find PCs to pair.",Toast.LENGTH_SHORT).show(); return }
        android.app.AlertDialog.Builder(this).setTitle("Saved PCs").setItems(pcs.map { it.name }.toTypedArray()) { _,which ->
            val pc=pcs[which]
            android.app.AlertDialog.Builder(this).setTitle(pc.name).setItems(arrayOf("Connect","Forget on this phone")) { _,action ->
                if(action==0) connectTo(pc) else {
                    manuallyDisconnected=true; disconnect()
                    try { pairings.forget(pc.pin); Toast.makeText(this,"Forgot PC. To revoke this phone, use Paired phones on the PC.",Toast.LENGTH_LONG).show() }
                    catch(ex: Exception) { Toast.makeText(this,"Could not forget pairing.",Toast.LENGTH_LONG).show() }
                }
            }.show()
        }.show()
    }
    private fun discoverPCs() {
        status.text="Finding PCs on this network…"
        Thread({
            val pcs=try { Discovery.scan(2000) } catch(_: Exception) { emptyList() }
            runOnUiThread {
                if(!foreground || isDestroyed) return@runOnUiThread
                if(pcs.isEmpty()) { Toast.makeText(this,"No PCs found. Open the updated companion on the same Wi-Fi and allow it on your Private network.",Toast.LENGTH_LONG).show(); return@runOnUiThread }
                android.app.AlertDialog.Builder(this).setTitle("Nearby PCs").setItems(pcs.map { it.name }.toTypedArray()) { _,which ->
                    val pc=pcs[which]; val known=saved().find { it.pin==pc.id }
                    if(known!=null) connectTo(known.copy(host=pc.host)) else {
                        pairDialog?.dismiss()
                        pairDialog=android.app.AlertDialog.Builder(this).setTitle("Pair with ${pc.name}?")
                            .setMessage("A pairing request will appear on your PC.")
                            .setNegativeButton("Cancel",null).setPositiveButton("Pair") { _,_ -> requestPair(pc) }.show()
                    }
                }.show()
            }
        },"pc-discovery").start()
    }
    private fun requestPair(pc: DiscoveredPC, invitation: String="") {
        val known=saved().find { it.pin==pc.id }
        if(known!=null && invitation.isNotEmpty()) { connectTo(known.copy(host=pc.host)); return }
        val token=ByteArray(32).also { java.security.SecureRandom().nextBytes(it) }.joinToString("") { "%02X".format(it.toInt() and 255) }
        connectTo(PairedPC(pc.name,pc.host,pc.id,token),true,invitation)
    }
    private fun connectTo(pc: PairedPC, requestPairing: Boolean=false, invitation: String="") {
        disconnect(); manuallyDisconnected=false
        val current=generation
        var wasConnected=false
        connection=LanConnection { state -> runOnUiThread {
            if(generation==current && !isDestroyed) {
                state.notice?.let { Toast.makeText(this,it,Toast.LENGTH_LONG).show() }
                status.text=state.message; setInputEnabled(state.inputEnabled)
                if(state.connected && !wasConnected) { connectionPanel.visibility=View.GONE }
                wasConnected=state.connected
                if(!state.connected && (state.message.contains("declined",true) || state.message.contains("Pairing failed",true))) connectionPanel.visibility=View.VISIBLE
            }
        } }.also { it.start(pc,phoneIdentity,requestPairing,invitation) { paired -> pairings.save(paired) { generation==current } } }
        keyboard.hideIme(); connectionPanel.visibility=View.GONE
    }
    private fun setInputEnabled(value:Boolean) {
        if (!value) { powerDialog?.dismiss(); powerDialog=null }
        shortcutButtons.forEach { it.isEnabled=value }
        touchpad.controlEnabled=value; leftButton.isEnabled=value; rightButton.isEnabled=value; keyboard.setControlEnabled(value); mediaButtons.forEach { it.isEnabled=value }
    }
    private fun disconnect() {
        generation++; setInputEnabled(false); connection?.stop(); connection=null
        status.text="Disconnected"; connectionPanel.visibility=View.VISIBLE
    }
    override fun onResume() {
        super.onResume(); foreground=true
        try {
            phoneDiscovery=PhoneDiscovery(phoneIdentity) { pc,invitation -> runOnUiThread {
                if(!foreground || isDestroyed || pairDialog?.isShowing==true) return@runOnUiThread
                pairDialog=android.app.AlertDialog.Builder(this).setTitle("Pairing request")
                    .setMessage("${pc.name} wants to pair. Allow only a request you expect.")
                    .setNegativeButton("Decline",null).setPositiveButton("Allow") { _,_ -> requestPair(pc,invitation) }.show()
                val shown=pairDialog
                android.os.Handler(mainLooper).postDelayed({ if(shown?.isShowing==true) shown.dismiss() },55000)
            } }
        } catch(ex: Exception) { Toast.makeText(this,"Phone discovery unavailable: ${ex.message}",Toast.LENGTH_LONG).show() }
        if(connection==null && !manuallyDisconnected) {
            val last=try { pairings.last() } catch(_: Exception) { null }
            if(last!=null) connectTo(last)
        }
    }
    override fun onPause() { foreground=false; pairDialog?.dismiss(); pairDialog=null; phoneDiscovery?.close(); phoneDiscovery=null; disconnect(); super.onPause() }
}
