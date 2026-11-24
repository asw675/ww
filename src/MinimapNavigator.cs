using System;
using System.Drawing;
using System.IO;

namespace TideCombat
{
    // Mirrors the reference minimap navigation: locate the boss marker inside the
    // minimap box, rotate the player arrow template to find the heading, and
    // return the clockwise turn angle in degrees.
    internal sealed class MinimapNavigator
    {
        private const int Rotations = 360, LegacyRotations = 72, ArrowSide = 47, Area = ArrowSide * ArrowSide;
        private static readonly Rectangle Minimap = new Rectangle(30, 24, 155, 154);
        private static readonly Rectangle ArrowBox = new Rectangle(83, 78, ArrowSide, ArrowSide);

        private readonly byte[][] arrows;
        private readonly int[] templateSums;
        private readonly double[] templateNorms;
        private readonly int[] patch = new int[Area];
        private readonly FarmVision vision = new FarmVision();

        internal static string ArrowPath
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates =
                {
                    Path.Combine(baseDir, "assets", "minimap", "arrow.bin"),
                    Path.Combine("assets", "minimap", "arrow.bin"),
                    Path.Combine(baseDir, "..", "..", "assets", "minimap", "arrow.bin")
                };
                foreach (string candidate in candidates)
                    if (File.Exists(candidate))
                        return candidate;
                return candidates[0];
            }
        }

        internal MinimapNavigator(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            int count = data.Length / Area;
            if (data.Length % Area != 0 || (count != Rotations && count != LegacyRotations))
                throw new InvalidDataException("方向模板数据不完整。");
            arrows = new byte[count][];
            templateSums = new int[count];
            templateNorms = new double[count];
            for (int index = 0; index < count; index++)
            {
                byte[] template = new byte[Area];
                Buffer.BlockCopy(data, index * Area, template, 0, Area);
                arrows[index] = template;
                int sum = 0, squares = 0;
                foreach (int light in template)
                {
                    sum += light;
                    squares += light * light;
                }
                templateSums[index] = sum;
                templateNorms[index] = Math.Sqrt((long)squares * Area - (long)sum * sum);
            }
        }

        internal bool TryTurn(PixelFrame frame, out double turn)
        {
            turn = 0;
            Rectangle marker;
            if (!vision.FindFirst(frame, "boss_check_mark_minimap", Minimap, 2, .68, .70, out marker))
                return false;
            int centerX = Minimap.X + Minimap.Width / 2;
            int centerY = Minimap.Y + Minimap.Height / 2;
            double targetX = marker.Left;
            double targetY = marker.Bottom;
            double direction = Math.Atan2(targetY - centerY, targetX - centerX) * 180 / Math.PI;
            if (direction < 0) direction += 360;
            double heading;
            if (!TryHeading(frame, out heading)) return false;
            turn = direction - heading;
            if (turn > 180) turn -= 360;
            else if (turn < -180) turn += 360;
            return true;
        }

        internal bool TryHeading(PixelFrame frame, out double heading)
        {
            heading = 0;
            if (frame.Width < ArrowBox.Right || frame.Height < ArrowBox.Bottom)
                return false;
            int sum = 0, squares = 0;
            for (int y = 0; y < ArrowSide; y++)
            {
                for (int x = 0; x < ArrowSide; x++)
                {
                    int light = frame.Light(ArrowBox.X + x, ArrowBox.Y + y);
                    patch[y * ArrowSide + x] = light;
                    sum += light;
                    squares += light * light;
                }
            }
            if ((long)squares * Area == (long)sum * sum)
                return false;
            // The live crop's variance is identical for all angles.
            double best = 0;
            for (int index = 0; index < arrows.Length; index++)
            {
                if (templateNorms[index] == 0) continue;
                byte[] template = arrows[index];
                int dot = 0;
                for (int position = 0; position < Area; position++)
                    dot += patch[position] * template[position];
                long covariance = (long)dot * Area - (long)sum * templateSums[index];
                double score = covariance / templateNorms[index];
                if (score <= best) continue;
                best = score;
                heading = index * (Rotations / arrows.Length);
            }
            return best > 0;
        }
    }
}
