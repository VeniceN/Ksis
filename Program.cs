using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class P2PChat
{
    private static int udpPort = 50000;
    private static int tcpPort = 50001;
    private static string userName;
    private static List<TcpClient> peers = new List<TcpClient>();
    private static TcpListener tcpListener;
    private static UdpClient udpClient;
    private static IPAddress localAddress;

    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: P2PChat <IP Address> <UserName>");
            return;
        }

        localAddress = IPAddress.Parse(args[0]);
        userName = args[1];

        udpClient = new UdpClient(new IPEndPoint(localAddress, udpPort))
        {
            EnableBroadcast = true
        };
        tcpListener = new TcpListener(localAddress, tcpPort);

        Thread udpThread = new Thread(new ThreadStart(ReceiveBroadcasts));
        udpThread.Start();

        Thread tcpThread = new Thread(new ThreadStart(StartTCPServer));
        tcpThread.Start();

        SendBroadcast();
        ChatLoop();
    }

    static void ReceiveBroadcasts()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, udpPort);
        try
        {
            while (true)
            {
                byte[] receivedBytes = udpClient.Receive(ref remoteEP);
                string message = Encoding.UTF8.GetString(receivedBytes);

                // Пропускаем сообщения, отправленные самим собой
                if (remoteEP.Address.Equals(localAddress))
                    continue;

                if (message.StartsWith("EXIT:"))
                {
                    string exitingUser = message.Substring(5);
                    Console.WriteLine($"[INFO] {exitingUser} ({remoteEP.Address}) вышел из чата.");
                    return;
                }

                Console.WriteLine($"[INFO] {remoteEP.Address} joined as {message}");
                ConnectToPeer(remoteEP.Address);
            }
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"[ERROR] UDP Socket error: {ex.Message}");
        }
        finally
        {
            udpClient?.Close();
        }
    }

    static void SendBroadcast()
    {
        byte[] sendBytes = Encoding.UTF8.GetBytes(userName);
        udpClient.Send(sendBytes, sendBytes.Length, new IPEndPoint(IPAddress.Broadcast, udpPort));
    }

    static void StartTCPServer()
    {
        try
        {
            tcpListener.Start();
            while (true)
            {
                TcpClient client = tcpListener.AcceptTcpClient();
                lock (peers) peers.Add(client);
                Thread clientThread = new Thread(() => HandleClient(client));
                clientThread.Start();
            }
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"[ERROR] TCP Socket error: {ex.Message}");
        }
        finally
        {
            tcpListener?.Stop();
        }
    }

    static void ConnectToPeer(IPAddress peerAddress)
    {
        lock (peers)
        {
            // Проверяем, есть ли уже соединение с данным адресом
            if (peers.Any(peer =>
            {
                if (peer.Client.RemoteEndPoint is IPEndPoint endPoint)
                    return endPoint.Address.Equals(peerAddress);
                return false;
            }))
            {
                return;
            }
        }

        try
        {
            TcpClient client = new TcpClient();
            client.Connect(peerAddress, tcpPort);
            lock (peers) peers.Add(client);
            Thread clientThread = new Thread(() => HandleClient(client));
            clientThread.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to connect to {peerAddress}: {ex.Message}");
        }
    }

    static void HandleClient(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[1024];
        try
        {
            while (true)
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Console.WriteLine(message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Client disconnected: {ex.Message}");
        }
        finally
        {
            lock (peers)
            {
                peers.Remove(client);
            }
            client.Close();
        }
    }

    static void ChatLoop()
    {
        while (true)
        {
            string message = Console.ReadLine();
            if (string.IsNullOrEmpty(message)) continue;

            if (message == "/exit") // Команда выхода
            {
                SendExitNotification(); // Уведомляем всех об отключении
                CloseAllConnections();
                Environment.Exit(0);
            }

            string fullMessage = $"{userName}: {message}";
            byte[] data = Encoding.UTF8.GetBytes(fullMessage);

            lock (peers)
            {
                foreach (var peer in peers.ToList()) // Создаем копию списка
                {
                    try
                    {
                        peer.GetStream().Write(data, 0, data.Length);
                    }
                    catch
                    {
                        peers.Remove(peer);
                    }
                }
            }

            Console.WriteLine(fullMessage);
        }
    }

    static void SendExitNotification()
    {
        byte[] exitMessage = Encoding.UTF8.GetBytes($"EXIT:{userName}");
        udpClient.Send(exitMessage, exitMessage.Length, new IPEndPoint(IPAddress.Broadcast, udpPort));
    }

    static void CloseAllConnections()
    {
        lock (peers)
        {
            foreach (var peer in peers)
            {
                peer.Close();
            }
            peers.Clear();
        }
        tcpListener.Stop();
        udpClient.Close();
    }
}