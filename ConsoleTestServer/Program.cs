using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IOCPServer;
using IOCPUtils;

namespace ConsoleTestServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Server server = new Server(8088, 50);
            server.OnAccpetErrored += Server_OnErrored;
            server.Start(Server_OnAccepted);
            Console.WriteLine("服务器已经启动...");
            Console.ReadKey();
            server.Stop();
            Console.WriteLine("服务器已经关闭...");
        }

        private static async Task Server_OnErrored(Exception ex, params object[] args)
        {
            await Console.Out.WriteLineAsync("服务器出现错误：" + ex.Message);
        }

        private static async Task Server_OnAccepted(IOCPBase server, int num, System.Net.Sockets.Socket socket)
        {
            while(true)
            {
                try
                {
                    SocketResult result = await server.SendAsync(socket, Encoding.UTF8.GetBytes(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")));
                    
                    Console.WriteLine("send result:"+result.SocketError);
                    if(result.SocketError!=System.Net.Sockets.SocketError.Success)
                    {
                        server.CloseClientSocket(socket);
                        return;
                    }
                    result = await server.ReceiveAsync(socket);
                    Console.WriteLine("receive result:" + result.SocketError);
                    if (result.SocketError != System.Net.Sockets.SocketError.Success)
                    {
                        server.CloseClientSocket(socket);
                        return;
                    }
                    await Task.Delay(1000);
                }
                catch(Exception ex)
                {
                    Console.WriteLine("error:" + ex.Message);
                    server.CloseClientSocket(socket);
                    return;
                }
            }
        }
    }
}
