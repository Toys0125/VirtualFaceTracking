# Virtual Face Tracking

Virtual Face Tracking is a VRCFaceTracking module plus a companion Windows GUI for manually controlling and simulating face tracking output.

The module loads inside VRCFaceTracking, starts or reconnects to the GUI, and exchanges state over a named pipe. The GUI exposes eyes, brows, face, simulation, along with diagnostics and persistent session state.

## Features

- Eye, brow, jaw, and mouth control
- Simulation mode for smooth idle motion
- Persistent state and runtime diagnostics
- Automatic GUI launch from the module when the companion app is not already running

## Requirements

- Windows 10 or Windows 11
- .NET 10 SDK
- VRCFaceTracking `5.4.4.1`
- The bundled VRCFaceTracking SDK binaries in `vendor/vrcft/5.4.4.1`

## Repository Layout

- `VirtualFaceTracking.Module` - VRCFaceTracking module entry point and deployment target
- `VirtualFaceTracking.Gui` - Windows Forms companion app
- `VirtualFaceTracking.Shared` - shared state, mapping, persistence, IPC, and simulation code
- `VirtualFaceTracking.Tests` - xUnit tests for the shared logic
- `docs/integration-smoke-test.md` - manual verification checklist for a real VRCFT install

## Build

Run the test suite first:

```powershell
dotnet test VirtualFaceTracking.slnx
```

Build the module in the desired configuration:

```powershell
dotnet build VirtualFaceTracking.Module -c Release
```

The module project copies the companion GUI into:

```text
%APPDATA%\VRCFaceTracking\CustomLibs\VirtualFaceTracking\
```

and places the module entry DLL at:

```text
%APPDATA%\VRCFaceTracking\CustomLibs\VirtualFaceTracking.Module.dll
```

## Run

1. Build `VirtualFaceTracking.Module`.
2. Start VRCFaceTracking.
3. Enable the `Virtual Face Tracking` module in the VRCFT module list.
4. Use the GUI to adjust manual controls, simulation, and overrides.

The GUI writes diagnostics to `virtual-tracker.log` in the deployment directory and persists UI/runtime state to `virtual-tracker.state.json`.

## Runtime Files

When deployed, the module expects these files under `%APPDATA%\VRCFaceTracking\CustomLibs\VirtualFaceTracking\`:

- `VirtualFaceTracking.Gui.exe`
- `VirtualFaceTracking.Shared.dll`
- `virtual-tracker.defaults.json`
- `virtual-tracker.state.json` after first run
- `virtual-tracker.log`

## Notes

- Right-click any sliders to reset them.
- The companion GUI can be launched by the module or started directly.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
