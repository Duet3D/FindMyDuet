using Android.Webkit;
using Java.Interop;

namespace com.chham.FindMyDuet
{
    /// <summary>Bridge exposed to the loaded web interface as the global "app" object</summary>
    public class AppInterface : Java.Lang.Object
    {
        private readonly BrowserActivity parent;

        public AppInterface(BrowserActivity activity)
        {
            parent = activity;
        }

        /// <summary>Close the web interface and return to the list of discovered boards</summary>
        [Export]
        [JavascriptInterface]
        public void listDevices()
        {
            parent.Finish();
        }

        /// <summary>Save a file the web interface generated in the browser, see the blob hook in <see cref="DuetWebViewClient"/></summary>
        /// <param name="fileName">Name to save the file under</param>
        /// <param name="dataUrl">File content as a base64-encoded data URL</param>
        [Export]
        [JavascriptInterface]
        public void saveFile(string fileName, string dataUrl)
        {
            // Bridge methods are called on a dedicated JavaBridge thread
            parent.RunOnUiThread(() => parent.SaveFile(fileName, dataUrl));
        }
    }
}
