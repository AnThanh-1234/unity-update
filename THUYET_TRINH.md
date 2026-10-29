# Prowl Engine: Cấu trúc code và công nghệ

> Game engine mã nguồn mở viết bằng **C# / .NET 10**, API tương tự Unity
> Phiên bản: 1.0 Preview 5. Giấy phép: MIT

---

## Mục lục

1. Tổng quan dự án
2. Công nghệ sử dụng
3. Cấu trúc solution
4. Kiến trúc tổng thể
5. Runtime: lõi engine
6. Vòng lặp game (Game Loop)
7. Mô hình GameObject / Component
8. Hệ thống Asset
9. Editor: phần logic
10. Editor: phần UI
11. Biên dịch script và Hot Reload
12. Build và Player
13. Kiểm thử và chất lượng code
14. Tổng kết

---

## 1. Tổng quan dự án

- **Prowl** là game engine 3D có đầy đủ **Editor** (giống Unity Editor) và **Runtime** (phần chạy game).
- Mục tiêu:
  - Giữ API gần với Unity để người dùng Unity dễ chuyển sang
  - Tuân theo nguyên tắc **KISS**: nhỏ gọn, dễ tùy biến
  - Viết **thuần C#**, chạy đa nền tảng (Windows, Linux, macOS, Android)
- Quy mô repo: khoảng **970 file**, chia thành 7 project .NET

---

## 2. Công nghệ sử dụng

| Nhóm | Công nghệ | Vai trò |
|---|---|---|
| Ngôn ngữ / nền tảng | **C#**, **.NET 10** | Toàn bộ engine và editor |
| Cửa sổ và input | **Silk.NET** (GLFW) | Tạo cửa sổ, bàn phím, chuột, gamepad |
| Đồ họa | **Prowl.Graphite** (+ ShaderDef) | Renderer **Vulkan**, biên dịch shader |
| UI | **Prowl.Paper** + **Prowl.Origami** | Paper là UI immediate-mode. Origami là bộ widget (Button, Slider, Dropdown…) dựng trên Paper |
| Vẽ 2D / chữ | Prowl.Quill, Prowl.Scribe | Vẽ vector và render font |
| Vật lý | **Jitter2** | Rigidbody, collider, constraint |
| Âm thanh | **miniaudio** (thư viện native) | Phát âm thanh, mixer, hiệu ứng |
| Navigation | **Prowl.Recast** | Bake NavMesh, tìm đường cho agent |
| Serialization | **Prowl.Echo** | Lưu/đọc scene, asset, prefab dạng cây dữ liệu |
| Import model | Prowl.Clay | Đọc model 3D (FBX, OBJ…) |
| Lightmap | Prowl.Unwrapper | Sinh UV cho lightmap |
| VR/XR | **OpenXR** (Silk.NET.OpenXR) | Hỗ trợ kính VR |
| Biên dịch script | **Roslyn** (Microsoft.CodeAnalysis) | Biên dịch C# của người dùng ngay trong editor |
| Kiểm thử | **xUnit**, NSubstitute | Unit test |

> Các package `Prowl.*` thuộc bộ thư viện **Anthology** của nhóm Prowl, được lấy qua NuGet (phiên bản `3.9.3`, khai báo trong `Directory.Build.props`).

---

## 3. Cấu trúc solution (`Prowl.slnx`)

```
Prowl/
├── Prowl.Runtime/          ← Lõi engine (game chạy bằng phần này)
├── Prowl.Editor/           ← Ứng dụng Editor
├── Players/Desktop/        ← Trình chạy game đã build (desktop)
├── Prowl.Cli/              ← Công cụ dòng lệnh, điều khiển editor từ bên ngoài
├── Prowl.Analyzers/        ← Roslyn analyzer kiểm tra code người dùng
├── Prowl.Runtime.Test/     ← Test cho Runtime
├── Prowl.Editor.Test/      ← Test cho Editor
├── Samples/Runtime/        ← Các ví dụ
└── Libraries/              ← Thư viện native (miniaudio, OpenXR) cho từng OS/CPU
```

**Quan hệ phụ thuộc giữa các project:**

```
Prowl.Editor ──► Prowl.Runtime ◄── Players/Desktop
     │
     ├──► Prowl.Cli
     └──► Prowl.Analyzers
```

