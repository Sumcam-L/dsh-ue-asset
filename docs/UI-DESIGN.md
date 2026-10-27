# dsh-ue-asset · UI 设计（定稿）

> 两个场景：**A** 聊天里模型找到的东西要能看能点；**B** 不想跟模型说话时，自己翻资产、看图、导文件。

---

## 0. 放在哪：右栏的一个 tab

主入口是右侧栏，不是中央面板。

```
┌──────────────┬────────────────────────────────┬─────────────────────┐
│  sidebar     │  main                          │  rightbar           │
│              │  conversation                  │  ┌───────────────┐  │
│              │                                │  │ UE 资产   ⛶ × │  │
│              │                                │  │               │  │
│              │                                │  └───────────────┘  │
├──────────────┴────────────────────────────────┴─────────────────────┤
│  shell.bottom  ← 导出队列进度条                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### 0.1 接入代码

右栏有一套 tab 注册服务（三个已装插件在用：chatgpt-subscription / wallpaper-engine / image-gen）：

```js
// 两个可选 Client 服务，必须 ctx.get 短轮询等它们就绪
const tabs = ctx.get('sidebarRightTabs');   // register(def)
const ctrl = ctx.get('sidebarRight');       // openTab(kind) / closeTab(kind)

tabs.register({
  id: 'ue-assets',
  kind: 'ue-assets',
  keepMounted: true,
  title: () => t('UE 资产'),
  guide: [{
    id: 'browser',
    order: 120,
    title: () => t('UE 资产'),
    description: () => t('浏览、预览、导出虚幻引擎资产'),
    icon: renderIcon,
  }],
});

ctx.slots.register(
  { name: 'sidebar.right.pane.tab', key: 'ue-assets' },
  UeAssetPanel,
);

