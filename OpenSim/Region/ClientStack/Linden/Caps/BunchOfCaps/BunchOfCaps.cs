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
using System.Timers;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Web;

using log4net;
using Nini.Config;

using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Capabilities;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Scenes.Serialization;
using OpenSim.Services.Interfaces;

using Caps = OpenSim.Framework.Capabilities.Caps;
using OSDArray = OpenMetaverse.StructuredData.OSDArray;
using OSDMap = OpenMetaverse.StructuredData.OSDMap;
using PermissionMask = OpenSim.Framework.PermissionMask;

namespace OpenSim.Region.ClientStack.Linden
{
    public delegate void UpLoadedAsset(
        string assetName, string description, UUID assetID, UUID inventoryItem, UUID parentFolder,
        byte[] data, string inventoryType, string assetType,
        int cost, UUID texturesFolder, int nreqtextures, int nreqmeshs, int nreqinstances,
        bool IsAtestUpload, ref string error, ref int nextOwnerMask, ref int groupMask, ref int everyoneMask, int[] meshesSides);

    public delegate void UpdateTaskScript(UUID itemID, UUID primID, bool isScriptRunning, byte[] data, ref ArrayList errors);

    public delegate void NewInventoryItem(UUID userID, InventoryItemBase item, uint cost);

    public delegate void NewAsset(AssetBase asset);

    public delegate ArrayList TaskScriptUpdatedCallback(UUID userID, UUID itemID, UUID primID,
                                                   bool isScriptRunning, byte[] data);

    /// <summary>
    /// XXX Probably not a particularly nice way of allow us to get the scene presence from the scene (chiefly so that
    /// we can popup a message on the user's client if the inventory service has permanently failed).  But I didn't want
    /// to just pass the whole Scene into CAPS.
    /// </summary>
    public delegate IClientAPI GetClientDelegate(UUID agentID);

    public partial class BunchOfCaps
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Scene m_Scene;
        private UUID m_AgentID;
        private UUID m_scopeID;
        private Caps m_HostCapsObj;
        private ModelCost m_ModelCost;

        // private static readonly string m_remoteParcelRequestPath = "0009/";// This is in the LandManagementModule.

        // These are callbacks which will be setup by the scene so that we can update scene data when we
        // receive capability calls
        public NewInventoryItem AddNewInventoryItem = null;
        public NewAsset AddNewAsset = null;
        public ItemUpdatedCallback ItemUpdatedCall = null;
        public TaskScriptUpdatedCallback TaskScriptUpdatedCall = null;
        public GetClientDelegate GetClient = null;

        private bool m_persistBakedTextures = false;
        private IAssetService m_assetService;
        private bool m_dumpAssetsToFile = false;
        private string m_regionName;

        private int m_levelUpload = 0;

        private bool m_enableFreeTestUpload = false; // allows "TEST-" prefix hack
        private bool m_ForceFreeTestUpload = false; // forces all uploads to be test

        private bool m_enableModelUploadTextureToInventory = false; // place uploaded textures also in inventory
                                                                    // may not be visible till relog

        private bool m_RestrictFreeTestUploadPerms = false; // reduces also the permitions. Needs a creator defined!!
        private UUID m_testAssetsCreatorID = UUID.Zero;

        private float m_PrimScaleMin = 0.001f;

        private bool m_AllowCapHomeLocation = true;
        private bool m_AllowCapGroupMemberData = true;
        private IUserManagement m_UserManager;
        private IUserAccountService m_userAccountService;
 
        private  IDisplayNamesModule m_DisplayNames;
        private bool m_AllowSetDisplayName = true;
        private string m_HomeURL = string.Empty;
        private bool m_TrimResident = true;

        IEventQueue m_EventQueue;

        private IMoneyModule m_moneyModule;

        private enum FileAgentInventoryState : int
        {
            idle = 0,
            processRequest = 1,
            waitUpload = 2,
            processUpload = 3
        }
        private FileAgentInventoryState m_FileAgentInventoryState = FileAgentInventoryState.idle;

        public BunchOfCaps(Scene scene, UUID agentID, Caps caps)
        {
            m_Scene = scene;
            m_AgentID = agentID;
            m_HostCapsObj = caps;

            // create a model upload cost provider
            m_ModelCost = new ModelCost(scene);

            m_PrimScaleMin = m_ModelCost.PrimScaleMin;

            IConfigSource config = m_Scene.Config;
            if (config != null)
            {
                IConfig sconfig = config.Configs["Startup"];
                if (sconfig != null)
                {
                    m_levelUpload = sconfig.GetInt("LevelUpload", 0);
                }
                if (m_levelUpload == 0)
                {
                    IConfig pconfig = config.Configs["Permissions"];
                    if (pconfig != null)
                    {
                        m_levelUpload = pconfig.GetInt("LevelUpload", 0);
                    }
                }

                IConfig appearanceConfig = config.Configs["Appearance"];
                if (appearanceConfig != null)
                {
                    m_persistBakedTextures = appearanceConfig.GetBoolean("PersistBakedTextures", m_persistBakedTextures);
                }

                // economy for model upload
                IConfig EconomyConfig = config.Configs["Economy"];
                if (EconomyConfig != null)
                {
                    m_ModelCost.Econfig(EconomyConfig);

                    m_enableModelUploadTextureToInventory = EconomyConfig.GetBoolean("MeshModelAllowTextureToInventory", m_enableModelUploadTextureToInventory);

                    m_RestrictFreeTestUploadPerms = EconomyConfig.GetBoolean("m_RestrictFreeTestUploadPerms", m_RestrictFreeTestUploadPerms);
                    m_enableFreeTestUpload = EconomyConfig.GetBoolean("AllowFreeTestUpload", m_enableFreeTestUpload);
                    m_ForceFreeTestUpload = EconomyConfig.GetBoolean("ForceFreeTestUpload", m_ForceFreeTestUpload);
                    string testcreator = EconomyConfig.GetString("TestAssetsCreatorID", "");
                    
                    if (testcreator != "")
                    {
                        UUID id;
                        UUID.TryParse(testcreator, out id);
                        if (id != null)
                            m_testAssetsCreatorID = id;
                    }
                }

                IConfig CapsConfig = config.Configs["ClientStack.LindenCaps"];
                if (CapsConfig != null)
                {
                    string homeLocationUrl = CapsConfig.GetString("Cap_HomeLocation", "localhost");
                    if (homeLocationUrl == String.Empty)
                        m_AllowCapHomeLocation = false;

                    string GroupMemberDataUrl = CapsConfig.GetString("Cap_GroupMemberData", "localhost");
                    if (GroupMemberDataUrl == String.Empty)
                        m_AllowCapGroupMemberData = false;

                    string SetDisplayNameURL = CapsConfig.GetString("Cap_SetDisplayName", "localhost");
                    if (SetDisplayNameURL == String.Empty)
                        m_AllowSetDisplayName = false;
                }

				IConfig hypergridConfig = config.Configs["Hypergrid"];
                if (hypergridConfig != null)
                {
                    m_HomeURL = hypergridConfig.GetString("HomeURI", string.Empty);
                }
				
                IConfig namesConfig = config.Configs["DisplayNames"];
                if (namesConfig != null)
                {
                    m_TrimResident = namesConfig.GetBoolean("TrimResident", true);
                }
            }

            m_assetService = m_Scene.AssetService;
            m_regionName = m_Scene.RegionInfo.RegionName;
            
            m_UserManager = m_Scene.RequestModuleInterface<IUserManagement>();
            m_userAccountService = m_Scene.RequestModuleInterface<IUserAccountService>();
            m_EventQueue = m_Scene.RequestModuleInterface<IEventQueue>();
            m_DisplayNames = m_Scene.RequestModuleInterface<IDisplayNamesModule>();
            m_moneyModule = m_Scene.RequestModuleInterface<IMoneyModule>();

            if (m_DisplayNames == null)
            {
                m_log.Error("[CAPS]: GetDisplayNames disabled because display names component not found");
            }

			if (m_UserManager == null)
            {
                m_log.Error("[CAPS]: SetDisplayName disabled because user management component not found");
                m_AllowSetDisplayName = false;
            }

            if (m_UserManager == null)
            {
                m_log.Error("[CAPS]: GetDisplayNames disabled because user management component not found");
            }

            UserAccount account = m_userAccountService?.GetUserAccount(m_Scene.RegionInfo.ScopeID, m_AgentID);
            if (account == null) // Hypergrid?
            {
                m_scopeID = m_Scene.RegionInfo.ScopeID;
            }
            else
            {
                m_scopeID = account.ScopeID;
            }

            AddNewInventoryItem = m_Scene.AddUploadedInventoryItem;
            ItemUpdatedCall = m_Scene.CapsUpdateItemAsset;
            TaskScriptUpdatedCall = m_Scene.CapsUpdateTaskInventoryScriptAsset;
            GetClient = m_Scene.SceneGraph.GetControllingClient;

            RegisterHandlers();

            m_FileAgentInventoryState = FileAgentInventoryState.idle;
        }

        public string GetNewCapPath()
        {
            return  "/" + UUID.Random();
        }

        /// <summary>
        /// Register a bunch of CAPS http service handlers
        /// </summary>
        public void RegisterHandlers()
        {
            // this path is also defined elsewhere so keeping it
            string seedcapsBase = "/CAPS/" + m_HostCapsObj.CapsObjectPath + "0000";

            m_HostCapsObj.RegisterSimpleHandler("SEED", new SimpleStreamHandler(seedcapsBase, SeedCapRequest));
            // m_log.DebugFormat(
            //     "[CAPS]: Registered seed capability {0} for {1}", seedcapsBase, m_HostCapsObj.AgentID);

            RegisterRegionServiceHandlers();
            RegisterInventoryServiceHandlers();
            RegisterOtherHandlers();
        }

        public void RegisterRegionServiceHandlers()
        {
            try
            {
                m_HostCapsObj.RegisterSimpleHandler("GetObjectPhysicsData",
                    new SimpleOSDMapHandler("POST", GetNewCapPath(), GetObjectPhysicsData));

                m_HostCapsObj.RegisterSimpleHandler("GetObjectCost",
                    new SimpleOSDMapHandler("POST", GetNewCapPath(), GetObjectCost));

                m_HostCapsObj.RegisterSimpleHandler("ResourceCostSelected",
                    new SimpleOSDMapHandler("POST", GetNewCapPath(), ResourceCostSelected));
 
                if(m_AllowCapHomeLocation)
                {
                    m_HostCapsObj.RegisterSimpleHandler("HomeLocation",
                        new SimpleStreamHandler(GetNewCapPath(), HomeLocation));
                }

                if (m_AllowCapGroupMemberData)
                {
                    m_HostCapsObj.RegisterSimpleHandler("GroupMemberData",
                        new SimpleStreamHandler(GetNewCapPath(), GroupMemberData));
                }
            }
            catch (Exception e)
            {
                m_log.Error("[CAPS]: " + e.ToString());
            }
        }

