plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}
android {
    namespace = "dev.localmouse"
    compileSdk = 35
    defaultConfig {
        applicationId = "dev.localmouse"
        minSdk = 26
        targetSdk = 35
        versionCode = 12
        versionName = "1.0.1"
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
}
