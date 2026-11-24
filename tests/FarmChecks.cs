using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace TideCombat
{
    // Offline checks for the 4C farm state machine. They drive Farm4C with
    // synthetic HUD/UI states so the flow can be validated without the game.
    internal static class FarmChecks
    {
        private static void Require(bool condition, string explanation)
        {
            if (!condition)
                throw new InvalidOperationException(explanation);
        }

        private static FarmUi Ui() { return new FarmUi(); }

        private static HudSnapshot Hud(bool enemy)
        {
            var hud = new HudSnapshot { Valid = true, PartySize = 1 };
            hud.Current = 0;
            hud.Health[0] = 1;
            hud.Fighters[0] = "Carlotta";
            hud.EnemyHealth = enemy;
            hud.Locked = enemy;
            return hud;
        }

        private static FarmDecision Step(Farm4C farm, HudSnapshot hud, FarmUi ui, ref long now, int advance = 300,
            System.Collections.Generic.List<EchoBox> echoes = null,
            System.Collections.Generic.List<OcrLine> levels = null, bool absorb = false, bool receipt = false,
            bool dispatch = true, double navTurn = double.NaN, string bossName = null)
        {
            now += advance;
            FarmDecision decision = farm.Observe(new FarmFrame(hud, hud != null && hud.Valid, ui, new Size(1600, 900),
                new Rectangle(0, 0, 1920, 1080), echoes, navTurn, levels,
                absorb ? EchoPrompt.Selected : EchoPrompt.None, bossName: bossName, pickupReceipt: receipt), now);
            if (dispatch)
                farm.Sent(decision, now);
            return decision;
        }

        private static string TensorHash(PixelFrame source)
        {
            var planes = new float[3 * 640 * 640];
            double gain = Math.Min(640.0 / source.Width, 640.0 / source.Height);
            int width = (int)Math.Round(source.Width * gain), height = (int)Math.Round(source.Height * gain);
            EchoDetector.FillLetterbox(source, planes, width, height, (640 - width) / 2, (640 - height) / 2);
            var bytes = new byte[planes.Length * sizeof(float)];
            Buffer.BlockCopy(planes, 0, bytes, 0, bytes.Length);
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        internal static void EchoInputPixels()
        {
            int[] widths = { 1279, 7, 457, 1280 }, heights = { 721, 3, 1137, 720 };
            // Independent OpenCV INTER_LINEAR / 8-bit RGB preprocessing results.
            string[] hashes = {
                "c29f682fdba1d5d4ad6a6af7346902f7bc2632e3783939a35d5a5f7e30f43205",
                "ba7a56ed0d350ad01ced495f83de890acaee568ab5a66f15db5f2ab51ab0de87",
                "488433bf8ce0dbbbd0d05033cc695f1598ed9f772cea288e244d726223521e8b",
                "c1385f1be5c4c445d67b8120b4e4f021ac136b7280db6560471989742eb0bb1f" };
            for (int i = 0; i < widths.Length; i++)
            {
                var pixels = new byte[widths[i] * heights[i] * 4];
                for (int y = 0; y < heights[i]; y++)
                    for (int x = 0; x < widths[i]; x++)
                    {
                        int offset = (y * widths[i] + x) * 4;
                        pixels[offset] = (byte)((x * 41 + y * 67) % 256);
                        pixels[offset + 1] = (byte)((x * 19 + y * 13) % 256);
                        pixels[offset + 2] = (byte)((x * 7 + y * 29) % 256);
                    }
                Require(TensorHash(new PixelFrame(pixels, widths[i], heights[i])) == hashes[i], "检测输入逐像素比对失败 " + widths[i]);
            }
            using (var reader = new FrameReader())
            using (var bitmap = new Bitmap(16, 12))
            {
                bitmap.SetPixel(10, 8, Color.FromArgb(33, 27, 75));
                Rectangle crop = new Rectangle(4, 3, 9, 7);
                PixelFrame input = reader.Read(bitmap, crop, true);
                Require(input.HasSourcePixels && input.FullResolution.Width == 9 && input.FullResolution.Height == 7, "检测输入应保留原始窗口裁切尺寸");
                int red, green, blue;
                input.FullResolution.Rgb(6, 5, out red, out green, out blue);
                Require(red == 33 && green == 27 && blue == 75, "窗口裁切像素行距或起点错误");
                Require(!reader.Read(bitmap, crop).HasSourcePixels, "普通 HUD 帧不应带有上一帧的检测图像");
            }
        }

        internal static void ReplayEchoDetector(string root)
        {
            string[] files = { "echo.png", "echo2.png" };
            string[] hashes = { "1d70a7eb101c9c665033cd7406a4b982aa89f1b45935da7f6b8a790c303ad0d8",
                "1cca88e137f29db55c3faab84320db69cfae45c863af64b2a9ee4f7a1b813447" };
            Rectangle[] boxes = { new Rectangle(1, 0, 384, 869), new Rectangle(1358, 671, 779, 399) };
            float[] scores = { .357634f, .835042f };
            using (var reader = new FrameReader())
            using (var detector = new EchoDetector(EchoModel.ModelPath))
                for (int i = 0; i < files.Length; i++)
                    using (var image = new Bitmap(Path.Combine(root, "tests", "images", files[i])))
                    {
                        PixelFrame frame = reader.Read(image, true);
                        Require(TensorHash(frame.FullResolution) == hashes[i], files[i] + " 的检测输入与原始截图不符");
                        var detections = detector.Detect(frame, .25f);
                        Require(detections.Count == 1, files[i] + " 的检测数量错误 " + detections.Count);
                        Require(detector.Detect(frame, .30f).Count == 1, files[i] + " 在 .30 的置信度下应检测到一只声骸");
                        EchoBox first = detections[0];
                        Require(first.Box == boxes[i] && first.SourceSize == image.Size &&
                            Math.Abs(first.Score - scores[i]) < .001f, files[i] + " 的声骸框或分数与独立推理不符 " +
                            first.Box + " score=" + first.Score);
                        Require(detector.Detect(frame, .65f).Count == i, files[i] + " 检测阈值未生效");
                    }
        }

        internal static void MultiEchoOrder(string root)
        {
            using (var source = new Bitmap(Path.Combine(root, "tests", "images", "echo2.png")))
            using (var pair = new Bitmap(source.Width * 2, source.Height))
            using (var reader = new FrameReader())
            using (var detector = new EchoDetector(EchoModel.ModelPath))
            {
                using (Graphics graphics = Graphics.FromImage(pair))
                {
                    graphics.DrawImageUnscaled(source, 0, 0);
                    graphics.DrawImageUnscaled(source, source.Width, 0);
                    graphics.FillRectangle(Brushes.Black, 0, 0, source.Width, 720);
                }
                List<EchoBox> boxes = detector.Detect(reader.Read(pair, true), .25f);
                Require(boxes.Count == 2 && boxes[0].Score < boxes[1].Score &&
                    boxes[0].Box == new Rectangle(1379, 727, 731, 378) && boxes[1].Box == new Rectangle(5206, 694, 719, 391),
                    "同一排应先靠近左侧声骸，即使右侧候选置信度更高");
                var approach = new EchoApproach();
                Require(approach.Direction(boxes, new Size(3200, 900), '\0', 0) == 'A',
                    "靠近时使用候选原图坐标");
            }
            var nearBoundary = new List<EchoBox> { new EchoBox { Box = new Rectangle(419, 130, 7, 90) } };
            var steering = new EchoApproach();
            Require(steering.Direction(nearBoundary, new Size(801, 459), '\0', 0) == 'D',
                "奇数分辨率应使用整数准星、整数阈值和取整后的候选中心");
        }

        internal static void Signatures()
        {
            foreach (string label in FarmIndicators.All)
                Require(PatternBank.For(label).Length > 0, "缺少签名: " + label);
        }

        internal static void StateMachine()
        {
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            long now = 1000;
            var idle = Ui();
            Step(farm, null, idle, ref now);
            Require(farm.Status.Contains("游戏"), "启动后应等待游戏画面");

            FarmDecision probe = Step(farm, Hud(false), idle, ref now);
            Require(probe.Request == FarmRequest.Battle && probe.Battle.Kind == ActionKind.Target, "应立即原地尝试锁敌");
            FarmDecision walk = Step(farm, Hud(false), idle, ref now, 1500, navTurn: 0);
            Require(walk.Request == FarmRequest.KeyDown && walk.Key == 'W', "确认导航方向后才前进");

            var prompt = Ui();
            prompt.FPrompt = true;
            prompt.FBox = new Rectangle(1000, 500, 40, 40);
            FarmDecision press = Step(farm, Hud(false), prompt, ref now);
            Require(press.Request == FarmRequest.Key && press.Key == 'F', "看到 F 提示应按 F");
            Require(farm.MovementKey == '\0' && !farm.WantsSprint, "看到 F 应松开方向键和奔跑");

            var challenge = Ui();
            challenge.Challenge = true;
            challenge.TeamStart = true;
            challenge.TeamStartBox = new Rectangle(1450, 795, 90, 60);
            bool clicked = false;
            for (int index = 0; index < 12; index++)
                if (Step(farm, Hud(false), challenge, ref now).Request == FarmRequest.Click)
                    clicked = true;
            Require(clicked, "挑战面板应触发点击");

            bool fought = false;
            for (int index = 0; index < 10; index++)
            {
                FarmDecision decision = Step(farm, Hud(true), idle, ref now, 400);
                if (decision.Request == FarmRequest.Battle && decision.Battle.Kind != ActionKind.Wait)
                    fought = true;
            }
            Require(fought, "战斗中应委派战斗动作");

            for (int index = 0; index < 12; index++)
                Step(farm, Hud(false), idle, ref now, 400);
            Require(farm.Kills == 1, "战斗结束应计入一次击杀");

            var f = Ui();
            f.FPrompt = true;
            FarmDecision pickup = Step(farm, Hud(false), f, ref now, 400, absorb: true);
            Require(pickup.Request == FarmRequest.Key && pickup.Key == 'F', "结算后应尝试拾取");
        }

        internal static void GuideEntrance()
        {
            var options = FarmOptions.Defaults();
            options.Teleport = true;
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            long now = 1000;
            var terminal = new FarmUi { Terminal = true, TerminalGuide = new Rectangle(760, 594, 68, 52) };
            Step(farm, Hud(false), terminal, ref now, 100);
            var click = Step(farm, Hud(false), terminal, ref now, 100);
            Require(click.Request == FarmRequest.Click && click.Point == new Point(794, 620),
                "从终端中的索拉指南图标进入，终端画面优先于残留 HUD");

            var bindings = CombatSettings.Defaults();
            bindings.GuidebookKey = "g";
            farm = new Farm4C(bindings, options);
            Step(farm, Hud(false), Ui(), ref now, 100);
            var first = Step(farm, Hud(false), Ui(), ref now, 5000);
            var second = Step(farm, Hud(false), Ui(), ref now, 5000);
            Require(first.VirtualKey == 'G' && second.Request == FarmRequest.AltClick,
                "自定义图鉴键未生效后使用 Alt 加索拉指南入口");
        }

        internal static void TeleportFlow()
        {
            FarmOptions options = FarmOptions.Defaults();
            options.Teleport = true;
            options.TeleportMode = FarmTeleportMode.BossChallenge;
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            long now = 1000;
            Step(farm, Hud(false), Ui(), ref now, 100);
            FarmDecision open = Step(farm, Hud(false), Ui(), ref now, 100);
            Require(open.Request == FarmRequest.Key && open.VirtualKey == 0x71, "自动传送应打开 F2 图鉴");

            var book = Ui();
            book.Book = true;
            book.BookBox = new Rectangle(45, 250, 26, 40);
            Step(farm, Hud(false), book, ref now, 500);
            bool category = false, kind = false;
            for (int index = 0; index < 20; index++)
            {
                FarmDecision decision = Step(farm, Hud(false), book, ref now, 500);
                if (decision.Request != FarmRequest.Click) continue;
                if (Math.Abs(decision.Point.X - 58) < 20)
                {
                    category = true;
                    book.BookSelected = true;
                }
                if (Math.Abs(decision.Point.X - 384) < 20) kind = true;
            }
            Require(category, "应点击左侧 Boss 图鉴页签");
            Require(kind, "应点击二级分类页签");
        }

        internal static void ClaimRestart()
        {
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            long now = 1000;
            Step(farm, null, Ui(), ref now, 100);
            Step(farm, Hud(false), Ui(), ref now, 100);
            for (int index = 0; index < 6; index++)
                Step(farm, Hud(true), Ui(), ref now, 400);
            for (int index = 0; index < 10; index++)
                Step(farm, Hud(false), Ui(), ref now, 400);
            Require(farm.Kills == 1, "应先完成一次击杀");

            var claim = Ui();
            claim.Claim = true;
            FarmDecision escape = Step(farm, Hud(false), claim, ref now, 400);
            if (escape.Request == FarmRequest.None)
                escape = Step(farm, Hud(false), claim, ref now, 700);
            Require(escape.Request == FarmRequest.Key && escape.Key == '\u001b', "领奖弹窗应发 ESC");

            var challenge = Ui();
            challenge.Challenge = true;
            challenge.TeamStart = true;
            challenge.TeamStartBox = new Rectangle(1450, 795, 90, 60);
            bool clicked = false;
            for (int index = 0; index < 10; index++)
                if (Step(farm, Hud(false), challenge, ref now, 400).Request == FarmRequest.Click)
                    clicked = true;
            Require(clicked, "关闭领奖后应重新点击挑战");
        }

        internal static void TeleportBossRow()
        {
            FarmOptions options = FarmOptions.Defaults();
            options.Teleport = true;
            options.BossIndex = 2;
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            long now = 1000;
            Step(farm, Hud(false), Ui(), ref now, 100);
            Step(farm, Hud(false), Ui(), ref now, 100);
            var book = Ui();
            book.Book = true;
            book.BookSelected = true;
            book.BossRows = new System.Collections.Generic.List<Rectangle>
            {
                new Rectangle(1480, 300, 30, 50),
                new Rectangle(1480, 400, 30, 50)
            };
            bool second = false;
            for (int index = 0; index < 16; index++)
            {
                FarmDecision decision = Step(farm, Hud(false), book, ref now, 500);
                if (decision.Request == FarmRequest.Click && decision.Point.Y > 380)
                    second = true;
            }
            Require(second, "应按序号点击第二个 Boss 行");
        }

        internal static void EchoEndsCombat()
        {
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            long now = 1000;
            Step(farm, null, Ui(), ref now, 100);
            Step(farm, Hud(false), Ui(), ref now, 100);
            for (int index = 0; index < 6; index++)
                Step(farm, Hud(true), Ui(), ref now, 400, null);
            Require(farm.Attempts == 1 && farm.Kills == 0 && farm.EchoThreshold(Ui()) == .30f,
                "进入战斗即计尝试轮次，战中固定使用 .30 检测阈值");
            var echoes = new System.Collections.Generic.List<EchoBox>
            {
                new EchoBox { Box = new Rectangle(780, 640, 40, 30), Score = .8f }
            };
            for (int index = 0; index < 6; index++)
                Step(farm, Hud(true), Ui(), ref now, 300, echoes);
            Require(farm.Kills == 0 && farm.Planner.Fighting, "有敌人血条时不应因声骸识别而结束战斗");
            var unlockedHealth = Hud(true);
            unlockedHealth.Locked = false;
            for (int i = 0; i < 10 && farm.Phase == "Fight"; i++)
                Step(farm, unlockedHealth, Ui(), ref now, 300, echoes);
            Require(farm.Kills == 1 && farm.Attempts == 1 && farm.Phase == "Loot",
                "PC 未锁敌时即使仍有血条，已确认掉落可结束战斗");
            Step(farm, unlockedHealth, Ui(), ref now, 300, echoes);
            Require(farm.Phase == "Loot", "仅残余血条不能立即覆盖掉落结束判定");

            var cloud = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults(), cloud: true);
            Step(cloud, Hud(false), Ui(), ref now);
            for (int i = 0; i < 6; i++) Step(cloud, Hud(true), Ui(), ref now, 400);
            for (int i = 0; i < 6; i++) Step(cloud, unlockedHealth, Ui(), ref now, 400, echoes);
            Require(cloud.Kills == 0, "云游戏有敌方血条时不提前结束");
            Step(cloud, Hud(false), Ui(), ref now, 300, echoes);
            Step(cloud, Hud(false), Ui(), ref now, 2999, echoes);
            Require(cloud.Kills == 0, "云端健康条消失不足三秒仍继续观察");
            for (int i = 0; i < 12 && cloud.Phase == "Fight"; i++)
                Step(cloud, Hud(false), Ui(), ref now, 400, echoes);
            Require(cloud.Kills == 1, "云端宽限后正常进入拾取");
        }

        private static Farm4C ReadyToPickUp(FarmOptions options, ref long now, Farm4C farm = null)
        {
            farm = farm ?? new Farm4C(CombatSettings.Defaults(), options);
            int previousKills = farm.Kills;
            var idle = Ui();
            Step(farm, Hud(false), idle, ref now);
            for (int i = 0; i < 6; i++)
                Step(farm, Hud(true), idle, ref now, 400);
            var drop = new List<EchoBox> { new EchoBox { Box = new Rectangle(780, 640, 40, 30), Score = .8f } };
            Step(farm, Hud(false), idle, ref now, 600, drop);
            Require(farm.Kills == previousKills + 1, "应进入战后拾取");
            return farm;
        }

        internal static void PickupAndRepeatLimit()
        {
            var options = FarmOptions.Defaults();
            options.RepeatCount = 1;
            long now = 1000;
            var farm = ReadyToPickUp(options, ref now);
            var idle = Ui();
            var prompt = Ui();
            prompt.FPrompt = true;
            FarmDecision stale = Step(farm, Hud(false), prompt, ref now, absorb: true, receipt: true);
            Require(stale.Request == FarmRequest.None, "前一条拾取通知还在时应等待");
            FarmDecision attempt = Step(farm, Hud(false), prompt, ref now, absorb: true);
            Require(attempt.PickupKey && farm.Echoes == 0, "发出 F 后仍需等待新通知");
            Step(farm, Hud(false), idle, ref now, receipt: true);
            Require(farm.Echoes == 0 && !farm.Finished, "单帧提示不能推进");
            Step(farm, Hud(false), idle, ref now, receipt: true);
            Require(farm.Echoes == 1, "连续两帧出现新通知才计入吸收");
            Step(farm, Hud(false), idle, ref now);
            var team = Ui();
            team.TeamStart = team.Challenge = true;
            team.TeamStartBox = new Rectangle(1400, 800, 100, 60);
            FarmDecision waitFinal = Step(farm, null, team, ref now, 4999);
            Require(!farm.Finished && waitFinal.Request == FarmRequest.None, "最后一轮确认掉落且无宝箱也等待五秒，不点击后续入口");
            FarmDecision finish = Step(farm, null, team, ref now, 1);
            Require(farm.Finished && farm.Failure == null && finish.Request == FarmRequest.None,
                "达到次数上限时不得点击下一轮组队按钮");
        }

        internal static void UnconfirmedDrop()
        {
            var options = FarmOptions.Defaults();
            long now = 1000;
            var farm = ReadyToPickUp(options, ref now);
            var prompt = Ui();
            prompt.InRealm = prompt.FPrompt = prompt.Absorb = true;
            FarmDecision rejected = Step(farm, Hud(false), prompt, ref now, absorb: true, dispatch: false);
            Require(rejected.PickupKey, "需要覆盖输入被窗口拒绝");
            FarmDecision first = Step(farm, Hud(false), prompt, ref now, 600, absorb: true);
            Require(first.PickupKey && !first.RetryInput, "被拒绝的 F 不能消耗拾取重试");
            FarmDecision second = Step(farm, Hud(false), prompt, ref now, 3000, absorb: true);
            Require(second.PickupKey && second.RetryInput, "未见通知时应切换按键方式补试一次");
            FarmDecision wait = Step(farm, Hud(false), prompt, ref now, 1000, absorb: true);
            Require(wait.Request == FarmRequest.None, "第二次尝试仍需等待获取通知");
            Step(farm, Hud(false), prompt, ref now, 1600, absorb: true);
            FarmDecision open = Step(farm, Hud(false), prompt, ref now, 1000, absorb: true);
            Require(open.Request == FarmRequest.Key && open.Key == 0x1b && !open.PickupKey &&
                farm.MovementKey == '\0' && !farm.Finished && farm.Echoes == 0,
                "两次未确认后应松开移动并打开重开菜单，即使仍显示吸收");
            var dialog = Ui();
            dialog.RechallengeButton = new Rectangle(485, 548, 105, 32);
            FarmDecision restart = Step(farm, null, dialog, ref now, 1100);
            Require(restart.Request == FarmRequest.Click && restart.StartsRechallenge,
                "确认弹窗中的重开按钮后续刷");

            options.RepeatCount = 1;
            var lastRound = ReadyToPickUp(options, ref now);
            Step(lastRound, Hud(false), prompt, ref now, absorb: true);
            Step(lastRound, Hud(false), prompt, ref now, 2600, absorb: true);
            Step(lastRound, Hud(false), prompt, ref now, 2600, absorb: true);
            Require(Step(lastRound, Hud(false), prompt, ref now, 1000, absorb: true).Request == FarmRequest.None &&
                lastRound.Finished && lastRound.Failure == null && lastRound.Echoes == 0,
                "最后一轮吸收未确认也应正常完成，不再打开重开菜单");
        }

        internal static void YoloPickupRecovery()
        {
            var options = FarmOptions.Defaults();
            options.Pickup = FarmPickupMode.Yolo;
            long now = 1000;
            var farm = ReadyToPickUp(options, ref now);
            var drop = new List<EchoBox> { new EchoBox { Box = new Rectangle(780, 640, 40, 30), Score = .8f } };
            var empty = new List<EchoBox>();
            var idle = Ui();
            Step(farm, Hud(false), idle, ref now, 400, drop);
            Require(farm.EchoThreshold(idle) == .65f && Step(farm, Hud(false), idle, ref now, 400, drop).Request == FarmRequest.None,
                "大世界首次发现后应等待新的追踪帧");
            Require(farm.EchoThreshold(idle) == .50f, "追踪阈值应切换到 0.5");
            FarmDecision back = Step(farm, Hud(false), idle, ref now, 100, drop);
            Require(back.Request == FarmRequest.KeyDown && back.Key == 'S',
                "声骸在准星下方时应后退接近");
            FarmDecision forward = Step(farm, Hud(false), idle, ref now, 600, empty);
            Require(forward.Request == FarmRequest.KeyDown && forward.Key == 'W' && !farm.Finished,
                "已检测到的声骸短暂被遮挡时应继续搜索");
            Step(farm, Hud(false), idle, ref now, 2800, empty);
            Require(farm.MovementKey == 'W', "消失计时从实际缺失的第一帧开始");
            Step(farm, Hud(false), idle, ref now, 210, empty);
            Require(!farm.Finished && farm.Echoes == 0 && farm.MovementKey == '\0',
                "超过搜寻上限后应松开移动且不增加吸收统计");
            var settlement = Ui();
            settlement.InRealm = settlement.FPrompt = true;
            settlement.Rechallenge = RechallengeChoice.Selected;
            Require(Step(farm, Hud(false), settlement, ref now, 1000).Request == FarmRequest.None,
                "结算后先等待一秒与0.2秒交互稳定");
            FarmDecision restart = Step(farm, Hud(false), settlement, ref now, 200);
            Require(restart.Request == FarmRequest.Key && restart.StartsRechallenge,
                "只看见过掉落时也应在搜寻结束后重新挑战");

            var noPrompt = ReadyToPickUp(options, ref now);
            var realm = Ui();
            realm.InRealm = true;
            Step(noPrompt, Hud(false), realm, ref now, 400, drop);
            Step(noPrompt, Hud(false), idle, ref now, 200, drop);
            Require(noPrompt.EchoThreshold(idle) == .25f, "副本寻找阈值应为 0.25");
            Step(noPrompt, Hud(false), idle, ref now, 1000, drop);
            var anchor = new List<EchoBox> { new EchoBox { Box = new Rectangle(781, 480, 38, 150), Score = .71f } };
            Require(noPrompt.EchoThreshold(idle) == .50f && Step(noPrompt, Hud(false), idle, ref now, 100, anchor).Key == 'W',
                "追踪方向使用框高三分之一处的目标位置");
            Step(noPrompt, Hud(false), idle, ref now, 100, empty);
            Step(noPrompt, Hud(false), idle, ref now, 2800, anchor);
            Step(noPrompt, Hud(false), idle, ref now, 2100, empty);
            Require(noPrompt.MovementKey == 'W', "再次识别目标后应清除上次丢失计时");
            Step(noPrompt, Hud(false), idle, ref now, 3100, empty);
            FarmDecision open = Step(noPrompt, Hud(false), idle, ref now, 2700, empty);
            Require(open.Request == FarmRequest.Key && open.Key == 0x1b && !noPrompt.Finished,
                "搜寻结束后没有宝箱或交互提示时，已确认的副本仍可走 Esc 重开");
        }

        internal static void PopupRetryBudget()
        {
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            var claim = new FarmUi { Claim = true, ClaimCancelBox = new Rectangle(400, 600, 100, 45) };
            int handlers = 0;
            long now = 0;
            Func<bool> dismiss = () => { handlers++; return true; };
            Require(farm.RestartAfterPopup(dismiss) == null && handlers == 0, "正常任务不应处理故障弹窗");
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Step(farm, null, claim, ref now);
                Require(farm.Finished && farm.Failure != null, "启动时有领奖对话框应进入故障处理");
                if (attempt == 0)
                    Require(farm.RestartAfterPopup(() => false) == null, "未确认关闭时应保留故障");
                var next = farm.RestartAfterPopup(dismiss);
                if (attempt == 3)
                    Require(next == null && handlers == 3, "用尽三次后不能再发出清除输入");
                else
                {
                    Require(next != null && next.AtStart && next.Failure == null, "确认关闭后应重新开始");
                    farm = next;
                }
            }
        }

        private static void ConfirmDrop(Farm4C farm, ref long now)
        {
            var prompt = Ui();
            prompt.FPrompt = true;
            Require(Step(farm, Hud(false), prompt, ref now, absorb: true).PickupKey, "近身可拾取时应直接按 F");
            Step(farm, Hud(false), Ui(), ref now, receipt: true);
            Step(farm, Hud(false), Ui(), ref now, receipt: true);
        }

        internal static void EchoRateAfterRetry()
        {
            long now = 0;
            var options = FarmOptions.Defaults();
            DateTime clock = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var farm = new Farm4C(CombatSettings.Defaults(), options, clock: () => clock);
            clock = clock.AddHours(1);
            farm.Stop("未获得声骸时遇到弹窗");
            farm = farm.RestartAfterPopup(() => true);
            ReadyToPickUp(options, ref now, farm);
            clock = clock.AddMilliseconds(100);
            ConfirmDrop(farm, ref now);
            Require(farm.Echoes == 1 && farm.EchoesPerHour == 3600, "首轮用时少于一秒时按一秒计算");
            clock = clock.AddMilliseconds(3599900);
            Require(farm.EchoesPerHour == 3600, "两次吸收之间保留上次显示的速率");
            farm.Stop("已获得声骸时遇到弹窗");
            farm = farm.RestartAfterPopup(() => true);
            Require(farm.Echoes == 1 && farm.Kills == 1 && farm.EchoesPerHour == 3600, "重试时保留累计战果");
            ReadyToPickUp(options, ref now, farm);
            ConfirmDrop(farm, ref now);
            Require(farm.Echoes == 2 && farm.EchoesPerHour == 2, "重试有战果时从最初开始刷取的时刻计算");
        }

        internal static void YoloViews()
        {
            var options = FarmOptions.Defaults();
            options.Pickup = FarmPickupMode.Yolo;
            long now = 1000;
            var empty = new List<EchoBox>();
            foreach (bool realm in new[] { false, true })
            {
                var farm = ReadyToPickUp(options, ref now);
                var ui = Ui();
                ui.InRealm = realm;
                Require(Step(farm, Hud(false), ui, ref now, dispatch: false).Request == FarmRequest.Middle &&
                    !farm.WantsEchoDetection(now), "未发出的中键不应略过视角复位");
                Require(Step(farm, Hud(false), ui, ref now).Request == FarmRequest.Middle, "首次拾取失败应复位视角");
                Require(Step(farm, Hud(false), ui, ref now, 199, empty).Request == FarmRequest.None, "中键后应等待 0.2 秒");
                if (realm)
                {
                    for (int view = 0; view <= 4; view++)
                    {
                        FarmDecision center = Step(farm, Hud(false), ui, ref now, view == 0 ? 1 : 500, empty);
                        Require(center.Request == FarmRequest.Middle && center.HoldMs == 200, "每个副本视角和结束时均中键居中");
                        Require(Step(farm, Hud(false), ui, ref now, 999, empty).Request == FarmRequest.None, "视角居中后须等待一秒");
                        if (view == 4) break;
                        FarmDecision shift = Step(farm, Hud(false), ui, ref now, 1, empty, dispatch: false);
                        Require(shift.Key == 'A' && shift.HoldMs == 50, "缺失时左移五十毫秒");
                        Require(Step(farm, Hud(false), ui, ref now, 1, empty).Key == 'A', "输入被拒绝时应重试当前视角");
                    }
                }
                Step(farm, Hud(false), ui, ref now, 1, empty);
                Require(farm.Phase != "Loot" && farm.MovementKey == 0 && farm.Echoes == 0,
                    "大世界看一次、副本看四次后结束搜索");
            }
        }

        internal static void WalkAndCirclePickup()
        {
            var options = FarmOptions.Defaults();
            long now = 1000;
            var ui = Ui();
            foreach (FarmPickupMode mode in new[] { FarmPickupMode.Walk, FarmPickupMode.Circle, FarmPickupMode.Yolo })
            {
                options.Pickup = mode;
                var direct = ReadyToPickUp(options, ref now);
                ConfirmDrop(direct, ref now);
                Require(direct.Echoes == 1 && direct.MovementKey == 0, "可直接拾取时各模式均跳过搜寻");
                if (mode == FarmPickupMode.Yolo) continue;
                var farm = ReadyToPickUp(options, ref now);
                Require(Step(farm, Hud(false), ui, ref now).Request == FarmRequest.Middle, "移动搜寻前复位视角");
                if (mode == FarmPickupMode.Walk)
                    Require(Step(farm, Hud(false), ui, ref now, 200).Request == FarmRequest.Middle, "直走检查交互后再次朝前");
                string expected = mode == FarmPickupMode.Walk ? "SW" : "WASDWASD";
                int[] durations = mode == FarmPickupMode.Walk ? new[] { 1000, 3000 } : new[] { 800, 800, 800, 1600, 1600, 2400, 2400, 2400 };
                for (int i = 0; i < expected.Length; i++)
                {
                    FarmDecision direction = Step(farm, Hud(false), ui, ref now, i == 0 ? 200 : 1);
                    Require(direction.Request == FarmRequest.KeyDown && direction.Key == expected[i] &&
                        farm.WantsSprint == (mode == FarmPickupMode.Circle), "搜寻方向与奔跑模式应匹配");
                    Require(Step(farm, Hud(false), ui, ref now, durations[i] - 1).Request == FarmRequest.None, "达到时限前应保持方向");
                }
                Step(farm, Hud(false), ui, ref now, 1);
                Require(farm.MovementKey == 0 && farm.Phase != "Loot", "完成全程后应松开移动");
            }
        }

        internal static void StationaryTargetSearch()
        {
            foreach (bool realm in new[] { false, true })
            {
                var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
                var idle = Ui();
                idle.InRealm = realm;
                long now = 1000;
                var hud = Hud(false);
                hud.ChallengeMarker = realm;
                Step(farm, hud, idle, ref now);
                bool probed = false;
                for (int i = 0; i < 20; i++)
                {
                    // An old world-map direction is invalid inside a challenge.
                    FarmDecision decision = Step(farm, hud, idle, ref now, navTurn: realm ? 0 : double.NaN);
                    probed |= decision.Request == FarmRequest.Battle && decision.Battle.Kind == ActionKind.Target;
                    Require(farm.MovementKey == '\0' && !farm.WantsSprint &&
                        decision.Request != FarmRequest.KeyDown, "无有效引导时不应盲走");
                }
                Require(probed && farm.Finished && farm.Failure.Contains("未锁定"), "原地锁敌超时后应停止");
            }
        }

        internal static void ApproachStopsOnLostEvidence()
        {
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            var idle = Ui();
            long now = 1000;
            Step(farm, Hud(false), idle, ref now);
            Step(farm, Hud(false), idle, ref now, 1500, navTurn: 0);
            Require(farm.MovementKey == 'W' && farm.WantsSprint, "有明确引导时应走向 Boss");
            Step(farm, Hud(false), idle, ref now, 300);
            Require(farm.MovementKey == 'W' && farm.WantsSprint, "已确认导航期间临时缺失标记按0度继续");
            Step(farm, null, idle, ref now);
            Require(farm.MovementKey == '\0' && !farm.WantsSprint, "丢失场景验证应立刻停止");
            Step(farm, Hud(false), idle, ref now, navTurn: 0);
            Require(farm.MovementKey == 'W', "重新确认方向后可以再次接近");
            var challenge = Hud(false);
            challenge.ChallengePrompt = true;
            FarmDecision start = Step(farm, challenge, idle, ref now, navTurn: 0);
            Require(start.Request == FarmRequest.Key && start.Key == 'F' && farm.MovementKey == '\0',
                "识别到场内挑战时，松开 W 并触发挑战");

            var world = new FarmUi { OpenWorld = true };
            var verified = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            Step(verified, Hud(false), world, ref now);
            Step(verified, Hud(false), world, ref now, 1500);
            Require(verified.MovementKey == 'W', "初始即确认大世界时，无标记按零度前进");
            Step(verified, Hud(false), world, ref now, 13500);
            Require(verified.Phase == "MapTeleport" && verified.MovementKey == '\0', "导航十五秒无目标执行回传兜底");
        }

        internal static void TravelWalkIsBounded()
        {
            var options = FarmOptions.Defaults();
            options.Teleport = true;
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            var idle = Ui();
            long now = 1000;
            Step(farm, Hud(false), idle, ref now);
            var book = Ui();
            book.Book = book.BookSelected = true;
            book.BossRows.Add(new Rectangle(1480, 320, 40, 46));
            Step(farm, null, book, ref now);
            Step(farm, null, book, ref now);
            Step(farm, null, book, ref now, 2200);
            var transport = Ui();
            transport.Teleport = true;
            transport.TeleportBox = new Rectangle(1300, 700, 90, 60);
            Require(Step(farm, null, transport, ref now, 1100).Request == FarmRequest.Click, "点击传送");
            Step(farm, null, idle, ref now, 1000);
            Step(farm, Hud(false), idle, ref now, 600);
            Step(farm, Hud(false), idle, ref now, 1000);
            Require(farm.MovementKey == '\0', "落地后先原地锁敌");
            Step(farm, Hud(false), idle, ref now, 600);
            Require(farm.MovementKey == 'W', "已确认传送落地时允许短距离向前");
            Step(farm, Hud(false), idle, ref now, 3000);
            Require(farm.MovementKey == 'W' && !farm.Finished, "落地后未发现标记时不应在三秒过早停止");
            Step(farm, Hud(false), idle, ref now, 16800);
            Require(farm.MovementKey == 'W' && !farm.Finished, "确认落地后向前跑最多20秒");
            Step(farm, Hud(false), idle, ref now, 100);
            Require(farm.Finished && farm.Failure.Contains("未锁定"), "落地无目标时应停止并给出原因");
            Require(farm.RestartAfterWalkInFailure() == null, "未找到战斗不是已走入战斗后的回传异常");

            var directCombat = new Farm4C(CombatSettings.Defaults(), options);
            EnterConfiguredWorldCombat(directCombat, ref now);
            Require(directCombat.Phase == "Fight" && directCombat.Attempts == 1,
                "传送后成功直接走入战斗");
            Step(directCombat, Hud(true), new FarmUi { InRealm = true }, ref now);
            Require(!directCombat.Realm, "确认配置传送直接接敌后，参考 treat-as-world 覆盖不能被普通副本采样推翻");
            directCombat.Stop("可恢复的刷取状态失败");
            var retry = directCombat.RestartAfterWalkInFailure();
            Require(retry != null && retry.AtStart && retry.Attempts == 1,
                "已走入首领战斗后的状态失败允许重新传送并保留原轮数");

            var neverTravels = new Farm4C(CombatSettings.Defaults(), options);
            Step(neverTravels, Hud(false), idle, ref now);
            Step(neverTravels, Hud(false), idle, ref now, 31000);
            Require(neverTravels.Finished && neverTravels.Failure != null &&
                neverTravels.RestartAfterWalkInFailure() == null, "配置传送自身超时直接失败，不进行走入战斗异常重传");
            WalkInExecutionRecovery();
        }

        private static void EnterConfiguredWorldCombat(Farm4C farm, ref long now)
        {
            var idle = Ui();
            var book = new FarmUi { Book = true, BookSelected = true };
            book.BossRows.Add(new Rectangle(1480, 320, 40, 46));
            var transport = new FarmUi { Teleport = true, TeleportBox = new Rectangle(1300, 700, 90, 60) };
            Step(farm, Hud(false), idle, ref now);
            Step(farm, null, book, ref now);
            Step(farm, null, book, ref now);
            Step(farm, null, book, ref now, 2200);
            Require(Step(farm, null, transport, ref now, 1100).Request == FarmRequest.Click, "配置首领传送控件");
            Step(farm, null, idle, ref now, 1000);
            Step(farm, Hud(false), idle, ref now, 600);
            Step(farm, Hud(true), idle, ref now);
            Require(farm.Phase == "Fight" && farm.ConfiguredWalkCombat, "已确认配置传送直接走入首领战斗");
        }

        private static Exception DetectorError(string codeName)
        {
            // The shipped wrapper's exception constructor is internal. This
            // constructs the exact managed type without running an ONNX model.
            Type exceptionType = typeof(Microsoft.ML.OnnxRuntime.OnnxRuntimeException);
            Type codeType = exceptionType.Assembly.GetType("Microsoft.ML.OnnxRuntime.ErrorCode", true);
            object code = Enum.Parse(codeType, codeName);
            return (Exception)Activator.CreateInstance(exceptionType,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, new object[] { code, "synthetic detector execution failure" }, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void WalkInExecutionRecovery()
        {
            var engine = DetectorError("EngineError");
            var runtime = DetectorError("RuntimeException");
            var invalidGraph = DetectorError("InvalidGraph");
            var invalidArgs = DetectorError("InvalidArgument");
            var unknownOrt = DetectorError("Fail");
            Require(Farm4C.IsRecoverableDetectorExecutionFailure(engine) && Farm4C.IsRecoverableDetectorExecutionFailure(runtime),
                "仅已加载模型的ONNX引擎及执行错误属于可恢复候选");
            foreach (Exception error in new[] { invalidGraph, invalidArgs, unknownOrt,
                new InvalidOperationException(engine.Message), new OperationCanceledException(),
                new UnauthorizedAccessException(), new InputElevationRequiredException() })
                Require(!Farm4C.IsRecoverableDetectorExecutionFailure(error), "不把未知、永久、伪造消息、取消及权限错误当成可恢复执行异常");

            var options = FarmOptions.Defaults();
            options.Teleport = true;
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            long now = 1000;
            Require(farm.RestartAfterWalkInExecutionFailure(runtime, true, false) == null,
                "尚未走入配置首领战斗时不得重传");
            for (int attempt = 1; attempt <= 4; attempt++)
            {
                EnterConfiguredWorldCombat(farm, ref now);
                Require(farm.Attempts == attempt && farm.Kills == 0 && farm.Echoes == 0,
                    "执行失败不虚增击杀或掉落，尝试次数仍保留");
                Require(farm.RestartAfterWalkInExecutionFailure(runtime, false, false) == null &&
                    farm.RestartAfterWalkInExecutionFailure(runtime, true, true) == null &&
                    farm.RestartAfterWalkInExecutionFailure(invalidGraph, true, false) == null,
                    "重传前要求同一有效输入路由、未取消、明确可恢复错误");
                var resumed = farm.RestartAfterWalkInExecutionFailure(attempt % 2 == 0 ? engine : runtime, true, false);
                if (attempt == 4)
                    Require(resumed == null, "三次连续恢复后停止，不无限重传");
                else
                {
                    Require(resumed != null && resumed.AtStart && resumed.Attempts == attempt &&
                        resumed.Kills == 0 && resumed.Echoes == 0, "恢复重新进入配置入口并保留全部统计");
                    farm = resumed;
                }
            }
            var manual = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            Step(manual, Hud(false), Ui(), ref now);
            Step(manual, Hud(true), Ui(), ref now);
            Require(manual.Phase == "Fight" && manual.RestartAfterWalkInExecutionFailure(runtime, true, false) == null,
                "手动进入世界首领不能擅自切换为配置目标重传");
            options.RepeatCount = 1;
            var lastRound = new Farm4C(CombatSettings.Defaults(), options);
            EnterConfiguredWorldCombat(lastRound, ref now);
            Require(lastRound.RestartAfterWalkInExecutionFailure(runtime, true, false) == null,
                "已达到设定尝试轮次时不得再发起多余重传");
        }

        internal static void ManualStartAcceptsTeamScreen()
        {
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            var team = Ui();
            team.TeamStart = team.Challenge = true;
            team.TeamStartBox = new Rectangle(1450, 790, 80, 60);
            long now = 1000;
            FarmDecision start = Step(farm, null, team, ref now);
            Require(start.Request == FarmRequest.Click && start.Point == new Point(1490, 820),
                "从队伍界面直接启动时，应点击已识别的开启挑战按钮");
            Require(Step(farm, null, Ui(), ref now, 4000).Request == FarmRequest.None && !farm.Finished,
                "等待加载时不应盲点或移动");
        }

        internal static void ManualStartRecognizesVerifiedPartialHud()
        {
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            long now = 1000;
            var world = Ui();
            world.FPrompt = true;
            world.FBox = new Rectangle(1000, 500, 40, 40);
            Require(Step(farm, null, world, ref now).Request == FarmRequest.None, "无场景证据时不可按 F");
            world.OpenWorld = true;
            var hud = Hud(false);
            hud.Valid = false;
            hud.Detail = "队伍栏可见，技能栏未确认";
            FarmDecision entry = Step(farm, hud, world, ref now);
            Require(entry.Request == FarmRequest.Key && entry.Key == 'F' && farm.MovementKey == '\0',
                "队伍和场景已确认时，允许操作清晰的 F 入口");
            FarmDecision target = Step(farm, Hud(false), Ui(), ref now, 850);
            Require(target.Request == FarmRequest.Battle && target.Battle.Kind == ActionKind.Target,
                "按 F 后若仍在场景，应主动锁敌以处理直接进入的战斗");
        }

        internal static void ManualRealmStartupWalkIsBounded()
        {
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            long now = 1000;
            var realm = Ui();
            realm.InRealm = true;
            FarmDecision initial = Step(farm, Hud(false), realm, ref now);
            Require(initial.Request == FarmRequest.Battle && initial.Battle.Kind == ActionKind.Target &&
                farm.MovementKey == '\0', "副本开局应先原地锁敌");
            Step(farm, Hud(false), realm, ref now, 1600);
            Require(farm.MovementKey == 'W', "确认身在副本时允许一次短距离接近");
            Step(farm, Hud(false), realm, ref now, 1000);
            Require(farm.MovementKey == '\0' && !farm.WantsSprint, "短距离接近结束应及时松开移动");
            Step(farm, Hud(false), realm, ref now, 3100);
            Require(farm.Finished && farm.MovementKey == '\0', "仍无目标时停止，不能重复往前走");
        }

        internal static void TargetReturnsDuringLoot()
        {
            long now = 1000;
            var farm = ReadyToPickUp(FarmOptions.Defaults(), ref now);
            var idle = Ui();
            Step(farm, Hud(false), idle, ref now, 400);
            Step(farm, Hud(false), idle, ref now, 400);
            Step(farm, Hud(false), idle, ref now, 200);
            Step(farm, Hud(false), idle, ref now, 1000);
            Require(farm.MovementKey == 'W', "应进入战后直走搜寻");
            Step(farm, Hud(true), idle, ref now);
            Require(farm.MovementKey == '\0' && farm.Kills == 0, "敌人仍在场时停止拾取走位并撤回击杀计数");
            for (int i = 0; i < 4; i++)
                Step(farm, Hud(true), idle, ref now, 400);
            Require(farm.Planner.Fighting && farm.Kills == 0, "应恢复同一场战斗");
        }

        internal static void RechallengeFromSettlement()
        {
            long now = 1000;
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            var settlement = Ui();
            settlement.InRealm = settlement.Treasure = true;
            settlement.TreasureBox = new Rectangle(917, 685, 25, 22);
            var hud = Hud(false);
            FarmDecision right = Step(farm, hud, settlement, ref now);
            Require(right.Request == FarmRequest.KeyDown && right.Key == 'D' && !farm.WantsSprint,
                "手动启动已结算副本时，应向右走到已识别的宝箱");
            Require(farm.Kills == 0 && farm.Status.Contains("宝箱"), "开始时的旧结算不计入本次刷取");
            settlement.Treasure = false;
            Require(Step(farm, hud, settlement, ref now).Request == FarmRequest.None && farm.MovementKey == '\0',
                "宝箱标记短暂消失时应先停止移动");
            settlement.Treasure = true;
            settlement.TreasureBox = new Rectangle(787, 685, 25, 22);
            FarmDecision back = Step(farm, hud, settlement, ref now);
            Require(back.Request == FarmRequest.KeyDown && back.Key == 'S', "宝箱居中且靠近画面底部时应后退接近");
            settlement.FPrompt = true;
            settlement.Rechallenge = RechallengeChoice.Below;
            Require(Step(farm, hud, settlement, ref now, 500).Request == FarmRequest.None && farm.MovementKey == '\0',
                "新交互先等待0.2秒");
            FarmDecision scroll = Step(farm, hud, settlement, ref now, 200);
            Require(scroll.Request == FarmRequest.Scroll && farm.MovementKey == '\0',
                "出现交互后停止移动，重开选项在下方时先滚动");
            settlement.Rechallenge = RechallengeChoice.Selected;
            Require(Step(farm, hud, settlement, ref now, 199).Request == FarmRequest.None, "滚动后等满0.2秒再F");
            FarmDecision restart = Step(farm, hud, settlement, ref now, 1);
            Require(restart.Request == FarmRequest.Key && restart.Key == 'F' && restart.StartsRechallenge,
                "确认为重新挑战后才按 F");
            Require(Step(farm, null, Ui(), ref now, 2000).Request == FarmRequest.None, "无 HUD 时等待下一轮加载");
            FarmDecision acquire = Step(farm, hud, Ui(), ref now, 1000);
            Require(acquire.Request == FarmRequest.Battle && acquire.Battle.Kind == ActionKind.Target,
                "重开后返回场景时应尝试锁敌");
        }

        internal static void NamelessRechallengeApproach()
        {
            var options = FarmOptions.Defaults();
            options.Boss = FarmBoss.NamelessExplorer;
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            var dialog = Ui();
            dialog.Claim = true;
            dialog.RechallengeButton = new Rectangle(485, 548, 105, 32);
            var world = Ui();
            world.OpenWorld = true;
            var hud = Hud(false);
            long now = 1000;

            FarmDecision restart = Step(farm, null, dialog, ref now, dispatch: false);
            Require(restart.Request == FarmRequest.Click && restart.StartsRechallenge, "确认离开弹窗中的重开按钮");
            Require(Step(farm, hud, world, ref now, 1000).Request == FarmRequest.None && farm.MovementKey == '\0',
                "重开操作未发送时不能开始接近");
            Step(farm, null, dialog, ref now);
            var chest = Ui();
            chest.Treasure = true;
            Require(Step(farm, hud, chest, ref now, 1900).Request == FarmRequest.None, "旧宝箱仍在时等待重开完成");
            Require(Step(farm, null, Ui(), ref now).Request == FarmRequest.None && farm.MovementKey == '\0',
                "加载时不按住移动键");
            Step(farm, hud, world, ref now);
            FarmDecision forward = Step(farm, hud, world, ref now, navTurn: 100);
            Require(forward.Request == FarmRequest.KeyDown && forward.Key == 'W' && farm.WantsSprint,
                "重开完成后先向前冲刺接近无铭探索者");
            Step(farm, null, Ui(), ref now);
            Require(farm.MovementKey == '\0' && !farm.WantsSprint, "丢失场景确认时松开 W 和冲刺键");
            Step(farm, hud, world, ref now);
            Require(farm.MovementKey == 'W' && farm.WantsSprint, "恢复确认后继续接近");
            Step(farm, Hud(true), world, ref now);
            Require(farm.Phase == "Fight" && farm.MovementKey == '\0' && !farm.WantsSprint,
                "发现敌人后结束接近并松开 W 和冲刺键");

            var fallback = new Farm4C(CombatSettings.Defaults(), options);
            Step(fallback, null, dialog, ref now);
            Step(fallback, hud, world, ref now, 1900);
            Step(fallback, hud, world, ref now);
            FarmDecision guided = Step(fallback, hud, world, ref now, 7700, navTurn: 100);
            Require(guided.Request == FarmRequest.Key && guided.Key == 'D' && guided.HoldMs == 50,
                "前进满八秒后使用方向短按转身而非持续向右");
            FarmDecision centered = Step(fallback, hud, world, ref now, 500, navTurn: 100);
            Require(centered.Request == FarmRequest.Middle && centered.HoldMs == 200,
                "转身后复位视角");
            FarmDecision moved = Step(fallback, hud, world, ref now, 1000, navTurn: 100);
            Require(moved.Request == FarmRequest.KeyDown && moved.Key == 'W' && fallback.WantsSprint,
                "复位后按 W 持续前进");
            Step(fallback, hud, world, ref now);
            Require(fallback.MovementKey == 'W' && fallback.WantsSprint, "确认重开后无标记方向按零度前进");
            Step(fallback, hud, world, ref now, 8200);
            Require(!fallback.Finished && fallback.Phase == "MapTeleport" && fallback.MovementKey == '\0',
                "重开后搜敌超时进入受控地图传送回退");
        }

        internal static void RechallengeChoiceUsesSelectedRow()
        {
            var key = new Rectangle(958, 432, 25, 24);
            var reward = new OcrLine { Box = new Rectangle(1028, 435, 98, 22), Text = "领取奖励", Score = .96f };
            var below = new OcrLine { Box = new Rectangle(1028, 482, 98, 22), Text = "再次 挑戰", Score = .96f };
            var above = new OcrLine { Box = new Rectangle(1028, 377, 98, 22), Text = "重新挑战", Score = .96f };
            var selected = new OcrLine { Box = new Rectangle(1028, 434, 133, 22), Text = "Restart Challenge", Score = .96f };
            var objective = new OcrLine { Box = new Rectangle(42, 223, 81, 24), Text = "领取奖励", Score = 1f };
            Require(FarmVision.ChooseRechallenge(new List<OcrLine> { objective }, key) == RechallengeChoice.None,
                "左侧任务文字不能当成当前交互选项");
            Require(FarmVision.ChooseRechallenge(new List<OcrLine> { reward }, key) == RechallengeChoice.RewardSelected,
                "领领奖励要与重开区分");
            Require(FarmVision.ChooseRechallenge(new List<OcrLine> { reward, below }, key) == RechallengeChoice.Below,
                "识别在下方的重开选项");
            Require(FarmVision.ChooseRechallenge(new List<OcrLine> { reward, above }, key) == RechallengeChoice.Above,
                "识别在上方的重开选项");
            Require(FarmVision.ChooseRechallenge(new List<OcrLine> { selected }, key) == RechallengeChoice.Selected,
                "识别选中行的英文按钮");
        }

        internal static void RechallengeFromRewardOnly()
        {
            long now = 1000;
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            var settlement = Ui();
            settlement.InRealm = settlement.Treasure = settlement.FPrompt = true;
            settlement.TreasureBox = new Rectangle(1108, 559, 25, 22);
            settlement.Rechallenge = RechallengeChoice.RewardSelected;
            Require(Step(farm, Hud(false), settlement, ref now).Request == FarmRequest.None, "先等交互稳定");
            FarmDecision scroll = Step(farm, Hud(false), settlement, ref now, 200);
            Require(scroll.Request == FarmRequest.Scroll && scroll.ScrollTicks == 1, "按参考方向滚动探查重开选项，不能误领体力奖励");
            FarmDecision escape = Step(farm, Hud(false), settlement, ref now, 4800);
            Require(escape.Request == FarmRequest.Key && escape.Key == 0x1b && !escape.StartsRechallenge,
                "五秒内无法确认重开选项时，才按 Esc 打开副本重开菜单");

            var modal = Ui();
            modal.Claim = true;
            Require(Step(farm, null, modal, ref now, 1200).Request == FarmRequest.None,
                "通用弹窗边框不能代替确认离开和重新挑战文字");
            modal.ClaimReward = true;
            modal.RechallengeButton = new Rectangle(485, 548, 105, 32);
            FarmDecision dismiss = Step(farm, null, modal, ref now);
            Require(dismiss.Request == FarmRequest.Key && dismiss.Key == 0x1b,
                "已识别为体力领奖弹窗时只能关闭");
            modal.ClaimReward = false;
            FarmDecision restart = Step(farm, null, modal, ref now, 1000);
            Require(restart.Request == FarmRequest.Click && restart.StartsRechallenge && restart.Point == new Point(537, 564),
                "确认是副本离开弹窗后应点击左侧重新挑战");
            Require(Step(farm, null, Ui(), ref now, 2500).Request == FarmRequest.None, "重开后等待加载");
            Step(farm, Hud(true), Ui(), ref now);
            for (int i = 0; i < 6; i++)
                Step(farm, Hud(true), Ui(), ref now);
            Require(farm.Planner.Fighting && farm.Kills == 0, "读到新敌人后应开始战斗，旧结算不计击杀");
        }

        internal static void RechallengeKeepsConfirmedSettlement()
        {
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            long now = 1000;
            var chest = Ui();
            chest.InRealm = chest.Treasure = true;
            chest.TreasureBox = new Rectangle(1108, 559, 25, 22);
            FarmDecision approach = Step(farm, Hud(false), chest, ref now);
            Require(approach.Request == FarmRequest.KeyDown, "先识别副本宝箱并接近");

            var reward = Ui();
            reward.FPrompt = true;
            reward.Rechallenge = RechallengeChoice.RewardSelected;
            Require(Step(farm, Hud(false), reward, ref now).Request == FarmRequest.None && farm.MovementKey == '\0', "先停下等待交互稳定");
            Require(Step(farm, Hud(false), reward, ref now, 200).Request == FarmRequest.Scroll, "尝试滚动找到重开项");
            FarmDecision escape = Step(farm, Hud(false), reward, ref now, 4800);
            Require(escape.Request == FarmRequest.Key && escape.Key == 0x1b && farm.MovementKey == '\0',
                "进入交互后宝箱和副本图标可以同时消失，五秒未确认后仍可按 Esc");
            FarmDecision retry = Step(farm, Hud(false), Ui(), ref now, 1200);
            Require(retry.Request == FarmRequest.Key && retry.Key == 0x1b && retry.RetryInput,
                "副本场景保持可见但未弹窗时，允许用另一种输入补试一次");
            Require(Step(farm, Hud(false), Ui(), ref now, 1200).Request == FarmRequest.None,
                "限制 Esc 补试次数");
        }

        internal static void RechallengeWaitsAtIntermittentPrompt()
        {
            long now = 1000;
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            var chest = Ui();
            chest.InRealm = chest.Treasure = true;
            chest.TreasureBox = new Rectangle(1108, 559, 25, 22);
            Step(farm, Hud(false), chest, ref now);
            var prompt = Ui();
            prompt.FPrompt = true;
            Step(farm, Hud(false), prompt, ref now);
            for (int i = 0; i < 3; i++)
            {
                FarmDecision wait = Step(farm, Hud(false), i == 1 ? chest : Ui(), ref now, 1000);
                Require(wait.Request == FarmRequest.None && farm.MovementKey == '\0',
                    "F 闪过后应原地等待，宝箱重现也不能再次走动");
            }
            FarmDecision escape = Step(farm, Hud(false), Ui(), ref now, 2000);
            Require(escape.Request == FarmRequest.Key && escape.Key == 0x1b && !escape.StartsRechallenge,
                "副本和交互已在之前确认，F 和文字暂时读不到时也可走 Esc 重开");

            var world = Ui();
            world.OpenWorld = world.FPrompt = true;
            world.Rechallenge = RechallengeChoice.RewardSelected;
            var outside = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            Step(outside, Hud(false), chest, ref now);
            Require(Step(outside, Hud(false), world, ref now).Request == FarmRequest.None, "等待交互稳定");
            FarmDecision movedOut = Step(outside, Hud(false), world, ref now, 200);
            Require(movedOut.Request == FarmRequest.Scroll, "明确返回大世界时，取消副本 Esc 路线");
            FarmDecision stopped = Step(outside, Hud(false), world, ref now, 5100);
            Require(outside.Finished && stopped.Request == FarmRequest.None, "大世界结算交互应有明确停止上限");
        }

        internal static void RechallengeWaitsForAbsorbReceipt()
        {
            long now = 1000;
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            var chest = Ui();
            chest.InRealm = chest.Treasure = true;
            chest.TreasureBox = new Rectangle(1108, 559, 25, 22);
            Step(farm, Hud(false), chest, ref now);
            var prompt = Ui();
            prompt.FPrompt = true;
            Step(farm, Hud(false), prompt, ref now);
            FarmDecision pickup = Step(farm, Hud(false), prompt, ref now, 3700, absorb: true);
            Require(pickup.Request == FarmRequest.Key && pickup.PickupKey,
                "即使到了重开超时，检测到吸收时也应先拾取");
            var reward = Ui();
            reward.FPrompt = true;
            reward.Rechallenge = RechallengeChoice.RewardSelected;
            FarmDecision wait = Step(farm, Hud(false), reward, ref now, 850);
            Require(wait.Request == FarmRequest.None && !farm.Finished && farm.Echoes == 0,
                "有吸收按键等待回执时不能按 Esc");
            FarmDecision open = Step(farm, Hud(false), reward, ref now, 1800);
            Require(open.Request == FarmRequest.Key && open.Key == 0x1b && !farm.Finished && farm.Echoes == 0,
                "吸收提示消失且等待回执超时后，应从结算交互继续重开");
        }

        internal static void ReplayRechallengeDialog(string root)
        {
            string screenshots = Path.Combine(root, "ok_templates");
            using (var reader = new FrameReader())
            using (var ocr = new OcrReader(OcrReader.Directory))
            {
                using (var dialog = new Bitmap(Path.Combine(screenshots, "4.png")))
                {
                    Rectangle button = FarmVision.ReadRechallengeDialog(reader.Read(dialog), ocr);
                    Require(button.Contains(new Point(536, 565)) && button.Right < 720,
                        "应从原版确认离开截图识别左侧重新挑战");
                }
                foreach (string file in new[] { "43.png", "11.png" })
                using (var other = new Bitmap(Path.Combine(screenshots, file)))
                    Require(FarmVision.ReadRechallengeDialog(reader.Read(other), ocr).IsEmpty,
                        "体力奖励或跳过剧情弹窗不能判为重新挑战 " + file);
            }
        }

        internal static void ReplaySettlementMarker(string root)
        {
            using (var reader = new FrameReader())
            using (var reference = new Bitmap(Path.Combine(root, "ok_templates", "treasure_icon.png")))
            using (var unrelated = new Bitmap(Path.Combine(root, "ok_templates", "42.png")))
            {
                var vision = new FarmVision();
                FarmUi marker = vision.Read(reader.Read(reference), true);
                Require(marker.Treasure && marker.TreasureBox.Contains(new Point(1231, 415)),
                    "应在原版参考帧中定位宝箱图标");
                Require(marker.OpenWorld && !marker.InRealm, "原版图标素材是大世界宝箱");
                var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
                long now = 1000;
                Step(farm, Hud(false), marker, ref now);
                Step(farm, Hud(false), marker, ref now, 1700);
                Require(farm.Phase == "Approach", "大世界普通宝箱不能触发副本重开");
                Require(!vision.Read(reader.Read(unrelated), true).Treasure, "队伍界面不能当成结算宝箱");
            }
        }

        internal static void WaitBeforeCombat()
        {
            var options = FarmOptions.Defaults();
            options.WaitSeconds = 5;
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            long now = 1000;
            Step(farm, Hud(false), Ui(), ref now);
            Step(farm, Hud(true), Ui(), ref now);
            for (int i = 0; i < 9; i++)
                Require(Step(farm, Hud(true), Ui(), ref now, 500).Request == FarmRequest.None,
                    "开战等待时间内不应派发战斗动作");
            Step(farm, Hud(true), Ui(), ref now, 500);
            Step(farm, Hud(true), Ui(), ref now, 500);
            Require(farm.Planner.Fighting, "等待结束后才交给战斗规划器");

            options.WaitSeconds = 61;
            options.RepeatCount = 100001;
            options.Validate();
            options.WeeklyChallengeIndex = 999;
            options.Validate();
            options.Teleport = true;
            options.TeleportMode = FarmTeleportMode.WeeklyChallenge;
            bool rejected = false;
            try { options.Validate(); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "只有实际选中的类别越界时拒绝未知目标操作");
            options.Teleport = false;
            options.WaitSeconds = int.MaxValue;
            options.Validate();
            var longWait = new Farm4C(CombatSettings.Defaults(), options);
            Step(longWait, Hud(false), Ui(), ref now);
            Step(longWait, Hud(true), Ui(), ref now);
            Require(Step(longWait, Hud(true), Ui(), ref now, 100000).Request == FarmRequest.None,
                "大配置值保持等待，不因毫秒换算溢出变成即时开战");

            options = FarmOptions.Defaults();
            options.RepeatCount = 0;
            options.Teleport = true;
            options.Validate();
            var noRounds = new Farm4C(CombatSettings.Defaults(), options);
            Require(Step(noRounds, Hud(false), Ui(), ref now).Request == FarmRequest.None &&
                noRounds.Finished && noRounds.Attempts == 0, "零轮配置不发出进入挑战或传送动作");

            options = FarmOptions.Defaults();
            options.RepeatCount = 1;
            var neverStarts = new Farm4C(CombatSettings.Defaults(), options);
            var challenge = Hud(false);
            challenge.ChallengePrompt = true;
            Step(neverStarts, Hud(false), Ui(), ref now);
            Step(neverStarts, challenge, Ui(), ref now);
            Require(neverStarts.Attempts == 1 && neverStarts.Kills == 0, "开始挑战即计一次尝试");
            Step(neverStarts, Hud(false), Ui(), ref now, 5001);
            Require(neverStarts.Phase == "Loot" && neverStarts.Attempts == 1 && neverStarts.Kills == 0,
                "未检测到战斗进入掉落检查但不产生虚假击杀");

            var profile = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            Step(profile, Hud(false), new FarmUi { InRealm = true }, ref now);
            Require(profile.Realm, "启动时识别副本，应采用副本配置");
            Step(profile, Hud(false), new FarmUi { OpenWorld = true }, ref now);
            Require(profile.Realm, "普通 HUD 更新不能取消已锁存的副本战斗及拾取配置");

            var lateProfile = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            Step(lateProfile, Hud(true), new FarmUi { OpenWorld = true }, ref now);
            Step(lateProfile, Hud(true), new FarmUi { OpenWorld = true }, ref now);
            Step(lateProfile, Hud(true), new FarmUi { InRealm = true }, ref now, 20001);
            Require(!lateProfile.Realm, "进入战斗二十秒后不再用普通采样锁存副本配置");

            var hecate = FarmOptions.Defaults();
            hecate.Boss = FarmBoss.NightmareHecate;
            var hecateProfile = new Farm4C(CombatSettings.Defaults(), hecate);
            Step(hecateProfile, Hud(false), new FarmUi { OpenWorld = true }, ref now);
            Step(hecateProfile, Hud(false), new FarmUi { InRealm = true }, ref now);
            Require(!hecateProfile.Realm, "梦魇赫卡忒普通采样按参考 treat-as-world 不触发副本配置");
        }

        internal static void SpecialNightmareAndEntry()
        {
            var options = FarmOptions.Defaults();
            options.Teleport = true;
            options.TeleportMode = FarmTeleportMode.SpecialNightmare;
            Require(Farm4C.BookSerial(options.TeleportMode, 1) == 6 &&
                Farm4C.BookSerial(options.TeleportMode, 3) == 8 &&
                Farm4C.BookSerial(options.TeleportMode, 10) == 15, "特异梦魇序号应跳过五个巢穴");
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            long now = 1000;
            var book = Ui();
            book.Book = book.BookSelected = true;
            Step(farm, Hud(false), Ui(), ref now);
            Step(farm, null, book, ref now);
            FarmDecision expand = Step(farm, null, book, ref now);
            FarmDecision category = Step(farm, null, book, ref now, 2100);
            Require(expand.Point == new Point(569, 793) && category.Point == new Point(384, 774),
                "应先展开梦魇分类，再选特异梦魇");
            book.BossRows.Add(new Rectangle(1480, 660, 40, 46));
            FarmDecision scroll = Step(farm, null, book, ref now, 2100);
            Require(scroll.Request == FarmRequest.Click && scroll.Point.X == 1556,
                "第六个条目应先滚动图鉴");
            FarmDecision row = Step(farm, null, book, ref now, 1300);
            Require(row.Point == new Point(1500, 683), "应选择滚动后识别到的目标行");
            var dialog = Ui();
            dialog.SkipDialog = dialog.Claim = true;
            dialog.SkipCheckBox = new Rectangle(690, 470, 40, 50);
            dialog.ConfirmBox = new Rectangle(970, 540, 90, 60);
            Require(Step(farm, null, dialog, ref now, 1100).Point == new Point(710, 495),
                "弹窗需先勾选本次登录不再提示");
            Require(Step(farm, null, dialog, ref now, 550).Point == new Point(1015, 570),
                "存在确认弹窗时应点确认");
            var team = Ui();
            team.TeamStart = team.Challenge = true;
            team.TeamStartBox = new Rectangle(1450, 790, 80, 60);
            Require(Step(farm, null, team, ref now, 550).Point == new Point(1490, 820),
                "直达队伍界面时应点击开启挑战");
            FarmDecision loading = Step(farm, null, Ui(), ref now, 7000);
            Require(loading.Request == FarmRequest.None && !farm.Planner.Fighting,
                "加载画面不能触发开战或前进");
        }

        internal static void ReplayMenus(string root)
        {
            var vision = new FarmVision();
            string folder = Path.Combine(root, "ok_templates");
            using (var reader = new FrameReader())
            using (var selected = new Bitmap(Path.Combine(folder, "5.png")))
            using (var unselected = new Bitmap(Path.Combine(folder, "6.png")))
            using (var team = new Bitmap(Path.Combine(folder, "42.png")))
            using (var travel = new Bitmap(Path.Combine(folder, "travel.png")))
            using (var dialog = new Bitmap(Path.Combine(folder, "11.png")))
            {
                FarmUi guide = vision.Read(reader.Read(selected), true);
                Require(guide.BookSelected && guide.BossRows.Count > 0 && !guide.TeamStart,
                    "原版图鉴应识别选中页签和可点击的条目");
                Require(!vision.Read(reader.Read(unselected), true).BookSelected, "未选中的图鉴要先点页签");
                FarmUi entry = vision.Read(reader.Read(team), true);
                Require(entry.TeamStart && entry.TeamStartBox.Contains(new Point(1493, 827)) && !entry.Teleport,
                    "原版队伍页面应返回开启挑战按钮");
                var manual = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
                long now = 1000;
                FarmDecision start = Step(manual, null, entry, ref now);
                Require(start.Request == FarmRequest.Click && entry.TeamStartBox.Contains(start.Point),
                    "用原版队伍页面离线启动刷取时应立即点击开启挑战");
                FarmUi move = vision.Read(reader.Read(travel), true);
                Require(move.Teleport && !move.TeleportBox.IsEmpty && !move.TeamStart,
                    "原版地图传送页面应返回传送按钮");
                FarmUi popup = vision.Read(reader.Read(dialog), true);
                Require(popup.SkipDialog && !popup.SkipCheckBox.IsEmpty &&
                    popup.ConfirmBox.X + popup.ConfirmBox.Width / 2 > 900,
                    "同布局的右侧确认按钮不能读成取消，读取到 " + popup.ConfirmBox);
            }
        }

        internal static void LevelOcr()
        {
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            long now = 1000;
            Step(farm, null, Ui(), ref now, 100);
            Step(farm, Hud(false), Ui(), ref now, 100);
            var prompt = Ui();
            prompt.FPrompt = true;
            Step(farm, Hud(false), prompt, ref now, 200);
            var challenge = Ui();
            challenge.Challenge = true;
            var levels = new System.Collections.Generic.List<OcrLine>
            {
                new OcrLine { Box = new Rectangle(200, 300, 60, 24), Text = "Lv.60" },
                new OcrLine { Box = new Rectangle(200, 372, 60, 24), Text = "Lv.80" }
            };
            FarmDecision click = Step(farm, Hud(false), challenge, ref now, 750, null, levels);
            Require(click.Request == FarmRequest.Click, "OCR 命中等级后应点击");
            Require(Math.Abs(click.Point.Y - 384) <= 2 && Math.Abs(click.Point.X - 230) <= 2,
                "应点击 OCR 给出的 80 级行");
        }

        internal static void BossScripts()
        {
            var fenrico = FarmOptions.Defaults();
            fenrico.Boss = FarmBoss.Fenrico;
            var farm = new Farm4C(CombatSettings.Defaults(), fenrico);
            long now = 1000;
            Step(farm, Hud(false), Ui(), ref now, 100);
            var prompt = Ui();
            prompt.FPrompt = true;
            FarmDecision press = Step(farm, Hud(false), prompt, ref now, 200, absorb: true);
            Require(press.Request == FarmRequest.Key && press.Key == 'F', "芬里科应先按 F 清理拾取");

            var fallacy = FarmOptions.Defaults();
            fallacy.Boss = FarmBoss.FallacyOfNoReturn;
            var second = new Farm4C(CombatSettings.Defaults(), fallacy);
            long clock = 1000;
            Step(second, Hud(false), Ui(), ref clock, 100);
            Step(second, Hud(false), Ui(), ref clock, 100);
            FarmDecision map = Step(second, Hud(false), Ui(), ref clock, 300);
            Require(map.Request == FarmRequest.Key && map.Key == 'M', "无归的谬误应先开地图传送");

            var fallacyPrompt = new Farm4C(CombatSettings.Defaults(), fallacy);
            Step(fallacyPrompt, Hud(false), Ui(), ref clock);
            Require(Step(fallacyPrompt, Hud(false), prompt, ref clock).Key == 'F', "无归的谬误已有交互时不需要回传");
            Step(fallacyPrompt, Hud(false), Ui(), ref clock, 30000);
            Require(!fallacyPrompt.Finished && fallacyPrompt.Phase == "Enter", "确认已交互的无归场景允许300秒等待战斗，不在15秒过早失败");
            Step(fallacyPrompt, Hud(true), Ui(), ref clock, 600);
            Require(fallacyPrompt.Phase == "Fight", "专属长等待期间出现实际敌人应立即进入战斗");

            var nextRound = ReadyToPickUp(fenrico, ref now);
            ConfirmDrop(nextRound, ref now);
            Step(nextRound, Hud(false), Ui(), ref now);
            Step(nextRound, Hud(false), Ui(), ref now, 1000);
            Require(nextRound.Phase == "BossScript" && nextRound.Attempts == 1,
                "芬里科首轮结束后重新进入下一轮特化脚本，而非只用普通接近");
        }

        internal static void ReviveFlow()
        {
            var options = FarmOptions.Defaults();
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            long now = 1000;
            Step(farm, null, Ui(), ref now, 100);
            Step(farm, Hud(false), Ui(), ref now, 100);
            var dead = Ui();
            dead.Death = true;
            dead.DeathBox = new Rectangle(1010, 600, 50, 60);
            Step(farm, Hud(true), dead, ref now, 100);
            FarmDecision click = Step(farm, Hud(true), dead, ref now, 100);
            Require(click.Request == FarmRequest.Key && click.Key == 27, "大世界倒下后应退出弹窗再开地图");
            Require(farm.Status.Contains("返回大世界"), "状态应显示正在返回大世界");
        }

        internal static void NamedBossSearch()
        {
            Require(GameWindow.DetectLanguage("Wuthering Waves", false) == GameLanguage.English &&
                GameWindow.DetectLanguage("鳴潮", false) == GameLanguage.TraditionalChinese &&
                GameWindow.DetectLanguage("鸣潮", false) == GameLanguage.SimplifiedChinese &&
                GameWindow.DetectLanguage("Wuthering Waves", true) == GameLanguage.SimplifiedChinese &&
                GameWindow.DetectLanguage("unrelated", false) == GameLanguage.Unknown, "按游戏窗口判定首领搜索语言");
            Rectangle normal = Farm4C.BossNameBounds(new Size(1600, 900));
            Rectangle wide = Farm4C.BossNameBounds(new Size(2134, 900));
            Require(normal == new Rectangle(529, 4, 527, 54) && wide == new Rectangle(796, 4, 527, 54),
                "超宽画面首领名称区域应保持宽度并居中平移");
            foreach (bool instantTravel in new[] { false, true })
            {
                var options = FarmOptions.Defaults();
                options.Boss = FarmBoss.FallacyOfNoReturn;
                var farm = new Farm4C(CombatSettings.Defaults(), options);
                long now = 100;
                var world = new FarmUi { OpenWorld = true };
                Step(farm, Hud(false), world, ref now, bossName: "异构武装");
                Step(farm, Hud(false), world, ref now);
                Require(Step(farm, Hud(false), world, ref now).VirtualKey == 0x71, "按已识别名称打开图鉴");
                var guide = new FarmUi { Book = true, BookMonsters = true,
                    BookMonstersBox = new Rectangle(50, 596, 22, 24) };
                Require(Step(farm, null, guide, ref now, 4500).Point == new Point(61, 608), "打开全部怪物");
                guide.BookMonstersSelected = true;
                Require(Step(farm, null, guide, ref now, 1000).Point == new Point(208, 126), "点击搜索");
                Require(Step(farm, null, guide, ref now, 500, dispatch: false).Text == "异构武装", "首领搜索词");
                Require(Step(farm, null, guide, ref now).Text == "异构武装", "键入未发送应重试");
                Require(Step(farm, null, guide, ref now).Point == new Point(624, 117), "提交搜索");
                Require(Step(farm, null, guide, ref now, 500).Point == new Point(208, 216), "选择第一条结果");
                Require(Step(farm, null, guide, ref now, 500).Point == new Point(1424, 828), "探测或传送到首领");
                if (!instantTravel)
                {
                    Require(Step(farm, null, Ui(), ref now, 1000).Request == FarmRequest.None,
                        "加载帧不重复点击探测");
                    var travel = new FarmUi { Teleport = true, TeleportBox = new Rectangle(1370, 810, 90, 30) };
                    Require(Step(farm, null, travel, ref now).Point == new Point(1424, 828), "地图传送按钮");
                }
                Require(Step(farm, Hud(false), world, ref now, 1000).Request == FarmRequest.None &&
                    farm.Phase == "BossScript", "已到达世界时继续无归的谬误专用流程");
                FarmDecision right = Step(farm, Hud(false), world, ref now, 100);
                Require(right.Request == FarmRequest.Key && right.Key == 'D' && right.HoldMs == 250,
                    "回传后右移0.25秒");
                Require(Step(farm, Hud(false), world, ref now, 750).Key == 'W', "右移后等待半秒再前进");
                Step(farm, Hud(false), world, ref now, 500);
                Require(farm.Phase == "Approach", "专用入场位移完成后接近");
            }
        }

        internal static void WeeklyLevelAfterBookRow()
        {
            var options = FarmOptions.Defaults();
            options.Teleport = true;
            options.TeleportMode = FarmTeleportMode.WeeklyChallenge;
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            long now = 1000;
            var book = new FarmUi { Book = true, BookSelected = true };
            book.BossRows.Add(new Rectangle(1490, 320, 30, 40));
            for (int i = 0; i < 12 && !farm.AwaitingBossDestination; i++)
                Step(farm, null, book, ref now, 700);
            Require(farm.AwaitingBossDestination, "进入周本图鉴目标行");
            var menu = new FarmUi { Challenge = true };
            Step(farm, null, menu, ref now, 1000);
            Require(farm.WantsLevelScan, "只有关闭按钮时也应进入等级选择流程");
            menu.TeamStart = true;
            menu.TeamStartBox = new Rectangle(1450, 810, 80, 50);
            Require(Step(farm, null, menu, ref now).Request == FarmRequest.None, "读到目标等级前不能直接开战");
            var levels = new List<OcrLine> {
                new OcrLine { Text = "70", Box = new Rectangle(100,180,50,30) },
                new OcrLine { Text = "等级 80", Box = new Rectangle(100,240,50,30) } };
            Require(Step(farm, null, menu, ref now, levels: levels, dispatch: false).Point == new Point(125, 255),
                "应选择配置的 80 级");
            Require(Step(farm, null, menu, ref now, 500, levels: levels).Point == new Point(125, 255),
                "等级操作未送达时应重试同一级别");
            Require(Step(farm, null, menu, ref now, 500).Point == new Point(1408, 819),
                "选级后进入单人挑战");
            var modal = new FarmUi { SkipDialog = true, SkipCheckBox = new Rectangle(715,480,16,16),
                ConfirmBox = new Rectangle(1095,555,80,40) };
            Require(Step(farm, null, modal, ref now, 2000).Point == new Point(723, 488), "周本先勾选低体力提示");
            Require(Step(farm, null, modal, ref now, 1000).Point == new Point(1135, 575), "确认继续进周本");
            Require(Step(farm, null, menu, ref now, 1500).Point == new Point(1490, 835),
                "确认低体力弹窗后才从队伍界面开战");
        }

        internal static void WalkInBossLevel()
        {
            var options = FarmOptions.Defaults();
            options.Teleport = true;
            options.BossChallengeIndex = 3;
            Require(Farm4C.BookSerial(options.TeleportMode, options.IndexFor(options.TeleportMode)) == 3,
                "首领挑战的序号不应跳过其他分类");
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            long now = 1000;
            var world = new FarmUi { OpenWorld = true };
            var book = new FarmUi { Book = true, BookSelected = true };
            book.BossRows.AddRange(new[] { new Rectangle(1490, 320, 30, 40),
                new Rectangle(1490, 455, 30, 40), new Rectangle(1490, 590, 30, 40) });
            for (int i = 0; i < 12 && !farm.AwaitingBossDestination; i++) Step(farm, null, book, ref now, 700);
            var travel = new FarmUi { Teleport = true, TeleportBox = new Rectangle(1320, 800, 75, 40) };
            Step(farm, null, travel, ref now, 1200);
            Step(farm, Hud(false), world, ref now, 1200);
            Step(farm, Hud(false), world, ref now, 1000);
            world.FPrompt = true;
            Require(Step(farm, Hud(false), world, ref now, 1000).Key == 'F', "从大世界交互进入等级页");
            var menu = new FarmUi { Challenge = true };
            var levels = new List<OcrLine> { new OcrLine { Text = "80", Box = new Rectangle(100,240,50,30) } };
            Require(Step(farm, null, menu, ref now, 2900, levels: levels).Request == FarmRequest.None, "进本加载期间不点等级");
            Require(Step(farm, null, menu, ref now, 100, levels: levels).Point == new Point(125,255), "步行进本选择等级");
            Require(Step(farm, null, menu, ref now, 1000).Point == new Point(1408,819), "单人挑战");
            var modal = new FarmUi { SkipDialog = true, SkipCheckBox = new Rectangle(715,480,16,16),
                ConfirmBox = new Rectangle(1095,555,80,40) };
            Require(Step(farm, null, modal, ref now, 2000).Point == new Point(723,488), "先勾选低体力提示");
            Require(Step(farm, null, modal, ref now, 1000).Point == new Point(1135,575), "确认继续进本");
            Require(Step(farm, null, menu, ref now, 1500, dispatch: false).Point == new Point(1452,827),
                "进队后使用步行进入对应的开战位置");
            Require(Step(farm, null, menu, ref now, 500).Point == new Point(1452,827), "开战操作未送达应重试");
            Require(Step(farm, null, menu, ref now, 4800).Request == FarmRequest.None,
                "等待队伍和场景加载时不能重复开战");
            Step(farm, Hud(true), new FarmUi { InRealm = true }, ref now, 300);
            Require(farm.Phase == "Fight", "确认进本后才开始战斗");
        }

        internal static void RealmRecoveryRoutes()
        {
            var options = FarmOptions.Defaults();
            var manual = new Farm4C(CombatSettings.Defaults(), options);
            var dead = new FarmUi { InRealm = true, Death = true };
            long now = 1000;
            Step(manual, null, dead, ref now);
            Require(manual.Finished && manual.Failure != null && manual.Kills == 0,
                "手动进本倒下不应自动退本或更换首领");

            options.Teleport = true;
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            Step(farm, null, dead, ref now);
            var recover = Step(farm, null, dead, ref now);
            Require(recover.Request == FarmRequest.RecoverRealm && !farm.EnteredFromWaypoint,
                "副本首次倒下应走固定信标路线");
            farm.RealmRecoveryFinished(true, now);
            Require(farm.EnteredFromWaypoint && farm.Phase == "Teleport.OpenBook" && farm.Kills == 0,
                "固定信标回血成功后从配置的图鉴入口重新进本");

            var failed = new Farm4C(CombatSettings.Defaults(), options);
            Step(failed, null, dead, ref now);
            failed.RealmRecoveryFinished(false, now);
            Require(failed.Finished && failed.Failure != null && !failed.EnteredFromWaypoint,
                "未核实回血不能从配置入口重新进本");

            var manualWorld = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            var world = new FarmUi { OpenWorld = true };
            Step(manualWorld, null, new FarmUi { Death = true }, ref now);
            Step(manualWorld, Hud(false), world, ref now, 1000);
            Require(Step(manualWorld, Hud(false), world, ref now).Key == 'M',
                "关闭自动进本仍须执行开放世界的就近信标回血");
        }

        internal static void RealmRecoveryLimit()
        {
            foreach (bool walkIn in new[] { false, true })
            {
                var options = FarmOptions.Defaults();
                options.Teleport = true;
                var farm = new Farm4C(CombatSettings.Defaults(), options);
                long now = 1000;
                var dead = new FarmUi { InRealm = true, Death = true };
                var book = new FarmUi { Book = true, BookSelected = true };
                book.BossRows.Add(new Rectangle(1480, 320, 40, 46));
                for (int count = 1; count <= 3; count++)
                {
                    Step(farm, null, dead, ref now);
                    Require(Step(farm, null, dead, ref now).Request == FarmRequest.RecoverRealm,
                        "每次死亡应先完成恢复");
                    farm.RealmRecoveryFinished(true, now);
                    Require(farm.Kills == 0 && farm.Echoes == 0, "每次恢复都要保留原击杀和声骸计数");
                    if (count == 3)
                    {
                        Require(farm.Finished && farm.Failure == null && farm.CompletionMessage.Contains("3"),
                            "第三次恢复结束后应正常收尾，不能再次进本");
                        break;
                    }
                    for (int i = 0; i < 12 && !farm.AwaitingBossDestination; i++)
                        Step(farm, null, book, ref now, 700);
                    Require(farm.AwaitingBossDestination, "恢复后重新选择配置的图鉴行");
                    if (walkIn)
                    {
                        var map = new FarmUi { Teleport = true, TeleportBox = new Rectangle(1330, 800, 70, 50) };
                        Require(Step(farm, null, map, ref now, 1200).Request == FarmRequest.Click &&
                            !farm.EnteredFromWaypoint, "世界传送后步行进本必须清除旧的回血起点");
                        break;
                    }
                    var team = new FarmUi { TeamStart = true, Challenge = true,
                        TeamStartBox = new Rectangle(1430, 800, 90, 50) };
                    Require(Step(farm, null, team, ref now, 1200).Request == FarmRequest.Click &&
                        farm.EnteredFromWaypoint, "直接从队伍进本时下一次仍可回到原信标");
                    Step(farm, Hud(true), new FarmUi { InRealm = true }, ref now, 2000);
                }
            }
        }

        internal static void DeathThenSuccessfulRounds()
        {
            var options = FarmOptions.Defaults();
            options.Teleport = true;
            options.RepeatCount = 2;
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            var book = new FarmUi { Book = true, BookSelected = true };
            book.BossRows.Add(new Rectangle(1480, 320, 40, 46));
            var team = new FarmUi { Challenge = true, TeamStart = true, TeamStartBox = new Rectangle(1450, 795, 90, 60) };
            var realm = new FarmUi { InRealm = true };
            long now = 1000;
            int entries = 0;
            Action enter = () =>
            {
                for (int i = 0; i < 12 && !farm.AwaitingBossDestination; i++)
                    Step(farm, null, book, ref now, 700);
                Require(farm.AwaitingBossDestination && Step(farm, null, team, ref now, 1200).Request == FarmRequest.Click,
                    "an entry must start from the configured boss");
                entries++;
                Step(farm, Hud(true), realm, ref now, 2000);
                Require(farm.Phase == "Fight", "the confirmed team load enters combat");
            };
            enter();
            for (int i = 0; i < 4; i++) Step(farm, Hud(true), realm, ref now, 400);
            var death = new FarmUi { InRealm = true, Death = true };
            Step(farm, null, death, ref now);
            Require(Step(farm, null, death, ref now).Request == FarmRequest.RecoverRealm, "the failed round takes the realm recovery route");
            farm.RealmRecoveryFinished(true, now);
            Require(farm.Kills == 0, "an interrupted fight cannot count as a completed round");
            enter();

            var drop = new List<EchoBox> { new EchoBox { Box = new Rectangle(780, 640, 40, 30), Score = .8f } };
            for (int cleared = 1; cleared <= 2; cleared++)
            {
                for (int i = 0; i < 7; i++) Step(farm, Hud(true), realm, ref now, 400);
                for (int i = 0; i < 12 && farm.Kills < cleared; i++)
                    Step(farm, Hud(false), realm, ref now, 400, drop);
                Require(farm.Kills == cleared, "the next confirmed fight increments one round");
                var prompt = new FarmUi { InRealm = true, FPrompt = true };
                Step(farm, Hud(false), prompt, ref now, absorb: true);
                Step(farm, Hud(false), realm, ref now, receipt: true);
                Step(farm, Hud(false), realm, ref now, receipt: true);
                if (cleared == 1)
                {
                    for (int i = 0; i < 4 && farm.Phase != "Fight"; i++) Step(farm, Hud(true), realm, ref now, 600);
                    Require(farm.Phase == "Fight", "another loaded wave can begin without a third guidebook entry");
                }
            }
            Require(entries == 2 && farm.Kills == 2 && farm.Echoes == 2, "each successful round has one receipt");
        }

        internal static void HealWaypointRecovery()
        {
            var best = new Rectangle(200, 300, 24, 24);
            var close = new Rectangle(790, 450, 24, 24);
            var order = FarmVision.SortWaypoints(new[] { best, close, best }, new Size(1600, 900), confidenceOrder: true);
            Require(order.Count == 2 && order[0] == best && order[1] == close,
                "置信度优先模式按既有模板评分顺序去重，不再按距离重排");
            var options = FarmOptions.Defaults();
            options.Teleport = true;
            options.TeleportMode = FarmTeleportMode.BossChallenge;
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            long now = 1000;
            var dead = Ui();
            dead.Death = true;
            dead.DeathBox = new Rectangle(1010, 600, 50, 60);
            Step(farm, Hud(true), dead, ref now, 100);
            Step(farm, Hud(true), dead, ref now, 100);

            var overworld = Ui();
            overworld.OpenWorld = true;
            Step(farm, Hud(false), overworld, ref now, 2500);
            Require(Step(farm, Hud(false), overworld, ref now, 100).Key == 'M', "返回大世界后先开地图");
            var map = Ui();
            map.HealWaypoints = new List<Rectangle>
            {
                new Rectangle(780, 440, 24, 24), new Rectangle(705, 420, 24, 24)
            };
            overworld.HealWaypoints = map.HealWaypoints;
            Require(Step(farm, Hud(false), overworld, ref now, 2200).Key == 'M',
                "开图未生效时在有 HUD 的画面重试 M，不点地图候选");
            Require(Step(farm, null, map, ref now, 2200).Point == new Point(792, 452), "按置信排序尝试最佳信标");

            var overlap = Ui();
            overlap.Teleport = true;
            overlap.TeleportBox = new Rectangle(1280, 780, 60, 50);
            Require(Step(farm, null, overlap, ref now, 1200).Request == FarmRequest.None,
                "确认回血路线时不能误点通用传送或移除标记");
            overlap.WaypointPopupBox = new Rectangle(1050, 450, 16, 24);
            Require(Step(farm, null, overlap, ref now, 2800).Point == new Point(1058, 462),
                "最佳信标无传送按钮时，选择已识别重叠选单中的同一类型信标，不切换其它地图候选");

            var waypoint = Ui();
            waypoint.WaypointTravelBox = new Rectangle(1310, 800, 40, 24);
            waypoint.Teleport = true;
            waypoint.TeleportBox = waypoint.WaypointTravelBox;
            Require(Step(farm, null, waypoint, ref now, 1000).Point == new Point(1370, 812),
                "识别灰色信标传送按钮后，按参考项目的图标右侧位置点击");
            Require(Step(farm, null, map, ref now, 1000).Request == FarmRequest.None,
                "传送后尚未到达世界，不能打开图鉴");
            Step(farm, Hud(false), overworld, ref now, 1000);
            Require(Step(farm, Hud(false), overworld, ref now, 1900).Request == FarmRequest.None,
                "信标回血后等待两秒再后退");
            FarmDecision retreat = Step(farm, Hud(false), overworld, ref now, 100);
            Require(retreat.Request == FarmRequest.KeyDown && retreat.Key == 'S' && farm.WantsSprint,
                "从回血信标后退奔跑一秒");
            Step(farm, Hud(false), overworld, ref now, 1000);
            Require(farm.MovementKey == 0 && Step(farm, Hud(false), overworld, ref now).Key == 'M',
                "首领名称未知时松开后退并开地图寻找首领标记");
            var bossMap = Ui();
            bossMap.HasMapMarker = true;
            bossMap.MapMarker = new Rectangle(790, 450, 30, 40);
            Require(Step(farm, null, bossMap, ref now, 2200).Point == new Point(805, 470),
                "返回的是首领标记");
            Require(Step(farm, null, waypoint, ref now, 1200).Request == FarmRequest.Click, "传送到首领");
            Step(farm, Hud(false), overworld, ref now, 1000);
            var forward = Step(farm, Hud(false), overworld, ref now, 500);
            Require(forward.Request == FarmRequest.KeyDown && forward.Key == 'W' && farm.WantsSprint,
                "回到首领位置应向前奔跑");
            Require(Step(farm, Hud(false), overworld, ref now, 500).Request == FarmRequest.Middle, "移动中搜索战斗");
            Step(farm, Hud(true), overworld, ref now, 500);
            Require(farm.MovementKey == 0 && farm.Kills == 0 && farm.Phase == "Fight" && farm.Attempts == 1,
                "回血后已返回首领且确认战斗，直接继续，不重复打开图鉴");
        }
    }
}
