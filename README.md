# 🔥 CinderPass

> **A cinematic volcanic off-road rally experience built from the ground up in Unity 6.**

CinderPass is a systems-driven off-road environment that combines **WheelCollider vehicle physics**, **Unity Splines**, **custom URP rendering**, **procedural volcanic terrain**, **dynamic lava hazards**, **environmental VFX**, and a deliberately cinematic presentation.

The project was built around a simple idea:

> **Take a practical Unity assignment and push every system far beyond the minimum implementation without sacrificing maintainability or runtime performance.**

---

<div align="center">

![Unity](https://img.shields.io/badge/Unity-6.5-black?style=for-the-badge&logo=unity)
![URP](https://img.shields.io/badge/URP-17.5-222222?style=for-the-badge)
![Splines](https://img.shields.io/badge/Unity%20Splines-2.9.1-7B61FF?style=for-the-badge)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?style=for-the-badge&logo=windows)
![Language](https://img.shields.io/badge/Language-C%23-239120?style=for-the-badge&logo=csharp)
![Rendering](https://img.shields.io/badge/Rendering-URP%20Forward%2B-8A2BE2?style=for-the-badge)

![GitHub repo size](https://img.shields.io/github/repo-size/swastikongithub/CinderPass?style=flat-square)
![GitHub last commit](https://img.shields.io/github/last-commit/swastikongithub/CinderPass?style=flat-square)
![GitHub commit activity](https://img.shields.io/github/commit-activity/m/swastikongithub/CinderPass?style=flat-square)
![GitHub stars](https://img.shields.io/github/stars/swastikongithub/CinderPass?style=flat-square)

</div>

---

## 🎬 Visual Showcase

| Overview | Pine Hollow | The Canyon |
|---|---|---|
| ![Aerial Overview](Screenshots/VP_1_Aerial_overview.jpg) | ![Pine Hollow](Screenshots/VP_2_Pine_Hollow_basecamp.jpg) | ![The Canyon](Screenshots/VP_3_The_canyon.jpg) |

| Ember Overlook | The Cauldron | Lava Field |
|---|---|---|
| ![Ember Overlook](Screenshots/VP_4_Ember_Overlook.jpg) | ![The Cauldron](Screenshots/VP_5_The_Cauldron.jpg) | ![Lava Field](Screenshots/VP_6_Lava_field_beacons.jpg) |

| Volcano Vista |
|---|
| ![Volcano from the Plains](Screenshots/VP_7_Volcano_from_the_plains.jpg) |

> The screenshots above are representative project views rather than a substitute for the playable build.

---

# 🧭 What Is CinderPass?

CinderPass is a **volcanic off-road rally environment** where the vehicle is treated as a physical object inside a reactive world rather than a camera attached to a moving model.

The experience is built around:

- **terrain with multiple elevations**
- **valleys, ridges, channels and geological formations**
- **regional material variation**
- **a traversable authored route**
- **an intro sequence driven by a predefined spline**
- **manual driving using physical wheel interaction**
- **surface-dependent traction**
- **dangerous animated lava**
- **glowing route beacons**
- **dust, heat haze, volcanic activity and atmospheric effects**
- **cinematic cameras and presentation**
- **editor tooling for deterministic world generation**
- **performance-aware rendering and object management**

The result is intended to feel closer to a **small environmental showcase / rally prototype** than a collection of disconnected assignment requirements.

---

# ⚙️ Core Technology Stack

| Layer | Technology |
|---|---|
| Engine | Unity 6.5 |
| Render Pipeline | Universal Render Pipeline 17.5 |
| Rendering Path | Forward+ |
| Geometry / Route | Unity Splines 2.9.1 |
| Vehicle Physics | WheelCollider-based suspension + drivetrain |
| Language | C# |
| Input | Unity Input System |
| Scene Generation | Custom editor/runtime generation pipeline |
| Materials | Custom URP shaders |
| VFX | Custom particle, lava, heat and atmosphere systems |
| Asset Handling | Addressable-style organization + LFS-backed binary assets |
| Version Control | Git + Git LFS |
| Target | Windows |

---

# 🧠 Design Philosophy

CinderPass is intentionally structured around four principles:

### 01 — Systems, not hacks

Gameplay behavior is split into focused components rather than one giant controller.

```text
Input
  ↓
VehicleInput
  ↓
Drivetrain
  ↓
VehicleController
  ↓
WheelCollider Physics
  ↓
VehicleWheel
  ↓
Surface Response / Suspension / VFX / Audio
```

The goal is to make individual systems replaceable without rewriting the entire project.

### 02 — Data drives behavior

Vehicle and environmental behavior is exposed through configuration objects and reusable systems.

```text
VehicleConfig
SurfaceLibrary
BeaconStyle
AtmosphereSettings
```

This keeps tuning separate from core behavior.

### 03 — The world is generated, not hand-assembled forever

The project includes a dedicated editor-side construction pipeline for terrain, geology, route dressing, volcano construction, vegetation and environmental systems.

```text
WorldDesign
      │
      ├── TerrainBuilder
      ├── TerrainSculptor
      ├── GeologyDresser
      ├── VolcanoBuilder
      ├── PropDresser
      ├── VegetationBuilder
      ├── RouteBuilder
      └── BeaconSystem
              │
              ▼
         Final Scene
```

### 04 — Visual ambition must coexist with performance

The project deliberately remains on **URP** while using techniques such as:

- Forward+
- GPU Resident Drawer
- instancing
- LOD-aware content
- pooled lights
- reusable beacon styles
- batched environmental generation
- shader-based variation
- targeted post-process style effects rather than brute-force scene complexity

---

# 🏔️ World Architecture

The playable environment is divided conceptually into geological and visual regions.

```text
                    ┌──────────────────────┐
                    │     Volcano Peak     │
                    │  stratovolcano mass  │
                    └──────────┬───────────┘
                               │
                     radial gullies / ridges
                               │
              ┌────────────────┴────────────────┐
              │                                 │
       basalt channels                     ash slopes
              │                                 │
       lava fields                         rocky basin
              │                                 │
       beacon route ─────────────── canyon ─────┘
                               │
                         pine / basecamp
                               │
                            plains
```

The terrain is intentionally designed so that elevation, material identity and object placement communicate where the player is in the world.

---

# 🌋 Volcanic Environment

The volcano is not a simple cone primitive.

The current generation approach creates a more convincing **stratovolcano silhouette** using:

- radial ridges
- carved gullies
- basalt-filled channels
- ash accumulation
- irregular elevation falloff
- geological dressing
- strategic vista composition

This gives the mountain readable structure from multiple distances rather than one repeated radial profile.

---

# 🪨 Terrain Rendering

CinderPass uses a custom terrain material pipeline rather than relying only on a basic terrain shader.

### Custom Terrain Shader

```text
CinderTerrain.shader
        │
        ├── CinderTerrainInput.hlsl
        ├── CinderTerrainPasses.hlsl
        ├── CinderPassCommon.hlsl
        └── TerrainTextureArrays.cs
```

The terrain pipeline supports:

- multi-layer surface blending
- triplanar projection
- dual-scale sampling
- height-based blending
- macro-scale breakup
- anti-tiling variation
- multiple geological regions
- second-control-map support
- high-frequency + low-frequency detail

Conceptually:

```text
Base Terrain
     │
     ├── Region Mask
     │      ├── Ash
     │      ├── Rock
     │      ├── Basalt
     │      └── Other surface layers
     │
     ├── Height Blend
     ├── Triplanar Projection
     ├── Macro Variation
     ├── Anti-Tiling
     └── Final Lighting
             │
             ▼
       Cinematic Surface
```

---

# 🔥 Lava System

Lava is treated as a **hazard + visual phenomenon**, not simply an orange emissive plane.

Relevant systems include:

```text
Lava.shader
LavaLight
HazardZone
VehicleHazardSensor
VolcanicActivity
HazardEffect
```

The lava presentation combines:

- animated surface motion
- emissive color response
- glowing illumination
- surrounding activity
- danger zones
- vehicle hazard sensing
- heat-haze interaction
- depth-aware rendering
- environment integration

A particularly important rendering detail is that heat distortion is synchronized with the actual lava surface instead of distorting stale scene color.

---

# ♨️ Heat Haze

CinderPass includes a dedicated heat-distortion shader:

```text
HeatHaze.shader
```

The effect is designed for:

- lava edges
- hot channels
- volcanic vents
- localized high-temperature regions

The implementation is structured so distortion is geographically tied to the heat source rather than behaving like a fullscreen gimmick.

---

# 🌫️ Atmosphere

Volcanic scale is communicated partly through aerial perspective and environmental depth.

The atmosphere system includes:

```text
CinderAtmosphere.hlsl
Atmosphere.shader
AtmosphereSettings.cs
AtmosphereFeature.cs
```

The intended pipeline incorporates:

- exponential height fog
- sun scattering
- basin haze
- depth-based atmospheric falloff
- integration with particles
- integration with emissive environmental objects

This creates separation between:

```text
foreground
    ↓
midground
    ↓
basin
    ↓
volcano
    ↓
sky / atmosphere
```

instead of rendering the entire environment with a flat lighting response.

---

# 🚙 Vehicle Architecture

The vehicle is built as a physical 4x4 rather than a transform-driven arcade car.

### Runtime architecture

```text
                  Player Input
                       │
                       ▼
               PlayerVehicleInput
                       │
                       ▼
                 VehicleInput
                       │
                       ▼
                  Drivetrain
                       │
        ┌──────────────┼──────────────┐
        ▼              ▼              ▼
     Steering       Throttle        Braking
        │              │              │
        └──────────────┼──────────────┘
                       ▼
                VehicleController
                       │
             ┌─────────┴─────────┐
             ▼                   ▼
       WheelColliders       VehicleWheel
             │                   │
             ▼                   ▼
        Suspension          Wheel visuals
             │
             ▼
      Terrain / Surface
             │
             ▼
      Grip / Friction / Dust
```

---

# 🛞 Vehicle Physics

The vehicle stack includes:

- four WheelColliders
- physical suspension
- wheel contact detection
- Ackermann steering
- drivetrain simulation
- gear changes
- traction control
- anti-roll behavior
- surface-dependent grip
- downforce
- airborne stabilization
- interpolated wheel visuals

The intent is for terrain geometry to visibly affect vehicle behavior.

---

# 🧲 Surface-Aware Driving

The environment does not expose one universal grip value.

The system is structured around:

```text
SurfaceLibrary
       │
       ▼
TerrainSurfaceMap
       │
       ▼
Wheel contact
       │
       ├── grip
       ├── friction
       ├── response
       └── visual/audio feedback
```

This allows different regions to communicate a different driving response.

Examples include:

- compact ground
- loose volcanic material
- rock
- hazardous volcanic surfaces

---

# 🪶 Suspension & Wheel Detail

Dedicated systems separate physical and visual responsibilities:

```text
VehicleWheel
SuspensionLinkage
WheelDust
VehicleAudio
```

This keeps wheel motion, suspension presentation, dust generation and audio response from becoming one monolithic vehicle class.

---

# 🎥 Cinematic Camera System

CinderPass contains multiple camera layers.

```text
CameraRig
   ├── ChaseCamera
   ├── IntroCameraDirector
   └── OverviewCamera
```

The intro sequence uses the actual predefined route rather than simply teleporting the camera between static transforms.

This makes the spline meaningful to both presentation and gameplay architecture.

---

# 🧵 Spline-Driven Intro

The route pipeline uses Unity Splines.

```text
RouteTrack
     │
     ▼
Unity Spline
     │
     ├── IntroCameraDirector
     │
     └── SplineAutopilot
```

During the opening sequence, the vehicle can follow a predefined spline-based path before control transitions to the player.

The same route can serve as a source for:

- navigation
- camera motion
- route markers
- beacon placement
- environmental dressing

That turns the spline into a **world-design primitive**, not merely a cinematic shortcut.

---

# 🟠 Beacon System

Glowing beacons are reusable route-signaling elements.

Architecture:

```text
BeaconStyle
      │
      ▼
GlowBeacon
      │
      ├── BeaconLightPool
      └── Route / Environment
```

Because beacon appearance is separated into reusable data, changing a visual style does not require individually editing every beacon.

This is particularly useful when iterating on:

- emissive intensity
- color treatment
- size
- light behavior
- placement density

---

# 🌲 Environmental Motion

The environment contains dedicated systems for subtle movement and visual activity.

### Foliage

```text
FoliageWind
```

### Regional emission

```text
RegionalEmission
```

### Volcanic activity

```text
VolcanicActivity
```

### Hazard response

```text
HazardEffect
```

The goal is to avoid a completely static environment even when the vehicle is stationary.

---

# 💨 Vehicle ↔ World Interaction

The world feeds information back into the vehicle.

Examples:

```text
Terrain
   │
   ├── surface classification
   ├── slope / contact
   ├── suspension response
   └── dust conditions
          │
          ▼
       Vehicle
```

And:

```text
Lava / Hazard
      │
      ▼
VehicleHazardSensor
      │
      ▼
HazardEffect / Game systems
```

The architecture therefore supports a two-way relationship:

> **The vehicle changes its behavior because of the environment, and the environment reacts to the vehicle.**

---

# 🧰 Editor & Generation Pipeline

One of the defining parts of the project is that the world is supported by dedicated creation tooling.

### Generation stack

```text
WorldBuildPipeline
       │
       ├── WorldDesign
       ├── WorldField
       ├── TerrainBuilder
       ├── TerrainSculptor
       ├── GeologyDresser
       ├── VolcanoBuilder
       ├── RouteBuilder
       ├── PropDresser
       ├── VegetationBuilder
       ├── BeaconSystem
       ├── LightingSetup
       ├── HudBuilder
       ├── SceneAssembler
       ├── MaterialLibrary
       ├── ModelLibrary
       ├── BuggyMeshes
       ├── AudioFactory
       ├── VfxFactory
       └── VehicleAssembler
```

This reduces manual scene-authoring overhead and makes it easier to rebuild the environment after a major structural change.

---

# 🧩 Project Structure

```text
Assets/
└── CinderPass/
    ├── Art/
    │   └── ThirdParty/
    │       └── PolyHaven/
    ├── Runtime/
    │   ├── Vehicle/
    │   ├── Environment/
    │   ├── Camera/
    │   ├── VFX/
    │   ├── Audio/
    │   ├── UI/
    │   └── Systems/
    ├── Editor/
    │   ├── WorldGeneration/
    │   ├── MaterialTools/
    │   ├── SceneAssembly/
    │   └── AssetTools/
    └── Shaders/
        ├── CinderTerrain.shader
        ├── Lava.shader
        ├── Foliage.shader
        ├── BeaconGlow.shader
        ├── BeaconHalo.shader
        ├── HeatHaze.shader
        ├── ParticleSoft.shader
        └── Atmosphere.shader

Assets/
├── InputSystem_Actions.inputactions
└── Settings/

Packages/
ProjectSettings/
Screenshots/
```

> The exact directory grouping may evolve as the project continues to grow; the architectural separation is the important part.

---

# 🎨 Custom Rendering Layer

The custom shader layer currently includes:

| Shader | Purpose |
|---|---|
| `CinderTerrain.shader` | Terrain surface rendering and geological blending |
| `Lava.shader` | Animated emissive lava surface |
| `Foliage.shader` | Vegetation rendering |
| `BeaconGlow.shader` | Beacon emissive core |
| `BeaconHalo.shader` | Atmospheric halo around beacons |
| `HeatHaze.shader` | Heat distortion |
| `ParticleSoft.shader` | Soft particle rendering |
| `Atmosphere.shader` | Volumetric-style atmospheric treatment |

Shared shader functionality is centralized through:

```text
CinderPassCommon.hlsl
CinderAtmosphere.hlsl
```

This reduces duplicated shader logic and makes further rendering iteration easier.

---

# ⚡ Performance Mindset

CinderPass is intentionally being pushed visually **without treating hardware as infinite**.

The performance strategy is centered around:

### Rendering

- URP Forward+
- GPU Resident Drawer
- instancing
- custom shader work rather than excessive material duplication
- controlled transparency
- selective emissive lighting

### Scene complexity

- reusable objects
- LOD-aware assets
- generated placement rules
- pooled lights
- reusable visual styles
- controlled effect density

### Runtime architecture

- specialized components
- data-driven tuning
- centralized generation
- limited unnecessary per-frame work

The objective is not simply:

> **“Make it as expensive as possible.”**

It is:

> **“Spend the rendering budget where the player actually sees it.”**

---

# 🧪 Debugging & Validation

The project contains dedicated diagnostic systems such as:

```text
DebugOverlay
GameFlow
GameState
RespawnSystem
```

These support iteration around:

- gameplay state
- vehicle behavior
- environmental hazards
- respawn flow
- runtime debugging

The scene is also developed against repeatable review shots to make visual regressions easier to identify during major rendering changes.

---

# 🎮 Controls

The playable loop is designed around physical vehicle control.

Typical responsibilities:

| Input | Behavior |
|---|---|
| Throttle | Accelerate |
| Brake / Reverse | Slow / reverse |
| Steering | Turn vehicle |
| Camera | Follow / presentation |
| Intro sequence | Spline-driven presentation |
| Respawn | Recover from invalid vehicle states |

> Exact keyboard bindings are determined by the project's Input System asset and may evolve during development.

---

# 🚀 Getting Started

## 1. Clone

```bash
git clone https://github.com/swastikongithub/CinderPass.git
cd CinderPass
```

## 2. Open in Unity

Open the project with the project's Unity 6.5 editor version.

Recommended environment:

```text
Unity 6.5
Universal Render Pipeline 17.5
Unity Splines 2.9.1
```

## 3. Let Unity import assets

The first import may take a while because the project contains:

- shaders
- imported models
- textures
- generated content
- editor tooling

## 4. Open the CinderPass scene

Use the project's generated / assembled gameplay scene.

## 5. Press Play

The intended loop is:

```text
Launch
  ↓
Cinematic intro
  ↓
Spline-guided sequence
  ↓
Player control
  ↓
Off-road traversal
  ↓
Volcanic hazard region
  ↓
Lava / VFX / beacons
  ↓
Exploration / route completion
```

---

# 🏗️ Build

For a standalone Windows build, create a Windows 64-bit build profile in Unity.

Recommended release settings:

```text
Development Build      OFF
Autoconnect Profiler    OFF
Script Debugging        OFF
```

The build should be distributed as a complete Unity build directory or packaged archive.

A Git repository is used for the project source; large binary assets are handled through Git LFS.

---

# 📦 Git LFS

Binary-heavy Unity content is stored with Git LFS.

Tracked examples include:

```text
*.fbx
*.obj
*.blend
*.png
*.jpg
*.jpeg
*.tga
*.tif
*.tiff
*.exr
*.hdr
*.wav
*.mp3
```

This keeps the normal Git history from becoming unnecessarily dominated by large binary blobs.

---

# 🧱 Architecture at a Glance

```text
                          CINDERPASS
                              │
          ┌───────────────────┼───────────────────┐
          │                   │                   │
      GAMEPLAY             WORLD              PRESENTATION
          │                   │                   │
          ▼                   ▼                   ▼
     VehicleController   TerrainBuilder      CameraRig
     Drivetrain          VolcanoBuilder      ChaseCamera
     VehicleWheel        GeologyDresser      IntroCameraDirector
     PlayerInput         RouteBuilder        OverviewCamera
     HazardSensor        VegetationBuilder   HUD
          │                   │
          └────────────┬──────┘
                       │
                       ▼
                 ENVIRONMENTAL
                    SYSTEMS
                       │
          ┌────────────┼────────────┐
          ▼            ▼            ▼
        Lava         VFX         Atmosphere
        Beacons      Audio       Foliage
        Hazards      Lighting    Emission
                       │
                       ▼
                 CUSTOM RENDERING
                       │
       ┌───────────────┼────────────────┐
       ▼               ▼                ▼
   Terrain            Lava          Atmosphere
    Shader           Shader           Shader
       │               │                │
       └───────────────┼────────────────┘
                       ▼
                    URP 17.5
                       │
                       ▼
                  Final Frame
```

---

# 🔬 Engineering Highlights

This repository demonstrates more than scene assembly.

### Vehicle engineering

- physical WheelCollider foundation
- drivetrain abstraction
- 4x4 distribution
- steering geometry
- suspension behavior
- traction control
- anti-roll behavior
- airborne stabilization

### Environmental engineering

- generated geological forms
- regional terrain treatment
- route construction
- reusable environmental assets
- hazard regions
- dynamic emissive systems

### Rendering engineering

- custom URP shaders
- triplanar terrain projection
- height blending
- anti-tiling techniques
- animated lava
- heat distortion
- atmospheric rendering
- custom emissive effects

### Tooling engineering

- scene assembly
- procedural world generation
- configurable material systems
- reusable beacon creation
- automated environmental dressing
- deterministic rebuild-oriented workflows

---

# 🧮 Assignment Requirements → Implementation

CinderPass was originally created to satisfy an outdoor off-road environment + vehicle + volcanic hazard practical.

| Requirement | Implementation |
|---|---|
| Outdoor terrain | Multi-elevation volcanic terrain |
| Hills / valleys | Generated geological forms |
| Distinguishable regions | Terrain surface library + custom terrain shading |
| Meaningful playable space | Authored route + traversable terrain |
| Off-road vehicle | Physical 4x4 WheelCollider system |
| Spline intro | Unity Splines + SplineAutopilot / camera director |
| Manual driving | PlayerVehicleInput |
| Realistic terrain response | Surface-aware grip + suspension / wheel interaction |
| Volcanic region | Stratovolcano + lava fields + geological dressing |
| Animated lava | `Lava.shader` + volcanic activity systems |
| Environmental effects | Heat haze, particles, emissive effects, atmosphere |
| Glowing route objects | Reusable beacon system |
| Maintainability | Modular systems + data-driven configuration |
| Performance focus | Forward+, instancing, pooling, LOD-aware organization |

---

# 🧠 Why the Name “CinderPass”?

**Cinder** represents the volcanic environment: ash, scorched rock and the remnants of extreme geological activity.

**Pass** represents the route itself: an off-road traversal through ridges, valleys and volcanic terrain.

Together:

> **CinderPass = a rugged passage through a volcanic landscape.**

---

# 📸 Visual Review Set

The repository includes a repeatable set of project viewpoints:

```text
VP_1  Aerial Overview
VP_2  Pine Hollow / Basecamp
VP_3  The Canyon
VP_4  Ember Overlook
VP_5  The Cauldron
VP_6  Lava Field / Beacons
VP_7  Volcano from the Plains
```

These viewpoints are useful for comparing major visual revisions without relying on a single camera angle.

---

# 🔧 Development Workflow

The project is designed around a repeatable iteration loop:

```text
Change System
     ↓
Regenerate / Rebuild
     ↓
Run Scene
     ↓
Drive Through Environment
     ↓
Capture Review Shot
     ↓
Inspect Visual / Physics Result
     ↓
Profile
     ↓
Tune
     ↓
Commit
```

This is especially useful when changing terrain generation or rendering systems because a visual improvement in one area can introduce regressions elsewhere.

---

# 🗺️ Roadmap

CinderPass is intentionally being pushed beyond its original practical requirements.

### Rendering

- further terrain breakup
- stronger close-range surface detail
- improved geological materials
- additional cinematic lighting passes
- more sophisticated volcanic atmosphere
- deeper lava/environment interaction

### Environment

- denser geological storytelling
- stronger road wear
- richer vegetation variation
- additional environmental micro-detail
- more route-side points of interest

### Vehicle

- deeper suspension feedback
- more nuanced surface response
- richer wheel interaction
- additional audio layers
- stronger dust / debris response

### Presentation

- more cinematic intro timing
- stronger camera composition
- richer environmental transitions
- improved HUD polish
- higher-impact finishing pass

---

# 🔐 Repository Hygiene

The repository intentionally avoids committing obsolete or unrelated project material.

Examples of excluded content include:

```text
/.plastic/
/_Archive/
/Screenshots/Review/
/Assets/PROMETEO - Car Controller/
/Assets/Screenshots/
```

The aim is to keep the public repository focused on the active CinderPass architecture rather than historical experiments.

---

# 🌐 Repository

**GitHub**

https://github.com/swastikongithub/CinderPass

The repository contains the Unity source project, custom systems, shaders, generation tools and project assets required to continue development.

---

# 📚 Third-Party Assets

Where third-party assets are used, their original attribution / credit information is retained within the project.

Currently documented third-party material includes the PolyHaven asset collection under:

```text
Assets/CinderPass/Art/ThirdParty/PolyHaven/
```

See the included credit documentation before redistributing third-party content.

---

# 👨‍💻 Author

### Swastik Singh

CSE undergraduate building CinderPass as an exploration of:

```text
Game Development
+
Vehicle Physics
+
Rendering
+
Procedural Environment Design
+
Technical Art
+
Systems Architecture
```

---

# ⭐ Project Intent

CinderPass started as a practical Unity assignment.

It evolved into something more interesting:

> **an experiment in how far a constrained Unity project can be pushed when gameplay systems, rendering, environment generation, tooling and presentation are treated as one coherent engineering problem.**

The emphasis is not only on how the final frame looks.

It is also on **how that frame is produced**.

```text
                         CINDERPASS

               ┌─────────────────────────┐
               │       PLAYABLE WORLD    │
               └────────────┬────────────┘
                            │
             ┌──────────────┼──────────────┐
             ▼              ▼              ▼
          Physics        Rendering       Tooling
             │              │              │
             └──────────────┼──────────────┘
                            ▼
                      Environment
                            │
                            ▼
                       Presentation
                            │
                            ▼
                      Final Experience
```

---

<div align="center">

## 🔥 Built to be driven. Built to be inspected. Built to be pushed.

**CinderPass — Volcanic terrain. Physical driving. Cinematic presentation.**

</div>

---

## 📄 License

This repository's source and project-specific code are provided for development / portfolio purposes.

Third-party assets remain subject to their respective licenses and attribution requirements.

See individual asset credit files where applicable.
