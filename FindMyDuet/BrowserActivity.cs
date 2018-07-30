using Android.App;
using Android.Content;
using Android.Webkit;
using Android.OS;
using Android.Util;

namespace com.chham.FindMyDuet
{
    [Activity(Theme = "@android:style/Theme.DeviceDefault.NoActionBar", Label = "Find My Duet")]
    public class BrowserActivity : Activity
    {
        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Browser);

            // Prepare WebView
            WebView webView = FindViewById<WebView>(Resource.Id.webContent);
            webView.Settings.CacheMode = CacheModes.Normal;
            webView.Settings.JavaScriptEnabled = true;
            webView.Settings.DomStorageEnabled = true;
            webView.Settings.UseWideViewPort = Build.VERSION.SdkInt != BuildVersionCodes.M;
            webView.Settings.LoadWithOverviewMode = Build.VERSION.SdkInt != BuildVersionCodes.M;

#if DEBUG
            // Enable debugging in the debug configuration
            WebView.SetWebContentsDebuggingEnabled(true);
#endif

            // Prepare JS-to-C# bindings
            AppInterface appInterface = new AppInterface(this);
            webView.AddJavascriptInterface(appInterface, "app");
        }

        protected override void OnStart()
        {
            base.OnStart();

            // Navigate to the selected board
            WebView webView = FindViewById<WebView>(Resource.Id.webContent);
            webView.LoadUrl("http://" + Intent.GetStringExtra("ip"));
        }
    }
}

