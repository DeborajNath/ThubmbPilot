package dev.localmouse

import org.json.JSONObject
import java.net.*
import java.util.UUID

data class DiscoveredPC(val name: String,val host: String,val id: String)
object Discovery {
    fun scan(duration: Long=1500): List<DiscoveredPC> {
        val found=linkedMapOf<String,DiscoveredPC>()
        val nonce=UUID.randomUUID().toString().replace("-","")
        DatagramSocket().use { socket ->
            socket.broadcast=true; socket.soTimeout=200
            val data=("LOCALMOUSE8:"+nonce).toByteArray(Charsets.US_ASCII)
            val targets=linkedSetOf(InetAddress.getByName("255.255.255.255"))
            try { NetworkInterface.getNetworkInterfaces().toList().filter { it.isUp && !it.isLoopback }.forEach { network -> network.interfaceAddresses.mapNotNull { it.broadcast }.forEach { targets.add(it) } } } catch(_: Exception) { }
            targets.forEach { try { socket.send(DatagramPacket(data,data.size,it,45833)) } catch(_: Exception) {} }
            val end=System.nanoTime()/1_000_000+duration
            while(System.nanoTime()/1_000_000<end && !Thread.currentThread().isInterrupted) {
                try {
                    val packet=DatagramPacket(ByteArray(1024),1024);socket.receive(packet)
                    val obj=JSONObject(String(packet.data,0,packet.length,Charsets.UTF_8))
                    val id=obj.optString("id")
                    if(obj.optString("type")=="localmouse" && obj.optInt("version")==8 && obj.optString("nonce")==nonce && id.matches(Regex("[0-9A-F]{64}")))
                        found[id]=DiscoveredPC(obj.optString("name").take(48),packet.address.hostAddress ?: continue,id)
                } catch(_: SocketTimeoutException) {} catch(_: Exception) {}
            }
        }
        return found.values.toList()
    }
}
