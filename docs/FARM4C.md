# 刷 4C 声骸（Farm 4C Echo）

本功能复刻 ok-ww 的 `FarmEchoTask`（`🌀 Farm 4C Echo in Dungeon/World`）：自动传送到 Boss、进入挑战、战斗、拾取并循环。实现不复用 ok-ww 框架，只按其行为重写。

## 使用前提

- 前台窗口模式（`本地客户端 · 前台` 或 `网页云游戏 · 前台`）。后台窗口模式无法可靠点击菜单，工具会直接拒绝启动。
- 游戏分辨率 1280×720 以上、16:9；HUD 使用默认布局。
- 若游戏以管理员身份运行，工具需要同等权限（与自动战斗一致）。
- 传送依赖 F2 图鉴：请确认 F2 是「索拉指南」快捷键，且已解锁对应 Boss 图鉴。

## 配置

| 项 | 说明 |
|---|---|
| 传送方式 | 不使用（手动站到 Boss 前）/ Boss 挑战 / 周常挑战 / 特异梦魇 |
| Boss 序号 | 图鉴列表中从上到下第几个（1 起） |
| 挑战等级 | 50/60/70/80/90；使用固定行坐标选择（无 OCR） |
| 刷取次数 | 击杀次数上限 |
| 开打前等待（秒） | 每轮开战前的等待，对应 ok-ww 的 `Combat Wait Time` |
| 拾取方式 | 向前走 / 绕圈（无 YOLO 时的替代） |
| 战斗设置 | 战斗前后切治疗 |

## 流程与状态机

`Farm4C` 是纯状态机，输入 `FarmFrame`（HUD 快照 + UI 识别结果 + 画面尺寸），输出 `FarmDecision`（点击/按键/长按/战斗动作）。`FarmLoop` 负责截图、识别、发送输入。

```
WaitGame → Approach → (F) → Enter → Fight → Loot → Claim → Enter …
                ↑                                      ↓
             Teleport ←──────── 传送开启且不在副本内 ──┘
```

- **Approach**：按住 W 前进，每 2.6 秒中键回正视角；发现 F 提示或敌人血条即切换。
- **Enter**：F 打开挑战面板后，按「等级行 → 单人挑战 → 跳过提示 → 开始挑战」的顺序点击。
- **Fight**：完全复用现有 `BattlePlanner`（角色连招、换人、处决、协奏）。
- **Loot**：向前走向声骸/宝箱，遇到 F 提示即拾取（不依赖地面掉落物检测）。
- **Claim**：ESC 关闭领奖弹窗，F 触发重开；副本内直接进入下一轮 Enter，不重复 F2 传送。
- **Teleport**：F2 → 左侧「Boss 图鉴」→ 二级分类 → 列表行 → 传送按钮 → 确认。
- **Revive**：检测 `revive_confirm` 并点击复活；副本内不先退本回血（见差异表）。

关键时序常量：`ApproachTimeout=20s`、`CombatLostMs=2.5s`、`EnterTimeout=8s`、传送单步超时 `12s`（失败 3 次后报错并停止）。

## 与 ok-ww 的逐项对照

| ok-ww | 本项目 | 状态 |
|---|---|---|
| `do_run` 主循环 | `Farm4C.Observe` 状态机 | 已对齐（结构等价） |
| `teleport_to_configured_boss`（F2 图鉴） | `Step.Teleport` 子步骤 | 已实现，简化了滚动条校准 |
| `click_on_book_target` 滚动条定位 | `FarmVision.FindAll("boss_proceed")` | 部分：只直接点选可见行（≤4 行），未实现字号/滚动校准 |
| `walk_after_boss_teleport` | `Approach`（W + 中键） | 已实现 |
| `enter_configured_boss_realm_from_f` | `Enter` 点击序列 | 部分：等级用固定行坐标，ok-ww 用 OCR |
| `combat_once` | `BattlePlanner` | 已对齐（沿用现有战斗实现） |
| `pick_echo` / `walk_find_echo` | `Loot`（F 提示 + 直走/绕圈） | 部分：无 YOLO 声骸检测 |
| `handle_claim_button` | `Claim` 发 ESC | 已实现 |
| `handle_boss_restart_after_treasure` | `Claim → Enter`（副本内） | 已实现 |
| `revive_action` | `Revive` 点击复活 | 部分：未实现「退本回信标回血」与 F2 搜无冠者 |
| `in_realm()` | `illusive_realm_exit` / `world_earth_icon` 模板 | 已实现 |
| `check_boss_name`（OCR） | — | 未实现（项目无 OCR 引擎） |
| 小地图 `go_to_boss_minimap` | — | 未实现（无小地图裁剪与朝向角计算） |
| 八边形 Boss 图标 | — | 未实现（ok-ww 主流程未调用） |
| `Echo Count` / 每小时 | `Kills` / `Hours` | 部分：以击杀计，不按实际拾取数 |
| 月卡/云断线弹窗 | — | 未实现（无对应模板流程） |

## 已实现的新增底层能力

- `FarmInput`：绝对坐标点击（前台 `SendInput`，后台 `PostMessage`）、按键长按、滚轮、鼠标左右中键；带权限提升检测。
- `FrameIndex`：整帧积分图 + 复用缓冲，支持同一帧内多次模板搜索而不产生 GC 压力。
- `FarmVision`：基于 `data/Signatures.tsv` 的 UI 定位（F 提示、吸收文字、领奖、挑战、死亡、图鉴、传送按钮）。固定 HUD 元素只在模板预期位置 ±8 像素内匹配；每个命中还要通过第 8 列「局部亮度对比」相关性校验（≥0.70），避免低分辨率签名在暗部误命中。
- 15 个新 UI 签名由 ok-ww 模板图逐位复刻生成（相似度 1.0000），并已注册进 `BuildSignatures` 的重建列表。

## 离线验证

`tests/FarmChecks.cs` 用合成 HUD/UI 驱动状态机，覆盖：

- 签名完整性（嵌入表包含全部 `FarmIndicators`）
- Approach → F → 挑战面板点击 → 战斗委派 → 击杀计数 → 拾取
- 自动传送打开 F2 并点击页签
- 死亡弹窗点击复活

Windows 上执行 `.\build.ps1 -Test` 会一并运行这些用例；macOS 上可单独编译运行 `FarmChecks`（不启动 WinForms）。

## 已知限制

1. 无 OCR：等级选择、Boss 名识别不可用；等级行坐标基于 16:9 固定布局，宽屏可能有偏移。
2. 无 YOLO：声骸拾取依赖走近后的 F 提示；远处掉落可能漏捡。
3. 无小地图导航：大世界模式下依赖传送落点与直线前进；绕路地形可能找不到 Boss。
4. 传送的图鉴列表只点选当前可见的行；目标序号超出可见范围时会点到错误行。
5. 不做宝箱/地面掉落物的视觉定位：拾取完全依赖走近后出现的 F 提示，绕过障碍或远距离掉落需要人工调整。
5. 宽屏（21:9）下居中/右对齐 UI 的锚点与 ok-ww 不完全一致，建议 16:9 使用。
