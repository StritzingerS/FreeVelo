# Unity setup (Windows / macOS)

You need this once on your PC. Time: about 45–60 minutes (mostly downloads, ~15 GB).

## 1. Tools

| Tool | Why | Download |
|---|---|---|
| **Git** + **GitHub Desktop** | Get the code, commit the Unity project | <https://desktop.github.com> (includes Git) |
| **.NET 10 SDK** | Builds the FreeVelo C# libraries that Unity uses | <https://dotnet.microsoft.com/download> |
| **Unity Hub** | Installs and manages Unity versions | <https://unity.com/download> |
| **IDE** (one of them) | Editing C# | Visual Studio 2022 Community (installed by Unity Hub), **JetBrains Rider** (free for non-commercial use) or VS Code + C# Dev Kit |

## 2. Clone the repository

1. GitHub Desktop → **File → Clone repository** → `StritzingerS/FreeVelo` → choose a folder, e.g. `C:\dev\FreeVelo`.
2. Switch to the branch **`phase1-solo-training`** (Current branch dropdown at the top).

## 3. Install Unity 6

1. Start **Unity Hub**, sign in with a (free) Unity account and activate a **Personal** license
   (Hub → Preferences → Licenses → Add → *Get a free personal license*).
2. **Installs → Install Editor** → choose the newest **Unity 6 LTS** (version `6000.x`, marked *LTS*).
3. Select these **modules**:
   - ✅ **Android Build Support**, including ✅ **OpenJDK** and ✅ **Android SDK & NDK Tools**
   - ✅ **Microsoft Visual Studio Community** (only if you don't use Rider / VS Code)
   - optional: Windows Build Support (IL2CPP) – for the later Windows version
4. Install and wait.

## 4. Create the Unity project

1. Unity Hub → **Projects → New project**.
2. Editor version: the Unity 6 LTS you just installed.
3. Template: **Universal 3D** (URP). Download the template if asked.
4. Project name: **`FreeVelo`**
5. Location: the **`unity` folder inside your repository clone**, e.g. `C:\dev\FreeVelo\unity`
   → the project ends up in `C:\dev\FreeVelo\unity\FreeVelo`.
6. ❌ Uncheck *Connect to Unity Cloud* and *Use Unity Version Control* (we use Git).
7. **Create project** and wait until the editor is open.

## 5. Android settings

1. **File → Build Profiles** → select **Android** → **Switch Platform** (takes a few minutes).
2. **Edit → Project Settings → Player**, tab with the Android icon:
   - Company Name: `FreeVelo`, Product Name: `FreeVelo`
   - *Other Settings → Identification*:
     - Override Default Package Name: ✅ → `io.github.stritzingers.freevelo`
     - Minimum API Level: **Android 10.0 (API level 29)**
     - Target API Level: *Automatic (highest installed)*
   - *Other Settings → Configuration*:
     - Scripting Backend: **IL2CPP**
     - Target Architectures: ✅ **ARM64** only
3. **Edit → Preferences → External Tools** → choose your IDE.

## 6. Commit and push

1. Close Unity.
2. In GitHub Desktop you should see new files under `unity/FreeVelo/` – **only** `Assets/`, `Packages/` and
   `ProjectSettings/` (folders like `Library/`, `Temp/`, `Logs/`, `UserSettings/` are ignored by `.gitignore`).
   If you see thousands of files from `Library/`, stop and tell me.
3. Commit message: `Create Unity project`, then **Push origin**.

Tell me when it's pushed – I'll then add the FreeVelo scripts, the Bluetooth bridge and the first scene.

## 7. (Optional) Phone for direct testing

To run builds directly from Unity on your phone (faster than via GitHub):

1. Phone: **Settings → About phone** → tap **Build number** 7× → developer options are enabled.
2. **Settings → System → Developer options** → enable **USB debugging**.
3. Connect via USB, allow the PC on the phone.
4. Unity: **File → Build Profiles → Android → Run Device** → your phone → **Build and Run**.
