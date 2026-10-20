# Daiyu2 技术规范与边界

本文分开记录现有事实与目标方案。接口签名以当前代码为准；当前任务状态见 [current_milestone.md](current_milestone.md)。工作流见 [workflow.md](workflow.md)。

## 1. Daiyu2 已核实基线（BASELINE）

项目根目录是 `D:/Starwishes/Project/Unity/Daiyu2`，不是旧工程的 `UnityProject/` 子目录。版本管理现状与授权见 [当前阶段](current_milestone.md)，恢复要求见工作流。

- Unity 版本事实源：`ProjectSettings/ProjectVersion.txt`；包事实源：`Packages/manifest.json`。
- 当前 `Assets/` 已包含 `Assets/Daiyu/Core`、`Assets/Daiyu/Simulation` 与 `Assets/Daiyu/Gameplay`；核心、粗模型和正式场景宿主已建立，对应 asmdef 均已纳入工程。
- 当前场景保留模板 `Assets/Scenes/SampleScene.unity`、`Assets/Settings/Scenes/URP2DSceneTemplate.unity`，正式场景为 `Assets/Daiyu/Scenes/Daiyu.unity`，唯一启用构建场景由 `ProjectSettings/EditorBuildSettings.asset` 核实。
- 依赖清单已有 URP、2D 工具、Input System、uGUI、Test Framework 和 Unity MCP；不因此假定现有渲染器已支持方案中的透视地形、材质和光照，实施时先核实资产配置。
- 基线快照与执行证据只在当前阶段维护；本节的模板基线不作为后续执行状态。

## 2. 旧项目参考基线（REFERENCE，尚未迁入）

以下是相邻 `D:/Starwishes/Project/Unity/daiyu-unified-player-natural-v6` 已读取的事实，供选择性复用决策使用；不是 Daiyu2 已实现功能。表内路径相对旧项目根目录。

| 模块/入口 | 当前职责与依据 |
|---|---|
| `UnityProject/Assets/Daiyu/Core/Daiyu.Core.asmdef` | 无程序集引用，`noEngineReferences: true` |
| `UnityProject/Assets/Daiyu/Rivers/Daiyu.Rivers.asmdef` | 引用 Core，`noEngineReferences: true` |
| `UnityProject/Assets/Daiyu/Gameplay/Daiyu.Gameplay.asmdef` | 引用 Core、Rivers、URP Runtime 和 Unity UI，尚未独立拆分表现/UI/诊断程序集 |
| `Core/WaterLedger.cs` | 既有账户间转移由 `Transfer` 执行；初始化分配/恢复有各自边界；账户枚举顺序参与兼容 |
| `Rivers/RiverWorld.cs` 及 partial | 世界模拟，使用账本与地图；开河精细几何与库存重分配仍在现有链路 |
| `Gameplay/RiverGame.cs` 及 partial | 权威场景入口 `RiverGame.Player`，世界、规划器、输入、推进、装配与保存/恢复协调 |
| `Gameplay/MenuFlow.cs` 及 partial | 单场景路由，安装独立账本/时钟/表现根的世界；状态含 Home、Preparing、Tutorial、Player、Paused、Ending |
| `Gameplay/RiverSceneView.cs` | 地形、水体、拾取及反馈；已有 `PresentationMap` 与正式地图固定表现路径 |
| `Gameplay/RiverCameraRig.cs` | 透视轨道相机，现有 FOV 为 32°，默认俯角 50°，部分缩放路径调整俯角；相机不改变模拟 |
| `Gameplay/TemporaryLandscapeProps.cs` | 当前树石主要使用三维网格；生态树读取实际覆盖，不是二维插片实现 |
| `Gameplay/PlayerSaveGame.cs` | `CurrentVersion=4`、3 个槽位，保存完整模拟快照；玩家档案位于 `Application.persistentDataPath` |

表内相对模块路径均位于 `UnityProject/Assets/Daiyu/`。Unity 与包版本分别只在 `ProjectSettings/ProjectVersion.txt` 和 `Packages/manifest.json` 维护，不在多份文档复制版本清单。

### 当前依赖方向

```text
Gameplay（Unity 宿主/表现/UI） → Simulation（纯模拟） → Core（账本）
Simulation 无 Unity 依赖；Gameplay 不直接拥有库存，Editor 与工具按实际消费者读取运行模块。
```

### 状态归属

| 状态 | 权威所有者/修改边界 | 表现侧职责 |
|---|---|---|
| 水库存、转移 | `WaterLedger`；世界装配/恢复使用其专用入口 | 读取水况，不另记可写总水量 |
| 地图拓扑、成河、模拟推进 | `RiverWorld` 及对应子系统 | 读取，缓存可重建；规划线不拥有水 |
| 当前世界安装、输入与玩家局协调 | 现有 `RiverGame`；`MenuFlow` 发起路由切换 | 安装/退出时重绑与释放旧资源 |
| 生态/天气/地下水/视野 | 世界内对应实际系统 | 使用真实状态，不用装饰计数伪造生态 |
| 相机 | `RiverCameraRig` | 只影响观察与拾取，不改真实高程 |
| 存档 DTO/文件 | `PlayerSaveGame` / `PlayerSaveGameStore` | 纹理、材质、GPU 数据和可派生缓存不新增入档 |

