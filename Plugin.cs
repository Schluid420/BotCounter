using System;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using EFT;
using UnityEngine;

namespace SPTBotCounter
{
    [BepInPlugin("com.yourname.botcounter", "SPT Detailed Bot Counter", "1.7.0")]
    public class BotCounterPlugin : BaseUnityPlugin
    {
        private float _updateTimer;
        private readonly GUIStyle _guiStyle = new GUIStyle();

        // Counter variables
        private int _countPmc;
        private int _countScav;
        private int _countBoss;
        private int _countRogueRaider;

        // GC Optimization: Reusable StringBuilders instead of string concatenation (+)
        private readonly StringBuilder _sbPmc = new StringBuilder(32);
        private readonly StringBuilder _sbScav = new StringBuilder(32);
        private readonly StringBuilder _sbBoss = new StringBuilder(32);
        private readonly StringBuilder _sbRogue = new StringBuilder(32);

        // Cache for finished UI strings (OnGUI reads directly from here to maximize performance)
        private readonly string[] _uiLines = new string[4];

        // ConfigEntries
        private ConfigEntry<bool> _modEnabled;
        private ConfigEntry<int> _fontSize;
        private ConfigEntry<string> _updateIntervalStr; // Kept as string for the dropdown list
        private ConfigEntry<int> _offsetRight;
        private ConfigEntry<int> _offsetTop;

        private void Awake()
        {
            _modEnabled = Config.Bind("1. General", "Enable Mod", true, "Toggle the on-screen display completely on or off.");
            _fontSize = Config.Bind("2. Display", "Font Size", 16, new ConfigDescription("The text size on your screen.", new AcceptableValueRange<int>(10, 30)));
            _offsetRight = Config.Bind("2. Display", "Offset Right", 20, "Distance from the right edge of the screen.");
            _offsetTop = Config.Bind("2. Display", "Offset Top", 40, "Distance from the top edge of the screen.");

            // Dropdown selection for your preferred intervals
            _updateIntervalStr = Config.Bind("3. Performance", "Update Interval", "15s",
                new ConfigDescription("How often the AI bots are counted.",
                new AcceptableValueList<string>("15s", "30s", "1min")));

            _guiStyle.normal.textColor = Color.green;
            _guiStyle.fontStyle = FontStyle.Bold;

            // Initialize default texts so nothing draws empty arrays on startup
            _uiLines[0] = "PMCs (AI): 0";
            _uiLines[1] = "Scavs: 0";
            _uiLines[2] = "Bosses & Guards: 0";
            _uiLines[3] = "Rogues & Raiders: 0";

            Logger.LogInfo("SPT Detailed Bot Counter (Max-Optimized) loaded successfully!");
        }

        private void Update()
        {
            if (_modEnabled == null || !_modEnabled.Value) return;

            // Convert dropdown interval string to seconds float
            float targetInterval = 15f;
            if (_updateIntervalStr != null)
            {
                if (_updateIntervalStr.Value == "30s") targetInterval = 30f;
                else if (_updateIntervalStr.Value == "1min") targetInterval = 60f;
            }

            _updateTimer += Time.deltaTime;
            if (_updateTimer >= targetInterval)
            {
                _updateTimer = 0f;
                UpdateBotClassifications();
            }
        }

        private void UpdateBotClassifications()
        {
            _countPmc = 0;
            _countScav = 0;
            _countBoss = 0;
            _countRogueRaider = 0;

            GameWorld gameWorld = FindObjectOfType<GameWorld>();
            if (gameWorld == null || gameWorld.RegisteredPlayers == null) return;

            var players = gameWorld.RegisteredPlayers;
            int playerCount = players.Count;

            for (int i = 0; i < playerCount; i++)
            {
                var player = players[i];

                if (player != null && player.IsYourPlayer) continue;
                if (player == null || player.Profile == null || player.Profile.Info == null || player.Profile.Info.Settings == null) continue;

                var role = player.Profile.Info.Settings.Role;
                var side = player.Profile.Side;

                if (side == EPlayerSide.Bear || side == EPlayerSide.Usec)
                {
                    _countPmc++;
                }
                else if (role == WildSpawnType.pmcBot || role == WildSpawnType.exUsec || role == WildSpawnType.arenaFighterEvent)
                {
                    _countRogueRaider++;
                }
                else if (IsBossOrFollower(role))
                {
                    _countBoss++;
                }
                else if (role == WildSpawnType.marksman || role == WildSpawnType.assault)
                {
                    _countScav++;
                }
            }

            // --- STRINGS CACHED USING STRINGBUILDER TO PREVENT GC STUTTERS ---
            _sbPmc.Length = 0; _sbPmc.Append("PMCs (AI): ").Append(_countPmc);
            _uiLines[0] = _sbPmc.ToString();

            _sbScav.Length = 0; _sbScav.Append("Scavs: ").Append(_countScav);
            _uiLines[1] = _sbScav.ToString();

            _sbBoss.Length = 0; _sbBoss.Append("Bosses & Guards: ").Append(_countBoss);
            _uiLines[2] = _sbBoss.ToString();

            _sbRogue.Length = 0; _sbRogue.Append("Rogues & Raiders: ").Append(_countRogueRaider);
            _uiLines[3] = _sbRogue.ToString();
        }

        private static bool IsBossOrFollower(WildSpawnType role)
        {
            string roleStr = role.ToString().ToLower();
            return roleStr.Contains("boss") || roleStr.Contains("follower") || roleStr.Contains("sectant") || role == WildSpawnType.crazyAssaultEvent;
        }

        private void OnGUI()
        {
            if (_modEnabled == null || !_modEnabled.Value) return;

            // Do not draw anything if no bots are tracked (e.g., main menu or loading screens)
            if (_countPmc == 0 && _countScav == 0 && _countBoss == 0 && _countRogueRaider == 0) return;

            int fSize = _fontSize != null ? _fontSize.Value : 16;
            _guiStyle.fontSize = fSize;
            int rowHeight = fSize + 6;

            _guiStyle.alignment = TextAnchor.UpperRight;

            int offsetR = _offsetRight != null ? _offsetRight.Value : 20;
            int startX = Screen.width - 300 - offsetR;
            int startY = _offsetTop != null ? _offsetTop.Value : 40;

            for (int i = 0; i < _uiLines.Length; i++)
            {
                Rect pos = new Rect(startX, startY + (i * rowHeight), 300, rowHeight);

                // Black text shadow effect
                _guiStyle.normal.textColor = Color.black;
                GUI.Label(new Rect(pos.x + 1, pos.y + 1, pos.width, pos.height), _uiLines[i], _guiStyle);

                // Green main text
                _guiStyle.normal.textColor = Color.green;
                GUI.Label(pos, _uiLines[i], _guiStyle);
            }
        }
    }
}