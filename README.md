<img width="423" height="820" alt="Ekran görüntüsü 2026-11-25 223701" src="https://github.com/user-attachments/assets/241b036c-c6e7-486a-b2ee-18460d1b3339" />
<img width="155" height="42" alt="Ekran görüntüsü 2026-11-25 223634" src="https://github.com/user-attachments/assets/00c1ba45-b7c7-4cc4-8864-925835a0e0fb" />
# WirelessBattery – Wireless Headphone and Mouse Battery Indicator

---

## WHAT IT DOES

- Adds a small indicator to the Windows taskbar, immediately to the left of the clock/system tray (^) area:

  `[Headphone icon] 85%   [Mouse icon] 92%` (a lightning bolt appears next to the percentage while charging).

  If the battery level of a device cannot be retrieved, `--` is displayed instead of a percentage. The indicator turns red when the battery level drops to 15% or below.

  **NO ALERTS OR NOTIFICATIONS.**

- Clicking the indicator opens a detailed information panel that updates in real time:

  - Device model name, connection type (Bluetooth / 2.4 GHz receiver / USB), status, and charging state.
  - **Headphones:** Audio output and microphone formats, including sample rate (kHz), bit depth, and channel count, reflecting the format currently used by Windows.
  - **Audio Devices** card: Displays ALL active audio devices on the computer, along with their sample rates, regardless of which device is identified as the headphones.
  - **Mouse:** Real-time polling rate (Hz), measured as the mouse moves (e.g., approximately 1000 Hz or 8000 Hz).
  - Bluetooth address, VID:PID, and the source from which the data is retrieved.

- The indicator brightens on hover and darkens while pressed, providing visual click feedback.
- The panel opens with a bottom-to-top animation.
- If the panel exceeds the available screen space, it can be scrolled using the mouse wheel or the Arrow, Page Up, Page Down, Home, and End keys. A slim scrollbar is displayed on the right.

- **KEYBOARD:** Displayed as a separate card with a keyboard icon inside the panel. The taskbar indicator itself displays only mouse and headphone battery levels.

  The keyboard card provides:

  - Device model
  - VID:PID
  - Connection type
  - Key rollover capability (estimated 6KRO/NKRO)
  - Media key support
  - Mouse functionality
  - Manufacturer software (RGB/ macro configuration software)
  - **Real-time polling rate test:** Press several keys rapidly in succession. The polling rate is calculated from the shortest measured interval between consecutive key events. The faster the keys are pressed, the closer the measurement gets to the actual polling rate.

  **PRIVACY:** The keys you press are NOT identified or recorded. Only event timestamps are measured, and the test runs exclusively while the panel is open.

  Keyboard battery information is available only for Bluetooth devices and supported 2.4 GHz receivers.

- The button in the upper-right corner of the panel switches between display themes:

  **Automatic (follows Windows settings) > Light > Dark**

- Right-click context menu:

  Show Details / Refresh Now / Start with Windows / Show Tray Icon / Indicator Text Size (Small–Medium–Large) / Panel Theme / Generate Diagnostic Report / Uninstall Program / Exit

---

## NEW FEATURES (v1.1)

### POLLING RATE

- Added a **USB Reported Polling Rate** / **Polling Rate (USB)** entry for both mice and keyboards.

  This displays the actual polling rate reported by the device over USB, retrieved directly from the USB hub. The value is available instantly and does not require any physical movement or user interaction.

  This information is not available for Bluetooth devices.

- The mouse also has a separate real-time polling rate measurement.

  It now runs in a dedicated, high-priority worker thread, independently of the user interface, providing faster measurements and reducing the likelihood of missed input events.

  Move the mouse rapidly in circular motions. The **Maximum Measured Rate** entry identifies the corresponding standard polling rate tier (500 / 1000 / 2000 / 4000 / 8000 Hz).

### COLLAPSIBLE CARDS

- Cards in the panel can now be expanded and collapsed by clicking their headers (Mouse / Keyboard / Audio Devices).
- The transitions are animated, and each card's expanded/collapsed state is remembered.

### LANGUAGE SUPPORT

- Added a **Language** button at the bottom of the panel (also accessible via the right-click menu).
- Supported languages:

  Turkish, English, German, French, Spanish, Italian, Portuguese, Dutch, Polish, and Russian.
- On first launch, the application automatically uses the Windows display language.

---

## NO INSTALLATION REQUIRED – NO DOWNLOADS NEEDED

The application uses the C# compiler (`csc.exe`) already included with Windows.

