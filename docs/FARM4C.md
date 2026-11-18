# 刷 4C 声骸（Farm 4C Echo）

复刻 ok-ww `FarmEchoTask`（`🌀 Farm 4C Echo in Dungeon/World`）的完整流程：传送/寻路到 Boss、进入挑战、战斗、按 YOLO 检测拾取声骸、领奖、重开或再传送，含死亡恢复与弹窗处理。实现不复用 ok-ww 框架，按其行为重写。

## 使用前提

- 前台窗口模式（`本地客户端 · 前台` 或 `网页云游戏 · 前台`）；后台模式无法可靠点击菜单。
- 16:9、1280×720 以上、默认 HUD 布局。
- 游戏与工具权限级别一致（与自动战斗一致）。
- 模型与运行库已随包提供：`assets/echo_model/echo.onnx`、`assets/ocr/`（PP-OCRv6）、`assets/minimap/arrow.bin`、`lib/onnxruntime.dll`。

## 配置

| 项 | 说明 |
|---|---|
| 传送方式 | 不使用 / Boss 挑战 / 周常挑战 / 特异梦魇；对应 ok-ww 的 `Teleport to Boss` |
| Boss 序号 | 图鉴列表从上到下第几个（1 起） |
| 挑战等级 | 50/60/70/80/90，使用参考实现自带的坐标版 `choose_level` |
| 刷取次数 | 击杀次数上限（`Repeat Farm Count`） |
| 开打前等待 | `Combat Wait Time`；Boss 档案有默认值（异构武装/荣耀狮像/无归的谬误 5s、海之女 7s） |
| 拾取方式 | 向前走 / 绕圈 / YOLO 检测 |
| Boss 档案 | 通用、海之女、无归的谬误、异构武装、罗蕾莱、荣耀狮像、梦魇·赫卡忒、芬里科、无铭探索者 |
| 战斗设置 | 战斗前后切治疗 |

Boss 档案会覆盖默认等待时间、`bypass_end_wait` 与 `treat_as_not_in_realm`（梦魇·赫卡忒），并启用罗蕾莱的每 660 秒改夜晚流程。

## 状态机

```
WaitGame → Approach（小地图寻路） → (F) → Enter → Fight → Loot → Claim
                  ↑                                   │        │
                  └──────── Teleport / MapTeleport ←──┴────────┘
Revive / Night / MonthlyCard 为可随时插入的旁路状态。
```

- **Approach**：小地图有 Boss 标记时按箭头朝向闭环转向（A/D/W + 中键回正）；否则按 W 直线搜索，20 秒超时后按配置走 F2 传送或地图传送。
- **Enter**：F → 等级行 → 单人挑战 → 跳过「无法获取奖励」提示 → 开始挑战；坐标取自参考实现。
- **Fight**：复用 `BattlePlanner`；每帧/每 300ms 跑一次 YOLO，看到地面声骸立即结束战斗（对齐 `combat_end_condition`）。
- **Loot**：YOLO 模式朝最近的声骸走，靠近后按 F；绕圈模式按 W/A/S/D 循环；直走模式按 W。F 提示始终优先。
- **Claim**：领奖弹窗发 ESC，F 提示先滚动再按 F；倒计时结束后按配置进入下一轮。
- **Teleport**：F2 → Boss 图鉴页签 → 二级分类 → 列表行（不足时点滚动条并按公式补滚）→ 传送按钮 → 确认。
- **MapTeleport**：M 开图 → 中央 40% 找 `boss_check_mark` → 点击 → 传送按钮 → 确认。
- **Revive**：点击复活；副本内先 ESC 退本回信标再重新传送。
- **Night**：罗蕾莱专属，按参考的点击序列调整昼夜。
- **MonthlyCard**：识别月卡弹窗并点击两次底部按钮。

## 模型与识别

