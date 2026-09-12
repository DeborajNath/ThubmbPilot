package dev.localmouse

import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket
import java.util.concurrent.atomic.AtomicBoolean

data class ConnectionState(val message: String, val connected: Boolean = false, val inputEnabled: Boolean = false, val notice: String? = null)

/** Socket I/O stays off the UI thread. Exactly one request is in flight at a time. */
class LanConnection(private val status: (ConnectionState) -> Unit) {
    private val stopped = AtomicBoolean(false)
    private val pending = InputQueue()
    @Volatile private var socket: Socket? = null
    private var worker: Thread? = null

    fun keyboard(values: List<InputCommand>): Boolean {
        if (values.isEmpty()) return true
        if (pending.offerBatch(values)) return true
        if (pending.isEnabled()) {
            pending.setEnabled(false)
            status(ConnectionState("Typing queue full; unsent input discarded. Reconnect and check the PC text."))
            try { socket?.close() } catch (_: IOException) { }
        }
        return false
    }
    fun command(value: InputCommand) {
        if (!pending.offer(value) && value is InputCommand.Button && pending.isEnabled()) {
            // On saturation abandon the session instead of risking an unmatched press.
            try { socket?.close() } catch (_: IOException) { }
        }
    }
    fun move(dx: Int, dy: Int) { if (dx != 0 || dy != 0) pending.offer(InputCommand.Move(dx, dy)) }
    fun click(button: String) { pending.offer(InputCommand.Click(button)) }

