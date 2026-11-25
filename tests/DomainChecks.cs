using System;
using System.Drawing;
using System.IO;
using System.Linq;

namespace TideCombat
{
    internal static class DomainChecks
    {
        private static void Require(bool condition, string explanation)
        {
            if (!condition)
                throw new InvalidOperationException(explanation);
        }

        internal static void Arithmetic()
        {
            Require(DomainMath.WaitForCombatMs == 200000 && DomainMath.CombatTimeoutMs == 0,
                "按原版等待开战 200 秒，开始战斗后不另设硬时限");
            int current, backup, total;
            DomainMath.ParseStamina(new[] { "160/240" }, out current, out backup, out total);
            Require(current == 160 && backup == 0 && total == 160, "斜杠右侧是上限，不应算作备用体力");

            DomainMath.ParseStamina(new[] { "160/240", "80" }, out current, out backup, out total);
            Require(current == 160 && backup == 80 && total == 240, "备用体力应取自单独数字");

            DomainMath.ParseStamina(new[] { "120" }, out current, out backup, out total);
            Require(current == 0 && backup == 120 && total == 120, "只有数字时按参考视为备用体力");

            double previous = 0;
            for (int serial = 1; serial <= 20; serial++)
            {
                double y = DomainMath.ScrollOffset(.1963, serial, 21, new[] { 4, 5, 5, 7 });
                Require(y > previous && y <= .8806, "滚动位置应递增且不超过底部");
                previous = y;
            }
            double fallback = DomainMath.ScrollOffset(0, 5, 0, new[] { 4, 5, 5, 7 });
            Require(Math.Abs(fallback - 424.0 / 2160) < 1e-9, "无总数量时应回退顶部");

            Require(DomainMath.RewardCost(80, 40, 40, 0) == 80, "当前体力足够双倍时优先选择双倍");
            Require(DomainMath.RewardCost(40, 40, 40, 60) == 40 &&
                DomainMath.RewardCost(40, 40, 41, 60) == 80, "使用备用计算双倍的日常阈值是严格大于一次");
            Require(!DomainMath.CanEnter(DomainKind.Forgery, 80, 0, 120, 40) &&
                DomainMath.CanEnter(DomainKind.Forgery, 80, 40, 120, 40), "材料本检查整个日常目标");
            Require(!DomainMath.CanEnter(DomainKind.Forgery, 0, 120, 0, 40) &&
                DomainMath.CanEnter(DomainKind.Tacet, 0, 60, 0, 60), "无音区与材料本的单刷入场条件不同");
            Require(DomainMath.CanContinue(120, 0, 80, 40, 40) &&
                !DomainMath.CanContinue(40, 80, 40, 40, 40) &&
                DomainMath.CanContinue(40, 80, 40, 41, 40), "下一轮按领取前的当前、备用和剩余目标共同决定");
        }

        internal static void HeaderBounds()
        {
            DomainRunner.StaminaReadout readout;
            var header = new OcrLine { Text = "1240/240", Box = new Rectangle(880, 30, 75, 19), Score = .95f };
            Require(DomainRunner.TryReadStaminaHeader(new[] { header }, 1200, out readout) &&
                readout.Current == 1240 && readout.Limit == 240 && !readout.BackupKnown,
                "手动补充后的波片数量可以高于自然恢复上限");
            header.Text = "0/180";
            Require(!DomainRunner.TryReadStaminaHeader(new[] { header }, 1200, out readout),
                "日常任务计数不能作为波片余额");
            foreach (string text in new[] { "365   220/240", "365 | 220 / 240", "３６５｜２２０／２４０" })
            {
                header.Text = text;
                Require(DomainRunner.TryReadStaminaHeader(new[] { header }, 1200, out readout) &&
                    readout.Current == 220 && readout.BackupKnown && readout.Backup == 365 && readout.Limit == 240,
                    "OCR 将两个合法余额识别在一行时应分别读取");
            }
            foreach (string text in new[] { "365220/240", "365 | 0/180", "365 | 220/240 | 0/180" })
            {
                header.Text = text;
                Require(!DomainRunner.TryReadStaminaHeader(new[] { header }, 1200, out readout),
                    "黏连数字或多个不同计数不可作为已确认余额");
            }
        }