Runtime **không phụ thuộc** vào Editor. Nhờ vậy game build ra không chứa code của editor.

---

## 4. Kiến trúc tổng thể

```
┌────────────────────────────────────────────────────────┐
│                     PROWL EDITOR                       │
│  ┌──────────────── UI (GUI/) ────────────────┐         │
│  │ Panels · Inspector · SceneView · Popups   │         │
│  └───────────────────┬───────────────────────┘         │
│                      │ gọi                             │
│  ┌──────────── Logic Editor ─────────────────┐         │
│  │ Core (Undo, Selection, Menu)              │         │
│  │ AssetsDatabase (Import, Meta, Watcher)    │         │
│  │ Projects (Scripting, Settings, Save)      │         │
│  │ Prefabs · Migration · Cli · Build         │         │
│  └───────────────────┬───────────────────────┘         │
└──────────────────────┼─────────────────────────────────┘
                       │ kế thừa / sử dụng
┌──────────────────────▼─────────────────────────────────┐
│                     PROWL RUNTIME                      │
│ Game loop · GameObject/Component · Scene · AssetDB     │
│ Rendering · Physics · Audio · Animation · Navigation   │
│ Input · UI trong game · XR                             │
└──────────────────────┬─────────────────────────────────┘
                       │
   Silk.NET · Graphite (Vulkan) · Jitter2 · miniaudio · Recast · Echo
```

Điểm thiết kế quan trọng: `EditorApplication` **kế thừa** lớp `Game` của Runtime. Vì vậy editor dùng chung vòng lặp với game thật và chỉ ghi đè thêm phần giao diện và play mode.

---

## 5. Runtime: lõi engine (`Prowl.Runtime/`)

| Thư mục | Nội dung |
|---|---|
| `Game.cs`, `Application.cs`, `Time.cs`, `Window.cs` | Vòng lặp game, trạng thái ứng dụng, thời gian, cửa sổ |
| `GameObject/` | `GameObject`, `Component`, `SceneDispatcher`, Tag/Layer, Prefab link |
| `Components/` (khoảng 128 file) | Camera, Light, MeshRenderer, ParticleSystem, Terrain, Physics, UI… |
| `Resources/` | Các loại asset: `Scene`, `Mesh`, `Material`, `Shader`, `Texture`, `AudioClip`, `PrefabAsset`… |
| `Rendering/`, `Graphics/` | Đồng bộ scene sang renderer, tonemapping, sương mù/mây volumetric, LOD |
| `Physics/` | `PhysicsWorld` bọc Jitter2: raycast, collision matrix, trigger |
| `Audio/` | `AudioContext`, mixer, hiệu ứng (Reverb, Delay, Filter…) |
| `Animation/` | `Animator`, `AnimationClip`, **Animation Graph** dạng node, Ragdoll |
| `Navigation/` | Build NavMesh theo tile, `NavMeshWorld` |
| `InputManagement/` | `Input`, Input Action Map (kiểu Unity Input System) |
| `GUI/` | `PaperRenderer` và `PaperInputBridge`: nối thư viện Paper với renderer và input |
| `AssetDatabase.cs` | Quản lý asset theo **GUID** |
| `Tasks/` | `MainThreadContext`: cho phép dùng `async/await` trong code game |
| `XR/` | Phiên làm việc OpenXR, input của kính VR |

---

## 6. Vòng lặp game (`Prowl.Runtime/Game.cs`)

Mỗi frame chạy theo thứ tự sau:

```
Window.Update
 ├─ XR.BeginFrame()
 ├─ Cập nhật input cho UI (Paper)
 ├─ AudioContext.Update()
 ├─ Time.Update()
 ├─ Input.UpdateActions()
 └─ SimulationStep(deltaTime)
     ├─ MainThreadContext.Pump()        ← chạy tiếp các lệnh await
     ├─ AssetDatabase.Pump()            ← nhận asset đã load nền xong
     ├─ Vòng FixedUpdate (bước cố định) ← vật lý
     │    while (accumulator >= FixedDeltaTime) scene.FixedUpdate()
     └─ OnUpdate → Update / LateUpdate

Window.Render
 ├─ OnRender(scene)                     ← vẽ 3D
 ├─ Paper.BeginFrame → OnGui → EndFrame ← vẽ UI
 ├─ XR.EndFrame()
 ├─ EngineObject.ProcessDestroyed()     ← hủy object ở cuối frame
 ├─ Scene.ProcessPendingLoad()          ← đổi scene an toàn
 └─ AssetDatabase.EndFrame()            ← giải phóng asset không còn dùng
```

