
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using UnityEngine;
using System.Collections.Generic;

namespace SPTBotCounter
{
    public enum TrackedBotType { Pmc, Scav, Rogue, Raider, Boss, Ignore }

    public struct BossRenderInfo
    {
        public string Text;
        public Color Color;
    }

    internal class ConfigurationManagerAttributes
    {
        public int? Order;
    }

    [BepInPlugin("com.spt.botcounter", "BotCounter", "1.8.0")]
    public class BotCounterPlugin : BaseUnityPlugin
    {
        private GUIStyle _guiStyle;

    
        private ConfigEntry<bool> _enableMod;
        private ConfigEntry<bool> _showPmc, _showScav, _showBoss, _showRogue, _showRaider, _showNearest, _hideZeroCounts, _bossHealthColors;

     
        private ConfigEntry<int> _fontSize, _offsetRight, _offsetTop, _opacity, _refreshRate;
        private ConfigEntry<Color> _globalColor;

      
        private bool _inRaid = false;
        private float _nextUpdate = 0f;

     
        private int _cPmc, _cScav, _cRogue, _cRaider;
        private float _distPmc = -1f, _distScav = -1f, _distRogue = -1f, _distRaider = -1f;

      
        private readonly Dictionary<int, TrackedBotType> _allSeenBots = new Dictionary<int, TrackedBotType>();
        private readonly HashSet<int> _deadBotIds = new HashSet<int>();

      
        private string _tPmc = "", _tScav = "", _tRogue = "", _tRaider = "", _tBossTitle = "";
        private readonly List<BossRenderInfo> _tBosses = new List<BossRenderInfo>();

        private void Awake()
        {
            // --- 1. Visibility Section ---
            _enableMod = Config.Bind("1. Visibility", "Enable Mod", true,
                new ConfigDescription("Turn the entire mod on or off.", null, new ConfigurationManagerAttributes { Order = 999 }));

            _showPmc = Config.Bind("1. Visibility", "Show PMCs", true, "Show the amount of PMCs on the map.");
            _showScav = Config.Bind("1. Visibility", "Show Scavs", true, "Show the amount of Scavs on the map.");
            _showRogue = Config.Bind("1. Visibility", "Show Rogues", true, "Show the amount of Rogues on the map.");
            _showRaider = Config.Bind("1. Visibility", "Show Raiders", true, "Show the amount of Raiders on the map.");
            _showBoss = Config.Bind("1. Visibility", "Show Bosses", true, "Show active bosses and their names.");

            _hideZeroCounts = Config.Bind("1. Visibility", "Hide Zero Counts", false, "Hides a category completely if there are 0 bots of that type alive.");
            _showNearest = Config.Bind("1. Visibility", "Show Distance", true, "Shows the distance to the closest bot of each category.");
            _bossHealthColors = Config.Bind("1. Visibility", "Boss Health Colors", true, "Changes the color of boss names based on their remaining health.");

            // --- 2. Display Section ---
            _refreshRate = Config.Bind("2. Display", "Refresh Rate (Seconds)", 1,
                new ConfigDescription("How often the distances and counters update. Higher = better performance.", new AcceptableValueRange<int>(1, 5)));

            _fontSize = Config.Bind("2. Display", "Font Size", 16,
                new ConfigDescription("Adjust the text size.", new AcceptableValueRange<int>(8, 50)));

            _offsetRight = Config.Bind("2. Display", "Offset Right", 10,
                new ConfigDescription("Move the UI away from the right edge of the screen.", new AcceptableValueRange<int>(0, 1000)));

            _offsetTop = Config.Bind("2. Display", "Offset Top", 70,
                new ConfigDescription("Move the UI down from the top edge of the screen.", new AcceptableValueRange<int>(0, 1000)));

            _opacity = Config.Bind("2. Display", "Opacity (%)", 100,
                new ConfigDescription("Set the transparency of the text (10% = nearly invisible, 100% = solid).", new AcceptableValueRange<int>(10, 100)));

            // --- 3. Colors Section ---
            _globalColor = Config.Bind("3. Colors", "Global Text Color", new Color(0f, 0.8f, 0f, 1f), "The default color for all normal text entries.");
        }

        private void Update()
        {
            bool currentlyInRaid = Singleton<IBotGame>.Instantiated && Singleton<GameWorld>.Instance?.MainPlayer != null;

            if (currentlyInRaid && !_inRaid)
            {
                _inRaid = true;
                _allSeenBots.Clear();
                _deadBotIds.Clear();
            }
            else if (!currentlyInRaid && _inRaid)
            {
                _inRaid = false;
            }

            if (_inRaid && _enableMod.Value && Time.time >= _nextUpdate)
            {
                ScanBots();
                BuildUIStrings();
                _nextUpdate = Time.time + _refreshRate.Value;
            }
        }

