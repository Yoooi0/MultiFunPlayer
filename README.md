<div align="center">
    <h1>MultiFunPlayer</h1>
    <br/>
    <img src="Assets/screenshot.png"/>
</div>

<br/>

# About

MultiFunPlayer is a simple app to synchronize your devices (e.g. [OSR](https://www.patreon.com/tempestvr) or [buttplug.io](https://buttplug.io) supported devices) with any video using funscripts. Supported video players are [DeoVR](https://deovr.com/), [MPV](https://mpv.io/) and [Whirligig](http://whirligig.xyz/).
The player's main feature is the ability to play multiple funscripts at the same time, allowing for greater movement fidelity.

# How To

To synchronize with videos, start your desired video player and wait for automatic connection or click on the connect button to connect manually *(NOTE: DeoVR and Whirligig require you to enable remote support in their settings)*. Once connected, the funscripts can be loaded in several ways:

* Manually, by dragging a funscript file from windows explorer and dropping it on the desired axis `File` text box.
* Manually, by using the `Load script` button in the axis settings toolbar.
* Automatically, based on the currently played video file name if the funscripts are named correctly:

| Axis | Description | File Name |
|-|-|-|
| L0 | Up/Down | `<video name>.funscript` <br/> `<video name>.stroke.funscript` |
| L1 | Forward/Backward | `<video name>.sway.funscript` |
| L2 | Left/Right | `<video name>.surge.funscript` |
| R0 | Twist | `<video name>.twist.funscript` |
| R1 | Roll | `<video name>.roll.funscript` |
| R2 | Pitch | `<video name>.pitch.funscript` |
| V0 | Vibrate | `<video name>.vib.funscript` |
| V1 | Pump | `<video name>.pump.funscript` |
| L3 | Suction | `<video name>.valve.funscript` |

The above file names are matched in all script libraries and in the currently playing video directory.

# Requirements

* [.NET 5.0 Runtime](https://dotnet.microsoft.com/download/dotnet/current/runtime)