## 3. 已选方向与待实现契约（PROPOSED）

引入任务：[M1-01](tasks/M1-01_oblique_2d_playable.md)。方向决策：[ADR-001](adr/ADR-001_oblique_2d_presentation.md)、[ADR-002](adr/ADR-002_coarse_water_and_new_saves.md)。下面是待实现契约，不是本工程已有 API；开工授权只查当前阶段。预计消费者：模拟、世界表现、输入、UI 与保存恢复。

- 选择性复用核心与玩家流程，在 Daiyu2 保持单向依赖；不复制旧 Library、Packages 或 ProjectSettings。
- 简化三维地势 + 二维景物 + 清爽卡通大色块/平面水带；显示、拾取与景物贴地共用同一静态表现地形口径。
- 相机为固定方向真透视，平移/缩放不改变朝向；玩家局无旋转，菜单展示也无自动环绕。旧相机 FOV/俯角可作初始参数，不是不可调硬约束。
- 旧表现只作布局/拾取参考，不整体迁入动态水面、精细开河几何与旧诊断装配。新工程只保留一套权威世界/宿主。
- 渲染允许分块/合批/低频更新和脏标记；选取、规划预览和操作反馈保持实时响应。不要把所有表现刷新统一降频导致输入滞后。
- 二维插片深度/遮挡处理只影响表现；水体拾取以玩家实际看到的范围为依据，避免装饰 Collider 抢占地形/水层射线。
- 保留既有玩家入口，默认新玩家流程也应用新画风；命令行只作诊断和复现入口，不作为正式交付唯一入口。
- 允许新粗模型与新保存契约，不保持旧精细算法/模拟步长/v4 布局等价；复用账本时保持其整数单位和转移约束，生态/天气参数按实际时间单位适配。

### 粗模型薄契约

| 边界 | 待实现约束 |
|---|---|
| 权威状态 | 单一粗粒度世界持有账本、河网连接、地块状态、模拟时钟、生态/天气/地下水/视野；旧 `RiverWorld` 不作为并行权威运行 |
| 静态地图 | 生成后地形高程、邻接与粗容量保持静态；河段可启用连接，不因开河重切地形/三角分区或重分配现有账户 |
| 转移 | 固定业务 Tick，根据旧步快照的水头/方向/连接和来源/接收容量生成转移请求，按稳定顺序/预算结算；来源不负、接收不超容，拒绝量留来源 |
| 引水 | 从合法实际含水顶点规划连续路径，启用连接后实际水逐步抵达；路径/动画不拥有库存，不允许提交即满河 |
| 雨与生态 | 催雨扣当地大气，天气/土壤/地下/海洋经同一账本；视野与生态由实际水况驱动，不用装饰或另一个隐藏世界造结果 |
| 调度 | 物理/生态/天气可以分频，但每个速率有明确单位与余量归属；默认频率属实现参数，暂停、重开和恢复保持一致语义 |
| 完整流程 | 单一宿主协调新局、教程、玩家、暂停、返回与恢复；UI/输入进入同一命令边界，表现只读状态 |
| 保存 | 新格式显式记录格式/模型/生成版本、配置、种子、拓扑/连接、库存、子系统状态、时钟/速率余量、视野和进度；可派生缓存重建 |
| 隔离/失败 | 使用独立 `Daiyu2` 子目录与 `Daiyu2.` 偏好键，不自动读取旧 v4 路径；候选恢复验证成功后再切局，失败不覆盖当前局/原档 |

这些约束批准后即为实施边界。具体类名/签名在 A 核心批次按实际依赖收敛，B 完成宿主/交互/最小快照接线并记录，不预先声称存在；多个批次需要的真实接口稳定后再提升为长期契约。

稳定条件：正式玩家链路已接入，并有 [快速交付检查](workflow.md#fast-delivery) 及相关定向检查证据。届时按实际实现更新本节并转 `STABLE`，未实现部分仍标 `PROPOSED`。

## 4. 契约变更门槛

M1 范围包括粗模型、适配调度、独立新档和固定相机；获批后可在上述薄契约内实施。额外突破有限水量/容量/引水/生态边界、引入第二权威世界、核心反向依赖 Unity、删减完整基础流程、修改旧数据或扩大依赖/发布范围时，补充计划并重新确认。

新的表现缓存、贴图、配色、景物密度和同一状态源的内部实现调整，在已批准 M1 范围内按 L2 执行；新增第三方包、付费资源、许可证义务或外部服务先确认。源资产与 `.meta` 保持 GUID，生成物使用现有忽略目录。
