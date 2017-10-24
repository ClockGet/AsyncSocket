using IOCPUtils;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IOCPServer
{
    public class Server : IOCPBase,IDisposable
    {
        const int opsToPreAlloc = 2;
        const int _waitResource = 0;
        const int _accepting = 1;
        const int _accepted = 2;
        #region Fields

        /// <summary>  
        /// 服务器程序允许的最大客户端连接数  
        /// </summary>  
        private int _maxClient;

        /// <summary>  
        /// 监听Socket，用于接受客户端的连接请求  
        /// </summary>  
        private Socket _serverSock;

        /// <summary>  
        /// 当前的连接的客户端数  
        /// </summary>  
        private int _clientCount;

        /// <summary>  
        /// 用于每个I/O Socket操作的缓冲区大小  
        /// </summary>  
        private int _bufferSize = 1024;

        /// <summary>
        /// 发送和接收超时
        /// </summary>
        private int _timeOut = 120;

        /// <summary>
        /// 队列排队超时
        /// </summary>
        private int _acceptTimeOut = 30 * 1000;

        /// <summary>  
        /// 信号量  
        /// </summary>  
        private SemaphoreSlim _maxAcceptedClients;

        /// <summary>  
        /// 缓冲区管理  
        /// </summary>  
        private BufferManager _bufferManager;

        /// <summary>  
        /// 对象池  
        /// </summary>  
        private UserTokenPool _objectPool;

        /// <summary>
        /// 已连接Socket集合
        /// </summary>
        private ConcurrentDictionary<IntPtr, Socket> _connectedSockets;

        /// <summary>
        /// 监听socket状态
        /// </summary>
        private int _state;

        #endregion

        #region Events

        private OnAcceptedHandler OnAccepted;

        public event OnServerErrorHandler OnAccpetErrored;

        protected void RaiseOnAccepted(int num, Socket socket)
        {
            OnAccepted?.BeginInvoke(this, num, socket, null, null);
        }

        protected void RaiseOnErrored(Exception ex, params object[] args)
        {
            OnAccpetErrored?.BeginInvoke(ex, args, null, null);
        }

        #endregion

        #region Properties

        /// <summary>  
        /// 服务器是否正在运行  
        /// </summary>  
        public bool IsRunning { get; private set; }
        /// <summary>  
        /// 监听的IP地址  
        /// </summary>  
        public IPAddress Address { get; private set; }
        /// <summary>  
        /// 监听的端口  
        /// </summary>  
        public int Port { get; private set; }
        /// <summary>  
        /// 通信使用的编码  
        /// </summary>  
        public Encoding Encoding { get; set; }

        public override int BufferSize
        {
            get
            {
                return _bufferSize;
            }

            protected set
            {
                _bufferSize = value;
            }
        }

        #endregion

        #region Ctors

        /// <summary>  
        /// 异步IOCP SOCKET服务器  
        /// </summary>  
        /// <param name="listenPort">监听的端口</param>  
        /// <param name="maxClient">最大的客户端数量</param>  
        public Server(int listenPort, int maxClient)
            : this(IPAddress.Any, listenPort, maxClient, Encoding.UTF8)
        {
        }

        /// <summary>  
        /// 异步Socket TCP服务器  
        /// </summary>  
        /// <param name="localEP">监听的终结点</param>  
        /// <param name="maxClient">最大客户端数量</param>  
        public Server(IPEndPoint localEP, int maxClient)
            : this(localEP.Address, localEP.Port, maxClient, Encoding.UTF8)
        {
        }

        /// <summary>  
        /// 异步Socket TCP服务器  
        /// </summary>  
        /// <param name="localIPAddress">监听的IP地址</param>  
        /// <param name="listenPort">监听的端口</param>  
        /// <param name="maxClient">最大客户端数量</param>  
        public Server(IPAddress localIPAddress, int listenPort, int maxClient, Encoding encoding, int timeOut = 120)
        {
            this.Address = localIPAddress;
            this.Port = listenPort;
            this.Encoding = encoding;

            _maxClient = maxClient;

            _serverSock = new Socket(localIPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            _bufferManager = new BufferManager(_bufferSize * _maxClient * opsToPreAlloc, _bufferSize);

            _objectPool = new UserTokenPool();

            _maxAcceptedClients = new SemaphoreSlim(_maxClient, _maxClient);

            _connectedSockets = new ConcurrentDictionary<IntPtr, Socket>(DefaultConcurrencyLevel, _maxClient);
        }

        #endregion

        #region Initialize

        private void Init()
        {
            // Allocates one large byte buffer which all I/O operations use a piece of.  This gaurds   
            // against memory fragmentation  
            _bufferManager.InitBuffer();

            // preallocate pool of SocketAsyncEventArgs objects  
            SocketAsyncEventArgs readWriteEventArg;
            UserToken userToken;
            for (int i = 0; i < _maxClient; i++)
            {
                //Pre-allocate a set of reusable SocketAsyncEventArgs  
                readWriteEventArg = new SocketAsyncEventArgs();
                readWriteEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(OnIOCompleted);

                // assign a byte buffer from the buffer pool to the SocketAsyncEventArg object  
                _bufferManager.SetBuffer(readWriteEventArg);
                userToken = new UserToken();
                userToken.ReceiveArgs = readWriteEventArg;
                readWriteEventArg.UserToken = userToken;
                // add SocketAsyncEventArg to the pool  
                _objectPool.Push(userToken);
            }
        }

        #endregion

        #region Start

        public void Start(OnAcceptedHandler onAccepted)
        {
            if (!IsRunning)
            {
                if (onAccepted == null)
                    throw new InvalidOperationException();
                Init();
                IsRunning = true;
                IPEndPoint localEndPoint = new IPEndPoint(Address, Port);
                //创建监听socket
                _serverSock = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _serverSock.ReceiveTimeout = _timeOut;
                _serverSock.SendTimeout = _timeOut;
                _serverSock.ReceiveBufferSize = _bufferSize;
                if (localEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    // 配置监听socket为 dual-mode (IPv4 & IPv6)   
                    // 27 is equivalent to IPV6_V6ONLY socket option in the winsock snippet below,  
                    _serverSock.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)27, false);
                    _serverSock.Bind(new IPEndPoint(IPAddress.IPv6Any, localEndPoint.Port));
                }
                else
                {
                    _serverSock.Bind(localEndPoint);
                }
                this.OnAccepted = onAccepted;
                //开始监听
                _serverSock.Listen(this._maxClient);
                StartAccept(null);
            }
        }

        #endregion

        #region Stop

        public void Stop()
        {
            if (IsRunning)
            {
                IsRunning = false;
                if(_state==_accepting)
                {
                    try
                    {
                        _maxAcceptedClients.Release();
                    }
                    catch(SemaphoreFullException)
                    {

                    }
                }
                _serverSock.Close();
                _objectPool.Clear();
                SpinWait spinWait = default(SpinWait);
                do
                {
                    spinWait.SpinOnce();
                    var sockets = _connectedSockets.Values.ToArray();
                    for(int i=0;i<sockets.Length;i++)
                    {
                        CloseClientSocket(sockets[i]);
                    }
                }
                while (_maxAcceptedClients.CurrentCount < _maxClient);
                _connectedSockets.Clear();
            }
        }

        #endregion

        #region Accept
        private void StartAccept(SocketAsyncEventArgs asyniar)
        {
            if (!IsRunning)
                return;
            if (asyniar == null)
            {
                asyniar = new SocketAsyncEventArgs();
                asyniar.Completed += new EventHandler<SocketAsyncEventArgs>(OnAcceptCompleted);
            }
            else
            {
                //socket must be cleared since the context object is being reused
                asyniar.AcceptSocket = null;
            }
            Interlocked.Exchange(ref _state, _waitResource);
            if(!_maxAcceptedClients.Wait(_acceptTimeOut))
            {
                RaiseOnErrored(new Exception(string.Format("获取可用连接资源超时，总资源数：{0}",_maxClient)));
                Task.Run(() => 
                {
                    StartAccept(asyniar);
                });
                return;
            }
            Interlocked.Exchange(ref _state, _accepting);
            if (!_serverSock.AcceptAsync(asyniar))
            {
                ProcessAccept(asyniar);
            }
        }

        private void OnAcceptCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            Interlocked.Exchange(ref _state, _accepted);
            if (e.SocketError == SocketError.Success)
            {
                Socket s = e.AcceptSocket;
                if (s.Connected)
                {
                    try
                    {
                        Interlocked.Increment(ref _clientCount);
                        SetSocketOptions(s);
                        _connectedSockets.TryAdd(s.Handle, s);
#if DEBUG
                        Log4Debug(String.Format("客户 {0} 连入, 共有 {1} 个连接。", s.RemoteEndPoint.ToString(), _clientCount));
#endif
                        RaiseOnAccepted(_clientCount, s);
                    }
                    catch (SocketException ex)
                    {
#if DEBUG
                        Log4Debug(String.Format("接收客户 {0} 数据出错, 异常信息： {1} 。", s.RemoteEndPoint, ex.ToString()));
#endif
                        RaiseOnErrored(new Exception(string.Format("接受客户 {0} 连接出错, 异常信息： {1} .", s.RemoteEndPoint, ex.ToString())));
                    }
                }
            }
            StartAccept(e);
        }
        #endregion

        #region Async Function

        #region Receive

        public override Task<SocketResult> ReceiveAsync(Socket socket, int timeOut=-1)
        {
            if (socket == null || !socket.Connected)
                return Task.FromResult(SocketResult.NotSocket);
            var userToken = _objectPool.Pop();
            userToken.ConnectSocket = socket;
            userToken.TimeOut = timeOut;
            return socket.ReceiveAsync(this, userToken);
        }

        #endregion

        #region Send

        public override Task<SocketResult> SendAsync(Socket socket, byte[] data, int timeOut=-1)
        {
            if(socket == null || !socket.Connected)
                return Task.FromResult(SocketResult.NotSocket);
            var userToken = _objectPool.Pop();
#if DEBUG
            Console.WriteLine($"SendAsync Create a new UserToken : id: {userToken.Id}");
#endif
            userToken.ConnectSocket = socket;
            userToken.TimeOut = timeOut;
            return socket.SendAsync(data, this, userToken);
        }

        public override Task<SocketResult> SendFileAsync(Socket socket, string fileName, int timeOut=-1)
        {
            if (socket == null || !socket.Connected)
                return Task.FromResult(SocketResult.NotSocket);
            var userToken = _objectPool.Pop();
            userToken.ConnectSocket = socket;
            userToken.TimeOut = timeOut;
            return socket.SendFileAsync(fileName, this, userToken);
        }

        #endregion

        #region Disconnect
        public override Task<SocketResult> DisconnectAsync(Socket socket, int timeOut = -1)
        {
            if (socket == null || !socket.Connected)
                return Task.FromResult(SocketResult.NotSocket);
            var userToken = _objectPool.Pop();
            userToken.ConnectSocket = socket;
            userToken.TimeOut = timeOut;
            userToken.ReceiveArgs.SetBuffer(userToken.ReceiveArgs.Offset, 0);
            return socket.DisconnectAsync(this, userToken);
        }

        #endregion

        #endregion

        #region Close

        public override void CloseClientSocket(Socket s)
        {
            try
            {
                if (s.IsDisposed())
                    return;
                int r = Interlocked.Decrement(ref _clientCount);
                Socket temp;
                _connectedSockets.TryRemove(s.Handle, out temp);
                temp = null;
#if DEBUG
                Log4Debug(String.Format("客户 {0} 断开连接! 剩余 {1} 个连接", s.RemoteEndPoint.ToString(), r));
#endif
                if (s.Connected)
                    s.Shutdown(SocketShutdown.Both);
                _maxAcceptedClients.Release();
            }
            catch(SemaphoreFullException)
            {

            }
            finally
            {
                s.Close();
            }
            
        }

        protected override void RecycleToken(UserToken token)
        {
            _bufferManager.ResetBuffer(token);
            _objectPool.Reture(token);
        }
        #endregion

#if DEBUG
        private void Log4Debug(string msg)
        {
            Console.WriteLine($"notice: {msg}");
        }
#endif
        #region IDisposable Support
        private bool disposedValue = false; // 要检测冗余调用

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    try
                    {
                        Stop();
                        if(_serverSock!=null)
                        {
                            _serverSock = null;
                        }
                    }
                    catch
                    {

                    }
                }
                disposedValue = true;
            }
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

    }
}
