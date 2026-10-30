# dsh-ue-asset · 技术方案（定稿）

> 本地 UE4/UE5 资产工具：一个 C# 核心 + CLI，一个 DSH 插件把能力接到模型和界面上。全程离线，不需要 FModel 在跑。

---

## 0. 定位：不走 MCP

| | 选择 | 理由 |
|---|---|---|
| 协议 | **私有 JSON-lines RPC**（`ueasset serve`） | 只有 8 个方法，比 MCP 简单得多；省掉 C# MCP SDK + DSH mcp-client 两层版本依赖 |
| 模型接口 | **DSH 原生工具**（`ctx.tools.register`） | 工具名干净（`uea_open_game`）、能 `deferLoading`、能定制 UI 卡片、能挂 `guard` |
| 人机接口 | **CLI**（`ueasset`） | 调 provider 装配的坑只有 CLI 能快速迭代；批量导出不该过模型 |
| 界面 | **右栏 tab + 设置页** | 见 [UI-DESIGN.md](UI-DESIGN.md) |

放弃 MCP 的代价：只有 DSH 能用。如果将来要给 Claude Desktop / Cursor 用，在 serve 上套一个 MCP adapter（约 20 行）即可，Core 一行不动。

### 不做什么

| 不做 | 原因 |
|---|---|
| 启动 / 控制 FModel GUI | 解析能力在 CUE4Parse 库里，不在 WPF 壳里 |
| 魔改引擎的专属适配 | 只要标准 AES + Usmap，额外加密不管 |
| 联网下载 keys / mappings / oodle | 运行时零网络请求 |
| 多引擎（Unity / Godot / RE） | 抽象边界是"虚幻引擎资产" |
| repack / 写回游戏包 | 只读 |
| 跨平台 | Win64 |

---

## 1. 技术选型（已核实）

| 项 | 选择 | 核实 |
|---|---|---|
| 运行时 | .NET 10 | 本机 SDK `10.0.101` ✓ |
| 解析 | `CUE4Parse` **1.2.2.202610** | target net10.0 ✓ |
| 转换 | `CUE4Parse-Conversion` **1.2.2.202610** | 含 SharpGLTF / SkiaSharp ✓ |
| 序列化 | `Newtonsoft.Json` | CUE4Parse 的 `UObject` 挂的是 Newtonsoft converter，换 STJ 会丢字段 |
| 日志 | Serilog → **stderr** | stdout 是 RPC 通道，一个 `Console.WriteLine` 就断连 |
| 前端 | React（运行时 `require('react')`）+ three.js | DSH Client 插件形态 |

CUE4Parse 是 C# 库——这是选 .NET 的唯一原因，不是偏好。

---

## 2. 工程结构

```
dsh-ue-asset/
├── DshUeAsset.slnx
├── Directory.Packages.props            # 锁死 CUE4Parse 1.2.2.202610
├── src/
│   ├── DshUeAsset.Core/                # 无传输依赖，可单测
│   │   ├── Config/AppConfig.cs         # profiles + 全局
│   │   ├── Config/ConfigStore.cs       # camelCase 落盘 + 原子替换
│   │   ├── Sessions/GameSession.cs     # provider + 每会话锁 + 元信息
│   │   ├── Sessions/SessionManager.cs
│   │   ├── Sessions/ProviderFactory.cs # ★ 唯一有难度的文件
│   │   ├── Assets/AssetQuery.cs        # path / ext 检索
│   │   ├── Assets/ObjectReader.cs      # LoadPackage + export 选择 + JSON
│   │   ├── Assets/TexturePreview.cs    # TextureDecoder → PNG
│   │   ├── Export/ExportService.cs     # ExportSession 调度
│   │   ├── Export/JobQueue.cs
│   │   ├── Fmodel/FmodelImporter.cs    # 读 FModel AppSettings.json
│   │   ├── Services/AssetService.cs    # CLI 与 serve 共用的门面
│   │   ├── Models/                     # DTO
│   │   └── Safety/PathPolicy.cs
│   ├── DshUeAsset.Cli/                 # ueasset.exe
│   │   ├── Program.cs                  # 子命令 + doctor
│   │   └── Rpc/JsonLineServer.cs       # serve 模式（JSON-lines）
│   └── ...
├── plugin/                             # DSH 插件（一个 bundle）
│   ├── package.json                    # dsh.bundle.patch + dsh.client
│   ├── cordis.patch.yml                # 插入 Host 行
│   ├── index.js                        # Host：8 工具 + /ue-asset/api/*
│   ├── client.js                       # Client：右栏 tab / 设置页 / 卡片
│   ├── locale/{zh,en}.json
│   └── icon.svg
├── tests/DshUeAsset.Tests/             # 17 个单测
├── scripts/
│   ├── New-Config.ps1                  # 从 FModel 生成配置
│   ├── publish.ps1                     # 单文件发布
│   ├── install-plugin.ps1              # 写入 Electron 托管 profile
│   ├── plugin-smoke.mjs                # 无 DSH 的 Host 端到端冒烟
│   └── client-smoke.mjs                # 无浏览器的 Client 注册冒烟
└── docs/{PLAN,UI-DESIGN}.md
```

