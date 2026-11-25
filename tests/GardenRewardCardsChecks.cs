using System;
using System.Drawing;
using System.Linq;

namespace TideCombat
{
    internal static class GardenRewardCardsChecks
    {
        internal static void UsesVerifiedCardsAndRejectsGutters()
        {
            using (var bitmap = new Bitmap(1600, 900))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var reader = new FrameReader())
            using (var paper = new SolidBrush(Color.FromArgb(236, 231, 214)))
            {
                graphics.Clear(Color.FromArgb(64, 70, 35));
                graphics.FillRectangle(paper, 267, 315, 330, 300);
                graphics.FillRectangle(paper, 643, 315, 331, 301);
                graphics.FillRectangle(paper, 1021, 315, 330, 299);
                var first = Line("先锋幼岩", 294, 162, 89, 26);
                var second = Line("遁地鼠", 671, 161, 69, 26);
                var third = Line("魔术先生", 1049, 162, 91, 26);
                var mistaken = Line("选择", 979, 162, 32, 24);
                var cards = GardenRewardCards.Read(reader.Read(bitmap), new[] { first, second, third, mistaken });
                Require(cards.Count == 3 && cards.Select(card => card.Title).SequenceEqual(new[] { first.Text, second.Text, third.Text }),
                    "only recognized titles inside a visible reward card are associated with its body");
                var gutter = new Point(990, 473);
                Require(cards.All(card => !card.Bounds.Contains(gutter) && !card.Header.Contains(gutter) && !card.SafeClick.Contains(gutter)),
                    "a click between the second and third card must be rejected");
                foreach (GardenRewardCard card in cards)
                    Require(card.Bounds.Contains(card.Header) && card.Header.Contains(card.SafeClick) && card.SafeClick.Top > card.Header.Top,
                        "only the interior title band can serve as the click target");
                cards = GardenRewardCards.Read(reader.Read(bitmap), new[] { first, third });
                Require(cards.Count == 2 && cards.All(card => card.Title != second.Text),
                    "card paint without a readable title cannot authorize a click");
            }
        }

        private static OcrLine Line(string text, int x, int y, int width, int height)
        {
            return new OcrLine { Text = text, Score = .99f, Box = new Rectangle(x, y, width, height) };
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
