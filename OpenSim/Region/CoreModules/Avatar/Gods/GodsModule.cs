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

using System;
using System.Net;
using System.Reflection;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

using Caps = OpenSim.Framework.Capabilities.Caps;
using OSDArray = OpenMetaverse.StructuredData.OSDArray;
using OSDMap = OpenMetaverse.StructuredData.OSDMap;

namespace OpenSim.Region.CoreModules.Avatar.Gods
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "GodsModule")]
    public class GodsModule : INonSharedRegionModule, IGodsModule
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Special UUID for actions that apply to all agents</summary>
        private static readonly UUID ALL_AGENTS = new UUID("44e87126-e794-4ded-05b3-7c42da3d5cdb");
        private static readonly UUID UUID_GRID_GOD = new UUID("6571e388-6218-4574-87db-f9379718315e");

        protected Scene m_scene;
        protected IDialogModule m_dialogModule;

        public void Initialise(IConfigSource source)
        {
        }

        public void AddRegion(Scene scene)
        {
            m_scene = scene;
            m_scene.RegisterModuleInterface<IGodsModule>(this);
            m_scene.EventManager.OnNewClient += SubscribeToClientEvents;
            m_scene.EventManager.OnRegisterCaps += OnRegisterCaps;
            scene.EventManager.OnIncomingInstantMessage +=
                    OnIncomingInstantMessage;
        }

        public void RemoveRegion(Scene scene)
        {
            m_scene.UnregisterModuleInterface<IGodsModule>(this);
            m_scene.EventManager.OnNewClient -= SubscribeToClientEvents;
            m_scene = null;
        }

        public void RegionLoaded(Scene scene)
        {
            m_dialogModule = m_scene.RequestModuleInterface<IDialogModule>();
        }

        public void Close() { }
        public string Name { get { return "Gods Module"; } }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void SubscribeToClientEvents(IClientAPI client)
        {
            client.OnGodKickUser += KickUser;
            client.OnRequestGodlikePowers += RequestGodlikePowers;
        }

        public void UnsubscribeFromClientEvents(IClientAPI client)
        {
            client.OnGodKickUser -= KickUser;
            client.OnRequestGodlikePowers -= RequestGodlikePowers;
        }

        private void OnRegisterCaps(UUID agentID, Caps caps)
        {
            caps.RegisterSimpleHandler("UntrustedSimulatorMessage", new SimpleStreamHandler("/C" + UUID.Random(), HandleUntrustedSimulatorMessage));
        }

        private void HandleUntrustedSimulatorMessage(IOSHttpRequest request, IOSHttpResponse response)
        {
            if (request.HttpMethod != "POST")
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            OSDMap osd;
            try
            {
                osd = (OSDMap)OSDParser.DeserializeLLSDXml(request.InputStream);

                string message = osd["message"].AsString();
                if (message == "GodKickUser")
                {
                    OSDMap body = (OSDMap)osd["body"];
                    OSDArray userInfo = (OSDArray)body["UserInfo"];
                    OSDMap userData = (OSDMap)userInfo[0];

                    UUID agentID = userData["AgentID"].AsUUID();
                    UUID godID = userData["GodID"].AsUUID();
                    UUID godSessionID = userData["GodSessionID"].AsUUID();
                    uint kickFlags = userData["KickFlags"].AsUInteger();
                    string reason = userData["Reason"].AsString();

                    ScenePresence god = m_scene.GetScenePresence(godID);
                    if (god == null || god.ControllingClient.SessionId != godSessionID)
                    {
                        response.StatusCode = (int)HttpStatusCode.Unauthorized;
                        return;
                    }

                    KickUser(godID, agentID, kickFlags, reason);
                }
                else
                {
                    m_log.ErrorFormat("[GOD]: Unhandled UntrustedSimulatorMessage: {0}", message);
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }
            }
            catch
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            response.StatusCode = (int)HttpStatusCode.OK;
        }

        public void RequestGodlikePowers(
            UUID agentID, UUID sessionID, UUID token, bool godLike)
        {
            ScenePresence sp = m_scene.GetScenePresence(agentID);
            if (sp == null || sp.IsDeleted || sp.IsNPC)
                return;

            if (sessionID != sp.ControllingClient.SessionId)
                return;

            sp.GrantGodlikePowers(token, godLike);

            if (godLike && !sp.IsViewerUIGod && m_dialogModule != null)
                m_dialogModule.SendAlertToUser(agentID, "Request for god powers denied");
        }

        public void KickUser(UUID godID, UUID agentID, uint kickflags, byte[] reason)
        {
            KickUser(godID, agentID, kickflags, Utils.BytesToString(reason));
        }

        /// <summary>
        /// Kicks or freezes User specified from the simulator. This logs them off of the grid
        /// </summary>
        /// <param name="godID">The person doing the kicking</param>
        /// <param name="agentID">the person that is being kicked</param>
        /// <param name="kickflags">Tells what to do to the user</param>
        /// <param name="reason">The message to send to the user after it's been turned into a field</param>
        public void KickUser(UUID godID, UUID agentID, uint kickflags, string reason)
        {
            // assuming automatic god rights on this for fast griefing reaction
            // this is also needed for kick via message
            if (!m_scene.Permissions.IsGod(godID))
                return;

            int godlevel = 200;
            // update level so higher gods can kick lower ones
            ScenePresence god = m_scene.GetScenePresence(godID);
            if (god != null && god.GodController.GodLevel > godlevel)
                godlevel = god.GodController.GodLevel;

            if (agentID == ALL_AGENTS)
            {
                m_scene.ForEachRootScenePresence(delegate (ScenePresence p)
                {
                    if (p.UUID != godID)
                    {
                        if (godlevel > p.GodController.GodLevel)
                            doKickmodes(godID, p, kickflags, reason);
                        else if (m_dialogModule != null)
                            m_dialogModule.SendAlertToUser(p.UUID, "Kick from " + godID.ToString() + " ignored, kick reason: " + reason);
                    }
                });
                return;
            }

            ScenePresence sp = m_scene.GetScenePresence(agentID);
            if (sp == null || sp.IsChildAgent)
            {
                IMessageTransferModule transferModule =
                        m_scene.RequestModuleInterface<IMessageTransferModule>();
                if (transferModule != null)
                {
                    m_log.DebugFormat("[GODS]: Sending nonlocal kill for agent {0}", agentID);
                    transferModule.SendInstantMessage(new GridInstantMessage(
                            m_scene, godID, "God", agentID, (byte)250, false,
                            reason, UUID.Zero, true,
                            new Vector3(), new byte[] { (byte)kickflags }, true),
                            delegate (bool success) { });
                }
                return;
            }

            if (godlevel <= sp.GodController.GodLevel) // no god wars
            {
                if (m_dialogModule != null)
                    m_dialogModule.SendAlertToUser(sp.UUID, "Kick from " + godID.ToString() + " ignored, kick reason: " + reason);
                return;
            }

            if (sp.UUID == godID)
                return;

            doKickmodes(godID, sp, kickflags, reason);
        }

        private void doKickmodes(UUID godID, ScenePresence sp, uint kickflags, string reason)
        {
            switch (kickflags)
            {
                case 0:
                    KickPresence(sp, reason);
                    break;
                case 1:
                    sp.AllowMovement = false;
                    if (m_dialogModule != null)
                    {
                        m_dialogModule.SendAlertToUser(sp.UUID, reason);
                        m_dialogModule.SendAlertToUser(godID, "User Frozen");
                    }
                    break;
                case 2:
                    sp.AllowMovement = true;
                    if (m_dialogModule != null)
                    {
                        m_dialogModule.SendAlertToUser(sp.UUID, reason);
                        m_dialogModule.SendAlertToUser(godID, "User Unfrozen");
                    }
                    break;
                default:
                    break;
            }
        }

        private void KickPresence(ScenePresence sp, string reason)
        {
            if (sp.IsDeleted || sp.IsChildAgent)
                return;
            sp.ControllingClient.Kick(reason);
            sp.Scene.CloseAgent(sp.UUID, true);
        }

        public void GridKickUser(UUID agentID, string reason)
        {
            int godlevel = 240; // grid god default

            ScenePresence sp = m_scene.GetScenePresence(agentID);
            if (sp == null || sp.IsChildAgent)
            {
                IMessageTransferModule transferModule =
                        m_scene.RequestModuleInterface<IMessageTransferModule>();
                if (transferModule != null)
                {
                    m_log.DebugFormat("[GODS]: Sending nonlocal kill for agent {0}", agentID);
                    transferModule.SendInstantMessage(new GridInstantMessage(
                            m_scene, UUID_GRID_GOD, "GRID", agentID, (byte)250, false,
                            reason, UUID.Zero, true,
                            new Vector3(), new byte[] { 0 }, true),
                            delegate (bool success) { });
                }
                return;
            }

            if (sp.IsDeleted)
                return;

            if (godlevel <= sp.GodController.GodLevel) // no god wars
            {
                if (m_dialogModule != null)
                    m_dialogModule.SendAlertToUser(sp.UUID, "GRID kick detected and ignored, kick reason: " + reason);
                return;
            }

            sp.ControllingClient.Kick(reason);
            sp.Scene.CloseAgent(sp.UUID, true);
        }

        private void OnIncomingInstantMessage(GridInstantMessage msg)
        {
            if (msg.dialog == (uint)250) // Nonlocal kick
            {
                UUID agentID = new UUID(msg.toAgentID);
                string reason = msg.message;
                UUID godID = new UUID(msg.fromAgentID);
                uint kickMode = (uint)msg.binaryBucket[0];

                if (godID == UUID_GRID_GOD)
                    GridKickUser(agentID, reason);
                else
                    KickUser(godID, agentID, kickMode, reason);
            }
        }
    }
}