- **YOLOv8 声骸模型**：`images 1×3×640×640 → output0 1×5×8400`；letterbox 640、RGB/255、NCHW、`cv2.dnn.NMSBoxes` 等价 IoU 0.45。阈值：副本内 0.25、大世界 0.65（对齐 `init_parameters`）。
- **ONNX Runtime**：托管库 + Windows x64 原生库随包；`EchoDetector` 直接用 `InferenceSession`。
- **小地图**：`box_minimap` 区域 + `boss_check_mark_minimap` 签名定位；玩家朝向用 72 个旋转的箭头灰度模板做归一化互相关，算出顺时针转向角。
- **宽屏锚点**：标签名含 `_hcenter` 的按屏幕中心锚定，其余按参考的右/左/中心规则；16:9 下等价。
- **UI 签名**：22 个标签（F 提示、吸收文字、领奖、挑战、死亡、宝箱、图鉴、传送、月卡、体力、Boss 标记等），全部带「局部亮度对比」列；固定 HUD 只在模板预期位置 ±8 像素内匹配。
- **OCR**：内置 PP-OCRv6（det+rec，ONNX Runtime）。等级选择用 OCR 在左侧面板找目标等级文本，拾取用 OCR 读「吸收/Absorb」文字；Boss 名识别未接入（改用地图标记传送）。

## 与 ok-ww 的逐项对照

| ok-ww | 本项目 | 实现方式 |
|---|---|---|
| `do_run` 主循环 | `Farm4C.Observe` | 状态机 |
| `teleport_to_configured_boss` | `Step.Teleport` | F2 图鉴 + 滚动条补滚 |
| `walk_after_boss_teleport` | `Approach` | W/中键，小地图导航 |
| `go_to_boss_minimap` | `MinimapNavigator` + `Navigate` | 箭头旋转 + 角度闭环 |
| `teleport_to_nearest_boss` | `Step.MapTeleport` | M 地图 + 标记点击 |
| `enter_configured_boss_realm_from_f` | `Enter` | 参考坐标序列 |
| `combat_once` | `BattlePlanner` | 现有战斗实现 |
| `combat_end_condition=find_echos` | `Fight` 中的 YOLO 判定 | 同阈值、同 NMS |
| `pick_echo` / `walk_find_echo` | `Loot` + 吸收模板 | 模板替代 OCR |
| `yolo_find_echo` | `ChaseEcho` | 同模型与阈值 |
| `run_in_circle_to_find_echo` | `Loot` 绕圈 | 方向循环 |
| `handle_claim_button` | `Claim` | ESC |
| `scroll_and_click_buttons` | `Claim` | 滚轮 + F |
| `handle_boss_restart_after_treasure` | `Claim → Enter` | 副本内 F 重开 |
| `revive_action` | `Revive` | 复活点击 + 退本/传送 |
| `check_boss_name`（OCR） | 未接入 | 用地图标记传送替代 |
| `change_time_to_night` | `Night` | 参考点击序列 |
| `handle_monthly_card` | 弹窗点击 | 参考坐标 |
| `manage_boss_parameters` | 构造时计算 | 等待/bypass/赫卡忒 |
| `init_parameters` | 阈值 0.25/0.65 | 已对齐 |

## 参考截图验证

| 截图 | 预期 | 结果 |
|---|---|---|
| `absorb.png` | F 提示 + 吸收文字 | 命中（0.976 / 0.911，含亮度校验） |
| `echo2.png` | 有发光特效 | YOLO 检出 1 个框（0.70） |
| `in_combat.png` | 无死亡弹窗 | 死亡不误报，识别为大世界 |
| `big_map.png` | 无图鉴/传送按钮 | 全部不误报 |

## 已知差异

1. Boss 名不识别，改用地图标记传送（`aim_boss` 路径未实现，中文文本输入未接）。
2. 云端断线弹窗用 OCR 关键词识别并点击确认，文案变化时需补关键词。
3. 芬里科/无归的谬误/无铭探索者的特化交互用通用流程 + 地图传送近似，未逐帧复刻。
4. 宽屏 21:9 的居中/右对齐锚点与参考不完全一致，建议 16:9。
5. 所有阈值与坐标未在真实客户端校准。
