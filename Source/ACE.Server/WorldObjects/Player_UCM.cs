using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using System;
using ACE.Common.Extensions;

namespace ACE.Server.WorldObjects
{
    public enum UcmStage
    {
        None,
        Basic,
        BotDetection
    }
    public enum UCMCheckFailReason
    {
        WrongAnswer,
        TimedOut,
        LoggedOut,
        SentByAdmin,
        SuspiciousSpeed,
    }

    /// <summary>
    /// Manages the logic for random "focus checks" that can occur for players in certain areas after combat.
    /// This class is thread-safe.
    /// </summary>
    public class UCMChecker(Player playerInstance)
    {
        private readonly System.Threading.Lock _lock = new();
        private readonly Player Self = playerInstance;
        private Random RNG { get; } = new();
        private bool IsChecking => CurrentStage != UcmStage.None;
        private DateTime Timeout { get; set; } = DateTime.UnixEpoch;
        private DateTime LastTickTime { get; set; } = DateTime.UtcNow;
        private DateTime LastUCMCheckTime { get; set; } = DateTime.UnixEpoch;
        /// <summary>Context id of the active UCM <see cref="ConfirmationType.Yes_No"/> dialog, if any.</summary>
        private uint? ActiveUcmConfirmationContextId { get; set; }
        /// <summary>UTC time the current prompt was shown (for audit / bot heuristics).</summary>
        private DateTime? UcmPromptStartedAtUtc { get; set; }
        /// <summary>The current active check stage.</summary>
        private UcmStage CurrentStage { get; set; } = UcmStage.None;
        /// <summary>
        /// True when the active <see cref="UcmStage.Basic"/> prompt is the advanced math variant.
        /// Cleared on escalation, so a bot detection failure still carries the full sentence.
        /// </summary>
        private bool CurrentQuestionIsAdvanced { get; set; }

        /// <summary>
        /// Determines whether a check operation is currently in progress.
        /// </summary>
        public bool IsCheckInProgress()
        {
            using var scope = _lock.EnterScope();
            return IsChecking;
        }

        /// <summary>
        /// Attempts to start a UCM check and returns true if it was started successfully.
        /// </summary>
        public bool Start()
        {
            using var scope = _lock.EnterScope();
            return StartLocked();
        }

        /// <summary>
        /// Attempts to start a UCM check and returns true if it was started successfully.
        /// The lock must be held to call this method.
        /// </summary>
        private bool StartLocked()
        {
            if (IsChecking) return false;

            // While the toggle is on, the hard question replaces the simple one entirely.
            bool isAdvanced = ServerConfig.ucm_advanced_math_enabled.Value;
            bool isRightAnswerForm;
            string question;

            if (isAdvanced)
            {
                question = BuildAdvancedQuestion(out isRightAnswerForm);
            }
            else
            {
                // Generate math problem using single digits (0-9)
                int a = RNG.Next(0, 10);
                int b = RNG.Next(0, 10);
                int correctAnswer = a + b;

                isRightAnswerForm = RNG.Next(0, 2) == 0;
                int shownAnswer = isRightAnswerForm ? correctAnswer : (correctAnswer + 1);
                question = $"Is {a} + {b} = {shownAnswer}?";
            }

            TimeSpan timeout = TimeSpan.FromSeconds(ServerConfig.ucm_check_timeout_seconds.Value);
            string message = $"{question}\n\nYou have {timeout.GetFriendlyLongString()} to respond.";
            var ucmBasicCheckConfirmation = new Confirmation_Custom(Self.Guid, (response, timedOut) =>
            {
                using var scope = _lock.EnterScope();
                if (!IsChecking || CurrentStage != UcmStage.Basic || !UcmPromptStartedAtUtc.HasValue)
                    return;

                if (timedOut)
                {
                    FailActiveCheckLocked(UCMCheckFailReason.TimedOut);
                }
                else if (response == isRightAnswerForm)
                {
                    // If extremely fast, trigger the random check.
                    TimeSpan responseTime = DateTime.UtcNow - (UcmPromptStartedAtUtc ?? DateTime.MaxValue);
                    if (responseTime < TimeSpan.FromSeconds(1))
                    {
                        PlayerManager.BroadcastToAuditChannel(Self, $"[UCM Check] Player {Self.Name} was selected for an advanced UCM check (responded in {responseTime.GetFriendlyString() ?? "unknown time"}).");
                        StartAdvancedCheckLocked(UcmStage.BotDetection);
                        return;
                    }
                    // 5% chance to be randomly selected regardless of response time on basic check
                    if (RNG.NextDouble() < 0.05)
                    {
                        PlayerManager.BroadcastToAuditChannel(Self, $"[UCM Check] Player {Self.Name} was selected for an advanced UCM check (randomly chosen).");
                        StartAdvancedCheckLocked(UcmStage.BotDetection);
                        return;
                    }

                    PassActiveCheckLocked();
                }
                else
                {
                    FailActiveCheckLocked(UCMCheckFailReason.WrongAnswer);
                }
            });
            bool enqueued = Self.ConfirmationManager.EnqueueSend(ucmBasicCheckConfirmation, message, timeout.TotalSeconds);

            if (!enqueued) return false;

            var startUtc = DateTime.UtcNow;
            ActiveUcmConfirmationContextId = ucmBasicCheckConfirmation.ContextId;
            CurrentStage = UcmStage.Basic;
            CurrentQuestionIsAdvanced = isAdvanced;
            UcmPromptStartedAtUtc = startUtc;
            LastUCMCheckTime = startUtc;
            Timeout = startUtc.Add(timeout);
            return true;
        }

