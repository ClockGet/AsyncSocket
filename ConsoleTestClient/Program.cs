using IOCPClient;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleTestClient
{
    class Program
    {
        static void Main(string[] args)
        {
            Test().Wait();
            Console.ReadKey();
        }
        static async Task Test()
        {
            IPAddress addr = IPAddress.Parse(ConfigurationManager.AppSettings["remoteAddr"]);
            int port = int.Parse(ConfigurationManager.AppSettings["remotePort"]);
            Client client = new Client(new IPEndPoint(addr, port));
            while(true)
            {
                try
                {
                    var result = await client.ConnectAsync(10000);
                    if (result.SocketError == SocketError.Success)
                        break;
                    Console.WriteLine("无法连接，错误：" + result.SocketError);
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    await Task.Delay(1000);
                }
            }
            while(true)
            {
                try
                {
                    var result = await client.ReceiveAsync(client.ConnectSocket, 10000);
                    if(!result.ReceiveSuccess)
                    {
                        Console.WriteLine("没有接收到数据");
                        break;
                    }
                    Console.WriteLine("接收到的数据为：" + client.Encoding.GetString(result.Data));
                    await client.SendAsync(client.ConnectSocket, client.Encoding.GetBytes(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
                }
                catch(Exception ex)
                {
                    Console.WriteLine("接收数据发生错误：" + ex.Message);
                }
            }
        }
    }
}
