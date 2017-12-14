using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Android.Content;
using Android.Widget;
using Zeroconf;

namespace com.chham.FindMyDuet
{
    public class DuetBoard
    {
        public DuetBoard(string displayName, string ipAddress)
        {
            DisplayName = displayName;
            IPAddress = ipAddress;
        }

        public string DisplayName { get; }
        public string IPAddress { get;  }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public class MDNSFinder
    {
        private readonly string HttpService = "_http._tcp.local.";

        public List<DuetBoard> Boards { get; }
        private Context context;

        public MDNSFinder(Context parent)
        {
            Boards = new List<DuetBoard>();
            context = parent;
        }

        public async Task DiscoverBoards()
        {
            Boards.Clear();
            IEnumerable<IZeroconfHost> hosts;
            try
            {
                hosts = await ZeroconfResolver.ResolveAsync(HttpService);
            }
            catch (Exception e)
            {
                Toast.MakeText(context, $"Error: Failed to scan network ({e.GetType().Name})", ToastLength.Long).Show();
                Console.WriteLine("ERROR: mDNS scan failed:");
                Console.WriteLine(e);
                return;
            }

            foreach (IZeroconfHost host in hosts)
            {
                bool isDuet = false;
                string displayName = "", firmwareVersion = "";

                foreach (var property in host.Services[HttpService].Properties)
                {
                    if (property.TryGetValue("product", out var product))
                    {
                        if (product.StartsWith("Duet"))
                        {
                            isDuet = true;
                            displayName = host.DisplayName;
                        }
                    }
                    property.TryGetValue("version", out firmwareVersion);
                }

                if (isDuet)
                {
                    if (firmwareVersion != "")
                    {
                        displayName = $"{displayName} (WiFi Firmware {firmwareVersion})";
                    }

                    DuetBoard board = new DuetBoard(displayName, host.IPAddress);
                    Boards.Add(board);
                }
            }
        }
    }
}