**铁律**：`Core` 不引用传输层，`Cli` 不引用插件。三块共享 Core，互不知道对方存在。

---

## 3. 配置契约

`%LOCALAPPDATA%\dsh-ue-asset\config.json`——**CLI 与插件共用同一份**。

```json
{
  "profiles": {
    "wukong": {
      "name": "悟空",
      "directory": "E:/SteamLibrary/steamapps/common/BlackMythWukong/b1/Content/Paks",
      "extraDirectories": [],
      "game": "GAME_UE5_0",
      "platform": "DesktopMobile",
      "aesKey": "0xA896...",
      "dynamicKeys": [],
      "usmap": "E:/BMW_Mod/Mappings.usmap"
    }
  },
  "outputRoot": "E:/WorkSpace/Tools/dsh-ue-asset/Exports",
  "oodleLibrary": "E:/BMW_Mod/Output/.data/oo2core_9_win64.dll",
  "defaultMeshFormat": "UEFormat",
  "limits": {
    "maxSessions": 4,
    "maxJobs": 16,
    "maxBatchAssets": 200,
    "maxReadBytes": 67108864,
    "maxResponseChars": 80000
  },
  "uiState": { "lastProfile": "wukong", "expandedPaths": [] }
}
```

| 字段 | 说明 |
|---|---|
| `directory` | **Paks 目录**绝对路径，不是游戏根目录 |
| `extraDirectories` | 插件 pak 等附加目录，对应 `DefaultFileProvider(extraDirectories)` |
| `game` | `EGame` 枚举名；黑神话 `UeVersion = 83886082` |
| `aesKey` | 32 字节 hex，`0x` 可省；全零 GUID = 主密钥 |
| `dynamicKeys` | `[{guid, key}]`，默认空 |
| `usmap` | `.usmap` 绝对路径；缺失则 unversioned 属性读不出（降级为 raw） |
| `defaultMeshFormat` | `UEFormat`（默认）/ `Gltf2` / `USD`。**只影响导出，不影响预览** |

密钥只存在这里，**永不经工具参数传递**（DSH 会留存工具参数进对话历史）。

---

## 4. 核心实现

### 4.1 装配 Provider —— 唯一有难度的文件

> 以下签名已在 `CUE4Parse 1.2.2.202610` 上**反射核实**。网上旧教程的
> `(dir, opt, isCaseInsensitive:true, versions)` 重载已 `[Obsolete]`，照旧写编译不过。

```csharp
OodleHelper.Initialize(oodlePath);          // 不是 Oodle.Initialize

var versions = new VersionContainer(
    game: Enum.Parse<EGame>(profile.Game),   // GAME_BlackMythWukong = 83886082 原生存在
    platform: Enum.Parse<ETexturePlatform>(profile.Platform));

var provider = new DefaultFileProvider(
    new DirectoryInfo(profile.Directory),
    profile.ExtraDirectories.Select(d => new DirectoryInfo(d)).ToArray(),
    SearchOption.TopDirectoryOnly,
    versions,
    StringComparer.OrdinalIgnoreCase);

if (profile.Usmap is not null)
    provider.MappingsContainer = new FileUsmapTypeMappingsProvider(profile.Usmap);

provider.Initialize();
provider.SubmitKey(new FGuid(), new FAesKey(aesKey));   // 提交时就会挂载读者
provider.Mount();
provider.PostMount();

var mounted = provider.MountedVfs.Count;                        // ★ 不是 Mount() 的返回值
var missing = ((AbstractVfsFileProvider)provider).RequiredKeys; // 不是 UnloadedVfs.RequiredKeys
```

