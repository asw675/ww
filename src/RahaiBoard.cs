using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace TideCombat
{
    internal struct RahaiPoint
    {
        internal readonly double X, Y;
        internal RahaiPoint(double x, double y) { X = x; Y = y; }
    }

    internal sealed class RahaiPiece
    {
        internal int Slot;
        internal Point[] Cells;
        internal RahaiPoint Pickup;
    }

    internal sealed class RahaiMove
    {
        internal int Slot, Turns, Row, Column, Width, Height;
        internal ulong Mask;
        internal RahaiPoint Drop
        {
            get { return RahaiVision.CellCenter(Column + Width / 2.0, Row + Height / 2.0); }
        }
    }

    internal sealed class RahaiBoard
    {
        internal ulong Mask, Fixed;
        internal List<RahaiPiece> Pieces;
    }

    internal static class RahaiVision
    {
        private const double BoardX = 537.5, BoardY = 89.4, Cell = 121.65, TrayX = 1785.5, Tile = 38.3;
        internal const int Width = 2048, Height = 1152;
        private static readonly int[][] Trays = { new[] { 495, 735 }, new[] { 375, 615, 855 }, new[] { 255, 495, 735, 975 } };

        internal static bool Supported(PixelFrame frame)
        {
            return frame.SourceHeight > 0 && Math.Abs(frame.SourceWidth / (double)frame.SourceHeight - 16.0 / 9) <= .012;
        }

        internal static Point At(PixelFrame frame, RahaiPoint canonical)
        {
            return new Point((int)Math.Round(canonical.X * frame.Width / Width),
                (int)Math.Round(canonical.Y * frame.Height / Height));
        }

        internal static RahaiPoint CellCenter(double column, double row)
        {
            return new RahaiPoint(BoardX + column * Cell, BoardY + row * Cell);
        }

        internal static RahaiBoard Read(PixelFrame frame)
        {
            if (!Supported(frame))
                return null;
            var image = new CanonicalView(frame);
            if (!HasBoardHeader(image)) return null;
            List<RahaiPiece> pieces = null;
            var layouts = Trays.Where(layout => HasTrays(image, layout)).ToList();
            foreach (int[] layout in layouts)
            {
                if (layouts.Any(other => other.Length > layout.Length && layout.All(y => other.Contains(y))))
                    continue;
                List<RahaiPiece> observed = ReadTrayLayout(image, layout);
                if (observed == null)
                    continue;
                if (pieces != null)
                    return null;
                pieces = observed;
            }
            if (pieces == null)
                return null;
            ulong occupied, fixedBlocks;
            ReadCells(image, out occupied, out fixedBlocks);
            return new RahaiBoard { Mask = occupied, Fixed = fixedBlocks, Pieces = pieces };
        }

        internal static bool MatchesPreview(PixelFrame frame, RahaiBoard before, RahaiMove move)
        {
            if (!Supported(frame))
                return false;
            ulong occupied, fixedBlocks;
            ReadCells(new CanonicalView(frame), out occupied, out fixedBlocks);
            return occupied == (before.Mask | move.Mask) && fixedBlocks == before.Fixed;
        }

        private static bool HasBoardHeader(CanonicalView frame)
        {
            int pale = 0;
            for (int y = 44; y < 102; y++)
                for (int x = 50; x < 110; x++)
                {
                    int red, green, blue;
                    ReadColor(frame, x, y, out red, out green, out blue);
                    if (Math.Min(red, Math.Min(green, blue)) > 181)
                        pale++;
                }
            if (pale < 210)
                return false;
            for (int y = 66; y < 92; y++)
                if (MedianValue(frame, 1024, y, 3) - MedianValue(frame, 1024, y + 8, 2) > 45)
                    return true;
            return false;
        }

        private static bool HasTrays(CanonicalView frame, int[] layout)
        {
            for (int slot = 0; slot < layout.Length; slot++)
            {
                int center = layout[slot];
                if (MedianValue(frame, 1710, center - 82, 3) >= 65 || MedianValue(frame, 1710, center + 82, 3) >= 65)
                    return false;
                if (slot > 0 && MedianValue(frame, 1710, (center + layout[slot - 1]) / 2, 3) <= 105)
                    return false;
            }
            return true;
        }

        private static List<RahaiPiece> ReadTrayLayout(CanonicalView frame, int[] layout)
        {
            var pieces = new List<RahaiPiece>();
            for (int slot = 0; slot < layout.Length; slot++)
            {
                RahaiPiece piece;
                if (!TryReadTray(frame, slot, layout[slot], out piece))
                    return null;
                if (piece != null)
                    pieces.Add(piece);
            }
            return pieces;
        }

        private static bool TryReadTray(CanonicalView frame, int slot, int trayY, out RahaiPiece piece)
        {
            piece = null;
            int left = (int)Math.Round(TrayX) - 88, top = trayY - 88;
            int minX = left + 176, minY = top + 176, maxX = 0, maxY = 0, bright = 0;
            for (int y = top; y < top + 176; y++)
                for (int x = left; x < left + 176; x++)
                {
                    if (Value(frame, x, y) <= 105)
                        continue;
                    bright++;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            if (bright < 20)
                return true;
            if (bright < 800)
                return false;
            int width = (int)Math.Round((maxX - minX + 1) / Tile);
            int height = (int)Math.Round((maxY - minY + 1) / Tile);
            double cx = (minX + maxX) / 2.0, cy = (minY + maxY) / 2.0;
            if (width < 1 || width > 5 || height < 1 || height > 5 || Math.Abs(cx - TrayX) > 5 || Math.Abs(cy - trayY) > 5)
                return false;

            var cells = new List<Point>();
            RahaiPoint pickup = default(RahaiPoint);
            double nearest = double.PositiveInfinity;
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    double dx = (x - (width - 1) / 2.0) * Tile, dy = (y - (height - 1) / 2.0) * Tile;
                    if (MedianValue(frame, cx + dx, cy + dy, 7) <= 83)
                        continue;
                    cells.Add(new Point(x, y));
                    double px = cx + dx, py = cy + dy;
                    double distance = (px - cx) * (px - cx) + (py - cy) * (py - cy);
                    if (distance < nearest)
                    {
                        nearest = distance;
                        pickup = new RahaiPoint(px, py);
                    }
                }
            if (cells.Count == 0 || cells.Max(point => point.X) != width - 1 || cells.Max(point => point.Y) != height - 1 ||
                bright < 900 * cells.Count || bright > 1580 * cells.Count || !Connected(cells))
                return false;
            piece = new RahaiPiece { Slot = slot, Cells = cells.ToArray(), Pickup = pickup };
            return true;
        }

        private static bool Connected(List<Point> cells)
        {
            var reached = new HashSet<Point> { cells[0] };
            int previous;
            do
            {
                previous = reached.Count;
                foreach (Point cell in cells)
                    if (!reached.Contains(cell) &&
                        reached.Any(other => Math.Abs(other.X - cell.X) + Math.Abs(other.Y - cell.Y) == 1))
                        reached.Add(cell);
            }
            while (previous != reached.Count);
            return reached.Count == cells.Count;
        }

        private static void ReadCells(CanonicalView frame, out ulong occupied, out ulong fixedBlocks)
        {
            occupied = fixedBlocks = 0;
            for (int row = 0; row < 8; row++)
                for (int column = 0; column < 8; column++)
                {
                    RahaiPoint center = CellCenter(column + .5, row + .5);
                    int votes = 0, fixedVotes = 0;
                    for (int dy = -25; dy <= 25; dy += 50)
                        for (int dx = -24; dx <= 24; dx += 48)
                        {
                            int red, green, blue;
                            ReadColor(frame, (int)Math.Round(center.X + dx), (int)Math.Round(center.Y + dy),
                                out red, out green, out blue);
                            int bright = Math.Max(red, Math.Max(green, blue));
                            int chroma = bright - Math.Min(red, Math.Min(green, blue));
                            bool gray = bright >= 80 && bright <= 155 && chroma <= 15;
                            if (gray) fixedVotes++;
                            if (gray || bright > 88 && chroma > 40) votes++;
                        }
                    ulong bit = 1UL << (row * 8 + column);
                    if (votes >= 3) occupied |= bit;
                    if (fixedVotes >= 3) fixedBlocks |= bit;
                }
        }

        private static int MedianValue(CanonicalView frame, double x, double y, int radius)
        {
            int centerX = (int)Math.Round(x), centerY = (int)Math.Round(y);
            var values = new int[(radius * 2 + 1) * (radius * 2 + 1)];
            int count = 0;
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                    values[count++] = Value(frame, centerX + dx, centerY + dy);
            Array.Sort(values);
            return values[count / 2];
        }

        private static int Value(CanonicalView frame, int x, int y)
        {
            int red, green, blue;
            ReadColor(frame, x, y, out red, out green, out blue);
            return Math.Max(red, Math.Max(green, blue));
        }

        private static void ReadColor(CanonicalView frame, int x, int y, out int red, out int green, out int blue)
        {
            frame.Rgb(x, y, out red, out green, out blue);
        }

        // Equivalent area-weighted samples of the native image resized directly
        // to 2048x1152 (INTER_AREA), without first using the standard HUD frame.
        // Evaluate only the pixels the board detector actually reads; previews
        // require only 256 samples, not a second full-frame allocation per drag.
        internal sealed class CanonicalView
        {
            private struct Slice
            {
                internal int First, Last, FirstWeight, LastWeight;
            }

            private readonly PixelFrame source;
            private readonly Slice[] horizontal, vertical;
            private readonly double area;
            private readonly bool halfScale;

            internal CanonicalView(PixelFrame frame)
            {
                source = frame.FullResolution;
                if (source.Width == Width && source.Height == Height) return;
                horizontal = Axis(source.Width, Width);
                vertical = Axis(source.Height, Height);
                area = (double)source.Width * source.Height;
                halfScale = source.Width == 2 * Width && source.Height == 2 * Height;
            }

            private static Slice[] Axis(int sourceLength, int targetLength)
            {
                var slices = new Slice[targetLength];
                for (int i = 0; i < targetLength; i++)
                {
                    long start = (long)i * sourceLength, end = start + sourceLength;
                    int first = (int)(start / targetLength), last = (int)((end - 1) / targetLength);
                    slices[i] = new Slice
                    {
                        First = first, Last = last,
                        FirstWeight = first == last ? sourceLength : targetLength - (int)(start % targetLength),
                        LastWeight = (int)(end - (long)last * targetLength)
                    };
                }
                return slices;
            }

            internal void Rgb(int x, int y, out int red, out int green, out int blue)
            {
                if (horizontal == null)
                {
                    source.Rgb(x, y, out red, out green, out blue);
                    return;
                }
                Slice xs = horizontal[x], ys = vertical[y];
                long r = 0, g = 0, b = 0;
                for (int sy = ys.First; sy <= ys.Last; sy++)
                {
                    long wy = sy == ys.First ? ys.FirstWeight : sy == ys.Last ? ys.LastWeight : Height;
                    for (int sx = xs.First; sx <= xs.Last; sx++)
                    {
                        long wx = sx == xs.First ? xs.FirstWeight : sx == xs.Last ? xs.LastWeight : Width;
                        int sr, sg, sb;
                        source.Rgb(sx, sy, out sr, out sg, out sb);
                        long weight = wx * wy;
                        r += sr * weight; g += sg * weight; b += sb * weight;
                    }
                }
                // OpenCV's optimized integer 2x shrink rounds half upwards;
                // the generic area accumulation rounds to the nearest byte.
                red = Round(r); green = Round(g); blue = Round(b);
            }

            private int Round(long value)
            {
                return halfScale ? (int)Math.Floor(value / area + .5) : (int)Math.Round(value / area);
            }
        }
    }

    internal static class RahaiSolver
    {
        private const ulong Left = 0x0101010101010101UL, Right = Left << 7;
        private static readonly ulong[] Lines = Enumerable.Range(0, 8).Select(row => 255UL << (row * 8))
            .Concat(Enumerable.Range(0, 8).Select(column => Left << column)).ToArray();

        private sealed class State
        {
            internal ulong Board;
            internal int Remaining, Bonus, FirstLines, Value;
            internal RahaiMove Move;
        }

        internal static ulong ClearLines(ulong mask, ulong fixedBlocks, out int count)
        {
            ulong cleared = 0;
            count = 0;
            foreach (ulong line in Lines)
                if ((mask & line) == line)
                {
                    cleared |= line;
                    count++;
                }
            return (mask & ~cleared) | fixedBlocks;
        }

        internal static RahaiMove Choose(RahaiBoard board, HashSet<Tuple<int, ulong>> excluded = null)
        {
            List<RahaiMove>[] choices = board.Pieces.Select(Placements).ToArray();
            int total = choices.Length;
            var states = new List<State> { new State { Board = board.Mask, Remaining = (1 << total) - 1 } };
            var values = new Dictionary<ulong, int>();
            RahaiMove best = null;
            for (int depth = 0; depth < total; depth++)
            {
                var candidates = new Dictionary<Tuple<ulong, int>, State>();
                var order = new List<Tuple<ulong, int>>();
                foreach (State state in states)
                    for (int index = 0; index < total; index++)
                    {
                        int pieceBit = 1 << index;
                        if ((state.Remaining & pieceBit) == 0)
                            continue;
                        int rest = state.Remaining & ~pieceBit;
                        foreach (RahaiMove placement in choices[index])
                        {
                            if ((state.Board & placement.Mask) != 0 || state.Move == null && excluded != null &&
                                excluded.Contains(Tuple.Create(placement.Slot, placement.Mask)))
                                continue;
                            int lines;
                            ulong after = ClearLines(state.Board | placement.Mask, board.Fixed, out lines);
                            int value;
                            if (!values.TryGetValue(after, out value))
                                values.Add(after, value = Value(after));
                            var candidate = new State
                            {
                                Board = after, Remaining = rest, Bonus = state.Bonus + lines * (lines + 1) * 30,
                                FirstLines = state.Move == null ? lines : state.FirstLines, Move = state.Move ?? placement,
                                Value = value
                            };
                            var key = Tuple.Create(after, rest);
                            State earlier;
                            if (!candidates.TryGetValue(key, out earlier))
                                order.Add(key);
                            else if (CompareRank(candidate, earlier) <= 0)
                                continue;
                            candidates[key] = candidate;
                        }
                    }
                if (order.Count == 0)
                    break;
                states = order.Select(key => candidates[key]).OrderByDescending(state => state.Bonus + state.Value)
                    .ThenByDescending(state => state.FirstLines).ThenBy(state => state.Move.Turns).ToList();
                best = states[0].Move;
                if (total == 3 && depth < 2)
                {
                    var count = new int[1 << total];
                    states = states.Where(state => count[state.Remaining]++ < 24).ToList();
                }
            }
            return best;
        }

        private static int CompareRank(State left, State right)
        {
            int score = (left.Bonus + left.Value).CompareTo(right.Bonus + right.Value);
            if (score != 0)
                return score;
            int lines = left.FirstLines.CompareTo(right.FirstLines);
            return lines == 0 ? right.Move.Turns.CompareTo(left.Move.Turns) : lines;
        }

        internal static List<RahaiMove> Placements(RahaiPiece piece)
        {
            var moves = new List<RahaiMove>();
            var orientations = new HashSet<int>();
            Point[] cells = piece.Cells;
            for (int turns = 0; turns < 4; turns++)
            {
                int width = cells.Max(point => point.X) + 1, height = cells.Max(point => point.Y) + 1;
                int shape = 0;
                foreach (Point cell in cells) shape |= 1 << (cell.Y * 5 + cell.X);
                if (orientations.Add(shape))
                    for (int row = 0; row <= 8 - height; row++)
                        for (int column = 0; column <= 8 - width; column++)
                        {
                            ulong mask = 0;
                            foreach (Point cell in cells) mask |= 1UL << ((row + cell.Y) * 8 + column + cell.X);
                            moves.Add(new RahaiMove { Slot = piece.Slot, Turns = turns, Row = row, Column = column,
                                Width = width, Height = height, Mask = mask });
                        }
                cells = cells.Select(point => new Point(height - 1 - point.Y, point.X)).ToArray();
            }
            return moves;
        }

        private static int Value(ulong mask)
        {
            ulong empty = ~mask;
            ulong horizontal = empty & (empty >> 1) & ~Right;
            int squares = Count(horizontal & (horizontal >> 8));
            ulong isolated = empty & ~((empty << 8) | (empty >> 8) | ((empty & ~Right) << 1) | ((empty & ~Left) >> 1));
            int near = 0;
            foreach (ulong line in Lines)
            {
                int filled = Math.Max(0, Count(mask & line) - 4);
                near += filled * filled;
            }
            return 2 * squares + near - 12 * Count(isolated) - 2 * Count(mask);
        }

        private static int Count(ulong value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }
    }
}