**Kỹ thuật đáng chú ý:**
- **Fixed timestep có accumulator**: giới hạn `MaxFixedIterations` và bỏ phần tồn đọng để tránh *spiral of death* sau khi bị giật.
- **Hủy trễ (deferred destroy)**: object bị `Destroy()` vẫn dùng được đến hết frame, nên không bị lỗi khi đang ở giữa một callback.
- **Đổi scene trễ**: scene chỉ được thay ở cuối frame.
- Dùng `InvariantCulture` để đọc số thực giống nhau trên mọi máy (tránh lỗi dấu phẩy thập phân).

---

## 7. Mô hình GameObject / Component

Mô hình giống Unity:

```
EngineObject
 ├─ GameObject   (có Transform, chứa danh sách Component)
 ├─ Component    (lớp cơ sở cho mọi script người dùng)
 └─ Scene        (chứa các GameObject gốc)
```

**Các callback vòng đời** (`Prowl.Runtime/GameObject/Component.cs`):

| Nhóm | Callback |
|---|---|
| Vòng đời | `OnAddedToScene`, `OnEnable`, `Start`, `OnDisable`, `OnRemovedFromScene` |
| Mỗi frame | `FixedUpdate`, `Update`, `LateUpdate`, `OnGui(Paper)` |
| Va chạm | `OnCollisionBegin/Stay/End` |
| Trigger | `OnTriggerEnter/Stay/Exit` |
| Character | `OnCharacterEnter/Stay/Exit` |
| Serialize | `OnBeforeSerialize`, `OnAfterDeserialize` |

**Tối ưu bằng `SceneDispatcher`:**
- Engine kiểm tra mỗi component **ghi đè** những callback nào. Component không ghi đè callback nào thì không được đăng ký vào vòng lặp.
- Thứ tự chạy được sắp theo `[ExecutionOrder]`. Các component cùng thứ tự chạy theo thứ tự đăng ký.
- Physics chỉ theo dõi va chạm khi có component lắng nghe sự kiện va chạm.
- Thuộc tính hỗ trợ: `[RequireComponent]`, `[ExecuteAlways]`, `[CreateAssetMenu]`, các thuộc tính hiển thị trong Inspector.

**Ví dụ script người dùng** (trích từ `Samples/Runtime/ControllerShowcase`):

```csharp
/// Xoay một vật thể vật lý quanh trục Y với tốc độ cố định.
public sealed class Spinner : Component
{
    public float DegreesPerSecond = 45f;

    private Rigidbody3D _body = null!;

    public override void OnEnable() => _body = GetComponent<Rigidbody3D>()!;

    public override void FixedUpdate()
        => _body.AngularVelocity = _body.Rotation * Float3.UnitY * (DegreesPerSecond * MathF.PI / 180f);
}
```

---

## 8. Hệ thống Asset

### Phía Runtime (`AssetDatabase.cs`, `AssetBackend.cs`)
- Mọi asset được định danh bằng **GUID**, nên tham chiếu không bị hỏng khi đổi tên hoặc di chuyển file.
- `AssetRef<T>` là tham chiếu tới asset qua GUID.
- **Backend có thể thay thế** (abstract `AssetBackend`):
  - `EditorAssetBackend`: đọc từ thư mục dự án và import khi cần
  - `PlayerAssetBackend`: đọc từ gói asset của bản build
  - `MemoryAssetBackend`: chỉ nằm trong bộ nhớ, dùng cho test
- Có thể **mount** thêm nguồn asset như content pack hoặc mod.
- Asset được **load nền** rồi gộp vào scene ở đầu frame qua `Pump()`.

### Phía Editor (`Prowl.Editor/AssetsDatabase/`)

```
File trong Assets/ ──► AssetWatcher (FileSystemWatcher)
                          │ phát hiện thêm/sửa/xóa/đổi tên
                          ▼
                    EditorAssetBackend
                          │ đọc/ghi file .meta (GUID + cài đặt importer)
                          ▼
                    Importer theo loại file
     (Texture, Mesh, Model, Audio, Shader, Material, Scene, Prefab, Script…)
                          │
                          ▼
                    Cache đã import + DependencyGraph
                          │ asset phụ thuộc thay đổi thì import lại theo dây chuyền
                          ▼
                    ThumbnailGenerator (ảnh xem trước)
```

