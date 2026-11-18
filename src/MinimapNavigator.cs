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
        private const int Rotations = 72, AngleStep = 5, ArrowSide = 47;
        private static readonly Rectangle Minimap = new Rectangle(30, 24, 155, 154);
        private static readonly Rectangle ArrowBox = new Rectangle(83, 78, ArrowSide, ArrowSide);

        private readonly byte[][] arrow = new byte[Rotations][];
        private readonly int[] patch = new int[ArrowSide * ArrowSide];
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
            if (data.Length < Rotations * ArrowSide * ArrowSide)
                throw new InvalidDataException("方向模板数据不完整。");
            for (int index = 0; index < Rotations; index++)
            {
                arrow[index] = new byte[ArrowSide * ArrowSide];
                Buffer.BlockCopy(data, index * ArrowSide * ArrowSide, arrow[index], 0, ArrowSide * ArrowSide);
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
            double targetX = marker.X + marker.Width / 2.0 - marker.Width / 2.0;
            double targetY = marker.Y + marker.Height / 2.0 + marker.Height / 2.0;
            double direction = Math.Atan2(targetY - centerY, targetX - centerX) * 180 / Math.PI;
            if (direction < 0) direction += 360;
            double heading = MatchArrow(frame);
            turn = direction - heading;
            if (turn > 180) turn -= 360;
            else if (turn < -180) turn += 360;
            return true;
        }

        private double MatchArrow(PixelFrame frame)
        {
            for (int y = 0; y < ArrowSide; y++)
            {
                for (int x = 0; x < ArrowSide; x++)
                    patch[y * ArrowSide + x] = frame.Light(ArrowBox.X + x, ArrowBox.Y + y);
            }
            double best = double.MinValue;
            int bestAngle = 0;
            for (int index = 0; index < Rotations; index++)
            {
                double score = Similarity(patch, arrow[index]);
                if (score <= best) continue;
                best = score;
                bestAngle = index * AngleStep;
            }
            return bestAngle;
        }

        private static double Similarity(int[] values, byte[] template)
        {
            double sumA = 0, sumB = 0;
            for (int index = 0; index < values.Length; index++)
            {
                sumA += values[index];
                sumB += template[index];
            }
            double meanA = sumA / values.Length, meanB = sumB / values.Length;
            double covariance = 0, varianceA = 0, varianceB = 0;
            for (int index = 0; index < values.Length; index++)
            {
                double a = values[index] - meanA;
                double b = template[index] - meanB;
                covariance += a * b;
                varianceA += a * a;
                varianceB += b * b;
            }
            double denominator = Math.Sqrt(varianceA * varianceB);
            return denominator <= 1e-6 ? 0 : covariance / denominator;
        }
    }
}
