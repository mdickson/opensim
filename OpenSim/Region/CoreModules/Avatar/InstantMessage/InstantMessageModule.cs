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
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Client;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace OpenSim.Region.CoreModules.Avatar.InstantMessage
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "InstantMessageModule")]
    public class InstantMessageModule : ISharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        /// <value>
        /// Is this module enabled?
        /// </value>
        protected bool m_enabled = false;

        protected readonly List<Scene> m_scenes = new List<Scene>();

        #region Region Module interface

        protected IMessageTransferModule m_TransferModule = null;

        public virtual void Initialise(IConfigSource config)
        {
            if (config.Configs["Messaging"] != null)
            {
                if (config.Configs["Messaging"].GetString(
                        "InstantMessageModule", "InstantMessageModule") !=
                        "InstantMessageModule")
                    return;
            }

            m_enabled = true;
        }

        public virtual void AddRegion(Scene scene)
        {
            if (!m_enabled)
                return;

            lock (m_scenes)
            {
                if (!m_scenes.Contains(scene))
                {
                    m_scenes.Add(scene);
                    scene.EventManager.OnClientConnect += OnClientConnect;
                    scene.EventManager.OnIncomingInstantMessage += OnGridInstantMessage;
                }
            }
        }

        public virtual void RegionLoaded(Scene scene)
        {
            if (!m_enabled)
                return;

            if (m_TransferModule == null)
            {
                m_TransferModule =
                    scene.RequestModuleInterface<IMessageTransferModule>();

                if (m_TransferModule == null)
                {
                    m_log.Error("[INSTANT MESSAGE]: No message transfer module, IM will not work!");
                    scene.EventManager.OnClientConnect -= OnClientConnect;
                    scene.EventManager.OnIncomingInstantMessage -= OnGridInstantMessage;

                    m_scenes.Clear();
                    m_enabled = false;
                }
            }
        }

        public virtual void RemoveRegion(Scene scene)
        {
            if (!m_enabled)
                return;

            lock (m_scenes)
            {
                m_scenes.Remove(scene);
            }
        }

        protected virtual void OnClientConnect(IClientCore client)
        {
            IClientIM clientIM;
            if (client.TryGet(out clientIM))
            {
                clientIM.OnInstantMessage += OnInstantMessage;
            }
        }

        public virtual void PostInitialise()
        {
        }

        public virtual void Close()
        {
        }

        public virtual string Name
        {
            get { return "InstantMessageModule"; }
        }

        public virtual Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion
        /*
                public virtual void OnViewerInstantMessage(IClientAPI client, GridInstantMessage im)
                {
                    im.fromAgentName = client.FirstName + " " + client.LastName;
                    OnInstantMessage(client, im);
                }
        */
        public virtual void OnInstantMessage(IClientAPI client, GridInstantMessage im)
        {
            byte dialog = im.dialog;

            if (dialog != (byte)InstantMessageDialog.MessageFromAgent
                && dialog != (byte)InstantMessageDialog.StartTyping
                && dialog != (byte)InstantMessageDialog.StopTyping
                && dialog != (byte)InstantMessageDialog.BusyAutoResponse
                && dialog != (byte)InstantMessageDialog.MessageFromObject)
            {
                return;
            }

            //DateTime dt = DateTime.UtcNow;

            // Ticks from UtcNow, but make it look like local. Evil, huh?
            //dt = DateTime.SpecifyKind(dt, DateTimeKind.Local);

            //try
            //{
            //    // Convert that to the PST timezone
            //    TimeZoneInfo timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
            //    dt = TimeZoneInfo.ConvertTime(dt, timeZoneInfo);
            //}
            //catch
            //{
            //    //m_log.Info("[OFFLINE MESSAGING]: No PST timezone found on this machine. Saving with local timestamp.");
            //}

            //// And make it look local again to fool the unix time util
            //dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

            // If client is null, this message comes from storage and IS offline
            if (client != null)
                im.offline = 0;

            if (im.offline == 0)
                im.timestamp = (uint)Util.UnixTimeSinceEpoch();

            if (m_TransferModule != null)
            {
                m_TransferModule.SendInstantMessage(im,
                    delegate (bool success)
                    {
                        if (dialog == (uint)InstantMessageDialog.StartTyping ||
                            dialog == (uint)InstantMessageDialog.StopTyping ||
                            dialog == (uint)InstantMessageDialog.MessageFromObject)
                        {
                            return;
                        }

                        if ((client != null) && !success)
                        {
                            client.SendInstantMessage(
                                    new GridInstantMessage(
                                    null, new UUID(im.fromAgentID), "System",
                                    new UUID(im.toAgentID),
                                    (byte)InstantMessageDialog.BusyAutoResponse,
                                    "Unable to send instant message. " +
                                    "User is not logged in.", false,
                                    new Vector3()));
                        }
                    }
                );
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="msg"></param>
        protected virtual void OnGridInstantMessage(GridInstantMessage msg)
        {
            // Just call the Text IM handler above
            // This event won't be raised unless we have that agent,
            // so we can depend on the above not trying to send
            // via grid again
            //
            OnInstantMessage(null, msg);
        }
    }
}
