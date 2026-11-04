# Testing FreeVelo

## Automated tests

| Part | Command | Runs in CI |
|---|---|---|
| C# libraries (`src/`, `tests/`) | `dotnet test FreeVelo.slnx` | ✅ |
| Android BLE library + test app (Kotlin unit tests) | `cd android && ./gradlew :ble:testDebugUnitTest :testapp:testDebugUnitTest` | ✅ |

The protocol parsers exist twice (C# for the Unity app, minimal Kotlin for the test app);
both are tested with the **same byte vectors**.

## Getting the test app (APK) on your phone

Every push builds the Android test app in GitHub Actions. Because the repository is public,
anyone signed in to GitHub could download build artifacts, so the APK is uploaded **only inside a
password-protected ZIP (AES-256)**.

### One-time setup

1. Open the repository on GitHub → **Settings → Secrets and variables → Actions → New repository secret**.
2. Name: `APK_ZIP_PASSWORD`, value: a long password of your choice (store it in your password manager).
3. On your phone, install a ZIP app that supports AES encryption, e.g. **ZArchiver** (Play Store).
4. Allow your file app / ZArchiver to install apps: Android asks automatically on the first install
   ("Install unknown apps").

### Installing a build

1. On your phone, open <https://github.com/StritzingerS/FreeVelo/actions> (signed in to GitHub, "Desktop site" in the browser if needed).
2. Open the latest green **CI** run → section **Artifacts** → download `FreeVelo-BleTest-<number>`.
3. GitHub wraps the artifact in a ZIP: open it with ZArchiver, then open the inner `FreeVelo-BleTest-<number>.zip`
   and enter your password.
4. Tap the extracted `.apk` → **Install**. Later builds install as updates (same signing key).

Artifacts are kept for 30 days.

## Using the BLE test app

1. Start the app and allow **Nearby devices** (Bluetooth) permission.
2. **Scan** → tap your trainer (shown with `[FTMS]`), heart rate strap (`[HR]`) or power meter (`[Power]`).
   Wait until the state shows `READY`. Several devices can be connected at the same time.
3. Live values appear at the top (power, cadence, speed, heart rate).
4. **ERG test:** choose a target with the ±buttons → **Set ERG power**. The trainer should hold the wattage
   regardless of cadence. The log shows `← SetTargetPower: Success`.
5. **SIM test:** choose a grade → **Set grade (SIM)**. Resistance should change like on a climb.
6. Tap a connected device again to disconnect.

### What to report

Please check and report:

- [ ] Trainer is found and reaches `READY`
- [ ] Power / cadence / speed values look plausible
- [ ] Heart rate strap works
- [ ] ERG: trainer holds the target (try 100 W, 200 W, cadence 70 vs. 95 rpm)
- [ ] SIM: grade changes resistance (try 0 %, 5 %, 10 %, −3 %)
- [ ] Reconnect after switching the trainer off and on (or walking away with the phone)
- [ ] Log lines starting with `⚠` or `✗`

Then tap **Share byte log** and send the CSV file (e.g. attach it to a GitHub issue).
Byte logs from real devices become regression tests (`FreeVelo.Devices.Logging.BleLogEntry`).
