# Daiyu2 项目指引

## 工作区与范围

- 本项目工作目录：`D:/Starwishes/Project/Unity/Daiyu2`，Unity 工程直接位于根目录。
- 相邻 `daiyu-unified-player-natural-v6` 为只读复用来源；设计方向见游戏设计，实施边界见技术规范与任务计划。
- 按当前阶段中的授权执行。工作流/版本管理授权不等于游戏开发授权，游戏开发授权不等于发布授权。
- 当前进度、批准范围和下一步只维护在 [当前阶段](docs/plans/current_milestone.md)。

## 事实源

| 文档 | 职责 |
|---|---|
| [游戏设计](docs/game-design.md) | 已确认玩法与画面方向 |
| [斜向 2D 可玩版方案](docs/design/oblique-2d-playable.md) | 已选设计方向与待批准的开工细节 |
| [工作流](docs/plans/workflow.md) | 分级、批准、执行、验证与交付 |
| [技术规范](docs/plans/technical_spec.md) | Daiyu2 现状、旧项目参考与待实现契约 |
| [路线图](docs/plans/development_roadmap.md) | 阶段目标和依赖，不维护实时进度 |
| [当前阶段](docs/plans/current_milestone.md) | 唯一的任务状态、批准凭据与完成证据 |
| [决策记录](docs/plans/decision_log.md) / `docs/plans/adr/` | 已确认决定与架构决策草案/结论 |
| `ProjectSettings/ProjectVersion.txt` / `Packages/manifest.json` | Unity 与包版本事实源 |

## 最低执行要求

1. 开始先读当前阶段与相关契约，核实实际文件和版本控制状态。旧项目的代码、构建入口、场景与通过记录不能当作本项目现状。
2. L1 小改轻量执行；L2 在已批准边界内按功能批次交付；L3 涉架构、状态归属、账本、模拟时序或存档的变更须先计划并经用户批准。
3. 不复制旧工程的 Library、缓存或整套 Packages/ProjectSettings；代码/资产迁入先确认范围、依赖与许可，保留源资源 GUID 与 meta。
4. 核心模拟保持无 Unity 依赖，表现不记账；粗模型保留有限水量/容量/引水/生态规则，新工程存档与教程键隔离，不要求旧 v4 直接兼容，禁止用旧真实玩家数据做调试准备。
5. Unity 操作先查 custom tools、instances、实例真实路径和 editor state；连接实例不等于目标工程，确认就绪后再修改。
6. 不擅自初始化 Git、提交、推送、合并、发布、安装依赖或覆盖配置。保留用户与其他任务已有工作。

## 轻量开发与快速交付

- 相关低风险工作组成可运行功能批次，作者先自查；不为每个小项创建任务文档、独立审查或重复构建。
- 独立审查集中用于架构、公共契约、迁移/恢复、跨模块整合和最终交付；不依赖 WarBrain 特定 Agent 名称或配置。工具不可用则记录限制，不虚构结论。
- 选择最小且充分的检查，并说明对应风险；未变且已覆盖的检查不重复，不对常规源码做哈希。
- 可玩版直接集成正式链路，优先最小纵向闭环；交付检查与跳过项统一见 [快速交付策略](docs/plans/workflow.md#fast-delivery)，不在入口重复维护清单。
- 游戏迁入前建立可恢复的版本基线；Git 授权、检查点及恢复规则见工作流。
- 只读文档迁移只做文件/链接/差异检查，不启动 Unity 或触碰玩家数据。
- 报告说明交付路径、实际检查、跳过项和剩余风险；存在阻断基本操作的错误或卡顿时，不称已交付可玩版。
