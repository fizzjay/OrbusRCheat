# FlatOrbus (experimental desktop controls for OrbusVR)

A MelonLoader **IL2CPP** plugin prototype that adds mouse/keyboard controls and a small top-right toggle. It uses the repository's `Assembly-CSharp.dll` as a reference for Orbus' IL2CPP type names (`Il2CppMagicalActual.PlayerCharacter`, `Grabber`, and `InventoryManager`); all game-component calls are discovered at runtime so small game updates are less likely to break plugin loading.

## Current controls

- **Top-right button** or **F8** — enable/disable desktop mode.
- **WASD** — move the local player in the direction the view faces; hold **Shift** to move faster.
- **Right mouse button** — capture/release the mouse for mouselook. **Esc** releases it.
- **B** or **middle mouse** — open/close Orbus' backpack via `InventoryManager.Open/Close`.
- **H** or the overlay button — switch WASD from player locomotion to experimental hand positioning.
- The overlay **− / +** controls adjust walk speed or hand speed, depending on the selected mode.
- **Tab** — choose the next discovered local hand; **WASD** moves that hand and **R/F** moves it up/down while hand mode is active.
- **F1** — show/hide the controls panel.
- With the cursor free, left mouse clicks are forwarded through the active Unity `EventSystem`, allowing standard Unity Canvas menus (including many spawn/menu screens) to be clicked.

## Build

1. Install MelonLoader for the 64-bit OrbusVR game and launch it once so `MelonLoader/Il2CppAssemblies` is generated.
2. Install the .NET 6 SDK.
3. Build from this directory, overriding the paths if Orbus is installed elsewhere:

   ```powershell
   dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\OrbusVR"
   ```

4. Copy `bin/Release/FlatOrbus.dll` to the game's `Mods` directory.
5. Start the game and click the top-right button (or press F8).

The project references the game-local MelonLoader and Unity interop assemblies; it does not redistribute them. The checked-in `Assembly-CSharp.dll` is a type metadata/dummy assembly, not the game's runtime binaries.

## Important limitations

This is an experimental input layer, **not a complete replacement for a tracked VR headset and two controllers**. Mouse UI routing works for Unity EventSystem-based UI; Orbus' PowerUI or laser-pointer-only screens can still require game-specific event hooks. The optional hand mode moves discovered local Grabber transforms, but a VR tracking update can overwrite those transforms, and it does not synthesize SteamVR/Oculus controller button, grip, physics, gestures, or network state. Actions that depend on those systems (weapon handling, grabbing, crafting gestures, climbing, etc.) are not guaranteed to work. XR shutdown is attempted when enabling desktop mode but depends on the Orbus/Unity build; use F8 to return to the game's default VR mode if rendering or input behaves incorrectly.

Use only where the game and its multiplayer rules permit client-side mods. This plugin does not alter server authority or bypass game checks.
