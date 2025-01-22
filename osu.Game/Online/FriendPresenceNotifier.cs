// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Online.Metadata;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Users;

namespace osu.Game.Online
{
    public partial class FriendPresenceNotifier : Component
    {
        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private MetadataClient metadataClient { get; set; } = null!;

        [Resolved]
        private ChannelManager channelManager { get; set; } = null!;

        [Resolved]
        private ChatOverlay chatOverlay { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private readonly Bindable<bool> notifyOnFriendPresenceChange = new BindableBool();

        private readonly IBindableList<APIRelation> friends = new BindableList<APIRelation>();
        private readonly IBindableDictionary<int, UserPresence> friendPresences = new BindableDictionary<int, UserPresence>();

        private readonly HashSet<APIUser> onlineAlertQueue = new HashSet<APIUser>();
        private readonly HashSet<APIUser> offlineAlertQueue = new HashSet<APIUser>();

        private double? lastOnlineAlertTime;
        private double? lastOfflineAlertTime;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            config.BindWith(OsuSetting.NotifyOnFriendPresenceChange, notifyOnFriendPresenceChange);

            friends.BindTo(api.Friends);
            friends.BindCollectionChanged(onFriendsChanged, true);

            friendPresences.BindTo(metadataClient.FriendPresences);
            friendPresences.BindCollectionChanged(onFriendPresenceChanged, true);
        }

        protected override void Update()
        {
            base.Update();

            alertOnlineUsers();
            alertOfflineUsers();
        }

        private void onFriendsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    foreach (APIRelation friend in e.NewItems!.Cast<APIRelation>())
                    {
                        if (friend.TargetUser is not APIUser user)
                            continue;

                        if (friendPresences.TryGetValue(friend.TargetID, out _))
                            markUserOnline(user);
                    }

                    break;

                case NotifyCollectionChangedAction.Remove:
                    foreach (APIRelation friend in e.OldItems!.Cast<APIRelation>())
                    {
                        if (friend.TargetUser is not APIUser user)
                            continue;

                        onlineAlertQueue.Remove(user);
                        offlineAlertQueue.Remove(user);
                    }

                    break;
            }
        }

        private void onFriendPresenceChanged(object? sender, NotifyDictionaryChangedEventArgs<int, UserPresence> e)
        {
            switch (e.Action)
            {
                case NotifyDictionaryChangedAction.Add:
                    foreach ((int friendId, _) in e.NewItems!)
                    {
                        APIRelation? friend = friends.FirstOrDefault(f => f.TargetID == friendId);

                        if (friend?.TargetUser is APIUser user)
                            markUserOnline(user);
                    }

                    break;

                case NotifyDictionaryChangedAction.Remove:
                    foreach ((int friendId, _) in e.OldItems!)
                    {
                        APIRelation? friend = friends.FirstOrDefault(f => f.TargetID == friendId);

                        if (friend?.TargetUser is APIUser user)
                            markUserOffline(user);
                    }

                    break;
            }
        }

        private void markUserOnline(APIUser user)
        {
            if (!offlineAlertQueue.Remove(user))
            {
                onlineAlertQueue.Add(user);
                lastOnlineAlertTime ??= Time.Current;
            }
        }

        private void markUserOffline(APIUser user)
        {
            if (!onlineAlertQueue.Remove(user))
            {
                offlineAlertQueue.Add(user);
                lastOfflineAlertTime ??= Time.Current;
            }
        }

        private void alertOnlineUsers()
        {
            if (onlineAlertQueue.Count == 0)
                return;

            if (lastOnlineAlertTime == null || Time.Current - lastOnlineAlertTime < 1000)
                return;

            if (!notifyOnFriendPresenceChange.Value)
            {
                lastOnlineAlertTime = null;
                return;
            }

            APIUser? singleUser = onlineAlertQueue.Count == 1 ? onlineAlertQueue.Single() : null;

            notifications.Post(new SimpleNotification
            {
                Transient = true,
                Icon = FontAwesome.Solid.UserPlus,
                Text = $"Online: {string.Join(@", ", onlineAlertQueue.Select(u => u.Username))}",
                IconColour = colours.Green,
                Activated = () =>
                {
                    if (singleUser != null)
                    {
                        channelManager.OpenPrivateChannel(singleUser);
                        chatOverlay.Show();
                    }

                    return true;
                }
            });

            onlineAlertQueue.Clear();
            lastOnlineAlertTime = null;
        }

        private void alertOfflineUsers()
        {
            if (offlineAlertQueue.Count == 0)
                return;

            if (lastOfflineAlertTime == null || Time.Current - lastOfflineAlertTime < 1000)
                return;

            if (!notifyOnFriendPresenceChange.Value)
            {
                lastOfflineAlertTime = null;
                return;
            }

            notifications.Post(new SimpleNotification
            {
                Transient = true,
                Icon = FontAwesome.Solid.UserMinus,
                Text = $"Offline: {string.Join(@", ", offlineAlertQueue.Select(u => u.Username))}",
                IconColour = colours.Red
            });

            offlineAlertQueue.Clear();
            lastOfflineAlertTime = null;
        }
    }
}
