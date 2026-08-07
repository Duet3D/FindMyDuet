using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Net.Nsd;

namespace com.chham.FindMyDuet
{
    /// <summary>Board discovered on the local network</summary>
    public class DuetBoard
    {
        public DuetBoard(string displayName, string url)
        {
            DisplayName = displayName;
            Url = url;
        }

        /// <summary>Name shown in the device list</summary>
        public string DisplayName { get; }

        /// <summary>Address of the web interface including scheme and port</summary>
        public string Url { get; }

        public override string ToString()
        {
            return DisplayName;
        }

        public override bool Equals(object obj)
        {
            return obj is DuetBoard other && Url == other.Url;
        }

        public override int GetHashCode()
        {
            return Url.GetHashCode();
        }
    }

    /// <summary>Finds Duet boards on the local network via mDNS</summary>
    public class MDNSFinder
    {
        private const string HttpService = "_http._tcp";
        private const string HttpsService = "_https._tcp";

        // RRF's own mDNS responders report FIRMWARE_NAME as product whereas the WiFi modules report "DuetWiFi"
        private const string FirmwareNamePrefix = "RepRapFirmware for ";

        private readonly NsdManager nsdManager;

        // The platform resolves one service at a time, so requests have to be queued
        private readonly SemaphoreSlim resolveLock = new(1, 1);

        private DiscoveryListener httpListener, httpsListener;

        public MDNSFinder(Context context)
        {
            nsdManager = (NsdManager)context.GetSystemService(Context.NsdService);
        }

        /// <summary>Raised from a background thread for every board found while discovery is running</summary>
        public event Action<DuetBoard> BoardFound;

        /// <summary>Whether discovery is currently running</summary>
        public bool IsRunning => httpListener is not null;

        /// <summary>Start listening for boards until <see cref="Stop"/> is called</summary>
        /// <remarks>Does nothing when discovery is already running - tearing the listeners down and
        /// registering new ones in the same breath leaves the platform with discovery stopped</remarks>
        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            httpListener = new DiscoveryListener(this, false);
            httpsListener = new DiscoveryListener(this, true);
            nsdManager.DiscoverServices(HttpService, NsdProtocol.DnsSd, httpListener);
            nsdManager.DiscoverServices(HttpsService, NsdProtocol.DnsSd, httpsListener);
        }

        /// <summary>Stop listening for boards</summary>
        public void Stop()
        {
            httpListener?.Stop(nsdManager);
            httpsListener?.Stop(nsdManager);
            httpListener = httpsListener = null;
        }

        private async Task ResolveAsync(NsdServiceInfo serviceInfo, bool secure)
        {
            await resolveLock.WaitAsync();
            try
            {
                ResolveListener listener = new();
                // ResolveService and NsdServiceInfo.Host are deprecated in favour of RegisterServiceInfoCallback,
                // which needs API 34 - they still work and keep this to one code path down to the minimum SDK
#pragma warning disable CA1422
                nsdManager.ResolveService(serviceInfo, listener);
#pragma warning restore CA1422

                DuetBoard board = GetBoard(await listener.Resolved, secure);
                if (board is not null)
                {
                    BoardFound?.Invoke(board);
                }
            }
            finally
            {
                resolveLock.Release();
            }
        }

        private static DuetBoard GetBoard(NsdServiceInfo service, bool secure)
        {
            if (service is null)
            {
                return null;
            }

            string product = GetAttribute(service, "product"), version = GetAttribute(service, "version");
            if (product is null || !(product.StartsWith("Duet") || product.StartsWith(FirmwareNamePrefix)))
            {
                return null;
            }

            string hostAddress = GetHostAddress(service);
            if (hostAddress is null)
            {
                return null;
            }

            string address = hostAddress.Contains(":") ? $"[{hostAddress}]" : hostAddress;
            string port = (service.Port == (secure ? 443 : 80)) ? "" : $":{service.Port}";
            string description = product.StartsWith(FirmwareNamePrefix) ? product.Substring(FirmwareNamePrefix.Length) : product;
            string displayName = (version is null) ? $"{service.ServiceName} ({description})" : $"{service.ServiceName} ({description} {version})";

            return new DuetBoard(displayName, $"{(secure ? "https" : "http")}://{address}{port}");
        }

        // Boards are reachable over both protocols but IPv4 is the safer bet on a home network, and
        // only Android 14 reports more than the single address it happens to have picked
        private static string GetHostAddress(NsdServiceInfo service)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(34))
            {
                string fallback = null;
                foreach (Java.Net.InetAddress address in service.HostAddresses)
                {
                    // Tested on the address itself because JNI hands back the InetAddress base type,
                    // so an Inet4Address type check does not hold
                    if (address.HostAddress is string hostAddress)
                    {
                        if (!hostAddress.Contains(":"))
                        {
                            return hostAddress;
                        }
                        fallback ??= hostAddress;
                    }
                }
                return fallback;
            }

#pragma warning disable CA1422
            return service.Host?.HostAddress;
#pragma warning restore CA1422
        }

        private static string GetAttribute(NsdServiceInfo service, string key)
        {
            return service.Attributes.TryGetValue(key, out byte[] value) && value is not null ? Encoding.UTF8.GetString(value) : null;
        }

        private class DiscoveryListener : Java.Lang.Object, NsdManager.IDiscoveryListener
        {
            private readonly MDNSFinder finder;
            private readonly bool secure;

            public DiscoveryListener(MDNSFinder parent, bool isSecure)
            {
                finder = parent;
                secure = isSecure;
            }

            public void Stop(NsdManager manager)
            {
                try
                {
                    // Unconditional because a listener whose OnDiscoveryStarted has not fired yet would
                    // otherwise be left registered, and a leaked listener wedges discovery for the session
                    manager.StopServiceDiscovery(this);
                }
                catch (Java.Lang.IllegalArgumentException)
                {
                    // Discovery had already been stopped
                }
            }

            public void OnDiscoveryStarted(string serviceType)
            {
            }

            public void OnDiscoveryStopped(string serviceType)
            {
            }

            public void OnServiceFound(NsdServiceInfo serviceInfo)
            {
                _ = finder.ResolveAsync(serviceInfo, secure);
            }

            public void OnServiceLost(NsdServiceInfo serviceInfo)
            {
            }

            public void OnStartDiscoveryFailed(string serviceType, NsdFailure errorCode)
            {
                Console.WriteLine($"ERROR: mDNS discovery of {serviceType} failed ({errorCode})");
            }

            public void OnStopDiscoveryFailed(string serviceType, NsdFailure errorCode)
            {
            }
        }

        private class ResolveListener : Java.Lang.Object, NsdManager.IResolveListener
        {
            private readonly TaskCompletionSource<NsdServiceInfo> completion = new();

            /// <summary>Completes with the resolved service, or with null if it could not be resolved</summary>
            public Task<NsdServiceInfo> Resolved => completion.Task;

            public void OnServiceResolved(NsdServiceInfo serviceInfo)
            {
                completion.TrySetResult(serviceInfo);
            }

            public void OnResolveFailed(NsdServiceInfo serviceInfo, NsdFailure errorCode)
            {
                completion.TrySetResult(null);
            }
        }
    }
}