        /// <summary>
        /// If a check is active, passes it.
        /// </summary>
        public void PassActiveCheck()
        {
            using var scope = _lock.EnterScope();
            PassActiveCheckLocked();
        }

        /// <summary>
        /// If a check is active, finishes it.
        /// The lock must be held to call this method.
        /// </summary>
        private void PassActiveCheckLocked()
        {
            if (!IsChecking) return;

            var passed = Self.GetProperty(PropertyInt.TimesUcmCheckPassed) ?? 0;
            if (passed < int.MaxValue)
                Self.SetProperty(PropertyInt.TimesUcmCheckPassed, passed + 1);

            int passCount = Self.QuestManager.Stamp("focus_test_passed");
            if (passCount >= 5) Self.QuestManager.StampFirst("focus_test_not_a_bot");
            if (passCount >= 10) Self.QuestManager.StampFirst("focus_test_staying_awake");
            if (passCount >= 20) Self.QuestManager.StampFirst("focus_test_actually_playing");
            if (passCount >= 50) Self.QuestManager.StampFirst("focus_test_unshakable_focus");

            Self.Session.Network.EnqueueSend(new GameMessageSystemChat("You passed the focus test.", ChatMessageType.Broadcast));
            var passElapsed = FormatUcmResponseSeconds(UcmPromptStartedAtUtc);
            var stageInfo = CurrentStage == UcmStage.Basic ? "" : $" (Stage: {CurrentStage})";
            PlayerManager.BroadcastToAuditChannel(Self, $"[UCM Check] Player {Self.Name} passed UCM check{stageInfo} at {Self.Location}.{passElapsed}");
            EndActiveCheckLocked();
        }

        /// <summary>
        /// If a check is active, fails it.
        /// </summary>
        public void FailActiveCheck(UCMCheckFailReason reason)
        {
            using var scope = _lock.EnterScope();
            FailActiveCheckLocked(reason);
        }

        /// <summary>
        /// If a check is active, fails it.
        /// The lock must be held to call this method.
        /// </summary>
        private void FailActiveCheckLocked(UCMCheckFailReason reason)
        {
            if (!IsChecking) return;

            // Tick timeout: confirmation is still registered; dismiss so the scheduled EnqueueAbort does not send a duplicate timeout message.
            if (ActiveUcmConfirmationContextId is uint ctx)
                Self.ConfirmationManager.TryDismissConfirmation(ConfirmationType.Yes_No, ctx);

            // Captured before EndActiveCheckLocked clears the stage state.
            bool advancedMathFail = CurrentStage == UcmStage.Basic && CurrentQuestionIsAdvanced;

            // The focus test stamps feed titles and leaderboards, so the joke question does not award them.
            if (CurrentStage == UcmStage.Basic && !advancedMathFail)
            {
                _ = reason switch
                {
                    UCMCheckFailReason.WrongAnswer => Self.QuestManager.StampFirst("focus_test_math_is_hard"),
                    UCMCheckFailReason.TimedOut => Self.QuestManager.StampFirst("focus_test_bathroom_break"),
                    UCMCheckFailReason.LoggedOut => Self.QuestManager.StampFirst("focus_test_tactical_dc"),
                    _ => 0
                };
            }

            string message = advancedMathFail ? PickAdvancedMathInsult() : "You failed the focus test and have been punished!";
            Self.Session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.Broadcast));

