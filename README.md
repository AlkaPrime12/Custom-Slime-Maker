<h1 align="center">🧪 Custom Slime Maker</h1>

<p align="center"><b>A MelonLoader mod that lets you design your own slimes in Slime Rancher 2 — colors, parts, plorts and fusions, live in-game.</b></p>

<p align="center">
  <img src="https://img.shields.io/badge/version-1.0.1-blue">
  <img src="https://img.shields.io/badge/game-Slime%20Rancher%202-ff69b4">
  <img src="https://img.shields.io/badge/loader-MelonLoader%200.7%2B-orange">
  <img src="https://img.shields.io/badge/license-MIT-green">
  <img src="https://img.shields.io/badge/PRs-welcome-brightgreen">
</p>

<p align="center"><i>Part of the <b>RealRancher</b> collection — alongside <a href="https://github.com/AlkaPrime12/Ranch-Builder">Ranch Builder</a>.</i></p>

---

## 📦 Installation

### Prerequisites
- **Slime Rancher 2** (Steam or Xbox Game Pass)
- **MelonLoader 0.7.0+** installed on the game

### Steps
1. Download `CustomSlimeCreator.dll` from the [latest release](https://github.com/AlkaPrime12/Custom-Slime-Maker/releases).
2. Place the `.dll` in your game's `Mods/` folder:
   ```
   [Slime Rancher 2 folder]/Mods/CustomSlimeCreator.dll
   ```
3. Launch the game and load into a save.
4. Press **F2** to open the Slime Maker editor.

---

## 🎮 What It Does

| Feature | Description |
| --- | --- |
| **Custom slimes** | Clone any base slime and make it your own — fully functional, vaccable, saveable |
| **Colors** | Set top / middle / bottom / vac colors with a live preview |
| **Parts** | Add wings, ears, tails, spikes, auras… taken from other slimes, recolorable |
| **Body & shader effects** | Twin swirl, Sloomber stars, Rad aura, crystal shards, rock plating, and more |
| **Custom plorts** | Each slime drops its own plort — custom name, color, 3D icon and market value |
| **Diet & zones** | Choose what it eats (fruit / veggie / meat / nectar / chicks) |
| **Custom name & icon** | Name it and auto-generate an icon rendered from its 3D model |
| **Fusions** | Feed a slime another's plort and they fuse into a mixed largo — custom×custom and custom×vanilla, taking the looks, diet and plorts of both. Feed the largo a third plort and it turns into a Tarr |
| **Discovered fusions tab** | Browse the fusions you've made, with both parents' icons |
| **Saves** | Everything persists per save via JSON in `UserData/CustomSlimeCreator/` |

---

## ⌨️ Usage
- **F2** — open / close the editor (in a save).
- Pick a base slime, tweak colors / parts / options, then **Create / Update** to apply live, or **Spawn** to drop one in front of you.
- **Save** stores the slime to disk so it loads automatically next time.

> ⚠️ The first time you make slimes, their icons are rendered once (a brief hitch). After that they're cached and load instantly.

---

## 🛠️ Build
`dotnet build -c Release`. The `GameDir` property in the `.csproj` points at your SR2 folder; a post-build step copies the DLL into `Mods/`. References only MelonLoader and the game's Il2Cpp assemblies.

---

## 🤝 Credits
Made by **AlkaPrime**. Part of the **RealRancher** mod collection.
Issues and PRs welcome.
