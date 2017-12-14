using Android.App;
using Android.Content;
using Android.Webkit;
using Java.Interop;

namespace com.chham.FindMyDuet
{
    public class AppInterface : Java.Lang.Object
    {
        private Activity parent;

        public AppInterface(Activity activity)
        {
            parent = activity;
        }

        [Export]
        [JavascriptInterface]
        public void listDevices()
        {
            parent.Finish();
        }
    }
}