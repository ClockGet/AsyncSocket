﻿using System;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace IOCPUtils
{
    public sealed class UserTokenPool
    {
        ConcurrentStack<UserToken> m_pool;
        public UserTokenPool()
        {
            m_pool = new ConcurrentStack<UserToken>();
        }
        public void Push(UserToken item)
        {
            if (item == null) { throw new ArgumentNullException("Items added to a UserTokenPool cannot be null"); }
            m_pool.Push(item);
        }
        public void Reture(UserToken token)
        {
            token.TimeOut = -1;
            token.Id = Guid.Empty.ToString();
            token.ConnectSocket = null;
            token.ReceiveArgs.RemoteEndPoint = null;
            Push(token);
        }
        private void InitItem(UserToken token)
        {
            token.Id = Guid.NewGuid().ToString();
        }
        public UserToken Pop()
        {
            UserToken item = null;
            if (m_pool.TryPop(out item))
            {
                InitItem(item);
                return item;
            }
            return item;
        }
        public int Count
        {
            get
            {
                return m_pool.Count;
            }
        }
        public void Clear()
        {
            m_pool.Clear();
        }
    }
}
