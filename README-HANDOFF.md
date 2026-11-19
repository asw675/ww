# TideCombat 开发交接

这份快照供换电脑后继续开发。战斗功能已暂停修改，停在约 90% 的交付阶段。当前提交是开发快照，发布包需要等下面的验收项完成。

## 当前实现

- 项目使用独立的 C# 战斗状态机、HUD 识别和 Windows 输入接口。`src/` 是应用源码，`tests/` 是离线回放和动作序列测试，`data/Signatures.tsv` 是编译所需的数值特征。
- 已接入原项目注册的 58 个角色及漂泊者四种形态。离线测试覆盖角色连招和队友联动、PC 和云游戏锁敌、两类处决提示、切人确认、临满协奏重判及战斗结束交接。
- 交接快照用同一份参考源码完成了构建和回放，`--reference` 套件 112 项通过。独立的真实浏览器用例另见未完成项。
- 新增 4C 声骸刷取（`src/Farm4C.cs`、`src/FarmLoop.cs`、`src/FarmVision.cs`、`src/FarmInput.cs`、`src/EchoDetector.cs`、`src/MinimapNavigator.cs`）：复刻 `FarmEchoTask` 的传送、寻路、进本、战斗、YOLO 拾取、重开与复活循环；随包携带 `echo.onnx`、`arrow.bin` 与 ONNX Runtime。`tests/FarmChecks.cs` 用合成状态机验证流程。逐项差异见 `docs/FARM4C.md`。
- 自动战斗新增「角色倒下后自动点击复活」，替代原先的手动恢复等待。
- 新增体力本（`src/DomainRunner.cs`、`src/DomainForm.cs`、`src/WorldSession.cs`）：无音区/材料本/模拟领域，复用世界操作层；数值与滚动公式有离线测试。
- 新增批量强化声骸（`src/EchoEnhanceTask.cs`、`src/EnhanceLoop.cs`、`src/EnhanceForm.cs`）：复刻 `EnhanceEchoTask` 的词条判定与强化/弃置/锁定流程，规则用 `tests/EnhanceChecks.cs` 覆盖。
- 参考的是 `ok-wuthering-waves-master` 本地快照。`config.py` 标为 `dev`，中文更新日志首项为 `v3.8.4`。`AutoCombatTask.py`、`CombatCheck.py` 和 `BaseCombatTask.py` 的 SHA-256 前缀依次为 `5A2745060107`、`94AD744B908E`、`55E9E4977ACD`。

## 未完成

- **4C 声骸真机验收。** 状态机、YOLO、OCR、小地图、名称搜索与特化分支均已完成代码与离线用例；仍需在实际客户端逐 Boss 校准阈值与坐标（属于运行验证）。
- **4C 的 OCR 与模型。** PP-OCRv6（等级/吸收/Boss 名）与 YOLOv8（声骸）已随包提供，并用参考截图验证过；名称搜索输入使用剪贴板粘贴，依赖输入框接受 Ctrl+V。
- **4C 小地图与 Boss 特化。** 小地图箭头闭环、未勾选 Boss 标记的 0/90/180 旋转匹配、芬里科/无归的谬误/无铭探索者的专属流程均已实现；仍需真机逐 Boss 校准阈值。
- **云游戏后台的真实浏览器离线测试。** 本地 HTML 用例会启动隔离的无头浏览器。Edge 在当前环境中未稳定写出 `DevToolsActivePort`，历史日志记录了浏览器 GPU 子进程启动失败。用 Chrome 重试时，回环 DevTools 端口和本地页面已能列出，但测试仍报告 `fixture screenshot unavailable or unexpected`。下一步分别记录页面 URL、视口尺寸、截图返回状态及中心像素，判断失败发生在首次截图前还是截图颜色比对，并重新验证 W 按下、释放和鼠标事件。截图路径在 `src/CloudBrowser.cs` 的 `TryRead` 和 `RefreshViewport`，用例在 `tests/SelfTest.cs` 的 `BrowserFixture` 和 `VerifyBrowserEvents`。
- **最终行为同源验收。** 逐角色状态覆盖与已有截图已通过广泛对照。发布前仍需以原版分支为准复查未获独立回放证据的边界，尤其是队友同时达到优先切换条件、技能动画遮挡中的长短动作条，以及连续战斗中的状态保留。最近一批源码改动把估计的锁敌图标位置改成了识别到的动作条布局，涉及爱弥斯、绯雪、嘉贝莉娜和西格莉卡。
- **发布包。** 待离线验收完成后，由同一次通过验证的提交生成 Windows 程序与源码包。用户指定只做离线验证。

## 在另一台电脑接续

克隆仓库，在 64 位 Windows 上用 PowerShell 从仓库根目录构建。系统自带的 .NET Framework 编译器即可编译。原项目快照用于额外回放和重新提取特征，可单独复制到新电脑。

```powershell
.\build.ps1 -Test
.\build.ps1 -Test -ReferenceDirectory 'D:\path\to\ok-wuthering-waves-master'
.\bin\TideCombat.Tests.exe --browser-fixture .\tests\cloud-fixture.html
```

浏览器对照也可以连接已经启动的隔离测试浏览器。替换两个参数为该浏览器的临时配置目录和用例的 `file:///` URL。

```powershell
.\bin\TideCombat.Tests.exe --browser-protocol '<测试浏览器配置目录>' '<本地用例 file:/// URL>'
```

`CombatSettings.json` 和 `CombatConcerto.json` 是运行时的本机设置及协奏校准数据。内置默认设置和构建所需文件均在源码中。
