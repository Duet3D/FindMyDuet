using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Android.Net.Wifi;

namespace com.chham.FindMyDuet
{
    [Activity(Label = "Find My Duet", MainLauncher = true)]
    public class DevicesActivity : Activity
    {
        private WifiManager wifi;
        private WifiManager.MulticastLock mlock;
        private MDNSFinder finder;
        private bool firstStart = true;

        private void SetScanState(bool scanning)
        {
            FindViewById<RelativeLayout>(Resource.Id.rlProgress).Visibility =
                scanning ? ViewStates.Visible : ViewStates.Gone;
            FindViewById<LinearLayout>(Resource.Id.llNoDevices).Visibility = scanning
                ? ViewStates.Gone
                : (finder.Boards.Count == 0 ? ViewStates.Visible : ViewStates.Gone);
            FindViewById<ListView>(Resource.Id.lvDevices).Visibility = scanning
                ? ViewStates.Gone
                : (finder.Boards.Count == 0 ? ViewStates.Gone : ViewStates.Visible);
            FindViewById<RelativeLayout>(Resource.Id.rlScanNetwork).Visibility =
                scanning ? ViewStates.Gone : ViewStates.Visible;
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Devices);

            // Create WiFi locks and mDNS scanner
            wifi = (WifiManager)ApplicationContext.GetSystemService(Context.WifiService);
            mlock = wifi.CreateMulticastLock("Zeroconf Lock");
            finder = new MDNSFinder(this);

            // Set events
            FindViewById<Button>(Resource.Id.btnScanNetwork).Click += (sender, args) => ScanNetwork(false);
            FindViewById<ListView>(Resource.Id.lvDevices).ItemClick += lvDevices_ItemClick;
        }

        private void lvDevices_ItemClick(object o, AdapterView.ItemClickEventArgs itemClickEventArgs)
        {
            GoToIP(finder.Boards[itemClickEventArgs.Position].IPAddress);
        }

        protected override void OnStart()
        {
            base.OnStart();

            ScanNetwork(firstStart);
            firstStart = false;
        }

        private async Task ScanNetwork(bool firstStart)
        {
            RunOnUiThread(() =>
            {
                SetScanState(true);
                FindViewById<ListView>(Resource.Id.lvDevices).Adapter = null;
            });

            try
            {
                mlock.Acquire();
                await finder.DiscoverBoards();
            }
            finally
            {
                mlock.Release();
            }

            RunOnUiThread(() =>
            {
                SetScanState(false);
                FindViewById<ListView>(Resource.Id.lvDevices).Adapter = new ArrayAdapter(this, Android.Resource.Layout.SimpleListItem1, finder.Boards);

                if (firstStart && finder.Boards.Count == 1)
                {
                    // If the app is starting and there is only one device available, connect to it immediately
                    GoToIP(finder.Boards[0].IPAddress);
                }
            });
        }

        private void GoToIP(string ip)
        {
            var webIntent = new Intent(this, typeof(BrowserActivity));
            webIntent.PutExtra("ip", ip);
            StartActivity(webIntent);
        }
    }
}