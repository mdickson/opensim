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
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using System.Web;
using OSDArray = OpenMetaverse.StructuredData.OSDArray;
using OSDMap = OpenMetaverse.StructuredData.OSDMap;

namespace OpenSim.Capabilities.Handlers
{
    public class GetDisplayNamesHandler : BaseStreamHandler
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        protected IUserManagement m_UserManagement;

        public GetDisplayNamesHandler(string path, IUserManagement umService, string name, string description)
            : base("GET", path, name, description)
        {
            m_UserManagement = umService;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            //            m_log.DebugFormat("[GET_DISPLAY_NAMES]: called {0}", httpRequest.Url.Query);

            NameValueCollection query = HttpUtility.ParseQueryString(httpRequest.Url.Query);
            string[] ids = query.GetValues("ids");

            if (m_UserManagement == null)
            {
                m_log.Error("[GET_DISPLAY_NAMES]: Cannot fetch display names without a user management component");
                httpResponse.StatusCode = (int)System.Net.HttpStatusCode.InternalServerError;
                return new byte[0];
            }

            Dictionary<UUID, string> names = m_UserManagement.GetUsersNames(ids, UUID.Zero);

            OSDMap osdReply = new OSDMap();
            OSDArray agents = new OSDArray();

            osdReply["agents"] = agents;
            foreach (KeyValuePair<UUID, string> kvp in names)
            {
                if (string.IsNullOrEmpty(kvp.Value))
                    continue;
                if (kvp.Key == UUID.Zero)
                    continue;

                string[] parts = kvp.Value.Split(new char[] { ' ' });
                OSDMap osdname = new OSDMap();
                if (parts[0] == "Unknown")
                {
                    osdname["display_name_next_update"] = OSD.FromDate(DateTime.UtcNow.AddHours(1));
                    osdname["display_name_expires"] = OSD.FromDate(DateTime.UtcNow.AddHours(2));
                }
                else
                {
                    osdname["display_name_next_update"] = OSD.FromDate(DateTime.UtcNow.AddDays(8));
                    osdname["display_name_expires"] = OSD.FromDate(DateTime.UtcNow.AddMonths(1));
                }
                osdname["display_name"] = OSD.FromString(kvp.Value);
                osdname["legacy_first_name"] = parts[0];
                osdname["legacy_last_name"] = parts[1];
                osdname["username"] = OSD.FromString(kvp.Value);
                osdname["id"] = OSD.FromUUID(kvp.Key);
                osdname["is_display_name_default"] = OSD.FromBoolean(true);

                agents.Add(osdname);
            }

            // Full content request
            httpResponse.StatusCode = (int)System.Net.HttpStatusCode.OK;
            //httpResponse.ContentLength = ??;
            httpResponse.ContentType = "application/llsd+xml";

            string reply = OSDParser.SerializeLLSDXmlString(osdReply);
            return System.Text.Encoding.UTF8.GetBytes(reply);
        }
    }
}
