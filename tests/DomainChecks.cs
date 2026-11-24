using System;
using System.Drawing;
using System.IO;

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

        internal static void Options()
        {
            DomainOptions options = DomainOptions.Defaults();
            options.Validate();
            Require(options.Kind == DomainKind.Tacet && options.Index == 1, "默认体力本配置错误");
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
