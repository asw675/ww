using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace TideCombat
{
    internal static class RahaiChecks
    {
        private static void Require(bool condition, string explanation)
        {
            if (!condition) throw new InvalidOperationException(explanation);
        }

        internal static void Solver()
        {
            int lines;
            ulong crossing = 255UL << 16 | 0x0101010101010101UL << 3 | 1;
            Require(RahaiSolver.ClearLines(crossing, 0, out lines) == 1 && lines == 2,
                "detect and clear intersecting lines from the same board");
            const ulong corners = 0x8100000000000081UL;
            ulong result = RahaiSolver.ClearLines(255UL | 0x0101010101010101UL | corners | (1UL << 9),
                corners, out lines);
            Require(lines == 2 && result == (corners | (1UL << 9)), "clear both lines while retaining fixed blocks");

            var distinct = new RahaiPiece { Slot = 0, Cells = new[] { new Point(0, 0), new Point(0, 1), new Point(1, 1) } };
            var moves = RahaiSolver.Placements(distinct).ToList();
            Require(moves.Select(move => move.Turns).Distinct().Count() == 4 &&
                moves.All(move => move.Row + move.Height <= 8 && move.Column + move.Width <= 8 &&
                    Enumerable.Range(0, 64).Count(bit => (move.Mask & (1UL << bit)) != 0) == 3),
                "enumerate the rotations without mirroring the piece");
            var square = new RahaiPiece { Slot = 0, Cells = new[] { Point.Empty, new Point(0, 1), new Point(1, 0), new Point(1, 1) } };
            var single = new RahaiPiece { Slot = 1, Cells = new[] { Point.Empty } };
            var board = new RahaiBoard { Mask = ((255UL << 8) ^ (1UL << 8)) | (255UL ^ 3),
                Pieces = new List<RahaiPiece> { square, single } };
            RahaiMove choice = RahaiSolver.Choose(board);
            Require(choice != null && choice.Slot == 1 && (choice.Mask & board.Mask) == 0, "look ahead across both pieces");
            RahaiSolver.ClearLines(board.Mask | choice.Mask, 0, out lines);
            Require(lines == 1, "the chosen first move immediately clears one line");

            int completed;
            Require(RahaiRunner.TryIncompleteDifficulty("进行中 ０１／０２", out completed) && completed == 1,
                "normalize recognized difficulty counts");
            Require(!RahaiRunner.TryIncompleteDifficulty("进行中 2/2", out completed), "skip completed stages");
            Require(!RahaiRunner.TryIncompleteDifficulty("进 行 中 0/2", out completed), "match the exact progress prefix");
            var nativeTitle = new OcrLine { Text = "关卡", RawText = " 原 关卡 ", Score = .52f,
                NativeQuad = new[] { new PointF(200, 200), new PointF(300, 190), new PointF(310, 225), new PointF(205, 235) } };
            var nativeStatus = new OcrLine { Text = "进行中 0/2", Score = .55f, Box = new Rectangle(205, 398, 110, 25) };
            var recognized = RahaiRunner.ReferenceLines(new[] { nativeStatus, nativeTitle,
                new OcrLine { Text = "关卡目标说明", Box = new Rectangle(95, 50, 100, 25), Score = .8f },
                new OcrLine { Text = "开始挑战", Box = new Rectangle(95, 50, 100, 25), Score = .49f },
                new OcrLine { Text = "关卡目标", Box = new Rectangle(70, 65, 100, 25), Score = .51f },
                new OcrLine { Text = "未知", Score = .5f, NativeQuad = new[] { new PointF(40, 40), new PointF(30, 40), new PointF(20, 50) } },
                new OcrLine { Text = "关卡", RawText = "關卡", Box = new Rectangle(500, 50, 100, 25), Score = .9f } });
            Require(recognized.Count == 5 && recognized[0].Text == "关卡目标" &&
                recognized.Any(line => line.Text == "關卡") && recognized.Any(line => line.Text == "原 关卡" &&
                    line.Box == new Rectangle(200, 200, 110, 25)), "native box, stable row sorting, engine score floor and original zh_CN text");
            Require(RahaiRunner.FindReferenceLine(recognized, "关卡目标", true).X == 70 &&
                RahaiRunner.FindReferenceLine(recognized, "目标说明").X == 95 &&
                RahaiRunner.FindReferenceLine(recognized, "开始挑战", true).IsEmpty, "exact string and regex matching preserve OCR gating");
            var nativeStages = RahaiRunner.StagesFromLines(recognized, 1600, 1024);
            Require(nativeStages.Count == 1 && nativeStages[0].Name == "原 关卡" && nativeStages[0].Box == nativeStatus.Box,
                "native stage position and original name form the attempt identity");
            var before = new[] { new RahaiRunner.Stage { Name = "第一关", Box = new Rectangle(400, 365, 100, 25) } };
            var after = new[] { new RahaiRunner.Stage { Name = "第1关", Box = new Rectangle(404, 365, 100, 25) } };
            Require(!RahaiRunner.StageListMoved(before, after, 1600), "do not mistake OCR drift for card movement");
            after[0].Box.Offset(10, 0);
            Require(RahaiRunner.StageListMoved(before, after, 1600), "recognize an actual card shift");
            Require(RahaiRunner.ScrollPath(new Size(1280, 720), true).SequenceEqual(
                new[] { 1037, 937, 837, 736, 636, 536, 435 }.Select(x => new Point(x, 353))) &&
                RahaiRunner.ScrollPath(new Size(1280, 720), false).SequenceEqual(
                new[] { 435, 535, 635, 736, 836, 936, 1037 }.Select(x => new Point(x, 353))),
                "reference swipe emits six truncated native intermediate points and the exact final endpoint");

            // Synthetic pixels on the .625x upsample and 1.25x downsample grids
            // distinguish area accumulation from nearest and HUD interpolation.
            foreach (int nativeWidth in new[] { 1280, 2560 })
            {
                int nativeHeight = nativeWidth * 9 / 16;
                var source = new byte[nativeWidth * nativeHeight * 4];
                int start = nativeWidth == 1280 ? 0 : 1;
                source[((start * nativeWidth + start) * 4) + 2] = 100;
                source[((start * nativeWidth + start + 1) * 4) + 1] = 200;
                source[(((start + 1) * nativeWidth + start) * 4)] = 250;
                source[(((start + 1) * nativeWidth + start + 1) * 4) + 2] = 200;
                var retained = new PixelFrame(new byte[1600 * 900 * 4], 1600, 900, sourceWidth: nativeWidth,
                    sourceHeight: nativeHeight, echoPixels: source);
                var canonical = new RahaiVision.CanonicalView(retained);
                int r, g, b;
                canonical.Rgb(1, 1, out r, out g, out b);
                Require(r == 68 && g == 48 && b == 60, "native fractional area resize (0.6/0.4 weights) at " + nativeWidth);
            }

            foreach (int width in new[] { 1280, 1600, 2048, 2560, 4096 })
            {
                RahaiBoard scaledBoard = RahaiVision.Read(SyntheticBoard(width));
                Require(scaledBoard != null && scaledBoard.Mask == (corners | (1UL << 9)) && scaledBoard.Fixed == corners &&
                    scaledBoard.Pieces.Select(piece => piece.Cells.Length).SequenceEqual(new[] { 1, 2 }),
                    "native area resize preserves board, edge gray pieces and tray shapes at " + width +
                    ": " + (scaledBoard == null ? "unrecognized" : scaledBoard.Mask.ToString("X16") + "/" + scaledBoard.Fixed.ToString("X16") +
                    " pieces=" + string.Join(",", scaledBoard.Pieces.Select(piece => piece.Cells.Length))));
            }
        }

        private static PixelFrame SyntheticBoard(int width)
        {
            int height = width * 9 / 16;
            var pixels = new byte[width * height * 4];
            Action<double, double, double, double, Color> paint = (left, top, right, bottom, color) =>
            {
                for (int y = (int)Math.Round(top * height / RahaiVision.Height); y < (int)Math.Round(bottom * height / RahaiVision.Height); y++)
                    for (int x = (int)Math.Round(left * width / RahaiVision.Width); x < (int)Math.Round(right * width / RahaiVision.Width); x++)
                    {
                        int offset = (y * width + x) * 4;
                        pixels[offset] = color.B; pixels[offset + 1] = color.G; pixels[offset + 2] = color.R; pixels[offset + 3] = 255;
                    }
            };
            paint(50, 44, 110, 102, Color.White);
            paint(990, 74, 1060, 84, Color.White);
            paint(1680, 600, 1740, 630, Color.White);
            paint(1767, 476.5, 1804, 513.5, Color.Orange);
            paint(1748, 716.5, 1823, 753.5, Color.Orange);
            foreach (int index in new[] { 0, 7, 9, 56, 63 })
            {
                RahaiPoint center = RahaiVision.CellCenter(index % 8 + .5, index / 8 + .5);
                paint(center.X - 42, center.Y - 42, center.X + 42, center.Y + 42,
                    index == 9 ? Color.Red : Color.FromArgb(120, 120, 120));
            }
            return new PixelFrame(new byte[1600 * 900 * 4], 1600, 900, sourceWidth: width, sourceHeight: height, echoPixels: pixels);
        }

        internal static void Replay(string images)
        {
            using (var reader = new FrameReader())
            {
                Func<string, PixelFrame> load = name =>
                {
                    using (var image = new Bitmap(Path.Combine(images, "rahai_" + name + ".jpg")))
                        return reader.Read(image, retainEcho: true);
                };
                PixelFrame score = load("score");
                RahaiBoard board = RahaiVision.Read(score);
                Require(board != null && board.Mask == 27026459673497112UL &&
                    board.Pieces.Select(piece => piece.Cells.Length).SequenceEqual(new[] { 4, 5 }),
                    "read two pieces and the occupied cells in the original score capture");
                RahaiMove move = RahaiSolver.Choose(board);
                Require(move != null && move.Slot == 0 && move.Row == 2 && move.Column == 5,
                    "select the same starting move as okww");
                Require(!RahaiVision.MatchesPreview(score, board, move) &&
                    RahaiVision.MatchesPreview(load("preview"), board, move), "require the observed preview");
                Require(RahaiVision.Read(load("reward")) == null &&
                    RahaiVision.Read(load("reward_4500")) == null, "reward overlays are not playable");

                if (File.Exists(Path.Combine(images, "rahai_four_trays_outer.jpg")))
                {
                    board = RahaiVision.Read(load("four_trays_outer"));
                    Require(board != null && board.Mask == 0xE0C0801F1F1F1F1FUL && board.Fixed == 0xE0C080000103070FUL &&
                        board.Pieces.Select(piece => piece.Slot).SequenceEqual(new[] { 0, 3 }) &&
                        board.Pieces.Select(piece => piece.Cells.Length).SequenceEqual(new[] { 6, 4 }),
                        "four trays retain both occupied outside slots");
                }
                board = RahaiVision.Read(load("score_4500"));
                Require(board != null && board.Mask == 0x991800C3C3001899UL && board.Fixed == 0x8100000000000081UL &&
                    board.Pieces.Select(piece => piece.Cells.Length).SequenceEqual(new[] { 5, 4, 5 }),
                    "read all three trays and four fixed cells");
                move = RahaiSolver.Choose(board);
                Require(move != null && (move.Mask & board.Mask) == 0, "three-tray move must not overlap existing tiles");
                var excluded = new HashSet<Tuple<int, ulong>> { Tuple.Create(move.Slot, move.Mask) };
                RahaiMove alternative = RahaiSolver.Choose(board, excluded);
                Require(alternative != null && !excluded.Contains(Tuple.Create(alternative.Slot, alternative.Mask)),
                    "exclude a rejected preview when choosing a move");
                board = RahaiVision.Read(load("medal_start"));
                Require(board != null && board.Mask == 0x0800E7AC2DEE8C12UL && board.Fixed == 0x0800008001000010UL &&
                    board.Pieces.Select(piece => piece.Cells.Length).SequenceEqual(new[] { 4, 4, 5 }) &&
                    board.Pieces[2].Cells.SequenceEqual(Enumerable.Range(0, 5).Select(x => new Point(x,0))),
                    "resolve an ambiguous tray layout on the medal board");
                board = RahaiVision.Read(load("score_4500_partial"));
                Require(board != null && board.Mask == 0x9B1A3E0E3E001A9BUL &&
                    board.Pieces.Select(piece => piece.Slot).SequenceEqual(new[] { 0, 1 }), "preserve the indices of occupied trays");
                board = RahaiVision.Read(load("score_4500_ambiguous_trays"));
                Require(board != null && board.Pieces.Select(piece => piece.Cells.Length).SequenceEqual(new[] { 9, 4, 5 }),
                    "a bright piece does not count as a divider in a different tray layout");
                var unrelated = new PixelFrame(new byte[1600 * 900 * 4], 1600, 900);
                var unsupported = new PixelFrame(new byte[1800 * 900 * 4], 1800, 900);
                Require(RahaiVision.Read(unrelated) == null && !RahaiVision.Supported(unsupported) &&
                    RahaiVision.Read(unsupported) == null, "reject unrelated or unsupported frames");
            }
        }

        // Optional native-model replay on the separately versioned 3.8.14
        // Rahai fixtures; it does not require a live Windows game or input.
        internal static void OcrReplay(string images)
        {
            using (var reader = new FrameReader())
            using (var ocr = new OcrReader(OcrReader.Directory))
            {
                using (var score = new Bitmap(Path.Combine(images, "rahai_score.jpg")))
                {
                    PixelFrame frame = reader.Read(score, true);
                    var title = RahaiRunner.ReadReferenceRoi(ocr, frame, .045, .04, .20, .09);
                    Require(!RahaiRunner.FindReferenceLine(title, "拉海洛方块").IsEmpty,
                        "the captured board title in its exact native ROI: " + string.Join(",", title.Select(line => line.Text)));
                    var points = RahaiRunner.ReadReferenceRoi(ocr, frame, .09, .125, .235, .181);
                    Require(!RahaiRunner.FindReferenceLine(points, @"2900\s*[/／]\s*3500").IsEmpty,
                        "the captured challenge score in its exact native ROI: " + string.Join(",", points.Select(line => line.Text)));
                    Require(RahaiRunner.FindReferenceLine(RahaiRunner.ReadReferenceRoi(ocr, frame, .36, .216, .64, .29),
                        @"^获\s*得$").IsEmpty, "do not identify a reward on the playable board");
                }
                foreach (string name in new[] { "rahai_reward.jpg", "rahai_reward_4500.jpg" })
                    using (var reward = new Bitmap(Path.Combine(images, name)))
                    {
                        PixelFrame frame = reader.Read(reward, true);
                        var lines = RahaiRunner.ReadReferenceRoi(ocr, frame, .36, .216, .64, .29);
                        Require(!RahaiRunner.FindReferenceLine(lines, @"^获\s*得$").IsEmpty,
                            "the captured reward in its exact native ROI (" + name + "): " + string.Join(",", lines.Select(line => line.Text)));
                    }
            }
        }
    }
}
