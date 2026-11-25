using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace TideCombat
{
    internal static class GardenMath
    {
        private static readonly Regex Points = new Regex(@"(?<!\d)([0-9]{1,4})[／/]6000(?!\d)");
        private static readonly Regex Day = new Regex(@"^第([0-9]{1,2})(?:日|天)$|^Day([0-9]{1,2})$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Stage = new Regex(@"^(?:阶段|階段|Stage)[:：]?([0-9]{1,2})(?:[/／][0-9]{1,2})?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static bool IsCompleted(string text)
        {
            string value = Normalize(text);
            if (value.Contains("已达到上限") || value.Contains("已達到上限") || value.Contains("已达上限") ||
                value.Contains("已達上限") || value.IndexOf("LimitReached", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            Match match = Points.Match(value);
            if (match.Success)
                return match.Groups[1].Value == "6000";
            return value == "6000" || value == "本周积分6000" || value == "本週積分6000";
        }

        internal static bool IsFinished(IEnumerable<string> texts)
        {
            return Regex.Matches(string.Join(" ", texts), "6000").Count != 1;
        }

        internal static bool HasWeeklyLimit(IEnumerable<OcrLine> lines)
        {
            return lines.Any(line => line.Score >= .70f && IsCompleted(line.Text));
        }

        internal static string Normalize(string text)
        {
            return Regex.Replace(text ?? string.Empty, @"\s+", string.Empty);
        }

        internal static Point PreferredPoint(Size frame, Rectangle detected, double x, double y)
        {
            Point reference = new Point((int)(frame.Width * x), (int)(frame.Height * y));
            return detected.Contains(reference) ? reference : new Point(detected.X + detected.Width / 2,
                detected.Y + detected.Height / 2);
        }

        internal static int WeeklyClaimSettleMs(bool initial, int attempt)
        {
            return (initial ? 3000 : 2000) - 1000 * attempt;
        }

        internal static Rectangle NextDayButton(IEnumerable<OcrLine> lines)
        {
            return DailyClaimVision.UniqueButton(lines, "下一日", "下一天", "次日", "进入下一日", "進入下一日",
                "进入下一天", "進入下一天", "结束本日", "結束本日", "Next Day", "End Day");
        }

        internal static int ReadDay(IEnumerable<OcrLine> lines)
        {
            int day = 0;
            foreach (OcrLine line in lines)
            {
                if (line.Score < .75f) continue;
                Match match = Day.Match(Normalize(line.Text));
                if (!match.Success) continue;
                int observed = int.Parse(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
                if (observed == 0 || day != 0 && day != observed) return 0;
                day = observed;
            }
            return day;
        }

        internal static bool ReadBoard(IEnumerable<OcrLine> lines, out GardenBoardPosition position)
        {
            position = new GardenBoardPosition { Day = ReadDay(lines) };
            foreach (OcrLine line in lines)
            {
                if (line.Score < .75f) continue;
                Match match = Stage.Match(Normalize(line.Text));
                if (!match.Success) continue;
                int stage = int.Parse(match.Groups[1].Value);
                if (position.Stage != 0 && position.Stage != stage)
                {
                    position.Stage = 0;
                    break;
                }
                position.Stage = stage;
            }
            return position.Day > 0 && DailyClaimVision.Contains(lines, "本周游园", "本週遊園",
                "本周遊園", "This Week's Garden", "Weekly Garden");
        }

        internal static string RewardOfferKey(IEnumerable<OcrLine> names, int width)
        {
            string[] titles = new string[3];
            foreach (OcrLine line in names)
            {
                string title = Normalize(line.Text);
                if (line.Score < .76f || title.Length < 2 || line.Box.IsEmpty) continue;
                double x = (line.Box.Left + line.Box.Width / 2.0) / width;
                int slot = x >= .17 && x <= .37 ? 0 : x >= .40 && x <= .61 ? 1 : x >= .635 && x <= .85 ? 2 : -1;
                if (slot < 0) continue;
                if (titles[slot] != null && titles[slot] != title) return null;
                titles[slot] = title;
            }
            return Array.Exists(titles, title => title == null) ? null : string.Join("\n", titles);
        }

        internal static bool HasGardenResult(IEnumerable<OcrLine> lines)
        {
            return lines.Any(line => line.Score >= .70f &&
                (IsCompleted(line.Text) || Points.IsMatch(Normalize(line.Text)) ||
                 Normalize(line.Text).IndexOf("6000", StringComparison.Ordinal) >= 0 ||
                 Normalize(line.Text).Contains("本周积分") || Normalize(line.Text).Contains("本週積分") ||
                 Normalize(line.Text).Contains("本轮积分") || Normalize(line.Text).Contains("本輪積分") ||
                 Normalize(line.Text).IndexOf("WeeklyPoints", StringComparison.OrdinalIgnoreCase) >= 0));
        }
    }

    internal struct GardenBoardPosition
    {
        internal int Stage, Day;

        internal bool IsAfter(GardenBoardPosition before)
        {
            if (Day == 0 || before.Day == 0) return false;
            return Stage > 0 && before.Stage > 0 && Stage != before.Stage ?
                Stage > before.Stage : Day > before.Day;
        }
    }

    internal sealed class GardenEventProgress
    {
        private long previousCapture;
        private string previousState;
        private int consecutive;

        internal bool Observe(long capture, string state)
        {
            if (capture == 0 || capture == previousCapture) return false;
            previousCapture = capture;
            consecutive = state == null ? 0 : state == previousState ? consecutive + 1 : 1;
            previousState = state;
            return consecutive >= 2;
        }
    }

    internal sealed class GardenReport
    {
        internal string State;
        internal int Actions;
        internal Size Size;
        internal bool Finished;
        internal string Failure;
    }

    // Clicks garden weekly events until the 6000-point limit is reached.
    internal sealed class GardenRunner : IDisposable
    {
        private const string NextDay = "garden_next_day", RewardChoice = "garden_reward_choice",
            Encounter = "garden_encounter";
        internal static readonly string[] EventLabels =
        {
            "garden_start_game", "garden_confirm", "garden_continue_game",
            "garden_new_stage", "garden_shop_buy", "garden_shop_close", "garden_tip_close", "garden_unpause"
        };

        private readonly CancellationToken token;
        private readonly WorldSession world;
        private readonly bool ownsWorld;
        private readonly IProgress<GardenReport> progress;
        private readonly Action<string, PixelFrame> recordEvidence;
        private int actions;
        private long lastActivity;
        private bool weeklyRewardVerified;
        internal bool Failed { get; private set; }
        internal string Failure { get; private set; }
        internal bool CompletionVerified { get; private set; }
        internal bool HasUnverifiedConsumption { get; private set; }
        internal bool AlreadyCompleted { get; private set; }

        internal GardenRunner(CombatSettings settings, CancellationToken token, IProgress<GardenReport> progress,
            Action<string, PixelFrame> recordEvidence = null, WorldSession session = null)
        {
            this.token = token;
            this.progress = progress;
            this.recordEvidence = recordEvidence;
            ownsWorld = session == null;
            world = session ?? new WorldSession(settings, token);
        }

        internal void Run()
        {
            if (token.IsCancellationRequested) return;
            world.Sleep(500);
            if (token.IsCancellationRequested) return;
            RunLog.Info("周常乐园", "开始");
            GardenBoardPosition startingBoard;
            GardenEncounterPopup startingEncounter;
            if (HasOpenRewardPopup())
            {
                Record("before");
                Report("继续处理当前乐园奖励");
            }
            else if (IsGardenCandidate() && HasEncounter(out startingEncounter))
            {
                Record("before");
                Report("继续处理当前奇缘异遇");
            }
            else if (IsGardenCandidate() && TryReadBoard(out startingBoard))
            {
                Record("before");
                Report("继续当前乐园挑战");
            }
            else if (!PrepareWeeklyRun())
                return;
            world.Sleep(1000);
            lastActivity = world.Now;
            while (!token.IsCancellationRequested)
            {
                world.Sleep(100);
                Rectangle box;
                string label = ReadEvent(out box);
                if (label == null && TryFinish())
                    break;
                if (label != null)
                {
                    if (!Handle(label, box, world.InputRouteVersion))
                    {
                        if (!token.IsCancellationRequested)
                            Fail("未确认" + EventName(label) + "后的游戏画面，已停止重复点击。");
                        break;
                    }
                    actions++;
                    lastActivity = world.Now;
                    Report("已确认" + EventName(label));
                    continue;
                }
                if (world.Now - lastActivity > 45000)
                {
                    Fail("45 秒没有识别到乐园操作或结束界面，停止等待。");
                    break;
                }
                world.Sleep(200);
            }
            if (CompletionVerified || token.IsCancellationRequested)
                Finish();
            else if (!Failed)
                Fail("未确认本周乐园积分达到上限。");
        }

        private bool HasOpenRewardPopup()
        {
            if (!IsGardenCandidate()) return false;
            GardenRewardPopup pending;
            return TryReadReward(out pending) || !pending.Hide.IsEmpty;
        }

        private bool PrepareWeeklyRun()
        {
            Report("打开每周乐园");
            Rectangle garden;
            if (!OpenWeeklyPage(out garden))
            {
                Fail("无法打开每周乐园页面。");
                return false;
            }
            int route = world.InputRouteVersion;
            Record("before");
            Record("garden-weekly-before");
            if (!ClaimWeeklyRewards(route, initial: true))
            {
                Fail("周常乐园页面中的可领取奖励未得到确认。");
                return false;
            }
            if (WeeklyCompleted(route))
            {
                if (!weeklyRewardVerified)
                {
                    Fail("已达到周积分上限，但未确认奖励已经领取。");
                    return false;
                }
                AlreadyCompleted = true;
                CompletionVerified = true;
                Record("after-garden-verified");
                Finish();
                return false;
            }
            if (!world.ClickMenuTransition(route, () =>
            {
                Rectangle entry;
                return world.Vision.IsGuidebookQuestSelected(world.Frame) && FindGardenEntry(out entry)
                    ? entry : Rectangle.Empty;
            }, GardenGameVisible, 6000, 500, entry => GardenMath.PreferredPoint(
                new Size(world.Frame.Width, world.Frame.Height), entry, .246, .486)))
            {
                if (!token.IsCancellationRequested) Fail("未确认从周常页面进入乐园。");
                return false;
            }
            return true;
        }

        private static string EventName(string label)
        {
            return label == NextDay ? "进入下一日" : label == RewardChoice ? "处理乐园奖励" :
                label == Encounter ? "跳过奇缘异遇" :
                label == "garden_start_game" ? "开始乐园挑战" : "处理乐园事件";
        }

        private string ReadEvent(out Rectangle box)
        {
            box = Rectangle.Empty;
            if (!IsGardenCandidate()) return null;
            GardenRewardPopup reward;
            if (TryReadReward(out reward))
            {
                box = reward.Skip;
                return RewardChoice;
            }
            if (!reward.Hide.IsEmpty)
                return null;
            GardenEncounterPopup encounter;
            if (HasEncounter(out encounter))
            {
                box = encounter.Decline;
                return Encounter;
            }
            if (GardenResultVisible())
                return null;
            box = GardenMath.NextDayButton(world.Ocr.ReadRelative(world.Frame, .71, .83, .97, .97, centered: true));
            if (!box.IsEmpty) return NextDay;
            return world.Vision.BestMatchScored(world.Frame, EventLabels, null, 20, .82, .72, out box);
        }

        private bool TryReadReward(out GardenRewardPopup reward)
        {
            reward = default(GardenRewardPopup);
            return world.Captured && GardenRewardVision.TryRead(world.Frame, world.Ocr, out reward);
        }

        private bool HasEncounter(out GardenEncounterPopup encounter)
        {
            encounter = default(GardenEncounterPopup);
            if (!world.Captured) return false;
            GardenEncounterVision.TryRead(world.Frame, world.Ocr, out encounter);
            return !encounter.Title.IsEmpty;
        }

        private bool IsGardenCandidate()
        {
            return world.Captured && !world.VerifiedWorld && !world.Terminal &&
                !world.Vision.IsGuidebookQuestSelected(world.Frame);
        }

        private bool GardenGameVisible()
        {
            Rectangle box;
            return ReadEvent(out box) != null || GardenResultVisible();
        }

        private bool TryFindResult(out Rectangle restart, out Rectangle back)
        {
            restart = back = Rectangle.Empty;
            if (!IsGardenCandidate() || !world.Vision.FindLabel(world.Frame, "a_garden_restart", .80, out restart) ||
                !world.Vision.FindLabel(world.Frame, "a_garden_back", .80, out back)) return false;
            GardenRewardPopup popup;
            TryReadReward(out popup);
            GardenEncounterPopup encounter;
            return popup.Hide.IsEmpty && !HasEncounter(out encounter) &&
                GardenMath.HasGardenResult(world.Ocr.ReadRelative(world.Frame, .34, .27, .88, .68, centered: true));
        }

        private bool Handle(string label, Rectangle box, int route)
        {
            RunLog.Info("周常乐园", "识别事件 " + label + "｜区域=" + box);
            Report("正在" + EventName(label));
            switch (label)
            {
                case RewardChoice:
                    return HandleReward(route, box);
                case Encounter:
                    return HandleEncounter(route);
                case "garden_start_game":
                    if (!Blessing(route))
                        return false;
                    break;
            }
            return VerifyEventClick(route, label, () =>
            {
                Rectangle current;
                return ReadEvent(out current) == label && box.IntersectsWith(current) ? current : Rectangle.Empty;
            }, retryIfUnchanged: label != "garden_shop_buy");
        }

        private bool VerifyEventClick(int route, string label, Func<Rectangle> findControl, bool retryIfUnchanged = true)
        {
            var progressCheck = new GardenEventProgress();
            GardenBoardPosition beforeBoard = default(GardenBoardPosition);
            if (label == NextDay) TryReadBoard(out beforeBoard);
            string beforeOffer = label == RewardChoice ? ReadRewardOffer() : null;
            int timeout = label == RewardChoice || label == Encounter ? 12000 : label == NextDay ? 8000 : 5000;
            bool sourceSeen = false;
            Record(label == RewardChoice ? "garden-choice-before" : label == NextDay ? "garden-next-day-before" :
                label == Encounter ? "garden-encounter-confirm-before" : "garden-action-before");
            Func<bool> destination = () =>
            {
                if (!sourceSeen) return false;
                Rectangle current;
                string next = ReadEvent(out current);
                string state = next != label ? next : null;
                if (next == null && GardenResultVisible()) state = "result";
                if (label == RewardChoice && next == RewardChoice && beforeOffer != null)
                {
                    GardenRewardPopup popup;
                    if (TryReadReward(out popup) && !GardenRewardVision.IsConfirmEnabled(world.Frame, popup))
                    {
                        string offer = ReadRewardOffer();
                        if (offer != null && offer != beforeOffer) state = "offer-" + offer;
                    }
                }
                if (((label == RewardChoice || label == Encounter) && state == null && next != label) ||
                    (label == NextDay && next == NextDay))
                {
                    GardenBoardPosition board;
                    if (TryReadBoard(out board) && (label != NextDay || board.IsAfter(beforeBoard)))
                        state = string.Format("board-{0}-{1}", board.Stage, board.Day);
                }
                return progressCheck.Observe(world.Frame.CaptureId, state);
            };
            if (!retryIfUnchanged)
            {
                Rectangle target = findControl();
                if (target.IsEmpty || world.InputRouteVersion != route) return false;
                sourceSeen = true;
                HasUnverifiedConsumption = true;
                if (!world.ClickOnRoute(target, route))
                    RunLog.Warn("周常乐园", "操作输入未获确认，继续核对游戏画面");
                bool confirmed = world.WaitUntil(() => world.InputRouteVersion != route || destination(), timeout) &&
                    world.InputRouteVersion == route;
                if (confirmed) HasUnverifiedConsumption = false;
                return confirmed;
            }
            return world.ClickMenuTransition(route, () =>
            {
                Rectangle target = findControl();
                sourceSeen |= !target.IsEmpty;
                return target;
            }, destination, timeout, 300);
        }

        private bool TryReadBoard(out GardenBoardPosition board)
        {
            return GardenMath.ReadBoard(world.Ocr.ReadRelative(world.Frame, .024, .022, .235, .179, centered: true),
                out board);
        }

        private string ReadRewardOffer()
        {
            return GardenMath.RewardOfferKey(world.Ocr.ReadRelative(world.Frame, .17, .171, .85, .218, centered: true),
                world.Frame.Width);
        }

        private bool HandleReward(int route, Rectangle originalSkip)
        {
            GardenRewardPopup popup = default(GardenRewardPopup);
            bool ready = world.WaitUntil(() => world.InputRouteVersion != route ||
                (TryReadReward(out popup) && originalSkip.IntersectsWith(popup.Skip) &&
                (GardenRewardVision.IsConfirmEnabled(world.Frame, popup) ||
                    GardenRewardVision.IsConfirmDisabled(world.Frame, popup))), 2000);
            if (!ready || world.InputRouteVersion != route)
                return false;

            string offer = ReadRewardOffer();
            Rectangle intendedCard = Rectangle.Empty;
            if (GardenRewardVision.IsConfirmDisabled(world.Frame, popup))
            {
                bool maySkip;
                if (!TrySelectRewardCard(route, popup, offer, out intendedCard, out maySkip))
                {
                    if (!maySkip) return false;
                    RunLog.Info("周常乐园", "当前没有可确认的声骸卡片选择，准备跳过本次奖励");
                    return VerifyEventClick(route, RewardChoice, () =>
                    {
                        GardenRewardPopup available;
                        return ReadAvailableRewardOffer(route, popup, offer, out available) ?
                            available.Skip : Rectangle.Empty;
                    }, retryIfUnchanged: false);
                }
            }

            Func<Rectangle> findSelectedConfirm = () => FindSelectedRewardConfirm(popup, intendedCard, offer);
            bool selected = world.WaitUntil(() => world.InputRouteVersion != route ||
                !findSelectedConfirm().IsEmpty, 6000);
            if (!selected || world.InputRouteVersion != route)
            {
                RunLog.Warn("周常乐园", "未同时核验卡片选中标记和已启用的确认按钮");
                return false;
            }
            RunLog.Info("周常乐园", "卡片选中状态及已启用的确认按钮已核验");
            Report("确认已选中的声骸奖励");
            Record("garden-card-selected");
            return VerifyEventClick(route, RewardChoice, findSelectedConfirm, retryIfUnchanged: false);
        }

        private bool TrySelectRewardCard(int route, GardenRewardPopup popup, string offer, out Rectangle intendedCard,
            out bool maySkip)
        {
            intendedCard = Rectangle.Empty;
            maySkip = false;
            List<GardenRewardCard> candidates = OrderRewardCards(world.Frame, GardenRewardCards.Read(world.Frame, world.Ocr));
            bool attempted = false;
            GardenRewardPopup available;
            for (int attempt = 0; attempt < 2 && candidates.Count != 0; attempt++)
            {
                if (!world.Tick() || !ReadAvailableRewardOffer(route, popup, offer, out available)) return false;
                GardenRewardCard planned = candidates[Math.Min(attempt, candidates.Count - 1)];
                GardenRewardCard current = GardenRewardCards.Read(world.Frame, world.Ocr).FirstOrDefault(card =>
                    card.Title == planned.Title && planned.SafeClick.IntersectsWith(card.SafeClick));
                if (current.SafeClick.IsEmpty) continue;

                attempted = true;
                RunLog.Info("周常乐园", "选取已核验声骸卡片 " + current.Title + "｜区域=" + current.SafeClick);
                Record("garden-card-before");
                if (!world.ClickOnRoute(current.SafeClick, route))
                    RunLog.Warn("周常乐园", "声骸选择输入未获确认，继续核对当前卡位");
                bool selected = world.WaitUntil(() => world.InputRouteVersion != route ||
                    !FindSelectedRewardConfirm(popup, current.SafeClick, offer).IsEmpty, 4500);
                if (world.InputRouteVersion != route) return false;
                if (selected)
                {
                    intendedCard = current.SafeClick;
                    return true;
                }
                if (offer == null) return false;
            }
            maySkip = (!attempted || offer != null) && world.Tick() &&
                ReadAvailableRewardOffer(route, popup, offer, out available);
            return false;
        }

        private Rectangle FindSelectedRewardConfirm(GardenRewardPopup popup, Rectangle intendedCard, string offer)
        {
            GardenRewardPopup current;
            Rectangle selectedCard;
            if (!TryReadReward(out current) || !popup.Confirm.IntersectsWith(current.Confirm) ||
                !popup.Skip.IntersectsWith(current.Skip) ||
                !GardenRewardVision.TryFindSelectedCard(world.Frame, current, out selectedCard) ||
                (!intendedCard.IsEmpty && !selectedCard.IntersectsWith(intendedCard)) ||
                (offer != null && ReadRewardOffer() != offer)) return Rectangle.Empty;
            return current.Confirm;
        }

        private bool ReadAvailableRewardOffer(int route, GardenRewardPopup popup, string offer,
            out GardenRewardPopup current)
        {
            current = default(GardenRewardPopup);
            return world.InputRouteVersion == route && TryReadReward(out current) &&
                popup.Skip.IntersectsWith(current.Skip) && popup.Confirm.IntersectsWith(current.Confirm) &&
                GardenRewardVision.IsConfirmDisabled(world.Frame, current) &&
                (offer == null || ReadRewardOffer() == offer);
        }

        internal static List<GardenRewardCard> OrderRewardCards(PixelFrame frame, IEnumerable<GardenRewardCard> cards)
        {
            return cards.OrderBy(card => GardenRewardVision.CardPriority(frame, card.Header))
                .ThenBy(card => Math.Abs(card.Bounds.Left + card.Bounds.Width / 2 - frame.Width / 2)).ToList();
        }

        private bool HandleEncounter(int route)
        {
            GardenEncounterPopup original;
            if (!FindEncounterDecline(route, out original))
            {
                RunLog.Warn("周常乐园", "奇缘异遇未核实到「不感兴趣」选项，保留当前弹窗");
                return false;
            }
            var selection = new GardenEventProgress();
            bool selectedFromDisabled = false;
            Func<Rectangle> findConfirmation = () =>
            {
                GardenEncounterPopup current;
                if (!HasEncounter(out current) || !original.Decline.IntersectsWith(current.Decline) ||
                    !original.Confirm.IntersectsWith(current.Confirm) ||
                    !GardenEncounterVision.IsConfirmEnabled(world.Frame, current)) return Rectangle.Empty;
                return selectedFromDisabled || GardenEncounterVision.CanConfirmDecline(world.Frame, current) ?
                    current.Confirm : Rectangle.Empty;
            };
            if (findConfirmation().IsEmpty)
            {
                bool selectionSent = false;
                Record("garden-encounter-decline-before");
                bool chosen = world.ClickMenuTransition(route, () =>
                {
                    GardenEncounterPopup current;
                    if (!HasEncounter(out current) || !original.Decline.IntersectsWith(current.Decline) ||
                        !original.Confirm.IntersectsWith(current.Confirm)) return Rectangle.Empty;
                    bool disabled = GardenEncounterVision.IsConfirmDisabled(world.Frame, current);
                    if (selectionSent && !disabled) return Rectangle.Empty;
                    selectedFromDisabled = disabled;
                    selectionSent = true;
                    return current.Decline;
                }, () =>
                {
                    bool verified = selectionSent && !findConfirmation().IsEmpty;
                    return selection.Observe(world.Frame.CaptureId, verified ? "decline" : null);
                }, 4000, 250);
                if (!chosen)
                {
                    RunLog.Warn("周常乐园", "未确认「不感兴趣」已选中，保留奇缘异遇弹窗");
                    return false;
                }
            }
            Report("确认跳过奇缘异遇");
            return VerifyEventClick(route, Encounter, findConfirmation, retryIfUnchanged: false);
        }

        private bool FindEncounterDecline(int route, out GardenEncounterPopup encounter)
        {
            GardenEncounterPopup observed = default(GardenEncounterPopup);
            Func<bool> read = () => world.InputRouteVersion != route ||
                (HasEncounter(out observed) && !observed.Decline.IsEmpty && !observed.Confirm.IsEmpty);
            bool found = world.WaitUntil(read, 1700);
            for (int scroll = 0; !found && scroll < 2 && !token.IsCancellationRequested; scroll++)
            {
                if (!world.Tick() || world.InputRouteVersion != route || !HasEncounter(out observed) ||
                    observed.Confirm.IsEmpty || !GardenEncounterVision.IsConfirmDisabled(world.Frame, observed) ||
                    world.Input == null)
                    break;
                if (!observed.Decline.IsEmpty)
                {
                    found = true;
                    break;
                }
                var size = new Size(world.Frame.Width, world.Frame.Height);
                world.Input.Scroll(-2, new Point(size.Width * 2 / 3, size.Height * 47 / 100), size, world.Bounds, token);
                found = world.WaitUntil(read, 1800);
            }
            encounter = observed;
            return found && world.InputRouteVersion == route && !token.IsCancellationRequested &&
                !observed.Decline.IsEmpty && !observed.Confirm.IsEmpty;
        }

        private bool Blessing(int route)
        {
            if (!ClickBlessingPoint(965, 860, route))
                return false;
            world.Sleep(2000);
            if (!ClickBlessingPoint(700, 666, route))
                return false;
            world.Sleep(2000);
            if (!ClickBlessingPoint(1600, 900, route))
                return false;
            world.Sleep(2000);
            return true;
        }

        private bool ClickBlessingPoint(int x, int y, int route)
        {
            return world.Captured && world.ClickOnRoute(new Point(world.Frame.Width * x / 1920,
                world.Frame.Height * y / 1080), route);
        }

        private bool TryFinish()
        {
            int route = world.InputRouteVersion;
            Rectangle restart, back;
            if (!TryFindResult(out restart, out back))
                return false;
            world.Sleep(2000);
            token.ThrowIfCancellationRequested();
            if (!world.Tick() || world.InputRouteVersion != route)
            {
                Fail("确认乐园结算时游戏窗口已变更或画面不可用。");
                return true;
            }
            if (!TryFindResult(out restart, out back))
                return false;
            var lines = world.Read(0.373, 0.346, 0.859, 0.615);
            var texts = new List<string>(lines.Count);
            foreach (OcrLine line in lines)
                if (line.Score >= .65f)
                    texts.Add(line.Text);
            if (!GardenMath.IsFinished(texts))
            {
                bool sawRestart = false;
                if (!world.ClickMenuTransition(route, () =>
                {
                    Rectangle current, ignored;
                    if (!TryFindResult(out current, out ignored)) return Rectangle.Empty;
                    sawRestart = true;
                    return current;
                }, () => sawRestart && !GardenResultVisible(), 5000, 500))
                {
                    Fail("识别到乐园未达标，但未能发送重试点击。");
                    return true;
                }
                lastActivity = world.Now;
                return false;
            }
            Record("garden-result");
            bool sawBack = false;
            if (!world.ClickMenuTransition(route, () =>
            {
                Rectangle ignored, current;
                if (!TryFindResult(out ignored, out current)) return Rectangle.Empty;
                sawBack = true;
                return current;
            }, () => sawBack && !GardenResultVisible(), 5000, 500))
            {
                Fail("未能从结算画面返回周常乐园页面。");
                return true;
            }
            world.Sleep(1000);
            if (world.WaitUntil(() =>
            {
                Rectangle start;
                return world.InputRouteVersion == route && world.FindNear("garden_start_game", .80, out start);
            }, 5000))
            {
                if (!world.Key((char)0x1B, route: route))
                {
                    Fail("未能退出乐园入口画面。");
                    return true;
                }
                world.Sleep(800);
            }
            Rectangle quest = Rectangle.Empty;
            bool guide = world.WaitUntil(() => world.InputRouteVersion == route &&
                world.Vision.FindGuidebookQuest(world.Frame, out quest), 30000);
            token.ThrowIfCancellationRequested();
            Rectangle garden;
            if (!guide || !FindGardenEntry(out garden) && !SelectWeeklyTab(route, out garden))
            {
                Fail("退出后未确认回到周常乐园页面。");
                return true;
            }
            if (!WeeklyCompleted(route))
            {
                Fail("返回周常页面后未确认积分达到上限。");
                return true;
            }
            if (!ClaimWeeklyRewards(route) || !weeklyRewardVerified)
            {
                Fail("结算后的可领取奖励未得到确认。");
                return true;
            }
            token.ThrowIfCancellationRequested();
            CompletionVerified = true;
            Record("after-garden-verified");
            return true;
        }

        private bool GardenResultVisible()
        {
            Rectangle restart, back;
            return TryFindResult(out restart, out back);
        }

        private bool ClaimWeeklyRewards(int route, bool initial = false)
        {
            // The reference clicks the weekly reward area twice. Handle that
            // same maximum number of claims, but verify each receipt before
            // checking for a second item instead of blindly clicking twice.
            weeklyRewardVerified = false;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!VerifyWeeklyPage(route))
                    return false;
                Rectangle claim;
                if (!FindWeeklyClaim(out claim))
                {
                    // An empty claim OCR is not proof of receipt. At the weekly
                    // limit require an actual received or claimed indication.
                    if (!VerifyWeeklyPage(route))
                        return false;
                    if (!weeklyRewardVerified)
                        weeklyRewardVerified = HasWeeklyClaimedStatus();
                    if (WeeklyCompleted(route) && !weeklyRewardVerified)
                        return false;
                    return world.InputRouteVersion == route && world.Captured;
                }
                Record("garden-reward-before");
                HasUnverifiedConsumption = true;
                if (!world.ClickMenuButton(GardenMath.PreferredPoint(new Size(world.Frame.Width, world.Frame.Height),
                    claim, .927, .893), retry: true, route: route))
                    RunLog.Warn("周常乐园", "周常奖励点击未获确认，先等待领取回执");
                token.ThrowIfCancellationRequested();
                long clickedAt = world.Now;
                if (!world.WaitUntil(() => world.InputRouteVersion == route && HasWeeklyClaimConfirmation() &&
                    world.InputRouteVersion == route, 5000))
                    return false;
                HasUnverifiedConsumption = false;
                weeklyRewardVerified = true;
                Record("garden-reward-confirmed");
                if (HasWeeklyRewardReceipt() && (!world.Key((char)0x1B, route: route) ||
                    !world.WaitUntil(() => VerifyWeeklyPage(route), 5000)))
                    return false;
                int remainingDelay = (int)Math.Max(0, GardenMath.WeeklyClaimSettleMs(initial, attempt) - (world.Now - clickedAt));
                if (remainingDelay > 0)
                    world.Sleep(remainingDelay);
            }
            Rectangle remaining;
            return !FindWeeklyClaim(out remaining) && VerifyWeeklyPage(route);
        }

        private bool VerifyWeeklyPage(int route)
        {
            Rectangle entry;
            return world.InputRouteVersion == route && FindGardenEntry(out entry) && world.Captured &&
                world.InputRouteVersion == route;
        }

        private bool FindWeeklyClaim(out Rectangle button)
        {
            button = Rectangle.Empty;
            foreach (OcrLine line in world.Read(.83, .80, .99, .96))
            {
                string text = GardenMath.Normalize(line.Text);
                if (line.Score < .72f || (text != "领取" && text != "領取" && text != "一键领取" && text != "一鍵領取" &&
                    !string.Equals(text, "Claim", StringComparison.OrdinalIgnoreCase)))
                    continue;
                button = line.Box;
                return !button.IsEmpty;
            }
            return false;
        }

        private bool HasWeeklyClaimConfirmation()
        {
            return HasWeeklyRewardReceipt() || HasWeeklyClaimedStatus();
        }

        private bool HasWeeklyClaimedStatus()
        {
            Rectangle entry;
            if (!FindGardenEntry(out entry))
                return false;
            return world.Read(.83, .80, .99, .96).Any(line => line.Score >= .72f &&
                (GardenMath.Normalize(line.Text) == "已领取" || GardenMath.Normalize(line.Text) == "已領取" ||
                 string.Equals(GardenMath.Normalize(line.Text), "Claimed", StringComparison.OrdinalIgnoreCase)));
        }

        private bool HasWeeklyRewardReceipt()
        {
            return world.Read(.26, .13, .74, .38).Any(line => line.Score >= .72f &&
                (GardenMath.Normalize(line.Text) == "获得道具" || GardenMath.Normalize(line.Text) == "獲得道具" ||
                 string.Equals(GardenMath.Normalize(line.Text), "ItemsObtained", StringComparison.OrdinalIgnoreCase)));
        }

        private bool OpenWeeklyPage(out Rectangle garden)
        {
            garden = Rectangle.Empty;
            if (ownsWorld && !EnsureWorld())
                return false;
            var guide = new GuidebookNavigation(world, Report, GuideTab.Quest);
            if (!guide.Open(EnsureWorld))
                return false;
            int route = world.InputRouteVersion;
            world.Sleep(1000);
            return SelectWeeklyTab(route, out garden);
        }

        private bool SelectWeeklyTab(int route, out Rectangle garden)
        {
            garden = Rectangle.Empty;
            Rectangle weekly = Rectangle.Empty;
            Rectangle entry = Rectangle.Empty;
            Func<bool> destination = () => world.Vision.IsGuidebookQuestSelected(world.Frame) &&
                FindGardenEntry(out entry);
            bool found = world.WaitUntil(() => world.InputRouteVersion != route || destination() ||
                world.Vision.IsGuidebookQuestSelected(world.Frame) && FindWeeklyTab(out weekly), 5000);
            if (world.InputRouteVersion != route || token.IsCancellationRequested)
                return false;
            if (!entry.IsEmpty)
            {
                garden = entry;
                return true;
            }
            if (found && !weekly.IsEmpty)
            {
                bool selected = world.ClickMenuTransition(route, () =>
                {
                    Rectangle current;
                    return world.Vision.IsGuidebookQuestSelected(world.Frame) && FindWeeklyTab(out current)
                        ? current : Rectangle.Empty;
                }, destination, 5000, 500, tab => GardenMath.PreferredPoint(
                    new Size(world.Frame.Width, world.Frame.Height), tab, .343, .129));
                garden = entry;
                return selected;
            }

            // The calibrated fallback is limited to the selected quest guide;
            // another attempt requires freshly recognized button text.
            if (!world.Tick() || world.InputRouteVersion != route ||
                !world.Vision.IsGuidebookQuestSelected(world.Frame))
                return false;
            Point known = new Point((int)(world.Frame.Width * .343), (int)(world.Frame.Height * .129));
            if (!world.ClickMenuButton(known, retry: true, route: route)) return false;
            bool valid = world.WaitUntil(() => world.InputRouteVersion != route || destination(), 5000) &&
                world.InputRouteVersion == route && !token.IsCancellationRequested;
            garden = entry;
            return valid;
        }

        private bool FindWeeklyTab(out Rectangle tab)
        {
            tab = Rectangle.Empty;
            foreach (OcrLine line in world.Read(.22, .067, .53, .201))
            {
                string text = GardenMath.Normalize(line.Text);
                if (line.Score < .70f || (text != "周度游历" && text != "周度遊歷" && text != "周常" && text != "週常" && text != "每周任务" &&
                    text != "每週任務" && text != "周常任务" && !string.Equals(text, "Weekly", StringComparison.OrdinalIgnoreCase)))
                    continue;
                tab = line.Box;
                return !tab.IsEmpty;
            }
            return false;
        }

        private bool FindGardenEntry(out Rectangle entry)
        {
            entry = Rectangle.Empty;
            foreach (OcrLine line in world.Read(.10, .21, .57, .75))
            {
                string text = GardenMath.Normalize(line.Text);
                if (line.Score < .70f || (!text.Contains("乐园") && !text.Contains("樂園") &&
                    !text.Contains("游园") && !text.Contains("遊園") &&
                    text.IndexOf("Garden", StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                entry = line.Box;
                return !entry.IsEmpty;
            }
            return false;
        }

        private bool WeeklyCompleted(int route)
        {
            var lines = world.Read(0.102, 0.793, 0.284, 0.956);
            return world.InputRouteVersion == route && GardenMath.HasWeeklyLimit(lines);
        }

        private bool EnsureWorld()
        {
            return new WorldRecovery(world, Report).EnsureMain(30000, loginPending: ownsWorld) &&
                world.VerifiedWorld && !world.InRealm;
        }

        private void Report(string state)
        {
            RunLog.Info("周常乐园", string.Format("{0} · 已处理操作={1}", state, actions));
            progress.Report(new GardenReport
            {
                State = state, Actions = actions,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Finish()
        {
            RunLog.Info("周常乐园", string.Format("结束 · 操作数={0} · 已取消={1} · 已失败={2}",
                actions, token.IsCancellationRequested, Failed));
            progress.Report(new GardenReport
            {
                State = token.IsCancellationRequested ? "乐园任务已取消" :
                    (AlreadyCompleted ? "周常页面已显示 6000" : "周常乐园结算完成"),
                Actions = actions, Finished = true,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Fail(string message)
        {
            if (token.IsCancellationRequested)
                return;
            RunLog.Warn("周常乐园", "任务停止 · " + message);
            Record("after-garden-unverified");
            Failed = true;
            Failure = message;
            progress.Report(new GardenReport
            {
                State = message, Failure = message, Actions = actions, Finished = true,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Record(string stage)
        {
            if (recordEvidence != null && world.Captured)
                recordEvidence(stage, world.Frame);
        }

        public void Dispose()
        {
            if (ownsWorld) world.Dispose();
        }
    }
}
