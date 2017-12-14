using System;
using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using Android.OS;

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
            WebView webView = FindViewById<WebView>(Resource.Id.webView);
            webView.Settings.CacheMode = CacheModes.NoCache;
            webView.Settings.JavaScriptEnabled = true;
            webView.Settings.DomStorageEnabled = true;
            webView.Settings.LoadWithOverviewMode = true;
            webView.Settings.UseWideViewPort = true;

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
            WebView webView = FindViewById<WebView>(Resource.Id.webView);
            webView.LoadUrl("http://" + Intent.GetStringExtra("ip"));
        }
    }
}