        private void ScanBots()
        {
            var controller = Singleton<IBotGame>.Instance?.BotsController;
            if (controller == null) return;

            _cPmc = _cScav = _cRogue = _cRaider = 0;
            float minPmc = float.MaxValue, minScav = float.MaxValue, minRogue = float.MaxValue, minRaider = float.MaxValue;
            Vector3 pPos = Singleton<GameWorld>.Instance.MainPlayer.Position;

            _tBosses.Clear();
            int aliveBosses = 0;

            foreach (var bot in controller.Bots.BotOwners)
            {
                if (bot?.Profile?.Info?.Settings == null) continue;

                int id = bot.Id;

                if (!_allSeenBots.TryGetValue(id, out TrackedBotType type))
                {
                    type = EvaluateBotType(bot);
                    _allSeenBots[id] = type;
                }

                if (bot.HealthController == null || !bot.HealthController.IsAlive)
                {
                    if (!_deadBotIds.Contains(id)) _deadBotIds.Add(id);
                    continue;
                }

                if (type == TrackedBotType.Boss)
                {
                    aliveBosses++;

                    float bDist = Vector3.Distance(pPos, bot.Transform.position);
                    string name = GetEnglishBossName(bot);

                    Color bColor = Color.red;
                    if (_bossHealthColors.Value) bColor = GetHealthColor(GetHealthPercentage(bot));

                    string label = _showNearest.Value ? $"[{bDist:F0}m] {name}" : name;
                    _tBosses.Add(new BossRenderInfo { Text = label, Color = bColor });
                }
                else
                {
                    float sqrDist = (bot.Transform.position - pPos).sqrMagnitude;

                    if (type == TrackedBotType.Pmc) { _cPmc++; if (sqrDist < minPmc) minPmc = sqrDist; }
                    else if (type == TrackedBotType.Rogue) { _cRogue++; if (sqrDist < minRogue) minRogue = sqrDist; }
                    else if (type == TrackedBotType.Raider) { _cRaider++; if (sqrDist < minRaider) minRaider = sqrDist; }
                    else { _cScav++; if (sqrDist < minScav) minScav = sqrDist; }
                }
            }

            _distPmc = minPmc != float.MaxValue ? Mathf.Sqrt(minPmc) : -1f;
            _distScav = minScav != float.MaxValue ? Mathf.Sqrt(minScav) : -1f;
            _distRogue = minRogue != float.MaxValue ? Mathf.Sqrt(minRogue) : -1f;
            _distRaider = minRaider != float.MaxValue ? Mathf.Sqrt(minRaider) : -1f;

            _tBossTitle = aliveBosses > 1 ? $"Bosses: {aliveBosses}" : $"Boss: {aliveBosses}";
        }

        private void BuildUIStrings()
        {
            bool showDist = _showNearest.Value;

            _tPmc = showDist && _distPmc >= 0 ? $"[{_distPmc:F0}m] PMCs: {_cPmc}" : $"PMCs: {_cPmc}";
            _tScav = showDist && _distScav >= 0 ? $"[{_distScav:F0}m] Scavs: {_cScav}" : $"Scavs: {_cScav}";
            _tRogue = showDist && _distRogue >= 0 ? $"[{_distRogue:F0}m] Rogues: {_cRogue}" : $"Rogues: {_cRogue}";
            _tRaider = showDist && _distRaider >= 0 ? $"[{_distRaider:F0}m] Raiders: {_cRaider}" : $"Raiders: {_cRaider}";
        }

        private void OnGUI()
        {
            if (!_inRaid || !_enableMod.Value) return;

            if (_guiStyle == null)
            {
                _guiStyle = new GUIStyle
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperRight
                };
            }

            _guiStyle.fontSize = _fontSize.Value;
            float alpha = _opacity.Value / 100f;
            int width = 500;

            int x = Screen.width - _offsetRight.Value - width;
            int y = _offsetTop.Value;

            DrawEntry(x, ref y, width, _tPmc, _showPmc, _cPmc, alpha);
            DrawEntry(x, ref y, width, _tScav, _showScav, _cScav, alpha);
            DrawEntry(x, ref y, width, _tRogue, _showRogue, _cRogue, alpha);
            DrawEntry(x, ref y, width, _tRaider, _showRaider, _cRaider, alpha);

