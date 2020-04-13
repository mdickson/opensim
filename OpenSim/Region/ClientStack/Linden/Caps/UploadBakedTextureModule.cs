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
using System.Reflection;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Capabilities.Handlers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

using Caps = OpenSim.Framework.Capabilities.Caps;

namespace OpenSim.Region.ClientStack.Linden
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "UploadBakedTextureModule")]
    public class UploadBakedTextureModule : INonSharedRegionModule
    {
        private static readonly ILog m_log =
             LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// For historical reasons this is fixed, but there
        /// </summary>
        private static readonly string m_uploadBakedTexturePath = "0010/";// This is in the LandManagementModule.

        private Scene m_scene;

        private string m_URL;

        public void Initialise(IConfigSource source)
        {
            IConfig config = source.Configs["ClientStack.LindenCaps"];
            if (config == null)
                return;

            m_URL = config.GetString("Cap_UploadBakedTexture", string.Empty);

            //            IConfig appearanceConfig = source.Configs["Appearance"];
        }

        public void AddRegion(Scene s)
        {
            m_scene = s;
        }

        public void RemoveRegion(Scene s)
        {
            s.EventManager.OnRegisterCaps -= RegisterCaps;
            s.EventManager.OnNewPresence -= RegisterNewPresence;
            s.EventManager.OnRemovePresence -= DeRegisterPresence;
            m_scene = null;
        }

        public void RegionLoaded(Scene s)
        {
            m_scene.EventManager.OnRegisterCaps += RegisterCaps;
            m_scene.EventManager.OnNewPresence += RegisterNewPresence;
            m_scene.EventManager.OnRemovePresence += DeRegisterPresence;
        }

        private void DeRegisterPresence(UUID agentId)
        {
        }

        private void RegisterNewPresence(ScenePresence presence)
        {
        }

        public void PostInitialise()
        {
        }

        public void Close() { }

        public string Name { get { return "UploadBakedTextureModule"; } }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void RegisterCaps(UUID agentID, Caps caps)
        {
            //caps.RegisterHandler("GetTexture", new StreamHandler("GET", "/CAPS/" + capID, ProcessGetTexture));
            if (m_URL == "localhost")
            {
                UploadBakedTextureHandler avatarhandler = new UploadBakedTextureHandler(
                    caps, m_scene.AssetService);

                caps.RegisterHandler(
                    "UploadBakedTexture",
                    new RestStreamHandler(
                        "POST",
                        "/CAPS/" + caps.CapsObjectPath + m_uploadBakedTexturePath,
                        avatarhandler.UploadBakedTexture,
                        "UploadBakedTexture",
                        agentID.ToString()));

            }
            else
            {
                caps.RegisterHandler("UploadBakedTexture", m_URL);
            }
        }
    }
}
