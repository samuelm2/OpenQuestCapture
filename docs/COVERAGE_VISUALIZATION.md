# Coverage Visualization Setup Guide

This guide explains how to set up and use the real-time scan coverage visualization feature.

## 🎨 What It Does

The coverage visualizer shows **where you've scanned** and **from what angles** by emitting colored line particles at depth sample points. Each line:
- Starts at a surface point (from depth map)
- Points toward where the camera was when captured
- Color represents viewing direction:
  - 🔴 Red = North
  - 🟡 Yellow = East
  - 🟢 Green = South
  - 🔵 Cyan/Blue = West
  - 🌈 Rainbow = Viewed from all angles (excellent coverage!)

## 📦 Setup Instructions

### 1. Add Assembly Reference

Unity needs to link the Depth and Visualization assemblies:

1. Select `Assets/RealityLog/Scripts/Runtime/Depth/RealityLog.Depth.asmdef`
2. In Inspector, find **Assembly Definition References**
3. Click **+** to add a new reference
4. Select `RealityLog.Visualization`
5. Click **Apply**

### 2. Create Particle System

1. **In your scene hierarchy**, create a new GameObject:
   - Right-click → **Effects → Particle System**
   - Rename it: `CoverageVisualization`

2. **Add the script**:
   - Select the GameObject
   - **Add Component** → `Coverage Line Visualizer`

3. **Assign the Particle System**:
   - In the `Coverage Line Visualizer` component
   - Drag the Particle System component to the **Particle System** field

### 3. Link to DepthMapExporter

1. Find your `DepthMapExporter` GameObject in the scene
2. In Inspector, find the **Coverage Visualization** section
3. Drag the `CoverageVisualization` GameObject to the **Coverage Visualizer** field

### 4. Configure Settings (Optional)

In the `Coverage Line Visualizer` component:

| Setting | Default | Description |
|---------|---------|-------------|
| **Line Length** | 0.05m | How long each coverage line appears (5cm) |
| **Downsample Factor** | 8 | Sample every Nth pixel (8 = every 8th pixel)<br>Higher = better performance, fewer lines |
| **Line Lifetime** | 30s | How long lines stay visible |
| **Is Enabled** | ✓ | Toggle visualization on/off |
| **Color Saturation** | 0.9 | Vividness of direction colors (0-1) |
| **Color Brightness** | 1.0 | Brightness of lines (0-1) |

## 🎮 Usage

### During Recording

- Press **Record** to start scanning
- Coverage lines appear automatically as you capture frames
- **Watch for:**
  - 🌈 **Rainbow clusters** = Great coverage (many angles)
  - 🔴 **Single color patches** = Poor coverage (only one angle)
  - ⚫ **No lines** = Not scanned yet

### Tips for Good Coverage

1. **Move around objects** - capture from multiple angles
2. **Look for color variety** - if an area is all one color, move to a different position
3. **Fill gaps** - areas with no lines need scanning
4. **Avoid over-scanning** - if lines are too dense, you've scanned enough

### Controls

**Toggle Visualization:**
- Check/uncheck **Is Enabled** in Inspector (or via code)

**Clear Lines:**
- Call `coverageVisualizer.Clear()` from code
- Or stop/restart recording

## 🎨 Visual Examples

### Good Coverage (Rainbow)
```
    ↗ → ↘          (Multiple colors = seen from many angles)
  ↑       ↓
↖         ↙
  ←     ←
```

### Poor Coverage (Single Color)
```
→ → → →            (All same color = only seen from one side)
→ → → →
```

### Not Scanned
```
(empty space)      (No lines = area not captured yet)
```

## ⚙️ Performance

**Expected Performance:**
- **~3-5ms overhead** per captured frame (at 3 FPS capture)
- **50,000+ particles** rendered smoothly @ 72 FPS
- **Minimal memory** (~2-4 MB for particle system)

**If experiencing lag:**
1. Increase **Downsample Factor** (8 → 16)
2. Decrease **Line Lifetime** (30s → 15s)
3. Reduce **Max Particles** in Particle System (100k → 50k)

## 🔧 Troubleshooting

### Lines not appearing

1. **Check coverage visualizer is assigned** in DepthMapExporter Inspector
2. **Verify "Is Enabled" is checked**
3. **Check Particle System is playing** (should auto-start)
4. **Look in Scene view** (not just Game view)

### Lines are tiny/hard to see

- Increase **Line Length** (0.05 → 0.1)
- Increase **Color Brightness** (1.0)
- Check Particle System **Renderer → Length Scale** (should be ~1.0)

### Lines are everywhere/too dense

- Increase **Downsample Factor** (8 → 16 or 32)
- Decrease **Line Lifetime** (30s → 10s)
- Call `coverageVisualizer.Clear()` to reset

### Performance issues

- Increase **Downsample Factor** (fewer lines = better FPS)
- Reduce **Max Particles** in Particle System settings
- Disable visualization during recording, enable only for review

## 🚀 Advanced Usage

### Programmatic Control

```csharp
// Toggle visualization
coverageVisualizer.IsEnabled = false;

// Clear all lines
coverageVisualizer.Clear();

// Adjust settings at runtime
coverageVisualizer.lineLength = 0.1f;
coverageVisualizer.downsampleFactor = 16;
```

### Custom Colors

Edit `DirectionToColor()` in `CoverageLineVisualizer.cs`:

```csharp
// Example: Use elevation angle instead of horizontal angle
float hue = (viewDirection.y + 1f) * 0.5f; // Up = red, down = blue
return Color.HSVToRGB(hue, colorSaturation, colorBrightness);
```

### Save Coverage Data

The lines are just visual - to save coverage data:
1. Track particle positions/directions
2. Export to CSV or JSON
3. Visualize in post-processing tools

## 📊 Technical Details

**How It Works:**
1. Every captured frame at 3 FPS (every 333ms)
2. Depth map sampled at downsampled grid (every 8th pixel)
3. ~1,000 points per frame unprojected to 3D world space
4. Particle emitted at each point, oriented toward camera
5. Particle color determined by horizontal viewing angle (HSV hue)
6. Particles fade after 30 seconds

**Coordinate System:**
- Lines point FROM surface TO camera position
- World space positioning (moves with scene)
- Particle rotation uses `Quaternion.LookRotation(viewDirection)`

**Rendering:**
- Unity Particle System with **Stretched Billboard** mode
- GPU instanced rendering (efficient)
- Unlit material with vertex colors
- Transparent queue (renders after opaque geometry)

---

## 🎯 Summary

Coverage visualization helps you:
- ✅ See what areas have been scanned
- ✅ Identify gaps in coverage
- ✅ Ensure angle diversity for better reconstruction
- ✅ Provide visual feedback during capture

**Result:** Better scan quality and more complete 3D reconstructions! 🌈✨