        internal static void SpendControlUsesFullButton()
        {
            var doublePrice = new OcrLine { Text = "120", Box = new Rectangle(954, 555, 42, 22), Score = .99f };
            var doubleLabel = new OcrLine { Text = "双倍领取", Box = new Rectangle(1045, 552, 98, 26), Score = .99f };
            var singlePrice = new OcrLine { Text = "60", Box = new Rectangle(439, 555, 32, 22), Score = .99f };
            var singleLabel = new OcrLine { Text = "单倍领取", Box = new Rectangle(531, 552, 98, 26), Score = .99f };
            var doubleButton = DomainMath.SpendControl(new[] { doubleLabel, doublePrice }, true, 120);
            Require(doubleButton == Rectangle.Union(doublePrice.Box, doubleLabel.Box) &&
                new Rectangle(902, 540, 294, 49).Contains(
                    new Point(doubleButton.Left + doubleButton.Width / 2, doubleButton.Top + doubleButton.Height / 2)),
                "双倍领取使用同一行的费用与按钮文字，共同定位实际可点击按钮");
            Require(DomainMath.SpendControl(new[] { singleLabel, singlePrice }, false, 60) ==
                Rectangle.Union(singlePrice.Box, singleLabel.Box),
                "单倍领取也需要核对同一按钮内的费用与文字");

            Require(DomainMath.SpendControl(new[] { doubleLabel, doublePrice }, true, 80).IsEmpty &&
                DomainMath.SpendControl(new[] { doubleLabel, doublePrice }, false, 120).IsEmpty,
                "费用和领取消耗类型都必须匹配");
            doublePrice.Box = new Rectangle(1154, 555, 42, 22);
            Require(DomainMath.SpendControl(new[] { doubleLabel, doublePrice }, true, 120).IsEmpty,
                "按钮右侧的相同数字不能作为费用");
            doublePrice.Box = new Rectangle(954, 495, 42, 22);
            Require(DomainMath.SpendControl(new[] { doubleLabel, doublePrice }, true, 120).IsEmpty,
                "其他行的相同数字不能作为费用");

            var combined = new OcrLine
            {
                Text = "120 | 双倍领取", Box = new Rectangle(954, 552, 189, 26), Score = .99f
            };
            Require(DomainMath.SpendControl(new[] { combined }, true, 120) == combined.Box,
                "费用和领取文字被 OCR 合并时仍使用同一按钮");
        }