        public void RegisterInventoryServiceHandlers()
        {
            try
            {
                m_HostCapsObj.RegisterHandler("NewFileAgentInventory",
                    new LLSDStreamhandler<LLSDAssetUploadRequest, LLSDAssetUploadResponse>(
                        "POST", GetNewCapPath(), NewAgentInventoryRequest, "NewFileAgentInventory", null));

                SimpleOSDMapHandler oreq;
                
                if (ItemUpdatedCall != null)
                {
                    // first sets the http handler, others only register the cap, using it
                    oreq = new SimpleOSDMapHandler("POST", GetNewCapPath(), UpdateNotecardItemAsset);
                    m_HostCapsObj.RegisterSimpleHandler("UpdateNotecardAgentInventory", oreq, true);
                    m_HostCapsObj.RegisterSimpleHandler("UpdateNotecardTaskInventory", oreq, false); // a object inv

                    oreq = new SimpleOSDMapHandler("POST", GetNewCapPath(), UpdateAnimSetItemAsset);
                    m_HostCapsObj.RegisterSimpleHandler("UpdateAnimSetAgentInventory", oreq, true);
                    //m_HostCapsObj.RegisterSimpleHandler("UpdateAnimSetTaskInventory", oreq, false);

                    oreq = new SimpleOSDMapHandler("POST", GetNewCapPath(), UpdateScriptItemAsset);
                    m_HostCapsObj.RegisterSimpleHandler("UpdateScriptAgent", oreq, true);
                    m_HostCapsObj.RegisterSimpleHandler("UpdateScriptAgentInventory", oreq, false); //legacy

                    oreq = new SimpleOSDMapHandler("POST", GetNewCapPath(), UpdateSettingsItemAsset);
                    m_HostCapsObj.RegisterSimpleHandler("UpdateSettingsAgentInventory", oreq, true);
                    m_HostCapsObj.RegisterSimpleHandler("UpdateSettingsTaskInventory", oreq, false); // a object inv

                    oreq = new SimpleOSDMapHandler("POST", GetNewCapPath(), UpdateGestureItemAsset);
                    m_HostCapsObj.RegisterSimpleHandler("UpdateGestureAgentInventory", oreq, true);
                    m_HostCapsObj.RegisterSimpleHandler("UpdateGestureTaskInventory", oreq, false);
                }

                if (TaskScriptUpdatedCall != null)
                {
                    oreq = new SimpleOSDMapHandler("POST", GetNewCapPath(), UpdateScriptTaskInventory);
                    m_HostCapsObj.RegisterSimpleHandler("UpdateScriptTask", oreq, true);
                    m_HostCapsObj.RegisterSimpleHandler("UpdateScriptTaskInventory", oreq, true); //legacy
                }

                m_HostCapsObj.RegisterSimpleHandler("UpdateAgentInformation",
                    new SimpleStreamHandler(GetNewCapPath(), UpdateAgentInformation));

                m_HostCapsObj.RegisterSimpleHandler("CopyInventoryFromNotecard",
                    new SimpleOSDMapHandler("POST", GetNewCapPath(), CopyInventoryFromNotecard));

                m_HostCapsObj.RegisterSimpleHandler("CreateInventoryCategory",
                    new SimpleStreamHandler(GetNewCapPath(), CreateInventoryCategory));
            }
            catch (Exception e)
            {
                m_log.Error("[CAPS]: " + e.ToString());
            }
        }

        public void RegisterOtherHandlers()
        {
            try
            {
                if (m_DisplayNames != null)
                {
                    m_HostCapsObj.RegisterSimpleHandler("GetDisplayNames",
                        new SimpleStreamHandler(GetNewCapPath(), GetDisplayNames));

                    if (m_AllowSetDisplayName)
                    {
                        // m_HostCapsObj.RegisterSimpleHandler("SetDisplayNames",
                        //     new SimpleStreamHandler(GetNewCapPath() +"/", SetDisplayNames));
                        IRequestHandler SetDisplayNameHandler = new RestStreamHandler(
                            "POST", GetNewCapPath(), SetDisplayNames, "SetDisplayName", null);
                        m_HostCapsObj.RegisterHandler("SetDisplayName", SetDisplayNameHandler);
                    }
                }
            }
            catch (Exception e)
            {
                m_log.Error("[CAPS]: " + e.ToString());
            }
        }

