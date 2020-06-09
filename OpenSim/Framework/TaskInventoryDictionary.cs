/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using log4net;
using OpenMetaverse;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace OpenSim.Framework
{
    /// <summary>
    /// A dictionary containing task inventory items.  Indexed by item UUID.
    /// </summary>
    /// <remarks>
    /// This class is not thread safe.  Callers must synchronize on Dictionary methods or Clone() this object before
    /// iterating over it.
    /// </remarks>
    public class TaskInventoryDictionary : Dictionary<UUID, TaskInventoryItem>, ICloneable, IXmlSerializable, IDisposable
    {
        // private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static XmlSerializer tiiSerializer = new XmlSerializer(typeof(TaskInventoryItem));
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Thread LockedByThread;
        //        private string WriterStack;

        //        private Dictionary<Thread, string> ReadLockers =
        //                new Dictionary<Thread, string>();

        /// <value>
        /// An advanced lock for inventory data
        /// </value>
        private volatile System.Threading.ReaderWriterLockSlim m_itemLock = new System.Threading.ReaderWriterLockSlim();

        ~TaskInventoryDictionary()
        {
            Dispose(false);
        }

        private bool disposed = false;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called.
            if (!disposed)
            {
                m_itemLock.Dispose();
                m_itemLock = null;
                disposed = true;
            }
        }

        /// <summary>
        /// Are we readlocked by the calling thread?
        /// </summary>
        public bool IsReadLockedByMe()
        {
            if (m_itemLock.RecursiveReadCount > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Lock our inventory list for reading (many can read, one can write)
        /// </summary>
        public void LockItemsForRead(bool locked)
        {
            if (locked)
            {
                if (m_itemLock.IsWriteLockHeld && LockedByThread != null)
                {
                    if (!LockedByThread.IsAlive)
                    {
                        //Locked by dead thread, reset.
                        m_itemLock = new System.Threading.ReaderWriterLockSlim();
                    }
                }

                if (m_itemLock.RecursiveReadCount > 0)
                {
                    m_log.Error("[TaskInventoryDictionary] Recursive read lock requested. This should not happen and means something needs to be fixed. For now though, it's safe to continue.");
                    try
                    {
                        // That call stack is useful for end users only. RealProgrammers need a full dump. Commented.
                        // StackTrace stackTrace = new StackTrace();           // get call stack
                        // StackFrame[] stackFrames = stackTrace.GetFrames();  // get method calls (frames)
                        //
                        // // write call stack method names
                        // foreach (StackFrame stackFrame in stackFrames)
                        // {
                        //     m_log.Error("[SceneObjectGroup.m_parts]  "+(stackFrame.GetMethod().Name));   // write method name
                        // }

                        // The below is far more useful
                        //                        System.Console.WriteLine("------------------------------------------");
                        //                        System.Console.WriteLine("My call stack:\n" + Environment.StackTrace);
                        //                        System.Console.WriteLine("------------------------------------------");
                        //                        foreach (KeyValuePair<Thread, string> kvp in ReadLockers)
                        //                        {
                        //                            System.Console.WriteLine("Locker name {0} call stack:\n" + kvp.Value, kvp.Key.Name);
                        //                            System.Console.WriteLine("------------------------------------------");
                        //                        }
                    }
                    catch
                    { }
                    m_itemLock.ExitReadLock();
                }
                if (m_itemLock.RecursiveWriteCount > 0)
                {
                    m_log.Error("[TaskInventoryDictionary] Recursive write lock requested. This should not happen and means something needs to be fixed.");
                    //                    try
                    //                    {
                    //                        System.Console.WriteLine("------------------------------------------");
                    //                        System.Console.WriteLine("My call stack:\n" + Environment.StackTrace);
                    //                        System.Console.WriteLine("------------------------------------------");
                    //                        System.Console.WriteLine("Locker's call stack:\n" + WriterStack);
                    //                        System.Console.WriteLine("------------------------------------------");
                    //                    }
                    //                    catch
                    //                    {}
                    m_itemLock.ExitWriteLock();
                }

                while (!m_itemLock.TryEnterReadLock(60000))
                {
                    m_log.Error("Thread lock detected while trying to aquire READ lock in TaskInventoryDictionary. Locked by thread " + LockedByThread.Name + ". I'm going to try to solve the thread lock automatically to preserve region stability, but this needs to be fixed.");
                    //if (m_itemLock.IsWriteLockHeld)
                    //{
                    m_itemLock = new System.Threading.ReaderWriterLockSlim();
                    //                        System.Console.WriteLine("------------------------------------------");
                    //                        System.Console.WriteLine("My call stack:\n" + Environment.StackTrace);
                    //                        System.Console.WriteLine("------------------------------------------");
                    //                        System.Console.WriteLine("Locker's call stack:\n" + WriterStack);
                    //                        System.Console.WriteLine("------------------------------------------");
                    //                        LockedByThread = null;
                    //                        ReadLockers.Clear();
                    //}
                }
                //                ReadLockers[Thread.CurrentThread] = Environment.StackTrace;
            }
            else
            {
                if (m_itemLock.RecursiveReadCount > 0)
                {
                    m_itemLock.ExitReadLock();
                }
                //                if (m_itemLock.RecursiveReadCount == 0)
                //                    ReadLockers.Remove(Thread.CurrentThread);
            }
        }

        /// <summary>
        /// Lock our inventory list for writing (many can read, one can write)
        /// </summary>
        public void LockItemsForWrite(bool locked)
        {
            if (locked)
            {
                //Enter a write lock, wait indefinately for one to open.
                if (m_itemLock.RecursiveReadCount > 0)
                {
                    m_log.Error("[TaskInventoryDictionary] Recursive read lock requested. This should not happen and means something needs to be fixed. For now though, it's safe to continue.");
                    m_itemLock.ExitReadLock();
                }
                if (m_itemLock.RecursiveWriteCount > 0)
                {
                    m_log.Error("[TaskInventoryDictionary] Recursive write lock requested. This should not happen and means something needs to be fixed.");

                    m_itemLock.ExitWriteLock();
                }
                while (!m_itemLock.TryEnterWriteLock(60000))
                {
                    if (m_itemLock.IsWriteLockHeld)
                    {
                        m_log.Error("Thread lock detected while trying to aquire WRITE lock in TaskInventoryDictionary. Locked by thread " + LockedByThread.Name + ". I'm going to try to solve the thread lock automatically to preserve region stability, but this needs to be fixed.");
                        //                        System.Console.WriteLine("------------------------------------------");
                        //                        System.Console.WriteLine("My call stack:\n" + Environment.StackTrace);
                        //                        System.Console.WriteLine("------------------------------------------");
                        //                        System.Console.WriteLine("Locker's call stack:\n" + WriterStack);
                        //                        System.Console.WriteLine("------------------------------------------");
                    }
                    else
                    {
                        m_log.Error("Thread lock detected while trying to aquire WRITE lock in TaskInventoryDictionary. Locked by a reader. I'm going to try to solve the thread lock automatically to preserve region stability, but this needs to be fixed.");
                        //                        System.Console.WriteLine("------------------------------------------");
                        //                        System.Console.WriteLine("My call stack:\n" + Environment.StackTrace);
                        //                        System.Console.WriteLine("------------------------------------------");
                        //                        foreach (KeyValuePair<Thread, string> kvp in ReadLockers)
                        //                        {
                        //                            System.Console.WriteLine("Locker name {0} call stack:\n" + kvp.Value, kvp.Key.Name);
                        //                            System.Console.WriteLine("------------------------------------------");
                        //                        }
                    }
                    m_itemLock = new System.Threading.ReaderWriterLockSlim();
                    //                    ReadLockers.Clear();
                }

                LockedByThread = Thread.CurrentThread;
                //                WriterStack = Environment.StackTrace;
            }
            else
            {
                if (m_itemLock.RecursiveWriteCount > 0)
                {
                    m_itemLock.ExitWriteLock();
                }
            }
        }

        #region ICloneable Members

        public Object Clone()
        {
            TaskInventoryDictionary clone = new TaskInventoryDictionary();

            m_itemLock.EnterReadLock();
            foreach (UUID uuid in Keys)
            {
                clone.Add(uuid, (TaskInventoryItem)this[uuid].Clone());
            }
            m_itemLock.ExitReadLock();

            return clone;
        }
        #endregion

        public List<TaskInventoryItem> GetItems()
        {
            m_itemLock.EnterReadLock();
            var ret = new List<TaskInventoryItem>(Values);
            m_itemLock.ExitReadLock();
            return ret;
        }

        #region IXmlSerializable Members

        public XmlSchema GetSchema()
        {
            return null;
        }

        // see IXmlSerializable
        public void ReadXml(XmlReader reader)
        {
            // m_log.DebugFormat("[TASK INVENTORY]: ReadXml current node before actions, {0}", reader.Name);

            if (!reader.IsEmptyElement)
            {
                reader.Read();
                while (tiiSerializer.CanDeserialize(reader))
                {
                    TaskInventoryItem item = (TaskInventoryItem)tiiSerializer.Deserialize(reader);
                    Add(item.ItemID, item);

                    //m_log.DebugFormat("[TASK INVENTORY]: Instanted prim item {0}, {1} from xml", item.Name, item.ItemID);
                }

                // m_log.DebugFormat("[TASK INVENTORY]: Instantiated {0} prim items in total from xml", Count);
            }
            // else
            // {
            //     m_log.DebugFormat("[TASK INVENTORY]: Skipping empty element {0}", reader.Name);
            // }

            // For some .net implementations, this last read is necessary so that we advance beyond the end tag
            // of the element wrapping this object so that the rest of the serialization can complete normally.
            reader.Read();

            // m_log.DebugFormat("[TASK INVENTORY]: ReadXml current node after actions, {0}", reader.Name);
        }

        // see IXmlSerializable
        public void WriteXml(XmlWriter writer)
        {
            lock (this)
            {
                foreach (TaskInventoryItem item in Values)
                {
                    tiiSerializer.Serialize(writer, item);
                }
            }

            //tiiSerializer.Serialize(writer, Values);
        }

        #endregion

        // see ICloneable
    }
}
