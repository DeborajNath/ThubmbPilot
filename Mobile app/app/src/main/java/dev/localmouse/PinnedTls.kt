package dev.localmouse

import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.net.ssl.*

object PinnedTls {
    fun fingerprint(certificate: java.security.cert.Certificate): String = MessageDigest.getInstance("SHA-256").digest(certificate.encoded).joinToString("") { "%02X".format(it.toInt() and 255) }
    fun socket(pin: String): SSLSocket {
        require(pin.matches(Regex("[0-9A-F]{64}")))
        val trust=object: X509TrustManager {
            override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
            override fun checkClientTrusted(chain: Array<X509Certificate>,authType: String) { throw CertificateException("Client certificates unsupported") }
            override fun checkServerTrusted(chain: Array<X509Certificate>,authType: String) {
                if(chain.isEmpty() || fingerprint(chain[0])!=pin) throw CertificateException("PC identity does not match. Choose your saved PC; its identity must match.")
                chain[0].checkValidity()
            }
        }
        val context=SSLContext.getInstance("TLS").apply { init(null,arrayOf(trust),null) }
        return (context.socketFactory.createSocket() as SSLSocket).apply { enabledProtocols=supportedProtocols.filter { it=="TLSv1.2" || it=="TLSv1.3" }.toTypedArray() }
    }
}