        /// <summary>
        /// Construct a client response detailing all the capabilities this server can provide.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="path"></param>
        /// <param name="param"></param>
        /// <param name="httpRequest">HTTP request header object</param>
        /// <param name="httpResponse">HTTP response header object</param>
        /// <returns></returns>
        public void SeedCapRequest(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            UUID agentID = m_HostCapsObj.AgentID;
            m_log.DebugFormat(
                "[CAPS]: Received SEED caps request in {0} for agent {1}", m_regionName, agentID);

            if(httpRequest.HttpMethod != "POST" || httpRequest.ContentType != "application/llsd+xml")
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            if (!m_HostCapsObj.WaitForActivation())
            {
                httpResponse.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                httpResponse.AddHeader("Retry-After", "30");
                return;
            }

            if (!m_Scene.CheckClient(agentID, httpRequest.RemoteIPEndPoint))
            {
                m_log.WarnFormat(
                    "[CAPS]: Unauthorized CAPS client {0} from {1}",
                    agentID, httpRequest.RemoteIPEndPoint);
                httpResponse.StatusCode = (int)HttpStatusCode.Forbidden;
                return;
            }

            OSDArray capsRequested;
            try
            {
                capsRequested = (OSDArray)OSDParser.DeserializeLLSDXml(httpRequest.InputStream);
            }
            catch
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            List<string> validCaps = new List<string>();

            foreach (OSD c in capsRequested)
            {
                string cstr = c.AsString();
                if (cstr.Equals("ObjectAnimation"))
                    m_HostCapsObj.Flags |= Caps.CapsFlags.ObjectAnim;
                else if (cstr.Equals("ExtEnvironment"))
                    m_HostCapsObj.Flags |= Caps.CapsFlags.AdvEnv;
                validCaps.Add(cstr);
            }

            string result = LLSDHelpers.SerialiseLLSDReply(m_HostCapsObj.GetCapsDetails(true, validCaps));
            httpResponse.RawBuffer = Util.UTF8NBGetbytes(result);
            httpResponse.StatusCode = (int)HttpStatusCode.OK;
            //m_log.DebugFormat("[CAPS] CapsRequest {0}", result);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="llsdRequest"></param>
        /// <returns></returns>
        public LLSDAssetUploadResponse NewAgentInventoryRequest(LLSDAssetUploadRequest llsdRequest)
        {
            //m_log.Debug("[CAPS]: NewAgentInventoryRequest Request is: " + llsdRequest.ToString());
            //m_log.Debug("asset upload request via CAPS" + llsdRequest.inventory_type + " , " + llsdRequest.asset_type);

            // start by getting the client
            IClientAPI client = null;
            m_Scene.TryGetClient(m_HostCapsObj.AgentID, out client);

            // check current state so we only have one service at a time
            lock (m_ModelCost)
            {
                switch (m_FileAgentInventoryState)
                {
                    case FileAgentInventoryState.processRequest:
                    case FileAgentInventoryState.processUpload:
                        LLSDAssetUploadError resperror = new LLSDAssetUploadError();
                        resperror.message = "Uploader busy processing previous request";
                        resperror.identifier = UUID.Zero;

                        LLSDAssetUploadResponse errorResponse = new LLSDAssetUploadResponse();
                        errorResponse.uploader = "";
                        errorResponse.state = "error";
                        errorResponse.error = resperror;
                        return errorResponse;
                    case FileAgentInventoryState.waitUpload:
                        // todo stop current uploader server
                        break;
                    case FileAgentInventoryState.idle:
                    default:
                        break;
                }

                m_FileAgentInventoryState = FileAgentInventoryState.processRequest;
            }

            int cost = 0;
            int nreqtextures = 0;
            int nreqmeshs = 0;
            int nreqinstances = 0;
            bool IsAtestUpload = false;
            int[] meshesSides = null;

            string assetName = llsdRequest.name;

            LLSDAssetUploadResponseData meshcostdata = new LLSDAssetUploadResponseData();

            if (llsdRequest.asset_type == "texture" ||
                llsdRequest.asset_type == "animation" ||
                llsdRequest.asset_type == "animatn" ||    // this is the asset name actually used by viewers
                llsdRequest.asset_type == "mesh" ||
                llsdRequest.asset_type == "sound")
            {
                ScenePresence avatar = null;
                m_Scene.TryGetScenePresence(m_HostCapsObj.AgentID, out avatar);

                // check user level
                if (avatar != null)
                {
                    if (avatar.GodController.UserLevel < m_levelUpload)
                    {
                        LLSDAssetUploadError resperror = new LLSDAssetUploadError();
                        resperror.message = "Insufficient permissions to upload";
                        resperror.identifier = UUID.Zero;

                        LLSDAssetUploadResponse errorResponse = new LLSDAssetUploadResponse();
                        errorResponse.uploader = "";
                        errorResponse.state = "error";
                        errorResponse.error = resperror;
                        lock (m_ModelCost)
                            m_FileAgentInventoryState = FileAgentInventoryState.idle;
                        return errorResponse;
                    }
                }

                // check test upload and funds
                if (client != null)
                {
                    IMoneyModule mm = m_Scene.RequestModuleInterface<IMoneyModule>();

                    int baseCost = 0;
                    if (mm != null)
                        baseCost = mm.UploadCharge;

                    string warning = String.Empty;

                    if (llsdRequest.asset_type == "mesh")
                    {
                        string error;
                        int modelcost;

                        if (!m_ModelCost.MeshModelCost(llsdRequest.asset_resources, baseCost, out modelcost,
                            meshcostdata, out error, ref warning, out meshesSides))
                        {
                            LLSDAssetUploadError resperror = new LLSDAssetUploadError();
                            resperror.message = error;
                            resperror.identifier = UUID.Zero;

                            LLSDAssetUploadResponse errorResponse = new LLSDAssetUploadResponse();
                            errorResponse.uploader = "";
                            errorResponse.state = "error";
                            errorResponse.error = resperror;

                            lock (m_ModelCost)
                                m_FileAgentInventoryState = FileAgentInventoryState.idle;
                            return errorResponse;
                        }
                        cost = modelcost;
                    }
                    else
                    {
                        cost = baseCost;
                    }

                    if (cost > 0 && mm != null)
                    {
                        // check for test upload

                        if (m_ForceFreeTestUpload) // all are test
                        {
                            if (!(assetName.Length > 5 && assetName.StartsWith("TEST-"))) // has normal name lets change it
                                assetName = "TEST-" + assetName;

                            IsAtestUpload = true;
                        }

                        else if (m_enableFreeTestUpload) // only if prefixed with "TEST-"
                        {

                            IsAtestUpload = (assetName.Length > 5 && assetName.StartsWith("TEST-"));
                        }

                        if (IsAtestUpload) // let user know, still showing cost estimation
                            warning += "Upload will have no cost, for testing purposes only. Other uses are prohibited. Items will be local to region only, Inventory entry will be lost on logout";

                        // check funds
                        else
                        {
                            if (!mm.UploadCovered(client.AgentId, (int)cost))
                            {
                                LLSDAssetUploadError resperror = new LLSDAssetUploadError();
                                resperror.message = "Insuficient funds";
                                resperror.identifier = UUID.Zero;

                                LLSDAssetUploadResponse errorResponse = new LLSDAssetUploadResponse();
                                errorResponse.uploader = "";
                                errorResponse.state = "error";
                                errorResponse.error = resperror;
                                lock (m_ModelCost)
                                    m_FileAgentInventoryState = FileAgentInventoryState.idle;
                                return errorResponse;
                            }
                        }
                    }
                    else if (m_enableFreeTestUpload) // only if prefixed with "TEST-"
                    {
                        IsAtestUpload = (assetName.Length > 5 && assetName.StartsWith("TEST-"));
                        if (IsAtestUpload)
                            warning += "Upload for testing purposes only. Items will be local to region only, Inventory entry will be lost on logout";
                    }

                    if (client != null && warning != String.Empty)
                        client.SendAgentAlertMessage(warning, true);
                }
            }

            string assetDes = llsdRequest.description;
            UUID newAsset = UUID.Random();
            UUID newInvItem = UUID.Random();
            UUID parentFolder = llsdRequest.folder_id;
            string uploaderPath = GetNewCapPath();
            UUID texturesFolder = UUID.Zero;

            if (!IsAtestUpload && m_enableModelUploadTextureToInventory)
                texturesFolder = llsdRequest.texture_folder_id;

            AssetUploader uploader =
                new AssetUploader(assetName, assetDes, newAsset, newInvItem, parentFolder, llsdRequest.inventory_type,
                        llsdRequest.asset_type, uploaderPath, m_HostCapsObj.HttpListener, m_dumpAssetsToFile, cost,
                        texturesFolder, nreqtextures, nreqmeshs, nreqinstances, IsAtestUpload,
                        llsdRequest.next_owner_mask, llsdRequest.group_mask, llsdRequest.everyone_mask, meshesSides);

            m_HostCapsObj.HttpListener.AddStreamHandler(
                new BinaryStreamHandler(
                    "POST",
                    uploaderPath,
                    uploader.uploaderCaps,
                    "NewAgentInventoryRequest",
                    m_HostCapsObj.AgentID.ToString()));

            string protocol = "http://";
            if (m_HostCapsObj.SSLCaps)
                protocol = "https://";

            string uploaderURL = protocol + m_HostCapsObj.HostName + ":" + m_HostCapsObj.Port.ToString() + uploaderPath;

            LLSDAssetUploadResponse uploadResponse = new LLSDAssetUploadResponse();
            uploadResponse.uploader = uploaderURL;
            uploadResponse.state = "upload";
            uploadResponse.upload_price = (int)cost;

            if (llsdRequest.asset_type == "mesh")
            {
                uploadResponse.data = meshcostdata;
            }

            uploader.OnUpLoad += UploadCompleteHandler;

            lock (m_ModelCost)
                m_FileAgentInventoryState = FileAgentInventoryState.waitUpload;

            return uploadResponse;
        }

        /// <summary>
        /// Convert raw uploaded data into the appropriate asset and item.
        /// </summary>
        /// <param name="assetID"></param>
        /// <param name="inventoryItem"></param>
        /// <param name="data"></param>
        public void UploadCompleteHandler(string assetName, string assetDescription, UUID assetID,
                                          UUID inventoryItem, UUID parentFolder, byte[] data, string inventoryType,
                                          string assetType, int cost,
                                          UUID texturesFolder, int nreqtextures, int nreqmeshs, int nreqinstances,
                                          bool IsAtestUpload, ref string error,
                                          ref int nextOwnerMask, ref int groupMask, ref int everyoneMask, int[] meshesSides)
        {
            lock (m_ModelCost)
                m_FileAgentInventoryState = FileAgentInventoryState.processUpload;

            m_log.DebugFormat(
                "[BUNCH OF CAPS]: Uploaded asset {0} for inventory item {1}, inv type {2}, asset type {3}",
                assetID, inventoryItem, inventoryType, assetType);

            sbyte assType = 0;
            sbyte inType = 0;

            IClientAPI client = null;

            UUID owner_id = m_HostCapsObj.AgentID;
            UUID creatorID;

            bool istest = IsAtestUpload && m_enableFreeTestUpload;

            bool restrictPerms = m_RestrictFreeTestUploadPerms && istest;

            if (istest && m_testAssetsCreatorID != UUID.Zero)
                creatorID = m_testAssetsCreatorID;
            else
                creatorID = owner_id;

            string creatorIDstr = creatorID.ToString();

            IMoneyModule mm = m_Scene.RequestModuleInterface<IMoneyModule>();
            if (mm != null)
            {
                // make sure client still has enougth credit
                if (!mm.UploadCovered(m_HostCapsObj.AgentID, (int)cost))
                {
                    error = "Insufficient funds.";
                    return;
                }
            }

            // strings to types
            if (inventoryType == "sound")
            {
                inType = (sbyte)InventoryType.Sound;
                assType = (sbyte)AssetType.Sound;
            }
            else if (inventoryType == "snapshot")
            {
                inType = (sbyte)InventoryType.Snapshot;
            }
            else if (inventoryType == "animation")
            {
                inType = (sbyte)InventoryType.Animation;
                assType = (sbyte)AssetType.Animation;
            }
            else if (inventoryType == "animset")
            {
                inType = (sbyte)CustomInventoryType.AnimationSet;
                assType = (sbyte)CustomAssetType.AnimationSet;
                m_log.Debug("got animset upload request");
            }
            else if (inventoryType == "wearable")
            {
                inType = (sbyte)InventoryType.Wearable;
                switch (assetType)
                {
                    case "bodypart":
                        assType = (sbyte)AssetType.Bodypart;
                        break;
                    case "clothing":
                        assType = (sbyte)AssetType.Clothing;
                        break;
                }
            }
            else if (inventoryType == "object")
            {
                if (assetType == "mesh") // this code for now is for mesh models uploads only
                {
                    inType = (sbyte)InventoryType.Object;
                    assType = (sbyte)AssetType.Object;

                    List<Vector3> positions = new List<Vector3>();
                    List<Quaternion> rotations = new List<Quaternion>();
                    OSDMap request = (OSDMap)OSDParser.DeserializeLLSDXml(data);

                    // compare and get updated information
                    /* does nothing still we do need something to avoid special viewer to upload something diferent from the cost estimation
                                        bool mismatchError = true;

                                        while (mismatchError)
                                        {
                                            mismatchError = false;
                                        }

                                        if (mismatchError)
                                        {
                                            error = "Upload and fee estimation information don't match";
                                            lock (m_ModelCost)
                                                m_FileAgentInventoryState = FileAgentInventoryState.idle;

                                            return;
                                        }
                    */
                    OSDArray instance_list = (OSDArray)request["instance_list"];
                    OSDArray mesh_list = (OSDArray)request["mesh_list"];
                    OSDArray texture_list = (OSDArray)request["texture_list"];
                    SceneObjectGroup grp = null;

                    // create and store texture assets
                    bool doTextInv = (!istest && m_enableModelUploadTextureToInventory &&
                                    texturesFolder != UUID.Zero);


                    List<UUID> textures = new List<UUID>();


                    //                    if (doTextInv)
                    m_Scene.TryGetClient(m_HostCapsObj.AgentID, out client);

                    if (client == null) // don't put textures in inventory if there is no client
                        doTextInv = false;

                    for (int i = 0; i < texture_list.Count; i++)
                    {
                        AssetBase textureAsset = new AssetBase(UUID.Random(), assetName, (sbyte)AssetType.Texture, creatorIDstr);
                        textureAsset.Data = texture_list[i].AsBinary();
                        if (istest)
                            textureAsset.Local = true;
                        m_assetService.Store(textureAsset);
                        textures.Add(textureAsset.FullID);

                        if (doTextInv)
                        {
                            string name = assetName;
                            if (name.Length > 25)
                                name = name.Substring(0, 24);
                            name += "_Texture#" + i.ToString();
                            InventoryItemBase texitem = new InventoryItemBase();
                            texitem.Owner = m_HostCapsObj.AgentID;
                            texitem.CreatorId = creatorIDstr;
                            texitem.CreatorData = String.Empty;
                            texitem.ID = UUID.Random();
                            texitem.AssetID = textureAsset.FullID;
                            texitem.Description = "mesh model texture";
                            texitem.Name = name;
                            texitem.AssetType = (int)AssetType.Texture;
                            texitem.InvType = (int)InventoryType.Texture;
                            texitem.Folder = texturesFolder;

                            texitem.CurrentPermissions
                                = (uint)(PermissionMask.Move | PermissionMask.Copy | PermissionMask.Modify | PermissionMask.Transfer | PermissionMask.Export);

                            texitem.BasePermissions = (uint)PermissionMask.All | (uint)PermissionMask.Export;
                            texitem.EveryOnePermissions = 0;
                            texitem.NextPermissions = (uint)PermissionMask.All;
                            texitem.CreationDate = Util.UnixTimeSinceEpoch();

                            m_Scene.AddInventoryItem(client, texitem);
                            texitem = null;
                        }
                    }

                    // create and store meshs assets
                    List<UUID> meshAssets = new List<UUID>();
                    List<bool> meshAvatarSkeletons = new List<bool>();
                    List<bool> meshAvatarColliders = new List<bool>();

                    bool curAvSkeleton;
                    bool curAvCollider;
                    for (int i = 0; i < mesh_list.Count; i++)
                    {
                        curAvSkeleton = false;
                        curAvCollider = false;

                        // we do need to parse the mesh now
                        OSD osd = OSDParser.DeserializeLLSDBinary(mesh_list[i]);
                        if (osd is OSDMap)
                        {
                            OSDMap mosd = (OSDMap)osd;
                            if (mosd.ContainsKey("skeleton"))
                            {
                                OSDMap skeleton = (OSDMap)mosd["skeleton"];
                                int sksize = skeleton["size"].AsInteger();
                                if (sksize > 0)
                                    curAvSkeleton = true;
                            }
                        }

                        AssetBase meshAsset = new AssetBase(UUID.Random(), assetName, (sbyte)AssetType.Mesh, creatorIDstr);
                        meshAsset.Data = mesh_list[i].AsBinary();
                        if (istest)
                            meshAsset.Local = true;
                        m_assetService.Store(meshAsset);
                        meshAssets.Add(meshAsset.FullID);
                        meshAvatarSkeletons.Add(curAvSkeleton);
                        meshAvatarColliders.Add(curAvCollider);

                        // test code
                        if (curAvSkeleton && client != null)
                        {
                            string name = assetName;
                            if (name.Length > 25)
                                name = name.Substring(0, 24);
                            name += "_Mesh#" + i.ToString();
                            InventoryItemBase meshitem = new InventoryItemBase();
                            meshitem.Owner = m_HostCapsObj.AgentID;
                            meshitem.CreatorId = creatorIDstr;
                            meshitem.CreatorData = String.Empty;
                            meshitem.ID = UUID.Random();
                            meshitem.AssetID = meshAsset.FullID;
                            meshitem.Description = "mesh ";
                            meshitem.Name = name;
                            meshitem.AssetType = (int)AssetType.Mesh;
                            meshitem.InvType = (int)InventoryType.Mesh;
                            //                            meshitem.Folder = UUID.Zero; // send to default

                            meshitem.Folder = parentFolder; // dont let it go to folder Meshes that viewers dont show

                            // If we set PermissionMask.All then when we rez the item the next permissions will replace the current
                            // (owner) permissions.  This becomes a problem if next permissions are changed.
                            meshitem.CurrentPermissions
                                = (uint)(PermissionMask.Move | PermissionMask.Copy | PermissionMask.Modify | PermissionMask.Transfer);

                            meshitem.BasePermissions = (uint)PermissionMask.All;
                            meshitem.EveryOnePermissions = 0;
                            meshitem.NextPermissions = (uint)PermissionMask.All;
                            meshitem.CreationDate = Util.UnixTimeSinceEpoch();

                            m_Scene.AddInventoryItem(client, meshitem);
                            meshitem = null;
                        }
                    }

                    int skipedMeshs = 0;
                    // build prims from instances
                    for (int i = 0; i < instance_list.Count; i++)
                    {
                        OSDMap inner_instance_list = (OSDMap)instance_list[i];

                        // skip prims that are 2 small
                        Vector3 scale = inner_instance_list["scale"].AsVector3();

                        if (scale.X < m_PrimScaleMin || scale.Y < m_PrimScaleMin || scale.Z < m_PrimScaleMin)
                        {
                            skipedMeshs++;
                            continue;
                        }

                        OSDArray face_list = (OSDArray)inner_instance_list["face_list"];

                        PrimitiveBaseShape pbs = null;
                        if (inner_instance_list.ContainsKey("mesh")) // seems to happen always but ...
                        {
                            int meshindx = inner_instance_list["mesh"].AsInteger();
                            if (meshAssets.Count > meshindx)
                            {
                                if (meshesSides != null && meshesSides.Length > meshindx)
                                    pbs = PrimitiveBaseShape.CreateMesh(meshesSides[i], meshAssets[meshindx]);
                                else
                                    pbs = PrimitiveBaseShape.CreateMesh(face_list.Count, meshAssets[meshindx]);
                            }
                        }
                        if (pbs == null) // fallback
                            pbs = PrimitiveBaseShape.CreateBox();

                        Primitive.TextureEntry textureEntry
                            = new Primitive.TextureEntry(Primitive.TextureEntry.WHITE_TEXTURE);

                        for (uint face = 0; face < face_list.Count; face++)
                        {
                            OSDMap faceMap = (OSDMap)face_list[(int)face];

                            Primitive.TextureEntryFace f = textureEntry.CreateFace(face); //clone the default
                            if (faceMap.ContainsKey("fullbright"))
                                f.Fullbright = faceMap["fullbright"].AsBoolean();
                            if (faceMap.ContainsKey("diffuse_color"))
                                f.RGBA = faceMap["diffuse_color"].AsColor4();

                            int textureNum = faceMap["image"].AsInteger();
                            float imagerot = faceMap["imagerot"].AsInteger();
                            float offsets = (float)faceMap["offsets"].AsReal();
                            float offsett = (float)faceMap["offsett"].AsReal();
                            float scales = (float)faceMap["scales"].AsReal();
                            float scalet = (float)faceMap["scalet"].AsReal();

                            if (imagerot != 0)
                                f.Rotation = imagerot;

                            if (offsets != 0)
                                f.OffsetU = offsets;

                            if (offsett != 0)
                                f.OffsetV = offsett;

                            if (scales != 0)
                                f.RepeatU = scales;

                            if (scalet != 0)
                                f.RepeatV = scalet;

                            if (textures.Count > textureNum)
                                f.TextureID = textures[textureNum];

                            textureEntry.FaceTextures[face] = f;
                        }

                        if (face_list.Count > 0)
                        {
                            int last = face_list.Count - 1;
                            // we do need a better te compacting code
                            textureEntry.DefaultTexture = textureEntry.FaceTextures[last];
                            textureEntry.FaceTextures[last] = null;
                            pbs.TextureEntry = textureEntry.GetBytes(last);
                        }

                        Vector3 position = inner_instance_list["position"].AsVector3();
                        Quaternion rotation = inner_instance_list["rotation"].AsQuaternion();

                        byte physicsShapeType = (byte)PhysShapeType.convex; // default is simple convex
                        if (inner_instance_list.ContainsKey("physics_shape_type"))
                            physicsShapeType = (byte)inner_instance_list["physics_shape_type"].AsInteger();
                        byte material = (byte)Material.Wood;
                        if (inner_instance_list.ContainsKey("material"))
                            material = (byte)inner_instance_list["material"].AsInteger();

                        SceneObjectPart prim
                            = new SceneObjectPart(owner_id, pbs, position, Quaternion.Identity, Vector3.Zero);

                        prim.Scale = scale;
                        rotations.Add(rotation);
                        positions.Add(position);
                        prim.UUID = UUID.Random();
                        prim.CreatorID = creatorID;
                        prim.OwnerID = owner_id;
                        prim.GroupID = UUID.Zero;
                        prim.LastOwnerID = creatorID;
                        prim.RezzerID = creatorID;
                        prim.CreationDate = Util.UnixTimeSinceEpoch();

                        if (grp == null)
                            prim.Name = assetName;
                        else
                            prim.Name = assetName + "#" + i.ToString();

                        prim.EveryoneMask = 0;
                        prim.GroupMask = 0;

                        if (restrictPerms)
                        {
                            prim.BaseMask = (uint)(PermissionMask.Move | PermissionMask.Modify);
                            prim.OwnerMask = (uint)(PermissionMask.Move | PermissionMask.Modify);
                            prim.NextOwnerMask = 0;
                        }
                        else
                        {
                            prim.BaseMask = (uint)PermissionMask.All | (uint)PermissionMask.Export;
                            prim.OwnerMask = (uint)PermissionMask.All | (uint)PermissionMask.Export;
                            prim.GroupMask = prim.BaseMask & (uint)groupMask;
                            prim.EveryoneMask = prim.BaseMask & (uint)everyoneMask;
                            prim.NextOwnerMask = prim.BaseMask & (uint)nextOwnerMask;
                            // If the viewer gives us bogus permissions, revert to the SL
                            // default of transfer only.
                            if ((prim.NextOwnerMask & (uint)PermissionMask.All) == 0)
                                prim.NextOwnerMask = (uint)PermissionMask.Transfer;
                        }

                        if (istest)
                            prim.Description = "For testing only. Other uses are prohibited";
                        else
                            prim.Description = "";

                        prim.Material = material;
                        prim.PhysicsShapeType = physicsShapeType;

                        //                    prim.BaseMask = (uint)base_mask;
                        //                    prim.EveryoneMask = (uint)everyone_mask;
                        //                    prim.GroupMask = (uint)group_mask;
                        //                    prim.NextOwnerMask = (uint)next_owner_mask;
                        //                    prim.OwnerMask = (uint)owner_mask;

                        if (grp == null)
                        {
                            grp = new SceneObjectGroup(prim);
                            grp.LastOwnerID = creatorID;
                            grp.RezzerID = creatorID;
                        }
                        else
                            grp.AddPart(prim);
                    }

                    Vector3 rootPos = positions[0];

                    if (grp.Parts.Length > 1)
                    {
                        // Fix first link number
                        grp.RootPart.LinkNum++;

                        Quaternion rootRotConj = Quaternion.Conjugate(rotations[0]);
                        Quaternion tmprot;
                        Vector3 offset;

                        // fix children rotations and positions
                        for (int i = 1; i < rotations.Count; i++)
                        {
                            tmprot = rotations[i];
                            tmprot = rootRotConj * tmprot;

                            grp.Parts[i].RotationOffset = tmprot;

                            offset = positions[i] - rootPos;

                            offset *= rootRotConj;
                            grp.Parts[i].OffsetPosition = offset;
                        }

                        grp.AbsolutePosition = rootPos;
                        grp.UpdateGroupRotationR(rotations[0]);
                    }
                    else
                    {
                        grp.AbsolutePosition = rootPos;
                        grp.UpdateGroupRotationR(rotations[0]);
                    }

                    data = ASCIIEncoding.ASCII.GetBytes(SceneObjectSerializer.ToOriginalXmlFormat(grp));
                }

                else // not a mesh model
                {
                    m_log.ErrorFormat("[CAPS Asset Upload] got unsuported assetType for object upload");
                    return;
                }
            }

            AssetBase asset;
            asset = new AssetBase(assetID, assetName, assType, creatorIDstr);
            asset.Data = data;
            if (istest)
                asset.Local = true;
            if (AddNewAsset != null)
                AddNewAsset(asset);
            else if (m_assetService != null)
                m_assetService.Store(asset);

            InventoryItemBase item = new InventoryItemBase();
            item.Owner = m_HostCapsObj.AgentID;
            item.CreatorId = creatorIDstr;
            item.CreatorData = String.Empty;
            item.ID = inventoryItem;
            item.AssetID = asset.FullID;
            if (istest)
            {
                item.Description = "For testing only. Other uses are prohibited";
                item.Flags = (uint)(InventoryItemFlags.SharedSingleReference);
            }
            else
                item.Description = assetDescription;
            item.Name = assetName;
            item.AssetType = assType;
            item.InvType = inType;
            item.Folder = parentFolder;

            // If we set PermissionMask.All then when we rez the item the next permissions will replace the current
            // (owner) permissions.  This becomes a problem if next permissions are changed.

            if (inType == (sbyte)CustomInventoryType.AnimationSet)
            {
                AnimationSet.setCreateItemPermitions(item);
            }

            else if (restrictPerms)
            {
                item.BasePermissions = (uint)(PermissionMask.Move | PermissionMask.Modify);
                item.CurrentPermissions = (uint)(PermissionMask.Move | PermissionMask.Modify);
                item.GroupPermissions = 0;
                item.EveryOnePermissions = 0;
                item.NextPermissions = 0;
            }
            else
            {
                item.BasePermissions = (uint)PermissionMask.All | (uint)PermissionMask.Export;
                item.CurrentPermissions = (uint)PermissionMask.All | (uint)PermissionMask.Export;
                item.GroupPermissions = item.BasePermissions & (uint)groupMask;
                item.EveryOnePermissions = item.BasePermissions & (uint)everyoneMask;
                item.NextPermissions = item.BasePermissions & (uint)nextOwnerMask;
                if ((item.NextPermissions & (uint)PermissionMask.All) == 0)
                    item.NextPermissions = (uint)PermissionMask.Transfer;
            }

            item.CreationDate = Util.UnixTimeSinceEpoch();

            everyoneMask = (int)item.EveryOnePermissions;
            groupMask = (int)item.GroupPermissions;
            nextOwnerMask = (int)item.NextPermissions;

            m_Scene.TryGetClient(m_HostCapsObj.AgentID, out client);

            if (AddNewInventoryItem != null)
            {
                if (istest)
                {
                    m_Scene.AddInventoryItem(client, item);
                    /*
                                        AddNewInventoryItem(m_HostCapsObj.AgentID, item, 0);
                                        if (client != null)
                                            client.SendAgentAlertMessage("Upload will have no cost, for personal test purposes only. Other uses are forbiden. Items may not work on a another region" , true);
                     */
                }
                else
                {
                    AddNewInventoryItem(m_HostCapsObj.AgentID, item, (uint)cost);
                    //                    if (client != null)
                    //                    {
                    //                        // let users see anything..  i don't so far
                    //                        string str;
                    //                        if (cost > 0)
                    //                            // dont remember where is money unit name to put here
                    //                            str = "Upload complete. charged " + cost.ToString() + "$";
                    //                        else
                    //                            str = "Upload complete";
                    //                        client.SendAgentAlertMessage(str, true);
                    //                    }
                }
            }

            lock (m_ModelCost)
                m_FileAgentInventoryState = FileAgentInventoryState.idle;
        }

        public void CreateInventoryCategory(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            if(httpRequest.HttpMethod != "POST")
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }
            if (m_Scene.InventoryService == null)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotImplemented;
                return;
            }