        internal static void TacetSettlementConfirmsSpend()
        {
            var heading = new OcrLine { Text = "挑战成功", Box = new Rectangle(706, 239, 184, 53), Score = .99f };
            var exit = new OcrLine { Text = "退出副本", Box = new Rectangle(535, 749, 108, 37), Score = .99f };
            var restart = new OcrLine { Text = "重新挑战", Box = new Rectangle(955, 752, 106, 33), Score = .99f };
            var remaining = new OcrLine { Text = "剩余106", Box = new Rectangle(941, 813, 85, 27), Score = .99f };
            TacetSettlement settlement;
            Require(TacetSettlement.HasSuccessHeading(new[] { heading }) &&
                TacetSettlement.TryParseActions(new[] { exit, restart, remaining }, out settlement) &&
                settlement.Exit == exit.Box && settlement.Restart == restart.Box && settlement.Remaining == 106 &&
                settlement.ConfirmsSpend(226, 120) && !settlement.ConfirmsSpend(226, 60),
                "无音区结果必须区分退出和重试，并读取同一区域中的结算余额");
            Require(TacetSettlement.TryParseActions(new[] { exit, restart, remaining }, out settlement),
                "后续模拟需要已定位的无音区结算控件");
            var check = new TacetDebitCheck();
            Require(!check.Observe(settlement, 226, 120), "一次读数不能直接确认扣减");
            check.Reset();
            Require(!check.Observe(settlement, 226, 120) && check.Observe(settlement, 226, 120) &&
                DomainMath.CanContinue(226, 360, 120, 180, 60) && DomainMath.RewardCost(106, 60, 60, 360) == 60,
                "两帧结算确认扣除 120 后，下一轮按剩下的 60 计划消费");
            TacetSettlement incomplete, conflicting;
            var other = new OcrLine { Text = "剩余166", Box = new Rectangle(942, 845, 85, 27), Score = .96f };
            Require(!TacetSettlement.TryParseActions(new[] { restart, remaining }, out incomplete) &&
                TacetSettlement.TryParseActions(new[] { exit, restart, remaining, other }, out conflicting) &&
                !conflicting.ConfirmsSpend(226, 120),
                "缺少退出控件或余额有冲突时保留未核实状态");
        }

        internal static void SpendRetryRequiresUnchangedBalanceAndControl()
        {
            var before = new DomainRunner.StaminaReadout
            {
                Current = 200, Backup = 360, Limit = 240, BackupKnown = true,
                Position = new Rectangle(1178, 44, 103, 21)
            };
            var button = new Rectangle(954, 552, 189, 26);
            Require(DomainRunner.CanRetrySpend(before, before, button, button),
                "只有仍显示同一领奖按钮且确认两项余额和计数位置未变化才可以重试");

            var observed = before;
            observed.Current = 80;
            Require(!DomainRunner.CanRetrySpend(before, observed, button, button), "已扣体力时不能再点");
            observed.Current = 201;
            Require(!DomainRunner.CanRetrySpend(before, observed, button, button), "当前余额变化时不重试");
            observed = before;
            observed.Backup = 359;
            Require(!DomainRunner.CanRetrySpend(before, observed, button, button), "备用余额变化时不能再点");
            observed = before;
            observed.BackupKnown = false;
            Require(!DomainRunner.CanRetrySpend(before, observed, button, button) &&
                !DomainRunner.CanRetrySpend(observed, before, button, button), "前后均需要可确认的备用余额");
            observed = before;
            observed.Limit = 360;
            Require(!DomainRunner.CanRetrySpend(before, observed, button, button), "体力上限变化时不能再点");
            observed = before;
            observed.Position = new Rectangle(340, 420, 103, 21);
            Require(!DomainRunner.CanRetrySpend(before, observed, button, button),
                "不同位置的体力计数不能证明原界面未变");
            Require(!DomainRunner.CanRetrySpend(before, before, button, new Rectangle(439, 552, 190, 26)) &&
                !DomainRunner.CanRetrySpend(before, before, button, new Rectangle(1145, 662, 227, 31)) &&
                !DomainRunner.CanRetrySpend(before, before, button, Rectangle.Empty) &&
                !DomainRunner.CanRetrySpend(before, before, Rectangle.Empty, button),
                "不同按钮、画面几何变化或按钮消失时不能重复点击");
        }

        internal static void RewardInteractionRequiresSelectedRow()
        {
            var key = new Rectangle(821, 445, 20, 20);
            var reward = new OcrLine { Text = "领取奖励", Box = new Rectangle(855, 444, 91, 22), Score = .99f };
            var other = new OcrLine { Text = "重新挑战", Box = new Rectangle(855, 479, 91, 22), Score = .99f };
            Require(DomainMath.IsSelectedRewardPrompt(new[] { other, reward }, key),
                "领奖文字和 F 同行时才可以交互");

            reward.Box = new Rectangle(855, 479, 91, 22);
            other.Box = new Rectangle(855, 444, 91, 22);
            Require(!DomainMath.IsSelectedRewardPrompt(new[] { reward, other }, key),
                "另一行可以领奖，但当前 F 选中重新挑战时不能按 F 领奖");

            reward.Box = new Rectangle(855, 444, 91, 22);
            reward.Score = .2f;
            Require(!DomainMath.IsSelectedRewardPrompt(new[] { reward }, key),
                "低置信度的领奖文字不能作为交互依据");
            reward.Score = .99f;
            reward.Text = "领取奖励预览";
            Require(!DomainMath.IsSelectedRewardPrompt(new[] { reward }, key),
                "含领奖字样的其他说明文字不是领奖入口");
        }

