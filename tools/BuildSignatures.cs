using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace TideCombat
{
    internal static class BuildSignatures
    {
        private static readonly Regex AssetName = new Regex(@"^(.+)_(\d+)x(\d+)_(\d+)_(\d+)$");
        private static readonly string[] Common = { "mouse_forte", "e_forte", "edge_levitator", "f_break_full" };

        public static int Main(string[] arguments)
        {
            if (arguments.Length != 2)
            {
                Console.Error.WriteLine("Usage: BuildSignatures <reference directory> <signature file>");
                return 2;
            }
            string templates = Path.Combine(arguments[0], "assets", "templates");
            var indicators = new HashSet<string>(Common, StringComparer.Ordinal);
            indicators.UnionWith(CombatVision.SceneIndicators);
            indicators.UnionWith(CombatVision.CloudTargetLabels);
            indicators.UnionWith(CombatVision.DesktopTargetLabels);
            indicators.UnionWith(CombatVision.DesktopTargetAreas);
            indicators.UnionWith(CombatVision.HotkeyLabels);
            indicators.UnionWith(FarmIndicators.All);
            indicators.UnionWith(TaskIndicators.All);
            indicators.UnionWith(TaskIndicators.EchoReceiptDigits);
            indicators.UnionWith(TaskIndicators.TeamNumbers);
            foreach (CharacterKit fighter in CharacterRoster.All)
                indicators.UnionWith(fighter.Indicators);
            var fighters = new HashSet<string>(StringComparer.Ordinal);
            using (var destination = new StreamWriter(arguments[1], false, new UTF8Encoding(false)))
            {
                destination.WriteLine("# binary rank signatures with 1600x900 HUD coordinates");
                foreach (string path in Directory.GetFiles(templates, "*.png").OrderBy(Path.GetFileName, StringComparer.Ordinal))
                {
                    Match filename = AssetName.Match(Path.GetFileNameWithoutExtension(path));
                    if (!filename.Success) continue;
                    string label = filename.Groups[1].Value;
                    string fighterId = CharacterRoster.FromPortrait(label);
                    if (fighterId == null && !indicators.Contains(label)) continue;
                    double scale = 900.0 / Number(filename, 3);
                    int referenceWidth = Number(filename, 2);
                    int x = (int)Math.Round((Number(filename, 4) - referenceWidth / 2.0) * scale + 800);
                    int y = (int)Math.Round(Number(filename, 5) * scale);
                    Rectangle placement;
                    VisualSignature descriptor;
                    string light = null;
                    using (var source = new Bitmap(path))
                    {
                        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
                        PixelFrame frame = Scale(source, width, height);
                        int insetX = fighterId == null ? 0 : Math.Max(1, (int)Math.Round(width * .06));
                        int insetY = fighterId == null ? 0 : Math.Max(1, (int)Math.Round(height * .06));
                        var crop = new Rectangle(insetX, insetY, width - 2 * insetX, height - 2 * insetY);
                        var pixels = new AreaPixels(frame, new Rectangle(0, 0, width, height));
                        int[] buffer = VisualSignature.Buffer();
                        VisualSignature.Sample(pixels, crop, buffer);
                        descriptor = VisualSignature.FromSample(buffer);
                        if (label.StartsWith("roccia_air_forte", StringComparison.Ordinal) || CombatVision.CloudTargetLabels.Contains(label) ||
                            CombatVision.DesktopTargetLabels.Contains(label) || CombatVision.HotkeyLabels.Contains(label) ||
                            label == "f_break" || label == "f_break_full" || label == "mouse_forte" || label == "e_forte" ||
                            label == "pick_up_f_hcenter_vcenter" || label == "dialog_3_dots" ||
                            label == "abyss_start_marker" || label == "abyss_tower_start_marker" ||
                            FarmIndicators.All.Contains(label) || TaskIndicators.TeamNumbers.Contains(label) ||
                            label == "echo_locked" || label == "echo_not_locked" ||
                            label == "echo_dropped" || label == "echo_not_dropped")
                            light = EncodeLocalContrast(frame, crop);
                        placement = new Rectangle(x + insetX, y + insetY, crop.Width, crop.Height);
                        if (label == "forte_cartethyia_sword2" || label == "forte_cartethyia_sword3")
                        {
                            crop.Height /= 2;
                            VisualSignature.Sample(pixels, crop, buffer);
                            destination.WriteLine(string.Join("\t", new[] { label + "_half", "-", Format(x), Format(y),
                                Format(crop.Width), Format(crop.Height), VisualSignature.FromSample(buffer).Encode(),
                                EncodeLocalContrast(frame, crop) }));
                        }
                    }
                    if (fighterId != null)
                    {
                        int slot = (int)Math.Round((placement.Y + placement.Height / 2.0 - 204) / 110);
                        placement.Y -= Math.Max(0, Math.Min(2, slot)) * 110;
                        fighters.Add(fighterId);
                    }
                    string row = string.Join("\t", new[] { label, fighterId ?? "-", Format(placement.X),
                        Format(placement.Y), Format(placement.Width), Format(placement.Height), descriptor.Encode() });
                    if (light != null) row += "\t" + light;
                    if (label == "monthly_card" || label == "login_close" || label == "switch_account")
                    {
                        if (light == null) row += "\t";
                        row += "\t" + Convert.ToBase64String(File.ReadAllBytes(path));
                    }
                    destination.WriteLine(row);
                }
                WriteTerminalSignatures(destination, arguments[0]);
            }
            string[] missing = CharacterRoster.All.Where(x => !x.Id.StartsWith("Rover", StringComparison.Ordinal) || x.Id == "Rover")
                .Where(x => !fighters.Contains(x.Id)).Select(x => x.Id).ToArray();
            if (missing.Length > 0)
            {
                Console.Error.WriteLine("No portrait descriptor: " + string.Join(", ", missing));
                return 1;
            }
            Console.WriteLine("Built portrait descriptors for " + fighters.Count + " registered characters.");
            return 0;
        }

        private static int Number(Match match, int field) { return int.Parse(match.Groups[field].Value, CultureInfo.InvariantCulture); }
        private static string Format(int value) { return value.ToString(CultureInfo.InvariantCulture); }

        private static void WriteTerminalSignatures(TextWriter destination, string reference)
        {
            string path = Path.Combine(reference, "ok_templates", "14.png");
            if (!File.Exists(path))
                return;
            using (var source = new Bitmap(path))
            {
                PixelFrame frame = Scale(source, 1600, 900);
                var pixels = new AreaPixels(frame, new Rectangle(0, 0, frame.Width, frame.Height));
                WriteTerminalSignature(destination, frame, pixels, "terminal_title_mark", new Rectangle(74, 30, 36, 40));
                WriteTerminalSignature(destination, frame, pixels, "terminal_close", new Rectangle(1505, 26, 55, 54));
                WriteTerminalSignature(destination, frame, pixels, "terminal_guide", new Rectangle(755, 582, 78, 77));
                WriteTerminalSignature(destination, frame, pixels, "terminal_mail", new Rectangle(999, 838, 56, 46));
            }
        }

        private static void WriteTerminalSignature(TextWriter destination, PixelFrame frame, AreaPixels pixels,
            string label, Rectangle crop)
        {
            int[] buffer = VisualSignature.Buffer();
            VisualSignature.Sample(pixels, crop, buffer);
            destination.WriteLine(string.Join("\t", new[] { label, "-", Format(crop.X), Format(crop.Y),
                Format(crop.Width), Format(crop.Height), VisualSignature.FromSample(buffer).Encode(),
                EncodeLocalContrast(frame, crop) }));
        }

        private static string EncodeLocalContrast(PixelFrame frame, Rectangle crop)
        {
            var samples = new int[crop.Width * crop.Height];
            for (int y = 0; y < crop.Height; y++)
                for (int x = 0; x < crop.Width; x++)
                    samples[y * crop.Width + x] = frame.Light(crop.X + x, crop.Y + y);
            double mean = samples.Average();
            return Convert.ToBase64String(samples.Select(value => unchecked((byte)(sbyte)Math.Round((value - mean) / 4))).ToArray());
        }

        private static PixelFrame Scale(Bitmap source, int width, int height)
        {
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.InterpolationMode = InterpolationMode.Bilinear;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    graphics.DrawImage(source, new Rectangle(0, 0, width, height),
                        new Rectangle(Point.Empty, source.Size), GraphicsUnit.Pixel);
                }
                var pixels = new byte[width * height * 4];
                BitmapData memory = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try { Marshal.Copy(memory.Scan0, pixels, 0, pixels.Length); }
                finally { bitmap.UnlockBits(memory); }
                return new PixelFrame(pixels, width, height);
            }
        }
    }
}
