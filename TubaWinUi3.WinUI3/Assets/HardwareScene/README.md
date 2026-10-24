# Hardware scene

`scene.js` authors the laptop and desktop meshes, opening/boot animation, fan rotation,
OrbitControls interaction and projected component anchors. Component GLBs and their attribution are listed in models/README.md;
Three.js 0.186.1 is MIT licensed (see THREE-LICENSE.txt). All component assets load from the local package, only when a card is expanded.
The checked-in `scene.bundle.js` lets normal .NET builds run without Node.js.

To rebuild the bundle, install dependencies in a directory **outside Assets** (so they
are not copied into app output):

```powershell
npm install --prefix "$env:TEMP/tuba-hardware-scene-build" three@0.186.1 esbuild@0.28.2
node TubaWinUi3.WinUI3/Assets/HardwareScene/build.mjs "$env:TEMP/tuba-hardware-scene-build"
```

The native page constructs the renderer only for diagram mode. Classic mode never
creates a WebView2. Switching back disposes the control. Only explicit screenshot
sharing captures a PNG; interaction renders directly with WebGL.
