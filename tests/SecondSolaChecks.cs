using System;
using System.Drawing;

namespace TideCombat
{
    internal static class SecondSolaChecks
    {
        internal static void Decisions()
        {
            {
                PixelFrame frame = Pixels(1600, 900, (x, y) =>
                {
                    if (x >= 705 && x < 711 && y >= 428 && y < 447) return Color.White;
                    double dx = (x + .5 - 708) / 16, dy = (y + .5 - 438.5) / 16.5;
                    return dx * dx + dy * dy <= 1 ? Color.Red : Color.Black;
                });
                var labels = new[]
                {
                    Text("０９／０２ 周二", 68, 48),
                    Text("８２％", 1000, 60),
                    Text("教学楼", 633, 468),
                    Text("24", 1030, 380),
                    Text("25", 1030, 470)
                };
                var screen = new SecondSolaScreen(frame, labels);
                Require(screen.Date == 902 && screen.Fatigue() == 82, "normalize dates and fatigue as displayed");
                var marker = screen.MapMarker();
                Require(marker.Item1 != null && marker.Item1.Text == "教学楼", "prefer a recognized map event marker's nearby building");
                OcrLine answer = SecondSolaRunner.ExamAnswer(screen.In(.60, .3, .8, .7), screen.Date);
                Require(answer != null && answer.Text == "25", "select the answer for the current exam date");
                Require(SecondSolaRunner.ExamAnswer(screen.Lines, 101) == null, "unrelated dates do not use an exam answer");
                OcrLine focused = screen.ResumeJourney(() => new[] { Text("继续 旅程", 980, 630) });
                Require(focused != null && focused.Text == "继续旅程",
                    "use focused OCR for the continuation button when full-screen OCR misses it");
                var alreadyFound = new SecondSolaScreen(screen.Frame, new[] { Text("继续旅程", 1170, 640) });
                Require(alreadyFound.ResumeJourney(() => { throw new InvalidOperationException(); }) != null,
                    "use an already recognized continuation button without running OCR again");

                const string rewardTitle = "常驻奖励";
                OcrLine title = Text(rewardTitle, 40, 50);
                OcrLine status = Text("已 领 取", 1340, 192);
                var rewards = new SecondSolaScreen(screen.Frame, new[] { title });
                Require(SecondSolaRunner.HasRewardStatus(rewards, rewardTitle, area =>
                {
                    Require(area == Rectangle.FromLTRB(1280, 158, 1520, 261), "crop the first reward status row");
                    return new[] { status };
                }), "confirm reward status with focused OCR when full-screen OCR misses it");
                Require(!SecondSolaRunner.HasRewardStatus(rewards, rewardTitle, area =>
                    new[] { Text("领取奖励", 1340, 192), Text("进行中", 1340, 320) }),
                    "require a status label in the expected reward row");
                Require(!SecondSolaRunner.HasRewardStatus(rewards, "限时奖励", area => { throw new InvalidOperationException(); }),
                    "require the requested reward page before reading its status");
                var recognizedRewards = new SecondSolaScreen(screen.Frame, new[] { title, status });
                Require(SecondSolaRunner.HasRewardStatus(recognizedRewards, rewardTitle, area => { throw new InvalidOperationException(); }),
                    "use an already recognized reward status without running OCR again");
            }
            ReferenceParity();
        }