            if (_showBoss.Value && _tBosses.Count > 0)
            {
                DrawEntry(x, ref y, width, _tBossTitle, _showBoss, _tBosses.Count, alpha, Color.red);

                foreach (var bossInfo in _tBosses)
                {
                    Color c = bossInfo.Color;
                    c.a = alpha;
                    _guiStyle.normal.textColor = c;

                    GUI.Label(new Rect(x, y, width, 30), bossInfo.Text, _guiStyle);
                    y += _fontSize.Value + 2;
                }
            }
        }

        private void DrawEntry(int x, ref int y, int width, string text, ConfigEntry<bool> enabled, int count, float alpha, Color? overrideColor = null)
        {
            if (!enabled.Value || (_hideZeroCounts.Value && count == 0)) return;

            Color c = overrideColor ?? _globalColor.Value;
            c.a = alpha;
            _guiStyle.normal.textColor = c;

            GUI.Label(new Rect(x, y, width, 30), text, _guiStyle);
            y += _fontSize.Value + 2;
        }

        private TrackedBotType EvaluateBotType(BotOwner bot)
        {
            var role = bot.Profile.Info.Settings.Role;
            string roleStr = role.ToString().ToLower();

            if (role == WildSpawnType.assault || role == WildSpawnType.marksman || roleStr.Contains("assault"))
                return TrackedBotType.Scav;

            if (roleStr.Contains("sptusec") || roleStr.Contains("sptbear") || roleStr.Contains("pmcusec") || roleStr.Contains("pmcbear"))
                return TrackedBotType.Pmc;

            if (role == WildSpawnType.exUsec)
                return TrackedBotType.Rogue;

            if (role == WildSpawnType.pmcBot || roleStr.Contains("sectant"))
                return TrackedBotType.Raider;

            if (role.IsBossOrFollower())
                return TrackedBotType.Boss;

            return TrackedBotType.Scav;
        }

        private float GetHealthPercentage(BotOwner bot)
        {
            try
            {
                float current = 0f, max = 0f;
                foreach (EBodyPart part in System.Enum.GetValues(typeof(EBodyPart)))
                {
                    if (part == EBodyPart.Common) continue;
                    var health = bot.HealthController.GetBodyPartHealth(part, false);
                    current += health.Current; max += health.Maximum;
                }
                return max > 0 ? (current / max) * 100f : 100f;
            }
            catch { return 100f; }
        }

        private Color GetHealthColor(float percentage)
        {
            if (percentage >= 70f) return new Color(0f, 0.8f, 0f);
            if (percentage >= 40f) return new Color(0.9f, 0.9f, 0f);
            if (percentage >= 10f) return new Color(1f, 0.5f, 0f);
            return new Color(1f, 0f, 0f);
        }

        private string GetEnglishBossName(BotOwner bot)
        {
            if (bot?.Profile?.Info?.Settings == null) return "Unknown";
            string roleStr = bot.Profile.Info.Settings.Role.ToString().ToLower();

            if (roleStr == "bossbully") return "Reshala";
            if (roleStr == "bosskojaniy") return "Shturman";
            if (roleStr == "bossgluhar") return "Glukhar";
            if (roleStr == "bosssanitar") return "Sanitar";
            if (roleStr == "bosskilla") return "Killa";
            if (roleStr == "bosstagilla") return "Tagilla";
            if (roleStr == "bosszryachiy") return "Zryachiy";
            if (roleStr == "bossknight") return "Knight";
            if (roleStr == "bossboar") return "Kaban";
            if (roleStr == "bosskolontay") return "Kollontay";
            if (roleStr == "bosspartisan") return "Partisan";

            if (roleStr.Contains("followerbully")) return "Zavodskoy";
            if (roleStr.Contains("followerkojaniy")) return "Shturman Guard";
            if (roleStr.Contains("followergluhar")) return "Glukhar Guard";
            if (roleStr.Contains("followersanitar")) return "Sanitar Guard";
            if (roleStr.Contains("followerboar")) return "Kaban Guard";
            if (roleStr.Contains("bossboarsniper")) return "Kaban Sniper";
            if (roleStr.Contains("followerkolontay")) return "Kollontay Guard";
            if (roleStr == "followerbigpipe") return "Big Pipe";
            if (roleStr == "followerbirdeye") return "Bird Eye";
            if (roleStr.Contains("zryachiy")) return "Zryachiy Guard";

            return bot.Profile.Info.Nickname ?? "Unknown Boss";
        }
    }
}