- **`MetaFile`**: lưu GUID, tên importer, phiên bản importer và cài đặt import. File được ghi theo kiểu **atomic** (ghi ra file tạm rồi đổi tên) để không bị hỏng khi crash.
- **`DependencyGraph`**: đồ thị phụ thuộc hai chiều, dùng để biết asset nào phải import lại.
- Bake lightmap (`Lightmapping/`) và NavMesh (`Navigation/`) cũng nằm trong pipeline này.

---

## 9. Editor: phần logic

| Thư mục | Chức năng |
|---|---|
| `Program.cs` | Điểm vào. Đọc tham số `--project`, `--buildmode`, `--output`, `--xr` |
| `Core/EditorApplication.cs` | Kế thừa `Game`. Quản lý cửa sổ, panel, play mode, pipeline asset |
| `Core/Undo.cs` | Undo/Redo theo **Command pattern**. Gộp các thay đổi thuộc tính liên tiếp; mặc định lưu 100 bước |
| `Core/Selection.cs` | Danh sách object đang chọn và sự kiện khi đổi lựa chọn |
| `Core/MenuSystem.cs`, `ShortcutManager.cs` | Menu khai báo bằng attribute, phím tắt |
| `Core/*Clipboard.cs` | Copy/paste GameObject và Component |
| `Projects/` | Mở và lưu project, các Settings (Physics, Audio, Build, Tags & Layers, XR…) |
| `Projects/Scripting/` | Biên dịch script và hot reload (xem mục 11) |
| `Prefabs/` | `PrefabUtility`: tạo prefab, override, apply/revert |
| `Migration/` | Tự sao lưu và nâng cấp project từ bản cũ |
| `Cli/` | `CliServer`: nhận lệnh từ `Prowl.Cli` và chạy trên main thread |
| `EditorRegistries.cs` | Quét assembly để đăng ký importer, editor, menu… bằng **reflection + attribute** |

**Hai chế độ chạy** (`Program.cs`):
1. **Có giao diện**: `new EditorApplication().Run("Prowl Editor", 1920, 1080)`
2. **Build không giao diện** (`--buildmode`): mở project, biên dịch script, import asset rồi build. Phù hợp để chạy trong CI.

---

## 10. Editor: phần UI (`Prowl.Editor/GUI/`)

**Công nghệ:** UI **immediate-mode** dùng **Paper**, với bộ widget **Origami**. Mỗi frame, UI được vẽ lại từ trạng thái hiện tại, không có cây control cố định như WinForms hay WPF.

```csharp
// Ví dụ phong cách immediate-mode (trích từ AnimationGraph editor)
Origami.Button(paper, "ag_stateOpen", "Open Graph", () => EnterState(state))
    .Subtle()
    .Width(UnitValue.Stretch())
    .Show();
```

| Thư mục | Nội dung |
|---|---|
| `Panels/` | Các cửa sổ dock: **Hierarchy, Inspector, Project, Console, SceneView, GameView**, Build Settings, Preferences… (kế thừa `DockPanel`) |
| `Popups/` | Hộp thoại: tạo script, đổi tên, import/export package, tiến trình build |
| `CustomEditors/` | Inspector tùy biến cho từng component và loại asset |
| `PropertyEditors/` | Ô nhập cho từng kiểu dữ liệu: Vector, Color, Gradient, Curve, LayerMask, AssetRef… |
| `SceneView/` | Camera của editor, **gizmo/handle** (di chuyển, xoay, scale), lưới, chọn object |
| `AnimationGraph/`, `GraphEditor/` | Trình chỉnh sửa đồ thị node |
| `ScriptEditor/` | Trình soạn code tích hợp, có tô màu cú pháp |
| `SpriteEditor/`, `AvatarEditor/`, `RagdollGenerator/` | Các công cụ chuyên dụng |
| `Registries/` | Icon, style và hành vi double-click theo từng loại file |
| `../Theming/` | Theme, preset màu, icon, cài đặt người dùng |

**Cơ chế mở rộng:** `CustomEditor` và `PropertyEditor` được tìm qua attribute. Muốn thêm Inspector cho component mới thì chỉ cần viết thêm một lớp, không phải sửa code lõi.

