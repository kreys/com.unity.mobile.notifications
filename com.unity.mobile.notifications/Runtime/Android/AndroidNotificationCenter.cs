using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.Notifications.Android
{
    /// <summary>
    /// Current status of a scheduled notification, can be queried using CheckScheduledNotificationStatus.
    /// </summary>
    public enum NotificationStatus
    {
        //// <summary>
        /// Status of the specified notification cannot be determined, this is only supported on Android Marshmallow (6.0) and above.
        /// </summary>
        Unavailable = -1,

        //// <summary>
        /// A notification with the specified id could not be found.
        /// </summary>
        Unknown = 0,

        //// <summary>
        /// A notification with the specified is scheduled but not yet delivered.
        /// </summary>
        Scheduled = 1,

        //// <summary>
        /// A notification with the specified was already delivered.
        /// </summary>
        Delivered = 2,
    }

    /// <summary>
    /// Use the AndroidNotificationCenter to register notification channels and schedule local notifications.
    /// </summary>
    public class AndroidNotificationCenter
    {
        public delegate void NotificationReceivedCallback(AndroidNotificationIntentData data);

        /// <summary>
        /// Subscribe to this event to receive callbacks whenever a scheduled notification is shown to the user.
        /// </summary>
        public static event NotificationReceivedCallback OnNotificationReceived = delegate {};

        private static AndroidJavaObject s_NotificationManager;
        private static bool s_Initialized;

        public static bool Initialize()
        {
            if (s_Initialized)
                return true;

            if (AndroidReceivedNotificationMainThreadDispatcher.GetInstance() == null)
            {
                var receivedNotificationDispatcher = new GameObject("AndroidReceivedNotificationMainThreadDispatcher");
                receivedNotificationDispatcher.AddComponent<AndroidReceivedNotificationMainThreadDispatcher>();
            }

#if UNITY_EDITOR || !UNITY_ANDROID
            s_NotificationManager = null;
            s_Initialized = false;
#elif UNITY_ANDROID
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            AndroidJavaObject context = activity.Call<AndroidJavaObject>("getApplicationContext");

            AndroidJavaClass managerClass = new AndroidJavaClass("com.unity.androidnotifications.UnityNotificationManager");

            s_NotificationManager = managerClass.CallStatic<AndroidJavaObject>("getNotificationManagerImpl", context, activity);
            s_NotificationManager.Call("setNotificationCallback", new NotificationCallback());

            s_Initialized = true;
#endif
            return s_Initialized;
        }

        /// <summary>
        /// Allows retrieving the notification used to open the app. You can save arbitrary string data in the 'AndroidNotification.IntentData' field.
        /// </summary>
        /// <returns>
        /// Returns the AndroidNotification used to open the app, returns null if the app was not opened with a notification.
        /// </returns>
        public static AndroidNotificationIntentData GetLastNotificationIntent()
        {
            if (!Initialize())
                return null;

            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            AndroidJavaObject intent = currentActivity.Call<AndroidJavaObject>("getIntent");

            return ParseNotificationIntentData(intent);
        }

        /// <summary>
        ///  Creates a notification channel that notifications can be posted to.
        ///  Notification channel settings can be changed by users on devices running Android 8.0 and above.
        ///  On older Android versions settings set on the notification channel struct will still be applied to the notification
        ///  if they are supported to by the Android version the app is running on.
        /// </summary>
        /// <remarks>
        ///  When a channel is deleted and recreated, all of the previous settings are restored. In order to change any settings
        ///  besides the name or description an entirely new channel (with a different channel ID) must be created.
        /// </remarks>
        public static void RegisterNotificationChannel(AndroidNotificationChannel channel)
        {
            if (!Initialize())
                return;

            if (string.IsNullOrEmpty(channel.Id))
            {
                throw new Exception("Cannot register notification channel, the channel ID is not specified.");
            }
            if (string.IsNullOrEmpty(channel.Name))
            {
                throw new Exception(string.Format("Cannot register notification channel: {0} , the channel Name is not set.", channel.Id));
            }
            if (string.IsNullOrEmpty(channel.Description))
            {
                throw new Exception(string.Format("Cannot register notification channel: {0} , the channel Description is not set.", channel.Id));
            }

            s_NotificationManager.Call("registerNotificationChannel",
                channel.Id,
                channel.Name,
                (int)channel.Importance,
                channel.Description,
                channel.EnableLights,
                channel.EnableVibration,
                channel.CanBypassDnd,
                channel.CanShowBadge,
                channel.VibrationPattern,
                (int)channel.LockScreenVisibility
            );
        }

        /// <summary>
        /// Cancel a scheduled or previously shown notification.
        /// The notification will no longer be displayed on it's scheduled time. If it's already delivered it will be removed from the status bar.
        /// </summary>
        public static void CancelNotification(int id)
        {
            if (!Initialize())
                return;

            CancelScheduledNotification(id);
            CancelDisplayedNotification(id);
        }

        /// <summary>
        /// Cancel a scheduled notification.
        /// The notification will no longer be displayed on it's scheduled time. It it will not be removed from the status bar if it's already delivered.
        /// </summary>
        public static void CancelScheduledNotification(int id)
        {
            if (!Initialize())
                return;

            s_NotificationManager.Call("cancelPendingNotificationIntent", id);
        }

        /// <summary>
        /// Cancel a previously shown notification.
        /// The notification will be removed from the status bar.
        /// </summary>
        public static void CancelDisplayedNotification(int id)
        {
            if (!Initialize())
                return;

            AndroidJavaObject androidNotificationManager = s_NotificationManager.Call<AndroidJavaObject>("getNotificationManager");
            androidNotificationManager.Call("cancel", id);
        }

        /// <summary>
        /// Cancel all notifications scheduled or previously shown by the app.
        /// All scheduled notifications will be canceled. All notifications shown by the app will be removed from the status bar.
        /// </summary>
        public static void CancelAllNotifications()
        {
            CancelAllScheduledNotifications();
            CancelAllDisplayedNotifications();
        }

        /// <summary>
        /// Cancel all notifications scheduled by the app.
        /// All scheduled notifications will be canceled. Notifications will not be removed from the status bar if they are already shown.
        /// </summary>
        public static void CancelAllScheduledNotifications()
        {
            if (Initialize())
                s_NotificationManager.Call("cancelAllPendingNotificationIntents");
        }

        /// <summary>
        /// Cancel all previously shown notifications.
        /// All notifications shown by the app will be removed from the status bar. All scheduled notifications will still be shown on their scheduled time.
        /// </summary>
        public static void CancelAllDisplayedNotifications()
        {
            if (Initialize())
                s_NotificationManager.Call("cancelAllNotifications");
        }

        /// <summary>
        /// Returns all notification channels that were created by the app.
        /// </summary>
        public static AndroidNotificationChannel[] GetNotificationChannels()
        {
            if (!Initialize())
                return new AndroidNotificationChannel[0];

            List<AndroidNotificationChannel> channels = new List<AndroidNotificationChannel>();

            var androidChannels = s_NotificationManager.Call<AndroidJavaObject[]>("getNotificationChannels");

            foreach (var channel in androidChannels)
            {
                var ch = new AndroidNotificationChannel();
                ch.Id = channel.Get<string>("id");
                ch.Name = channel.Get<string>("name");
                ch.Importance = channel.Get<int>("importance").ToImportance();
                ch.Description = channel.Get<string>("description");

                ch.EnableLights = channel.Get<bool>("enableLights");
                ch.EnableVibration = channel.Get<bool>("enableVibration");
                ch.CanBypassDnd = channel.Get<bool>("canBypassDnd");
                ch.CanShowBadge = channel.Get<bool>("canShowBadge");
                ch.VibrationPattern = channel.Get<long[]>("vibrationPattern");
                ch.LockScreenVisibility = channel.Get<int>("lockscreenVisibility").ToLockScreenVisibility();

                channels.Add(ch);
            }
            return channels.ToArray();
        }

        /// <summary>
        /// Returns the notification channel with the specified id.
        /// The notification channel struct fields might not be identical to the channel struct used to initially register the channel if they were changed by the user.
        /// </summary>
        public static AndroidNotificationChannel GetNotificationChannel(string channelId)
        {
            return GetNotificationChannels().SingleOrDefault(channel => channel.Id == channelId);
        }

        /// <summary>
        /// Update an already scheduled notification.
        /// If a notification with the specified id was already scheduled it will be overridden with the information from the passed notification struct.
        /// </summary>
        public static void UpdateScheduledNotification(int id, AndroidNotification notification, string channelId)
        {
            if (!Initialize())
                return;

            if (s_NotificationManager.CallStatic<bool>("checkIfPendingNotificationIsRegistered", id))
                SendNotification(id, notification, channelId);
        }

        /// <summary>
        /// Schedule a notification which will be shown at the time specified in the notification struct.
        /// The specified id can later be used to update the notification before it's triggered, it's current status can be tracked using CheckScheduledNotificationStatus.
        /// </summary>
        public static void SendNotificationWithExplicitID(AndroidNotification notification, string channelId, int id)
        {
            if (Initialize())
                SendNotification(id, notification, channelId);
        }

        /// <summary>
        /// Schedule a notification which will be shown at the time specified in the notification struct.
        /// The returned id can later be used to update the notification before it's triggered, it's current status can be tracked using CheckScheduledNotificationStatus.
        /// </summary>
        public static int SendNotification(AndroidNotification notification, string channelId)
        {
            if (!Initialize())
                return -1;

            int id = Math.Abs(DateTime.Now.ToString("yyMMddHHmmssffffff").GetHashCode()) + (new System.Random().Next(10000));
            SendNotification(id, notification, channelId);

            return id;
        }

        /// <summary>
        /// Return the status of a scheduled notification.
        /// Only available in API  23 and above.
        /// </summary>
        public static NotificationStatus CheckScheduledNotificationStatus(int id)
        {
            var status = s_NotificationManager.Call<int>("checkNotificationStatus", id);
            return (NotificationStatus)status;
        }

        internal static void SendNotification(int id, AndroidNotification notification, string channelId)
        {
            if (notification.fireTime < 0L)
            {
                Debug.LogError("Failed to schedule notification, it did not contain a valid FireTime");
            }

            AndroidJavaClass managerClass =
                new AndroidJavaClass("com.unity.androidnotifications.UnityNotificationManager");
            AndroidJavaObject context = s_NotificationManager.Get<AndroidJavaObject>("mContext");

            AndroidJavaObject notificationIntent = new AndroidJavaObject("android.content.Intent", context, managerClass);

            notificationIntent.Call<AndroidJavaObject>("putExtra", "id", id);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "channelID", channelId);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "textTitle", notification.title);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "textContent", notification.text);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "smallIconStr", notification.smallIcon);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "autoCancel", notification.shouldAutoCancel);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "usesChronometer", notification.usesStopwatch);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "fireTime", notification.fireTime);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "repeatInterval", notification.repeatInterval);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "largeIconStr", notification.largeIcon);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "style", notification.style);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "color", notification.color);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "number", notification.number);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "data", notification.intentData);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "group", notification.group);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "groupSummary", notification.groupSummary);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "sortKey", notification.sortKey);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "groupAlertBehaviour", notification.groupAlertBehaviour);
            notificationIntent.Call<AndroidJavaObject>("putExtra", "showTimestamp", notification.showTimestamp);

            long timestampValue = notification.showCustomTimestamp ? notification.customTimestamp : notification.fireTime;
            notificationIntent.Call<AndroidJavaObject>("putExtra", "timestamp", timestampValue);

            s_NotificationManager.Call("scheduleNotificationIntent", notificationIntent);
        }

        /// <summary>
        /// Delete the specified notification channel.
        /// </summary>
        public static void DeleteNotificationChannel(string channelId)
        {
            if (Initialize())
                s_NotificationManager.Call("deleteNotificationChannel", channelId);
        }

        internal static AndroidNotificationIntentData ParseNotificationIntentData(AndroidJavaObject notificationIntent)
        {
            var id = notificationIntent.Call<int>("getIntExtra", "id", -1);
            if (id == -1)
                return null;

            var channel = notificationIntent.Call<string>("getStringExtra", "channelID");

            var notification = new AndroidNotification();
            notification.title = notificationIntent.Call<string>("getStringExtra", "textTitle");
            notification.text = notificationIntent.Call<string>("getStringExtra", "textContent");
            notification.shouldAutoCancel = notificationIntent.Call<bool>("getBooleanExtra", "autoCancel", false);
            notification.usesStopwatch = notificationIntent.Call<bool>("getBooleanExtra", "usesChronometer", false);
            notification.fireTime = notificationIntent.Call<long>("getLongExtra", "fireTime", -1L);
            notification.repeatInterval = notificationIntent.Call<long>("getLongExtra", "repeatInterval", -1L);
            notification.style = notificationIntent.Call<int>("getIntExtra", "style", -1);
            notification.color = notificationIntent.Call<int>("getIntExtra", "color", 0);
            notification.number = notificationIntent.Call<int>("getIntExtra", "number", -1);
            notification.intentData = notificationIntent.Call<string>("getStringExtra", "data");
            notification.group = notificationIntent.Call<string>("getStringExtra", "group");
            notification.groupSummary = notificationIntent.Call<bool>("getBooleanExtra", "groupSummary", false);
            notification.sortKey = notificationIntent.Call<string>("getStringExtra", "sortKey");
            notification.groupAlertBehaviour = notificationIntent.Call<int>("getIntExtra", "groupAlertBehaviour", -1);

            return new AndroidNotificationIntentData(id, channel, notification);
        }

        internal static void ReceivedNotificationCallback(AndroidJavaObject intent)
        {
            var data = ParseNotificationIntentData(intent);
            OnNotificationReceived(data);
        }
    }
}
