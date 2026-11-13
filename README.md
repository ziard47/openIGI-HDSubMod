# HDSubMod

**HDSubMod** is a high-definition subtitle, typography, and localization enhancement mod for **Project I.G.I.** running on the **OpenIGI** engine.

It replaces the low-resolution retail bitmap cutscene font with crisp, modern TrueType vector typography, provides cinematic letterbox positioning, restores character-specific color themes, and adds full multi-language translation support.

---

## ✨ Features

- **🎬 Cinematic Letterbox Placement**:
  - Subtitles are positioned neatly inside the bottom black letterbox bar, preventing any overlap with the 3D game scene.
- **🎨 Dynamic Character Speaker Themes**:
  - Speaker names are color-coded with distinct tactical colors and a drop shadow for high readability:
    - **ANYA**: Electric Cyan (`#00DCFF`)
    - **JONES**: Tactical Green (`#50E66E`)
    - **PRIBOI / JOSEF**: Warm Gold (`#FFD23C`)
    - **EKK**: Crimson Red (`#FF4B4B`)
    - **CAPT. HARRISON**: Amber (`#FFAA32`)
    - **Dialogue Body**: Pure White (`#FFFFFF`) with shadow
- **🌍 Multi-Language Localization**:
  - Full translations for all **176 cutscene dialogue lines** across all 14 missions, Intro, and Outro cutscenes.
  - **Supported Languages**:
    - **English** (Default)
    - **Sinhala (සිංහල)**
    - **Tamil (தமிழ்)**
    - **Hindi (हिन्दी)**
    - **Russian (Русский)**
    - **Spanish (Español)**
    - **German (Deutsch)**
- **🔤 Native Unicode TrueType Fonts**:
  - Bundled with official **Google Noto** fonts (`Noto Sans Sinhala`, `Noto Sans Tamil`, `Noto Sans Devanagari`) and TrueType Cyrillic fonts.
  - Dynamic font atlas baking ensures non-Latin Unicode scripts render without question marks (`????`).
- **⚙️ In-Game Mod Settings**:
  - Select your preferred subtitle language anytime via the OpenIGI Launcher (`Settings` → `Mods` → `HDSubMod`).
- **🛡️ Full HUD Preservation**:
  - Implements `IHudIconSource` to guarantee that all in-game HUD elements (weapons, health bars, interaction timers, ammo, and radar) remain 100% intact.
  - Standard in-game gameplay dialogue forwards directly to OpenIGI's native HUD.

---

## 📂 Project Structure

```text
HDSubMod/
├── HDSubMod.cs               # Main mod source code & CustomHudPresenter
├── HDSubMod.csproj           # OpenIGI Modding SDK project file
├── README.md                 # This documentation
├── fonts/                    # High-definition TrueType fonts
│   ├── NotoSansSinhala-Regular.ttf
│   ├── NotoSansTamil-Regular.ttf
│   └── NotoSansDevanagari-Regular.ttf
├── translations/             # Runtime translation dictionaries
│   ├── english.json
│   ├── sinhala.json
│   ├── tamil.json
│   ├── hindi.json
│   ├── russian.json
│   ├── spanish.json
│   └── german.json
└── *_detailed.json           # Detailed side-by-side translation files
    ├── english_detailed.json
    ├── sinhala_detailed.json
    ├── tamil_detailed.json
    ├── hindi_detailed.json
    ├── russian_detailed.json
    ├── spanish_detailed.json
    └── german_detailed.json
```

---

## 🚀 Building & Installation

### Requirements
- [.NET 10.0 SDK](https://dotnet.microsoft.com/)
- OpenIGI with `OpenIGI.Modding.Sdk` (v0.6.7 or newer)

### Build
Run the following command from the mod directory:

```bash
dotnet build
```

This compiles `HDSubMod.dll` and outputs the installable mod package into:
```
bin/Debug/net10.0/mod/
```

### Installation
1. Launch OpenIGI.
2. In the Launcher, go to **Settings** → **Mods**.
3. Enable **HDSubMod**.
4. Choose your preferred **Subtitle Language** from the dropdown menu.

---

## 🌐 Adding New Languages

To add a new language translation:
1. Create a new JSON file inside `translations/<language>.json`.
2. Map the original English subtitle string to your translated text:
   ```json
   {
     "ANYA: Good work getting here so fast.": "ANYA: <Your Translation Here>"
   }
   ```
3. Add the language to the `SubtitleLanguage` enum in `HDSubMod.cs` and recompile.

---

## 📄 License
MIT License.