        internal static void LeaveDialogSeparatesRestartAndExit()
        {
            var size = new Size(1600, 900);
            var title = new OcrLine { Text = "确认离开", Box = new Rectangle(738, 413, 109, 38), Score = .99f };
            var left = new OcrLine { Text = "重新挑战", Box = new Rectangle(485, 549, 105, 32), Score = .99f };
            var right = new OcrLine { Text = "确认", Box = new Rectangle(1020, 548, 60, 34), Score = .99f };
            var recognized = FarmVision.ParseLeaveDialog(new[] { left, right, title }, size);
            Require(recognized.Recognized && recognized.Restart == left.Box && recognized.Exit == right.Box,
                "确认离开弹窗的左侧重新挑战与右侧确认必须定位到各自的文字");

            title.Text = "领取奖励";
            var reward = FarmVision.ParseLeaveDialog(new[] { left, right, title }, size);
            Require(!reward.Recognized && reward.Restart.IsEmpty && reward.Exit.IsEmpty,
                "领奖弹窗不能复用离开或重开按钮");
            title.Text = "跳过剧情";
            var skip = FarmVision.ParseLeaveDialog(new[] { left, right, title }, size);
            Require(!skip.Recognized && skip.Restart.IsEmpty && skip.Exit.IsEmpty,
                "跳过弹窗的确认按钮不能当成离开副本");

            title.Text = "确认离开";
            Rectangle leftBox = left.Box;
            left.Box = right.Box;
            right.Box = leftBox;
            var swapped = FarmVision.ParseLeaveDialog(new[] { left, right, title }, size);
            Require(swapped.Restart.IsEmpty && swapped.Exit.IsEmpty,
                "标签跑到另一侧后，不能再作为该操作的点击坐标");
        }

        internal static void GuideScrollbar()
        {
            var size = new Size(2100, 900);
            Rectangle area = GuidebookList.Area(size, .969, .191, .978, .271);
            Require(area == new Rectangle(2050, 172, 14, 72) &&
                GuidebookList.PointAt(size, .973, .4) == new Point(2057, 360) &&
                GuidebookList.PointAt(new Size(1600, 900), .973, .4) == new Point(1556, 360),
                "宽屏滚动条应保留距右边缘的锚点");
            const int width = 14, height = 72;
            var mask = new bool[width * height];
            for (int x = 0; x < 9; x++) mask[x] = true;
            Require(GuidebookList.FindBar(mask, width, height, 3, 6) == -1,
                "单行亮色干扰不能被当作滚动条");
            for (int y = 15; y < 42; y++)
                for (int x = 5; x < 10; x++) mask[y * width + x] = true;
            Require(GuidebookList.FindBar(mask, width, height, 3, 6) == 15,
                "读取符合尺寸和颜色的滚动条顶部");
        }

