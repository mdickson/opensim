/*
 * Copyright (c) 2008, openmetaverse.org, http://opensimulator.org/
 * All rights reserved.
 *
 * - Redistribution and use in source and binary forms, with or without
 *   modification, are permitted provided that the following conditions are met:
 *
 * - Redistributions of source code must retain the above copyright notice, this
 *   list of conditions and the following disclaimer.
 * - Neither the name of the openmetaverse.org nor the names
 *   of its contributors may be used to endorse or promote products derived from
 *   this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 * POSSIBILITY OF SUCH DAMAGE.
 */

// this is a lighter alternative to libomv, no sliding option

using System;
using System.Threading;
using System.Collections.Generic;
using Timer = System.Threading.Timer ;

namespace OpenSim.Framework
{
    public sealed class ExpiringCacheOS<TKey1, TValue1> : IDisposable
    {
        private const int MINEXPIRECHECK = 500;

        private Timer m_purgeTimer;
        private ReaderWriterLockSlim m_rwLock;
        private readonly Dictionary<TKey1, int> m_expireControl;
        private readonly Dictionary<TKey1, TValue1> m_values;
        private readonly double m_startTS;
        private readonly int m_expire;

        public ExpiringCacheOS()
        {
            m_expireControl = new Dictionary<TKey1, int>();
            m_values = new Dictionary<TKey1, TValue1>();
            m_rwLock = new ReaderWriterLockSlim();
            m_expire = MINEXPIRECHECK;
            m_startTS = Util.GetTimeStampMS();
        }

