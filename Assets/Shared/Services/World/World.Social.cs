// World.Social: arkadaş listesi aynası (FriendPreviewsResult login push'u) + PlayerPreview push'ları (FriendshipAction'a göre olay).
// Sunucu: Status (online/offline + Location), Location (masaya oturdu), ChangeLook, Request/Accept/Deny/Remove, Message/DeleteMessage,
// Challenge. Model: Friends[pid] MergeFrom; Deny/Remove → siler.
using System.Collections.Generic;
using M;

namespace Digitoy
{
    public static partial class World
    {
        public static Dictionary<long, PlayerPreview> Friends { get; } = new();
        public static bool FriendsLoaded { get; private set; }
        public static int SupportUnread { get; private set; }

        static void ApplyFriends(FriendPreviewsResult f)
        {
            if (f == null || f.Error is not (null or ErrorCode.None)) return;
            Friends.Clear();
            if (f.Previews != null) foreach (var p in f.Previews) Friends[p.PlayerId] = p;
            FriendsLoaded = true;
            Dirty(WorldPart.Friends);
            Emit(Ev.FriendsLoaded);
            if (f.SupportUnread is { } su && su != SupportUnread) { SupportUnread = su; Emit(new WorldEvent(Ev.SupportUnread, amount: su)); }
        }

        static void ApplyPreview(PlayerPreview pv)
        {
            if (pv == null || pv.Error is not (null or ErrorCode.None) || pv.PlayerId == 0) return;
            var pid = pv.PlayerId;
            var action = pv.FriendshipAction ?? FriendshipAction.None;
            Friends.TryGetValue(pid, out var cur);
            var wasOnline = cur?.Online == true;
            var prevLoc = cur?.Location;

            // model
            switch (action)
            {
                case FriendshipAction.Deny:
                case FriendshipAction.Remove:
                    Friends.Remove(pid);
                    break;
                default:
                    if (cur == null) Friends[pid] = cur = pv; else cur.MergeFrom(pv);
                    break;
            }
            Dirty(WorldPart.Friends);

            // olaylar
            switch (action)
            {
                case FriendshipAction.Status:
                    if (pv.Online is { } on && on != wasOnline) Emit(new WorldEvent(on ? Ev.FriendOnline : Ev.FriendOffline, playerId: pid, text: pv.Location));
                    else if (pv.Location != null && pv.Location != prevLoc) Emit(new WorldEvent(Ev.FriendMoved, playerId: pid, text: pv.Location));
                    break;
                case FriendshipAction.Location: Emit(new WorldEvent(Ev.FriendMoved, playerId: pid, text: pv.Location)); break;
                case FriendshipAction.ChangeLook: Emit(new WorldEvent(Ev.FriendUpdated, playerId: pid)); break;
                case FriendshipAction.Request: Emit(new WorldEvent(Ev.FriendRequest, playerId: pid)); break;
                case FriendshipAction.Accept: Emit(new WorldEvent(Ev.FriendAccepted, playerId: pid)); break;
                case FriendshipAction.Deny:
                case FriendshipAction.Remove: Emit(new WorldEvent(Ev.FriendRemoved, playerId: pid)); break;
                case FriendshipAction.Message:
                case FriendshipAction.DeleteMessage: Emit(new WorldEvent(Ev.MessageReceived, playerId: pid, flag: action == FriendshipAction.DeleteMessage)); break;
                case FriendshipAction.Challenge: Emit(new WorldEvent(Ev.ChallengeReceived, playerId: pid, text: pv.Location)); break;
                default: Emit(new WorldEvent(Ev.FriendUpdated, playerId: pid)); break;
            }
        }

        static void ResetSocial() { Friends.Clear(); FriendsLoaded = false; SupportUnread = 0; }
    }
}