        internal static void ReferenceParity()
        {
            PixelFrame black = Pixels(1000, 900, (x, y) => Color.Black);
            var empty = new SecondSolaScreen(black, new OcrLine[0]);
            var withKey = new SecondSolaScreen(black, new[] { SmallText("F", 665, 300, 15, 20) });
            Require(SecondSolaRunner.HasDialogueChoiceKey(withKey, true, (l, t, r, b) => { throw new InvalidOperationException(); }),
                "full-screen F takes priority over focused Tab recognition");
            Require(!SecondSolaRunner.HasDialogueChoiceKey(empty, false, (l, t, r, b) => { throw new InvalidOperationException(); }),
                "do not accept Tab as a dialogue choice without a confirmed bottom panel");
            Require(SecondSolaRunner.HasDialogueChoiceKey(empty, true, (l, t, r, b) =>
            {
                Require(l == .78 && t == .05 && r == .83 && b == .11, "Tab OCR uses the reference key region");
                return new[] { SmallText(" Tab ", 800, 50, 20, 20, .55f) };
            }), "focused Tab can recover a missing full-screen label at the reference confidence");
            var broadTab = new SecondSolaScreen(black, new[] { SmallText("Tab", 800, 50, 20, 20) });
            Require(!SecondSolaRunner.HasDialogueChoiceKey(broadTab, true, (l, t, r, b) => new[] { Text("T ab", 800, 50) }),
                "a broad match does not substitute for an exact focused Tab label");
            Require(!SecondSolaRunner.HasDialogueChoiceKey(empty, true, (l, t, r, b) => new[] { SmallText("Tab", 800, 50, 20, 20, .49f) }),
                "recognition below the reference OCR confidence is rejected");

            Require(SecondSolaRunner.FocusedRewardPage("限时奖励", (l, t, r, b) =>
            {
                Require(l == .035 && t == .035 && r == .18 && b == .09, "reward title OCR uses the reference title region");
                return new[] { SmallText("限时奖励", 50, 40, 60, 15, .55f) };
            }), "focus reward title when waiting for the page");
            Require(!SecondSolaRunner.FocusedRewardPage("限时奖励", (l, t, r, b) => new[] { Text("常驻奖励", 50, 40) }),
                "do not confuse the two reward pages");

            OcrLine finish = SmallText("完成", 92, 82, 16, 8);
            var yellow = new SecondSolaScreen(Pixels(200, 100, (x, y) => Color.FromArgb(255, 255, 40)), new[] { finish });
            var paper = new SecondSolaScreen(Pixels(200, 100, (x, y) => Color.White), new[] { finish });
            Require(SecondSolaRunner.AttributeResult(yellow) == null && SecondSolaRunner.AttributeResult(paper) != null,
                "attribute result requires bright individual channels, not merely a bright yellow average");

            PixelFrame dialogue = Pixels(100, 100, (x, y) => x == 76 && y == 95 ? Color.Black : Color.White);
            Require(SecondSolaRunner.DialoguePanel(new SecondSolaScreen(dialogue, new[] { SmallText("对话", 25, 86, 15, 6) })),
                "dialogue panel with correctly positioned subtitle and contrasting pixels");
            Require(!SecondSolaRunner.DialoguePanel(new SecondSolaScreen(dialogue, new[] { SmallText("对话", 24, 86, 20, 6) })),
                "dialogue text left-edge boundary is strict, even when text center lies inside");
            Require(!SecondSolaRunner.DialoguePanel(new SecondSolaScreen(dialogue, new[] { SmallText("对话", 25, 82, 20, 6) })),
                "dialogue subtitle center at the lower bound is not inside the region");

            var stats = new SecondSolaScreen(black, new[]
            {
                SmallText("知识", 40, 600, 40, 20), SmallText("999", 105, 600, 80, 20),
                SmallText("1,234", 130, 600, 40, 20), SmallText("1 2", 130, 600, 40, 20),
                SmallText("555", 120, 600, 40, 20), SmallText("444", 140, 618, 40, 20),
                SmallText("３２１", 170, 600, 80, 20)
            });
            Require(SecondSolaRunner.Stat(stats, "知识") == 321,
                "stat value uses strict left-edge and row limits plus original decimal text, even when its center is farther right");
            Require(SecondSolaRunner.Stat(new SecondSolaScreen(black, new[]
            {
                SmallText("知识", 40, 600, 40, 20), SmallText("1,234", 130, 600, 40, 20)
            }), "知识") == null, "map stats do not accept comma-grouped counters");

            OcrLine narration = SmallText("独白", 30, 40, 20, 10);
            PixelFrame scattered = Pixels(90, 90, (x, y) => x % 3 == 0 && y % 3 == 0 ? Color.Black : Color.White);
            Require(!SecondSolaRunner.BlackNarration(new SecondSolaScreen(scattered, new[] { narration })),
                "black narration uses all pixels rather than sampling only the dark lattice");
            Require(SecondSolaRunner.BlackNarration(new SecondSolaScreen(Pixels(90, 90, (x, y) => Color.Black), new[] { narration })),
                "black narration still detects a real dark page with central Chinese text");
            Require(!SecondSolaRunner.BlackNarration(new SecondSolaScreen(Pixels(90, 90, (x, y) => Color.FromArgb(25, 25, 25)), new[] { narration })),
                "black narration threshold of 25 is strict");

            Require(SecondSolaRunner.HasLotteryDigits(new[] { SmallText(" 19 ", 1, 1, 10, 10, .55f) }), "positive one or two digit ticket balance");
            Require(!SecondSolaRunner.HasLotteryDigits(new[] { Text("0", 1, 1), Text("100", 1, 1), Text("1 2", 1, 1) }),
                "reject zero, three digits or non-contiguous ticket OCR");
            foreach (int height in new[] { 1080, 1440, 2159 })
            {
                PixelFrame original = Pixels(6, height, (x, y) => Color.FromArgb(x * 30, 0, 0));
                PixelFrame crop = SecondSolaScreen.TargetOcrCrop(original, 0, 0, 1, 6.0 / height, 1440);
                Require(crop.Width == 6 && crop.Height == 6, "lottery target does not upscale or downscale below the 2160 threshold");
            }
            PixelFrame full = Pixels(6, 2160, (x, y) => Color.FromArgb(x * 30, Math.Min(255, y * 30), 0));
            var retained = new byte[6 * 2160 * 4];
            full.CopyBgra(new Rectangle(0, 0, 6, 2160), retained);
            var reduced = new PixelFrame(new byte[24], 3, 2, sourceWidth: 6, sourceHeight: 2160, echoPixels: retained);
            PixelFrame down = SecondSolaScreen.TargetOcrCrop(reduced, 0, 0, 1, 6.0 / 2160, 1440);
            int red, green, blue;
            down.Rgb(0, 0, out red, out green, out blue);
            Require(down.Width == 4 && down.Height == 4 && red == 10 && green == 10 && blue == 0,
                "crop native pixels first; area downsample at 2160p has the expected weighted pixel");
            PixelFrame balance = SecondSolaScreen.TargetOcrCrop(Pixels(100, 2160, (x, y) => Color.Red), .827, .038, .896, .103, 1440);
            Require(balance.Width == 5 && balance.Height == 93, "lottery ROI dimensions scale relative to original screen height");

            PixelFrame badges = Pixels(320, 180, (x, y) =>
            {
                if (x >= 140 && x < 146 && y >= 80 && y < 86)
                    return x >= 142 && x < 144 && y >= 81 && y < 84 ? Color.White : Color.Red;
                if (x >= 302 && x < 306 && y >= 39 && y < 43) return Color.Red;
                return Color.Black;
            });
            byte[] badgePixels = new byte[320 * 180 * 4];
            badges.CopyBgra(new Rectangle(0, 0, 320, 180), badgePixels);
            var blankDisplay = new PixelFrame(new byte[160 * 90 * 4], 160, 90, sourceWidth: 320, sourceHeight: 180, echoPixels: badgePixels);
            var marker = new SecondSolaScreen(blankDisplay, new[] { SmallText("教学楼", 60, 44, 10, 4) });
            Require(marker.MapMarker().Item1 != null && marker.GoalBadge() == new Point(151, 22),
                "map markers and stage badges use retained source colors and project coordinates back for clicks");
            Require(marker.Fraction(.93, .17, .97, .29, (h, s, v) => h == 0 && s == 255 && v == 255) > 0,
                "HSV fractions use retained native pixels even when normalized display is missing them");
            Require(SecondSolaRunner.ClickHoldMilliseconds == 20 && SecondSolaRunner.SpaceHoldMilliseconds == 20 &&
                SecondSolaRunner.RewardCloseDelayMilliseconds == 700, "local ordinary input and reward page timings match the reference");
        }

        private static PixelFrame Pixels(int width, int height, Func<int, int, Color> color)
        {
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Color c = color(x, y);
                    int i = (y * width + x) * 4;
                    pixels[i] = c.B; pixels[i + 1] = c.G; pixels[i + 2] = c.R; pixels[i + 3] = 255;
                }
            return new PixelFrame(pixels, width, height);
        }

        private static OcrLine SmallText(string value, int x, int y, int width, int height, float score = .95f)
        {
            return new OcrLine { Text = value, Box = new Rectangle(x, y, width, height), Score = score };
        }

        private static OcrLine Text(string value, int x, int y)
        {
            return new OcrLine { Text = value, Box = new Rectangle(x, y, 100, 20), Score = .95f };
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
