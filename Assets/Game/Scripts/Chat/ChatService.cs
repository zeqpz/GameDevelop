// ChatService — the ChatService/ChatAlerts pair, ported. Parses the player
// command set exactly as the server did:
//   /me → ME · /shout → SHOUT · /whisper → WHISPER · /ooc → OOC ·
//   /stats → routes an open-stats request · anything else starting "/" →
//   a SYS nudge toward /help (which is fully CLIENT-side, per the Roblox
//   rule — ChatScreen intercepts it before this ever runs).
// Types clamp to the five player kinds + SYS/ALERT; the proximity ranges
// (IC 20 · SHOUT 30 · WHISPER 8 studs) are carried for the multiplayer
// port — in the single-player slice every message self-delivers, exactly
// like the 2026-08-16 self-visibility pass wanted anyway. CHAT_COOLDOWN
// 0.5 s between broadcast messages. Join lines ride ALERT (the ChatReady
// flush: the UI publishes the join once it exists, so it's never lost).
using UnityEngine;
using Game.Core;

namespace Game.Chat
{
    public readonly struct ChatMessage
    {
        public readonly string Type;      // IC/OOC/ME/SHOUT/WHISPER/SYS/ALERT/HELP
        public readonly string Name;
        public readonly string Message;
        public ChatMessage(string type, string name, string message)
        {
            Type = type;
            Name = name;
            Message = message;
        }
    }

    public readonly struct ChatTypingChanged
    {
        public readonly bool Typing;
        public ChatTypingChanged(bool typing) { Typing = typing; }
    }

    public readonly struct OpenStatsRequested { }

    public class ChatService
    {
        public const string PlayerName = "Player";   // RP name system: later port
        public const float IcRange = 20f;            // studs — multiplayer seam
        public const float ShoutRange = 30f;
        public const float WhisperRange = 8f;
        const float ChatCooldown = 0.5f;

        float _lastSendAt = -10f;

        public void Send(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            raw = raw.Trim();

            string type = "IC";
            string msg = raw;
            if (StripCommand(raw, "/me", out string rest)) { type = "ME"; msg = rest; }
            else if (StripCommand(raw, "/shout", out rest)) { type = "SHOUT"; msg = rest; }
            else if (StripCommand(raw, "/whisper", out rest)) { type = "WHISPER"; msg = rest; }
            else if (StripCommand(raw, "/ooc", out rest)) { type = "OOC"; msg = rest; }
            else if (raw == "/stats")
            {
                EventBus.Publish(new OpenStatsRequested());
                return;
            }
            else if (raw.StartsWith("/"))
            {
                SystemLine("Unknown command — try /help");
                return;
            }
            if (string.IsNullOrWhiteSpace(msg)) return;

            if (Time.time - _lastSendAt < ChatCooldown) return;   // anti-spam
            _lastSendAt = Time.time;
            EventBus.Publish(new ChatMessage(type, PlayerName, msg));
        }

        static bool StripCommand(string raw, string cmd, out string rest)
        {
            rest = null;
            if (!raw.StartsWith(cmd + " ", System.StringComparison.OrdinalIgnoreCase))
                return false;
            rest = raw.Substring(cmd.Length + 1).Trim();
            return true;
        }

        // ChatAlerts twins: join/equip/leave lines ride ALERT (purple italic).
        public void Alert(string line) =>
            EventBus.Publish(new ChatMessage("ALERT", "", line));

        public void SystemLine(string line) =>
            EventBus.Publish(new ChatMessage("SYS", "", line));

        public void SetTyping(bool typing) =>
            EventBus.Publish(new ChatTypingChanged(typing));
    }
}
