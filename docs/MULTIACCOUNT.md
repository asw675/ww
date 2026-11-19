# 多账号（Multi-Account）

对应 ok-ww 的 `MultiAccountDailyTask` 与 `MultiAccountConfigTask`。

## 两种模式

| 模式 | 行为 |
|---|---|
| 多账号日常 | 当前账号先跑一条龙 → 回登录界面 → 依次选择未完成账号登录 → 每个账号跑一条龙 |
| 多账号配置 | 读取 `configs/MultiAccountConfig.json`，按顺序登录启用账号并执行各自配置的任务 |

## 账号切换

1. ESC 打开设置 → 点左下退出 → 确认退出到登录界面。
2. 优先使用原生登录 ComboBox（国际服 KRSDK 对话框）读取账号列表并选择；这解决后台模式点击下拉项被撤销的问题。
3. 找不到 ComboBox 时回退到 OCR：识别带 `***` 的账号条目并点击。
4. 账号归一化：小写、`0→o`、`.con→.com`，避免 OCR 差异导致重复执行。
5. 登录按钮：OCR 匹配「登录/登入/Log/Click to Connect/点击连接」，找不到时点屏幕中下 (0.5,0.568)。

## 配置文件

`configs/MultiAccountConfig.json`：

```json
{
  "accounts": [
    {
      "account_key": "user@example.com",
      "enabled": true,
      "task": { "module": "TideCombat", "class": "DailyTask" },
      "config": {}
    }
  ]
}
```

- `account_key` 必填、唯一、不能包含 `*` 或首尾空格。
- `task` 可为 `null`（只登录不执行）。
- 配置界面：账号列表、启用勾选、任务下拉、添加/更新/删除、保存并开始。
- 运行结果写入 `configs/MultiAccountResults.json`（状态/时间/任务/消息）。

## 可执行任务映射

`DailyTask`、`FarmEchoTask`、`EnhanceEchoTask`、`TacetTask`、`ForgeryTask`、`SimulationTask`、`NightmareNestTask`、`GardenTask`、`MergeEchoTask`、`ChangeEchoTask`。

## 按账号覆盖配置

`config` 可包含以下分组（键名大小写不敏感），执行前应用、执行后恢复：

| 分组 | 可覆盖字段 |
|---|---|
| `Farm` | Teleport、TeleportMode、BossIndex、BossLevel、RepeatCount、WaitSeconds、SwitchHealer、Pickup、Boss |
| `Daily` | Farm、TacetIndex、ForgeryIndex、Material、FarmNightmareEcho、AutoNightmare、CheckGarden、MergeIfNeeded、Farm4C |
| `Enhance` | RequireBothCrit、RequireValidBeforeCrit、CritTotalMinimum、FirstCritMinimum、ValidCountMinimum、FirstMustBeValid、ValidStats、PauseAfterSuccess |
| `Domain` | Kind、Index、Material、MaxStamina |
| `Nightmare` | TacetNest、Purification、CaptureOnce |
| `Change` | Target |
| `Combat` | AutoTarget、AutoRevive、UseLiberation、RotateParty、PreferConcerto、SwitchAtLowHealth、SupportOpener、DetectSkillKeys、CheckLevitator、Source、各技能键 |

枚举可用数字或名称；`ValidStats` 为字符串数组。

## 已知限制

1. 配置界面为简化列表，未实现参考的卡片排序/拖拽与完整校验提示。
2. 原生 ComboBox 依赖国际服登录框；其他登录界面走 OCR。
3. 未实机验证账号切换与登录。
