# EyeTracker Module
Vive Focus Vision 3 headset eyetracking data export for Unity projects.

This module is meant to be used in Unity to export eyetracking data from an experience to be used by the companion [Gaze Analysis App](https://github.com/ARTSLab-CITH/eyetracking-analysis-app).

### Requirements
* **VIVE OpenXR Plugin Version**: `2.5.1`
* **OpenXR Version**: `1.12.1` (required by VIVE, must manually change to this older version in the Package Manager)

### Usage
* Place the `EyeTracking.prefab` in a scene to enable eyetracking functionality.
* Add the `RegionOfInterest.cs` script to any GameObjects with colliders to track when the user looks at them.

### Included Assets
* `EyeTracker.cs`
* `EyeTracking.prefab`
* `RegionOfInterest.cs`
* `Spectator Camera Debug.mat`
* `SystemRecorderWatcher.cs`
* `VIVEEnablePassthrough.cs`

### Notes
* Incompatible with VIVE Spectator Camera as the feature does not have functionality for recording passthrough.