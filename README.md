<div align="center">
    <h1>MultiFunPlayer</h1>
    <img src="Assets/screenshot.png"/>
</div>
<br/>

# About

MultiFunPlayer is a simple app to synchronize your TCode device (e.g. [OSR](https://www.patreon.com/tempestvr)) with any video using funscripts. Supported video players are [DeoVR](https://deovr.com/), [MPV](https://mpv.io/) and [Whirligig](http://whirligig.xyz/).
The player's main feature is the ability to play multiple funscripts at the same time, allowing for greater movement fidelity.

<br/>

# How To

To synchronize with the video player, click on the button with the player you want to use to start the connection *(note: DeoVR and Whirligig require you to enable remote support)*. Once connected, the funscripts can be loaded in several ways:

* By dragging a funscript file from windows explorer and dropping it on one of the axis buttons. The file will be attached to the axis you drop it on.
* By using the open file dialog in axis options.
* Automatically based on the currently played video file name if the files are named correctly:

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

<br/>

# Requirements

* [.NET 5.0 Runtime](https://dotnet.microsoft.com/download/dotnet/current/runtime)