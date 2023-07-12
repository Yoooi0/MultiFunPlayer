<div align="center">
    <h1>MultiFunPlayer</h1>
    <br/>
    <img src="Assets/screenshot.png"/>
</div>

<br/>

# About

MultiFunPlayer is a simple app to synchronize your devices (e.g. [OSR](https://www.patreon.com/tempestvr) or [buttplug.io](https://buttplug.io) supported devices) with any video using funscripts. Supported video players are [DeoVR](https://deovr.com/), [MPV](https://mpv.io/), [MPC-HC/BE](https://github.com/clsid2/mpc-hc), [HereSphere](https://store.steampowered.com/app/1234730/HereSphere/) and [Whirligig](http://whirligig.xyz/).
The player's main feature is the ability to play multiple funscripts at the same time, allowing for greater movement fidelity.

# Patreon only features

* Support for DeoVR SLR Interactive script streaming (requires SLR subscription)

# Main features

* Supports **[DeoVR](https://deovr.com/), [MPV](https://mpv.io/), [MPC-HC/BE](https://github.com/clsid2/mpc-hc), [HereSphere](https://store.steampowered.com/app/1234730/HereSphere/), [OpenFunscripter](https://github.com/OpenFunscripter/OFS), [Whirligig](http://whirligig.xyz/), [Plex](https://plex.tv), [Emby](https://emby.media/) and [Jellyfin](https://jellyfin.org/)** video players
* Internal player to **play scripts without video files** 
* Supports **[buttplug.io](https://buttplug.io), TCP, UDP, websockets, namedpipes, serial, file and The Handy (experimental)** outputs
* **C# plugin system** for custom behaviours and integrations
* Supports **multiple concurrent outputs** of the same type
* Supports **TCode v0.2 and TCode v0.3** devices with advanced customization
* Auto detection and connection to any supported video player and output
* Bind **keyboard/mouse/gamepad input** to almost any customizable action (150+ available actions)
* Seek, open and play/pause video from MultiFunPlayer
* Real time **script smoothing** using pchip or makima interpolation
* Per axis **speed limit**
* Configurable **auto-home** when axis is idle for specified time
* **Smart limit** to limit axis output range or speed based on position of another axis with fully customizable curve
* **Soft start sync** feature to prevent unwanted motion
* **Script libraries** to organize funscripts in different folders and load funscripts not located next to the video file
* Ability to **link unscripted axes** to scripted axes
* Ability to **generate additional motion** or **fill script gaps** using random, script, pattern or custom curve motion providers
* Customizable **color theme**
* Multi **funscript heatmap** with stroke length visualization
* Supports funscript **bookmarks and chapters**
* True portable app, no files are created/edited outside of the executable folder

# How To

To synchronize with videos:

* Add desired video player via the top-right "plus" button
* Configure if needed by expanding settings with the "arrow" button on the right side
* Click connect *(NOTE: DeoVR, Whirligig and HereSphere require you to enable remote server/control support in their settings)*
* Add desired output via the bottom-right "plus" button
* Configure by expanding settings with the "arrow" button on the right side
* Click connect

Once your video player and output are connected, the funscripts can be loaded in several ways:

* Manually, by dragging a funscript file from windows explorer and dropping it on the desired axis `File` text box.
* Manually, by using the `Script->Load` menu in the axis settings toolbar.
* Automatically, based on the currently played video file name if the funscripts are named correctly:


<details>
<summary>Common</summary>

| Axis | Description | Valid file names |
|-|-|-|
| L0 | Up/Down | **`<video name>.funscript`** |
| L1 | Forward/Backward | **`<video name>.surge.funscript`**  |
| L2 | Left/Right | **`<video name>.sway.funscript`** |
| R0 | Twist | **`<video name>.twist.funscript`** |
| R1 | Roll | **`<video name>.roll.funscript`** |
| R2 | Pitch | **`<video name>.pitch.funscript`** |

</details>

<details>
<summary>TCode v0.2</summary>

| Axis | Description | Valid file names |
|-|-|-|
| V0 | Vibrate | **`<video name>.vib.funscript`** |
| V1 | Pump | **`<video name>.lube.funscript`** |
| L3 | Suction | **`<video name>.suck.funscript`** |

</details>

<details>
<summary>TCode v0.3</summary>

| Axis | Description | Valid file names |
|-|-|-|
| V0 | Vibrate | **`<video name>.vib.funscript`** |
| A0 | Valve | **`<video name>.valve.funscript`** |
| A1 | Suction | **`<video name>.suck.funscript`** |
| A2 | Lube | **`<video name>.lube.funscript`** |

</details>
</br>

> The above file names are standard and recommended, other supported funscript names can be seen and configured in "Device" settings.

> The above file names are matched in all script libraries and in the currently playing video directory.

# Prerequisites

* [.NET 7.0 x64 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/7.0/runtime)
* [Visual C++ 2019 x64 Redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe)