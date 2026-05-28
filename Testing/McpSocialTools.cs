using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using HarveyStressMeter.Helpers;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;

namespace HarveyStressMeter.Testing
{
    /// <summary>MCP tools for friendship, NPC placement, and conversation topics.</summary>
    internal static class McpSocialTools
    {
        private const int MaxFriendshipPoints = 2500;
        private const int MaxListedTopics = 150;

        private static readonly HashSet<string> AllowedRelationships =
            new(StringComparer.OrdinalIgnoreCase) { "none", "Dating", "Married" };

        public static string SetFriendship(JsonElement? arguments)
        {
            if (!TryGetString(arguments, "npc", out var npcName))
                return "Error: npc is required.";

            if (!TryGetInt(arguments, "points", out var points))
                return "Error: points is required.";

            if (points < 0 || points > MaxFriendshipPoints)
                return $"Error: points must be 0–{MaxFriendshipPoints}.";

            if (!TryResolveNpcName(npcName, out var resolvedNpc, out var npcError))
                return npcError;

            var friendship = GetOrCreateFriendship(resolvedNpc);
            friendship.Points = points;

            string? relationshipArg = null;
            if (TryGetString(arguments, "relationship", out var relationshipRaw))
                relationshipArg = relationshipRaw.Trim();

            string? warning = null;
            if (!string.IsNullOrEmpty(relationshipArg))
            {
                if (!AllowedRelationships.Contains(relationshipArg))
                {
                    return "Error: relationship must be none, Dating, or Married.";
                }

                if (!TryApplyRelationship(resolvedNpc, friendship, relationshipArg, out var relError, out warning))
                    return relError;
            }

            return BuildFriendshipSnapshot(resolvedNpc, friendship, relationshipArg, warning);
        }

        public static string SetRelationship(JsonElement? arguments)
        {
            if (!TryGetString(arguments, "npc", out var npcName))
                return "Error: npc is required.";

            if (!TryGetString(arguments, "relationship", out var relationshipRaw))
                return "Error: relationship is required (none|Dating|Married).";

            var relationship = relationshipRaw.Trim();
            if (!AllowedRelationships.Contains(relationship))
                return "Error: relationship must be none, Dating, or Married.";

            if (!TryResolveNpcName(npcName, out var resolvedNpc, out var npcError))
                return npcError;

            var friendship = GetOrCreateFriendship(resolvedNpc);
            if (!TryApplyRelationship(resolvedNpc, friendship, relationship, out var relError, out var warning))
                return relError;

            return BuildFriendshipSnapshot(resolvedNpc, friendship, relationship, warning);
        }

        public static string GetFriendship(JsonElement? arguments)
        {
            if (!TryGetString(arguments, "npc", out var npcName))
                return "Error: npc is required.";

            if (!TryResolveNpcName(npcName, out var resolvedNpc, out var npcError))
                return npcError;

            if (!Game1.player.friendshipData.TryGetValue(resolvedNpc, out var friendship))
            {
                var sb = new StringBuilder();
                sb.AppendLine("ok: true");
                sb.AppendLine($"npc: {resolvedNpc}");
                sb.AppendLine("points: 0");
                sb.AppendLine("heartsApprox: 0");
                sb.AppendLine("relationship: none");
                sb.AppendLine("isDating: false");
                sb.AppendLine("isMarried: false");
                sb.AppendLine($"spouse: {FormatSpouse()}");
                sb.AppendLine("warning: no friendship entry yet (NPC not met in save).");
                return sb.ToString().TrimEnd();
            }

            return BuildFriendshipSnapshot(resolvedNpc, friendship, relationshipLabel: null, warning: null);
        }

        public static string PlaceNpc(JsonElement? arguments)
        {
            if (McpEnvironmentTools.EnvironmentBlockMessage("mcp_place_npc") is { } blocked)
                return blocked;

            if (!TryGetString(arguments, "npc", out var npcName))
                return "Error: npc is required.";

            if (!TryGetString(arguments, "location", out var locationName))
                return "Error: location is required.";

            if (!TryGetInt(arguments, "x", out var x) || !TryGetInt(arguments, "y", out var y))
                return "Error: x and y are required.";

            var npc = Game1.getCharacterFromName(npcName);
            if (npc == null)
                return $"Error: NPC '{npcName}' not found.";

            if (!McpEnvironmentTools.TryResolveLocation(locationName, out var targetLocation, out var resolveError))
                return resolveError;

            var oldLocation = npc.currentLocation?.NameOrUniqueName ?? npc.currentLocation?.Name ?? "null";
            var oldTile = npc.Tile;
            var warpName = targetLocation!.NameOrUniqueName ?? targetLocation.Name ?? locationName;

            if (!RescuePlayerPositionHelper.TryFindSafeStandingTile(targetLocation, new Point(x, y), out var safeTile))
                return $"Error: no safe standing tile near {warpName} ({x},{y}).";

            try
            {
                MoveNpcToTile(npc, targetLocation, safeTile.X, safeTile.Y);
            }
            catch (Exception ex)
            {
                return $"Error: failed to move NPC '{npcName}': {ex.Message}";
            }

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"npc: {npc.Name}");
            sb.AppendLine($"oldLocation: {oldLocation}");
            sb.AppendLine($"newLocation: {npc.currentLocation?.NameOrUniqueName ?? npc.currentLocation?.Name ?? warpName}");
            sb.AppendLine($"oldTile: {(int)oldTile.X},{(int)oldTile.Y}");
            sb.AppendLine($"newTile: {safeTile.X},{safeTile.Y}");
            if (safeTile.X != x || safeTile.Y != y)
                sb.AppendLine($"requestedTile: {x},{y}");
            return sb.ToString().TrimEnd();
        }

        public static string AddTopic(JsonElement? arguments)
        {
            if (!TryGetString(arguments, "topic", out var topic))
                return "Error: topic is required.";

            var days = 1;
            if (TryGetInt(arguments, "days", out var parsedDays))
                days = parsedDays;

            if (days < 0)
                return "Error: days must be >= 0.";

            var existsBefore = ConversationHelper.HasTopic(topic);
            var daysBefore = GetTopicDays(topic);

            Game1.player.activeDialogueEvents[topic] = days;

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"topic: {topic}");
            sb.AppendLine($"days: {days}");
            sb.AppendLine($"existsBefore: {existsBefore}");
            sb.AppendLine($"existsAfter: {ConversationHelper.HasTopic(topic)}");
            if (existsBefore)
                sb.AppendLine($"daysBefore: {daysBefore}");
            sb.AppendLine($"daysAfter: {GetTopicDays(topic)}");
            return sb.ToString().TrimEnd();
        }

        public static string RemoveTopic(JsonElement? arguments)
        {
            if (!TryGetString(arguments, "topic", out var topic))
                return "Error: topic is required.";

            var existsBefore = ConversationHelper.HasTopic(topic);
            var daysBefore = GetTopicDays(topic);

            if (existsBefore)
                ConversationHelper.RemoveTopic(topic);

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"topic: {topic}");
            sb.AppendLine($"existsBefore: {existsBefore}");
            sb.AppendLine($"existsAfter: {ConversationHelper.HasTopic(topic)}");
            if (existsBefore)
                sb.AppendLine($"daysBefore: {daysBefore}");
            return sb.ToString().TrimEnd();
        }

        public static string HasTopic(JsonElement? arguments)
        {
            if (!TryGetString(arguments, "topic", out var topic))
                return "Error: topic is required.";

            var exists = ConversationHelper.HasTopic(topic);
            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"topic: {topic}");
            sb.AppendLine($"exists: {exists}");
            if (exists)
                sb.AppendLine($"daysLeft: {GetTopicDays(topic)}");
            else
                sb.AppendLine("daysLeft: (none)");
            return sb.ToString().TrimEnd();
        }

        public static string ListTopics(JsonElement? arguments)
        {
            string? filter = null;
            if (TryGetString(arguments, "filter", out var filterRaw))
                filter = filterRaw.Trim();

            var topics = new List<(string Key, int Days)>();
            foreach (var key in Game1.player.activeDialogueEvents.Keys)
            {
                var days = Game1.player.activeDialogueEvents[key];
                if (days < 0)
                    continue;

                if (filter != null && !key.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                topics.Add((key, days));
            }

            topics.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            var totalMatching = topics.Count;
            if (topics.Count > MaxListedTopics)
                topics = topics.Take(MaxListedTopics).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"filter: {filter ?? "(all)"}");
            sb.AppendLine($"count: {topics.Count}");
            sb.AppendLine($"totalMatching: {totalMatching}");
            if (totalMatching > MaxListedTopics)
                sb.AppendLine($"warning: output truncated to {MaxListedTopics} topics; narrow filter.");

            foreach (var (key, days) in topics)
                sb.AppendLine($"topic: {key}, daysLeft: {days}");

            if (topics.Count == 0)
                sb.AppendLine("(none)");

            return sb.ToString().TrimEnd();
        }

        private static void MoveNpcToTile(NPC npc, GameLocation targetLocation, int x, int y)
        {
            var oldLocation = npc.currentLocation;
            if (oldLocation != null && oldLocation.characters.Contains(npc))
                oldLocation.characters.Remove(npc);

            if (!targetLocation.characters.Contains(npc))
                targetLocation.addCharacter(npc);

            npc.setTileLocation(new Vector2(x, y));
            npc.currentLocation = targetLocation;
        }

        private static Friendship GetOrCreateFriendship(string npcName)
        {
            if (!Game1.player.friendshipData.TryGetValue(npcName, out var friendship))
            {
                friendship = new Friendship(0);
                Game1.player.friendshipData[npcName] = friendship;
            }

            return friendship;
        }

        private static bool TryApplyRelationship(
            string npcName,
            Friendship friendship,
            string relationship,
            out string error,
            out string? warning)
        {
            error = "";
            warning = null;
            var currentSpouse = Game1.player.spouse;
            var hasOtherSpouse = !string.IsNullOrEmpty(currentSpouse)
                                 && !string.Equals(currentSpouse, npcName, StringComparison.OrdinalIgnoreCase);

            if (string.Equals(relationship, "none", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(currentSpouse, npcName, StringComparison.OrdinalIgnoreCase))
                {
                    Game1.player.spouse = "";
                    warning = "test-only: cleared spouse field for this NPC; farmhouse/state may be inconsistent until reload.";
                }

                if (friendship.Status is FriendshipStatus.Dating or FriendshipStatus.Engaged or FriendshipStatus.Married)
                    friendship.Status = FriendshipStatus.Friendly;

                return true;
            }

            if (string.Equals(relationship, "Dating", StringComparison.OrdinalIgnoreCase))
            {
                if (hasOtherSpouse)
                {
                    error = $"Error: player is married to '{currentSpouse}'; cannot set Dating with '{npcName}' safely.";
                    return false;
                }

                if (string.Equals(currentSpouse, npcName, StringComparison.OrdinalIgnoreCase))
                {
                    warning = "player already has spouse set to this NPC; Dating status applied but spouse field unchanged.";
                }

                friendship.Status = FriendshipStatus.Dating;
                return true;
            }

            if (string.Equals(relationship, "Married", StringComparison.OrdinalIgnoreCase))
            {
                if (hasOtherSpouse)
                {
                    error = $"Error: player is married to '{currentSpouse}'; refusing to change spouse to '{npcName}'.";
                    return false;
                }

                Game1.player.spouse = npcName;
                friendship.Status = FriendshipStatus.Married;
                warning = "test-only: marriage set via debug MCP; wedding/event state may be incomplete until reload.";
                return true;
            }

            error = "Error: unsupported relationship value.";
            return false;
        }

        private static string BuildFriendshipSnapshot(
            string npcName,
            Friendship friendship,
            string? relationshipLabel,
            string? warning)
        {
            var isMarriedToNpc = string.Equals(Game1.player.spouse, npcName, StringComparison.OrdinalIgnoreCase)
                                 || friendship.Status == FriendshipStatus.Married;
            var isDating = friendship.Status == FriendshipStatus.Dating;
            var resolvedRelationship = relationshipLabel
                                       ?? (isMarriedToNpc ? "Married"
                                           : isDating ? "Dating"
                                           : "none");

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"npc: {npcName}");
            sb.AppendLine($"points: {friendship.Points}");
            sb.AppendLine($"heartsApprox: {Game1.player.getFriendshipHeartLevelForNPC(npcName)}");
            sb.AppendLine($"relationship: {resolvedRelationship}");
            sb.AppendLine($"status: {friendship.Status}");
            sb.AppendLine($"isDating: {isDating}");
            sb.AppendLine($"isMarried: {isMarriedToNpc}");
            sb.AppendLine($"spouse: {FormatSpouse()}");
            if (!string.IsNullOrEmpty(warning))
                sb.AppendLine($"warning: {warning}");
            return sb.ToString().TrimEnd();
        }

        private static string FormatSpouse()
            => string.IsNullOrEmpty(Game1.player.spouse) ? "(none)" : Game1.player.spouse;

        private static bool TryResolveNpcName(string name, out string resolved, out string error)
        {
            resolved = name.Trim();
            error = "";

            if (string.IsNullOrEmpty(resolved))
            {
                error = "Error: npc name is empty.";
                return false;
            }

            if (Game1.getCharacterFromName(resolved) != null)
                return true;

            if (Game1.player.friendshipData.ContainsKey(resolved))
                return true;

            error = $"Error: NPC '{name}' not found (no character and no friendship entry).";
            return false;
        }

        private static int GetTopicDays(string topic)
        {
            return Game1.player.activeDialogueEvents.TryGetValue(topic, out var days) ? days : -1;
        }

        private static bool TryGetString(JsonElement? arguments, string name, out string value)
        {
            value = string.Empty;
            if (arguments is not { ValueKind: JsonValueKind.Object } args
                || !args.TryGetProperty(name, out var el)
                || el.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = el.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryGetInt(JsonElement? arguments, string name, out int value)
        {
            value = 0;
            if (arguments is not { ValueKind: JsonValueKind.Object } args
                || !args.TryGetProperty(name, out var el))
            {
                return false;
            }

            return el.TryGetInt32(out value);
        }
    }
}