        public ExpiringCacheOS(int expireCheckTimeinMS)
        {
            m_expireControl = new Dictionary<TKey1, int>();
            m_values = new Dictionary<TKey1, TValue1>();
            m_rwLock = new ReaderWriterLockSlim();
            m_startTS = Util.GetTimeStampMS();
            m_expire = (expireCheckTimeinMS > MINEXPIRECHECK) ? m_expire = expireCheckTimeinMS : MINEXPIRECHECK;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void CheckTimer()
        {
            if (m_purgeTimer == null)
            {
                m_purgeTimer = new Timer(Purge, null, m_expire, Timeout.Infinite);
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void DisposeTimer()
        {
            if (m_purgeTimer != null)
            {
                m_purgeTimer.Dispose();
                m_purgeTimer = null;
            }
        }

        ~ExpiringCacheOS()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (m_rwLock != null)
            {
                DisposeTimer();
                m_rwLock.Dispose();
                m_rwLock = null;
            }
        }

        private void Purge(object ignored)
        {
            bool gotLock = false;
            int now = (int)(Util.GetTimeStampMS() - m_startTS);

            try
            {
                try { }
                finally
                {
                    m_rwLock.EnterWriteLock();
                    gotLock = true;
                }

                if (m_expireControl.Count == 0)
                {
                    DisposeTimer();
                    return;
                }

                List<TKey1> expired = new List<TKey1>(m_expireControl.Count);
                foreach(KeyValuePair<TKey1, int> kvp in m_expireControl)
                {
                    if(kvp.Value < now)
                        expired.Add(kvp.Key);
                }
                foreach(TKey1 key in expired)
                {
                    m_expireControl.Remove(key);
                    m_values.Remove(key);
                }
                if(m_expireControl.Count == 0)
                    DisposeTimer();
                else
                    m_purgeTimer.Change(m_expire, Timeout.Infinite);
            }
            finally
            {
                if (gotLock)
                    m_rwLock.ExitWriteLock();
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void AddOrUpdate(TKey1 key, TValue1 val)
        {
            Add(key, val);
        }

        public void Add(TKey1 key, TValue1 val)
        {
            bool gotLock = false;
            int now = (int)(Util.GetTimeStampMS() - m_startTS) + m_expire;

            try
            {
                try { }
                finally
                {
                    m_rwLock.EnterWriteLock();
                    gotLock = true;
                }

                m_expireControl[key] = now;
                m_values[key] = val;
                CheckTimer();
            }
            finally
            {
                if (gotLock)
                    m_rwLock.ExitWriteLock();
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void AddOrUpdate(TKey1 key, TValue1 val, int expireSeconds)
        {
            Add(key, val, expireSeconds * 1000);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void AddOrUpdate(TKey1 key, TValue1 val, double expireSeconds)
        {
            Add(key, val, (int)(expireSeconds * 1000));
        }

        public void Add(TKey1 key, TValue1 val, int expireMS)
        {
            bool gotLock = false;
            expireMS = (expireMS > m_expire) ? expireMS : m_expire;
            int now = (int)(Util.GetTimeStampMS() - m_startTS) + expireMS;

            try
            {
                try { }
                finally
                {
                    m_rwLock.EnterWriteLock();
                    gotLock = true;
                }

                m_expireControl[key] = now;
                m_values[key] = val;
                CheckTimer();
            }
            finally
            {
                if (gotLock)
                    m_rwLock.ExitWriteLock();
            }
        }

        public bool Remove(TKey1 key)
        {
            bool success;
            bool gotLock = false;

            try
            {
                try {}
                finally
                {
                    m_rwLock.EnterWriteLock();
                    gotLock = true;
                }
                success = m_expireControl.Remove(key);
                success |= m_values.Remove(key);
                if(m_expireControl.Count == 0)
                    DisposeTimer();
            }
            finally
            {
                if (gotLock)
                    m_rwLock.ExitWriteLock();
            }

            return success;
        }

        public void Clear()
        {
            bool gotLock = false;

            try
            {
                try {}
                finally
                {
                    m_rwLock.EnterWriteLock();
                    gotLock = true;
                }
                DisposeTimer();
                m_expireControl.Clear();
                m_values.Clear();
            }
            finally
            {
                if (gotLock)
                    m_rwLock.ExitWriteLock();
            }
        }

        public int Count
        {
            get { return m_expireControl.Count; }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public bool Contains(TKey1 key)
        {
            return ContainsKey(key);
        }

        public bool ContainsKey(TKey1 key)
        {
            bool gotLock = false;
            try
            {
                try { }
                finally
                {
                    m_rwLock.EnterReadLock();
                    gotLock = true;
                }
                return m_expireControl.ContainsKey(key);
            }
            finally
            {
                if (gotLock)
                    m_rwLock.ExitReadLock();
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public bool Contains(TKey1 key, int expireMS)
        {
            return ContainsKey(key, expireMS);
        }

        public bool ContainsKey(TKey1 key, int expireMS)
        {
            bool gotLock = false;
            try
            {
                try { }
                finally
                {
                    m_rwLock.EnterUpgradeableReadLock();
                    gotLock = true;
                }
                if(m_expireControl.ContainsKey(key))
                {
                    bool gotWriteLock = false;
                    try
                    {
                        try { }
                        finally
                        {
                            m_rwLock.EnterWriteLock();
                            gotWriteLock = true;
                        }
                        expireMS = (expireMS > m_expire) ? expireMS : m_expire;
                        int now = (int)(Util.GetTimeStampMS() - m_startTS) + expireMS;

                        m_expireControl[key] = now;
                        return true;
                    }
                    finally
                    {
                        if (gotWriteLock)
                            m_rwLock.ExitWriteLock();
                    }
                }
                return false;
            }
            finally
            {
                if (gotLock)
                    m_rwLock.ExitUpgradeableReadLock();
            }
        }

        public bool TryGetValue(TKey1 key, out TValue1 value)
        {
            bool success;
            bool gotLock = false;

            try
            {
                try {}
                finally
                {
                    m_rwLock.EnterReadLock();
                    gotLock = true;
                }

                success = m_values.TryGetValue(key, out value);
            }
            finally
            {
                if (gotLock)
                    m_rwLock.ExitReadLock();
            }

            return success;
        }

        public bool TryGetValue(TKey1 key, int expireMS, out TValue1 value)
        {
            bool success;
            bool gotLock = false;

            try
            {
                try { }
                finally
                {
                    m_rwLock.EnterUpgradeableReadLock();
                    gotLock = true;
                }

                success = m_values.TryGetValue(key, out value);
                if(success)
                {
                    bool gotWriteLock = false;
                    try
                    {
                        try { }
                        finally
                        {
                            m_rwLock.EnterWriteLock();
                            gotWriteLock = true;
                        }
                        expireMS = (expireMS > m_expire) ? expireMS : m_expire;
                        int now = (int)(Util.GetTimeStampMS() - m_startTS) + expireMS;

                        m_expireControl[key] = now;
                    }
                    finally
                    {
                        if (gotWriteLock)
                            m_rwLock.ExitWriteLock();
                    }
                }
            }
            finally
            {
                if (gotLock)
                    m_rwLock.ExitUpgradeableReadLock();
            }

            return success;
        }

    }
}