---

## HOW TO RUN

**1.** Right-click the ZIP archive and select **Extract All**. Do NOT run the application directly from inside the ZIP archive.

The extracted folder should contain:

- `build.bat`
- `kaldir.bat`
- `README.txt`
- A `src` folder containing 11 `.cs` source files.

**2.** Double-click `build.bat`.

Within a few seconds, `WirelessBattery.exe` will be generated and launched automatically.

- If a red error message appears, copy the contents of the console window and send them to me so I can fix the issue.

**3.** On its first launch, the application asks once whether it should start automatically when the computer boots.

To change this setting later, use:

Right-click > **Start with Windows**

Note: Automatic startup will stop working if the application folder is moved or deleted. The `kaldir.bat` script or the **Uninstall Program** option removes the startup entry.

---

## UNINSTALLATION (REMOVING THE APPLICATION)

There are two ways to uninstall WirelessBattery:

- **Method 1:** Right-click the indicator > **Uninstall Program...** > Yes.
- **Method 2:** Double-click `kaldir.bat` in the application folder and press `E` when prompted.

Both methods:

- Close the application.
- Remove the Windows startup entry and application settings.
- Delete the application files.

No other system files or settings are modified. The application folder itself is removed only if it becomes empty.

---

## SCOPE AND LIMITATIONS

### Battery Data Sources

- **Bluetooth devices:** Battery information is retrieved through Windows' native battery reporting mechanism (the information displayed under Settings > Bluetooth & devices).

  If Windows does not report the battery level, WirelessBattery cannot display it either.

- **Mice with 2.4 GHz receivers:** The following brands and device families are supported:

  - Attack Shark / Ajazz (Rongyuan)
  - WLMouse
  - LAMZU / Arbiter (373E and 37B0 families)
  - Razer mice

  Other brands, including Logitech and SteelSeries, are not supported in this version.

- Battery queries are **read-only**. No configuration or settings are written to the connected devices.
- The application does NOT connect to the internet.

### Mouse Polling Rate Measurement

The measurement counts raw mouse input events received by Windows. It cannot produce a measurement while the mouse is stationary.

### Headphone Sample Rate

The displayed sample rate represents the audio format Windows is currently using for that device.

For Bluetooth headphones, the sample rate typically drops to 16 kHz while the microphone is in use. When the microphone is not in use, it is generally 44.1 or 48 kHz.

---

## IF THE INDICATOR DISAPPEARS AFTER EXITING A GAME

When a fullscreen game changes the display mode, Windows may clear the contents of transparent windows.

The indicator detects display configuration changes and redraws itself within a maximum of 3 seconds.

---

## IF THE INDICATOR KEEPS APPEARING AND DISAPPEARING

To prevent the indicator from being hidden behind the taskbar or other application windows, it is periodically brought to the foreground every 150 ms. It is also brought to the foreground whenever Windows reports a change in the active foreground window.

An application is considered fullscreen only when its foreground window covers the entire display and Windows identifies it as a fullscreen application. To prevent false detections, two consecutive checks must confirm this condition.

If the issue persists:

Right-click > **Generate Diagnostic Report**

Send me the lines beginning with **Indicator** at the top of the generated report.

---

## RESOURCE USAGE (ESTIMATED)

- The indicator makes a few Windows API calls per second and redraws itself only when something changes.
- Bluetooth and audio device scanning runs every 30 seconds.
- Manufacturer-specific (receiver) battery queries run every 60 seconds.
- Devices are refreshed immediately when connected or disconnected, or when the computer resumes from sleep.
- While the panel is open, the refresh intervals are reduced to 2 seconds / 10 seconds.
- Mouse polling rate measurement runs only while the panel is open.
- No internet access is used.

You can verify the application's resource consumption on your own computer by locating `WirelessBattery.exe` in Windows Task Manager.

---

## TROUBLESHOOTING

### A device is not detected / its battery level cannot be retrieved

Right-click the indicator > **Generate Diagnostic Report**.

Send me the text file that opens in Notepad.

Alternatively, run the following command from the command line:

`WirelessBattery.exe --rapor`

### The indicator is not visible on the taskbar

Right-click the indicator > **Generate Diagnostic Report**.

The beginning of the report explains why the indicator is hidden and where it is positioned (e.g., a fullscreen application or a hidden taskbar).

### Windows SmartScreen / Microsoft Defender warning

Since the application is compiled locally on your computer, Windows may identify it as an unsigned executable.
