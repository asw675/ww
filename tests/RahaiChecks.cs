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
            var before = new[] { new RahaiRunner.Stage { Name = "第一关", Box = new Rectangle(400, 365, 100, 25) } };
            var after = new[] { new RahaiRunner.Stage { Name = "第1关", Box = new Rectangle(404, 365, 100, 25) } };
            Require(!RahaiRunner.StageListMoved(before, after, 1600), "do not mistake OCR drift for card movement");
            after[0].Box.Offset(10, 0);
            Require(RahaiRunner.StageListMoved(before, after, 1600), "recognize an actual card shift");
        }

        internal static void Replay(string images)
        {
            using (var reader = new FrameReader())
            {
                Func<string, PixelFrame> load = name =>
                {
                    using (var image = new Bitmap(Path.Combine(images, "rahai_" + name + ".jpg")))
                        return reader.Read(image);
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
    }
}
