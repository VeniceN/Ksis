using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

class MyTraceroute
{
    const int MaxHops = 30;      // Максимальное количество узлов маршрута
    const int Timeout = 3000;    // Тайм-аут ожидания ответа (3 секунды)
    const int PacketSize = 32;   // Размер ICMP-пакета в байтах
    const int ProbesPerHop = 3;  // Количество запросов на каждый TTL

    static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Аргументы не переданы");
            return;
        }

        // Получение целевого хоста
        string targetHost = args[0];
        bool resolveNames = args.Length > 1 && args[1] == "-resolve";

        // Преобразование хоста в IP-адрес
        if (!IPAddress.TryParse(targetHost, out IPAddress targetIP))
        {
            try
            {
                targetIP = Dns.GetHostAddresses(targetHost)[0];
            }
            catch
            {
                Console.WriteLine("Ошибка хоста");
                return;
            }
        }

        Console.WriteLine($"\nТрассировка маршрута к {targetHost} [{targetIP}]");
        Console.WriteLine($"с максимальным числом прыжков {MaxHops}:\n");

        // Создание ICMP-сокета
        using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp))
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, Timeout);
            EndPoint endPoint = new IPEndPoint(targetIP, 0);

            for (int ttl = 1; ttl <= MaxHops; ttl++)
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, ttl);
                Console.Write($"{ttl,2}  "); // Вывод номера прыжка с выравниванием

                bool received = false;
                string lastRouter = "";
                string times = "";

                for (int probe = 1; probe <= ProbesPerHop; probe++) 
                {
                    ushort sequenceNumber = (ushort)((ttl - 1) * 256 + probe * 256);
                    byte[] icmpPacket = CreateIcmpPacket(sequenceNumber); // Создание ICMP-пакета

                    var stopwatch = Stopwatch.StartNew(); // Запуск таймера
                    socket.SendTo(icmpPacket, endPoint); // Отправка пакета

                    try
                    {
                        byte[] buffer = new byte[512];
                        EndPoint senderEP = new IPEndPoint(IPAddress.Any, 0);
                        socket.ReceiveFrom(buffer, ref senderEP); // Получение ответа
                        stopwatch.Stop(); // Остановка таймера

                        // Получение IP-адреса узла
                        IPAddress routerIP = ((IPEndPoint)senderEP).Address;
                        string host = resolveNames ? GetHostName(routerIP) : routerIP.ToString();
                        lastRouter = host;  
                        times += $"{stopwatch.ElapsedMilliseconds,3} ms  "; 
                        received = true;
                    }
                    catch (SocketException)
                    {
                        times += "*    ";
                    }
                }

                Console.WriteLine($"{times} {lastRouter}");
                if (lastRouter == targetIP.ToString())
                {
                    Console.WriteLine("\nТрассировка завершена.");
                    return;
                }
            }
        }
    }

    // Создание ICMP-запроса с уникальным Sequence Number
    static byte[] CreateIcmpPacket(ushort sequenceNumber)
    {
        byte[] packet = new byte[PacketSize];
        packet[0] = 8; 
        packet[1] = 0; 
        BitConverter.GetBytes((ushort)0).CopyTo(packet, 2); // Checksum 
        BitConverter.GetBytes((ushort)1).CopyTo(packet, 4); // Identifier
        BitConverter.GetBytes(sequenceNumber).CopyTo(packet, 6); // Sequence Number

        // Вычисление контрольной суммы
        int checksum = ComputeChecksum(packet);
        BitConverter.GetBytes((ushort)checksum).CopyTo(packet, 2);

        return packet;
    }

    // Вычисление контрольной суммы (Checksum)
    static int ComputeChecksum(byte[] data)
    {
        int checksum = 0;
        for (int i = 0; i < data.Length; i += 2)
        {
            checksum += BitConverter.ToUInt16(data, i);
        }
        checksum = (checksum >> 16) + (checksum & 0xFFFF);
        return ~checksum & 0xFFFF;
    }

    // Получение имени хоста по IP-адресу
    static string GetHostName(IPAddress ip)
    {
        try
        {
            return Dns.GetHostEntry(ip).HostName;
        }
        catch
        {
            return ip.ToString();
        }
    }
}
