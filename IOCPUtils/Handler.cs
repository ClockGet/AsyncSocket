using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace IOCPUtils
{
    public delegate Task OnAcceptedHandler(IOCPBase server, int num, Socket socket);
    public delegate Task OnServerErrorHandler(Exception ex, params object[] args);
}
