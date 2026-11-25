using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace TideCombat
{
    internal static class NightmareChecks
    {
        private static void Require(bool condition, string explanation)
        {
            if (!condition)
                throw new InvalidOperationException(explanation);
        }

        internal static void Counts()
        {
            int denominator;
            Require(NestMath.TryIncomplete("0/24", out denominator) && denominator == 24, "0/24 应判定未完成");
            Require(NestMath.TryIncomplete("0/41", out denominator) && denominator == 41, "0/41 应判定未完成");
            Require(!NestMath.TryIncomplete("5 / 24", out denominator),
                "单次捕获的零进度初筛只选择尚未开始的聚落");
            Require(!NestMath.TryIncomplete("24/24", out denominator), "满进度才算清理完成");
            Require(!NestMath.TryIncomplete("148/48", out denominator), "不可把较大数字的一部分当成聚落进度");
            Require(!NestMath.TryIncomplete("0/30", out denominator), "未知分母不应判定");
            Require(!NestMath.TryIncomplete("00/24", out denominator) &&
                NestMath.TryIncomplete("1/24 0/41", out denominator) && denominator == 41,
                "匹配单个零字符并检查同一文本中的后续进度");
            Require(!NestMath.TryIncomplete("hello", out denominator), "无计数文本不应判定");
        }

        internal static void Keys()
        {
            var row = new Rectangle(20, 380, 20, 20);
            string first = NestMath.CacheKey(NestTab.TacetNest, 24, row, 900);
            string near = NestMath.CacheKey(NestTab.TacetNest, 24, new Rectangle(20, 384, 20, 20), 900);
            string otherTab = NestMath.CacheKey(NestTab.Purification, 24, row, 900);
            string scaled = NestMath.CacheKey(NestTab.TacetNest, 24, new Rectangle(40, 760, 40, 40), 1800);
            Require(first == "TacetNest:24:22", "缓存行槽应按原版使用 OCR 框的中心");
            Require(first == near, "轻微抖动应落同一行槽位");
            Require(first != otherTab, "不同页签应有不同键");
            Require(first == scaled, "同一相对位置的行应有相同键");
        }

        internal static void GuidebookCategoryAndScroll()
        {
            var tacet = new Rectangle(240, 653, 85, 24);
            var purification = new Rectangle(240, 755, 86, 23);
            var lines = new[]
            {
                new OcrLine { Text = "培养目标", Box = new Rectangle(237, 130, 88, 23), Score = .98f },
                new OcrLine { Text = "残象聚落", Box = tacet, Score = .97f },
                new OcrLine { Text = "梦魇祓除", Box = purification, Score = .96f }
            };
            Require(NestMath.BookCategoryButton(lines, false) == tacet &&
                NestMath.BookCategoryButton(lines, true) == purification,
                "图鉴切换应匹配准确的聚落分类名称");
            Require(NestMath.BookCategoryButton(new[] { lines[0] }, false).IsEmpty &&
                NestMath.BookCategoryButton(new[] { lines[0] }, true).IsEmpty,
                "培养目标出现聚落材料时不能当作已找到聚落分类");
            Require(NestMath.BookCategoryButton(new[]
                { new OcrLine { Text = "梦魔祓除", Box = purification, Score = .91f } }, true) == purification,
                "对实机窄裁剪中出现的梦魇标题识别异体，应仍能找到分类入口");
            var subtitle = new Rectangle(240, 782, 110, 19);
            Require(NestMath.BookCategoryButton(new[]
                { new OcrLine { Text = "产出梦魇声骸", Box = subtitle, Score = .93f } }, true) == subtitle &&
                NestMath.BookCategoryButton(new[]
                { new OcrLine { Text = "产出梦魇声骸", Box = subtitle, Score = .93f } }, false).IsEmpty,
                "分类标题缺失时使用该分类自己的可见副标题，不能识别成另一分类");
            using (var screenshot = new Bitmap(1600, 900))
            using (Graphics paint = Graphics.FromImage(screenshot))
            using (var reader = new FrameReader())
            {
                paint.Clear(Color.FromArgb(28, 41, 60));
                paint.FillRectangle(Brushes.White, 568, 185, 4, 605);
                paint.FillRectangle(Brushes.White, 1556, 282, 4, 507);
                paint.FillRectangle(Brushes.White, 1556, 130, 2, 9);
                PixelFrame frame = reader.Read(screenshot);
                Require(NestMath.ScrollThumb(frame, true) == new Rectangle(568, 185, 1, 605) &&
                    NestMath.ScrollThumb(frame, false) == new Rectangle(1556, 282, 1, 507),
                    "根据独立的侧栏和列表滑块确认滚动位置，忽略短亮点");

                var top = new Rectangle(1556, 179, 1, 508);
                var bottom = new Rectangle(1556, 285, 1, 508);
                Require(NestMath.AtBookListEdge(top, 900, false) && !NestMath.AtBookListEdge(top, 900, true) &&
                    NestMath.AtBookListEdge(bottom, 900, true) && !NestMath.AtBookListEdge(bottom, 900, false),
                    "长滑块覆盖中点不代表已经到末页，重试要区分顶部和底部");
                Require(NestMath.BookTrackControl(frame, bottom, false).IsEmpty, "没有实际轨道时不能发送兜底点击");
                paint.FillRectangle(Brushes.Black, 1557, 179, 1, 106);
                Rectangle rail = NestMath.BookTrackControl(reader.Read(screenshot), bottom, false);
                Require(rail.Left == 1557 && rail.Top > 179 && rail.Top < bottom.Top,
                    "回顶兜底点击必须在可见轨道内，避免点到实际轨道上方");
            }
        }

        internal static void GuidebookIntermediatePurification()
        {
            var first = new Rectangle(1557, 178, 1, 186);
            var next = new Rectangle(1557, 231, 1, 186);
            var jump = new Rectangle(1557, 440, 1, 186);
            Require(NestMath.BookViewportMovedDown(first, next, 900), "实机滚动后的滑块下移且前后视图重叠");
            Require(!NestMath.BookViewportMovedDown(first, first, 900) &&
                !NestMath.BookViewportMovedDown(next, first, 900) &&
                !NestMath.BookViewportMovedDown(first, Rectangle.Empty, 900) &&
                !NestMath.BookViewportMovedDown(Rectangle.Empty, next, 900) &&
                !NestMath.BookViewportMovedDown(first, jump, 900),
                "停留、倒退、滑块丢失或跨过完整一屏时，均不能确认下一个扫描位置");

            var title = new OcrLine
                { Text = "千殁沉岛梦魇聚落", Box = new Rectangle(740, 500, 179, 26), Score = .89f };
            var button = new OcrLine
                { Text = "直接挑战", Box = new Rectangle(1414, 529, 87, 30), Score = .99f };
            var progress = new OcrLine
                { Text = "已击败残象：0/36", Box = new Rectangle(741, 561, 163, 22), Score = .98f };
            var specialHeader = new OcrLine
                { Text = "特异梦魇", Box = new Rectangle(614, 620, 100, 31), Score = .92f };
            var specialTitle = new OcrLine
                { Text = "梦魇·赫卡忒", Box = new Rectangle(738, 682, 136, 30), Score = .87f };
            var viewport = new List<OcrLine> { title, button, progress, specialHeader, specialTitle };
            bool missing, ended;
            var size = new Size(1600, 900);
            var excluded = new HashSet<string>();
            NestTarget eligible = NestMath.InspectBookViewport(viewport, NestTab.Purification, size, next,
                excluded, 1, true, out missing, out ended);
            Require(eligible != null && eligible.Key == "Purification:千殁沉岛梦魇聚落" &&
                eligible.Defeated == 0 && eligible.Total == 36 && button.Box.Contains(eligible.Point) && !missing && !ended,
                "中间视图即使已出现特异梦魇标题，也须先处理其上方未完成的普通聚落");
            Require(NestMath.CountedPurificationEnded(viewport),
                "特异梦魇标题和上方可读的计数行共同证明普通聚落分组已经结束");
            progress.Text = "已击败残象：36/36";
            NestTarget updated = NestMath.ReadTargetProgress(viewport, eligible, size.Width, size.Height, 2);
            Require(updated != null && updated.Defeated == updated.Total &&
                NestMath.InspectBookViewport(viewport, NestTab.Purification, size, next,
                    excluded, 2, true, out missing, out ended) == null && !missing && ended,
                "通过名称复核清场后，即使实际滚动次数改变，也能在已核验的常规分组边界结束");
            Require(!NestMath.CountedPurificationEnded(new List<OcrLine> { title, button, progress }) &&
                !NestMath.CountedPurificationEnded(new List<OcrLine> { title, button, specialHeader, specialTitle }) &&
                !NestMath.CountedPurificationEnded(new List<OcrLine> { specialHeader, specialTitle }),
                "只露出普通标题或只识别出特异标题时，不能推断普通聚落已扫描到底");
            progress.Box = new Rectangle(741, 755, 163, 22);
            Require(!NestMath.CountedPurificationEnded(viewport),
                "特异分组标题下方的计数不构成上方普通聚落的结束证据");
        }

        internal static void TravelWaitsThroughLoading()
        {
            var anchor = new Rectangle(1093, 808, 68, 63);
            var other = new Rectangle(650, 808, 68, 63);
            var preparation = new NestTravelObservation();
            Require(!preparation.ObserveButton(NestTravelView.Button, anchor, anchor) &&
                !preparation.ObserveButton(NestTravelView.Unknown, anchor, Rectangle.Empty) &&
                !preparation.ObserveButton(NestTravelView.Button, anchor, anchor) &&
                !preparation.ObserveButton(NestTravelView.Button, anchor, other) &&
                !preparation.ObserveButton(NestTravelView.Button, anchor, anchor) &&
                preparation.ObserveButton(NestTravelView.Button, anchor, anchor),
                "短暂漏识别或另一位置的控件需要重新采样，连续确认原地图按钮后允许发送传送");

            var travel = new NestTravelObservation();
            travel.Observe(NestTravelView.Button);
            Require(travel.StableView == NestTravelView.Unknown && !travel.Arrived,
                "传送后首帧仍见原按钮不能判定抵达");
            travel.Observe(NestTravelView.Button);
            Require(travel.StableView == NestTravelView.Button && !travel.Arrived && !travel.CanConfirm,
                "连续显示地图按钮只是同一个地图界面");

            travel.Observe(NestTravelView.Unknown);
            travel.Observe(NestTravelView.Unknown);
            Require(travel.StableView == NestTravelView.Unknown && !travel.Arrived && !travel.CanConfirm,
                "加载画面没有可交互界面或到达证据");
            travel.Observe(NestTravelView.World);
            travel.Observe(NestTravelView.Unknown);
            travel.Observe(NestTravelView.World);
            Require(travel.StableView == NestTravelView.Unknown && !travel.Arrived,
                "加载中的单帧识别结果和不连续的世界画面均不能启动战斗");
            travel.Observe(NestTravelView.World);
            Require(travel.StableView == NestTravelView.World && travel.Arrived && !travel.CanConfirm,
                "连续两帧确认世界画面后才能继续巢穴挑战");
        }

        internal static void TravelObservesSameAutoLocalClientAcrossFocusChanges()
        {
            var handle = new IntPtr(101);
            const uint process = 202;
            var size = new Size(1920, 1080);
            var automatic = new NestTravelRoute(GameSource.AutoLocal, handle, process, size, GameSource.Background, 3);
            Require(automatic.Observe(handle, process, size, GameSource.Background, 3) && automatic.Version == 3,
                "同一路线继续观察传送画面");
            Require(automatic.Observe(handle, process, size, GameSource.Foreground, 4) && automatic.Version == 4 &&
                automatic.Observe(handle, process, size, GameSource.Background, 5) && automatic.Version == 5,
                "自动模式下同一个游戏窗口的焦点变化不应中断等待传送");

            Require(!new NestTravelRoute(GameSource.AutoLocal, handle, process, size, GameSource.Background, 3)
                .Observe(new IntPtr(104), process, size, GameSource.Foreground, 4),
                "更换窗口句柄后不能接受新的画面");
            Require(!new NestTravelRoute(GameSource.AutoLocal, handle, process, size, GameSource.Background, 3)
                .Observe(handle, process + 1, size, GameSource.Foreground, 4),
                "更换进程后不能接受新的画面");
            Require(!new NestTravelRoute(GameSource.AutoLocal, handle, process, size, GameSource.Background, 3)
                .Observe(handle, process, new Size(1280, 720), GameSource.Foreground, 4),
                "更换画面尺寸后不能继续沿用本次传送");
            Require(!new NestTravelRoute(GameSource.Background, handle, process, size, GameSource.Background, 3)
                .Observe(handle, process, size, GameSource.Foreground, 4),
                "固定后台模式不允许用不同模式的路线继续本次传送");
        }

        internal static void TravelConfirmsLatePromptOnce()
        {
            var travel = new NestTravelObservation();
            travel.Observe(NestTravelView.Button);
            travel.Observe(NestTravelView.Unknown);
            travel.Observe(NestTravelView.Confirm);
            Require(!travel.CanConfirm && !travel.Arrived, "迟到的确认弹窗也需要复核");
            travel.Observe(NestTravelView.Confirm);
            Require(travel.CanConfirm && !travel.Arrived, "连续确认画面允许发送确认点击");
            travel.ConfirmationAttempted();
            Require(travel.StableView == NestTravelView.Unknown && !travel.CanConfirm && !travel.Arrived,
                "确认点击后重新等待新画面");
            travel.Observe(NestTravelView.Confirm);
            travel.Observe(NestTravelView.Confirm);
            Require(travel.StableView == NestTravelView.Confirm && !travel.CanConfirm && !travel.Arrived,
                "仍显示同一弹窗时不能重复确认或判定抵达");
            travel.Observe(NestTravelView.World);
            travel.Observe(NestTravelView.World);
            Require(travel.Arrived && !travel.CanConfirm, "确认后仍需核实到达画面");
        }

        internal static void RetryUnstartedCombat()
        {
            Require(NestMath.CanRetryUnstartedCombat(CombatOutcome.Unverified, false, false) &&
                NestMath.CanRetryUnstartedCombat(CombatOutcome.NoCombat, false, false),
                "首次未确认开战时，观察到同一巢穴的敌人可重新锁敌");
            Require(!NestMath.CanRetryUnstartedCombat(CombatOutcome.Unverified, false, true) &&
                !NestMath.CanRetryUnstartedCombat(CombatOutcome.Unverified, true, false) &&
                !NestMath.CanRetryUnstartedCombat(CombatOutcome.Finished, false, false) &&
                !NestMath.CanRetryUnstartedCombat(CombatOutcome.Dead, false, false),
                "已经重试、已经开战、已清场或倒下应走各自的结果处理");

            var hud = new HudSnapshot { Valid = true };
            var ui = new FarmUi { InRealm = true };
            var exit = new NestTeamExitCheck();
            Require(exit.Observe(hud, ui, true, 0) == NestTeamExitState.Unverified &&
                exit.Observe(hud, ui, true, 100) == NestTeamExitState.Unverified,
                "仍在副本且暂时看不到目标时，不能凭少量帧发出退场输入");
            hud.EnemyHealth = true;
            Require(exit.Observe(hud, ui, true, 1800) == NestTeamExitState.Enemies,
                "退场前重新看到敌人血条时应留在挑战内");
            hud.EnemyHealth = false;
            Require(exit.Observe(hud, ui, true, 2000) == NestTeamExitState.Unverified &&
                exit.Observe(hud, ui, true, 2500) == NestTeamExitState.Unverified,
                "敌人重新出现后要重新累计安静画面");
            Require(exit.Observe(null, ui, false, 3000) == NestTeamExitState.Unverified &&
                exit.Observe(hud, ui, true, 3700) == NestTeamExitState.Unverified &&
                exit.Observe(hud, ui, true, 4500) == NestTeamExitState.Unverified &&
                exit.Observe(hud, ui, true, 5400) == NestTeamExitState.Quiet,
                "只有连续多张安静且可操作的截图才允许退场");
            ui.Death = true;
            Require(exit.Observe(hud, ui, true, 5500) == NestTeamExitState.Unverified,
                "倒下或复苏画面不能延续战斗结束确认");
        }

        internal static void ReplayGuidebook(string root)
        {
            var vision = new FarmVision();
            using (var reader = new FrameReader())
            {
                foreach (string file in new[] { "6.png", "5.png" })
                using (var image = new Bitmap(Path.Combine(root, "ok_templates", file)))
                {
                    Rectangle tab;
                    bool selected;
                    Require(vision.FindGuidebookBoss(reader.Read(image), out tab, out selected),
                        file + " 中应识别到 Boss 图鉴页签");
                    Require(selected == (file == "5.png"), file + " 的 Boss 图鉴选中状态识别错误");
                    Require(tab.Contains(new Point(63, 270)), file + " 的 Boss 图鉴点击位置偏移");
                }
                using (var image = new Bitmap(Path.Combine(root, "tests", "images", "echo2.png")))
                {
                    Rectangle tab;
                    bool selected;
                    Require(!vision.FindGuidebookBoss(reader.Read(image), out tab, out selected),
                        "大世界画面被误判为 Boss 图鉴");
                }
            }
        }

        internal static void ReplayNestEntry(string root)
        {
            var vision = new FarmVision();
            string screenshots = Path.Combine(root, "ok_templates");
            using (var reader = new FrameReader())
            {
                string[] guidebooks = { "5.png", "6.png" };
                foreach (string file in guidebooks)
                using (var image = new Bitmap(Path.Combine(screenshots, file)))
                {
                    PixelFrame frame = reader.Read(image);
                    Rectangle box;
                    string feature;
                    Require(vision.FindLabel(frame, "team_close", .72, out box),
                        "测试图鉴应复现旧版对关闭图标的误判");
                    Require(!NightmareNestRunner.FindNestEntry(vision, frame, out feature, out box),
                        "图鉴的关闭图标不能识别为队伍入口");
                }
                using (var image = new Bitmap(Path.Combine(screenshots, "42.png")))
                {
                    string feature;
                    Rectangle box;
                    Require(NightmareNestRunner.FindNestEntry(vision, reader.Read(image), out feature, out box) &&
                        feature == "team_start_challenge" && box.Contains(new Point(1493, 827)),
                        "队伍界面应返回开启挑战按钮的点击区域");
                }
                using (var image = new Bitmap(Path.Combine(screenshots, "travel.png")))
                {
                    string feature;
                    Rectangle box;
                    PixelFrame frame = reader.Read(image);
                    Require(NightmareNestRunner.FindNestEntry(vision, frame, out feature, out box) &&
                        feature == "gray_teleport", "传送页应继续识别为传送路线");
                    Rectangle menuButton;
                    Require(NightmareNestRunner.ReadTravelControls(vision, frame, vision.Read(frame, true),
                        feature, null, out menuButton) == NestTravelView.Button && menuButton.IntersectsWith(box),
                        "等待入场和传送应识别为同一个地图按钮");
                }
                using (var image = new Bitmap(Path.Combine(screenshots, "19.png")))
                {
                    PixelFrame frame = reader.Read(image);
                    Rectangle confirmation;
                    Require(NightmareNestRunner.ReadTravelControls(vision, frame, vision.Read(frame, true),
                        "gray_teleport", null, out confirmation) == NestTravelView.Confirm &&
                        confirmation.X > frame.Width / 2,
                        "真实的快速旅行弹窗应识别右侧确认，不能点击弹窗后的地图");
                }
            }
        }

        internal static void ReplayNestButtons(string root)
        {
            string screenshots = Path.Combine(root, "ok_templates");
            using (var reader = new FrameReader())
            using (var ocr = new OcrReader(OcrReader.Directory))
            {
                using (var image = new Bitmap(Path.Combine(screenshots, "42.png")))
                {
                    PixelFrame frame = reader.Read(image);
                    Rectangle button;
                    Require(NestMath.TryChallengeText(ocr.Read(frame, new Rectangle(1008, 747, 592, 144)), out button) &&
                        button.Contains(new Point(1374, 827)), "OCR 应定位繁体开启挑战按钮");
                }
                using (var image = new Bitmap(Path.Combine(screenshots, "travel.png")))
                {
                    PixelFrame frame = reader.Read(image);
                    Rectangle button;
                    string feature;
                    Point point;
                    Require(NightmareNestRunner.FindNestEntry(new FarmVision(), frame, out feature, out button) &&
                        NightmareNestRunner.TryTravelButtonText(ocr.Read(frame, new Rectangle(960, 775, 625, 117)),
                            button, frame.Width, out point) &&
                        new Rectangle(1260, 805, 150, 69).Contains(point) && point.X > button.Right,
                        "应点快速旅行整条按钮的文字中心，而非左边缘模板的中心");
                }
                using (var image = new Bitmap(Path.Combine(screenshots, "5.png")))
                {
                    List<OcrLine> lines = ocr.Read(reader.Read(image), new Rectangle(560, 225, 1040, 639));
                    OcrLine count = lines.Find(line => line.Text.Contains("48/48"));
                    Point button;
                    Require(count != null && NestMath.TryVisitPoint(lines, count.Box, 1600, out button) &&
                        new Rectangle(1380, 719, 62, 30).Contains(button),
                        "应点击计数同一行的前往按钮中心");
                }
            }
        }

        internal static void VisibleVisitButtons()
        {
            var lines = new List<OcrLine>
            {
                new OcrLine { Text = "前往", Box = new Rectangle(1409, 225, 48, 8), Score = .96f },
                new OcrLine { Text = "已击败残象：0/48", Box = new Rectangle(740, 246, 167, 25), Score = .99f },
                new OcrLine { Text = "前往", Box = new Rectangle(1409, 283, 48, 28), Score = .99f },
                new OcrLine { Text = "已击败残象：0/41", Box = new Rectangle(740, 313, 162, 23), Score = .99f },
                new OcrLine { Text = "前往", Box = new Rectangle(1408, 465, 48, 31), Score = .99f },
                new OcrLine { Text = "已击败残象：0/48", Box = new Rectangle(740, 497, 163, 23), Score = .99f }
            };
            var blocked = new HashSet<string>();
            bool missing;
            NestTarget first = NestMath.FindTarget(lines, NestTab.TacetScroll, 1600, 900, blocked, out missing);
            Require(missing && first != null && first.Point == new Point(1440, 292), "应跳过被裁切的第一行，完整行采用原版点击点");
            blocked.Add(first.ScanKey);
            NestTarget second = NestMath.FindTarget(lines, NestTab.TacetScroll, 1600, 900, blocked, out missing);
            Require(second != null && second.Point == new Point(1440, 476), "未能入场的目标应让位给下一行");
            blocked.Add(second.ScanKey);
            Require(NestMath.FindTarget(lines, NestTab.TacetScroll, 1600, 900, blocked, out missing) == null && missing,
                "没有完整按钮时不应返回推算坐标");
        }

        internal static void NamedTargetsAcrossPages()
        {
            var firstPage = new List<OcrLine>
            {
                new OcrLine { Text = "落渊南丘残象聚落", Box = new Rectangle(740, 250, 179, 28), Score = .99f },
                new OcrLine { Text = "前往", Box = new Rectangle(1409, 283, 48, 28), Score = .99f },
                new OcrLine { Text = "已击败残象：0/41", Box = new Rectangle(740, 313, 162, 23), Score = .99f }
            };
            var secondPage = new List<OcrLine>
            {
                new OcrLine { Text = "落渊南丘殘象聚落", Box = new Rectangle(740, 376, 179, 28), Score = .99f },
                new OcrLine { Text = "前往", Box = new Rectangle(1409, 409, 48, 28), Score = .99f },
                new OcrLine { Text = "已击败残象：0/41", Box = new Rectangle(740, 439, 162, 23), Score = .99f },
                new OcrLine { Text = "另一个残象聚落", Box = new Rectangle(740, 533, 179, 28), Score = .99f },
                new OcrLine { Text = "前往", Box = new Rectangle(1409, 566, 48, 28), Score = .99f },
                new OcrLine { Text = "已击败残象：0/41", Box = new Rectangle(740, 596, 162, 23), Score = .99f }
            };
            var excluded = new HashSet<string>();
            bool missing;
            NestTarget first = NestMath.FindTarget(firstPage, NestTab.TacetNest, 1600, 900, excluded, out missing);
            NestTarget shifted = NestMath.FindTarget(secondPage, NestTab.TacetScroll, 1600, 900, excluded, out missing);
            Require(first != null && first.Named && shifted != null && first.Key == shifted.Key,
                "同一聚落滚动后应保留点击前验证身份");
            Require(first.ScanKey != shifted.ScanKey, "参考不可达去重按页签和计数行槽，不按名称跨页排除");
            excluded.Add(first.ScanKey);
            Require(NestMath.FindTarget(secondPage, NestTab.TacetScroll, 1600, 900, excluded, out missing).Key == shifted.Key,
                "第一预设页的缓存不应排除第二预设页的同名聚落");
            excluded.Add(shifted.ScanKey);
            NestTarget next = NestMath.FindTarget(secondPage, NestTab.TacetScroll, 1600, 900, excluded, out missing);
            Require(next != null && next.Point == new Point(1440, 575),
                "同分母的其他聚落仍可选择");
        }

        internal static void PurificationTargets()
        {
            var top = new List<OcrLine>
            {
                new OcrLine { Text = "穂波市梦魔聚落", Box = new Rectangle(738, 250, 159, 28), Score = .91f },
                new OcrLine { Text = "直接挑战", Box = new Rectangle(1413, 281, 86, 27), Score = 1f },
                new OcrLine { Text = "已击败残象：0/36", Box = new Rectangle(740, 311, 164, 25), Score = 1f }
            };
            var scrolled = new List<OcrLine>
            {
                new OcrLine { Text = "穂波市夢魘聚落", Box = new Rectangle(738, 237, 159, 28), Score = .90f },
                new OcrLine { Text = "直接挑戰", Box = new Rectangle(1413, 268, 86, 27), Score = 1f },
                new OcrLine { Text = "已击败残象：0/36", Box = new Rectangle(740, 298, 164, 25), Score = 1f }
            };
            var excluded = new HashSet<string>();
            bool missing;
            NestTarget first = NestMath.FindTarget(top, NestTab.Purification, 1600, 900, excluded, out missing);
            Require(first != null && first.Point == new Point(1440, 288) && first.Named && !missing,
                "梦魇拔除应定位同一行的直接挑战按钮");
            NestTarget second = NestMath.FindTarget(scrolled, NestTab.PurificationScroll, 1600, 900,
                excluded, out missing, 1);
            Require(second != null && first.Key == second.Key, "滚动位置和魇字识别差异不应改变聚落身份");
            Require(first.ScanKey != second.ScanKey, "不可达缓存按参考页签行槽分离");
            excluded.Add(second.ScanKey);
            Require(NestMath.FindTarget(scrolled, NestTab.PurificationScroll, 1600, 900, excluded, out missing, 1) == null && !missing,
                "已处理的聚落不应重复挑战");
            Require(NestMath.FindTarget(top, NestTab.TacetNest, 1600, 900, excluded, out missing) == null,
                "检测到梦魇聚落名称时不应当成残象聚落");
        }

        internal static void SameNestProgress()
        {
            var lines = new List<OcrLine>
            {
                new OcrLine { Text = "落渊南丘残象聚落", Box = new Rectangle(740, 250, 179, 28), Score = .99f },
                new OcrLine { Text = "前往", Box = new Rectangle(1409, 283, 48, 28), Score = .99f },
                new OcrLine { Text = "已击败残象：0/41", Box = new Rectangle(740, 313, 162, 23), Score = .99f },
                new OcrLine { Text = "其他地方残象聚落", Box = new Rectangle(740, 378, 179, 28), Score = .99f },
                new OcrLine { Text = "前往", Box = new Rectangle(1409, 411, 48, 28), Score = .99f },
                new OcrLine { Text = "已击败残象：41/41", Box = new Rectangle(740, 441, 162, 23), Score = .99f }
            };
            bool missing;
            NestTarget target = NestMath.FindTarget(lines, NestTab.TacetScroll, 1600, 900,
                new HashSet<string>(), out missing);
            Require(target != null && target.Defeated == 0 && target.Total == 41,
                "零进度聚落应被选中");
            lines[2].Text = "已击败残象：5/41";
            Require(NestMath.FindTarget(lines, NestTab.TacetScroll, 1600, 900,
                new HashSet<string>(), out missing) == null, "零进度筛选模式不选择部分进度");
            NestTarget partial = NestMath.FindTarget(lines, NestTab.TacetScroll, 1600, 900,
                new HashSet<string>(), out missing, includePartial: true);
            Require(partial != null && partial.Key == target.Key && partial.Defeated == 5 && partial.Total == 41,
                "清理全部模式保留同一聚落的实际部分进度");
            Require(NestMath.HasVisibleProgress(lines) && !NestMath.HasVisibleProgress(new[] { lines[0], lines[1] }),
                "图鉴页只有标题和按钮、没有可信击败数时不能核验完成");
            Require(NestMath.FindTarget(lines, NestTab.TacetScroll, 1600, 900,
                new HashSet<string> { target.ScanKey }, out missing, includePartial: true) != null,
                "另一个聚落占过同一位置不能排除当前有名称的未完成聚落");
            NestTarget progress = NestMath.ReadTargetProgress(lines, target, 1600, 900, 1);
            Require(progress != null && progress.Defeated == 5,
                "其他聚落的满进度不能证明当前聚落已完成");
            lines[2].Text = "已击败残象：41/41";
            Require(NestMath.ReadTargetProgress(lines, target, 1600, 900, 0).Defeated == 41,
                "应能验证同一聚落的满进度");
            Require(NestMath.FindTarget(lines, NestTab.TacetScroll, 1600, 900,
                new HashSet<string>(), out missing) == null && !missing, "两个聚落已满进度时不再入场");
        }

        internal static void PartialNestPrecedesLaterUntouchedNest()
        {
            var lines = new List<OcrLine>
            {
                new OcrLine { Text = "落渊南丘残象聚落", Box = new Rectangle(730, 239, 179, 26), Score = .99f },
                new OcrLine { Text = "前往", Box = new Rectangle(1401, 269, 47, 29), Score = .99f },
                new OcrLine { Text = "已击败残象：2/41", Box = new Rectangle(733, 301, 162, 21), Score = .99f },
                new OcrLine { Text = "盲望之塌残象聚落", Box = new Rectangle(731, 422, 178, 27), Score = .99f },
                new OcrLine { Text = "前往", Box = new Rectangle(1401, 453, 47, 29), Score = .99f },
                new OcrLine { Text = "已击败残象：48/48", Box = new Rectangle(733, 485, 176, 22), Score = .99f },
                new OcrLine { Text = "复生丘原残象聚落", Box = new Rectangle(731, 549, 179, 29), Score = .99f },
                new OcrLine { Text = "前往", Box = new Rectangle(1401, 581, 47, 29), Score = .99f },
                new OcrLine { Text = "已击败残象：0/48", Box = new Rectangle(733, 612, 163, 22), Score = .99f }
            };
            bool missing;
            var excluded = new HashSet<string>();
            NestTarget target = NestMath.FindTarget(lines, NestTab.TacetScroll, 1600, 900, excluded,
                out missing, includePartial: true);
            Require(target != null && target.Named && target.Key == "TacetNest:落渊南丘残象聚落" &&
                target.Defeated == 2 && target.Total == 41 && lines[1].Box.Contains(target.Point),
                "清理全部时优先处理列表中已打到 2/41 的同名聚落和它的前往按钮");

            lines[0].Text = "临时文字";
            target = NestMath.FindTarget(lines, NestTab.TacetScroll, 1600, 900, excluded,
                out missing, includePartial: true);
            Require(target != null && target.Key == "TacetNest:复生丘原残象聚落" &&
                target.Defeated == 0 && target.Total == 48,
                "名称无法核对的部分进度和已经打满的聚落都不能入场");
        }

        internal static void AbsorbLabels()
        {
            Require(NestMath.IsAbsorbText("吸收") && NestMath.IsAbsorbText("Absorb") &&
                !NestMath.IsAbsorbText("领取奖励") && !NestMath.IsAbsorbText("开启挑战"),
                "拾取应要求吸收文案");
        }

        internal static void ReplayAbsorbPrompt(string root)
        {
            string images = Path.Combine(root, "tests", "images");
            var vision = new FarmVision();
            using (var reader = new FrameReader())
            using (var ocr = new OcrReader(OcrReader.Directory))
            using (var source = new Bitmap(Path.Combine(images, "absorb.png")))
            using (var shifted = new Bitmap(source))
            using (var below = new Bitmap(source))
            using (var unrelated = new Bitmap(Path.Combine(images, "xinyao_interaction_different_prompt.png")))
            {
                MovePrompt(source, shifted, new Rectangle(1194, 521, 404, 66), -54);
                MovePrompt(source, below, new Rectangle(1342, 540, 57, 29), 44);

                PixelFrame frame = reader.Read(source);
                Require(NestMath.ReadEchoPrompt(frame, vision.Read(frame, false), vision, ocr) == EchoPrompt.Selected,
                    "原版吸收截图应能拾取");
                frame = reader.Read(shifted);
                FarmUi shiftedUi = vision.Read(frame, false);
                Require(!shiftedUi.FPrompt && NestMath.ReadEchoPrompt(frame, shiftedUi, vision, ocr) == EchoPrompt.Selected,
                    "提示上移一行时应在扩展区域找到 F 和吸收文字");
                frame = reader.Read(below);
                Require(NestMath.ReadEchoPrompt(frame, vision.Read(frame, false), vision, ocr) == EchoPrompt.Below,
                    "未选中的下一行吸收提示应先滚动选择");
                frame = reader.Read(unrelated);
                Require(NestMath.ReadEchoPrompt(frame, vision.Read(frame, false), vision, ocr) == EchoPrompt.None,
                    "不可将其他交互误认为声骸");
            }
        }

        internal static void ReceiptMustBeNew()
        {
            var stale = new PickupReceiptCheck(alreadyVisible: true);
            Require(!stale.Observe(true) && !stale.Observe(true), "上一次获取通知不能证明这次吸收");
            Require(!stale.Observe(false) && !stale.Observe(true), "单帧闪烁不能刷新确认");
            Require(!stale.Observe(false) && !stale.Observe(false) && !stale.Observe(true) && stale.Observe(true),
                "旧通知离开后，两帧新通知应确认拾取");

            var fresh = new PickupReceiptCheck(alreadyVisible: false);
            Require(!fresh.Observe(false) && !fresh.Observe(true) && fresh.Observe(true),
                "按键后出现的新通知应确认拾取");
            var noReceipt = new PickupReceiptCheck(alreadyVisible: false);
            Require(!noReceipt.Observe(false) && !noReceipt.Observe(false), "无通知时不能确认拾取");
        }

        internal static void ReplayPickupReceipt(string root)
        {
            using (var reader = new FrameReader())
            using (var source = new Bitmap(Path.Combine(root, "tests", "images", "absorb.png")))
            using (var echo = new Bitmap(Path.Combine(root, "tests", "images", "echo2.png")))
            {
                Require(!FarmVision.HasEchoReceipt(reader.Read(source)), "只有吸收提示的画面不能确认拾取");
                Require(!FarmVision.HasEchoReceipt(reader.Read(echo)), "地面声骸本身不能确认拾取");
                foreach (string label in TaskIndicators.EchoReceiptDigits)
                {
                    using (Bitmap notification = RenderPickupReceipt(root, label, new Point(128, 442)))
                        Require(FarmVision.HasEchoReceipt(reader.Read(notification)), "应识别原版通知数字 " + label);
                    using (Bitmap unrelated = RenderPickupReceipt(root, label, new Point(1020, 450)))
                        Require(!FarmVision.HasEchoReceipt(reader.Read(unrelated)), "通知区域之外的数字不能确认拾取");
                }
            }
        }

        internal static Bitmap RenderPickupReceipt(string root, string label, Point position)
        {
            VisualPattern pattern = PatternBank.For(label)[0];
            string[] matches = Directory.GetFiles(Path.Combine(root, "assets", "templates"), label + "_*.png");
            using (var source = new Bitmap(matches[0]))
            using (var scaled = new Bitmap(pattern.Area.Width, pattern.Area.Height))
            using (Graphics graphics = Graphics.FromImage(scaled))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                graphics.DrawImage(source, new Rectangle(Point.Empty, scaled.Size));
                var frame = new Bitmap(1600, 900);
                using (Graphics background = Graphics.FromImage(frame))
                    background.Clear(Color.FromArgb(24, 30, 34));
                for (int y = 0; y < scaled.Height; y++)
                    for (int x = 0; x < scaled.Width; x++)
                    {
                        Color pixel = scaled.GetPixel(x, y);
                        if ((3 * pixel.R + 6 * pixel.G + pixel.B) / 10 < 137)
                            frame.SetPixel(position.X + x, position.Y + y, Color.White);
                    }
                return frame;
            }
        }

        private static void MovePrompt(Bitmap source, Bitmap target, Rectangle area, int dy)
        {
            using (Bitmap patch = source.Clone(area, source.PixelFormat))
            using (Graphics graphics = Graphics.FromImage(target))
            {
                graphics.FillRectangle(Brushes.Black, area);
                graphics.DrawImageUnscaled(patch, area.X, area.Y + dy);
            }
        }

        internal static void Options()
        {
            NightmareOptions options = NightmareOptions.Defaults();
            options.Validate();
            options.TacetNest = false;
            options.Purification = false;
            options.Validate();
            Require(options.TacetNest && options.Purification, "空选择按参考回退默认两类");
            options.TacetNest = false;
            options.Validate();
            Require(!options.TacetNest && options.Purification, "已有明确选择不能被默认值覆盖");

            int probes = 0;
            Func<bool> nextWave = () => { probes++; return true; };
            Require(!NestMath.ShouldContinueCombatAfterPickup(true, nextWave) && probes == 0,
                "单次捕获后不探测或继续下一波战斗");
            Require(NestMath.ShouldContinueCombatAfterPickup(false, nextWave) && probes == 1 &&
                !NestMath.ShouldContinueCombatAfterPickup(false, () => false),
                "全刷模式仅在探测到下一波敌人时继续战斗");
            Require(NestMath.AcceptPickupFlow(true, true, true, false) &&
                !NestMath.AcceptPickupFlow(true, false, true, false) &&
                !NestMath.AcceptPickupFlow(true, true, false, false) &&
                !NestMath.AcceptPickupFlow(true, true, true, true),
                "参考拾取动作结果与新获取回执分离，只接受同路由、已确认游戏场景且无错误领奖的有效交互");
        }
    }
}
