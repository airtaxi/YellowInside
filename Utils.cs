using YellowInside.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace YellowInside;

public static class Utils
{
    public static string GetImageUrl(ContentSource source, string imagePath)
    {
        if (source == ContentSource.Dccon) return $"https://dcimg5.dcinside.com/dccon.php?no={imagePath}";
        else if (source == ContentSource.Arcacon) return imagePath;
        else if (source == ContentSource.Inven) return imagePath;
        else throw new NotImplementedException("이미지 URL 생성 미구현됨");
    }

    public static async Task<ImageSource> GenerateImageSourceAsync(DispatcherQueue dispatcherQueue, ContentSource source, string url)
    {
        if (source == ContentSource.Dccon)
        {
            try
            {
                using var httpClient = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Referer", "https://dccon.dcinside.com");

                var response = await httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();

                var taskCompletionSource = new TaskCompletionSource<ImageSource>();
                dispatcherQueue.TryEnqueue(async () =>
                {
                    var bitmapImage = new BitmapImage { AutoPlay = SettingsManager.GifPlaybackEnabled };
                    await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
                    taskCompletionSource.SetResult(bitmapImage);
                });
                return await taskCompletionSource.Task;
            }
            catch { return null; }
        }
        else if (source == ContentSource.Arcacon)
        {
            try
            {
                using var httpClient = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Referer", "https://arca.live");

                var response = await httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();

                var taskCompletionSource = new TaskCompletionSource<ImageSource>();
                dispatcherQueue.TryEnqueue(async () =>
                {
                    var bitmapImage = new BitmapImage { AutoPlay = SettingsManager.GifPlaybackEnabled };
                    await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
                    taskCompletionSource.SetResult(bitmapImage);
                });
                return await taskCompletionSource.Task;
            }
            catch { return null; }
        }
        else if (source == ContentSource.Inven)
        {
            try
            {
                var taskCompletionSource = new TaskCompletionSource<ImageSource>();
                dispatcherQueue.TryEnqueue(() =>
                {
                    var bitmapImage = new BitmapImage(new Uri(url)) { AutoPlay = SettingsManager.GifPlaybackEnabled };
                    taskCompletionSource.SetResult(bitmapImage);
                });
                return await taskCompletionSource.Task;
            }
            catch { return null; }
        }
        else throw new NotImplementedException("이미지 다운로드 미구현됨");
    }
}
