using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace laba1
{
    public class Program
    {
        public static void Main()
        {
            bool isEnd = false;
            do
            {
                Console.WriteLine("Select option: \n1.Host Scanner\n2.Port Scanner\nother key to exit");
                var result = Console.ReadLine();
                switch (result)
                {
                    case "1":
                        HostScanner();
                        break;
                    case "2":
                        PortScanner();
                        break;
                    default:
                        isEnd = true;
                        break;
                }
            } while (!isEnd);
        }

        private static void PortScanner()
        {
            int startPort = 0; // начальный порт
            int endPort = 65535; // конечный порт

            Console.WriteLine("Enter ip address: ");
            IPAddress ip;
            while (!IPAddress.TryParse(Console.ReadLine(), out ip))
            {
                Console.WriteLine("Try again");
            }

            int threadCount = 100;
            
            Task[] tasks = new Task[threadCount];
            int count = 0;
            int task_i = 0;

            int check = startPort + threadCount + 1000;
            
            for (int i = startPort; i <= endPort; i++)
            {
                var endPoint = new IPEndPoint(ip, i);
                if (count == threadCount)
                {
                    task_i = Task.WaitAny(tasks);
                    tasks[task_i] = Task.Run(() => CheckPort(endPoint));
                }
                else
                {
                    tasks[task_i] = Task.Run(() => CheckPort(endPoint));
                    count++;
                    task_i++;
                }

                if (i == check)
                {
                    Console.WriteLine($"{check - threadCount} ports checked");
                    check += 1000;
                }
            }
            Task.WaitAll(tasks);
        }

        private static void CheckPort(IPEndPoint endPoint)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    client.Connect(endPoint);
                    Console.WriteLine($"Port {endPoint.Port} is open on {endPoint.Address}");
                }
            }
            catch (SocketException)
            {
            }
        }
        

        private static void HostScanner()
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            ShowInterfacesList(interfaces);

            bool isEnd = false;
            do
            {
                Console.WriteLine("Select interface(q to exit): ");
                var userInput = Console.ReadLine();
                if (userInput == "q")
                {
                    isEnd = true;
                }
                else if (Int32.TryParse(userInput, out int selected) && selected >= 1 && selected <= interfaces.Length)
                {
                    var interfaceIp = interfaces[selected - 1].GetIPProperties().UnicastAddresses
                        .First(x => x.Address.AddressFamily == AddressFamily.InterNetwork);
                    try
                    {
                        PingAllIp(interfaceIp.Address, interfaceIp.IPv4Mask,
                            interfaces[selected - 1].GetPhysicalAddress().ToString());
                    }
                    catch (PingException)
                    {
                        Console.WriteLine("interface is not available. Try other interface.");
                    }
                }
                else
                {
                    Console.WriteLine("Incorrect input");
                }
            } while (!isEnd);
        }

        private static void ShowInterfacesList(NetworkInterface[] interfaces)
        {
            int interfaceIndex = 1;
            foreach (var adapter in interfaces)
            {
                var ipProperty =
                    adapter.GetIPProperties().UnicastAddresses
                        .First(x => x.Address.AddressFamily == AddressFamily.InterNetwork);
                if (ipProperty != null)
                {
                    Console.WriteLine("Interface Index: {0}", interfaceIndex++);
                    Console.WriteLine("Name: {0}", adapter.Name);
                    Console.WriteLine("Description: {0}", adapter.Description);
                    Console.WriteLine("MAC: {0}", adapter.GetPhysicalAddress());
                    Console.WriteLine("IP: {0}", ipProperty.Address);
                    Console.WriteLine("IP mask: {0}", ipProperty.IPv4Mask);

                    Console.WriteLine("_______________________________________________");
                }
            }
        }

        private static uint BytesToUInt(byte[] bytes)
        {
            uint result = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                result <<= 8;
                result += bytes[i];
            }

            return result;
        }

        private static byte[] UIntToBytes(uint number)
        {
            byte[] result = new byte[4];

            for (int i = 3; i >= 0; i--)
            {
                result[i] = (byte)(number & 0b11111111);
                number >>= 8;
            }

            return result;
        }
 

        private static IPAddress? _curHostIp;
        private static string? _curHostMac;

        private static void PingAllIp(IPAddress hostIp, IPAddress hostMask, string hostMac)
        {
            _curHostIp = hostIp;
            _curHostMac = hostMac;

            var ip = BytesToUInt(hostIp.GetAddressBytes());
            var mask = BytesToUInt(hostMask.GetAddressBytes());

            Task[] tasks = new Task[256];
            int count = 0;
            int task_i = 0;

            uint amount = ~mask;
            ip &= mask;

            for (ulong i = 0; i <= amount; i++, ip++)
            {
                IPAddress tempIp = new IPAddress(UIntToBytes(ip));
                if (count == 256)
                {
                    task_i = Task.WaitAny(tasks);
                    tasks[task_i] = Task.Run(() => PingIp(tempIp));
                }
                else
                {
                    tasks[task_i] = Task.Run(() => PingIp(tempIp));
                    count++;
                    task_i++;
                }
            }

            Task.WaitAll(tasks);
        }

        private static void PingIp(IPAddress address)
        {
            PingReply pingReply;
            using (var pinger = new Ping())
            {
                pingReply = pinger.Send(address, 1000);
            }

            string? mac;

            if (address.Equals(_curHostIp))
            {
                mac = _curHostMac;
            }
            else
            {
                mac = CheckArp(address);
            }

            if (pingReply.Status == IPStatus.Success)
            {
                Console.Write($"Ping: Success, ip: {address}");
                if (mac != null)
                {
                    Console.WriteLine($", mac: {mac}");
                }
                else
                {
                    Console.WriteLine();
                }
            }
            else if (mac != null)
            {
                Console.WriteLine($"Ping: Failed, ip: {address}, mac: {mac}");
            }
        }

        private static string? CheckArp(IPAddress address)
        {
            var p = Process.Start(new ProcessStartInfo("arp",
                String.Concat("-a ", address.ToString(), " ", _curHostIp?.ToString()))
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });

            var output = p?.StandardOutput.ReadToEnd();
            p?.Close();

            return ParseArpResult(output);
        }

        private static string? ParseArpResult(string output)
        {
            var lines = output.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l));

            if (lines.ToList().Count == 1)
            {
                return null;
            }

            var result =
                (from line in lines
                    select Regex.Split(line, @"\s+")
                        .Where(i => !string.IsNullOrWhiteSpace(i)).ToList()
                    into items
                    where items.Count == 3
                    select items).ToList()[0];

            return result.ToList()[1];
        }
    }
}