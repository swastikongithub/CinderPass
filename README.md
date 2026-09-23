# CinderPass

> A cinematic off-road rally experience built in Unity 6.5, combining a physics-driven 4x4 vehicle, procedural volcanic terrain, custom URP shaders, dynamic lava, environmental VFX, and a systems-oriented gameplay architecture.

![Unity](https://img.shields.io/badge/Unity-6.5-black?logo=unity)
![Render Pipeline](https://img.shields.io/badge/Render%20Pipeline-URP-blue)
![Language](https://img.shields.io/badge/Language-C%23-purple)
![Splines](https://img.shields.io/badge/Unity-Splines-orange)
![Status](https://img.shields.io/badge/Status-Playable%20Vertical%20Slice-success)

---

## Overview

CinderPass is a third-person off-road driving experience set across a rugged volcanic wilderness.

The project was built around three core goals:

- **A convincing outdoor environment** with strong elevation changes, valleys, ridges, distinct ground regions, vegetation, geology, and a meaningful playable route.
- **A physically driven off-road vehicle** with spline-based cinematic introduction, WheelCollider suspension, terrain-aware handling, wheel interaction, and recovery systems.
- **A volcanic gameplay region** with animated lava, hazards, heat distortion, ash, smoke, embers, fumaroles, glowing route markers, and region-specific atmosphere.

The project is deliberately structured as a reusable technical system rather than a single monolithic scene script. Environment generation, route definition, vehicle simulation, rendering, VFX, hazards, cameras, and presentation are separated into focused systems that can be iterated independently.

---

## Visual Showcase

| Aerial Overview | Pine Hollow |
|---|---|
| ![Aerial Overview](Screenshots/VP_1_Aerial_overview.jpg) | ![Pine Hollow](Screenshots/VP_2_Pine_Hollow_basecamp.jpg) |

| The Canyon | Ember Overlook |
|---|---|
| ![The Canyon](Screenshots/VP_3_The_canyon.jpg) | ![Ember Overlook](Screenshots/VP_4_Ember_Overlook.jpg) |

| The Cauldron | Lava Field |
|---|---|
| ![The Cauldron](Screenshots/VP_5_The_Cauldron.jpg) | ![Lava Field](Screenshots/VP_6_Lava_field_beacons.jpg) |

![Volcano Vista](Screenshots/VP_7_Volcano_from_the_plains.jpg)

---

## Key Features

### World & Environment

- Procedurally authored mountainous off-road landscape
- Hills, valleys, ridges, canyon sections, basin terrain, and volcanic slopes
- Route-conforming terrain sculpting
- Road carving with crown, ditches, embankments, and surface transitions
- Multiple terrain regions including grass, forest floor, dirt, trail, rock, scorched ground, ash, and basalt
- Geological dressing using clustered rocks, cliff formations, basalt fields, boulders, debris, and terrain-aware placement
- Forests built from optimized procedural conifer geometry and photographed branch cards
- Burned vegetation and volcanic transition zones
- Backdrop terrain tiles for large-scale environmental continuity

### Vehicle & Driving

- Physics-driven 4x4 off-road vehicle
- Unity WheelCollider-based wheel simulation
- Suspension and linkage system
- Ackermann steering
- Drivetrain and gearbox simulation
- Surface-dependent grip and wheel response
- Traction control
- Anti-roll behavior
- Airborne stabilization
- Visual wheel interpolation
- Wheel dust and terrain-dependent feedback
- Hazard detection and vehicle recovery
- Spline-driven introduction before manual control

### Route & Cinematics

- Unity Splines-based authoritative route
- Route sampling reused for terrain carving, placement, cameras, autopilot, and recovery
- Intro camera director
- Chase camera
- Overview/presentation viewpoints
- Speed-aware camera behavior
- Volcano reveal and environmental composition viewpoints

### Volcanic Systems

- Multiple lava pools and volcanic vents
- Main lava river
- Summit crater
- Animated lava crust and molten channels
- Cooling crust and shore transitions
- Lava hazard volumes
- Heat distortion
- Ember particles
- Ash, smoke, steam, and eruption effects
- Fumaroles and geothermal activity
- Localized lava lighting
- Scorch and ash transitions around volcanic areas
- Reusable glowing route beacons

### Rendering

- Custom URP terrain shader
- Single-pass 8-layer terrain shading
- Triplanar projection for steep terrain
- Height-based layer blending
- Multi-scale sampling and anti-tiling
- Macro terrain variation
- Custom lava shader
- Custom foliage shader with wind animation and alpha testing
- Custom beacon glow and halo shaders
- Heat-haze shader
- Soft-particle shader
- Height-based atmospheric fog and sun scattering
- Region-aware atmospheric settings
- Reflection/probe and lighting support

### Performance & Maintainability

- Runtime LOD chains
- Shared materials
- GPU instancing where appropriate
- Distance and frustum culling
- Pooled dynamic lights
- Bounded particle systems
- Spatially concentrated VFX
- Terrain surface lookup optimized for wheel queries
- Minimal per-object runtime work
- Asynchronous editor build pipeline
- Data-driven world configuration
- Single authoritative world assembler to avoid duplicate environments

---

## Technical Architecture

The project is organized around focused systems rather than one large controller.

```text
Assets/
└── CinderPass/
    ├── Art/
    │   ├── Generated/
    │   ├── Materials/
    │   ├── Shaders/
    │   ├── TerrainLayers/
    │   └── ThirdParty/
    │
    ├── Scripts/
    │   ├── Runtime/
    │   │   ├── Cameras/
    │   │   ├── Environment/
    │   │   ├── Hazards/
    │   │   ├── Rendering/
    │   │   ├── Route/
    │   │   ├── UI/
    │   │   └── Vehicle/
    │   │
    │   └── Editor/
    │       ├── Art/
    │       ├── Pipeline/
    │       ├── Scene/
    │       ├── Vehicle/
    │       └── World/
    │
    ├── Settings/
    └── UI/
```

### Core Systems

| System | Responsibility |
|---|---|
| `RouteTrack` | Authoritative spline route used across gameplay and world generation |
| `WorldBuildPipeline` | Coordinates asynchronous project and world generation passes |
| `WorldField` | Continuous large-scale landscape definition |
| `TerrainSculptor` | Generates terrain height, road shaping, erosion, and volcanic terrain forms |
| `TerrainBuilder` | Creates terrain data, paints regions, and handles vegetation/detail placement |
| `VehicleController` | WheelCollider-based 4x4 physics and handling |
| `SplineAutopilot` | Drives the vehicle along the introduction route |
| `HazardZone` | Defines gameplay hazard volumes such as lava |
| `RespawnSystem` | Handles vehicle recovery using route-aware safe positions |
| `VolcanicActivity` | Coordinates volcanic effects and regional behavior |
| `BeaconSystem` | Places and manages reusable glowing route markers |
| `CameraRig` | Coordinates gameplay and cinematic camera states |
| `CinderTerrain` | Custom terrain rendering pipeline |
| `MaterialLibrary` | Centralized shared material creation |
| `VegetationBuilder` | Generates optimized vegetation LOD sets |
| `ModelLibrary` | Processes, decimates, and prepares environment models |
| `VfxFactory` | Authors reusable bounded VFX prefabs |

---

## Custom Rendering

CinderPass uses custom HLSL/ShaderLab rendering where the stock pipeline was not sufficient for the intended look.

The terrain renderer combines all eight terrain layers in a custom pass and adds:

- world-space triplanar projection
- dual-scale sampling
- height-aware blending
- macro-scale variation
- anti-tiling techniques
- terrain-aware layer transitions

The volcanic region adds dedicated shaders for:

- animated molten flow
- cooling lava crust
- glowing fissures
- heat distortion
- emissive route beacons
- soft atmospheric particles
- wind-reactive foliage

The goal is to add visual detail without turning every environment object into a unique material or expensive runtime effect.

---

## Asset Pipeline

Environment content is prepared through editor-side tooling before entering the playable scene.

The pipeline covers:

1. Import rules and texture processing
2. Texture baking and mask generation
3. Material construction
4. Model decimation and collision preparation
5. Vegetation LOD generation
6. Procedural vehicle assembly
7. VFX prefab creation
8. Terrain/world generation
9. Geology and environmental dressing
10. Lighting, cameras, HUD, and gameplay wiring

This keeps heavy authoring work out of gameplay code and makes the environment reproducible from its source data.

---

## Controls

Keyboard and gamepad input are provided through Unity's Input System.

The project includes a dedicated input action asset at:

```text
Assets/InputSystem_Actions.inputactions
```

For keyboard play, the primary vehicle movement uses the project's WASD input scheme.

---

## Getting Started

### Requirements

- Unity 6.5 or a compatible Unity 6.x editor
- Windows development machine recommended for the current project
- Git LFS for repository assets

### Clone

```bash
git lfs install
git clone https://github.com/swastikongithub/CinderPass.git
cd CinderPass
```

Open the project through Unity Hub.

### Scene

Open the generated CinderPass playable scene and enter Play Mode.

---

## Development Workflow

The project is designed around an iterative build pipeline.

```text
Change world data / systems
        ↓
Compile
        ↓
Run build pipeline
        ↓
Regenerate affected content
        ↓
Open playable scene
        ↓
Review fixed camera viewpoints
        ↓
Profile
        ↓
Iterate
```

The editor tooling deliberately keeps world generation separate from runtime vehicle and gameplay code, allowing the environment to be regenerated without rewriting the driving systems.

---

## Project Goals

CinderPass demonstrates practical Unity engineering across:

- Environment generation
- Vehicle physics
- Unity Splines
- Terrain systems
- Custom shaders
- VFX
- Lighting and atmosphere
- Camera systems
- Gameplay hazards
- Performance-aware content authoring
- Editor automation
- Maintainable Unity project architecture

The project intentionally balances visual ambition with runtime practicality rather than relying on a collection of disconnected visual effects.

---

## Third-Party Assets

The project includes third-party environment assets used as source material for the generated environment.

Relevant credit information is included inside the project, including:

```text
Assets/CinderPass/Art/ThirdParty/PolyHaven/CREDITS.md
```

Third-party licenses and attribution notices should be preserved when redistributing the project.

---

## Repository & Releases

**Source:** https://github.com/swastikongithub/CinderPass

The repository contains the Unity project, source code, generated assets, shaders, and editor tooling.

A packaged Windows build can be distributed separately through a GitHub Release or game platform once the final build has been prepared.

---

## Author

**Swastik Singh**

Built as a Unity technical/game-development project focused on combining systems engineering with high-fidelity environment presentation.

---

## License

The source code and original project work are provided for portfolio and educational purposes.

Third-party assets retain their respective licenses. See the included credit and license files before redistributing those assets.