ctrl.openTab('ue-assets');   // 由卡片 / 快捷键触发
```

`guide` 那条就是"开始"面板里的卡片，**免费的发现入口**。

实际实现比上面多一层防御：`sidebarRightTabs` / `sidebarRight` 是可选服务，且启动时可能还没就绪。
所以挂在 `ctx.slots.inject("sidebar.right.pane.tab", ...)` 里，用 250ms 短轮询 `ctx.get(...)`
等两个服务，拿到后再 `register`。注册被拒（id 撞车等）就安静回退，不 park 插件。

### 0.2 降级

`sidebarRightTabs` / `sidebarRight` 是可选服务。拿不到（旧版宿主）就退回 `shell.overlay` 浮动面板，功能不减，只是没有宿主的 tab 外观、⛶、拖动。

### 0.3 其它落点

| 层 | Slot / API | 承担 |
|---|---|---|
| 进度条 | `shell.bottom`（当前无 occupant） | 导出队列，跨面板常驻 |
| 对话卡片 | `tool.call.toolview` key=`ue_*` | 工具结果卡 |
| 设置页 | `settings.section`，`order: 30` | 游戏 / 全局配置 |
| 唤起 | `ctrl.openTab('ue-assets')` | 卡片、快捷键、guide 共用 |

---

## 1. 右栏结构：浏览 / 预览 Tab 互斥

右栏约 300px 宽，两个 Tab 各自占满，互不挤压。

```
┌──────────────────────────────┐
│ UE 资产              ⛶   ×  │ ← 宿主 chrome
├──────────────────────────────┤
│  浏览  │  预览                │ ← 两枚 Tab
├──────────────────────────────┤
│                              │
│         当前 Tab 内容        │
│                              │
└──────────────────────────────┘
```

- 单击树行 = 选中（高亮），**不切换 Tab**
- 双击 / `Enter` = 切到预览 Tab
- 切回浏览时，选中行仍高亮，滚动位置保持

---

## 2. 浏览 Tab

```
┌──────────────────────────────┐
│  浏览  │  预览                │
├──────────────────────────────┤
│ 🔍 搜索路径…                 │
│ wukong ▾    ● 已挂载 10/12  │
├──────────────────────────────┤
│ ▾ 📁 Characters          128 │
│   ▾ 📁 Boss               24 │
│     🖼 T_Boss_A       2.1M   │
│     🖼 T_Boss_A_N     1.8M   │
│     🎭 SK_Boss       12.4M   │
│   ▸ 📁 Player             16 │
│ ▸ 📁 Effects             412 │
│ ▸ 📁 UI                   86 │
└──────────────────────────────┘
```

### 2.1 结构

| 元素 | 规格 |
|---|---|
| 搜索框 | 常驻顶部，输入即全局搜路径，清空回树 |
| 游戏选择 | 下拉，显示当前 profile + 挂载状态点 |
| 树行高 | 36px（24px 缩略图 + 6px 上下留白） |
| 缩进 | 每级 12px |
| 排序 | 目录优先，名称升序（忽略大小写） |
| 显示范围 | 只显示 `.uasset` / `.umap`，隐藏 `.uexp` `.ubulk` `.sig` `.utoc` |

### 2.2 行内容

```
[24px 缩略图] 名称 ................ 大小
```

- **纹理**：24px 缩略图，懒加载，只解码**可见行 ±5**，按 `(路径, mtime)` 缓存
- **模型**：类型色块 + 🎭 图标（3D 缩略图成本太高，不做）
- **其它**：按类型着色的方块图标
- **长路径**：只显示文件名，hover 显示完整虚拟路径；大小列在窄态可隐藏

### 2.3 搜索

- 输入即搜，全局匹配虚拟路径（`provider.Files` 内存字典过滤，毫秒级）
- 结果替换树为扁平列表，清空立即还原树与滚动位置
- **不做**类名搜索（需 SQLite 索引 + 首次几分钟建设，本期不做）

### 2.4 位置记忆

记住每个 `(profile, 树)` 的滚动位置与展开路径集合，下次打开右栏恢复。存 `config.json` 的 `uiState` 段，与配置分开。

---

## 3. 预览 Tab

按资产类型分流，三种形态。

### 3.1 纹理

```
┌──────────────────────────────┐
│ 浏览  │  预览                │
├──────────────────────────────┤
│ ┌──────────────────────────┐ │
│ │                          │ │
│ │        纹理大图           │ │ 16:10，棋盘格透明底
│ │                          │ │
│ └──────────────────────────┘ │
│ T_Boss_A                     │
│ [Texture2D]  [2048×2048] 2.1M│
│ ── 属性 ──────────────────── │
│ ▾ Properties                 │
│   SizeX          2048        │
│   SizeY          2048        │
│   PixelFormat    BGRA8       │
│ ──────────────────────────── │
│ [ 导出 ]  [ 复制路径 ]       │
└──────────────────────────────┘
```

### 3.2 模型（StaticMesh / SkeletalMesh）

```
├──────────────────────────────┤
│ ┌──────────────────────────┐ │
│ │                          │ │
│ │      [3D canvas]         │ │ three.js 视口
│ │   拖动旋转 · 滚轮缩放      │ │
│ │                          │ │
│ └──────────────────────────┘ │
│ SK_Boss                      │
│ [SkeletalMesh]  12.4M        │
│ 动画 [ Idle        ▾ ] [▶]   │
│ 顶点 24,310 · 材质 3 · 骨骼 68│
│ ── 属性 ──────────────────── │
│ [ 导出 ]  [ 复制路径 ]       │
└──────────────────────────────┘
```

- 视口：`GLTFLoader` 加载（内部 GLB 缓存，**与导出格式无关**；three.js r128 随插件 vendor，不联网）
- 实现状态：✅ 已落地（自动包围盒取景、拖动旋转、滚轮缩放、网格地面；有动画轨道时出现播放控件）
- 相机：`OrbitControls`，默认 3/4 视角，包围盒自动取景
- 动画：列出该网格关联的 `AnimSequence`（通过 `refs` 查），默认**不播**，选一个再点 ▶
- 大模型（>50MB）：先出线框预览，完整加载后替换，避免白屏

### 3.3 其它（蓝图 / 材质 / 数据表 / 音频…）

JSON 属性树 + 元信息，默认展开两层。

```
│ T_Boss_Data                  │
│ [DataTable]  1.2M            │
│ ── 属性 ──────────────────── │
│ ▾ Rows                       │
│   ▸ Row_1                    │
│   ▸ Row_2                    │
│ ▾ Exports (3)                │
│   #0 DataTable               │
│ [ 导出 ]  [ 复制路径 ]       │
```

**异步填充**：先出骨架屏 + 元信息（类名/大小/路径），`LoadObject` 完成后属性进来。大包可能要几百毫秒到几秒，不能整栏等。

---

## 4. 模型渲染与 AI 返图

### 4.1 分层

| 用途 | 格式 | 落地 |
|---|---|---|
| 用户导出给 Blender | **UEFormat**（默认）/ Gltf2 / USD | `outputRoot`，可见 |
| 面板内 3D 预览 | glTF | 内部缓存，用户不可见 |

UEFormat 是二进制资产格式，只有 Blender 的 `io_scene_ueformat` 能读，**three.js 读不了**。所以预览强制走 glTF，与"默认网格格式"设置解耦。

### 4.2 AI 拿渲染图：离屏，不开面板

```js
const canvas = document.createElement('canvas');   // 不 append 到 DOM
const renderer = new THREE.WebGLRenderer({ canvas, preserveDrawingBuffer: true });
renderer.render(scene, camera);
canvas.toBlob(/* → PNG bytes */);
```

- 不需要打开面板、不需要面板可见
- 真正的边界是**整个 DSH 页面是否存活**
- 页面关闭时降级：返回元信息 + `image_omitted: "renderer_unavailable"`

### 4.3 工具行为分流

`preview(path)` 由 Host 判断类型：

```
纹理  → serve decode → PNG → attachment
模型  → Host 请求 Client 离屏渲染 → PNG → attachment
其它  → 返回属性摘要（无图）
```

实现说明：纯 JS Client 插件没有 `@Remote` 装饰器的入口，本版改为 Host 的
`ctx.webServer.register({ kind:'exact', path:'/ue-asset/api/*' })` + Client `fetch`。
数据契约不变，只是传输从 Remote 换成同源 HTTP。纹理图仍由 Host 端 `ctx.attachments.saveImage`
直接返回 attachment，不经过 Client。

⚠ 仍未实现的是 **模型（网格）离屏渲染**：它需要 three.js + WebGL，属于 M4/M5。

---

## 5. 设置页（`settings.section`，order 30）

### 5.1 结构

主从：左列游戏列表，右列当前游戏表单；全局配置在列表下方单独一块。

```
┌ UE 资产 ────────────────────────────────────────────────┐
│  游戏                                        [+ 导入 FModel]│
│  ┌──────────────┬──────────────────────────────────────┐ │
│  │ ● 悟空       │  名称  [ 悟空                        ]│ │
│  │ ○ Wuwa       │  主目录 [ E:/…/b1/Content/Paks ] [选]│ │
│  │              │  附加目录              ▸ 展开 (0)    │ │
│  │ + 新增游戏   │  引擎   [ UE5.0                    ▾]│ │
│  │              │  平台   [ DesktopMobile            ▾]│ │
│  │              │  AES    [ •••••••••••••• ] [👁]       │ │
│  │              │  动态密钥              ▸ 展开 (0)    │ │
│  │              │  Mappings [ E:/…/Mappings.usmap ] [选]│ │
│  │              │                                      │ │
│  │              │  状态 ● 已挂载 10/12 · 缺密钥 1      │ │
│  │              │  [ 保存 ]  [ 立即重载 ]              │ │
│  └──────────────┴──────────────────────────────────────┘ │
│                                                          │
│  全局                                                    │
│  导出目录 [ E:/…/Exports            ] [选择…]            │
│  Oodle    [ E:/…/oo2core_9_win64.dll] [选择…] [检测]     │
│  网格格式 [ UEFormat                ▾]                   │
│  会话上限 [ 4                       ]                    │
│  ▸ 高级（并发任务 16 · 单批 200）                        │
└──────────────────────────────────────────────────────────┘
```

### 5.2 游戏配置字段

| 字段 | 必填 | 说明 |
|---|---|---|
| `name` | 是 | 显示名，可重复 |
| `directory` | 是 | 主 Paks 目录（绝对路径） |
| `extraDirectories[]` | 否 | 附加目录，默认折叠；对应 `DefaultFileProvider(extraDirectories)` |
| `game` | 是 | `EGame` 枚举名，默认 `GAME_UE5_0` |
| `platform` | 否 | 默认 `DesktopMobile` |
| `aesKey` | 否 | 主密钥，掩码显示，保存后只回显"已设置" |
| `dynamicKeys[]` | 否 | `{guid, key}`，默认折叠 |
| `usmap` | 否 | `.usmap` 绝对路径 |

### 5.3 全局字段

平铺 4 项：导出目录、Oodle DLL、网格格式（默认 **UEFormat**）、会话上限。
折叠高级：并发任务、单批资产数。响应字符上限不暴露。

### 5.4 从 FModel 导入

按钮读 `%APPDATA%\FModel\AppSettings.json`：

- 遍历 `PerDirectory`，每个带 `GameDirectory` 的条目生成一个游戏配置
- 取 `UeVersion` 映射 `EGame`，取 `AesKeys.mainKey`
- 只在本地读文件，**不要求 FModel 运行**

### 5.5 生效策略

| 字段类 | 字段 | 生效 |
|---|---|---|
| 软件级 | 导出目录、网格格式、会话上限、高级项 | 保存即生效 |
| 挂载级 | 主目录、附加目录、引擎、平台、AES、动态密钥、usmap | 保存后标 **"下次打开生效"** + 手动「立即重载」（带进度） |

「保存并重新打开」会保存表单并重新 `Initialize` + `Mount` 当前游戏，可能 10–30 秒，**必须用户主动点**。
后端在 `SessionManager.Open` 里比较挂载级字段（目录 / 引擎 / 平台 / AES / 动态密钥 / usmap / 附加目录）：
只要有一项变了就丢弃旧 provider 重新挂载，没变则复用。所以「重新打开」不需要额外接口，走普通 `open` 即可。

---

## 6. 状态机

| 状态 | 触发 | 画面 |
|---|---|---|
| `empty` | 本对话未绑定游戏 | 居中："打开游戏" + **「继续上次：悟空」** + 「选择…」 |
| `opening` | `open_game` 进行中 | 骨架屏 + `正在挂载 3/12…` + 可取消 |
| `needs-key` | `missing.length > 0` | 顶部黄条 + 缺失 GUID + "去设置填 AES"，**可关闭** |
| `ready` | `mounted > 0` | 正常两 Tab |
| `error` | `parser_error` | 红条 + 摘要 + 「详情」折叠堆栈 + 重试 |

`needs-key` 不做弹窗——已挂载的那部分归档能正常浏览，弹窗会挡住本来能用的东西。

---

## 7. 视觉

只用宿主 token：

| 用途 | Token |
|---|---|
| 面板底 | `--dsw-alias-bg-base` |
| 列表行 hover | `--dsw-alias-bg-layer-2` |
| 分隔线 | `--dsw-alias-border-l1` |
| 选中 / 主按钮 | `--dsw-alias-brand-primary` |
| 主文本 | `--dsw-alias-label-primary` |
| 次要文本、大小 | `--dsw-alias-label-secondary` |
| 已挂载 | `--dsw-alias-state-success-primary` |
| 缺密钥 / 部分 | `--dsw-alias-state-warn-primary` |
| 失败 | `--dsw-alias-state-error-primary` |
| 未挂载 | `--dsw-alias-state-idle-primary` |
| 浮层 | `--dsw-alias-bg-overlay` |

类型色只用于图标块，不铺行。缩略图透明底用 8px 棋盘格。行高 36、圆角 6、图标 16。

---

## 8. 交互

| 场景 | 行为 |
|---|---|
| 单击文件行 | 选中并**直接切到预览**（树里点一下就开） |
| 单击目录行 | 只选中；双击目录或点箭头展开 |
| 双击目录 | **展开/收起**，不切预览（目录不是资产） |
| 双击文件 / Enter | 切到预览 Tab |
| `Esc` | 预览 Tab → 浏览 Tab |
| `/` | 聚焦搜索框 |
| `Ctrl+E` | 导出选中资产 |
| 右键 | 复制路径 / 复制对象路径 / 导出 / 在资源管理器显示 |
| 目录展开 | 懒加载，缓存在组件状态 |
| 搜索 | 300ms 防抖，清空回树；结果带父目录；超过 200 条给截断提示 |
| 导出失败 | **弹出提示**（toast），`shell.bottom` 保留记录可点开看 |
| 渲染中 | 面板无感知（离屏渲染） |

---

## 9. 数据流

```
Client 面板 (React, 运行时 require)
   │  fetch('/ue-asset/api/*')
   ▼
