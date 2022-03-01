<div align="center">
    <h1>MultiFunPlayer</h1>
    <br/>
    <img src="Assets/screenshot.png"/>
</div>

<br/>

# About

MultiFunPlayer is a simple app to synchronize your devices (e.g. [OSR](https://www.patreon.com/tempestvr) or [buttplug.io](https://buttplug.io) supported devices) with any video using funscripts. Supported video players are [DeoVR](https://deovr.com/), [MPV](https://mpv.io/), [MPC-HC/BE](https://github.com/clsid2/mpc-hc), [HereSphere](https://store.steampowered.com/app/1234730/HereSphere/) and [Whirligig](http://whirligig.xyz/).
The player's main feature is the ability to play multiple funscripts at the same time, allowing for greater movement fidelity.

# Features

* Supports [DeoVR](https://deovr.com/), [MPV](https://mpv.io/), [MPC-HC/BE](https://github.com/clsid2/mpc-hc), [HereSphere](https://store.steampowered.com/app/1234730/HereSphere/) and [Whirligig](http://whirligig.xyz/) video players
* Supports [buttplug.io](https://buttplug.io), network TCP/UDP, namedpipes and serial output
* Supports TCode v0.2 and TCode v0.3 devices
* Auto detection and connection to any supported video player and output
* Bind keyboard/mouse/gamepad input to almost any customizable action (150+ available actions)
* Seek and play/pause video from MultiFunPlayer
* Supports multiple concurrent outputs
* Real time script smoothing using pchip or makima interpolation
* Per axis speed limit
* Auto-home feature which when video is paused moves axis to its default value after some delay
* Supports local, DLNA, web and unc video paths
* Soft start sync feature to prevent unwanted motion
* Script libraries to organize funscripts in different folders and load funscripts not located next to the video file
* Ability to link unscripted axes to scripted axes
* Ability to generate motion for unscripted axes with motion providers
* Smart limit on R1 (roll) and R2 (pitch) axes to limit values based on L0 (stroke) height
* Multi funscript heatmap with stroke length visualization
* True portable app, no files are created/edited outside of the executable folder

# How To

To synchronize with videos, start your desired video player and wait for automatic connection or click on the connect button to connect manually *(NOTE: DeoVR, Whirligig and HereSphere require you to enable remote support in their settings)*. Once connected, the funscripts can be loaded in several ways:

* Manually, by dragging a funscript file from windows explorer and dropping it on the desired axis `File` text box.
* Manually, by using the `Load script` button in the axis settings toolbar.
* Automatically, based on the currently played video file name if the funscripts are named correctly:


<details>
<summary>Common</summary>

| Axis | Description | Valid file names |
|-|-|-|
| L0 | Up/Down | **`<video name>.funscript`** <br/> <sub>`<video name>.stroke.funscript`</sub> <br/> <sub>`<video name>.L0.funscript`</sub> |
| L1 | Forward/Backward | **`<video name>.sway.funscript`** <br/> <sub>`<video name>.L1.funscript`</sub> |
| L2 | Left/Right | **`<video name>.surge.funscript`** <br/> <sub>`<video name>.L2.funscript`</sub> |
| R0 | Twist | **`<video name>.twist.funscript`** <br/> <sub>`<video name>.R0.funscript`</sub> |
| R1 | Roll | **`<video name>.roll.funscript`** <br/> <sub>`<video name>.R1.funscript`</sub> |
| R2 | Pitch | **`<video name>.pitch.funscript`** <br/> <sub>`<video name>.R2.funscript`</sub> |

</details>

<details>
<summary>TCode v0.2</summary>

| Axis | Description | Valid file names |
|-|-|-|
| V0 | Vibrate | **`<video name>.vib.funscript`** <br/> <sub>`<video name>.V0.funscript`</sub> |
| V1 | Pump | **`<video name>.lube.funscript`** <br/> <sub>`<video name>.pump.funscript`</sub> <br/> <sub>`<video name>.V1.funscript`</sub> |
| L3 | Suction | **`<video name>.suck.funscript`** <br/> <sub>`<video name>.valve.funscript`</sub> <br/> <sub>`<video name>.L3.funscript`</sub> |

</details>

<details>
<summary>TCode v0.3</summary>

| Axis | Description | Valid file names |
|-|-|-|
| V0 | Vibrate | **`<video name>.vib.funscript`** <br/> <sub>`<video name>.V0.funscript`</sub> |
| A0 | Valve | **`<video name>.valve.funscript`** <br/> <sub>`<video name>.A0.funscript`</sub> |
| A1 | Suction | **`<video name>.suck.funscript`** <br/> <sub>`<video name>.A1.funscript`</sub> |

</details>
</br>

> Names in **bold** are commonly used used and are preferred 

> The above file names are matched in all script libraries and in the currently playing video directory.

# Prerequisites

* [.NET 6.0 x64 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/thank-you/runtime-desktop-6.0.0-windows-x64-installer)
* [Visual C++ 2019 x64 Redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe)