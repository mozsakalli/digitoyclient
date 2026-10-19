// AppLovin MAX plugin — Android Gradle LIBRARY modulu (docs/plugins.md). aotcompiler generated/plugins/AppLovin'a kopyalar,
// settings include + app implementation(project) eder. SDK/adapter bagimliliklari BURADA DEGIL: Editor/AppLovinHooks.cs
// oyunun Config'indeki surum/adapter listesini app seviyesine yazar (p.Dependency) — bu modul yalniz kopru kodunu tasir
// ve derleme icin SDK'ya compileOnly bagimlidir (surum app'in cozdugu ile ayni major olmali).
plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.digitoy.plugins.applovin"
    compileSdk = 35
    defaultConfig { minSdk = 26; consumerProguardFiles("consumer-rules.pro") }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    kotlinOptions { jvmTarget = "17" }
    sourceSets["main"].java.srcDirs("src/main/kotlin")
}

dependencies {
    implementation(project(":dehost")) // DEPlugin/PluginLib (generated/dehost)
    compileOnly("com.applovin:applovin-sdk:13.6.4") // gercek surum app'te (hook: Config.sdkVersionAndroid)
}