---

## 11. Biên dịch script và Hot Reload

```
Người dùng sửa file .cs
        │
        ▼
AssetWatcher phát hiện thay đổi ──► ScriptAssemblyManager.RequestRecompile()
        │ (chờ debounce để gộp nhiều lần sửa liên tiếp)
        ▼
ScriptCompiler.CompileAll()
  ├─ Sinh file .csproj cho từng Assembly Definition
  ├─ Biên dịch theo thứ tự phụ thuộc bằng Roslyn
  └─ Chạy Prowl.Analyzers để báo lỗi riêng của engine
        │
        ▼
Nạp DLL mới vào AssemblyLoadContext có thể gỡ (collectible)
        │
        ▼
SceneHotReload: chuyển dữ liệu scene từ kiểu cũ sang kiểu mới
        │
        ▼
Game tiếp tục chạy với code mới, không cần khởi động lại editor
```

- **AssemblyLoadContext collectible** cho phép gỡ DLL cũ khỏi bộ nhớ.
- Editor tự sinh file `.sln`/`.csproj` để mở project bằng VS Code hoặc Visual Studio.
- Ký hiệu `PROWL_EDITOR` chỉ có khi biên dịch cho editor, không có trong bản build game.
- **Prowl.Analyzers** gồm 3 analyzer:
  - `BlockingWaitAnalyzer`: cảnh báo khi chờ đồng bộ (`.Wait()`, `.Result`) có thể làm treo main thread
  - `ComponentConstructorAnalyzer`: cấm viết logic trong constructor của Component
  - `EngineObjectNullAnalyzer`: kiểm tra null đúng cách với object của engine

---

## 12. Build và Player

```
Editor (Build Settings)
   │
   ├─ ScriptCompiler.CompileForPlayer(platform, defines)
   ├─ Đóng gói asset đã import (PlayerManifest)
   └─ Chép Players/Desktop cùng thư viện native của nền tảng đích
        │
        ▼
Bản game độc lập ──► PlayerEntryPoint → DesktopPlayer (kế thừa Game)
                      dùng PlayerAssetBackend để đọc asset
```

- `NativeLibraryResolver` chọn đúng file `miniaudioex` / `openxr_loader` theo hệ điều hành và kiến trúc CPU (x64, x86, arm64…).
- Editor và Player dùng chung `Prowl.Runtime`, nên game chạy giống nhau ở cả hai nơi.

---

## 13. Kiểm thử và chất lượng code

- **Prowl.Runtime.Test** (khoảng 85 file): vật lý, CharacterController, asset, component…
- **Prowl.Editor.Test** (khoảng 50 file): AssetDatabase, Undo, Prefab, biên dịch script, hot reload, CLI, migration…
  - `EditorTestHarness` cho phép chạy editor **không giao diện** trong test.
  - `MemoryAssetBackend` giúp test không cần đọc/ghi đĩa.
- `.editorconfig` và `CodeStyleTests` giữ thống nhất phong cách code.
- `CrashReporter` ghi lại lỗi ngay từ dòng đầu tiên của `Main`.

---

## 14. Tổng kết

| Khía cạnh | Lựa chọn thiết kế |
|---|---|
| **Tách lớp** | Runtime độc lập với Editor; Editor kế thừa `Game` |
| **Phân chia UI và logic** | UI nằm trong `Editor/GUI` + `Theming`. Logic nằm ở `Runtime` và `Editor/Core, AssetsDatabase, Projects, Prefabs` |
| **Mở rộng** | Dùng attribute + reflection: importer, inspector, menu, script template |
| **Hiệu năng** | Chỉ dispatch callback mà component có ghi đè; load asset nền; fixed timestep có giới hạn |
| **Trải nghiệm phát triển** | Hot reload script, Undo/Redo, Prefab, CLI, build không giao diện |
| **Đa nền tảng** | .NET 10 + Silk.NET + Vulkan + thư viện native cho từng OS/CPU |

### Cách chạy
```bash
# Mở editor
dotnet run --project Prowl.Editor

# Mở thẳng một project
dotnet run --project Prowl.Editor -- --project "D:/MyGame"

# Build game không mở giao diện
dotnet run --project Prowl.Editor -- --buildmode "D:/MyGame" --output "D:/MyGame/Builds"
```

---

**Cảm ơn đã lắng nghe!**
