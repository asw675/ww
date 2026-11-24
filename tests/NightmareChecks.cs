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
                "只选择尚未开始的聚落");
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
                    Require(NightmareNestRunner.FindNestEntry(vision, reader.Read(image), out feature, out box) &&
                        feature == "gray_teleport", "传送页应继续识别为传送路线");
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
                new HashSet<string>(), out missing) == null, "部分进度不作为新候选");
            NestTarget progress = NestMath.ReadTargetProgress(lines, target, 1600, 900, 1);
            Require(progress != null && progress.Defeated == 5,
                "其他聚落的满进度不能证明当前聚落已完成");
            lines[2].Text = "已击败残象：41/41";
            Require(NestMath.ReadTargetProgress(lines, target, 1600, 900, 0).Defeated == 41,
                "应能验证同一聚落的满进度");
            Require(NestMath.FindTarget(lines, NestTab.TacetScroll, 1600, 900,
                new HashSet<string>(), out missing) == null && !missing, "两个聚落已满进度时不再入场");
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