三条硬性要求：

1. **成功判据是 `MountedVfs.Count`**。`SubmitKey` 内部会 `TryMountReader`，所以随后的
   `Mount()` 返回 0 是正常路径；拿它判断会误报 needs_key。本机实测提交 21 个密钥后
   `Mount()==0`，但 `MountedVfs==21`、`Files==502366`。
2. **缺失密钥不中止已挂载的部分**。只有 `mounted == 0` 才抛 `needs_key`；否则把 missing
   GUID 放进会话，UI 用可关闭黄条提示（UI-DESIGN §6）。
3. **usmap 用 `FileUsmapTypeMappingsProvider(path)`**。缺 usmap 时 unversioned 属性会抛
   `parser_error`，但路径浏览与搜索仍然可用。

### 4.2 读资产

```csharp
var objs = provider.LoadAllObjects(virtualPath);      // UObject[]
var one  = provider.LoadObject("b1/.../Foo_C.ObjName");
var json = JsonConvert.SerializeObject(objs, Formatting.Indented);
```

大对象先给元信息，属性异步填；深查询走 JSON pointer，不整包 dump。

### 4.3 纹理

```csharp
var decoded = TextureDecoder.Decode(tex, ETexturePlatform.DesktopMobile);
var png = TextureEncoder.Encode(decoded, ETextureFormat.Png, saveHdrAsHdr: false, out var ext);
```

`TextureDecoder.Decode(tex, maxSize, platform)` 在 `maxSize` 大于贴图本身时会返回 null，
必须回退到第一个 mip 的 `Decode(tex, platform)`。

另有一类存根贴图（本机 BossRush UI 图即如此）：`SizeX=0 / PixelFormat=Unknown / 无
PlatformData`，数据本身不可解码。这属于资产内容，不是管线问题，报 `parser_error` 即可。

### 4.4 导出与预览是两条路

```csharp
// 导出（用户可见，落在 outputRoot）
var session = new ExportSession { MaxDegreeOfParallelism = 1 };  // provider 非线程安全
session.Add(uobject);                                            // 依类型自动选 exporter

var results = await session.RunAsync(
    jobDirectory,
    new ExportOptions(meshFormat: EMeshFormat.UEFormat, textureFormat: ETextureFormat.Png, exportMaterials: true));
// results: ExportResult { Success, ObjectPath, DiskFilePaths, Error }
```

**新版 API 已经解决了路径越界问题。** 旧文档里的 `Exporter.TryWriteToDir` 在
1.2.2.202610 已不存在；`ExportSession.RunAsync(baseDirectory, ...)` 会在内部用
`Path.Combine(baseDirectory, savePath)` 解析每个产物，**包括被连带导出的依赖资产**。
因此 job 目录既是沙箱也是目的地，不需要 staging → 校验 → 再移动那一套。

仍然保留一次 `PathPolicy.EnsureWithin(jobDirectory, diskPath)` 作为纵深防御。

**预览不走上面这条**。面板里的 3D 视口固定用 glTF（three.js 只能读 glTF），导到内部
缓存目录，用户不可见。UEFormat 是二进制资产格式，`GLTFLoader` 读不了。

### 4.5 模型预览与 AI 返图

