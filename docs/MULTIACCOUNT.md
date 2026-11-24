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
4. 优先匹配账号原文。原文不匹配时才使用小写、`0→o`、`.con→.com` 归一化，结果必须唯一。选择账号后重新读取确认。
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
- `task` 可为 `null`（只登录不执行）；配置界面任务下拉包含 None。
- 保存配置时会保留账号已有的 `config`，不会重建为空对象。
- 配置界面：账号列表、启用勾选、任务下拉、添加/更新/删除、保存并开始。
- 运行结果写入 `configs/MultiAccountResults.json`（状态/时间/任务/消息）。

## 可执行任务映射

`DailyTask`、`FarmEchoTask`、`EnhanceEchoTask`、`TacetTask`、`ForgeryTask`、`SimulationTask`、`NightmareNestTask`、`GardenTask`、`MergeEchoTask`、`ChangeEchoTask`、`CultivationTask`、`RahaiBlocksTask`、`GalGameTask`。

在切换账号前，程序会核对全部已启用任务引用。接受 `TideCombat` 和对应的原版 `src.task.<类名>` 模块。拉海洛方块和第二索拉登录后会暂停，打开对应活动页后点击「继续」，再切回游戏。

## 按账号覆盖配置

`config` 可包含以下分组（键名大小写不敏感），执行前应用、执行后恢复：

| 分组 | 可覆盖字段 |
|---|---|
| `Farm` | Teleport、TeleportMode、BossIndex、BossLevel、RepeatCount、WaitSeconds、SwitchHealer、Pickup、Boss |
| `Daily` | Tasks、NightmareMode、Farm、TacetIndex、ForgeryIndex、Material、FarmNightmareEcho、AutoNightmare、CheckGarden、MergeIfNeeded、Farm4C |
| `Enhance` | RequireBothCrit、RequireValidBeforeCrit、CritTotalMinimum、FirstCritMinimum、ValidCountMinimum、FirstMustBeValid、ValidStats、PauseAfterSuccess |
| `Domain` | Kind、Index、Material、MaxStamina |
| `Nightmare` | TacetNest、Purification、CaptureOnce |
| `Change` | Target |
| `Cultivation` | Ascension、Skills、Tacet、Nightmare |
| `Combat` | AutoTarget、AutoRevive、UseLiberation、RotateParty、PreferConcerto、SwitchAtLowHealth、SupportOpener、DetectSkillKeys、CheckLevitator、Source、各技能键 |

枚举可用数字或名称；`ValidStats` 为字符串数组。

也支持原版一条龙、副本、梦魇、培养目标和强化的对应字段。本地分组配置可覆盖同名选项，每个账号结束后恢复全局配置。

任务为 `EnhanceEchoTask` 时，选中该账号并点「强化条件」可单独配置。登录后先在背包筛选声骸并按等级升序，再点击主界面的「继续」。开启成功暂停时，在同一账号继续执行。整批结束并返回大世界后才记录该账号成功。

`Daily.Tasks` 是非空任务数组，顺序就是执行顺序，可选 `Nightmare`、`Stamina`、`DailyRewards`、`Mail`、`BattlePass`、`Garden`、`Merge`、`Farm4C`。`Daily.NightmareMode` 可选 `AsNeeded`、`CaptureOnce` 或 `All`。

省略这两个字段时继承全局设置。显式设为 `null` 可按当前账号的旧版选项推导该字段。全局或账号有明确的任务数组时，旧版勾选项由该数组里的任务选择替代。

```json
{
  "Daily": {
    "Tasks": ["Mail", "Nightmare", "DailyRewards"],
    "NightmareMode": "CaptureOnce"
  }
}
```

## 失败判定

- 每个配置任务执行后检查 `Failed`：失败会写入 `MultiAccountResults.json` 的 `failed` 并停止后续账号。
- 顺序模式中当前账号的一条龙失败也会停止并报错，不会把该账号记为成功。

## 已知限制

1. 配置界面支持上移/下移排序与保存校验，未实现拖拽。
2. 原生 ComboBox 依赖国际服登录框；其他登录界面走 OCR。
3. 未实机验证账号切换与登录。