            ScenePresence sp = m_Scene.GetScenePresence(m_AgentID);
            if (sp == null || sp.IsDeleted)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                httpResponse.AddHeader("Retry-After", "60");
                return;
            }

            OSDMap req;
            OSD tmp;
            try
            {
                req = (OSDMap)OSDParser.DeserializeLLSDXml(httpRequest.InputStream);
            }
            catch
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            try
            {
                while (true) // kinda goto
                {
                    if (!req.TryGetValue("folder_id", out tmp) || !(tmp is OSDUUID))
                        break;
                    UUID folderID = tmp.AsUUID();

                    if(folderID == UUID.Zero)
                        break;

                    if (!req.TryGetValue("parent_id", out tmp) || !(tmp is OSDUUID))
                        break;
                    UUID parentID = tmp.AsUUID();

                    if (!req.TryGetValue("name", out tmp) || !(tmp is OSDString))
                        break;
                    string folderName = tmp.AsString();

                    if(string.IsNullOrEmpty(folderName))
                        break;

                    if(folderName.Length > 63)
                        folderName = folderName.Substring(0, 63);

                    if (!req.TryGetValue("type", out tmp) || !(tmp is OSDInteger))
                        break;
                    int folderType = tmp.AsInteger();

                    InventoryFolderBase folder = new InventoryFolderBase(folderID, folderName, m_AgentID, (short)folderType, parentID, 1);
                    if (!m_Scene.InventoryService.AddFolder(folder))
                        break;

                    // costly double check plus possible service changes
                    folder = m_Scene.InventoryService.GetFolder(m_AgentID, folderID);
                    if (folder == null)
                        break;

                    StringBuilder sb = LLSDxmlEncode.Start();
                    LLSDxmlEncode.AddMap(sb);
                    LLSDxmlEncode.AddElem("folder_id", folder.ID, sb);
                    LLSDxmlEncode.AddElem("name", folder.Name, sb);
                    LLSDxmlEncode.AddElem("parent_id", folder.ParentID, sb);
                    LLSDxmlEncode.AddElem("type", folder.Type, sb);
                    LLSDxmlEncode.AddEndMap(sb);

                    httpResponse.RawBuffer = LLSDxmlEncode.EndToNBBytes(sb);
                    httpResponse.StatusCode = (int)HttpStatusCode.OK;
                    return;
                }
            }
            catch { }

            m_log.Debug("[CAPS]: CreateInventoryCategory failed to process request");
            httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
        }

        /// <summary>
        /// Called by the CopyInventoryFromNotecard caps handler.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="path"></param>
        /// <param name="param"></param>

        public void CopyInventoryFromNotecard(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse, OSDMap content)
        {
            InventoryItemBase copyItem = null;
            IClientAPI client = null;

            httpResponse.StatusCode = (int)HttpStatusCode.OK;

            try
            {
                UUID objectID = content["object-id"].AsUUID();
                UUID notecardID = content["notecard-id"].AsUUID();
                UUID folderID = content["folder-id"].AsUUID();
                UUID itemID = content["item-id"].AsUUID();

                //  m_log.InfoFormat("[CAPS]: CopyInventoryFromNotecard, FolderID:{0}, ItemID:{1}, NotecardID:{2}, ObjectID:{3}", folderID, itemID, notecardID, objectID);

                UUID noteAssetID = UUID.Zero;
                UUID agentID = m_HostCapsObj.AgentID;

                m_Scene.TryGetClient(agentID, out client);

                if (objectID != UUID.Zero)
                {
                    SceneObjectPart part = m_Scene.GetSceneObjectPart(objectID);
                    if(part == null)
                        throw new Exception("failed to find object with notecard item" + notecardID.ToString());

                    TaskInventoryItem taskItem = part.Inventory.GetInventoryItem(notecardID);
                    if (taskItem == null || taskItem.AssetID == UUID.Zero)
                        throw new Exception("Failed to find notecard item" + notecardID.ToString());

                    if (!m_Scene.Permissions.CanCopyObjectInventory(notecardID, objectID, agentID))
                        throw new Exception("No permission to copy notecard from object");

                    noteAssetID = taskItem.AssetID;
                }
                else
                {
                    // we may have the item around...
                    InventoryItemBase localitem = m_Scene.InventoryService.GetItem(agentID, itemID);
                    if (localitem != null)
                    {
                        string message;
                        copyItem = m_Scene.GiveInventoryItem(agentID, localitem.Owner, itemID, folderID, out message);
                        if (copyItem == null)
                            throw new Exception("Failed to find notecard item" + notecardID.ToString());

                        m_log.InfoFormat("[CAPS]: CopyInventoryFromNotecard, ItemID:{0}, FolderID:{1}", copyItem.ID, copyItem.Folder);
                        if (client != null)
                            client.SendBulkUpdateInventory(copyItem);
                        return;
                    }

                    if (notecardID != UUID.Zero)
                    {
                        InventoryItemBase noteItem = m_Scene.InventoryService.GetItem(agentID, notecardID);
                        if (noteItem == null || noteItem.AssetID == UUID.Zero)
                            throw new Exception("Failed to find notecard item" + notecardID.ToString());
                        noteAssetID = noteItem.AssetID;
                    }
                }

                AssetBase noteAsset = m_Scene.AssetService.Get(noteAssetID.ToString());
                if (noteAsset == null || noteAsset.Type != (sbyte)AssetType.Notecard)
                    throw new Exception("Failed to find the notecard asset" + notecardID.ToString());

                InventoryItemBase item = SLUtil.GetEmbeddedItem(noteAsset.Data, itemID);
                if(item == null)
                    throw new Exception("Failed to find the notecard item" + notecardID.ToString());

                if (!m_Scene.Permissions.CanTransferUserInventory(itemID, item.Owner, agentID))
                    throw new Exception("Notecard item permissions check fail" + notecardID.ToString());

                if (!m_Scene.Permissions.BypassPermissions())
                {
                    if ((item.CurrentPermissions & (uint)PermissionMask.Transfer) == 0)
                        throw new Exception("Notecard item permissions check fail" + notecardID.ToString());
                }

                // check if we do have the item asset
                noteAsset = m_Scene.AssetService.Get(item.AssetID.ToString());
                if (noteAsset == null)
                    throw new Exception("Failed to find the notecard " + notecardID.ToString() +" item asset");

                // find where to put it
                InventoryFolderBase folder = null;
                if (folderID != UUID.Zero)
                    folder = m_Scene.InventoryService.GetFolder(agentID, folderID);

                if (folder == null && Enum.IsDefined(typeof(FolderType), (sbyte)item.AssetType))
                    folder = m_Scene.InventoryService.GetFolderForType(agentID, (FolderType)item.AssetType);

                if (folder == null)
                    folder = m_Scene.InventoryService.GetRootFolder(agentID);

                if (folder == null)
                    throw new Exception("Failed to find a folder for the notecard item" + notecardID.ToString());

                item.Folder = folder.ID;

                // do change owner permissions (c&p from scene inventory code)
                if (m_Scene.Permissions.PropagatePermissions() && item.Owner != agentID)
                {
                    uint permsMask = ~((uint)PermissionMask.Copy |
                                        (uint)PermissionMask.Transfer |
                                        (uint)PermissionMask.Modify |
                                        (uint)PermissionMask.Export);

                    uint nextPerms = permsMask | (item.NextPermissions &
                                        ((uint)PermissionMask.Copy |
                                        (uint)PermissionMask.Transfer |
                                        (uint)PermissionMask.Modify));

                    if (nextPerms == permsMask)
                        nextPerms |= (uint)PermissionMask.Transfer;

                    uint basePerms = item.BasePermissions | (uint)PermissionMask.Move;
                    uint ownerPerms = item.CurrentPermissions;

                    uint foldedPerms = (item.CurrentPermissions & (uint)PermissionMask.FoldedMask) << (int)PermissionMask.FoldingShift;
                    if (foldedPerms != 0 && item.InvType == (int)InventoryType.Object)
                    {
                        foldedPerms |= permsMask;

                        bool isRootMod = (item.CurrentPermissions &
                                            (uint)PermissionMask.Modify) != 0 ?
                                            true : false;

                        ownerPerms &= foldedPerms;
                        basePerms &= foldedPerms;

                        if (isRootMod)
                        {
                            ownerPerms |= (uint)PermissionMask.Modify;
                            basePerms |= (uint)PermissionMask.Modify;
                        }
                    }

                    ownerPerms &= nextPerms;
                    basePerms &= nextPerms;
                    basePerms &= ~(uint)PermissionMask.FoldedMask;
                    basePerms |= ((basePerms >> 13) & 7) | (((basePerms & (uint)PermissionMask.Export) != 0) ? (uint)PermissionMask.FoldedExport : 0);
                    item.BasePermissions = basePerms;
                    item.CurrentPermissions = ownerPerms;
                    item.Flags |= (uint)InventoryItemFlags.ObjectSlamPerm;
                    item.Flags &= ~(uint)(InventoryItemFlags.ObjectOverwriteBase | InventoryItemFlags.ObjectOverwriteOwner | InventoryItemFlags.ObjectOverwriteGroup | InventoryItemFlags.ObjectOverwriteEveryone | InventoryItemFlags.ObjectOverwriteNextOwner);
                    item.NextPermissions = item.NextPermissions;
                    item.EveryOnePermissions = item.EveryOnePermissions & nextPerms;
                }
                else
                {
                    //??
                    item.EveryOnePermissions &= item.NextPermissions;
                }

                item.GroupPermissions = 0; // we killed the group
                item.Owner = agentID;

                if (!m_Scene.InventoryService.AddItem(item))
                    throw new Exception("Failed create the notecard item" + notecardID.ToString());

                m_log.InfoFormat("[CAPS]: CopyInventoryFromNotecard, ItemID:{0} FolderID:{1}", item.ID, item.Folder);
                if (client != null)
                    client.SendBulkUpdateInventory(item);
                return;
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[CAPS]: CopyInventoryFromNotecard : {0}", e.Message);
                copyItem = null;
            }

            if(copyItem == null)
            {
                if (client != null)
                    client.SendAlertMessage("Failed to retrieve item");
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
            }
        }

        public void GetObjectPhysicsData(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse, OSDMap req)
        {
            OSDArray object_ids;
            try
            {
                object_ids = (OSDArray)req["object_ids"];
            }
            catch
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            StringBuilder lsl = LLSDxmlEncode.Start();

            if (object_ids.Count == 0)
                LLSDxmlEncode.AddEmptyMap(lsl);
            else
            {
                LLSDxmlEncode.AddMap(lsl);
                for (int i = 0; i < object_ids.Count; i++)
                {
                    UUID uuid = object_ids[i].AsUUID();

                    SceneObjectPart obj = m_Scene.GetSceneObjectPart(uuid);
                    if (obj != null)
                    {
                        LLSDxmlEncode.AddMap(uuid.ToString(), lsl);

                        LLSDxmlEncode.AddElem("PhysicsShapeType", obj.PhysicsShapeType, lsl);
                        LLSDxmlEncode.AddElem("Density", obj.Density, lsl);
                        LLSDxmlEncode.AddElem("Friction", obj.Friction, lsl);
                        LLSDxmlEncode.AddElem("Restitution", obj.Restitution, lsl);
                        LLSDxmlEncode.AddElem("GravityMultiplier", obj.GravityModifier, lsl);

                        LLSDxmlEncode.AddEndMap(lsl);
                    }
                    LLSDxmlEncode.AddEndMap(lsl);
                }
            }

            httpResponse.RawBuffer = LLSDxmlEncode.EndToNBBytes(lsl);
            httpResponse.StatusCode = (int)HttpStatusCode.OK;
        }

        public void GetObjectCost(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse, OSDMap req)
        {
            OSDArray object_ids;
            try
            {
                object_ids = (OSDArray)req["object_ids"];
            }
            catch
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            StringBuilder lsl = LLSDxmlEncode.Start(512);

            if (object_ids.Count == 0)
                LLSDxmlEncode.AddEmptyMap(lsl);
            else
            {
                bool haveone = false;
                LLSDxmlEncode.AddMap(lsl);
                for (int i = 0; i < object_ids.Count; i++)
                {
                    UUID uuid = object_ids[i].AsUUID();

                    SceneObjectPart part = m_Scene.GetSceneObjectPart(uuid);
                    SceneObjectGroup grp = null;
                    if (part != null)
                        grp = part.ParentGroup;
                    if (grp != null)
                    {
                        haveone = true;
                        float linksetCost;
                        float linksetPhysCost;
                        float partCost;
                        float partPhysCost;

                        grp.GetResourcesCosts(part, out linksetCost, out linksetPhysCost, out partCost, out partPhysCost);

                        LLSDxmlEncode.AddMap(uuid.ToString(), lsl);

                        LLSDxmlEncode.AddElem("linked_set_resource_cost", linksetCost, lsl);
                        LLSDxmlEncode.AddElem("resource_cost", partCost, lsl);
                        LLSDxmlEncode.AddElem("physics_cost", partPhysCost, lsl);
                        LLSDxmlEncode.AddElem("linked_set_physics_cost", linksetPhysCost, lsl);
                        LLSDxmlEncode.AddElem("resource_limiting_type", "legacy", lsl);

                        LLSDxmlEncode.AddEndMap(lsl);
                    }
                }
                if (!haveone)
                {
                    LLSDxmlEncode.AddMap(UUID.Zero.ToString(), lsl);
                    LLSDxmlEncode.AddElem("linked_set_resource_cost", 0, lsl);
                    LLSDxmlEncode.AddElem("resource_cost", 0, lsl);
                    LLSDxmlEncode.AddElem("physics_cost", 0, lsl);
                    LLSDxmlEncode.AddElem("linked_set_physics_cost", 0, lsl);
                    LLSDxmlEncode.AddElem("resource_limiting_type", "legacy", lsl);
                    LLSDxmlEncode.AddEndMap(lsl);
                }
                LLSDxmlEncode.AddEndMap(lsl);
            }

            httpResponse.RawBuffer = LLSDxmlEncode.EndToNBBytes(lsl);
            httpResponse.StatusCode = (int)HttpStatusCode.OK;
        }

        public void ResourceCostSelected(IOSHttpRequest httpRequest,IOSHttpResponse httpResponse, OSDMap req)
        {
            float phys=0;
            float stream=0;
            float simul=0;

            if (req.ContainsKey("selected_roots"))
            {
                OSDArray object_ids = (OSDArray)req["selected_roots"];

                // should go by SOG suming costs for all parts
                // ll v3 works ok with several objects select we get the list and adds ok
                // FS calls per object so results are wrong guess fs bug
                for (int i = 0; i < object_ids.Count; i++)
                {
                    UUID uuid = object_ids[i].AsUUID();
                    float Physc;
                    float simulc;
                    float streamc;

                    SceneObjectGroup grp = m_Scene.GetGroupByPrim(uuid);
                    if (grp != null)
                    {
                        grp.GetSelectedCosts(out Physc, out streamc, out simulc);
                        phys += Physc;
                        stream += streamc;
                        simul += simulc;
                    }
                }
            }
            else if (req.ContainsKey("selected_prims"))
            {
                OSDArray object_ids = (OSDArray)req["selected_prims"];

                // don't see in use in any of the 2 viewers
                // guess it should be for edit linked but... nothing
                // should go to SOP per part
                for (int i = 0; i < object_ids.Count; i++)
                {
                    UUID uuid = object_ids[i].AsUUID();

                    SceneObjectPart part = m_Scene.GetSceneObjectPart(uuid);
                    if (part != null)
                    {
                        phys += part.PhysicsCost;
                        stream += part.StreamingCost;
                        simul += part.SimulationCost;
                    }
                }
            }

            StringBuilder lsl = LLSDxmlEncode.Start();
            LLSDxmlEncode.AddMap(lsl);

            LLSDxmlEncode.AddMap("selected", lsl);

            LLSDxmlEncode.AddElem("physics", phys, lsl);
            LLSDxmlEncode.AddElem("streaming", stream, lsl);
            LLSDxmlEncode.AddElem("simulation", simul, lsl);

            LLSDxmlEncode.AddEndMap(lsl);
            LLSDxmlEncode.AddEndMap(lsl);

            // resp["transaction_id"] = "undef";
            httpResponse.RawBuffer = LLSDxmlEncode.EndToNBBytes(lsl);
            httpResponse.StatusCode = (int)HttpStatusCode.OK;
        }

        public void UpdateAgentInformation(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            if (httpRequest.HttpMethod != "POST")
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            // this is wrong now ignores request and sends same result for all
            // we don't store and worse don't use that to filter contents

            // OSDMap req = (OSDMap)OSDParser.DeserializeLLSDXml(request);
            OSDMap resp = new OSDMap();

            OSDMap accessPrefs = new OSDMap();
            accessPrefs["max"] = "A";

            resp["access_prefs"] = accessPrefs;

            httpResponse.RawBuffer = OSDParser.SerializeLLSDXmlBytes(resp);
            httpResponse.StatusCode = (int)HttpStatusCode.OK;
        }

        public bool OSDMapTOVector3(OSDMap map, out Vector3 v)
        {
            v = Vector3.Zero;
            if (!map.ContainsKey("X"))
                return false;
            if (!map.ContainsKey("Y"))
                return false;
            if (!map.ContainsKey("Z"))
                return false;
            v.X = (float)map["X"].AsReal();
            v.Y = (float)map["Y"].AsReal();
            v.Z = (float)map["Z"].AsReal();
            return true;
        }

        public void HomeLocation(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            if (httpRequest.HttpMethod != "POST")
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            bool fail = true;
            string message = "Set Home request failed";
            //int locationID = 1;
            Vector3 pos = Vector3.Zero;
            Vector3 lookAt = Vector3.Zero;

            IClientAPI client = null;
            ScenePresence sp;

            while (true)
            {
                if (m_Scene.GridUserService == null)
                    break;

                if (m_Scene.UserManagementModule == null)
                    break;

                m_Scene.TryGetScenePresence(m_AgentID, out sp);
                if (sp == null || sp.IsChildAgent || sp.IsDeleted)
                    break;

                if (sp.IsInTransit && !sp.IsInLocalTransit)
                    break;

                client = sp.ControllingClient;

                if (!m_Scene.UserManagementModule.IsLocalGridUser(m_AgentID))
                    break;

                OSDMap req;
                try
                {
                    req = (OSDMap)OSDParser.DeserializeLLSDXml(httpRequest.InputStream);
                }
                catch
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

                OSD tmp;
                if (!req.TryGetValue("HomeLocation", out tmp) || !(tmp is OSDMap))
                    break;

                OSDMap HLocation = (OSDMap)tmp;

                if(!HLocation.TryGetValue("LocationPos", out tmp) || !(tmp is OSDMap))
                    break;
                if (!OSDMapTOVector3((OSDMap)tmp, out pos))
                    break;

                if (!HLocation.TryGetValue("LocationLookAt", out tmp) || !(tmp is OSDMap))
                    break;
                if (!OSDMapTOVector3((OSDMap)tmp, out lookAt))
                    break;

                //locationID = HLocation["LocationId"].AsInteger();

                ILandObject land = m_Scene.LandChannel.GetLandObject(pos);
                if (land == null)
                    break;

                ulong gpowers = client.GetGroupPowers(land.LandData.GroupID);
                SceneObjectGroup telehub = null;
                if (m_Scene.RegionInfo.RegionSettings.TelehubObject != UUID.Zero)
                    // Does the telehub exist in the scene?
                    telehub = m_Scene.GetSceneObjectGroup(m_Scene.RegionInfo.RegionSettings.TelehubObject);

                if (!m_Scene.Permissions.IsAdministrator(m_AgentID) && // (a) gods and land managers can set home
                    !m_Scene.Permissions.IsGod(m_AgentID) &&
                    m_AgentID != land.LandData.OwnerID && // (b) land owners can set home
                                                          // (c) members of the land-associated group in roles that can set home
                    ((gpowers & (ulong)GroupPowers.AllowSetHome) != (ulong)GroupPowers.AllowSetHome) &&
                    // (d) parcels with telehubs can be the home of anyone
                    (telehub == null || !land.ContainsPoint((int)telehub.AbsolutePosition.X, (int)telehub.AbsolutePosition.Y)))
                {
                    message = "You are not allowed to set your home location in this parcel.";
                    break;
                }

                string userId;
                UUID test;
                if (!m_Scene.UserManagementModule.GetUserUUI(m_AgentID, out userId))
                {
                    message = "Set Home request failed. (User Lookup)";
                    break;
                }

                if (!UUID.TryParse(userId, out test))
                {
                    message = "Set Home request failed. (HG visitor)";
                    break;
                }

                if (m_Scene.GridUserService.SetHome(userId, land.RegionUUID, pos, lookAt))
                    fail = false;

                break;
            }

            OSDMap resp = new OSDMap();

            if (fail)
            {
                if (client != null)
                    client.SendAlertMessage(message);
                resp["success"] = "false";
            }
            else
            {
                // so its http but still needs a udp reply to inform user? crap :p
                if(client != null)
                   client.SendAlertMessage("Home position set.","HomePositionSet");

                resp["success"] = "true";
                OSDMap homeloc = new OSDMap();
                OSDMap homelocpos = new OSDMap();
                // for some odd reason viewers send pos as reals but read as integer
                homelocpos["X"] = new OSDReal(pos.X);
                homelocpos["Y"] = new OSDReal(pos.Y);
                homelocpos["Z"] = new OSDReal(pos.Z);
                homeloc["LocationPos"] = homelocpos;

                resp["HomeLocation"] = homeloc;
            }

            httpResponse.RawBuffer = Util.UTF8NBGetbytes(OSDParser.SerializeLLSDXmlString(resp));
            httpResponse.StatusCode = (int)HttpStatusCode.OK;
        }

        private static int CompareRolesByMembersDesc(GroupRolesData x, GroupRolesData y)
        {
            return -(x.Members.CompareTo(y.Members));
        }

        public void GroupMemberData(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            if (httpRequest.HttpMethod != "POST")
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            OSDMap resp = new OSDMap();

            bool fail = true;
            IClientAPI client = null;
            ScenePresence sp;
            IGroupsModule m_GroupsModule;
            UUID groupID = UUID.Zero;

            while (true)
            {
                m_GroupsModule = m_Scene.RequestModuleInterface<IGroupsModule>();
                if (m_GroupsModule == null)
                    break;

                m_Scene.TryGetScenePresence(m_AgentID, out sp);
                if (sp == null || sp.IsChildAgent || sp.IsDeleted)
                    break;

                if (sp.IsInTransit && !sp.IsInLocalTransit)
                    break;

                client = sp.ControllingClient;

                OSDMap req;
                try
                {
                    req = (OSDMap)OSDParser.DeserializeLLSDXml(httpRequest.InputStream);
                }
                catch
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

                OSD tmp;
                if(!req.TryGetValue("group_id", out tmp) || !(tmp is OSDUUID))
                    break;

                groupID = tmp.AsUUID();
                if(groupID == UUID.Zero)
                    break;

                List<GroupRolesData> roles = m_GroupsModule.GroupRoleDataRequest(client, groupID);
                if (roles == null || roles.Count == 0)
                    break;

                List<GroupMembersData> members = m_GroupsModule.GroupMembersRequest(client, groupID);
                if (members == null || members.Count == 0)
                    break;

                int memberCount = members.Count;

                Dictionary<string, int> titles = new Dictionary<string, int>();
                int i = 0;

                ulong defaultPowers = 0;


                // build titles array and index
                roles.Sort(CompareRolesByMembersDesc);

                OSDArray osdtitles = new OSDArray();
                foreach (GroupRolesData grd in roles)
                {
                    if (grd.Title == null)
                        continue;
                    string title = grd.Title;
                    if (i == 0)
                        defaultPowers = grd.Powers;

                    if (!titles.ContainsKey(title))
                    {
                        titles[title] = i++;
                        osdtitles.Add(new OSDString(title));
                    }
                }

                if (titles.Count == 0)
                    break;

                OSDMap osdmembers = new OSDMap();
                foreach (GroupMembersData gmd in members)
                {
                    OSDMap m = new OSDMap();
                    if (gmd.OnlineStatus != null && gmd.OnlineStatus != "")
                        m["last_login"] = new OSDString(gmd.OnlineStatus);
                    if (gmd.AgentPowers != defaultPowers)
                        m["powers"] = new OSDString((gmd.AgentPowers).ToString("X"));
                    if (gmd.Title != null && titles.ContainsKey(gmd.Title) && titles[gmd.Title] != 0)
                        m["title"] = new OSDInteger(titles[gmd.Title]);
                    if (gmd.IsOwner)
                        m["owner"] = new OSDString("true");
                    if (gmd.Contribution != 0)
                        m["donated_square_meters"] = new OSDInteger(gmd.Contribution);

                    osdmembers[(gmd.AgentID).ToString()] = m;
                }

                OSDMap osddefaults = new OSDMap();
                osddefaults["default_powers"] = new OSDString(defaultPowers.ToString("X"));

                resp["group_id"] = new OSDUUID(groupID);
                resp["agent_id"] = new OSDUUID(m_AgentID);
                resp["member_count"] = new OSDInteger(memberCount);
                resp["defaults"] = osddefaults;
                resp["titles"] = osdtitles;
                resp["members"] = osdmembers;

                fail = false;
                break;
            }

            if (fail)
            {
                resp["group_id"] = new OSDUUID(groupID);
                resp["agent_id"] = new OSDUUID(m_AgentID);
                resp["member_count"] = new OSDInteger(0);
                resp["defaults"] = new OSDMap();
                resp["titles"] = new OSDArray();
                resp["members"] = new OSDMap();
            }

            httpResponse.RawBuffer = Util.UTF8NBGetbytes(OSDParser.SerializeLLSDXmlString(resp));
            httpResponse.StatusCode = (int)HttpStatusCode.OK;
        }

        #region Cool Name functions
        string getUserName(string firstname, string lastname)
        {
            if (m_TrimResident && lastname.ToLower() == "resident")
            {
                return firstname.ToLower();
            }
            else 
            {
                return string.Format("{0}.{1}", firstname, lastname).ToLower();
            }
        }

        string getDefaultName(string firstname, string lastname)
        {
            if (m_TrimResident && lastname.ToLower() == "resident")
            {
                return firstname;
            }
            else
            {
                return string.Format("{0} {1}", firstname, lastname);
            }
        }
		#endregion

		#region SetDisplayName EventQueue Items
        public OSD DisplayNameUpdate(string newDisplayName, string oldDisplayName, UUID iD, bool isDefault, string first,
                                     string last, string account, DateTime nextUpdate)
        {
            OSDMap nameReply = new OSDMap { { "message", OSD.FromString("DisplayNameUpdate") } };
            OSDMap body = new OSDMap();

            OSDMap agentData = new OSDMap();
            agentData["display_name"] = OSD.FromString(newDisplayName);
            agentData["id"] = OSD.FromUUID(iD);
            agentData["is_display_name_default"] = OSD.FromBoolean(isDefault);
            agentData["legacy_first_name"] = OSD.FromString(first);
            agentData["legacy_last_name"] = OSD.FromString(last);
            agentData["username"] = OSD.FromString(account);
            agentData["display_name_next_update"] = OSD.FromDate(nextUpdate);

            body.Add("agent", agentData);
            body.Add("agent_id", OSD.FromUUID(iD));
            body.Add("old_display_name", OSD.FromString(oldDisplayName));

            nameReply.Add("body", body);

            return nameReply;
        }

        void DisplayNameUpdate(string newDisplayName, string oldDisplayName, NameInfo nameInfo, UUID toAgentID, DateTime nextUpdate)
        {
            if (m_EventQueue != null)
            {
                if (string.IsNullOrWhiteSpace(newDisplayName))
                    newDisplayName = m_TrimResident && nameInfo.LastName.ToLower() == "resident" ? newDisplayName = nameInfo.FirstName : nameInfo.Name;

                OSD update = null;

                if (m_UserManager.IsLocalGridUser(toAgentID))
                {
                    update = DisplayNameUpdate(
                        newDisplayName, oldDisplayName, m_AgentID, nameInfo.IsDefault, nameInfo.FirstName, nameInfo.LastName, 
                        m_TrimResident && nameInfo.LastName.ToLower() == "resident" ? nameInfo.FirstName.ToLower() : nameInfo.UserName, nextUpdate);
                }
                else
                {
					string bname = m_TrimResident && nameInfo.LastName.ToLower() == "resident" ? nameInfo.FirstName : nameInfo.Name;

                    string firstname = getUserName(nameInfo.FirstName, nameInfo.LastName);
                    string lastname = "@" + new Uri(m_HomeURL).Authority;
                    string username = (firstname + lastname).ToLower();

                    bool is_default = nameInfo.IsDefault;

                    if (is_default)
                    {
                        newDisplayName = bname;
                        is_default = false;
                    }

                    update = DisplayNameUpdate(newDisplayName, oldDisplayName, m_AgentID, is_default, firstname, lastname, username, nextUpdate);
                }

                m_EventQueue.Enqueue(update, toAgentID);
            }
        }


        OSD DisplayNameReply(string newDisplayName, string oldDisplayName, UUID iD, bool isDefault, string first,
                                    string last, string account, DateTime nextUpdate)
        {
            OSDMap nameReply = new OSDMap();

            OSDMap body = new OSDMap();
            OSDMap content = new OSDMap();
            OSDMap agentData = new OSDMap();

            content.Add("display_name", OSD.FromString(newDisplayName));
            content.Add("display_name_next_update", OSD.FromDate(nextUpdate));
            content.Add("id", OSD.FromUUID(iD));
            content.Add("is_display_name_default", OSD.FromBoolean(isDefault));
            content.Add("legacy_first_name", OSD.FromString(first));
            content.Add("legacy_last_name", OSD.FromString(last));
            content.Add("username", OSD.FromString(account));

            body.Add("content", content);
            body.Add("agent", agentData);
            //body.Add ("old_display_name", OSD.FromString (oldDisplayName));
            body.Add("reason", OSD.FromString("OK"));
            body.Add("status", OSD.FromInteger(200));

            nameReply.Add("body", body);
            nameReply.Add("message", OSD.FromString("SetDisplayNameReply"));

            return nameReply;
        }

        public void SetDisplayNameReply(string newDisplayName, string oldDisplayName, NameInfo nameInfo, DateTime nextUpdate)
        {
            if (m_EventQueue != null)
            {
                if (string.IsNullOrWhiteSpace(newDisplayName))
                    newDisplayName = m_TrimResident && nameInfo.LastName.ToLower() == "resident" ? newDisplayName = nameInfo.FirstName : nameInfo.Name;

                OSD item = DisplayNameReply(newDisplayName, oldDisplayName, m_AgentID, nameInfo.IsDefault,
                                            nameInfo.FirstName, nameInfo.LastName, m_TrimResident ? nameInfo.UserName : getUserName(nameInfo.FirstName, nameInfo.LastName), nextUpdate);

                m_EventQueue.Enqueue(item, m_AgentID);
            }
        }
        #endregion

        public void GetDisplayNames(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            if (httpRequest.HttpMethod != "GET")
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            ScenePresence sp = m_Scene.GetScenePresence(m_AgentID);

            if (sp == null || sp.IsDeleted)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.Gone;
                return;
            }

            if (sp.IsInTransit && !sp.IsInLocalTransit)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                httpResponse.AddHeader("Retry-After","30");
                return;
            }

            // Full content request
            NameValueCollection query = httpRequest.QueryString;
            string[] ids = query.GetValues("ids");

            Dictionary<UUID, NameInfo> names = m_DisplayNames.GetDisplayNames(ids);

            bool is_local = m_UserManager.IsLocalGridUser(m_AgentID);
            string home_uri = m_UserManager.GetUserHomeURL(m_AgentID);
  
            StringBuilder lsl = LLSDxmlEncode.Start(names.Count * 256 + 256);
            LLSDxmlEncode.AddMap(lsl);
            int ct = 0;

            if (names.Count == 0)
            {
                LLSDxmlEncode.AddEmptyArray("agents", lsl);
            }
            else
            {
                LLSDxmlEncode.AddArray("agents", lsl);

                foreach (KeyValuePair<UUID, NameInfo> kvp in names)
                {
                    if (kvp.Key == UUID.Zero)
                        continue;

                    NameInfo nameInfo = kvp.Value;
                    
                    string firstname = nameInfo.FirstName;
                    string lastname = nameInfo.LastName;
                    string displayname = nameInfo.DisplayName;
                    string username = getUserName(firstname, lastname);
                    bool is_default_name = nameInfo.IsDefault;
                    
                    if (nameInfo.IsLocal && is_local == false)
                    {
                        if (is_default_name)
                        {
                            displayname = getDefaultName(firstname, lastname);
                            is_default_name = false;
                        }
                        firstname = getUserName(firstname, lastname);
                        lastname = "@" + new Uri(m_HomeURL).Authority;
                        username = (firstname + lastname).ToLower();
                    }
                    else if (nameInfo.IsLocal && is_local)
                    {
                        //if (is_default_name)
                        //{
                        //    displayname = getDefaultName(firstname, lastname);
                        //}
                        //else
                        //{
                        //    username = getUserName(firstname, lastname);
                        //}
                    }

                    if (!nameInfo.IsLocal)
                    {
                        string[] parts = firstname.Split('.');
                        if(parts.Length == 2)
                        {
                            if(nameInfo.HomeURI == home_uri)
                            {
                                firstname = parts[0];
                                lastname = parts[1];
                                username = getUserName(firstname, lastname);
                                displayname = nameInfo.IsDefault ? getDefaultName(firstname, lastname) : nameInfo.DisplayName;
                            }
                            else
                            {
                                firstname = getUserName(parts[0], parts[1]);
                                username = (firstname + lastname).ToLower();

                                if (is_default_name)
                                {
                                    displayname = getDefaultName(parts[0], parts[1]);
									is_default_name = false;
								}
                            }
                        }
                    }

                    DateTime test = nameInfo.NameChanged.AddDays(7);
 
                    //m_log.InfoFormat("{0} {1} can change their name on {2}", nameInfo.FirstName, nameInfo.LastName, test.ToString());

                    LLSDxmlEncode.AddMap(lsl);

                    LLSDxmlEncode.AddElem("display_name_next_update", test, lsl); // fix this
                    LLSDxmlEncode.AddElem("display_name_expires", test, lsl); // fix this
                    LLSDxmlEncode.AddElem("display_name", displayname, lsl);
                    LLSDxmlEncode.AddElem("legacy_first_name", firstname, lsl);
                    LLSDxmlEncode.AddElem("legacy_last_name", lastname, lsl);
                    LLSDxmlEncode.AddElem("username", username, lsl);
                    LLSDxmlEncode.AddElem("id", kvp.Key, lsl);
                    LLSDxmlEncode.AddElem("is_display_name_default", is_default_name, lsl);
                    LLSDxmlEncode.AddEndMap(lsl);
                    ct++;
                }
                LLSDxmlEncode.AddEndArray(lsl);
            }

            LLSDxmlEncode.AddEndMap(lsl);

            httpResponse.RawBuffer = LLSDxmlEncode.EndToNBBytes(lsl);
            httpResponse.ContentType = "application/llsd+xml";
            httpResponse.StatusCode = (int)HttpStatusCode.OK;
        }
	
        public string SetDisplayNames(string request, string path,
                string param, IOSHttpRequest httpRequest,
                IOSHttpResponse httpResponse)
        {
            if (m_EventQueue == null)
                return string.Empty;
            
            if (!m_UserManager.IsLocalGridUser(m_AgentID))
            {
                m_Scene.GetScenePresence(m_AgentID).ControllingClient.SendAlertMessage("You can only set your display name on your home grid!");
                return string.Empty;
            }

            OSDMap req = (OSDMap)OSDParser.DeserializeLLSDXml(request);
            if (req.ContainsKey("display_name"))
            {
                OSDArray name = req["display_name"] as OSDArray;

                string oldName = name[0].AsString();
                string newName = name[1].AsString();

                bool resetting = string.IsNullOrWhiteSpace(newName);
                if (resetting) newName = string.Empty;

                UUID agentID = m_AgentID;

                NameInfo nameInfo = null;
                bool success = m_DisplayNames.SetDisplayName(agentID, newName, out nameInfo);

                if (success)
                {
                    if (resetting)
                    {
                        m_log.InfoFormat("[DISPLAY NAMES] {0} {1} reset their display name", nameInfo.FirstName, nameInfo.LastName);
                    }
                    else
                    {
                        m_log.InfoFormat("[DISPLAY NAMES] {0} {1} changed their display name to {2}", nameInfo.FirstName, nameInfo.LastName, nameInfo.DisplayName);
                    }

                    DateTime date = DateTime.UtcNow.AddDays(7);

                    DisplayNameUpdate(newName, oldName, nameInfo, m_AgentID, date);

                    m_Scene.ForEachClient(x => { if (x.AgentId != m_AgentID) DisplayNameUpdate(newName, oldName, nameInfo, x.AgentId, date); });

                    SetDisplayNameReply(newName, oldName, nameInfo, date);
                }
                else
                {
                    m_Scene.GetScenePresence(m_AgentID).ControllingClient.SendAlertMessage("You are unable to change your display name at this time!");
                }
            }

            return string.Empty;
        }
    }

    public class AssetUploader
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public event UpLoadedAsset OnUpLoad;
        private UpLoadedAsset handlerUpLoad = null;

        private string uploaderPath = String.Empty;
        private UUID newAssetID;
        private UUID inventoryItemID;
        private UUID parentFolder;
        private IHttpServer httpListener;
        private bool m_dumpAssetsToFile;
        private string m_assetName = String.Empty;
        private string m_assetDes = String.Empty;

        private string m_invType = String.Empty;
        private string m_assetType = String.Empty;
        private int m_cost;
        private string m_error = String.Empty;

        private Timer m_timeoutTimer = new Timer();
        private UUID m_texturesFolder;
        private int m_nreqtextures;
        private int m_nreqmeshs;
        private int m_nreqinstances;
        private bool m_IsAtestUpload;

        private int m_nextOwnerMask;
        private int m_groupMask;
        private int m_everyoneMask;
        private int[] m_meshesSides;

        public AssetUploader(string assetName, string description, UUID assetID, UUID inventoryItem,
                                UUID parentFolderID, string invType, string assetType, string path,
                                IHttpServer httpServer, bool dumpAssetsToFile,
                                int totalCost, UUID texturesFolder, int nreqtextures, int nreqmeshs, int nreqinstances,
                                bool IsAtestUpload, int nextOwnerMask, int groupMask, int everyoneMask, int[] meshesSides)
        {
            m_assetName = assetName;
            m_assetDes = description;
            newAssetID = assetID;
            inventoryItemID = inventoryItem;
            uploaderPath = path;
            httpListener = httpServer;
            parentFolder = parentFolderID;
            m_assetType = assetType;
            m_invType = invType;
            m_dumpAssetsToFile = dumpAssetsToFile;
            m_cost = totalCost;

            m_texturesFolder = texturesFolder;
            m_nreqtextures = nreqtextures;
            m_nreqmeshs = nreqmeshs;
            m_nreqinstances = nreqinstances;
            m_IsAtestUpload = IsAtestUpload;

            m_timeoutTimer.Elapsed += TimedOut;
            m_timeoutTimer.Interval = 120000;
            m_timeoutTimer.AutoReset = false;
            m_timeoutTimer.Start();

            m_nextOwnerMask = nextOwnerMask;
            m_groupMask = groupMask;
            m_everyoneMask = everyoneMask;

            m_meshesSides = meshesSides;
        }

        /// <summary>
        /// Handle raw asset upload data via the capability.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="path"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public string uploaderCaps(byte[] data, string path, string param)
        {
            UUID inv = inventoryItemID;
            string res = String.Empty;
            LLSDAssetUploadComplete uploadComplete = new LLSDAssetUploadComplete();
            /*
                        uploadComplete.new_asset = newAssetID.ToString();
                        uploadComplete.new_inventory_item = inv;
                        uploadComplete.state = "complete";

                        res = LLSDHelpers.SerialiseLLSDReply(uploadComplete);
            */
            m_timeoutTimer.Stop();
            httpListener.RemoveStreamHandler("POST", uploaderPath);

            // TODO: probably make this a better set of extensions here
            string extension = ".jp2";
            if (m_invType != "image")
            {
                extension = ".dat";
            }

            if (m_dumpAssetsToFile)
            {
                SaveAssetToFile(m_assetName + extension, data);
            }
            handlerUpLoad = OnUpLoad;
            if (handlerUpLoad != null)
            {
                handlerUpLoad(m_assetName, m_assetDes, newAssetID, inv, parentFolder, data, m_invType, m_assetType,
                    m_cost, m_texturesFolder, m_nreqtextures, m_nreqmeshs, m_nreqinstances, m_IsAtestUpload,
                    ref m_error, ref m_nextOwnerMask, ref m_groupMask, ref m_everyoneMask, m_meshesSides);
            }

            uploadComplete.new_next_owner_mask = m_nextOwnerMask;
            uploadComplete.new_group_mask = m_groupMask;
            uploadComplete.new_everyone_mask = m_everyoneMask;

            if (m_error == String.Empty)
            {
                uploadComplete.new_asset = newAssetID.ToString();
                uploadComplete.new_inventory_item = inv;
                //                if (m_texturesFolder != UUID.Zero)
                //                    uploadComplete.new_texture_folder_id = m_texturesFolder;
                if (m_IsAtestUpload)
                {
                    LLSDAssetUploadError resperror = new LLSDAssetUploadError();
                    resperror.message = "Upload SUCCESSFUL for testing purposes only. Other uses are prohibited. Item will not work after 48 hours or on other regions";
                    resperror.identifier = inv;

                    uploadComplete.error = resperror;
                }
                uploadComplete.state = "complete";
            }
            else
            {
                LLSDAssetUploadError resperror = new LLSDAssetUploadError();
                resperror.message = m_error;
                resperror.identifier = inv;

                uploadComplete.error = resperror;
                uploadComplete.state = "failed";
            }

            res = LLSDHelpers.SerialiseLLSDReply(uploadComplete);
            return res;
        }

        private void TimedOut(object sender, ElapsedEventArgs args)
        {
            m_log.InfoFormat("[CAPS]: Removing URL and handler for timed out mesh upload");
            httpListener.RemoveStreamHandler("POST", uploaderPath);
        }

        private static void SaveAssetToFile(string filename, byte[] data)
        {
            string assetPath = "UserAssets";
            if (!Directory.Exists(assetPath))
            {
                Directory.CreateDirectory(assetPath);
            }
            FileStream fs = File.Create(Path.Combine(assetPath, Util.safeFileName(filename)));
            BinaryWriter bw = new BinaryWriter(fs);
            bw.Write(data);
            bw.Close();
            fs.Close();
        }
    }
}