面板：`MeshPreview` 把 StaticMesh / SkeletalMesh 用 `ExportSession` 以 **Gltf2** 格式
导成自包含的 `.glb`，落在内部缓存 `%LOCALAPPDATA%\dsh-ue-asset\cache\<hash>\`。
Host 用 `/ue-asset/cache/*` 前缀路由把缓存目录只读暴露给前端；three.js r128
（`plugin/vendor/`，不联网）用 `GLTFLoader` + `OrbitControls` 渲染，包围盒自动取景，
有动画轨道时列出并可播放。

骨骼网格按绑定姿势显示；**CUE4Parse 的 Gltf2 导出器目前不带 AnimSequence**，所以
「动画」下拉只在 GLB 自带 animations 时出现。需要动画播放时，后续要么用 UEFormat +
离线转换，要么单独导出动画并手动挂到同一 skeleton 上。

缓存是**可丢弃**的：键包含引擎版本、虚拟路径和 pak 内文件大小，游戏打补丁后自动失效。

AI 返图：**离屏 canvas 渲染，不打开面板**。

```js
const canvas = document.createElement('canvas');   // 不 append
const renderer = new THREE.WebGLRenderer({ canvas, preserveDrawingBuffer: true });
renderer.render(scene, camera);
canvas.toBlob(/* → PNG */);
```

真正的边界是**整个 DSH 页面是否存活**，不是面板开没开。页面关闭时降级返回元信息 + `image_omitted: "renderer_unavailable"`。

---

## 5. 工具面：8 个

注册为 DSH 原生工具（`ctx.tools.register`），不是 MCP。

| 工具 | 参数 | 说明 |
|---|---|---|
| `uea_open_game` | `profile` 或 `directory` | 返回 `sessionId` + 诊断（mounted / missing / 样本路径 / 实际版本） |
| `uea_sessions` | `action: list\|close` | 会话状态、释放句柄 |
| `uea_tree` | `path` `limit` | 一层目录与文件 |
| `uea_find` | `query` `prefix` `ext` `limit` | 全局路径搜索（内存字典过滤，毫秒级） |
| `uea_inspect` | `path` 或 `object`；`section` `pointer` `limit` | 元信息 / exports / imports / 属性 |
| `uea_preview` | `object` `maxSize` | **按类型分流**：纹理 → decode PNG；网格 → 客户端离屏渲染 PNG；其它 → 属性摘要 |
| `uea_export` | `paths[]` | 入队，返回 `jobId`；格式取全局默认 |
| `uea_job` | `action: status\|results\|cancel\|list` | job 全生命周期 |

约定写进工具描述：

- 列表统一 `{items, total, offset, nextOffset}`，`nextOffset=null` 到底
- 错误分型：`invalid_argument` / `not_found` / `needs_key` / `unsupported` / `parser_error` / `too_large` / `path_denied` / `renderer_unavailable`
- `needs_key` 必须回带 missing GUID
- **任何工具都不返回密钥**，日志与异常里也不出现明文
- `deferLoading: true`（除 `uea_open_game`），省初始上下文

---

## 6. 会话与并发

```
SessionManager (max 4)
  └── GameSession
        ├── provider           非线程安全
        ├── SemaphoreSlim(1)   ★ 每会话一把锁，解析串行
        └── JobQueue           会话内串行，会话间并行
```

1. **provider 绝不并发调用**。`LoadPackage` 会改内部字典，并发读会偶发 `KeyNotFoundException`，极难复现。
2. **job 状态查询不抢会话锁**，否则长导出期间查不到进度。
3. **`close` 遇到运行中 job 直接拒绝**。
4. **同一目录被多个对话打开时复用同一个 provider**，不重复挂载。

---

## 7. 安全

| 面 | 措施 |
|---|---|
| 读 | 只允许 `profiles[*].directory` 与其 `extraDirectories` 之下；`Path.GetFullPath` 归一后比对；拒绝 symlink/junction |
| 密钥 | 只在 config；不进工具参数、不进返回、不进日志 |
| 写 | 只写 `outputRoot`；每 job 一个 GUID 目录；导出后校验产物路径 |
| 资源 | `limits` 五项全部生效 |
| 网络 | 运行时零出网 |

---

## 8. DSH 接入

一个 bundle，三段：`cordis.patch.yml` 插 Host 行 + `index.js`（Host）+ `client.js`（Client）。

```yaml
- insert:
    - id: ue-asset
      name: 'dsh-ue-asset'
      config:
        configPath: C:/Users/Sumcam/AppData/Local/dsh-ue-asset/config.json
        exePath: C:/Users/Sumcam/AppData/Local/dsh-ue-asset/ueasset.exe
