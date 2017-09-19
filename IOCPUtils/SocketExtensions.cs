using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace IOCPUtils
{
    public static class SocketExtensions
    {
        public static Task<SocketResult> ConnectAsync(this Socket socket, IOCPBase iocpBase, UserToken userToken)
        {
            var task = userToken.CompletionSource;
            if(!socket.ConnectAsync(userToken.ReceiveArgs))
            {
                SocketResult result = new SocketResult { SocketError = userToken.ReceiveArgs.SocketError, Args = userToken.ReceiveArgs };
                iocpBase.ProcessConnect(userToken.ReceiveArgs, result);
            }
            return task;
        }
        public static Task<SocketResult> ReceiveAsync(this Socket socket,IOCPBase iocpBase, UserToken userToken)
        {
            var task = userToken.CompletionSource;
            if(!socket.ReceiveAsync(userToken.ReceiveArgs))
            {
                SocketResult result = new SocketResult { SocketError = userToken.ReceiveArgs.SocketError, Args = userToken.ReceiveArgs };
                iocpBase.ProcessReceive(userToken.ReceiveArgs, result);
            }
            return task;
        }
        public static Task<SocketResult> SendAsync(this Socket socket, byte[] data, IOCPBase iocpBase, UserToken userToken)
        {
            if (data.Length>iocpBase.BufferSize)
            {
                SendPacketsElement[] elements = new SendPacketsElement[1];
                elements[0] = new SendPacketsElement(data,0,data.Length,true);
                return SendDataAsync(socket, elements, iocpBase, userToken);
            }
            var task = userToken.CompletionSource;
            Array.Copy(data, 0, userToken.ReceiveArgs.Buffer, userToken.ReceiveArgs.Offset, data.Length);
            userToken.ReceiveArgs.SetBuffer(userToken.ReceiveArgs.Offset, data.Length);
            if(!socket.SendAsync(userToken.ReceiveArgs))
            {
                SocketResult result = new SocketResult { SocketError = userToken.ReceiveArgs.SocketError, Args = userToken.ReceiveArgs };
                iocpBase.ProcessSend(userToken.ReceiveArgs, result);
            }
            return task;
        }
        public static Task<SocketResult> SendFileAsync(this Socket socket, string fileName, IOCPBase iocpBase, UserToken userToken)
        {
            SendPacketsElement[] elements = new SendPacketsElement[1];
            elements[0] = new SendPacketsElement(fileName,0,0,true);
            return SendDataAsync(socket, elements, iocpBase, userToken);
        }
        static Task<SocketResult> SendDataAsync(Socket socket, SendPacketsElement[] elements, IOCPBase iocpBase, UserToken userToken)
        {
            var task = userToken.CompletionSource;
            userToken.ReceiveArgs.SendPacketsElements = elements;
            userToken.ReceiveArgs.SendPacketsFlags = TransmitFileOptions.UseKernelApc;
            if(!socket.SendPacketsAsync(userToken.ReceiveArgs))
            {
                SocketResult result = new SocketResult { SocketError = userToken.ReceiveArgs.SocketError, Args = userToken.ReceiveArgs };
                iocpBase.ProcessSendPackets(userToken.ReceiveArgs, result);
            }
            return task;
        }
    }
}
