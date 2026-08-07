using System;
using System.IO;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Webkit;
using Android.Widget;

namespace com.chham.FindMyDuet
{
    [Activity(Theme = "@android:style/Theme.DeviceDefault.NoActionBar", Label = "@string/ApplicationName", ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.KeyboardHidden | ConfigChanges.UiMode)]
    public class BrowserActivity : Activity
    {
        /// <summary>Intent extra holding the address of the board to open</summary>
        public const string UrlExtra = "url";

        private const int FileChooserRequestCode = 1;
        private const int StoragePermissionRequestCode = 2;

        private WebView webView;
        private IValueCallback fileChooserCallback;
        private Action pendingSave;

        /// <summary>Host of the board this activity was opened for, used to tell its own links from external ones</summary>
        public string BoardHost { get; private set; }

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Browser);

            // Prepare WebView
            webView = FindViewById<WebView>(Resource.Id.webContent);
            webView.Settings.JavaScriptEnabled = true;
            webView.Settings.DomStorageEnabled = true;
            webView.Settings.DatabaseEnabled = true;
            webView.Settings.CacheMode = CacheModes.Default;
            webView.Settings.UseWideViewPort = true;
            webView.Settings.LoadWithOverviewMode = true;
            webView.SetWebViewClient(new DuetWebViewClient(this));
            webView.SetWebChromeClient(new DuetWebChromeClient(this));
            webView.SetDownloadListener(new DuetDownloadListener(this));

            // Prepare JS-to-C# bindings
            webView.AddJavascriptInterface(new AppInterface(this), "app");

#if DEBUG
            // Enable debugging in the debug configuration
            WebView.SetWebContentsDebuggingEnabled(true);
#endif