```

Host 侧（`index.js`）职责：

| 职责 | 实现 |
|---|---|
| 管 serve 进程 | `ctx.subprocess.spawn('ueasset serve --config ...')`，崩溃重启 |
| 注册 8 个工具 | `ctx.tools.register(...)`，转发到 serve |
| 暴露 HTTP API | `ctx.webServer.register` → `/ue-asset/api/*`（纯 JS 插件没有 `@Remote` 装饰器入口） |
| 纹理出图 | `ctx.attachments.saveImage` → 工具结果里的 image block（已实现） |
| 模型出图 | 收到 `uea_preview` 的网格请求 → 通知 Client 离屏渲染 → 拿 PNG（M5） |

Client 侧（`client.js`）职责：

| 职责 | Slot |
|---|---|
| 右栏 tab | `sidebarRightTabs.register` + `sidebar.right.pane.tab` |
| 设置页 | `settings.section` |
| 对话卡片 | `tool.call.toolview` |
| 进度条 | `shell.bottom` |
| 离屏渲染服务 | 无 slot，监听 Host 请求 |

发布：`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`。

---

## 9. 里程碑

| 阶段 | 内容 | 验收 | 状态 |
|---|---|---|---|
| **M0** | sln + Core 骨架 + CLI `open` | `ueasset open b1` 打印 `mounted=21 missing=0` | ✅ |
| **M1** | CLI `tree` / `find` / `inspect` | 能列 60 个一级目录，能 dump Texture2D/Material 属性 | ✅ |
| **M2** | `preview`（纹理）+ `export` + `job` + `serve` | 纹理出 PNG；导出 PNG 产物落在 job 目录 | ✅ |
| **M3** | DSH 插件：右栏 tab + 树 + 搜索 + 设置页 | Host 8 工具 smoke 全通；Client 已挂 5 个 slot | ✅ |
| **M4** | 3D 视口 + 动画 | 骨骼网格能旋转、自动取景；动画轨道存在时可播 | 🟡 视口已完成，动画导出受上游限制 |
| **M5** | AI 返图（离屏渲染往返） | AI 调 `uea_preview` 拿到模型 PNG | ⬜ |

**M1 是分水岭**：加密 PAK 挂不上，后面全是空谈。M1 三个验证点按顺序：Oodle 初始化 → 密钥提交后 `missing` 为空 → `provider.Files` 能列路径。

**M4 开工第一件事**：拿骨骼网格试导 glTF，验证动画轨道与贴图是否真的出来。导不出来，M4/M5 方案要改。

---

## 10. 风险

| 风险 | 概率 | 对策 |
|---|---|---|
| Oodle 初始化失败 | 高 | 显式 `OodleHelper.Initialize(dll)`；本机已验证 `oo2core_9_win64.dll` |
| ~~`UsmapContainer` 签名不符~~ | 已消除 | 实际类型是 `FileUsmapTypeMappingsProvider(path)` |
| ~~`EGame` 与 83886082 对不上~~ | 已消除 | `EGame.GAME_BlackMythWukong = 83886082` 原生存在 |
| glTF 导出不带动画/贴图 | 中 | M4 首验，导不出则改用 UEFormat 离线转换或放弃动画 |
| ~~导出依赖写越界~~ | 已消除 | `ExportSession.RunAsync(baseDir)` 天然约束 |
| 大纹理 Decode OOM | 中 | `maxSize` 硬上限 4096；mip 回退 |
| stdout 被日志污染 | 低但致命 | 全局限定 stderr；CI 加"stdout 必须为空"冒烟测试 |
| Host→Client 渲染请求通道无现成 API | 中 | 当前用 `ctx.webServer.register` 的 HTTP 通道；离屏渲染仍待 M5 验证 |

---

## 11. 与 UI 的分工

本文只管"能力怎么实现"。界面怎么排、放哪、交互细节，见 [UI-DESIGN.md](UI-DESIGN.md)。