        internal static void TacetGuideButtons()
        {
            Rectangle area = GuidebookList.Area(new Size(1600, 900), .80, .213, .964, .859);
            var go = new Rectangle(1409, 243, 49, 32);
            var second = new Rectangle(1414, 372, 87, 29);
            var third = new Rectangle(1413, 555, 89, 31);
            var sixth = new Rectangle(1414, 684, 87, 28);
            var buttons = DomainMath.TacetEntryButtons(new[]
            {
                new OcrLine { Text = "直接挑战", Box = sixth, Score = .99f },
                new OcrLine { Text = "前往", Box = go, Score = .99f },
                new OcrLine { Text = "直接挑战", Box = second, Score = .99f },
                new OcrLine { Text = "直接挑战", Box = new Rectangle(1415, 685, 86, 27), Score = .97f },
                new OcrLine { Text = "直接 挑战", Box = third, Score = .99f },
                new OcrLine { Text = "…", Box = new Rectangle(1504, 628, 28, 22), Score = .99f },
                new OcrLine { Text = "直接挑战次数", Box = new Rectangle(1400, 450, 110, 29), Score = .99f },
                new OcrLine { Text = "直接挑战", Box = new Rectangle(1414, 757, 87, 28), Score = .99f },
                new OcrLine { Text = "直接挑战", Box = new Rectangle(1414, 638, 87, 28), Score = .50f },
                null
            }, area);

            Require(buttons.SequenceEqual(new[] { go, second, third, sixth }),
                "无音区应按垂直顺序保留完整且可信的入口，排除行间误识别、重复项及被列表裁切的下一行");
            Point click = DomainMath.VerifiedReferencePoint(buttons[3], Point.Empty);
            Require(click == new Point(1457, 698), "第 6 项应点击冰原运输港的直接挑战文字中心");
        }

        internal static void StableTacetGuideButtons()
        {
            var first = new Rectangle(1410, 240, 50, 32);
            var second = new Rectangle(1414, 684, 87, 28);
            var rows = new[] { first, second };
            Require(DomainMath.SameBookRows(rows, new[]
            {
                new Rectangle(1412, 241, 47, 31), new Rectangle(1410, 682, 90, 29)
            }, 900), "同一列表允许 OCR 框的少量变化");
            Require(!DomainMath.SameBookRows(rows, new[] { first, new Rectangle(1414, 556, 87, 28) }, 900) &&
                !DomainMath.SameBookRows(rows, new[] { second, first }, 900) &&
                !DomainMath.SameBookRows(rows, new[] { first }, 900) &&
                !DomainMath.SameBookRows(new Rectangle[0], new Rectangle[0], 900),
                "滚动、重排、遮挡或空画面均不能作为同一组选项的依据");
        }

        internal static void Options()
        {
            DomainOptions options = DomainOptions.Defaults();
            options.Validate();
            Require(options.Kind == DomainKind.Tacet && options.Index == 1, "默认体力本配置错误");
            options.Index = 17; // 旧版配置仅有当前 Index。
            DomainOptions forgery = options.ForKind(DomainKind.Forgery);
            Require(forgery.Index == 1 && forgery.TacetIndex == 17, "缺省材料本序号不应沿用旧的无音区序号");
            forgery.SetIndexForKind(DomainKind.Forgery, 8);
            DomainOptions tacet = forgery.ForKind(DomainKind.Tacet);
            Require(tacet.Index == 17 && tacet.ForgeryIndex == 8 && tacet.ForKind(DomainKind.Forgery).Index == 8,
                "两类副本的序号应该独立保存并可重复切换");
            bool threw = false;
            try
            {
                options.Index = 0;
                options.Validate();
            }
            catch (System.IO.InvalidDataException) { threw = true; }
            Require(threw, "越界序号应拒绝");
            options.Index = 1;
            options.MaxStamina = -1;
            threw = false;
            try { options.Validate(); }
            catch (System.IO.InvalidDataException) { threw = true; }
            Require(threw, "负预算应拒绝");
        }

