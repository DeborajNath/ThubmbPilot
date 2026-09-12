package dev.localmouse
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.Signature
import java.security.spec.ECGenParameterSpec
class PhoneIdentity {
    private val store=KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
    init {
        if(!store.containsAlias("localmouse-phone")) KeyPairGenerator.getInstance("EC","AndroidKeyStore").apply {
            initialize(KeyGenParameterSpec.Builder("localmouse-phone",KeyProperties.PURPOSE_SIGN or KeyProperties.PURPOSE_VERIFY)
                .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1")).setDigests(KeyProperties.DIGEST_SHA256).build())
        }.generateKeyPair()
    }
    val publicKey: String get()=Base64.encodeToString(store.getCertificate("localmouse-phone").publicKey.encoded,Base64.NO_WRAP)
    fun sign(fingerprint:String,invitation:String,token:String):String {
        val signature=Signature.getInstance("SHA256withECDSA")
        signature.initSign(store.getKey("localmouse-phone",null) as java.security.PrivateKey)
        signature.update(("LocalMouse8\n"+fingerprint+"\n"+invitation+"\n"+token).toByteArray(Charsets.UTF_8))
        return Base64.encodeToString(signature.sign(),Base64.NO_WRAP)
    }
}
