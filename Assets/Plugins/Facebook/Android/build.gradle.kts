// Facebook plugin — Android Gradle LIBRARY modulu (docs/plugins.md). aotcompiler generated/plugins/Facebook'a kopyalar,
// settings include + app implementation(project) eder. Bagimlilik/manifest/proguard burada: AGP cozer, R8 consumer-rules alir.
// Granuler artefakt: yalniz facebook-login (tum facebook-android-sdk degil) -> trim'in yarisi secimde.
plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.digitoy.plugins.facebook"
    compileSdk = 35
    defaultConfig { minSdk = 26; consumerProguardFiles("consumer-rules.pro") }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    kotlinOptions { jvmTarget = "17" }
    sourceSets["main"].java.srcDirs("src/main/kotlin")
}

dependencies {
    implementation(project(":dehost")) // DEPlugin/PluginLib (generated/dehost; androidx.fragment api ile gelir)
    implementation("com.facebook.android:facebook-login:17.0.0")
}
