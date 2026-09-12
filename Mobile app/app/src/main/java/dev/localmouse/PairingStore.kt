package dev.localmouse

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

data class PairedPC(val name: String,val host: String,val pin: String,val token: String)
class PairingStore(context: Context) {
    private val prefs=context.getSharedPreferences("secure-pairing",Context.MODE_PRIVATE)
    private fun key(): SecretKey {
        val store=KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        val existing=store.getKey("localmouse-pairings",null)
        if(existing!=null) return existing as SecretKey
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder("localmouse-pairings",KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build())
        }.generateKey()
    }
    @Synchronized fun all(): List<PairedPC> {
        val blob=prefs.getString("pcs",null) ?: return emptyList()
        val bytes=Base64.decode(blob,Base64.NO_WRAP)
        val cipher=Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE,key(),GCMParameterSpec(128,bytes.copyOfRange(0,12)))
        val array=JSONArray(String(cipher.doFinal(bytes.copyOfRange(12,bytes.size)),Charsets.UTF_8))
        return (0 until array.length()).map { val o=array.getJSONObject(it); PairedPC(o.getString("name"),o.getString("host"),o.getString("pin"),o.getString("token")) }
    }
    private fun write(pcs: List<PairedPC>) {
        val array=JSONArray(); pcs.forEach { array.put(JSONObject().put("name",it.name).put("host",it.host).put("pin",it.pin).put("token",it.token)) }
        val cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key())
        val blob=Base64.encodeToString(cipher.iv+cipher.doFinal(array.toString().toByteArray(Charsets.UTF_8)),Base64.NO_WRAP)
        check(prefs.edit().putString("pcs",blob).commit()) { "Could not save pairing" }
    }
    @Synchronized fun save(pc: PairedPC, allowed: () -> Boolean = { true }) { if(!allowed()) return; write(all().filter { it.pin!=pc.pin }+pc); prefs.edit().putString("last",pc.pin).apply() }
    @Synchronized fun forget(pin: String) { write(all().filter { it.pin!=pin }); if(prefs.getString("last",null)==pin) prefs.edit().remove("last").apply() }
    @Synchronized fun last(): PairedPC? = all().find { it.pin==prefs.getString("last",null) }
}
