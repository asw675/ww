using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace TideCombat
{
    internal sealed class SecondSolaRunner : IDisposable
    {
        private const string EventName = "第二索拉：未名之梦";
        private static readonly string[] RewardPages = { "限时奖励", "常驻奖励" };
        private static readonly string[] StoryChoices =
        {
            "之前在列车", "在列车上", "昨天在列车", "昨天在火车", "昨天在电车", "回到原本的世界", "我想回去", "是的",
            "休息一下再回去", "同时加入", "在天马集团之前买下", "综合素质", "学业与逻辑", "洛可可帮忙",
            "学生理事会", "回家吧", "开启下一周目", "开启新周目"
        };
        private static readonly string[] Outings =
        {
            "礼堂", "教学楼", "入学报到", "百年校庆", "期末综合测验", "期末测验", "预选赛", "暑期作战",
            "检定考试", "成人礼庆典", "成人礼", "全国大赛", "最终考察", "毕业典礼"
        };
        private static readonly Dictionary<string, string[]> Courses = new Dictionary<string, string[]>
        {
            { "理科教室", new[] { "课堂助教", "进阶理科课", "理科课程" } },
            { "文科教室", new[] { "进阶文科课", "文科课程" } },
            { "操场", new[] { "协助训练", "日常锻炼" } },
            { "家庭餐厅", new[] { "前台收银", "点单服务" } },
            { "中央广场", new[] { "校园清洁" } },
            { "学院商店", new[] { "店员帮忙", "商品销售", "货物整理" } },
            { "外事联络中心", new[] { "物资采购" } },
            { "学生理事会", new[] { "学院宣传", "文书处理" } },
            { "社团联合", new[] { "活动运营", "协助训练" } },
            { "海滨", new[] { "滨海采风" } }
        };
        private static readonly Dictionary<int, string[]> ExamAnswers = new Dictionary<int, string[]>
        {
            { 701, new[] { "判断.*(?:感受|立场)", "斜边的平方", "(?:两者|二者|都).*(?:一样|相同|相等)" } },
            { 702, new[] { "弦乐器", "表达.*(?:情感|思想)", "(?:舞台|表演).*(?:生动|统一|完整)" } },
            { 902, new[] { "^4倍$", "^25$", "^28$" } },
            { 903, new[] { "竖琴", "明暗关系", "低音提琴" } },
            { 1126, new[] { "入射角", "一手.*二手|一次资料.*二次资料" } },
            { 1127, new[] { "音高.*音量|声音的高低.*声音的大小", "打击乐器" } }
        };
        private static readonly Tuple<string, double, double>[] Tutorials =
        {
            Tuple.Create("点击【背包】", .247, .86), Tuple.Create("使用【日常补给】", .786, .886),
            Tuple.Create("在当前周目结束后不会消失", .58, .45), Tuple.Create("剧情选择会影响属性培养方向", .115, .77),
            Tuple.Create("此处会显示当前历程的", .938, .234), Tuple.Create("可以邀请伙伴外出或赠送礼物", .326, .5),
            Tuple.Create("可进行【档案存储】", .954, .067), Tuple.Create("点击【档案存储】", .382, .808),
            Tuple.Create("可查看个人信息与角色好感度", .246, .68), Tuple.Create("角色会发布【限时】任务", .655, .49),
            Tuple.Create("需要连续占用多天的日程", .516, .27), Tuple.Create("执行【休息】类行为后", .773, .15)
        };

        private readonly WorldSession world;
        private readonly CancellationToken token;
        private readonly IProgress<string> progress;
        private readonly Dictionary<string, string> rewardCounts = new Dictionary<string, string>();
        private readonly Dictionary<string, int> astriteRemaining = new Dictionary<string, int>();
        private readonly Dictionary<string, int> friendships = new Dictionary<string, int>();
        private readonly Dictionary<int, bool> commissionTabs = new Dictionary<int, bool>();
        private readonly HashSet<int> rewardCheckpoints = new HashSet<int>();
        private readonly HashSet<Tuple<int, string>> skippedCommissions = new HashSet<Tuple<int, string>>();
        private readonly HashSet<string> tiredRooms = new HashSet<string>();
        private int activeDate, shopBuys, lotteryIndex;
        private bool startingSemester, storyEnding, needShop, finishedLottery;
        private string currentRoom;

        internal SecondSolaRunner(CombatSettings settings, CancellationToken token, IProgress<string> progress)
        {
            this.token = token;
            this.progress = progress;
            world = new WorldSession(settings, token);
        }

        internal void Run()
        {
            if (!world.WaitUntil(() => IsEventScreen(Read()), 8000))
            {
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("请打开简体中文的「" + EventName + "」活动首页或正在进行的旅程。");
            }

            int completedJourneys = 0;
            bool entered = false;
            var recent = new Queue<string>(40);
            var idle = Stopwatch.StartNew();
            try
            {
                for (int step = 0; step < 10000; step++)
                {
                    token.ThrowIfCancellationRequested();
                    SecondSolaScreen screen = Read();
                    int date = screen.Date;
                    if (date != 0 && activeDate != date)
                    {
                        activeDate = date;
                        startingSemester = finishedLottery = false;
                        lotteryIndex = 0;
                        tiredRooms.Clear();
                        Report("学期进度 " + (date / 100).ToString("00") + "/" + (date % 100).ToString("00"));
                    }
                    if (Lobby(screen) != null)
                    {
                        screen = CollectRewards(screen, candidate => Lobby(candidate) != null);
                        if (FinishIfComplete())
                            return;
                        OcrLine resume = screen.ResumeJourney(() => world.Read(.64, .59, .97, .96));
                        if (entered)
                        {
                            if (!(storyEnding || resume == null && activeDate >= 1201) || completedJourneys >= 2)
                                throw new InvalidOperationException("旅程已返回活动首页，奖励仍待确认。 " + RewardSummary());
                            completedJourneys++;
                            storyEnding = finishedLottery = false;
                            friendships.Clear();
                            rewardCheckpoints.Clear();
                            commissionTabs.Clear();
                            skippedCommissions.Clear();
                            tiredRooms.Clear();
                            currentRoom = null;
                            recent.Clear();
                            resume = null;
                        }
                        startingSemester = resume == null;
                        Require(Click(resume ?? Lobby(screen), 2000), "未识别到开始或继续旅程按钮。");
                        entered = true;
                        idle.Restart();
                        continue;
                    }
                    entered = entered || date != 0;
                    string[] labels = screen.Lines.Where(line => SecondSolaScreen.Han.IsMatch(line.Text) ||
                        Regex.IsMatch(line.Text, @"\d/\d")).Select(line => line.Text).ToArray();
                    string signature = string.Join("|", labels);
                    bool transition = date == 0 && (labels.Length < 3 || Letter(screen));
                    bool stuck = labels.Contains("长按加速")
                        ? recent.Count >= 15 && recent.Skip(recent.Count - 15).All(value => value == signature)
                        : signature.Length > 0 && recent.Count(value => value == signature) >= (Lottery(screen) ? 35 : 7);
                    if (stuck)
                        throw new InvalidOperationException("活动界面重复出现 · " + string.Join("，", labels.Take(12)));
                    if (PlayStep(screen, date))
                    {
                        if (FinishIfComplete())
                            return;
                        if (recent.Count == 40) recent.Dequeue();
                        recent.Enqueue(signature);
                        idle.Restart();
                    }
                    else if (idle.ElapsedMilliseconds > (transition ? 125000 : 25000))
                        throw new InvalidOperationException("无法识别下一步 · " + string.Join("，", labels.Take(16)));
                    else
                        world.Sleep(350);
                }
                throw new InvalidOperationException("超过学期操作次数上限。");
            }
            catch (InvalidOperationException)
            {
                if (world.Captured)
                    Screenshots.Save(world.Frame, "second-sola", "unconfirmed", new Rectangle(0, 0, 1600, 900));
                throw;
            }
        }

        private bool PlayStep(SecondSolaScreen screen, int date)
        {
            if (Tutorial(screen))
                return true;
            if (screen.Find("本次共获取", .50, .20, .95, .98, true) != null)
                return Close(700);
            if (screen.Find("故事结局", .14, .05, .86, .65, true) != null)
                foreach (string name in new[] { "返回标题", "返回首页", "返回主界面" })
                    if (Click(screen.Find(name, .25, .70, .99, .98, true), 1000))
                    {
                        storyEnding = true;
                        return true;
                    }
            if (Click(screen.Find("进入下个月", .75, .85, .96, .95), 800))
                return true;
            OcrLine offer = CommissionOffer(screen);
            if (offer != null)
            {
                if (!CommissionUnmet(screen))
                {
                    Click(offer, 750);
                    if (CommissionOffer(Read()) == null)
                        return true;
                }
                skippedCommissions.Add(Tuple.Create(date == 0 ? activeDate : date, currentRoom));
                return Close();
            }
            if (Click(AttributeChoice(screen), 750) || Click(AttributeResult(screen), 650))
                return true;
            if (Lottery(screen))
            {
                PlayLottery(screen);
                return true;
            }
            if (Shop(screen))
            {
                BuyInShop();
                return true;
            }
            OcrLine save = ScheduleSave(screen);
            if (save != null)
            {
                SetSchedule(screen, save, date);
                return true;
            }
            if (Settings(screen))
            {
                CollectRewards(screen, Settings);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    Close(800);
                    if (world.WaitUntil(() => !Settings(Read()), 2000))
                        return true;
                }
                throw new InvalidOperationException("无法关闭学院手册。");
            }
            if (screen.Find("角色档案", 0, 0, .2, .12, true) != null ||
                screen.Find("档案管理", 0, 0, .2, .12, true) != null || RewardPages.Any(title => RewardPage(screen, title)) ||
                screen.Find("历程已结束", 0, .45, 1, .85, true) != null)
                return Close();
            if (Forum(screen))
            {
                Tap(.81, .876, 300);
                Tap(.857, .095, 550);
                return true;
            }
            if (screen.Find("课题清单", 0, 0, .2, .12, true) != null)
                return Click(screen.Find("领取", .8, .16, .95, .94, true), 650) || Close();
            if (screen.Find("倍速播放", .86, .89, .99, .99, true) != null)
                return Hold((char)0x11, 1400, 250);
            if (Click(StudyFocus(screen), 650) || Click(StoryOption(screen, false), 650) ||
                Click(StoryOption(screen, true), 650) || Click(screen.Find("确认执行", .35, .55, .74, .96), 650) ||
                Click(ConfirmEvent(screen), 600) || Click(screen.Find("确定", .32, .69, .72, .96), 550) ||
                Click(screen.Find("确认", .43, .77, .57, .92), 550) || Dialogue(screen))
                return true;
            if (Envelope(screen))
                return Tap(.5, .58, 800);
            if (screen.Find("新增物品", .06, .2, .33, .4, true) != null)
                return Hold(' ', 80, 550);
            if (screen.Find("日常补给") != null && screen.Find("珍藏道具") != null)
                return Close(550);
            return date != 0 && AdvanceDate(screen, date);
        }

        private bool AdvanceDate(SecondSolaScreen screen, int date)
        {
            bool weekend = screen.Find("周六", .035, .085, .11, .14) != null ||
                screen.Find("周日", .035, .085, .11, .14) != null;
            OcrLine schedule = screen.Find("日程安排", .70, .87, .87, .98, true);
            bool onMap = IsMap(screen);
            OcrLine outingButton = screen.In(.83, .88, .99, .98).FirstOrDefault(line =>
                SecondSolaScreen.Han.IsMatch(line.Text) && !line.Text.Contains("回家"));
            bool eventDay = Outings.Any(name => screen.Find(name, .83, .88, .99, .98, true) != null) ||
                !onMap && (screen.Find("就是今天啦", .72, .25, .96, .45, true) != null ||
                    schedule != null && outingButton != null && !outingButton.Text.Contains("外出"));
            bool outing = eventDay || screen.Find("外出", .83, .88, .99, .98, true) != null;
            OcrLine home = screen.Find("回家", .82, .88, .99, .98, true);
            int checkpoint = (date / 100) * 10 + (date % 100 - 1) / 7;
            if ((schedule != null || outing || home != null) && date >= 325 && rewardCheckpoints.Add(checkpoint))
                return Close();
            if (schedule != null && !eventDay && !weekend && screen.Find("本日暂无法进行", contains: true) == null)
                return Tap(.793, .855, 650);
            if (!onMap && screen.Find("阶段目标", .72, .11, .91, .22, true) != null)
            {
                Point? badge = screen.GoalBadge();
                if (badge.HasValue) return Tap(badge.Value, 650);
            }
            if (VisitLocation(screen))
                return true;
            int fatigue = screen.Fatigue().GetValueOrDefault();
            if ((!weekend || tiredRooms.Count > 0) && fatigue >= 75 &&
                Click(screen.Find("居家休息", .7, .4, .99, .98, true), 650))
                return true;
            if (outing)
                return Tap(.908, .919, 750);
            if (!onMap)
                return false;
            if (home != null && !weekend && fatigue >= 85)
                return Tap(.918, .868, 650);
            Tuple<OcrLine, string> marker = screen.MapMarker();
            if (marker.Item1 != null)
                return EnterLocation(marker.Item1);
            if (marker.Item2.Length > 0)
            {
                foreach (char key in marker.Item2) Hold(key, 550, 170);
                return true;
            }
            if (home == null)
                foreach (string name in Outings)
                {
                    OcrLine location = screen.In(.06, .18, .93, .92).FirstOrDefault(line =>
                        !(screen.X(line) > .71 && screen.Y(line) < .36) && !(screen.X(line) < .26 && screen.Y(line) < .24) &&
                        SecondSolaScreen.IsLocationLabel(line) && line.Text.Contains(name));
                    if (location != null) return EnterLocation(location);
                }
            IEnumerable<string> order = weekend ? (IEnumerable<string>)Courses.Keys.OrderBy(Friendship) : CourseOrder(screen);
            if (needShop && shopBuys < 3)
                order = new[] { "学院商店" }.Concat(order.Where(name => name != "学院商店"));
            foreach (string name in order.Where(name => !tiredRooms.Contains(name)))
            {
                OcrLine location = screen.Find(name, .06, .2, .93, .97);
                if (location != null) return EnterLocation(location);
            }
            return home != null && Tap(.918, .868, 650);
        }

        private SecondSolaScreen Read()
        {
            List<OcrLine> words = world.Read(0, 0, 1, .987);
            return new SecondSolaScreen(world.Frame, words);
        }

        private static OcrLine Lobby(SecondSolaScreen screen) { return screen.Find("开始新学期", .65, .55, .98, .96); }
        private static bool Settings(SecondSolaScreen screen)
        {
            return screen.Find("设置", .79, .03, .92, .11) != null && screen.Find("退出游戏", .52, .81, .71, .94) != null;
        }
        private static bool Forum(SecondSolaScreen screen) { return screen.Find("SOLAR-X", .12, .045, .29, .1, true) != null; }
        private static bool IsMap(SecondSolaScreen screen) { return screen.Find("个人背包", .48, .02, .66, .13, true) != null; }
        private static bool Lottery(SecondSolaScreen screen) { return screen.Find("特别刮奖", .76, .115, .995, .2, true) != null; }
        private static bool Shop(SecondSolaScreen screen)
        {
            return screen.Find("学院商店", 0, 0, .2, .12) != null && screen.Find("商店刷新", .85, .14, .99, .22, true) != null;
        }
        private static bool RewardPage(SecondSolaScreen screen, string title) { return screen.Find(title, 0, 0, .2, .13) != null; }
        private static bool Envelope(SecondSolaScreen screen) { return screen.Find("致:", .18, .22, .38, .33, true) != null; }
        private static bool Letter(SecondSolaScreen screen)
        {
            return screen.In(.47, .875, .53, .93).Any(line => Regex.IsMatch(line.Text, @"^[1-9]/[1-9]$"));
        }

        private bool IsEventScreen(SecondSolaScreen screen)
        {
            return Lobby(screen) != null || Settings(screen) || screen.Date > 0 || DialoguePanel(screen) ||
                ConfirmEvent(screen) != null || StudyFocus(screen) != null || StoryOption(screen, false) != null ||
                StoryOption(screen, true) != null || ScheduleSave(screen) != null || CommissionOffer(screen) != null ||
                AttributeChoice(screen) != null || AttributeResult(screen) != null || Lottery(screen) || Shop(screen) ||
                Forum(screen) || Envelope(screen) || Letter(screen) || BlackNarration(screen) ||
                RewardPages.Any(title => RewardPage(screen, title)) ||
                screen.Find("是否快速跳过剧情", .23, .3, .8, .58, true) != null ||
                screen.Find("好感度提升", .25, .24, .55, .36, true) != null ||
                screen.Find("新增物品", .06, .2, .33, .4, true) != null || screen.Find("属性提升", .35, .26, .65, .39) != null ||
                screen.Find("本月总结", .16, .03, .42, .14) != null || screen.Find("「理科课程」", .2, .34, .8, .59, true) != null ||
                screen.Find("课题清单", 0, 0, .2, .12, true) != null || screen.Find("档案管理", 0, 0, .2, .12, true) != null ||
                screen.Find("结束", .83, .90, .97, .98) != null;
        }

        private bool Tutorial(SecondSolaScreen screen)
        {
            OcrLine direction = screen.Find("前往【", .1, .2, .9, .79, true);
            if (direction != null)
                return Tap(screen.X(direction), Math.Min(.94, screen.Y(direction) + .17), 700);
            foreach (var hint in Tutorials)
                if (screen.Find(hint.Item1, contains: true) != null)
                    return Tap(hint.Item2, hint.Item3, 650);
            return screen.Find("A", .22, .75, .3, .86) != null && screen.Find("D", .70, .75, .78, .86) != null &&
                Tap(.963, .225, 550);
        }

        private OcrLine ConfirmEvent(SecondSolaScreen screen)
        {
            var prompts = new[] { "是否外出", "选择该方向后", "是否开始执行", "是否开始日程", "是否执行日程", "将结束今日的地图探索" };
            if (startingSemester)
                prompts = prompts.Concat(new[] { "是否开始新学期", "是否开启新学期" }).ToArray();
            return prompts.Any(name => screen.Find(name, .2, .34, .8, .59, true) != null)
                ? screen.Find("确认", .61, .75, .8, .9) : null;
        }

        private static OcrLine StudyFocus(SecondSolaScreen screen)
        {
            OcrLine academic = screen.Find("学业的追求与逻辑的锻炼", .46, .28, .92, .44, true);
            if (academic == null) return null;
            OcrLine overall = screen.Find("综合素质的全面成长与发展", .46, .68, .92, .84, true);
            return overall != null && screen.Find("后解锁", .46, .81, .92, .95, true) == null ? overall : academic;
        }

        private OcrLine StoryOption(SecondSolaScreen screen, bool paper)
        {
            if (IsMap(screen) || screen.Find("阶段目标", .72, .11, .91, .22, true) != null)
                return null;
            var choices = screen.Lines.Where(line =>
            {
                double x = line.Box.Left / (double)Math.Max(1, screen.Frame.Width), y = screen.Y(line);
                return (paper ? x > .28 && x < .67 && y > .53 && y < .88 :
                    x > .57 && x < .88 && y > .28 && y < .9) && SecondSolaScreen.HanOrNumber.IsMatch(line.Text);
            }).ToList();
            OcrLine key = paper ? screen.Find("F", .7, .5, .755, .88) : screen.Find("F", .4, .28, .53, .88);
            OcrLine selected = screen.Selected(choices, key, paper ? .271 : .422, paper ? .286 : .45,
                paper ? .047 : .052);
            if (selected == null) return null;
            OcrLine answer = ExamAnswer(choices, screen.Date == 0 ? activeDate : screen.Date);
            if (answer != null) return answer;
            if (!paper)
            {
                foreach (string phrase in new[] { "理事会组织的学院开放日" }.Concat(StoryChoices))
                {
                    OcrLine choice = choices.FirstOrDefault(line => line.Text.Contains(phrase));
                    if (choice != null && screen.Mean(screen.Region(.54, screen.Y(choice) - .009, .575, screen.Y(choice) + .009)) > 165)
                        return choice;
                }
            }
            return selected;
        }

        internal static OcrLine ExamAnswer(IEnumerable<OcrLine> choices, int date)
        {
            string[] patterns;
            if (!ExamAnswers.TryGetValue(date, out patterns)) return null;
            foreach (string pattern in patterns)
            {
                OcrLine answer = choices.FirstOrDefault(line => Regex.IsMatch(pattern: pattern, input: line.Text));
                if (answer != null) return answer;
            }
            return null;
        }

        private static bool DialoguePanel(SecondSolaScreen screen)
        {
            if (!screen.In(.24, .85, .76, .94).Any(line => SecondSolaScreen.Han.IsMatch(line.Text))) return false;
            Rectangle panel = screen.Region(.755, .95, .785, .975);
            return screen.LightFraction(panel, 155, true) > .70 && screen.LightFraction(panel, 90, false) > .015;
        }

        private static bool BlackNarration(SecondSolaScreen screen)
        {
            return screen.In(.15, .3, .85, .7).Any(line => SecondSolaScreen.Han.IsMatch(line.Text)) &&
                screen.Mean(screen.Region(0, 0, 1, 1), 3) < 25;
        }

        private bool Dialogue(SecondSolaScreen screen)
        {
            if (screen.Find("是否快速跳过剧情", .23, .3, .8, .58, true) != null)
            {
                if (screen.Find("本次登录不再提醒") != null) Tap(.431, .492, 120);
                return Tap(.694, .832, 600);
            }
            if (screen.Find("结束", .83, .90, .97, .98) != null || screen.Find("下一页", .83, .90, .97, .98) != null)
                return Tap(.894, .872, 600);
            var choices = screen.Lines.Where(line => line.Box.X > screen.Frame.Width * .67 &&
                line.Box.Y > screen.Frame.Height * .25 && line.Box.Y < screen.Frame.Height * .73 &&
                SecondSolaScreen.Han.IsMatch(line.Text)).ToList();
            bool panel = DialoguePanel(screen);
            if (choices.Count > 0 && (screen.Find("F", .65, .25, .70, .76) != null ||
                panel && screen.Find("Tab", .78, .05, .83, .11) != null))
            {
                OcrLine answer = ExamAnswer(choices, screen.Date == 0 ? activeDate : screen.Date);
                if (answer != null) return Click(answer, 650);
                foreach (string phrase in StoryChoices)
                    if (Click(choices.FirstOrDefault(line => line.Text.Contains(phrase)), 650))
                        return true;
                return Click(choices[0], 650);
            }
            if (screen.Find("长按加速", .83, .88, 1, .98, true) != null || BlackNarration(screen))
                return Close(550);
            return panel && Tap(.77, .955, 450);
        }

        private static OcrLine CommissionOffer(SecondSolaScreen screen)
        {
            return screen.Find("特别委托", .30, .13, .50, .24) == null ? null : screen.Find("接取委托", .50, .85, .80, .97);
        }

        private static bool CommissionUnmet(SecondSolaScreen screen)
        {
            return screen.In(.38, .45, .88, .60).Select(line => Regex.Match(line.Text, @"^(\d{1,8})/(\d{1,8})$"))
                .Any(match => match.Success && int.Parse(match.Groups[1].Value) < int.Parse(match.Groups[2].Value));
        }

        private static OcrLine AttributeChoice(SecondSolaScreen screen)
        {
            if (screen.Find("我的属性", .81, .105, .96, .19, true) == null) return null;
            var choices = screen.Lines.Where(line => line.Box.X > screen.Frame.Width * .14 &&
                line.Box.X < screen.Frame.Width * .435 && screen.Y(line) > .62 && screen.Y(line) < .891 &&
                SecondSolaScreen.Han.IsMatch(line.Text)).ToList();
            foreach (string phrase in new[] { "推理", "知识", "思考" })
            {
                OcrLine preferred = choices.FirstOrDefault(line => line.Text.Contains(phrase));
                if (preferred != null) return preferred;
            }
            return screen.Selected(choices, screen.Find("F", .126, .61, .195, .90), .175, .197, .05) ?? choices.FirstOrDefault();
        }

        private static OcrLine AttributeResult(SecondSolaScreen screen)
        {
            OcrLine button = screen.Find("完成", .43, .805, .58, .91);
            return button != null && screen.LightFraction(screen.Region(.38, .49, .64, .74), 180, true) > .78 ? button : null;
        }

        private void PlayLottery(SecondSolaScreen screen)
        {
            Rectangle balance = screen.Region(.788, .044, .833, .091);
            if (screen.Fraction(balance, (h, s, v) => s > 115 && v > 150) < .03)
                world.Sleep(500);
            else if (screen.Fraction(balance, (h, s, v) => s > 115 && v > 150 && (h < 10 || h > 170)) < .02)
            {
                finishedLottery = true;
                Tap(.942, .067, 750);
            }
            else if (world.Read(.827, .038, .896, .103).Any(line => Regex.IsMatch(line.Text, @"^[1-9]\d?$")))
            {
                int index = lotteryIndex++ % 28;
                Tap(.231 + .0683 * (index % 7), .38 + .121 * (index / 7), 850);
            }
            else
                world.Sleep(500);
        }

        private void BuyInShop()
        {
            PointF[] slots = { new PointF(.30f, .68f), new PointF(.30f, .4f), new PointF(.42f, .4f),
                new PointF(.54f, .4f), new PointF(.66f, .4f) };
            if (shopBuys < 3)
            {
                PointF item = slots[shopBuys];
                Tap(item.X, item.Y, 350);
                if (Click(Read().Find("购买", .75, .84, .95, .94), 700))
                {
                    SecondSolaScreen popup = Read();
                    if (!Click(popup.Find("确认", .43, .70, .80, .92) ?? popup.Find("确定", .32, .69, .72, .96), 600) &&
                        (popup.Find("获得", .4, .18, .6, .4) != null ||
                        popup.Find("点击空白处继续", .36, .82, .64, .96) != null))
                        Tap(.5, .89, 500);
                    shopBuys++;
                    return;
                }
            }
            needShop = false;
            Close();
        }

        private static int? Stat(SecondSolaScreen screen, string name)
        {
            OcrLine label = screen.Find(name, .035, .63, .10, .95, true);
            return label == null ? null : screen.Number(.12, screen.Y(label) - .02, .18, screen.Y(label) + .02);
        }

        private static string[] CourseOrder(SecondSolaScreen screen)
        {
            int? knowledge = Stat(screen, "知识"), art = Stat(screen, "艺术"), physique = Stat(screen, "体魄");
            if (!knowledge.HasValue || knowledge < 65)
                return new[] { "理科教室", "文科教室", "操场", "家庭餐厅" };
            if (art.HasValue && art < 70)
                return new[] { "文科教室", "操场", "理科教室", "家庭餐厅" };
            if (physique.HasValue && physique < 35)
                return new[] { "操场", "理科教室", "文科教室", "家庭餐厅" };
            return knowledge < 850 ? new[] { "理科教室", "家庭餐厅", "文科教室", "操场" } :
                new[] { "家庭餐厅", "理科教室", "操场", "文科教室" };
        }

        private bool EnterLocation(OcrLine location)
        {
            Click(location, 750);
            SecondSolaScreen after = Read();
            if (IsMap(after))
            {
                OcrLine same = after.Find(location.Text, .06, .2, .93, .97);
                if (same != null) Tap(after.X(same) - .02, after.Y(same) - .11, 650);
            }
            currentRoom = location.Text;
            return true;
        }

        private int Friendship(string room)
        {
            int level;
            return friendships.TryGetValue(room, out level) ? level : 0;
        }

        private bool VisitLocation(SecondSolaScreen screen)
        {
            if (screen.Find("居家休息", .7, .4, .99, .98, true) != null ||
                screen.Find("日程安排", .7, .87, .87, .98, true) != null ||
                screen.Find("阶段目标", .72, .11, .91, .22, true) != null)
                return false;
            string room = Courses.Keys.FirstOrDefault(name => screen.Find(name, .02, .14, .23, .23) != null);
            OcrLine slot = screen.Find("占用「", .72, .2, .99, .85, true);
            if (room == null && slot == null && screen.Find("赠送礼物", .19, .30, .40, .46, true) == null)
                return false;
            if (room != null) currentRoom = room;
            if (screen.Find("值过高", .40, .7, .65, .85, true) != null)
            {
                if (room != null) tiredRooms.Add(room);
                return Close();
            }
            if (room == "学院商店" && needShop && shopBuys < 3 && Click(screen.Find("学院商店", .70, .35, .95, .46), 750))
                return true;
            OcrLine lottery = screen.Find("特别刮奖", .70, .44, .98, .65, true);
            if (lottery != null)
                return finishedLottery ? Close(700) : Click(lottery, 850);
            if (room != null)
            {
                string[] levels = { "初遇", "相知", "熟识", "挚友" };
                for (int index = 0; index < levels.Length; index++)
                    if (screen.Find(levels[index], .70, .125, .93, .205, true) != null)
                    {
                        friendships[room] = index;
                        break;
                    }
            }
            OcrLine commission = skippedCommissions.Contains(Tuple.Create(activeDate, room))
                ? null : screen.Find("特别委托", .19, .23, .96, .80);
            if (Click(commission ?? screen.Find("邀约出行", .2, .3, .52, .66, true), 650))
                return true;
            int fatigue = screen.Fatigue().GetValueOrDefault();
            if (fatigue < 88)
            {
                string[] courses;
                if (room != null && Courses.TryGetValue(room, out courses))
                    foreach (string name in courses)
                        if (Click(screen.Find(name, .67, .22, .95, .87, true), 650))
                            return true;
                if (slot != null) return Tap(screen.X(slot) - .08, screen.Y(slot) + .04, 650);
            }
            else if (room != null)
                tiredRooms.Add(room);
            return Close();
        }

        private static OcrLine ScheduleSave(SecondSolaScreen screen)
        {
            if (screen.Find("疲劳度预览", .53, .035, .64, .112, true) == null) return null;
            return screen.Find("执行日程", .78, .85, .99, .98) ?? screen.Find("保存日程", .78, .85, .99, .98);
        }

        private bool ScheduleActivity(double tab, IEnumerable<string> activities)
        {
            Tap(tab, .15, 450);
            SecondSolaScreen cards = Read();
            if (Tutorial(cards))
            {
                Tap(tab, .15, 450);
                cards = Read();
            }
            foreach (string name in activities)
            {
                OcrLine card = cards.Find(name, .66, .2, .83, .74, true);
                if (card != null) return Tap(.896, cards.Y(card), 550);
            }
            return false;
        }

        private bool HasCommissionTab(int date)
        {
            bool exists;
            if (commissionTabs.TryGetValue(date, out exists))
                return exists;
            Tap(.654, .15, 450);
            SecondSolaScreen screen = Read();
            if (Tutorial(screen)) screen = Read();
            Rectangle tab = screen.Region(.64, .135, .67, .177);
            exists = screen.Fraction(tab, (h, s, v) => h >= 13 && h <= 33 && s > 140) > .10 ||
                screen.Find("待执行次数", .42, .36, .61, .52, true) != null;
            commissionTabs[date] = exists;
            if (exists) ScheduleCommission(screen);
            return exists;
        }

        private void ScheduleCommission(SecondSolaScreen screen)
        {
            if (screen.Find("周一", .045, .035, .24, .09, true) == null) return;
            List<OcrLine> cards = screen.Lines.Where(line => line.Box.X > screen.Frame.Width * .68 &&
                line.Box.X < screen.Frame.Width * .85 && line.Box.Y > screen.Frame.Height * .21 &&
                line.Box.Y < screen.Frame.Height * .70 && SecondSolaScreen.Han.Matches(line.Text).Count >= 4).ToList();
            Func<SecondSolaScreen, bool> appears = current =>
                cards.Any(card => current.Find(card.Text, .11, .24, .25, .70) != null);
            if (appears(screen)) return;
            foreach (OcrLine card in cards)
            {
                Tap(.895, screen.Y(card), 650);
                if (appears(Read())) return;
            }
        }

        private void SetSchedule(SecondSolaScreen screen, OcrLine save, int date)
        {
            var empty = screen.Lines.Where(line => line.Box.X > screen.Frame.Width * .11 &&
                line.Box.X < screen.Frame.Width * .37 && screen.Y(line) > .235 && screen.Y(line) < .70 &&
                line.Text.Contains("暂无安排")).ToList();
            if (empty.Count == 0)
            {
                Click(save, 800);
                return;
            }
            double offset = HasCommissionTab(date) ? .059 : 0;
            int? fatigue = screen.Fatigue();
            if (empty.Count == 5 && screen.Find("04/01", .045, .03, .22, .095, true) != null &&
                ScheduleActivity(.654, new[] { "文科课程" }))
                return;
            Click(empty[0], 180);
            if (!fatigue.HasValue) fatigue = Read().Fatigue();
            Require(fatigue.HasValue, "无法读取日程中的疲劳度预览。");
            if (fatigue >= 70 && ScheduleActivity(.773 + offset, new[] { "居家休息" })) return;

            int knowledge = ScheduleStat(screen, .215), thinking = ScheduleStat(screen, .284);
            int art = ScheduleStat(screen, .356), physique = ScheduleStat(screen, .426);
            int? budget = screen.Number(.80, .035, .86, .11);
            string[] work = Courses["家庭餐厅"], science = Courses["理科教室"];
            bool needsBadges = knowledge >= 65 && budget < 20000 &&
                !work.Any(job => screen.Find(job, .23, .235, .37, .70, true) != null);
            if ((needsBadges || knowledge >= 850 && thinking >= 600) && ScheduleActivity(.713 + offset, work)) return;
            IEnumerable<string> course = science;
            if (knowledge >= 65)
            {
                if (physique < 35) course = Courses["操场"].Concat(science);
                else if (art < 70) course = Courses["文科教室"].Concat(science);
            }
            Require(ScheduleActivity(.654 + offset, course), "找不到可加入日程的课程。");
        }

        private static int ScheduleStat(SecondSolaScreen screen, double x)
        {
            return screen.Number(x - .017, .88, x + .017, .94).GetValueOrDefault();
        }

        private SecondSolaScreen CollectRewards(SecondSolaScreen screen, Func<SecondSolaScreen, bool> parent)
        {
            foreach (OcrLine line in screen.Lines.Where(line => line.Box.X < screen.Frame.Width * .15))
            {
                Match count = Regex.Match(line.Text, @"^(\d{1,2})/(14|15)$");
                if (count.Success)
                    rewardCounts[RewardPages[int.Parse(count.Groups[2].Value) - 14]] = line.Text;
            }
            foreach (string title in RewardPages)
            {
                OcrLine button = screen.Find(title, 0, .42, .20, .9);
                if (button == null) continue;
                ClaimRewards(title, button);
                bool returned = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (world.WaitUntil(() => parent(screen = Read()), 2500))
                    {
                        world.Sleep(350);
                        SecondSolaScreen settled = Read();
                        if (parent(settled)) screen = settled;
                        returned = true;
                        break;
                    }
                    if (RewardPage(Read(), title)) Close(700);
                }
                Require(returned, "领取奖励后无法返回活动菜单。");
            }
            if (rewardCounts.Count > 0)
                Report("奖励进度 " + RewardSummary());
            return screen;
        }

        private void ClaimRewards(string title, OcrLine button)
        {
            bool opened = false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0)
                {
                    SecondSolaScreen screen = Read();
                    if (screen.Find("档案管理", 0, 0, .2, .12, true) != null)
                    {
                        Close();
                        screen = Read();
                    }
                    else if (Tutorial(screen)) screen = Read();
                    button = screen.Find(title, 0, .42, .20, .90);
                    if (button == null) continue;
                }
                Click(button, 550);
                if (world.WaitUntil(() => RewardPage(Read(), title), 2500))
                {
                    opened = true;
                    break;
                }
            }
            Require(opened, "无法打开" + title + "。");
            for (int attempt = 0; attempt < 8; attempt++)
            {
                SecondSolaScreen screen = Read();
                if (screen.Find("获得", .45, .19, .55, .36) != null ||
                    screen.Find("点击空白处继续", .36, .82, .64, .96) != null)
                {
                    Tap(.5, .89, 500);
                    continue;
                }
                if (Click(screen.Find("领取奖励", .81, .13, .95, .95), 650))
                    continue;
                if (RewardPage(screen, title) && HasRewardStatus(screen))
                {
                    astriteRemaining[title] = CountRemainingRewards(screen, title);
                    Close(700);
                    return;
                }
                world.Sleep(400);
            }
            throw new InvalidOperationException("无法确认" + title + "的领取状态。");
        }

        private static bool HasRewardStatus(SecondSolaScreen screen)
        {
            Rectangle status = screen.Region(.84, .19, .92, .27);
            return screen.Fraction(status, (h, s, v) => h >= 35 && h <= 75 && s > 80 && v > 150) > .015 ||
                screen.In(.80, .175, .95, .29).Any(line => Regex.IsMatch(line.Text, "进行中|已领取|已完成"));
        }

        private int CountRemainingRewards(SecondSolaScreen screen, string title)
        {
            bool limited = title == RewardPages[0];
            if (limited) needShop = screen.Find("累计购买商品", .35, .15, .75, .80, true) != null;
            return screen.In(.80, .15, .95, limited ? .80 : .92).Count(line =>
                line.Text.Contains("进行中") && screen.Fraction(screen.Region(.655, screen.Y(line) - .045,
                    .712, screen.Y(line) + .045), (h, s, v) => h >= 92 && h <= 122 && s >= 40 && v >= 150) > .03);
        }

        private string RewardSummary()
        {
            return string.Join("，", RewardPages.Where(rewardCounts.ContainsKey).Select(title => title + " " + rewardCounts[title]));
        }

        private bool FinishIfComplete()
        {
            if (astriteRemaining.Count != RewardPages.Length || astriteRemaining.Values.Any(count => count != 0))
                return false;
            Report("已核对限时和常驻奖励。 " + RewardSummary());
            return true;
        }

        private bool Click(OcrLine line, int delay)
        {
            if (line == null) return false;
            Require(world.Click(line.Box), "活动按钮的点击未发送。");
            world.Sleep(delay);
            return true;
        }

        private bool Tap(double x, double y, int delay)
        {
            Require(world.Click(x, y), "活动点击未发送。");
            world.Sleep(delay);
            return true;
        }

        private bool Tap(Point point, int delay)
        {
            Require(world.Click(point), "活动点击未发送。");
            world.Sleep(delay);
            return true;
        }

        private bool Close(int delay = 650) { return Tap(.954, .068, delay); }

        private bool Hold(char key, int holdMs, int delay)
        {
            Require(world.Key(key, holdMs), "活动按键未发送。");
            world.Sleep(delay);
            return true;
        }

        private void Require(bool condition, string message)
        {
            token.ThrowIfCancellationRequested();
            if (!condition) throw new InvalidOperationException(message);
        }

        private void Report(string message)
        {
            RunLog.Info(EventName, message);
            progress.Report(message);
        }

        public void Dispose() { world.Dispose(); }
    }
}
