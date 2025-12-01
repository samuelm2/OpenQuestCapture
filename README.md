# OpenQuestCapture


**Capture and store real-world data on Meta Quest 3 or 3s, including HMD/controller poses, stereo passthrough images, camera metadata, and depth maps.**


![Demo](docs/demo.gif)


---

## 📖 Overview

`OpenQuestCapture` is a Unity-based data logging app for Meta Quest 3. It captures and stores synchronized real-world information such as headset and controller poses, images from both passthrough cameras, camera characteristics, and depth data, organized per session.

For **data parsing, visualization, and reconstruction**, refer to the companion project:
**[Meta Quest 3D Reconstruction](https://github.com/samuelm2/quest-3d-reconstruction)**

This includes:

* Scripts for **loading and decoding** camera poses, intrinsics, and depth descriptors
* Conversions of **raw YUV images** and **depth maps** to usable formats (RGB, point clouds)
* Utilities for **reconstructing 3D scenes** using [Open3D](http://www.open3d.org/)
* Export pipelines to prepare data for **SfM/SLAM tools** like **COLMAP**

---

## ✅ Features

* Records HMD and controller poses (in Unity coordinate system)
* Captures **YUV passthrough images** from **both left and right cameras**
* Logs **Camera2 API characteristics** and image format information
* Saves **depth maps** and **depth descriptors** from both cameras
* **Synchronized capture** with configurable frame rate for depth and camera images (default: 3 FPS)
* **Perfect timestamp alignment** between camera images and depth maps
* **Real-time Depth Point Cloud Visualization**:
  * Visualizes depth coverage using a particle system.
  * **Color-coded feedback**: Different colors indicate different angles at which the point was captured. White indicates head-on coverage, while vivid colors indicate grazing angles.
* Automatically organizes logs into timestamped folders on internal storage


### Step-by-Step Guide

1. **Install the app**: Sideload the app from the APK file in the Releases section of this repository. [This video](https://www.youtube.com/watch?v=bsC805t63-E) has a good guide on how to set up sideloading.
2. **Start recording**: Launch the app and press the Menu button on the left controller to start a capture session.
3. **Stop recording**: To stop, press the left controller's Menu button again.
4. **Move the data from your Quest to your computer**: The data is stored on the Quest's internal storage. You can move it to your computer using a USB cable by connecting the Quest to your computer and using Windows File Explorer. The data is stored in the `/Quest 3/Internal Shared Storage/data/com.samusynth.OpenQuestCapture/files` directory.
Or, you can use press the Y button on the left controller to toggle the Recording Menu. Select "Export Data" to export the data to a zip file in the Quest 3 Download folder which can be uploaded to Google Drive or other cloud storage services. Note: Sometimes you may have to restart the headset for the file to show up in the Download folder. This is an active bug.
5. **Reconstruct the scene**: Use the companion project [Meta Quest 3D Reconstruction](https://github.com/samuelm2/quest-3d-reconstruction) to reconstruct a COLMAP sparse point cloud from the captured data. Or, if you prefer a cloud-based, end-to-end solution, you can go to [vid2scene.com/upload/quest](https://vid2scene.com/upload/quest) and upload the Quest raw data zip file to create a 3DGS reconstruction on the cloud.

### 📸 How to take a good capture

To ensure the best possible 3D reconstruction results:

1.  **Lighting**: Ensure the environment is well-lit and consistent. Avoid extreme shadows or blinding lights.
2.  **Coverage**: Use the **Depth Point Cloud Visualization** to verify you have covered all surfaces.
    *   The point visualization point color is determined by the angle at which the point was captured. White indicates head-on coverage, while vivid colors different indicate grazing angles. For best capture, try to make sure the surface has points with many different colors. This indicates it has been captured from many different angles. 
    *   Note: the point cloud visualization is for reference only. In reality, scenes are reconstructed from thousands more points than what is visible in the visualization.
3.  **Movement**: Move slowly and steadily. Avoid rapid head movements which can cause motion blur.
4.  **How long**: From my testing, the best captures usually are within the 1 to 3 minute range.

---

## 🧾 Data Structure

Each time you start recording, a new folder is created under:

```
/sdcard/Android/data/com.samusynth.OpenQuestCapture/files
```

Example structure:

```
/sdcard/Android/data/com.samusynth.OpenQuestCapture/files
└── YYYYMMDD_hhmmss/
    ├── hmd_poses.csv
    ├── left_controller_poses.csv
    ├── right_controller_poses.csv
    │
    ├── left_camera_raw/
    │   ├── <unixtimeMs>.yuv
    │   └── ...
    ├── right_camera_raw/
    │   ├── <unixtimeMs>.yuv
    │   └── ...
    │
    ├── left_camera_image_format.json
    ├── right_camera_image_format.json
    ├── left_camera_characteristics.json
    ├── right_camera_characteristics.json
    │
    ├── left_depth/
    │   ├── <unixtimeMs>.raw
    │   └── ...
    ├── right_depth/
    │   ├── <unixtimeMs>.raw
    │   └── ...
    │
    ├── left_depth_descriptors.csv
    └── right_depth_descriptors.csv
```

---

## 📄 Data Format Details

### Pose CSV

* Files: `hmd_poses.csv`, `left_controller_poses.csv`, `right_controller_poses.csv`
* Format:

  ```
  unix_time,ovr_timestamp,pos_x,pos_y,pos_z,rot_x,rot_y,rot_z,rot_w
  ```

### Camera Characteristics (JSON)

* Obtained via Android Camera2 API
* Includes pose, intrinsics (fx, fy, cx, cy), sensor info, etc.

### Image Format (JSON)

* Includes resolution, format (e.g., `YUV_420_888`), per-plane buffer info
* Contains baseMonoTimeNs and baseUnixTimeMs for timestamp alignment

### Passthrough Camera (Raw YUV)
- Raw YUV frames are stored as `.yuv` files under `left_camera_raw/` and `right_camera_raw/`.
- Image format and buffer information are provided in the accompanying `*_camera_image_format.json` files.

To convert passthrough YUV (YUV_420_888) images to RGB for visualization or reconstruction, see: [Meta Quest 3D Reconstruction](https://github.com/samuelm2/quest-3d-reconstruction)

### Depth Map Descriptor CSV

* Format:

  ```
  timestamp_ms,ovr_timestamp,create_pose_location_x, ..., create_pose_rotation_w,
  fov_left_angle_tangent,fov_right_angle_tangent,fov_top_angle_tangent,fov_down_angle_tangent,
  near_z,far_z,width,height
  ```

### Depth Map

* Raw `.float32` depth images (1D float per pixel)

To convert raw depth maps into linear or 3D form, refer to the companion project: [Meta Quest 3D Reconstruction](https://github.com/samuelm2/quest-3d-reconstruction)

---

## 🚀 Installation & Usage

### For End Users

> [!NOTE]
> Releases are not yet available. Please build from source or check back later.


## 🎮 Usage

### Recording & Management

1. **Start/Stop Recording**: 
   - Launch the app.
   - Press the **Menu button** on the left controller to dismiss the instruction panel and start logging.
   - To stop, simply close the app or pause the session.

2. **Manage Recordings**:
   - Press the **Y button** on the left controller to toggle the **Recording Menu**.
   - This menu allows you to:
     - **View** a list of all recorded sessions.
     - **Export** sessions to a zip file (saved directly to the Quest Downloads folder: `/Download/Export/`).
     - **Delete** unwanted sessions to free up space.

> [!NOTE]
> The capture frame rate is set to **3 FPS** by default to balance performance and data quality for reconstruction.

---

## ☁️ Cloud Processing (Recommended)

For the easiest workflow, you can upload your exported `.zip` files directly to the vid2scene cloud processing service:

**[vid2scene.com/upload/quest](https://vid2scene.com/upload/quest)**

This service will automatically process your data and generate a 3D reconstruction.

---

## 💻 Local Processing & Reconstruction

If you prefer to process data locally, this project includes a submodule **[quest-3d-reconstruction](https://github.com/samuelm2/quest-3d-reconstruction)** with powerful Python scripts.

### Setup

Ensure you have the submodule initialized:

```bash
git submodule update --init --recursive
```

### End-to-End Pipeline

The `e2e_quest_to_colmap.py` script provides a one-step solution to convert your Quest data into a COLMAP format.

**Usage Example:**

```bash
python quest-3d-reconstruction/scripts/e2e_quest_to_colmap.py \
  --project_dir /path/to/extracted/session/folder \
  --output_dir /path/to/output/colmap/project \ 
  --use_colored_pointcloud
```

Once in colmap format, the reconstruction can be passed into various Gaussian Splatting tools to generate a Gaussian Splatting scene.

**What this script does:**
1. **Converts YUV images** to RGB.
2. **Reconstructs the 3D scene** (point cloud).
3. **Exports** everything (images, cameras, points) to a COLMAP sparse model.

---

## 🛠 Environment

* Unity **6000.2.9f1**
* Meta OpenXR SDK
* Device: Meta Quest 3 or 3s only

---

## 🙏 Acknowledgements

This project is a fork of **[QuestRealityCapture](https://github.com/t-34400/QuestRealityCapture)** by **[t-34400](https://github.com/t-34400)**.

Huge thanks to the original author for their excellent work in making Quest sensor data accessible!

---

## 📝 License

This project is licensed under the **[MIT License](LICENSE)**.

This project uses Meta’s OpenXR SDK — please ensure compliance with its license when redistributing.

---

## 📌 Call for Contributions

This project is in its early stages and is still in active development. If you have any suggestions, bug reports, or feature requests, please open an issue or submit a pull request.

One area improvment is the export process. Currently it can take several minutes to export a session. It would be great to have a faster way to do this.
