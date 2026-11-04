# Testing FreeVelo

## Automated tests

| Part | Command | Runs in CI |
|---|---|---|
| C# libraries (`src/`, `tests/`) | `dotnet test FreeVelo.slnx` | ✅ |
| Android BLE library + test app (Kotlin unit tests) | `cd android && ./gradlew :ble:testDebugUnitTest :testapp:testDebugUnitTest` | ✅ |

The protocol parsers exist twice (C# for the Unity app, minimal Kotlin for the test app);
both are tested with the **same byte vectors**.

## Getting the test app (APK) on your phone

Every push builds the Android test app in GitHub Actions and publishes it as the pre-release
**"ble-test-latest"**. The download link always points to the newest build:

**<https://github.com/StritzingerS/FreeVelo/releases/download/ble-test-latest/FreeVelo-BleTest.apk>**

1. Open the link on your phone (bookmark it) – no GitHub login needed.
2. Open the downloaded `FreeVelo-BleTest.apk` → **Install**. The first time, Android asks you to allow
   installing apps from your browser.
3. Later builds install as updates (same signing key). The build number is shown on the
   [release page](https://github.com/StritzingerS/FreeVelo/releases/tag/ble-test-latest).

> The APK is public – anyone can download it. It is a debug build for testing only.
> Each CI run also keeps the APK as an artifact for 30 days.

## Using the BLE test app

1. Start the app and allow **Nearby devices** (Bluetooth) permission.
2. **Scan** → only fitness devices are listed (headphones etc. are hidden). Tap your trainer (shown with `[FTMS]`), heart rate strap (`[HR]`) or power meter (`[Power]`).
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

Then tap **Share byte log** (send it directly, e.g. by email or messenger) or **Save to Downloads**
(the file appears in `Downloads/FreeVelo/` in your file manager) and send the CSV file
(e.g. attach it to a GitHub issue).
Byte logs from real devices become regression tests (`FreeVelo.Devices.Logging.BleLogEntry`).
