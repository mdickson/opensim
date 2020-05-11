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
using Mono.Addins;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.Messages.Linden;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

using Caps = OpenSim.Framework.Capabilities.Caps;
using OSDMap = OpenMetaverse.StructuredData.OSDMap;

namespace OpenSim.Region.ClientStack.Linden
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "UploadObjectAssetModule")]
    public class UploadObjectAssetModule : INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Scene m_scene;

        #region Region Module interfaceBase Members

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void Initialise(IConfigSource source)
        {
        }

        public void AddRegion(Scene pScene)
        {
            m_scene = pScene;
        }

        public void RemoveRegion(Scene scene)
        {
            m_scene.EventManager.OnRegisterCaps -= RegisterCaps;
            m_scene = null;
        }

        public void RegionLoaded(Scene scene)
        {
            m_scene.EventManager.OnRegisterCaps += RegisterCaps;
        }

        #endregion


        #region Region Module interface

        public void Close() { }

        public string Name { get { return "UploadObjectAssetModuleModule"; } }


        public void RegisterCaps(UUID agentID, Caps caps)
        {
            caps.RegisterSimpleHandler("UploadObjectAsset",
                new SimpleOSDMapHandler("POST","/" + UUID.Random(), delegate (IOSHttpRequest httpRequest, IOSHttpResponse httpResponse, OSDMap map)
                {
                    ProcessAdd(httpRequest, httpResponse, map, agentID, caps);
                }));
            // m_log.Debug("[UPLOAD OBJECT ASSET MODULE]: /CAPS/" + capID);
        }

        #endregion


        /// <summary>
        /// Parses add request
        /// </summary>
        /// <param name="request"></param>
        /// <param name="agentID"></param>
        /// <param name="cap"></param>
        /// <returns></returns>
        public void ProcessAdd(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse, OSDMap map, UUID agentID, Caps cap)
        {
            httpResponse.KeepAlive = false;

            if (!m_scene.TryGetScenePresence(agentID, out ScenePresence avatar))
            {
                httpResponse.StatusCode = (int)HttpStatusCode.Gone;
                return;
            }

            UploadObjectAssetMessage message = new UploadObjectAssetMessage();
            try
            {
                message.Deserialize(map);

            }
            catch (Exception ex)
            {
                m_log.Error("[UPLOAD OBJECT ASSET MODULE]: Error deserializing message " + ex.ToString());
                message = null;
            }

            if (message == null)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            try
            {
                Vector3 pos = avatar.AbsolutePosition + (Vector3.UnitX * avatar.Rotation);
                Quaternion rot = Quaternion.Identity;
                Vector3 rootpos = Vector3.Zero;

                SceneObjectGroup rootGroup = null;
                SceneObjectGroup[] allparts = new SceneObjectGroup[message.Objects.Length];
                for (int i = 0; i < message.Objects.Length; i++)
                {
                    UploadObjectAssetMessage.Object obj = message.Objects[i];
                    PrimitiveBaseShape pbs = PrimitiveBaseShape.CreateBox();

                    if (i == 0)
                        rootpos = obj.Position;
 
                    for (int extparams = 0; extparams < obj.ExtraParams.Length; extparams++)
                    {
                        UploadObjectAssetMessage.Object.ExtraParam extraParam = obj.ExtraParams[extparams];
                        switch ((ushort)extraParam.Type)
                        {
                            case (ushort)ExtraParamType.Sculpt:
                                Primitive.SculptData sculpt = new Primitive.SculptData(extraParam.ExtraParamData, 0);

                                pbs.SculptEntry = true;

                                pbs.SculptTexture = obj.SculptID;
                                pbs.SculptType = (byte)sculpt.Type;

                                break;
                            case (ushort)ExtraParamType.Flexible:
                                Primitive.FlexibleData flex = new Primitive.FlexibleData(extraParam.ExtraParamData, 0);
                                pbs.FlexiEntry = true;
                                pbs.FlexiDrag = flex.Drag;
                                pbs.FlexiForceX = flex.Force.X;
                                pbs.FlexiForceY = flex.Force.Y;
                                pbs.FlexiForceZ = flex.Force.Z;
                                pbs.FlexiGravity = flex.Gravity;
                                pbs.FlexiSoftness = flex.Softness;
                                pbs.FlexiTension = flex.Tension;
                                pbs.FlexiWind = flex.Wind;
                                break;
                            case (ushort)ExtraParamType.Light:
                                Primitive.LightData light = new Primitive.LightData(extraParam.ExtraParamData, 0);
                                pbs.LightColorA = light.Color.A;
                                pbs.LightColorB = light.Color.B;
                                pbs.LightColorG = light.Color.G;
                                pbs.LightColorR = light.Color.R;
                                pbs.LightCutoff = light.Cutoff;
                                pbs.LightEntry = true;
                                pbs.LightFalloff = light.Falloff;
                                pbs.LightIntensity = light.Intensity;
                                pbs.LightRadius = light.Radius;
                                break;
                            case 0x40:
                                pbs.ReadProjectionData(extraParam.ExtraParamData, 0);
                                break;
                        }
                    }

                    pbs.PathBegin = (ushort) obj.PathBegin;
                    pbs.PathCurve = (byte) obj.PathCurve;
                    pbs.PathEnd = (ushort) obj.PathEnd;
                    pbs.PathRadiusOffset = (sbyte) obj.RadiusOffset;
                    pbs.PathRevolutions = (byte) obj.Revolutions;
                    pbs.PathScaleX = (byte) obj.ScaleX;
                    pbs.PathScaleY = (byte) obj.ScaleY;
                    pbs.PathShearX = (byte) obj.ShearX;
                    pbs.PathShearY = (byte) obj.ShearY;
                    pbs.PathSkew = (sbyte) obj.Skew;
                    pbs.PathTaperX = (sbyte) obj.TaperX;
                    pbs.PathTaperY = (sbyte) obj.TaperY;
                    pbs.PathTwist = (sbyte) obj.Twist;
                    pbs.PathTwistBegin = (sbyte) obj.TwistBegin;
                    pbs.HollowShape = (HollowShape) obj.ProfileHollow;
                    pbs.PCode = (byte) PCode.Prim;
                    pbs.ProfileBegin = (ushort) obj.ProfileBegin;
                    pbs.ProfileCurve = (byte) obj.ProfileCurve;
                    pbs.ProfileEnd = (ushort) obj.ProfileEnd;
                    pbs.Scale = obj.Scale;
                    pbs.State = (byte) 0;
                    pbs.LastAttachPoint = (byte) 0;
                    SceneObjectPart prim = new SceneObjectPart();
                    prim.UUID = UUID.Random();
                    prim.CreatorID = agentID;
                    prim.OwnerID = agentID;
                    prim.GroupID = obj.GroupID;
                    prim.LastOwnerID = prim.OwnerID;
                    prim.RezzerID = agentID;
                    prim.CreationDate = Util.UnixTimeSinceEpoch();
                    prim.Name = obj.Name;
                    prim.Description = "";

                    prim.PayPrice[0] = -2;
                    prim.PayPrice[1] = -2;
                    prim.PayPrice[2] = -2;
                    prim.PayPrice[3] = -2;
                    prim.PayPrice[4] = -2;
                    Primitive.TextureEntry tmp =
                        new Primitive.TextureEntry(UUID.Parse("89556747-24cb-43ed-920b-47caed15465f"));

                    for (int j = 0; j < obj.Faces.Length; j++)
                    {
                        UploadObjectAssetMessage.Object.Face face = obj.Faces[j];
                        Primitive.TextureEntryFace primFace = tmp.CreateFace((uint) j);

                        primFace.Bump = face.Bump;
                        primFace.RGBA = face.Color;
                        primFace.Fullbright = face.Fullbright;
                        primFace.Glow = face.Glow;
                        primFace.TextureID = face.ImageID;
                        primFace.Rotation = face.ImageRot;
                        primFace.MediaFlags = ((face.MediaFlags & 1) != 0);

                        primFace.OffsetU = face.OffsetS;
                        primFace.OffsetV = face.OffsetT;
                        primFace.RepeatU = face.ScaleS;
                        primFace.RepeatV = face.ScaleT;
                        primFace.TexMapType = (MappingType) (face.MediaFlags & 6);
                    }

                    pbs.TextureEntry = tmp.GetBytes();
                    prim.Shape = pbs;
                    prim.Scale = obj.Scale;

                    SceneObjectGroup grp = new SceneObjectGroup();

                    grp.SetRootPart(prim);
                    prim.ParentID = 0;
                    if (i == 0)

                        rootGroup = grp;

                    grp.AttachToScene(m_scene);
                    grp.AbsolutePosition = obj.Position;
                    prim.RotationOffset = obj.Rotation;

                    // Required for linking
                    grp.RootPart.ClearUpdateSchedule();

                    if (m_scene.Permissions.CanRezObject(1, avatar.UUID, pos))
                    {
                        m_scene.AddSceneObject(grp);
                        grp.AbsolutePosition = obj.Position;
                    }

                    allparts[i] = grp;
                }

                for (int j = 1; j < allparts.Length; j++)
                {
                    // Required for linking
                    rootGroup.RootPart.ClearUpdateSchedule();
                    allparts[j].RootPart.ClearUpdateSchedule();
                    rootGroup.LinkToGroup(allparts[j]);
                }

                rootGroup.ScheduleGroupForFullAnimUpdate();

                httpResponse.StatusCode = (int)HttpStatusCode.OK;
                httpResponse.RawBuffer = Util.UTF8NBGetbytes(String.Format("<llsd><map><key>local_id</key>{0}</map></llsd>", ConvertUintToBytes(allparts[0].LocalId)));
            }
            catch{ }

            httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
        }

        private string ConvertUintToBytes(uint val)
        {
            byte[] resultbytes = Utils.UIntToBytes(val);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(resultbytes);
            return String.Format("<binary encoding=\"base64\">{0}</binary>", Convert.ToBase64String(resultbytes));
        }
    }
}