            // Navigate to the selected board
            string url = Intent.GetStringExtra(UrlExtra);
            BoardHost = Android.Net.Uri.Parse(url).Host;
            if (bundle is null)
            {
                webView.LoadUrl(url);
            }
            else
            {
                webView.RestoreState(bundle);
            }
        }

        protected override void OnSaveInstanceState(Bundle outState)
        {
            base.OnSaveInstanceState(outState);
            webView.SaveState(outState);
        }

        public override bool OnKeyDown(Keycode keyCode, KeyEvent e)
        {
            if (keyCode == Keycode.Back && webView.CanGoBack())
            {
                webView.GoBack();
                return true;
            }
            return base.OnKeyDown(keyCode, e);
        }

        /// <summary>Hand a link that does not belong to the board's web interface to the system browser</summary>
        public void OpenExternal(Android.Net.Uri url)
        {
            try
            {
                StartActivity(new Intent(Intent.ActionView, url));
            }
            catch (ActivityNotFoundException)
            {
                Toast.MakeText(this, Resource.String.NoBrowser, ToastLength.Long).Show();
            }
        }

        /// <summary>Let the user pick a file for an upload started from the web interface</summary>
        public bool ShowFileChooser(IValueCallback callback, WebChromeClient.FileChooserParams parameters)
        {
            // The file input stays unresponsive until the pending callback has been answered
            fileChooserCallback?.OnReceiveValue(null);
            fileChooserCallback = callback;

            try
            {
                StartActivityForResult(parameters.CreateIntent(), FileChooserRequestCode);
            }
            catch (ActivityNotFoundException)
            {
                fileChooserCallback = null;
                return false;
            }
            return true;
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (requestCode == FileChooserRequestCode && fileChooserCallback is not null)
            {
                fileChooserCallback.OnReceiveValue(WebChromeClient.FileChooserParams.ParseResult((int)resultCode, data));
                fileChooserCallback = null;
            }
        }

        /// <summary>Save a file the web interface generated in the browser, see the blob hook in <see cref="DuetWebViewClient"/></summary>
        public void SaveFile(string fileName, string dataUrl)
        {
            int separator = dataUrl.IndexOf(',');
            if (separator < 0)
            {
                Toast.MakeText(this, GetString(Resource.String.FileNotSaved, fileName), ToastLength.Long).Show();
                return;
            }

            byte[] content = Convert.FromBase64String(dataUrl.Substring(separator + 1));
            WithStorageAccess(() => WriteToDownloads(fileName, content));
        }

        /// <summary>Download a file the web interface linked to directly</summary>
        public void DownloadFile(string url, string contentDisposition, string mimeType)
        {
            string fileName = URLUtil.GuessFileName(url, contentDisposition, mimeType);
            WithStorageAccess(() =>
            {
                DownloadManager.Request request = new(Android.Net.Uri.Parse(url));
                request.SetDestinationInExternalPublicDir(Android.OS.Environment.DirectoryDownloads, fileName);
                request.SetNotificationVisibility(DownloadVisibility.VisibleNotifyCompleted);
                ((DownloadManager)GetSystemService(DownloadService)).Enqueue(request);
            });
        }

        // Writing outside the app's own directories requires an explicit permission before Android 10
        private void WithStorageAccess(Action save)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29) || CheckSelfPermission(Manifest.Permission.WriteExternalStorage) == Permission.Granted)
            {
                save();
                return;
            }

            pendingSave = save;
            RequestPermissions(new string[] { Manifest.Permission.WriteExternalStorage }, StoragePermissionRequestCode);
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (requestCode == StoragePermissionRequestCode)
            {
                if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
                {
                    pendingSave?.Invoke();
                }
                else
                {
                    Toast.MakeText(this, Resource.String.StoragePermissionRequired, ToastLength.Long).Show();
                }
                pendingSave = null;
            }
        }

        private void WriteToDownloads(string fileName, byte[] content)
        {
            try
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(29))
                {
                    ContentValues values = new();
                    values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
                    values.Put(MediaStore.IMediaColumns.RelativePath, Android.OS.Environment.DirectoryDownloads);
                    using Stream stream = ContentResolver.OpenOutputStream(ContentResolver.Insert(MediaStore.Downloads.ExternalContentUri, values));
                    stream.Write(content, 0, content.Length);
                }
                else
                {
                    Java.IO.File downloads = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads);
                    downloads.Mkdirs();
                    File.WriteAllBytes(Path.Combine(downloads.AbsolutePath, fileName), content);
                }
                Toast.MakeText(this, GetString(Resource.String.FileSaved, fileName), ToastLength.Short).Show();
            }
            catch (Exception e)
            {
                Toast.MakeText(this, GetString(Resource.String.FileNotSaved, fileName), ToastLength.Long).Show();
                Console.WriteLine($"ERROR: Failed to save {fileName}: {e}");
            }
        }
    }

    internal class DuetWebViewClient : WebViewClient
    {
        // DWC saves generated files by clicking a synthetic anchor with a blob: URL, which the download listener
        // never sees, so hand those to the JS bridge instead
        private const string BlobDownloadHook = @"(function() {
	if (window.__findMyDuetHook) { return; }
	window.__findMyDuetHook = true;
	var click = HTMLAnchorElement.prototype.click;
	HTMLAnchorElement.prototype.click = function() {
		if (this.href && this.href.indexOf('blob:') === 0 && this.hasAttribute('download')) {
			var name = this.getAttribute('download');
			fetch(this.href).then(function(response) { return response.blob(); }).then(function(blob) {
				var reader = new FileReader();
				reader.onloadend = function() { app.saveFile(name, reader.result); };
				reader.readAsDataURL(blob);
			});
			return;
		}
		click.call(this);
	};
})();";

        private readonly BrowserActivity activity;

        public DuetWebViewClient(BrowserActivity parent)
        {
            activity = parent;
        }

        public override bool ShouldOverrideUrlLoading(WebView view, IWebResourceRequest request)
        {
            if (request.Url.Host == activity.BoardHost)
            {
                return false;
            }

            activity.OpenExternal(request.Url);
            return true;
        }

        public override void OnPageFinished(WebView view, string url)
        {
            base.OnPageFinished(view, url);
            view.EvaluateJavascript(BlobDownloadHook, null);
        }
    }

    internal class DuetWebChromeClient : WebChromeClient
    {
        private readonly BrowserActivity activity;

        public DuetWebChromeClient(BrowserActivity parent)
        {
            activity = parent;
        }

        public override bool OnShowFileChooser(WebView webView, IValueCallback filePathCallback, FileChooserParams fileChooserParams)
        {
            return activity.ShowFileChooser(filePathCallback, fileChooserParams);
        }
    }

    internal class DuetDownloadListener : Java.Lang.Object, IDownloadListener
    {
        private readonly BrowserActivity activity;

        public DuetDownloadListener(BrowserActivity parent)
        {
            activity = parent;
        }

        public void OnDownloadStart(string url, string userAgent, string contentDisposition, string mimetype, long contentLength)
        {
            activity.DownloadFile(url, contentDisposition, mimetype);
        }
    }
}
