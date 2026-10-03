using Microsoft.Maui.Controls;

namespace MarkUptv.Services
{
    public class AdMobService
    {
        private const string BannerTestId = "ca-app-pub-3940256099942544/6300978111";
        private const string InterstitialTestId = "ca-app-pub-3940256099942544/1033173712";

        public static void Initialize()
        {
        }

        public void ShowBanner(View bannerContainer)
        {
            _ = bannerContainer;
            _ = BannerTestId;
        }

        public Task ShowInterstitialAsync()
        {
            _ = InterstitialTestId;
            return Task.CompletedTask;
        }
    }
}