    fun start(initial: PairedPC, identity: PhoneIdentity, requestPairing: Boolean = false, invitation: String = "", save: (PairedPC) -> Unit) {
        check(worker == null)
        worker = Thread({
            var target=initial
            var pairing=requestPairing
            var delay = 1000L
            while (!stopped.get()) {
                try {
                    status(ConnectionState("Finding ${target.name}…"))
                    if(!pairing) {
                        val discovered=try { Discovery.scan().find { it.id==target.pin } } catch(_: Exception) { null }
                        if(discovered!=null) target=target.copy(host=discovered.host)
                    }
                    if(stopped.get()) return@Thread
                    val host=target.host
                    status(ConnectionState("Connecting to ${target.name}…"))
                    PinnedTls.socket(target.pin).use { peer ->
                        socket = peer
                        if (stopped.get()) return@Thread
                        peer.tcpNoDelay = true
                        peer.soTimeout = if(pairing) 65000 else 6000
                        peer.connect(InetSocketAddress(host, 45832), 4000)
                        peer.startHandshake()
                        val fingerprint=PinnedTls.fingerprint(peer.session.peerCertificates[0])
                        val input = BufferedInputStream(peer.getInputStream())
                        fun exchange(message: JSONObject): JSONObject {
                            peer.getOutputStream().write((message.toString() + "\n").toByteArray(Charsets.UTF_8))
                            return read(input)
                        }
                        val hello=JSONObject().put("type","hello").put("version",8).put("token",target.token).put("name",android.os.Build.MODEL.take(48))
                        if(pairing) {
                            status(ConnectionState(if(invitation.isEmpty()) "Waiting for approval on ${target.name}…" else "Pairing with ${target.name}…"))
                            hello.put("pair","request").put("invitation",invitation).put("publicKey",identity.publicKey).put("proof",identity.sign(fingerprint,invitation,target.token))
                        }
                        val welcome=exchange(hello)
                        if (welcome.optString("type") == "error") {
                            status(ConnectionState("Pairing declined, expired or revoked. Find the PC and send a new request."))
                            return@Thread
                        }
                        if (welcome.optString("type") != "welcome" || welcome.optInt("version") != 8)
                            throw IOException("Install the updated pairing apps on both devices")
                        if(welcome.optString("id")!=fingerprint) throw IOException("PC identity mismatch")
                        val pcName = welcome.optString("name", host)
                        val paired=target.copy(name=pcName,pin=fingerprint)
                        if(paired!=target || pairing) save(paired)
                        pairing=false
                        peer.soTimeout=6000
                        target=paired
                        var allowed = welcome.optBoolean("inputEnabled", false)
                        var latency = ""
                        fun publish() = status(ConnectionState(
                            if (allowed) "Connected · $pcName$latency" else "Connected · enable ‘Allow mouse & keyboard control’ on PC",
                            connected = true, inputEnabled = allowed))
                        pending.setEnabled(allowed)
                        publish()
                        delay = 1000L
                        var nextPing = 0L
                        while (!stopped.get()) {
                            val now = System.nanoTime() / 1_000_000
                            if (now >= nextPing) {
                                val start = System.nanoTime()
                                val pong = exchange(JSONObject().put("type", "ping"))
                                if (pong.optString("type") != "pong") throw IOException("Unexpected heartbeat response")
                                latency = " · ${(System.nanoTime() - start) / 1_000_000}ms"
                                allowed = pong.optBoolean("inputEnabled", false)
                                pending.setEnabled(allowed)
                                publish()
                                nextPing = System.nanoTime() / 1_000_000 + 2000
                            }
                            val command = pending.take((nextPing - System.nanoTime() / 1_000_000).coerceAtLeast(0)) ?: continue
                            if (stopped.get()) return@Thread
                            val frame = when (command) {
                                is InputCommand.Action -> JSONObject().put("type","action").put("action",command.action).put("confirmed",command.confirmed)
                                is InputCommand.Move -> JSONObject().put("type", "move").put("dx", command.dx).put("dy", command.dy)
                                is InputCommand.Click -> JSONObject().put("type", "click").put("button", command.button)
                                is InputCommand.Scroll -> JSONObject().put("type", "scroll").put("dx", command.dx).put("dy", command.dy)
                                is InputCommand.Button -> JSONObject().put("type", "button").put("down", command.down)
                                is InputCommand.Text -> JSONObject().put("type", "text").put("text", command.text)
                                is InputCommand.Key -> JSONObject().put("type", "key").put("key", command.key).put("modifiers", org.json.JSONArray(command.modifiers))
                            }
                            val ack = exchange(frame)
                            if (ack.optString("type") != "ack") throw IOException("Unexpected input response")
                            if (!ack.isNull("error")) status(ConnectionState("Connected · $pcName",true,allowed,ack.optString("error")))
                            val enabled = ack.optBoolean("inputEnabled", false)
                            if (enabled != allowed) {
                                allowed = enabled
                                pending.setEnabled(allowed)
                                publish()
                            }
                        }
                    }
                } catch (_: InterruptedException) {
                    return@Thread
                } catch (ex: Exception) {
                    if(pairing) { if(!stopped.get()) status(ConnectionState("Pairing failed or timed out. Send a new request.")); return@Thread }
                    if (!stopped.get()) status(ConnectionState("Disconnected · ${ex.message ?: "Network error"}. Retrying…"))
                } finally {
                    pending.setEnabled(false)
                    socket = null
                }
                if (!stopped.get()) {
                    try { Thread.sleep(delay) } catch (_: InterruptedException) { return@Thread }
                    delay = (delay * 2).coerceAtMost(8000)
                }
            }
        }, "local-mouse-lan").also { it.start() }
    }

    fun stop() {
        stopped.set(true)
        pending.setEnabled(false)
        try { socket?.close() } catch (_: IOException) { }
        worker?.interrupt()
    }

    private fun read(input: BufferedInputStream): JSONObject {
        val bytes = ByteArrayOutputStream()
        while (bytes.size() < 4096) {
            val value = input.read()
            if (value < 0) throw IOException("PC closed the connection")
            if (value == 10) return JSONObject(bytes.toString("UTF-8"))
            bytes.write(value)
        }
        throw IOException("Server message too large")
    }
}