        internal static void NavigationAndRechallenge()
        {
            bool centered = false;
            var screen = new Size(1600, 900);
            Require(DomainMath.TreasureDirection(new Rectangle(950, 510, 20, 20), screen, '\0', ref centered) == 'D' &&
                !centered, "宝箱位于右侧时应先横移");
            Require(DomainMath.TreasureDirection(new Rectangle(795, 230, 20, 20), screen, 'D', ref centered) == 'W' &&
                centered, "进入中心带后向前接近");
            Require(DomainMath.TreasureDirection(new Rectangle(930, 790, 20, 20), screen, 'W', ref centered) == 'S',
                "已对准的宝箱在角色后方时应后退");
            var near = new Rectangle(810, 440, 24, 24);
            var duplicate = new Rectangle(800, 436, 45, 43);
            var far = new Rectangle(422, 612, 24, 24);
            var waypoints = FarmVision.SortWaypoints(new[] { far, near, duplicate }, new Size(1600, 900));
            Require(waypoints.Count == 2 && waypoints[0] == near && waypoints[1] == far,
                "重叠的大小信标应去重，尝试次序应按屏幕中心的距离排序");

            Require(DomainMath.ReferenceCategoryPoint(DomainKind.Tacet, screen, new Rectangle(150, 644, 80, 27)) == new Point(384, 657) &&
                DomainMath.ReferenceCategoryPoint(DomainKind.Forgery, screen, new Rectangle(150, 345, 80, 30)) == new Point(384, 360) &&
                DomainMath.ReferenceCategoryPoint(DomainKind.Simulation, screen, new Rectangle(150, 254, 80, 32)) == new Point(384, 270),
                "正常图鉴布局采用参考三类分类固定点");
            Require(DomainMath.ReferenceCategoryPoint(DomainKind.Tacet, screen, new Rectangle(150, 440, 80, 27)).IsEmpty,
                "分类位置偏移时不点击过期固定坐标");
            Require(DomainMath.VerifiedReferencePoint(near, new Point(814, 446)) == new Point(814, 446) &&
                DomainMath.VerifiedReferencePoint(near, far.Location) == new Point(822, 452),
                "参考点在已验证按钮内时使用，否则落在按钮中心");

            foreach (string label in new[] { "重新挑战", "重新挑戰", "再次挑战", "再次挑戰", "Rechallenge" })
                Require(DomainMath.RechallengeButton(new[] { new OcrLine { Text = label, Box = near, Score = .95f } }) == near,
                    "应识别参考项目支持的再挑战文字 " + label);
            Require(DomainMath.RechallengeButton(new[]
            {
                new OcrLine { Text = "再次挑战次数", Box = near, Score = .95f },
                new OcrLine { Text = "Rechallenge", Box = far, Score = .2f }
            }).IsEmpty, "非按钮文字和低可信度文字不应成为再挑战点击目标");
        }

        internal static void RecoveryMap(string root)
        {
            string folder = Path.Combine(root, "ok_templates");
            string map = Directory.GetFiles(folder, "39cad5e8*.png")[0];
            var vision = new FarmVision();
            using (var reader = new FrameReader())
            using (var capture = new Bitmap(map))
            using (var small = new Bitmap(capture, new Size(1600, 900)))
            using (var blank = new Bitmap(1600, 900))
            {
                foreach (Bitmap image in new[] { capture, small })
                {
                    var points = vision.FindWaypoints(reader.Read(image));
                    Require(points.Count >= 2, "地图应识别普通和大型信标");
                    Require(Math.Abs(points[0].X - 713) <= 3 && Math.Abs(points[0].Y - 435) <= 3 &&
                        Math.Abs(points[1].X - 548) <= 3 && Math.Abs(points[1].Y - 340) <= 3,
                        "应先尝试靠近中心的普通信标，再尝试大型信标");
                }
                Require(vision.FindWaypoints(reader.Read(blank)).Count == 0, "空白地图不能触发传送");
            }
        }