Host 插件 (index.js)  ──── preview(模型) 请求 ────► Client 离屏渲染
   │  JSON line over stdin/stdout                    │ PNG
   ▼                                                 ▼
ueasset serve (C#)  ◄────────────────────────── attachment
   │  CUE4Parse provider（每会话一把锁）
   ▼
pak 文件
```

Host 暴露的 Remote：

| 方法 | 用途 |
|---|---|
| `listProfiles()` | 游戏列表 |
| `open(profile)` / `close(sessionId)` | 挂载 / 释放 |
| `browse(sessionId, path)` | 一层目录与文件 |
| `find(sessionId, query)` | 全局路径搜索 |
| `inspect(sessionId, path)` | 元信息 + 属性 |
| `preview(sessionId, object)` | 纹理图 / 模型渲染 / 属性 |
| `export(sessionId, paths[])` → jobId | 入队 |
| `watchJob(jobId)` | 进度流 |

实现说明：见 §4.3。本版用 `/ue-asset/api/*` 同源 HTTP 替代 Remote；Host 与 Client 的数据契约保持一致。

---

## 10. 分期

| 阶段 | 内容 | 判据 |
|---|---|---|
| **M1** | 右栏 tab + 浏览/预览 Tab + 树 + 搜索 + 纹理预览 + 设置页 | ✅ 已实现（浏览树懒加载、300ms 防抖搜索、纹理 PNG 预览、游戏/全局设置） |
| **M2** | 导出按钮 + `shell.bottom` 进度 + 失败 toast + 位置记忆 | 🟡 导出与进度条已实现；失败 toast 已接；树位置记忆待补 |
| **M3** | 3D 视口 + 动画播放 | ⬜ 计划中 |
| **M4** | AI 返图（离屏渲染 + 往返通道） | ⬜ 计划中 |

M3 开工第一件事：拿一个骨骼网格试导 glTF，**验证动画轨道和贴图是否真的出来**。导不出来整个 M3 要改方案。

补充：新版 `ExportSession.RunAsync(baseDir, options)` 已把产物（含依赖）约束在 baseDir 内，
所以 3D 预览的 glTF 缓存可以直接落在内部缓存目录，不需要额外的路径校验。

---

## 11. 不做

| 不做 | 原因 |
|---|---|
| 类名搜索 / SQLite 索引 | 首次几分钟建设，本期用路径搜索 + 缩略图替代 |
| 材质节点图可视化 | 要复现 UE 着色器语义，成本远超收益 |
| 音频播放器 | 浏览器原生 `<audio>` 够用，不自建 |
| repack / 写回游戏包 | 只读定位 |
| 拖拽到外部应用 | 跨进程拖拽需要 OS 级配合 |
| 网格列表行内 3D 缩略图 | 每行一次 glTF 加载，性能不可接受 |