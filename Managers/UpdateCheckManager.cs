using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Xml.Dom;
using Windows.Services.Store;
using Windows.UI.Notifications;

namespace YellowInside;

public static class UpdateCheckManager
{
    private static readonly TimeSpan s_checkInterval = TimeSpan.FromHours(8);
    private static CancellationTokenSource s_cancellationTokenSource;

    public static void Start()
    {
        s_cancellationTokenSource = new CancellationTokenSource();
        _ = CheckUpdateLoopAsync(s_cancellationTokenSource.Token);
    }

    public static void Stop()
    {
        s_cancellationTokenSource?.Cancel();
        s_cancellationTokenSource?.Dispose();
        s_cancellationTokenSource = null;
    }

    private static async Task CheckUpdateLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(s_checkInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await CheckForUpdateAsync();
            }
            catch
            {
                // Silently ignore (no network, not installed from Store, etc.)
            }
        }
    }

    private static async Task CheckForUpdateAsync()
    {
        var storeContext = StoreContext.GetDefault();
        var updates = await storeContext.GetAppAndOptionalStorePackageUpdatesAsync();

        if (updates.Count > 0)
            ShowUpdateNotification();
    }

    private static void ShowUpdateNotification()
    {
        var packageFamilyName = Package.Current.Id.FamilyName;
        var storeUri = $"ms-windows-store://pdp/?PFN={packageFamilyName}";

        var toastXml = new XmlDocument();
        toastXml.LoadXml(
            $"""
            <toast activationType="protocol" launch="{storeUri}">
                <visual>
                    <binding template="ToastGeneric">
                        <text>Yellow Inside 업데이트</text>
                        <text>새로운 버전이 출시되었습니다. Microsoft Store에서 업데이트할 수 있습니다.</text>
                    </binding>
                </visual>
                <actions>
                    <action content="업데이트하기" activationType="protocol" arguments="{storeUri}" />
                </actions>
            </toast>
            """);

        var notification = new ToastNotification(toastXml);
        ToastNotificationManager.CreateToastNotifier().Show(notification);
    }
}
