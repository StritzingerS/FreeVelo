pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "freevelo-android"

// Bluetooth LE library (later also used as Unity plugin)
include(":ble")
// Native Android test app for trainers and sensors (phase 0)
include(":testapp")
