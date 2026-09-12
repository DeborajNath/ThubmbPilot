package dev.localmouse
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.SocketTimeoutException
import org.json.JSONObject
class PhoneDiscovery(private val identity:PhoneIdentity,private val invite:(DiscoveredPC,String)->Unit) : AutoCloseable {
    private val socket=DatagramSocket(45834).apply { soTimeout=500 }
    @Volatile private var stopped=false
    private val seen=linkedSetOf<String>()
    init { Thread({
        while(!stopped) {
            try {
                val packet=DatagramPacket(ByteArray(2048),2048);socket.receive(packet)
                val text=String(packet.data,0,packet.length,Charsets.UTF_8)
                if(text.startsWith("LOCALMOUSEPHONE8:")) {
                    val nonce=text.removePrefix("LOCALMOUSEPHONE8:")
                    if(!nonce.matches(Regex("[0-9A-Fa-f]{32}"))) continue
                    val reply=JSONObject().put("type","localmouse-phone").put("nonce",nonce).put("name",android.os.Build.MODEL.take(48)).put("publicKey",identity.publicKey).toString().toByteArray(Charsets.UTF_8)
                    socket.send(DatagramPacket(reply,reply.size,packet.address,packet.port))
                } else {
                    val obj=JSONObject(text);val id=obj.optString("id");val nonce=obj.optString("invitation")
                    if(obj.optString("type")!="localmouse-invite" || obj.optInt("version")!=8 || !id.matches(Regex("[0-9A-F]{64}")) || !nonce.matches(Regex("[0-9A-F]{32}"))) continue
                    if(!seen.add(nonce)) continue
                    if(seen.size>32) seen.remove(seen.first())
                    invite(DiscoveredPC(obj.optString("name").take(48),packet.address.hostAddress ?: continue,id),nonce)
                }
            } catch(_:SocketTimeoutException) {} catch(_:Exception) {if(stopped) break}
        }
    },"phone-discovery").start() }
    override fun close() { stopped=true;socket.close() }
}