            var failElapsed = FormatUcmResponseSeconds(UcmPromptStartedAtUtc);
            var stageTag = advancedMathFail ? $"{CurrentStage} / AdvancedMath" : CurrentStage.ToString();
            PlayerManager.BroadcastToAuditChannel(Self, $"[UCM Check] Player {Self.Name} failed UCM check ({GetUCMCheckFailReasonString(reason)}, Stage: {stageTag}) at {Self.Location}.{failElapsed}");
            EndActiveCheckLocked();

            if (advancedMathFail)
            {
                Self.SendToJail(TimeSpan.FromSeconds(ServerConfig.ucm_advanced_math_jail_seconds.Value));
                PlayerManager.BroadcastToAll(new GameMessageSystemChat($"{Self.Name} could not do basic math and has been jailed.", ChatMessageType.Broadcast));
            }
            else
            {
                Self.SendToJail();
                PlayerManager.BroadcastToAll(new GameMessageSystemChat($"{Self.Name} failed a focus check and was sent to jail!", ChatMessageType.Broadcast));
            }
        }

        /// <summary>
        /// Performs steps to finish the check regardless of pass/fail/abort.
        /// While typically called by pass/fail, it can also be used directly to abort without a pass/fail occurring.
        /// </summary>
        private void EndActiveCheckLocked()
        {
            CurrentStage = UcmStage.None;
            CurrentQuestionIsAdvanced = false;
            ActiveUcmConfirmationContextId = null;
            UcmPromptStartedAtUtc = null;
        }

        /// <summary>
        /// Starts a specific advanced check stage.
        /// </summary>
        private void StartAdvancedCheckLocked(UcmStage stageToStart)
        {
            if (!IsChecking) return;

            // Update state. Escalation leaves the joke question behind: bot detection is a genuine
            // check, so a failure here carries the full sentence even while advanced math is enabled.
            CurrentStage = stageToStart;
            CurrentQuestionIsAdvanced = false;
            UcmPromptStartedAtUtc = DateTime.UtcNow;
            TimeSpan timeout = TimeSpan.FromSeconds(30); // They responded too quickly at first, so... fixed shorter interval.
            Timeout = DateTime.UtcNow.Add(timeout);

            string message = "";
            bool expectedAnswer = false;

            switch (stageToStart)
            {
                case UcmStage.BotDetection:
                    message = "Are you sure you're not a bot?\n\nAttempting to macro these prompts carries higher punishments.";
                    expectedAnswer = true; // Yes, I'm not a bot.
                    break;
            }

            message += $"\n\nYou have {timeout.GetFriendlyLongString()} to respond.";

            UcmStage stageForCheck = stageToStart;
            var nextAdvancedCheckConfirmation = new Confirmation_Custom(Self.Guid, (response, timedOut) =>
            {
                using var scope = _lock.EnterScope();
                if (!IsChecking || CurrentStage != stageForCheck || !UcmPromptStartedAtUtc.HasValue)
                    return;

                if (timedOut)
                {
                    FailActiveCheckLocked(UCMCheckFailReason.TimedOut);
                }
                else if (response == expectedAnswer)
                {
                    // Answering too quickly on an advanced check is an automatic failure.
                    double secondsElapsed = (DateTime.UtcNow - UcmPromptStartedAtUtc.Value).TotalSeconds;
                    if (secondsElapsed < 1.0)
                    {
                        FailActiveCheckLocked(UCMCheckFailReason.SuspiciousSpeed);
                        return;
                    }

                    // They got it right, so advance to the next stage.
                    switch (CurrentStage)
                    {
                        case UcmStage.BotDetection:
                            PassActiveCheckLocked();
                            break;
                        default:
                            break;
                    }
                }
                else
                {
                    FailActiveCheckLocked(UCMCheckFailReason.WrongAnswer);
                }
            });

            // Dismiss the previous confirmation context if any (the math one already should be dismissed or completed)
            if (ActiveUcmConfirmationContextId.HasValue)
                Self.ConfirmationManager.TryDismissConfirmation(ConfirmationType.Yes_No, ActiveUcmConfirmationContextId.Value);

            if (Self.ConfirmationManager.EnqueueSend(nextAdvancedCheckConfirmation, message, timeout.TotalSeconds))
            {
                ActiveUcmConfirmationContextId = nextAdvancedCheckConfirmation.ContextId;
            }
            else
            {
                // Unexpectedly failed to start, so abort it.
                EndActiveCheckLocked();
            }
        }

        /// <summary>
        /// Handles random starts of checks and timing out of active checks. For use by Player.Tick(). 
        /// Also enforces the active UCM Jails.
        /// </summary>
        public void Tick()
        {
            using var scope = _lock.EnterScope();
            DateTime now = DateTime.UtcNow;
            TimeSpan sinceLastTick = now - LastTickTime;
            LastTickTime = now;

            if (!IsChecking)
            {
                // Player must have been in combat recently.
                if (now > Self.LastCombatActionTime.AddSeconds(ServerConfig.ucm_check_combat_eligibility_seconds.Value)) return;

                // If not past the cooldown period since the last check, not eligible for a check.
                if (now < LastUCMCheckTime.AddSeconds(ServerConfig.ucm_check_cooldown_seconds.Value)) return;

                // If not in a valid landblock, not eligible for a check.
                ushort landblockId = (ushort)Self.Location.Landblock;
                if (!LandblockCollections.ValleyOfDeathLandblocks.Contains(landblockId) && !LandblockCollections.ThaelarynIslandLandblocks.Contains(landblockId)) return;

                // Calculate the true probability of at least one trigger occurring over the elapsed time.
                // Formula: 1 - (1 - chancePerSecond)^TotalSeconds.
                // Note: ucm_check_spawn_chance is configured as a percentage between 0 and 1.
                double chancePerSec = Math.Clamp(ServerConfig.ucm_check_spawn_chance.Value, 0, 1);
                double probOverElapsed = 1.0 - Math.Pow(1.0 - chancePerSec, sinceLastTick.TotalSeconds);
                if (RNG.NextDouble() < probOverElapsed) StartLocked();
                return;
            }

            if (now > Timeout)
            {
                FailActiveCheckLocked(UCMCheckFailReason.TimedOut);
                return;
            }
        }

        /// <summary>
        /// Returns a human-readable string for the given jail reason, for use in audit messages.
        /// </summary>
        /// <param name="reason"></param>
        /// <returns></returns>
        public string GetUCMCheckFailReasonString(UCMCheckFailReason reason) => reason switch
        {
            UCMCheckFailReason.WrongAnswer => "selected incorrectly",
            UCMCheckFailReason.TimedOut => "timed out",
            UCMCheckFailReason.LoggedOut => "logged out",
            UCMCheckFailReason.SentByAdmin => "sent by admin",
            UCMCheckFailReason.SuspiciousSpeed => "responded too fast",
            _ => "unknown reason",
        };

        /// <summary>
        /// Sent to a player who fails the advanced math question. The joke is that the question
        /// they just failed was anything but basic.
        /// </summary>
        private static readonly string[] AdvancedMathInsults =
        [
            "You failed. That was BASIC math, friend. Basic. Sixty seconds to reflect on it.",
            "Wrong. Somewhere a math teacher just felt a chill.",
            "Incorrect. Perhaps stick to hitting things with a stick.",
            "Failed. Your abacus is missing beads.",
            "Nope. Basic numbers are clearly not for everyone.",
            "Wrong answer. Counting to ten must be quite the adventure for you.",
            "Incorrect. Do not fret, basic arithmetic is only most of mathematics.",
            "Failed. A drudge could have worked that one out, and drudges cannot even count.",
        ];

        private string PickAdvancedMathInsult() => AdvancedMathInsults[RNG.Next(0, AdvancedMathInsults.Length)];

        /// <summary>
        /// Builds an integer-exact math question that almost nobody can answer in their head.
        /// The value shown is the true answer half the time, so a blind guess stays a coin flip.
        /// </summary>
        /// <param name="shownIsCorrect">True when the value displayed is the true answer.</param>
        private string BuildAdvancedQuestion(out bool shownIsCorrect)
        {
            long correct;
            string expression;

            switch (RNG.Next(0, 7))
            {
                case 0:
                {
                    long x = RNG.Next(1000, 10000);
                    long y = RNG.Next(1000, 10000);
                    correct = x * y;
                    expression = $"{x} * {y}";
                    break;
                }
                case 1:
                {
                    long b = RNG.Next(3, 20);
                    int e = RNG.Next(7, 18);
                    correct = ModPow(b, e, 1000);
                    expression = $"{b} ^ {e} mod 1000";
                    break;
                }
                case 2:
                {
                    int n = RNG.Next(11, 18);
                    correct = Factorial(n);
                    expression = $"{n}!";
                    break;
                }
                case 3:
                {
                    int n = RNG.Next(40, 60);
                    correct = Fibonacci(n);
                    expression = $"the {Ordinal(n)} Fibonacci number";
                    break;
                }
                case 4:
                {
                    int n = RNG.Next(20, 34);
                    int k = RNG.Next(6, 13);
                    correct = Binomial(n, k);
                    expression = $"{n} choose {k}";
                    break;
                }
                case 5:
                {
                    int limit = RNG.Next(150, 400);
                    correct = SumOfPrimesBelow(limit);
                    expression = $"the sum of all primes below {limit}";
                    break;
                }
                default:
                {
                    long x = RNG.Next(200, 900);
                    long y = RNG.Next(20, 90);
                    long d = RNG.Next(3, 13);
                    long product = x * y;
                    long c = RNG.Next(50, 1000);
                    c += ((product - c) % d + d) % d;    // nudge c so the division comes out exact
                    correct = (product - c) / d;
                    expression = $"({x} * {y} - {c}) / {d}";
                    break;
                }
            }

            shownIsCorrect = RNG.Next(0, 2) == 0;
            long shown = correct;

            if (!shownIsCorrect)
            {
                // Shift by roughly a thousandth so the wrong value keeps the same digit count and
                // cannot be spotted by shape alone.
                long magnitude = Math.Max(1, correct / 1000);
                long delta = RNG.NextInt64(1, magnitude + 1);
                shown = RNG.Next(0, 2) == 0 ? correct + delta : correct - delta;
                if (shown < 0 || shown == correct) shown = correct + delta;
            }

            return $"Is {expression} = {shown}?";
        }

        private static long ModPow(long value, int exponent, long modulus)
        {
            long result = 1;
            long b = value % modulus;
            for (int i = 0; i < exponent; i++)
                result = result * b % modulus;
            return result;
        }

        private static long Factorial(int n)
        {
            long result = 1;
            for (int i = 2; i <= n; i++)
                result *= i;
            return result;
        }

        private static long Fibonacci(int n)
        {
            long a = 0, b = 1;
            for (int i = 0; i < n; i++)
                (a, b) = (b, a + b);
            return a;
        }

        private static long Binomial(int n, int k)
        {
            if (k > n - k) k = n - k;
            long result = 1;
            // Each step holds an exact binomial coefficient, so the division never truncates.
            for (int i = 0; i < k; i++)
                result = result * (n - i) / (i + 1);
            return result;
        }

        private static long SumOfPrimesBelow(int limit)
        {
            long sum = 0;
            for (int candidate = 2; candidate < limit; candidate++)
            {
                bool isPrime = true;
                for (int divisor = 2; divisor * divisor <= candidate; divisor++)
                {
                    if (candidate % divisor != 0) continue;
                    isPrime = false;
                    break;
                }
                if (isPrime) sum += candidate;
            }
            return sum;
        }

        private static string Ordinal(int n)
        {
            if (n % 100 is >= 11 and <= 13) return $"{n}th";
            return (n % 10) switch
            {
                1 => $"{n}st",
                2 => $"{n}nd",
                3 => $"{n}rd",
                _ => $"{n}th",
            };
        }

        /// <summary>Audit suffix: seconds from prompt shown to outcome (UTC).</summary>
        private static string FormatUcmResponseSeconds(DateTime? promptStartedUtc)
        {
            if (!promptStartedUtc.HasValue)
                return string.Empty;

            TimeSpan elapsed = DateTime.UtcNow - promptStartedUtc.Value;
            return $" Response time: {elapsed.GetFriendlyString()}.";
        }
    }

    public partial class Player
    {
        public UCMChecker UCMChecker { get; }
    }
}
