using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace TideCombat
{
    internal sealed class FarmReport
    {
        internal string State;
        internal string LastAction;
        internal int Kills;
        internal int Echoes;
        internal double Rate;
        internal Size Size;
        internal HudSnapshot Hud;
        internal bool Finished;
        internal string Failure;
    }

    internal static class FarmLoop
    {
        private const string LogArea = "刷4C";

        internal static FarmReport Run(CombatSettings settings, FarmOptions options, IProgress<FarmReport> progress,
            CancellationToken cancellation, ConcertoMeter concerto, Action<string, PixelFrame> recordEvidence = null)
        {
            RunLog.Info(LogArea, string.Format("开始 · 来源={0} · 首领={1} · 目标轮数={2} · 搜寻={3} · 自动传送={4} · 图鉴分类={5} · 序号={6}",
                settings.Source, options.Boss, options.RepeatCount, options.Pickup, options.Teleport, options.TeleportMode,
                options.IndexFor(options.TeleportMode)));
            var window = GameWindow.ForTask(settings.Source);
            var input = new WindowInput(window);
            var farmInput = new FarmInput(window);
            var vision = new HudVision(settings, concerto);
            var farmVision = new FarmVision();
            bool cloudStream = settings.Source == GameSource.Cloud || settings.Source == GameSource.ManagedCloud;
            var farm = new Farm4C(settings, options, cloudStream);
            if (cancellation.WaitHandle.WaitOne(500)) return null;
            var monthly = new MonthlyCardSchedule(settings.WorldAssist);
            EchoDetector detector = null;
            try
            {
                string model = EchoModel.ModelPath;
                if (File.Exists(model)) detector = new EchoDetector(model);
            }
            catch (Exception error)
            {
                RunLog.Error(LogArea, "声骸检测模型加载失败", error);
                detector = null;
            }
            if (options.Pickup == FarmPickupMode.Yolo && detector == null)
                throw new InvalidOperationException("无法加载刷 4C 的声骸检测模型。");
            long lastDetect = -1000;
            List<EchoBox> echoes = null;
            OcrReader ocr = null;
            try
            {
                string dir = OcrReader.Directory;
                if (File.Exists(Path.Combine(dir, "det.onnx"))) ocr = new OcrReader(dir);
            }
            catch (Exception error) { RunLog.Error(LogArea, "OCR 模型加载失败", error); }
            long lastOcr = -1000;
            long lastEntryOcr = -1000;
            List<OcrLine> levelLines = null;
            EchoPrompt pickupPrompt = EchoPrompt.None;
            long lastPickupScan = -1000;
            long lastEntryPrompt = -1000;
            long lastRechallengeOcr = -1000;
            long lastRechallengeDialogOcr = -1000;
            string bossName = null;
            long lastBossOcr = -1000;
            long lastDialogOcr = -1000, lastClaimScan = -1000;
            BossMarkerFinder markerFinder = null;
            try
            {
                string markerPath = BossMarkerFinder.DataPath;
                if (File.Exists(markerPath)) markerFinder = new BossMarkerFinder(markerPath);
            }
            catch (Exception error) { RunLog.Error(LogArea, "地图标记模型加载失败", error); }
            long lastMarker = -1000;
            long lastWaypointScan = -1000;
            MinimapNavigator navigator = null;
            double navTurn = double.NaN;
            long lastNav = -1000;
            try
            {
                string arrow = MinimapNavigator.ArrowPath;
                if (File.Exists(arrow)) navigator = new MinimapNavigator(arrow);
            }
            catch (Exception error) { RunLog.Error(LogArea, "小地图导航模型加载失败", error); }
            RunLog.Info(LogArea, string.Format("识别组件就绪 · 声骸检测={0} · OCR={1} · 地图标记={2} · 小地图导航={3}",
                detector != null, ocr != null, markerFinder != null, navigator != null));
            var timer = Stopwatch.StartNew();
            string lastAction = "—";
            string lastState = null, lastPhase = null;
            long lastReport = -500, lastStatusLog = -500, lastRejectedLog = -2000;
            FarmRequest lastRejectedRequest = FarmRequest.None;
            var loggedScanErrors = new HashSet<string>();
            char movement = '\0';
            bool sprinting = false;
            long lostSince = -1, pausedMs = 0;
            IntPtr capturedWindow = IntPtr.Zero;
            uint capturedProcess = 0;
            GameSource capturedMode = window.Mode;
            Size capturedSize = Size.Empty;
            try
            {
                using (var capture = new BattleLoop.WindowFrames(window))
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        long started = timer.ElapsedMilliseconds;
                        Rectangle bounds = Rectangle.Empty;
                        string state = "等待《鸣潮》客户端";
                        FarmUi ui = null;
                        HudSnapshot hud = null;
                        PixelFrame frame = default(PixelFrame);
                        bool captured = false;
                        if (window.TryLocate(out bounds, out state))
                        {
                            if (window.InputBlockedByIntegrity)
                                throw new InputElevationRequiredException();
                            try
                            {
                                long captureTime = started - pausedMs;
                                bool retainEcho = detector != null && farm.WantsEchoDetection(captureTime) &&
                                    (farm.SearchingForEcho || captureTime - lastDetect >= 300);
                                bool readText = ocr != null && (!farm.Planner.Fighting || cloudStream || farm.WantsLevelScan ||
                                    farm.WaitingForEntryControls || farm.CanScanInteraction || farm.WantsPickupScan || farm.WantsRechallengeDialog);
                                if (!capture.Read(bounds, out frame, retainEcho || readText))
                                    state = "已找到游戏窗口，等待截图";
                                else if (!window.Available())
                                    state = "游戏窗口状态已变化，正在重新识别";
                                else
                                    captured = true;
                            }
                            catch (System.ComponentModel.Win32Exception error)
                            {
                                state = "游戏截图暂不可用";
                                LogScanErrorOnce(loggedScanErrors, "游戏截图暂不可用", error);
                            }
                            if (captured)
                            {
                                try
                                {
                                    hud = vision.Read(frame, farm.Planner.Fighting, cloudStream,
                                        farm.Planner.LucillaTransformSlot, farm.Planner.ShouldScanExecution(
                                            (lostSince >= 0 ? lostSince : started) - pausedMs));
                                }
                                catch (Exception error)
                                {
                                    if (error is OutOfMemoryException || error is StackOverflowException)
                                        throw;
                                    LogScanErrorOnce(loggedScanErrors, "队伍 HUD 识别失败", error);
                                    hud = null;
                                }
                                ui = farmVision.Read(frame, !(hud != null && (hud.EnemyHealth || hud.Locked)));
                                if (farm.WantsGuideEntrance)
                                    farmVision.ReadGuideEntrance(frame, ui);
                                if (farm.NeedsBossMenuMarker && (hud == null || !hud.Valid))
                                    farmVision.ReadBossDestination(frame, ui);
                                if (farm.WantsMonsterGuide)
                                    farmVision.ReadMonsterGuide(frame, ui);
                            }
                        }

                        long elapsed = timer.ElapsedMilliseconds;
                        long now = elapsed - pausedMs;
                        bool captureChanged = captured && capturedWindow != IntPtr.Zero &&
                            (window.Handle != capturedWindow || window.ProcessId != capturedProcess ||
                            window.Mode != capturedMode || bounds.Size != capturedSize);
                        if (!captured || captureChanged)
                        {
                            farmInput.ReleaseAll();
                            input.ReleaseAll();
                            movement = '\0';
                            sprinting = false;
                            pickupPrompt = EchoPrompt.None;
                            echoes = null;
                            if (lostSince < 0)
                            {
                                lostSince = elapsed;
                                farm.Suspend(now);
                                levelLines = null;
                                navTurn = double.NaN;
                                bossName = null;
                                lastPickupScan = lastDetect = lastNav = lastOcr = lastEntryOcr =
                                    lastEntryPrompt = lastRechallengeOcr = lastRechallengeDialogOcr =
                                    lastBossOcr = lastDialogOcr = lastClaimScan = lastMarker = -1000;
                            }
                        }
                        if (captured)
                        {
                            capturedWindow = window.Handle;
                            capturedProcess = window.ProcessId;
                            capturedMode = window.Mode;
                            capturedSize = bounds.Size;
                            if (lostSince >= 0)
                            {
                                RunLog.Info(LogArea, string.Format("恢复游戏画面 · {0}×{1} · 模式={2} · 等待 {3}ms",
                                    frame.Width, frame.Height, capturedMode, elapsed - lostSince));
                                pausedMs += elapsed - lostSince;
                                lostSince = -1;
                                now = elapsed - pausedMs;
                            }
                            bool wantEchoes = detector != null && farm.WantsEchoDetection(now) && hud != null && hud.Valid;
                            if (wantEchoes && frame.HasSourcePixels && (farm.SearchingForEcho || now - lastDetect >= 300))
                            {
                                lastDetect = now;
                                try
                                {
                                    echoes = detector.Detect(frame, farm.EchoThreshold(ui));
                                }
                                catch (Exception error)
                                {
                                    RunLog.Error(LogArea, "声骸检测失败，禁用本次任务的检测模型", error);
                                    echoes = null;
                                    detector.Dispose();
                                    detector = null;
                                    if (options.Pickup == FarmPickupMode.Yolo)
                                        throw new InvalidOperationException("刷 4C 的声骸检测失败。", error);
                                }
                            }
                            else if (!wantEchoes)
                            {
                                echoes = null;
                                lastDetect = -1000;
                            }
                            if (markerFinder != null && farm.NeedsMapMarker && ui != null && !ui.HasMapMarker &&
                                now - lastMarker >= 500)
                            {
                                lastMarker = now;
                                try
                                {
                                    Rectangle marker;
                                    if (markerFinder.TryFind(frame, out marker))
                                    {
                                        ui.HasMapMarker = true;
                                        ui.MapMarker = marker;
                                    }
                                }
                                catch (Exception error)
                                {
                                    LogScanErrorOnce(loggedScanErrors, "查找地图标记失败", error);
                                }
                            }
                            if (ui != null && !Farm4C.PlayableScene(hud, ui) && farm.WantsWaypoints(now) &&
                                now - lastWaypointScan >= 500)
                            {
                                lastWaypointScan = now;
                                try
                                {
                                    ui.HealWaypoints = farmVision.FindWaypoints(frame);
                                }
                                catch (Exception error)
                                {
                                    LogScanErrorOnce(loggedScanErrors, "查找回血传送点失败", error);
                                }
                            }
                            if (!farm.WantsNavigation || hud == null || !hud.Valid)
                                navTurn = double.NaN;
                            else if (navigator != null && now - lastNav >= 150)
                            {
                                lastNav = now;
                                try
                                {
                                    if (!navigator.TryTurn(frame, out navTurn))
                                        navTurn = double.NaN;
                                }
                                catch (Exception error)
                                {
                                    LogScanErrorOnce(loggedScanErrors, "小地图导航识别失败", error);
                                    navTurn = double.NaN;
                                }
                            }
                            if (!farm.WantsLevelScan)
                                levelLines = null;
                            else if (ocr != null && (hud == null || !hud.Valid) && now - lastOcr >= 400)
                            {
                                lastOcr = now;
                                try
                                {
                                    levelLines = ocr.Read(frame, new Rectangle((int)(frame.Width * .03),
                                        (int)(frame.Height * .113), (int)(frame.Width * .293),
                                        (int)(frame.Height * .547)));
                                }
                                catch (Exception error)
                                {
                                    LogScanErrorOnce(loggedScanErrors, "关卡难度 OCR 失败", error);
                                    levelLines = null;
                                }
                            }
                            if (farm.CanScanInteraction && Farm4C.PlayableScene(hud, ui) && !hud.Locked && !hud.EnemyHealth &&
                                !ui.FPrompt &&
                                now - lastEntryPrompt >= 300)
                            {
                                lastEntryPrompt = now;
                                ui.FPrompt = farmVision.FindPickupKey(frame, out ui.FBox);
                            }
                            bool scanPickup = farm.WantsPickupScan || farm.CanScanInteraction && (ui.FPrompt || ui.Absorb);
                            if (!scanPickup)
                            {
                                pickupPrompt = EchoPrompt.None;
                                lastPickupScan = -1000;
                            }
                            else if (now - lastPickupScan >= 180)
                            {
                                lastPickupScan = now;
                                EchoPrompt detectedPrompt = NestMath.ReadEchoPrompt(frame, ui, farmVision, ocr);
                                if (detectedPrompt != pickupPrompt)
                                    RunLog.Info(LogArea, "吸收提示变化 · " + pickupPrompt + " → " + detectedPrompt);
                                pickupPrompt = detectedPrompt;
                            }
                            if (ui.FPrompt && farm.WantsRechallengeChoice(ui) && !ui.Claim && !ui.SkipDialog &&
                                now - lastRechallengeOcr >= 250)
                            {
                                lastRechallengeOcr = now;
                                ui.Rechallenge = FarmVision.ReadRechallenge(frame, ui.FBox, ocr);
                            }
                            if (farm.WantsRechallengeDialog && !ui.ClaimReward && !ui.Book && !ui.Teleport &&
                                (ui.Claim || hud == null || !hud.Valid) && now - lastRechallengeDialogOcr >= 300)
                            {
                                lastRechallengeDialogOcr = now;
                                ui.RechallengeButton = FarmVision.ReadRechallengeDialog(frame, ocr);
                            }
                            if (ocr != null && farm.WaitingForEntryControls && (hud == null || !hud.Valid) &&
                                !ui.Book && !ui.TeamStart && !ui.Challenge && !ui.SkipDialog && !ui.Teleport &&
                                now - lastEntryOcr >= 500)
                            {
                                lastEntryOcr = now;
                                var entryArea = new Rectangle((int)(frame.Width * .63), (int)(frame.Height * .83),
                                    (int)(frame.Width * .37), (int)(frame.Height * .16));
                                try
                                {
                                    Rectangle entry;
                                    if (NestMath.TryChallengeText(ocr.Read(frame, entryArea), out entry))
                                    {
                                        ui.Challenge = ui.TeamStart = true;
                                        ui.TeamStartBox = entry;
                                    }
                                }
                                catch (Exception error)
                                {
                                    LogScanErrorOnce(loggedScanErrors, "挑战入口 OCR 失败", error);
                                }
                            }
                            if (farm.AtStart && now - lastClaimScan >= 500)
                            {
                                lastClaimScan = now;
                                farmVision.ReadBlockingClaim(frame, ui, hud != null && hud.Valid);
                            }
                            if (cloudStream && ocr != null && (hud == null || !hud.Valid) && now - lastDialogOcr >= 1000)
                            {
                                lastDialogOcr = now;
                                try
                                {
                                    var lines = ocr.ReadRelative(frame, .20, .25, .80, .75, centered: true);
                                    bool disconnected;
                                    ui.CloudDisconnectBox = WorldAssist.CloudDisconnectButton(lines, out disconnected);
                                }
                                catch (Exception error)
                                {
                                    LogScanErrorOnce(loggedScanErrors, "弹窗 OCR 失败", error);
                                }
                            }
                            if (ocr != null && !farm.Planner.Fighting && now - lastBossOcr >= 3000)
                            {
                                lastBossOcr = now;
                                try
                                {
                                    bossName = null;
                                    if (window.Language == GameLanguage.SimplifiedChinese)
                                    {
                                        var lines = ocr.Read(frame, Farm4C.BossNameBounds(new Size(frame.Width, frame.Height)));
                                        bossName = string.Join(" ", lines.Select(line => line.Text));
                                    }
                                }
                                catch (Exception error)
                                {
                                    LogScanErrorOnce(loggedScanErrors, "首领名称 OCR 失败", error);
                                    bossName = null;
                                }
                            }
                            if (monthly.Due(DateTime.Now) && !farm.Planner.Fighting && hud != null &&
                                !hud.Locked && !hud.EnemyHealth && !hud.Dead && farmVision.HasTeamNumbers(frame))
                            {
                                if (movement != '\0') { farmInput.KeyUp(movement); movement = '\0'; }
                                input.ReleaseAll();
                                sprinting = false;
                                farm.Suspend(now);
                                using (var session = new WorldSession(settings, cancellation, ocr, window, capture, monthly))
                                    session.CheckScheduledCard();
                                continue;
                            }
                            var farmFrame = new FarmFrame(hud, hud != null && hud.Valid, ui,
                                new Size(frame.Width, frame.Height), bounds, echoes, navTurn, levelLines, pickupPrompt,
                                bossName, pickupReceipt: scanPickup && FarmVision.HasEchoReceipt(frame));
                            int echoesBefore = farm.Echoes;
                            FarmDecision decision = farm.Observe(farmFrame, now);
                            if (recordEvidence != null && farm.Echoes > echoesBefore)
                                recordEvidence("farm-echo-receipt", frame);
                            state = farm.Status;
                            if (movement != '\0' && movement != farm.MovementKey)
                            {
                                farmInput.KeyUp(movement);
                                movement = '\0';
                            }
                            if (farm.Planner.TakeReleaseRequest())
                            {
                                input.ReleaseAll();
                                sprinting = false;
                            }
                            if (sprinting && !farm.WantsSprint)
                            {
                                input.Send(BattleAction.Release(BattleButton.Dodge), cancellation);
                                sprinting = false;
                            }
                            if (hud != null && !hud.Valid && !farm.Planner.KeepAnimationHold(hud, now))
                            {
                                if (input.ReleaseAll())
                                    farm.Planner.HoldsCanceled(now);
                                sprinting = false;
                            }
                            string action;
                            if (decision.Request == FarmRequest.DismissMonthlyCard)
                            {
                                using (var session = new WorldSession(settings, cancellation, ocr, window, capture, monthly))
                                {
                                    action = session.DismissMonthlyCard() ? "已关闭月卡奖励" : null;
                                    cancellation.ThrowIfCancellationRequested();
                                    if (action == null) farm.Stop("月卡奖励关闭后未确认返回游戏。");
                                }
                            }
                            else if (decision.Request == FarmRequest.RecoverRealm)
                            {
                                // Use the same window and capture stream while the farming loop waits.
                                using (var session = new WorldSession(settings, cancellation, ocr, window, capture, monthly))
                                {
                                    bool recovered = farm.RecoverRealm(session, message =>
                                    {
                                        RunLog.Info(LogArea, message);
                                        if (progress != null) progress.Report(new FarmReport { State = message,
                                            Kills = farm.Kills, Echoes = farm.Echoes, Rate = farm.EchoesPerHour,
                                            Size = bounds.Size, Hud = hud });
                                    });
                                    cancellation.ThrowIfCancellationRequested();
                                    farm.RealmRecoveryFinished(recovered, timer.ElapsedMilliseconds - pausedMs);
                                    action = recovered ? "已确认退出副本并回血" : null;
                                }
                            }
                            else
                                action = Execute(decision, frame, bounds, farmInput, input, cancellation, ref movement);
                            if (action != null)
                            {
                                if (decision.Request != FarmRequest.Battle)
                                    RunLog.Info(LogArea, string.Format("操作已发出 · 阶段={0} · 请求={1} · 动作={2} · 补试={3}",
                                        farm.Phase, decision.Request, action, decision.RetryInput));
                                farm.Sent(decision, timer.ElapsedMilliseconds - pausedMs);
                                lastAction = action;
                            }
                            else
                            {
                                if (decision.Request == FarmRequest.KeyDown)
                                    farm.MovementRejected();
                                bool actionable = decision.Request != FarmRequest.None &&
                                    decision.Request != FarmRequest.KeyUp &&
                                    (decision.Request != FarmRequest.Battle || decision.Battle.Kind != ActionKind.Wait);
                                if (actionable && !cancellation.IsCancellationRequested &&
                                    (decision.Request != lastRejectedRequest || elapsed - lastRejectedLog >= 2000))
                                {
                                    RunLog.Warn(LogArea, string.Format("操作未发出 · 阶段={0} · 请求={1} · 吸收={2} · 重开={3} · 补试={4}",
                                        farm.Phase, decision.Request, decision.PickupKey, decision.StartsRechallenge,
                                        decision.RetryInput));
                                    lastRejectedRequest = decision.Request;
                                    lastRejectedLog = elapsed;
                                }
                            }
                            if (farm.Finished)
                            {
                                var next = farm.RestartAfterPopup(() =>
                                {
                                    using (var session = new WorldSession(settings, cancellation, ocr, window, capture, monthly))
                                        return new WorldRecovery(session, message => RunLog.Info(LogArea, message)).ClearFarmInterruption();
                                });
                                cancellation.ThrowIfCancellationRequested();
                                if (next != null)
                                {
                                    farmInput.ReleaseAll();
                                    input.ReleaseAll();
                                    movement = '\0';
                                    sprinting = false;
                                    farm = next;
                                    levelLines = null;
                                    bossName = null;
                                    echoes = null;
                                    navTurn = double.NaN;
                                    pickupPrompt = EchoPrompt.None;
                                    lastPickupScan = lastDetect = lastNav = lastOcr = lastEntryOcr =
                                        lastEntryPrompt = lastRechallengeOcr = lastRechallengeDialogOcr =
                                        lastBossOcr = lastDialogOcr = lastClaimScan = lastMarker = lastWaypointScan = -1000;
                                    continue;
                                }
                                if (recordEvidence != null)
                                    recordEvidence(farm.Failure == null ? "farm-final-settlement" : "farm-unconfirmed", frame);
                                if (farm.Failure != null)
                                    RunLog.Warn(LogArea, "任务停止 · " + farm.Failure);
                                RunLog.Info(LogArea, string.Format("结束 · 状态={0} · 击杀={1} · 确认声骸={2} · 耗时={3}ms",
                                    farm.Phase, farm.Kills, farm.Echoes, elapsed));
                                return new FarmReport
                                {
                                    State = farm.Failure ?? farm.CompletionMessage ?? "刷取完成", Failure = farm.Failure, Finished = true,
                                    Kills = farm.Kills, Echoes = farm.Echoes,
                                    Rate = farm.EchoesPerHour,
                                    Size = bounds.Size, Hud = hud, LastAction = lastAction
                                };
                            }
                            if (!sprinting && movement != '\0' && farm.WantsSprint && farmFrame.HudValid)
                                sprinting = input.Send(BattleAction.Press(BattleButton.Dodge, 0, true), cancellation);
                        }

                        string phase = captured ? farm.Phase : "WaitFrame";
                        if (phase != lastPhase || state != lastState && elapsed - lastStatusLog >= 500)
                        {
                            RunLog.Info(LogArea, string.Format("状态 · 阶段={0} · {1}", phase, state));
                            lastPhase = phase;
                            lastState = state;
                            lastStatusLog = elapsed;
                        }
                        if (progress != null && elapsed - lastReport >= 200)
                        {
                            progress.Report(new FarmReport
                            {
                                State = state, LastAction = lastAction, Kills = farm.Kills,
                                Echoes = farm.Echoes,
                                Rate = farm.EchoesPerHour,
                                Size = bounds.Size, Hud = hud
                            });
                            lastReport = elapsed;
                        }
                        int remaining = (int)Math.Max(40, 90 - (timer.ElapsedMilliseconds - started));
                        cancellation.WaitHandle.WaitOne(remaining);
                    }
                }
            }
            finally
            {
                try { farmInput.ReleaseAll(); }
                catch (Exception error) { RunLog.Error(LogArea, "释放刷取输入失败", error); }
                try { input.ReleaseAll(); }
                catch (Exception error) { RunLog.Error(LogArea, "释放战斗输入失败", error); }
                farm.Planner.Invalidate(timer.ElapsedMilliseconds);
                if (detector != null) detector.Dispose();
                if (ocr != null) ocr.Dispose();
                if (concerto != null) concerto.Save();
            }
            RunLog.Info(LogArea, string.Format("任务取消 · 击杀={0} · 确认声骸={1}", farm.Kills, farm.Echoes));
            return null;
        }

        private static void LogScanErrorOnce(HashSet<string> loggedErrors, string context, Exception error)
        {
            if (loggedErrors.Add(context))
                RunLog.Error(LogArea, context, error);
        }

        private static string Execute(FarmDecision decision, PixelFrame frame, Rectangle bounds, FarmInput farmInput,
            WindowInput input, CancellationToken token, ref char movement)
        {
            Size size = new Size(frame.Width, frame.Height);
            switch (decision.Request)
            {
                case FarmRequest.Battle:
                    if (decision.Battle.Kind == ActionKind.Wait)
                        return null;
                    if (decision.Battle.AttackBefore)
                        input.Send(BattleAction.Attack(), token);
                    if (!input.Send(decision.Battle, token))
                        return null;
                    if (decision.Battle.FollowWithAttack)
                        input.Send(BattleAction.Attack(), token);
                    return decision.Battle.Label;
                case FarmRequest.Click:
                    return farmInput.Click(decision.Point, size, bounds, FarmMouse.Left, 100, token,
                        settleMs: 150, useWindowMessage: !decision.RetryInput) ?
                        (decision.StartsRechallenge ? "点击重新挑战" : "点击") : null;
                case FarmRequest.AltClick:
                    const char alt = (char)0x12;
                    if (!farmInput.KeyDown(alt)) return null;
                    try
                    {
                        return !token.WaitHandle.WaitOne(50) && farmInput.Click(decision.Point, size, bounds,
                            FarmMouse.Left, 60, token) ? "通过大世界入口打开索拉指南" : null;
                    }
                    finally { farmInput.KeyUp(alt); }
                case FarmRequest.Middle:
                    return farmInput.Click(new Point(size.Width / 2, size.Height / 2), size, bounds,
                        FarmMouse.Middle, decision.HoldMs, token) ? "中键" : null;
                case FarmRequest.AdjustDirection:
                    if (!farmInput.KeyDown(decision.Key))
                        return null;
                    try
                    {
                        if (token.WaitHandle.WaitOne(100))
                            return null;
                        return farmInput.Click(new Point(size.Width / 2, size.Height / 2), size, bounds,
                            FarmMouse.Middle, 100, token) ? "修正行进方向" : null;
                    }
                    finally { farmInput.KeyUp(decision.Key); }
                case FarmRequest.Key:
                    if (decision.PickupKey)
                        return farmInput.KeyCode('F', 90, token, useWindowMessage: decision.RetryInput) ? "F 吸收" : null;
                    if (decision.StartsRechallenge)
                        return farmInput.KeyCode('F', 90, token, useWindowMessage: decision.RetryInput) ? "F 重新挑战" : null;
                    if (decision.VirtualKey != 0)
                        return farmInput.KeyCode(decision.VirtualKey, 60, token) ? "功能键" : null;
                    if (decision.Key == 0x1B)
                        return farmInput.KeyCode(0x1B, 60, token, useWindowMessage: decision.RetryInput) ? "Esc" : null;
                    return farmInput.Key(decision.Key, decision.HoldMs > 0 ? decision.HoldMs : 60, token) ? decision.Key.ToString() : null;
                case FarmRequest.KeyDown:
                    if (!farmInput.KeyDown(decision.Key))
                        return null;
                    movement = decision.Key;
                    return decision.Key.ToString();
                case FarmRequest.KeyUp:
                    if (decision.Key == movement) movement = '\0';
                    farmInput.KeyUp(decision.Key);
                    return null;
                case FarmRequest.Text:
                    return farmInput.TypeText(decision.Text, token) ? "输入文字" : null;
                case FarmRequest.Scroll:
                    return farmInput.Scroll(decision.ScrollTicks, new Point(size.Width / 2, size.Height / 2), size, bounds, token)
                        ? "滚动" : null;
                default:
                    return null;
            }
        }
    }
}