        internal static void ReplayRewardControls(string root)
        {
            string templates = Path.Combine(root, "ok_templates");
            using (var reader = new FrameReader())
            using (var ocr = new OcrReader(OcrReader.Directory))
            using (var claim = new Bitmap(Path.Combine(templates, "43.png")))
            using (var replenish = new Bitmap(Path.Combine(templates,
                "22ed9455-BitBlt_True_3840x2160_1753117487906.06_original.png")))
            using (var guidebook = new Bitmap(Path.Combine(templates, "5.png")))
            {
                var vision = new FarmVision();
                Rectangle box;
                PixelFrame frame = reader.Read(claim);
                Require(vision.FindLabel(frame, "claim_stamina_sign", .82, out box),
                    "识别原版领奖标记");
                var single = ocr.Read(frame, Area(frame, .23, .57, .47, .73));
                var doubleReward = ocr.Read(frame, Area(frame, .56, .57, .76, .73));
                Require(!DomainMath.SpendControl(single, false, 60).IsEmpty &&
                    DomainMath.SpendControl(single, false, 40).IsEmpty, "确认原版单倍按钮与 60 体力");
                Require(!DomainMath.SpendControl(doubleReward, true, 120).IsEmpty &&
                    DomainMath.SpendControl(doubleReward, true, 80).IsEmpty, "确认原版双倍按钮与 120 体力");
                RequireStamina(ocr, frame, 61, 365);

                frame = reader.Read(replenish);
                Require(vision.FindLabel(frame, "gem_add_stamina", .80, out box) &&
                    !vision.FindLabel(frame, "claim_stamina_sign", .82, out box),
                    "补充弹窗与领奖弹窗应分开识别");
                RequireStamina(ocr, frame, 20, 93);
                Require(!DomainRunner.RefillConfirmButton(ocr.Read(frame, Area(frame, .55, .60, .85, .82))).IsEmpty,
                    "确认原始补充画面当前选中的结晶溶剂");
                Require(DomainRunner.RefillConfirmButton(ocr.Read(frame, Area(frame, .18, .60, .45, .82))).IsEmpty,
                    "左侧取消按钮不能作为确认");

                frame = reader.Read(guidebook);
                RequireStamina(ocr, frame, 220, 365);
                var headings = ocr.Read(frame, Area(frame, .095, .11, .35, .91));
                Rectangle forgery = DomainMath.BookKindButton(headings, DomainKind.Forgery);
                Rectangle simulation = DomainMath.BookKindButton(headings, DomainKind.Simulation);
                Rectangle tacet = DomainMath.BookKindButton(headings, DomainKind.Tacet);
                Rectangle cultivation = DomainMath.BookKindButton(headings, DomainKind.Cultivation);
                Require(!forgery.IsEmpty && !simulation.IsEmpty && !tacet.IsEmpty && !cultivation.IsEmpty &&
                    forgery.Top < simulation.Top && simulation.Top < tacet.Top, "识别图鉴中四个实际分类位置");
                Require(DomainMath.IsHighlightedBookKind(frame, cultivation) &&
                    !DomainMath.IsHighlightedBookKind(frame, forgery) && !DomainMath.IsHighlightedBookKind(frame, simulation) &&
                    !DomainMath.IsHighlightedBookKind(frame, tacet), "只有已选的培养目标分类显示高亮");
            }
        }

        private static void RequireStamina(OcrReader ocr, PixelFrame frame, int current, int backup)
        {
            DomainRunner.StaminaReadout readout;
            Require(DomainRunner.TryReadStaminaHeader(ocr.Read(frame, Area(frame, .45, 0, .94, .112)),
                frame.Width, out readout) && readout.Current == current && readout.Limit == 240 &&
                readout.BackupKnown && readout.Backup == backup, "确认原版当前体力、上限与左侧备用体力");
        }

        private static Rectangle Area(PixelFrame frame, double left, double top, double right, double bottom)
        {
            return new Rectangle((int)(frame.Width * left), (int)(frame.Height * top),
                (int)(frame.Width * (right - left)), (int)(frame.Height * (bottom - top)));
        }
    }
}
