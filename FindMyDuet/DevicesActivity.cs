using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace com.chham.FindMyDuet
{
    [Activity(Label = "@string/ApplicationName", MainLauncher = true, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.KeyboardHidden | ConfigChanges.UiMode)]
    public class DevicesActivity : Activity
    {
        // Discovery keeps running for as long as the list is shown, this is only how long an empty
        // list is presented as a scan in progress rather than as an empty network
        private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(5);

        // Grace period between stopping and restarting discovery
        private static readonly TimeSpan RestartDelay = TimeSpan.FromMilliseconds(500);

        private readonly List<DuetBoard> boards = new();
        private WifiManager.MulticastLock multicastLock;
        private MDNSFinder finder;
        private bool settling, autoConnectArmed, scanPending;
        private int scanId;

        private void UpdateViews()
        {
            bool waiting = settling && boards.Count == 0;
            FindViewById<RelativeLayout>(Resource.Id.rlProgress).Visibility = waiting ? ViewStates.Visible : ViewStates.Gone;
            FindViewById<LinearLayout>(Resource.Id.llNoDevices).Visibility = (!settling && boards.Count == 0) ? ViewStates.Visible : ViewStates.Gone;
            FindViewById<ListView>(Resource.Id.lvDevices).Visibility = (boards.Count != 0) ? ViewStates.Visible : ViewStates.Gone;
            FindViewById<Button>(Resource.Id.btnScanNetwork).Visibility = waiting ? ViewStates.Gone : ViewStates.Visible;
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Devices);

            // Create WiFi locks and mDNS scanner
            multicastLock = ((WifiManager)ApplicationContext.GetSystemService(Context.WifiService)).CreateMulticastLock("FindMyDuet mDNS");
            finder = new MDNSFinder(this);
            finder.BoardFound += board => RunOnUiThread(() => AddBoard(board));

            // Set events
            FindViewById<Button>(Resource.Id.btnScanNetwork).Click += (sender, args) => { autoConnectArmed = false; _ = ScanNetworkAsync(); };
            FindViewById<ListView>(Resource.Id.lvDevices).ItemClick += (sender, args) => OpenBoard(boards[args.Position]);

            // Only the first scan of a session skips the list when it finds a single board
            autoConnectArmed = true;
        }

        protected override void OnStart()
        {
            base.OnStart();

            // Held for as long as the list is on screen because the WiFi chip keeps filtering multicast
            // for a moment after the lock is taken, which loses the replies of a scan started right away
            multicastLock.Acquire();

            // Android hands out a second OnStart on some launches, which must not disturb a running scan
            if (!scanPending && !finder.IsRunning)
            {
                _ = ScanNetworkAsync();
            }
        }

        protected override void OnStop()
        {
            base.OnStop();

            finder.Stop();
            multicastLock.Release();
        }

        private async Task ScanNetworkAsync()
        {
            int id = ++scanId;
            scanPending = true;
            boards.Clear();
            RefreshList();
            settling = true;
            UpdateViews();

            try
            {
                // Discovery is always restarted because the platform reports every service only once per
                // run, and it always gets a breather first because the listeners are torn down
                // asynchronously - starting again too soon leaves the platform with discovery stopped
                finder.Stop();
                await Task.Delay(RestartDelay);
                if (id != scanId)
                {
                    return;
                }
                finder.Start();
                scanPending = false;
            }
            catch (Exception e)
            {
                scanPending = false;
                settling = false;
                UpdateViews();
                Toast.MakeText(this, GetString(Resource.String.ScanFailed, e.GetType().Name), ToastLength.Long).Show();
                Console.WriteLine($"ERROR: mDNS scan failed: {e}");
                return;
            }

            await Task.Delay(SettleTime);
            if (id != scanId)
            {
                return;
            }

            settling = false;
            UpdateViews();
            if (autoConnectArmed && boards.Count == 1)
            {
                // If the app is starting and there is only one device available, connect to it immediately
                OpenBoard(boards[0]);
            }
        }

        private void AddBoard(DuetBoard board)
        {
            if (!boards.Contains(board))
            {
                // The same board is announced once per interface it can be reached on
                boards.Add(board);
                RefreshList();
                UpdateViews();

                if (autoConnectArmed && boards.Count == 1 && !settling)
                {
                    // Discovery keeps running past the settle time, so a board that only turns up once the
                    // list already reads as empty is still the single board the app would have connected to
                    OpenBoard(board);
                }
            }
        }

        // The adapter is rebuilt rather than notified because its Java peer wraps a snapshot of the list
        private void RefreshList()
        {
            FindViewById<ListView>(Resource.Id.lvDevices).Adapter = new ArrayAdapter(this, Android.Resource.Layout.SimpleListItem1, boards.ToList());
        }

        private void OpenBoard(DuetBoard board)
        {
            autoConnectArmed = false;

            Intent webIntent = new(this, typeof(BrowserActivity));
            webIntent.PutExtra(BrowserActivity.UrlExtra, board.Url);
            StartActivity(webIntent);
        }
    }
}
