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

* Supports [DeoVR](https://deovr.com/), [MPV](https://mpv.io/), [MPC-HC/BE](https://github.com/clsid2/mpc-hc), [HereSphere](https://store.steampowered.com/app/1234730/HereSphere/) and [Whirligig](http://whirligig.xyz/) video players
* Internal player to play scripts without video files 
* Supports [buttplug.io](https://buttplug.io), network TCP/UDP, websockets, namedpipes, serial, file and The Handy outputs
* Supports multiple outputs of the same type working concurrently
* Supports TCode v0.2 and TCode v0.3 devices
* Allows customization of TCode axes via "Device" settings
* Auto detection and connection to any supported video player and output
* Bind keyboard/mouse/gamepad input to almost any customizable action (150+ available actions)
* Seek and play/pause video from MultiFunPlayer
* Supports multiple concurrent outputs
* Real time script smoothing using pchip or makima interpolation
* Per axis speed limit
* Auto-home when axis is idle for specified time
* Smart limit to limit axis output range or speed based on position of another axis with fully customizable curve
* Supports local, DLNA, web and unc video paths
* Soft start sync feature to prevent unwanted motion
* Script libraries to organize funscripts in different folders and load funscripts not located next to the video file
* Ability to link unscripted axes to scripted axes
* Ability to generate additional motion or fill script gaps using random, script or pattern motion providers
* Customizable color theme
* Multi funscript heatmap with stroke length visualization
* True portable app, no files are created/edited outside of the executable folder

# How To

To synchronize with videos, start your desired video player and wait for automatic connection or click on the connect button to connect manually *(NOTE: DeoVR, Whirligig and HereSphere require you to enable remote support in their settings)*. Once connected, the funscripts can be loaded in several ways:

* Manually, by dragging a funscript file from windows explorer and dropping it on the desired axis `File` text box.
* Manually, by using the `Script->Load` menu in the axis settings toolbar.
* Automatically, based on the currently played video file name if the funscripts are named correctly:


<details>
<summary>Common</summary>

| Axis | Description | Valid file names |
|-|-|-|
| L0 | Up/Down | **`<video name>.funscript`** |
| L1 | Forward/Backward | **`<video name>.sway.funscript`**  |
| L2 | Left/Right | **`<video name>.surge.funscript`** |
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

</details>
</br>

> The above file names are standard and recommended, other supported funscript names can be seen and configured in "Device" settings.

> The above file names are matched in all script libraries and in the currently playing video directory.

# Prerequisites

* [.NET 6.0 x64 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/6.0/runtime)
* [Visual C++ 2019 x64 Redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe)