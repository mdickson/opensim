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
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using log4net;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace OpenSim.Framework
{
    public enum ProfileShape : byte
    {
        Circle = 0,
        Square = 1,
        IsometricTriangle = 2,
        EquilateralTriangle = 3,
        RightTriangle = 4,
        HalfCircle = 5
    }

    public enum HollowShape : byte
    {
        Same = 0,
        Circle = 16,
        Square = 32,
        Triangle = 48
    }

    public enum PCodeEnum : byte
    {
        Primitive = 9,
        Avatar = 47,
        Grass = 95,
        NewTree = 111,
        ParticleSystem = 143,
        Tree = 255
    }

    public enum Extrusion : byte
    {
        Straight = 16,
        Curve1 = 32,
        Curve2 = 48,
        Flexible = 128
    }

    [Serializable]
    public class PrimitiveBaseShape
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly byte[] DEFAULT_TEXTURE = new Primitive.TextureEntry(new UUID("89556747-24cb-43ed-920b-47caed15465f")).GetBytes();

        private byte[] m_textureEntry;

        private ushort _pathBegin;
        private byte _pathCurve;
        private ushort _pathEnd;
        private sbyte _pathRadiusOffset;
        private byte _pathRevolutions;
        private byte _pathScaleX;
        private byte _pathScaleY;
        private byte _pathShearX;
        private byte _pathShearY;
        private sbyte _pathSkew;
        private sbyte _pathTaperX;
        private sbyte _pathTaperY;
        private sbyte _pathTwist;
        private sbyte _pathTwistBegin;
        private byte _pCode;
        private ushort _profileBegin;
        private ushort _profileEnd;
        private ushort _profileHollow;
        private Vector3 _scale;
        private byte _state;
        private byte _lastattach;
        private ProfileShape _profileShape;
        private HollowShape _hollowShape;

        // Sculpted
        [XmlIgnore] private UUID _sculptTexture;
        [XmlIgnore] private byte _sculptType;
        [XmlIgnore] private byte[] _sculptData = Utils.EmptyBytes;

        // Flexi
        [XmlIgnore] private int _flexiSoftness;
        [XmlIgnore] private float _flexiTension;
        [XmlIgnore] private float _flexiDrag;
        [XmlIgnore] private float _flexiGravity;
        [XmlIgnore] private float _flexiWind;
        [XmlIgnore] private float _flexiForceX;
        [XmlIgnore] private float _flexiForceY;
        [XmlIgnore] private float _flexiForceZ;

        //Bright n sparkly
        [XmlIgnore] private float _lightColorR;
        [XmlIgnore] private float _lightColorG;
        [XmlIgnore] private float _lightColorB;
        [XmlIgnore] private float _lightColorA = 1.0f;
        [XmlIgnore] private float _lightRadius;
        [XmlIgnore] private float _lightCutoff;
        [XmlIgnore] private float _lightFalloff;
        [XmlIgnore] private float _lightIntensity = 1.0f;


        // Light Projection Filter
        [XmlIgnore] private UUID _projectionTextureID;
        [XmlIgnore] private float _projectionFOV;
        [XmlIgnore] private float _projectionFocus;
        [XmlIgnore] private float _projectionAmb;

        [XmlIgnore] private uint _meshFlags;

        [XmlIgnore] private bool _flexiEntry;
        [XmlIgnore] private bool _lightEntry;
        [XmlIgnore] private bool _sculptEntry;
        [XmlIgnore] private bool _projectionEntry;
        [XmlIgnore] private bool _meshFlagsEntry;

        public bool MeshFlagEntry
        {
            get { return _meshFlagsEntry;}
        }
        public byte ProfileCurve
        {
            get { return (byte)((byte)HollowShape | (byte)ProfileShape); }

            set
            {
                // Handle hollow shape component
                byte hollowShapeByte = (byte)(value & 0xf0);

                if (!Enum.IsDefined(typeof(HollowShape), hollowShapeByte))
                {
                    m_log.WarnFormat(
                        "[SHAPE]: Attempt to set a ProfileCurve with a hollow shape value of {0}, which isn't a valid enum.  Replacing with default shape.",
                        hollowShapeByte);

                    this._hollowShape = HollowShape.Same;
                }
                else
                {
                    this._hollowShape = (HollowShape)hollowShapeByte;
                }

                // Handle profile shape component
                byte profileShapeByte = (byte)(value & 0xf);

                if (!Enum.IsDefined(typeof(ProfileShape), profileShapeByte))
                {
                    m_log.WarnFormat(
                        "[SHAPE]: Attempt to set a ProfileCurve with a profile shape value of {0}, which isn't a valid enum.  Replacing with square.",
                        profileShapeByte);

                    this._profileShape = ProfileShape.Square;
                }
                else
                {
                    this._profileShape = (ProfileShape)profileShapeByte;
                }
            }
        }

        /// <summary>
        /// Entries to store media textures on each face
        /// </summary>
        /// Do not change this value directly - always do it through an IMoapModule.
        /// Lock before manipulating.
        public MediaList Media { get; set; }

        public PrimitiveBaseShape()
        {
            PCode = (byte)PCodeEnum.Primitive;
            m_textureEntry = DEFAULT_TEXTURE;
        }

        /// <summary>
        /// Construct a PrimitiveBaseShape object from a OpenMetaverse.Primitive object
        /// </summary>
        /// <param name="prim"></param>
        public PrimitiveBaseShape(Primitive prim)
        {
//            m_log.DebugFormat("[PRIMITIVE BASE SHAPE]: Creating from {0}", prim.ID);

            PCode = (byte)prim.PrimData.PCode;

            State = prim.PrimData.State;
            LastAttachPoint = prim.PrimData.State;
            PathBegin = Primitive.PackBeginCut(prim.PrimData.PathBegin);
            PathEnd = Primitive.PackEndCut(prim.PrimData.PathEnd);
            PathScaleX = Primitive.PackPathScale(prim.PrimData.PathScaleX);
            PathScaleY = Primitive.PackPathScale(prim.PrimData.PathScaleY);
            PathShearX = (byte)Primitive.PackPathShear(prim.PrimData.PathShearX);
            PathShearY = (byte)Primitive.PackPathShear(prim.PrimData.PathShearY);
            PathSkew = Primitive.PackPathTwist(prim.PrimData.PathSkew);
            ProfileBegin = Primitive.PackBeginCut(prim.PrimData.ProfileBegin);
            ProfileEnd = Primitive.PackEndCut(prim.PrimData.ProfileEnd);
            Scale = prim.Scale;
            PathCurve = (byte)prim.PrimData.PathCurve;
            ProfileCurve = (byte)prim.PrimData.ProfileCurve;
            ProfileHollow = Primitive.PackProfileHollow(prim.PrimData.ProfileHollow);
            PathRadiusOffset = Primitive.PackPathTwist(prim.PrimData.PathRadiusOffset);
            PathRevolutions = Primitive.PackPathRevolutions(prim.PrimData.PathRevolutions);
            PathTaperX = Primitive.PackPathTaper(prim.PrimData.PathTaperX);
            PathTaperY = Primitive.PackPathTaper(prim.PrimData.PathTaperY);
            PathTwist = Primitive.PackPathTwist(prim.PrimData.PathTwist);
            PathTwistBegin = Primitive.PackPathTwist(prim.PrimData.PathTwistBegin);

            m_textureEntry = prim.Textures.GetBytes();

            if (prim.Sculpt != null)
            {
                SculptEntry = (prim.Sculpt.Type != OpenMetaverse.SculptType.None);
                SculptData = prim.Sculpt.GetBytes();
                SculptTexture = prim.Sculpt.SculptTexture;
                SculptType = (byte)prim.Sculpt.Type;
            }
            else
            {
                SculptType = (byte)OpenMetaverse.SculptType.None;
            }
        }

        [XmlIgnore]
        public Primitive.TextureEntry Textures
        {
            get
            {
//                m_log.DebugFormat("[SHAPE]: get m_textureEntry length {0}", m_textureEntry.Length);
                try { return new Primitive.TextureEntry(m_textureEntry, 0, m_textureEntry.Length); }
                catch { }

                m_log.Warn("[SHAPE]: Failed to decode texture, length=" + ((m_textureEntry != null) ? m_textureEntry.Length : 0));
                return new Primitive.TextureEntry(UUID.Zero);
            }

            set { m_textureEntry = value.GetBytes(); }
        }

        public byte[] TextureEntry
        {
            get { return m_textureEntry; }

            set
            {
                if (value == null)
                    m_textureEntry = new byte[1];
                else
                    m_textureEntry = value;
            }
        }

        public static PrimitiveBaseShape Default
        {
            get
            {
                PrimitiveBaseShape boxShape = CreateBox();

                boxShape.SetScale(0.5f);

                return boxShape;
            }
        }

        public static PrimitiveBaseShape Create()
        {
            PrimitiveBaseShape shape = new PrimitiveBaseShape();
            return shape;
        }

        public static PrimitiveBaseShape CreateBox()
        {
            PrimitiveBaseShape shape = Create();

            shape._pathCurve = (byte) Extrusion.Straight;
            shape._profileShape = ProfileShape.Square;
            shape._pathScaleX = 100;
            shape._pathScaleY = 100;

            return shape;
        }

        public static PrimitiveBaseShape CreateSphere()
        {
            PrimitiveBaseShape shape = Create();

            shape._pathCurve = (byte) Extrusion.Curve1;
            shape._profileShape = ProfileShape.HalfCircle;
            shape._pathScaleX = 100;
            shape._pathScaleY = 100;

            return shape;
        }

        public static PrimitiveBaseShape CreateCylinder()
        {
            PrimitiveBaseShape shape = Create();

            shape._pathCurve = (byte) Extrusion.Curve1;
            shape._profileShape = ProfileShape.Square;

            shape._pathScaleX = 100;
            shape._pathScaleY = 100;

            return shape;
        }

        public static PrimitiveBaseShape CreateMesh(int numberOfFaces, UUID meshAssetID)
        {
            PrimitiveBaseShape shape = new PrimitiveBaseShape();

            shape._pathScaleX = 100;
            shape._pathScaleY = 100;

            if(numberOfFaces <= 0) // oops ?
                numberOfFaces = 1;

            switch(numberOfFaces)
            {
                case 1: // torus 
                    shape.ProfileCurve = (byte)ProfileShape.Circle | (byte)HollowShape.Triangle;
                    shape.PathCurve = (byte)Extrusion.Curve1;
                    shape._pathScaleY = 150;
                    break;

                case 2: // torus with hollow (a sl viewer whould see 4 faces on a hollow sphere)
                    shape.ProfileCurve = (byte)ProfileShape.Circle | (byte)HollowShape.Triangle;
                    shape.PathCurve = (byte)Extrusion.Curve1;
                    shape.ProfileHollow = 27500;
                    shape._pathScaleY = 150;
                    break;

                case 3: // cylinder
                    shape.ProfileCurve = (byte)ProfileShape.Circle | (byte)HollowShape.Triangle;
                    shape.PathCurve = (byte)Extrusion.Straight;
                    break;

                case 4: // cylinder with hollow
                    shape.ProfileCurve = (byte)ProfileShape.Circle | (byte)HollowShape.Triangle;
                    shape.PathCurve = (byte)Extrusion.Straight;
                    shape.ProfileHollow = 27500;
                    break;

                case 5: // prism
                    shape.ProfileCurve = (byte)ProfileShape.EquilateralTriangle | (byte)HollowShape.Triangle;
                    shape.PathCurve = (byte)Extrusion.Straight;
                    break;

                case 6: // box
                    shape.ProfileCurve = (byte)ProfileShape.Square  | (byte)HollowShape.Triangle;
                    shape.PathCurve = (byte)Extrusion.Straight;
                    break;

                case 7: // box with hollow
                    shape.ProfileCurve = (byte)ProfileShape.Square | (byte)HollowShape.Triangle;
                    shape.PathCurve = (byte)Extrusion.Straight;
                    shape.ProfileHollow = 27500;
                    break;

                default: // 8 faces  box with cut
                    shape.ProfileCurve = (byte)ProfileShape.Square | (byte)HollowShape.Triangle;
                    shape.PathCurve = (byte)Extrusion.Straight;
                    shape.ProfileBegin = 9375;
                    break;
            }

            shape.SculptEntry = true;
            shape.SculptType = (byte)OpenMetaverse.SculptType.Mesh;
            shape.SculptTexture = meshAssetID;

            return shape;
        }

        public void SetScale(float side)
        {
            _scale = new Vector3(side, side, side);
        }

        public void SetHeigth(float height)
        {
            _scale.Z = height;
        }

        public void SetRadius(float radius)
        {
            _scale.X = _scale.Y = radius * 2f;
        }

        // TODO: void returns need to change of course
        public virtual void GetMesh()
        {
        }

        public PrimitiveBaseShape Copy()
        {
            return (PrimitiveBaseShape) MemberwiseClone();
        }

        public static PrimitiveBaseShape CreateCylinder(float radius, float heigth)
        {
            PrimitiveBaseShape shape = CreateCylinder();

            shape.SetHeigth(heigth);
            shape.SetRadius(radius);

            return shape;
        }

        public void SetPathRange(Vector3 pathRange)
        {
            _pathBegin = Primitive.PackBeginCut(pathRange.X);
            _pathEnd = Primitive.PackEndCut(pathRange.Y);
        }

        public void SetPathRange(float begin, float end)
        {
            _pathBegin = Primitive.PackBeginCut(begin);
            _pathEnd = Primitive.PackEndCut(end);
        }

        public void SetSculptProperties(byte sculptType, UUID SculptTextureUUID)
        {
            _sculptType = sculptType;
            _sculptTexture = SculptTextureUUID;
        }

        public void SetProfileRange(Vector3 profileRange)
        {
            _profileBegin = Primitive.PackBeginCut(profileRange.X);
            _profileEnd = Primitive.PackEndCut(profileRange.Y);
        }

        public void SetProfileRange(float begin, float end)
        {
            _profileBegin = Primitive.PackBeginCut(begin);
            _profileEnd = Primitive.PackEndCut(end);
        }

        public byte[] ExtraParams
        {
            get
            {
                return ExtraParamsToBytes();
            }
            set
            {
                ReadInExtraParamsBytes(value);
            }
        }

        public ushort PathBegin {
            get {
                return _pathBegin;
            }
            set {
                _pathBegin = value;
            }
        }

        public byte PathCurve {
            get {
                return _pathCurve;
            }
            set {
                _pathCurve = value;
            }
        }

        public ushort PathEnd {
            get {
                return _pathEnd;
            }
            set {
                _pathEnd = value;
            }
        }

        public sbyte PathRadiusOffset {
            get {
                return _pathRadiusOffset;
            }
            set {
                _pathRadiusOffset = value;
            }
        }

        public byte PathRevolutions {
            get {
                return _pathRevolutions;
            }
            set {
                _pathRevolutions = value;
            }
        }

        public byte PathScaleX {
            get {
                return _pathScaleX;
            }
            set {
                _pathScaleX = value;
            }
        }

        public byte PathScaleY {
            get {
                return _pathScaleY;
            }
            set {
                _pathScaleY = value;
            }
        }

        public byte PathShearX {
            get {
                return _pathShearX;
            }
            set {
                _pathShearX = value;
            }
        }

        public byte PathShearY {
            get {
                return _pathShearY;
            }
            set {
                _pathShearY = value;
            }
        }

        public sbyte PathSkew {
            get {
                return _pathSkew;
            }
            set {
                _pathSkew = value;
            }
        }

        public sbyte PathTaperX {
            get {
                return _pathTaperX;
            }
            set {
                _pathTaperX = value;
            }
        }

        public sbyte PathTaperY {
            get {
                return _pathTaperY;
            }
            set {
                _pathTaperY = value;
            }
        }

        public sbyte PathTwist {
            get {
                return _pathTwist;
            }
            set {
                _pathTwist = value;
            }
        }

        public sbyte PathTwistBegin {
            get {
                return _pathTwistBegin;
            }
            set {
                _pathTwistBegin = value;
            }
        }

        public byte PCode {
            get {
                return _pCode;
            }
            set {
                _pCode = value;
            }
        }

        public ushort ProfileBegin {
            get {
                return _profileBegin;
            }
            set {
                _profileBegin = value;
            }
        }

        public ushort ProfileEnd {
            get {
                return _profileEnd;
            }
            set {
                _profileEnd = value;
            }
        }

        public ushort ProfileHollow {
            get {
                return _profileHollow;
            }
            set {
                _profileHollow = value;
            }
        }

        public Vector3 Scale {
            get {
                return _scale;
            }
            set {
                _scale = value;
            }
        }

        public byte State {
            get {
                return _state;
            }
            set {
                _state = value;
            }
        }

        public byte LastAttachPoint {
            get {
                return _lastattach;
            }
            set {
                _lastattach = value;
            }
        }

        public ProfileShape ProfileShape {
            get {
                return _profileShape;
            }
            set {
                _profileShape = value;
            }
        }

        public HollowShape HollowShape {
            get {
                return _hollowShape;
            }
            set {
                _hollowShape = value;
            }
        }

        public UUID SculptTexture {
            get {
                return _sculptTexture;
            }
            set {
                _sculptTexture = value;
            }
        }

        public byte SculptType
        {
            get
            {
                return _sculptType;
            }
            set
            {
                _sculptType = value;
            }
        }

        // This is only used at runtime. For sculpties this holds the texture data, and for meshes
        // the mesh data.
        public byte[] SculptData
        {
            get
            {
                return _sculptData;
            }
            set
            {
//                m_log.DebugFormat("[PRIMITIVE BASE SHAPE]: Setting SculptData to data with length {0}", value.Length);
                _sculptData = value;
            }
        }

        public int FlexiSoftness
        {
            get
            {
                return _flexiSoftness;
            }
            set
            {
                _flexiSoftness = value;
            }
        }

        public float FlexiTension {
            get {
                return _flexiTension;
            }
            set {
                _flexiTension = value;
            }
        }

        public float FlexiDrag {
            get {
                return _flexiDrag;
            }
            set {
                _flexiDrag = value;
            }
        }

        public float FlexiGravity {
            get {
                return _flexiGravity;
            }
            set {
                _flexiGravity = value;
            }
        }

        public float FlexiWind {
            get {
                return _flexiWind;
            }
            set {
                _flexiWind = value;
            }
        }

        public float FlexiForceX {
            get {
                return _flexiForceX;
            }
            set {
                _flexiForceX = value;
            }
        }

        public float FlexiForceY {
            get {
                return _flexiForceY;
            }
            set {
                _flexiForceY = value;
            }
        }

        public float FlexiForceZ {
            get {
                return _flexiForceZ;
            }
            set {
                _flexiForceZ = value;
            }
        }

        public float LightColorR {
            get {
                return _lightColorR;
            }
            set {
                if (value < 0)
                    _lightColorR = 0;
                else if (value > 1.0f)
                    _lightColorR = 1.0f;
                else
                    _lightColorR = value;
            }
        }

        public float LightColorG {
            get {
                return _lightColorG;
            }
            set {
                if (value < 0)
                    _lightColorG = 0;
                else if (value > 1.0f)
                    _lightColorG = 1.0f;
                else
                    _lightColorG = value;
            }
        }

        public float LightColorB {
            get {
                return _lightColorB;
            }
            set {
                if (value < 0)
                    _lightColorB = 0;
                else if (value > 1.0f)
                    _lightColorB = 1.0f;
                else
                    _lightColorB = value;
            }
        }

        public float LightColorA {
            get {
                return _lightColorA;
            }
            set {
                if (value < 0)
                    _lightColorA = 0;
                else if (value > 1.0f)
                    _lightColorA = 1.0f;
                else
                    _lightColorA = value;
            }
        }

        public float LightRadius {
            get {
                return _lightRadius;
            }
            set {
                _lightRadius = value;
            }
        }

        public float LightCutoff {
            get {
                return _lightCutoff;
            }
            set {
                _lightCutoff = value;
            }
        }

        public float LightFalloff {
            get {
                return _lightFalloff;
            }
            set {
                _lightFalloff = value;
            }
        }

        public float LightIntensity {
            get {
                return _lightIntensity;
            }
            set {
                _lightIntensity = value;
            }
        }

        public bool FlexiEntry {
            get {
                return _flexiEntry;
            }
            set {
                _flexiEntry = value;
            }
        }

        public bool LightEntry {
            get {
                return _lightEntry;
            }
            set {
                _lightEntry = value;
            }
        }

        public bool SculptEntry {
            get {
                return _sculptEntry;
            }
            set {
                _sculptEntry = value;
            }
        }

        public bool ProjectionEntry {
            get {
                return _projectionEntry;
            }
            set {
                _projectionEntry = value;
            }
        }

        public UUID ProjectionTextureUUID {
            get {
                return _projectionTextureID;
            }
            set {
                _projectionTextureID = value;
            }
        }

        public float ProjectionFOV {
            get {
                return _projectionFOV;
            }
            set {
                _projectionFOV = value;
            }
        }

        public float ProjectionFocus {
            get {
                return _projectionFocus;
            }
            set {
                _projectionFocus = value;
            }
        }

        public float ProjectionAmbiance {
            get {
                return _projectionAmb;
            }
            set {
                _projectionAmb = value;
            }
        }

        public ulong GetMeshKey(Vector3 size, float lod)
        {
            return GetMeshKey(size, lod, false);
        }

        public ulong GetMeshKey(Vector3 size, float lod, bool convex)
        {
            ulong hash = 5381;

            hash = djb2(hash, this.PathCurve);
            hash = djb2(hash, (byte)((byte)this.HollowShape | (byte)this.ProfileShape));
            hash = djb2(hash, this.PathBegin);
            hash = djb2(hash, this.PathEnd);
            hash = djb2(hash, this.PathScaleX);
            hash = djb2(hash, this.PathScaleY);
            hash = djb2(hash, this.PathShearX);
            hash = djb2(hash, this.PathShearY);
            hash = djb2(hash, (byte)this.PathTwist);
            hash = djb2(hash, (byte)this.PathTwistBegin);
            hash = djb2(hash, (byte)this.PathRadiusOffset);
            hash = djb2(hash, (byte)this.PathTaperX);
            hash = djb2(hash, (byte)this.PathTaperY);
            hash = djb2(hash, this.PathRevolutions);
            hash = djb2(hash, (byte)this.PathSkew);
            hash = djb2(hash, this.ProfileBegin);
            hash = djb2(hash, this.ProfileEnd);
            hash = djb2(hash, this.ProfileHollow);

            // TODO: Separate scale out from the primitive shape data (after
            // scaling is supported at the physics engine level)
            byte[] scaleBytes = size.GetBytes();
            for (int i = 0; i < scaleBytes.Length; i++)
                hash = djb2(hash, scaleBytes[i]);

            // Include LOD in hash, accounting for endianness
            byte[] lodBytes = new byte[4];
            Buffer.BlockCopy(BitConverter.GetBytes(lod), 0, lodBytes, 0, 4);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(lodBytes, 0, 4);
            }
            for (int i = 0; i < lodBytes.Length; i++)
                hash = djb2(hash, lodBytes[i]);

            // include sculpt UUID
            if (this.SculptEntry)
            {
                scaleBytes = this.SculptTexture.GetBytes();
                for (int i = 0; i < scaleBytes.Length; i++)
                    hash = djb2(hash, scaleBytes[i]);
            }

            if(convex)
                hash = djb2(hash, 0xa5);

            return hash;
        }

        private ulong djb2(ulong hash, byte c)
        {
            return ((hash << 5) + hash) + (ulong)c;
        }

        private ulong djb2(ulong hash, ushort c)
        {
            hash = ((hash << 5) + hash) + (ulong)((byte)c);
            return ((hash << 5) + hash) + (ulong)(c >> 8);
        }

        public byte[] ExtraParamsToBytes()
        {
//            m_log.DebugFormat("[EXTRAPARAMS]: Called ExtraParamsToBytes()");

            const byte FlexiEP = 0x10;
            const byte LightEP = 0x20;
            const byte SculptEP = 0x30;
            const byte ProjectionEP = 0x40;
            //const byte MeshEP = 0x60;
            const byte MeshFlagsEP = 0x70;

            int TotalBytesLength = 1; // ExtraParamsNum

            uint ExtraParamsNum = 0;
            if (_flexiEntry)
            {
                ExtraParamsNum++;
                TotalBytesLength += 16 + 2 + 4;// data
            }

            if (_lightEntry)
            {
                ExtraParamsNum++;
                TotalBytesLength += 16 + 2 + 4; // data
            }

            if (_sculptEntry)
            {
                ExtraParamsNum++;
                TotalBytesLength += 17 + 2 + 4;// data
            }

            if (_projectionEntry)
            {
                ExtraParamsNum++;
                TotalBytesLength += 28 + 2 + 4; // data
            }

            if (_meshFlagsEntry)
            {
                ExtraParamsNum++;
                TotalBytesLength += 4 + 2 + 4; // data
            }
            byte[] returnBytes = new byte[TotalBytesLength];

            returnBytes[0] = (byte)ExtraParamsNum;

            if(ExtraParamsNum == 0)
                return returnBytes;

            int i = 1;

            if (_flexiEntry)
            {
                returnBytes[i] = FlexiEP; // 2 bytes id code
                i += 2;
                returnBytes[i] = 16; // 4 bytes size
                i += 4;

                // Softness is packed in the upper bits of tension and drag
                returnBytes[i] = (byte)((_flexiSoftness & 2) << 6);
                returnBytes[i + 1] = (byte)((_flexiSoftness & 1) << 7);

                returnBytes[i++] |= (byte)((byte)(_flexiTension * 10.01f) & 0x7F);
                returnBytes[i++] |= (byte)((byte)(_flexiDrag * 10.01f) & 0x7F);
                returnBytes[i++] = (byte)((_flexiGravity + 10.0f) * 10.01f);
                returnBytes[i++] = (byte)(_flexiWind * 10.01f);
                Utils.FloatToBytes(_flexiForceX, returnBytes, i);
                Utils.FloatToBytes(_flexiForceY, returnBytes, i + 4);
                Utils.FloatToBytes(_flexiForceZ, returnBytes, i + 8);
                i += 12;
            }

            if (_lightEntry)
            {
                returnBytes[i] = LightEP;
                i += 2;
                returnBytes[i] = 16;
                i += 4;

                // Alpha channel in color is intensity
                Color4 tmpColor = new Color4(_lightColorR, _lightColorG, _lightColorB, _lightIntensity);
                tmpColor.GetBytes().CopyTo(returnBytes, i);
                Utils.FloatToBytes(_lightRadius, returnBytes, i + 4);
                Utils.FloatToBytes(_lightCutoff, returnBytes, i + 8);
                Utils.FloatToBytes(_lightFalloff, returnBytes, i + 12);
                i += 16;
            }

            if (_sculptEntry)
            {
                //if(_sculptType == 5)
                //    returnBytes[i] = MeshEP;
                //else
                    returnBytes[i] = SculptEP;
                i += 2;
                returnBytes[i] = 17;
                i += 4;

                _sculptTexture.GetBytes().CopyTo(returnBytes, i);
                i += 16;
                returnBytes[i++] = _sculptType;
            }

            if (_projectionEntry)
            {
                returnBytes[i] = ProjectionEP;
                i += 2;
                returnBytes[i] = 28;
                i += 4;

                _projectionTextureID.GetBytes().CopyTo(returnBytes, i);
                Utils.FloatToBytes(_projectionFOV, returnBytes, i + 16);
                Utils.FloatToBytes(_projectionFocus, returnBytes, i + 20);
                Utils.FloatToBytes(_projectionAmb, returnBytes, i + 24);
                i += 28;
            }

            if (_meshFlagsEntry)
            {
                returnBytes[i] = MeshFlagsEP;
                i += 2;
                returnBytes[i] = 4;
                i += 4;
                Utils.UIntToBytes(_meshFlags, returnBytes, i);
            }

            return returnBytes;

        }

        public void ReadInUpdateExtraParam(ushort type, bool inUse, byte[] data)
        {
            const ushort FlexiEP = 0x10;
            const ushort LightEP = 0x20;
            const ushort SculptEP = 0x30;
            const ushort ProjectionEP = 0x40;
            const ushort MeshEP = 0x60;
            const ushort MeshFlagsEP = 0x70;

            switch (type)
            {
                case FlexiEP:
                    if (!inUse)
                    {
                        _flexiEntry = false;
                        return;
                    }
                    ReadFlexiData(data, 0);
                    break;

                case LightEP:
                    if (!inUse)
                    {
                        _lightEntry = false;
                        return;
                    }
                    ReadLightData(data, 0);
                    break;

                case MeshEP:
                case SculptEP:
                    if (!inUse)
                    {
                        _sculptEntry = false;
                        return;
                    }
                    ReadSculptData(data, 0);
                    break;
                case ProjectionEP:
                    if (!inUse)
                    {
                        _projectionEntry = false;
                        return;
                    }
                    ReadProjectionData(data, 0);
                    break;
                case MeshFlagsEP:
                    if (!inUse)
                    {
                        _meshFlagsEntry = false;
                        return;
                    }
                    ReadMeshFlagsData(data, 0);
                    break;
            }
        }

        public void ReadInExtraParamsBytes(byte[] data)
        {
            if (data == null)
                return;

            _flexiEntry = false;
            _lightEntry = false;
            _sculptEntry = false;
            _projectionEntry = false;
            _meshFlagsEntry = false;

            if (data.Length == 1)
                return;

            const byte FlexiEP = 0x10;
            const byte LightEP = 0x20;
            const byte SculptEP = 0x30;
            const byte ProjectionEP = 0x40;
            const byte MeshEP = 0x60;
            const byte MeshFlagsEP = 0x70;

            byte extraParamCount = data[0];
            int i = 1;
            for (int k = 0; k < extraParamCount; k++)
            {
                byte epType = data[i];
                i += 6;

                switch (epType)
                {
                    case FlexiEP:
                        ReadFlexiData(data, i);
                        i += 16;
                        break;

                    case LightEP:
                        ReadLightData(data, i);
                        i += 16;
                        break;

                    case MeshEP:
                    case SculptEP:
                        ReadSculptData(data, i);
                        i += 17;
                        break;

                    case ProjectionEP:
                        ReadProjectionData(data, i);
                        i += 28;
                        break;

                    case MeshFlagsEP:
                        ReadMeshFlagsData(data, i);
                        i += 4;
                        break;
                }
            }
        }

        public void ReadSculptData(byte[] data, int pos)
        {
            if (data.Length-pos >= 17)
            {
                _sculptTexture = new UUID(data, pos);
                _sculptType = data[pos + 16];
                _sculptEntry = true;
            }
            else
            {
                _sculptEntry = false;
                _sculptTexture = UUID.Zero;
                _sculptType = 0x00;
            }
        }

        public void ReadFlexiData(byte[] data, int pos)
        {
            if (data.Length-pos >= 16)
            {
                _flexiEntry = true;
                _flexiSoftness = ((data[pos] & 0x80) >> 6) | ((data[pos + 1] & 0x80) >> 7);

                _flexiTension = (float)(data[pos++] & 0x7F) / 10.0f;
                _flexiDrag = (float)(data[pos++] & 0x7F) / 10.0f;
                _flexiGravity = (float)(data[pos++] / 10.0f) - 10.0f;
                _flexiWind = (float)data[pos++] / 10.0f;
                _flexiForceX = Utils.BytesToFloat(data, pos);
                _flexiForceY = Utils.BytesToFloat(data, pos + 4);
                _flexiForceZ = Utils.BytesToFloat(data, pos + 8);
            }
            else
            {
                _flexiEntry = false;
                _flexiSoftness = 0;

                _flexiTension = 0.0f;
                _flexiDrag = 0.0f;
                _flexiGravity = 0.0f;
                _flexiWind = 0.0f;
                _flexiForceX = 0f;
                _flexiForceY = 0f;
                _flexiForceZ = 0f;
            }
        }

        public void ReadLightData(byte[] data, int pos)
        {
            if (data.Length - pos >= 16)
            {
                _lightEntry = true;
                Color4 lColor = new Color4(data, pos, false);
                _lightIntensity = lColor.A;
                _lightColorA = 1f;
                _lightColorR = lColor.R;
                _lightColorG = lColor.G;
                _lightColorB = lColor.B;

                _lightRadius = Utils.BytesToFloat(data, pos + 4);
                _lightCutoff = Utils.BytesToFloat(data, pos + 8);
                _lightFalloff = Utils.BytesToFloat(data, pos + 12);
            }
            else
            {
                _lightEntry = false;
                _lightColorA = 1f;
                _lightColorR = 0f;
                _lightColorG = 0f;
                _lightColorB = 0f;
                _lightRadius = 0f;
                _lightCutoff = 0f;
                _lightFalloff = 0f;
                _lightIntensity = 0f;
            }
        }

        public void ReadProjectionData(byte[] data, int pos)
        {
            if (data.Length - pos >= 28)
            {
                _projectionEntry = true;
                _projectionTextureID = new UUID(data, pos);
                _projectionFOV = Utils.BytesToFloat(data, pos + 16);
                _projectionFocus = Utils.BytesToFloat(data, pos + 20);
                _projectionAmb = Utils.BytesToFloat(data, pos + 24);
            }
            else
            {
                _projectionEntry = false;
                _projectionTextureID = UUID.Zero;
                _projectionFOV = 0f;
                _projectionFocus = 0f;
                _projectionAmb = 0f;
            }
        }

        public void ReadMeshFlagsData(byte[] data, int pos)
        {
            if (data.Length - pos >= 4)
            {
                _meshFlagsEntry = true;
                _meshFlags = Utils.BytesToUInt(data, pos);
            }
            else
            {
                _meshFlagsEntry = true;
                _meshFlags = 0;
            }
        }

        /// <summary>
        /// Creates a OpenMetaverse.Primitive and populates it with converted PrimitiveBaseShape values
        /// </summary>
        /// <returns></returns>
        public Primitive ToOmvPrimitive()
        {
            // position and rotation defaults here since they are not available in PrimitiveBaseShape
            return ToOmvPrimitive(new Vector3(0.0f, 0.0f, 0.0f),
                new Quaternion(0.0f, 0.0f, 0.0f, 1.0f));
        }

        /// <summary>
        /// Creates a OpenMetaverse.Primitive and populates it with converted PrimitiveBaseShape values
        /// </summary>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
        /// <returns></returns>
        public Primitive ToOmvPrimitive(Vector3 position, Quaternion rotation)
        {
            OpenMetaverse.Primitive prim = new OpenMetaverse.Primitive();

            prim.Scale = this.Scale;
            prim.Position = position;
            prim.Rotation = rotation;

            if (this.SculptEntry)
            {
                prim.Sculpt = new Primitive.SculptData();
                prim.Sculpt.Type = (OpenMetaverse.SculptType)this.SculptType;
                prim.Sculpt.SculptTexture = this.SculptTexture;
            }

            prim.PrimData.PathShearX = this.PathShearX < 128 ? (float)this.PathShearX * 0.01f : (float)(this.PathShearX - 256) * 0.01f;
            prim.PrimData.PathShearY = this.PathShearY < 128 ? (float)this.PathShearY * 0.01f : (float)(this.PathShearY - 256) * 0.01f;
            prim.PrimData.PathBegin = (float)this.PathBegin * 2.0e-5f;
            prim.PrimData.PathEnd = 1.0f - (float)this.PathEnd * 2.0e-5f;

            prim.PrimData.PathScaleX = (200 - this.PathScaleX) * 0.01f;
            prim.PrimData.PathScaleY = (200 - this.PathScaleY) * 0.01f;

            prim.PrimData.PathTaperX = this.PathTaperX * 0.01f;
            prim.PrimData.PathTaperY = this.PathTaperY * 0.01f;

            prim.PrimData.PathTwistBegin = this.PathTwistBegin * 0.01f;
            prim.PrimData.PathTwist = this.PathTwist * 0.01f;

            prim.PrimData.ProfileBegin = (float)this.ProfileBegin * 2.0e-5f;
            prim.PrimData.ProfileEnd = 1.0f - (float)this.ProfileEnd * 2.0e-5f;
            prim.PrimData.ProfileHollow = (float)this.ProfileHollow * 2.0e-5f;

            prim.PrimData.profileCurve = this.ProfileCurve;
            prim.PrimData.ProfileHole = (HoleType)this.HollowShape;

            prim.PrimData.PathCurve = (PathCurve)this.PathCurve;
            prim.PrimData.PathRadiusOffset = 0.01f * this.PathRadiusOffset;
            prim.PrimData.PathRevolutions = 1.0f + 0.015f * this.PathRevolutions;
            prim.PrimData.PathSkew = 0.01f * this.PathSkew;

            prim.PrimData.PCode = OpenMetaverse.PCode.Prim;
            prim.PrimData.State = 0;

            if (this.FlexiEntry)
            {
                prim.Flexible = new Primitive.FlexibleData();
                prim.Flexible.Drag = this.FlexiDrag;
                prim.Flexible.Force = new Vector3(this.FlexiForceX, this.FlexiForceY, this.FlexiForceZ);
                prim.Flexible.Gravity = this.FlexiGravity;
                prim.Flexible.Softness = this.FlexiSoftness;
                prim.Flexible.Tension = this.FlexiTension;
                prim.Flexible.Wind = this.FlexiWind;
            }

            if (this.LightEntry)
            {
                prim.Light = new Primitive.LightData();
                prim.Light.Color = new Color4(this.LightColorR, this.LightColorG, this.LightColorB, this.LightColorA);
                prim.Light.Cutoff = this.LightCutoff;
                prim.Light.Falloff = this.LightFalloff;
                prim.Light.Intensity = this.LightIntensity;
                prim.Light.Radius = this.LightRadius;
            }

            prim.Textures = this.Textures;

            prim.Properties = new Primitive.ObjectProperties();
            prim.Properties.Name = "Object";
            prim.Properties.Description = "";
            prim.Properties.CreatorID = UUID.Zero;
            prim.Properties.GroupID = UUID.Zero;
            prim.Properties.OwnerID = UUID.Zero;
            prim.Properties.Permissions = new Permissions();
            prim.Properties.SalePrice = 10;
            prim.Properties.SaleType = new SaleType();

            return prim;
        }

        /// <summary>
        /// Encapsulates a list of media entries.
        /// </summary>
        /// This class is necessary because we want to replace auto-serialization of MediaEntry with something more
        /// OSD like and less vulnerable to change.
        public class MediaList : List<MediaEntry>, IXmlSerializable
        {
            public const string MEDIA_TEXTURE_TYPE = "sl";

            public MediaList() : base() {}
            public MediaList(IEnumerable<MediaEntry> collection) : base(collection) {}
            public MediaList(int capacity) : base(capacity) {}

            public XmlSchema GetSchema()
            {
                return null;
            }

            public string ToXml()
            {
                lock (this)
                {
                    using (StringWriter sw = new StringWriter())
                    {
                        using (XmlTextWriter xtw = new XmlTextWriter(sw))
                        {
                            xtw.WriteStartElement("OSMedia");
                            xtw.WriteAttributeString("type", MEDIA_TEXTURE_TYPE);
                            xtw.WriteAttributeString("version", "0.1");

                            OSDArray meArray = new OSDArray();
                            foreach (MediaEntry me in this)
                            {
                                OSD osd = (null == me ? new OSD() : me.GetOSD());
                                meArray.Add(osd);
                            }

                            xtw.WriteStartElement("OSData");
                            xtw.WriteRaw(OSDParser.SerializeLLSDXmlString(meArray));
                            xtw.WriteEndElement();

                            xtw.WriteEndElement();

                            xtw.Flush();
                            return sw.ToString();
                        }
                    }
                }
            }

            public void WriteXml(XmlWriter writer)
            {
                writer.WriteRaw(ToXml());
            }

            public static MediaList FromXml(string rawXml)
            {
                MediaList ml = new MediaList();
                ml.ReadXml(rawXml);
                if(ml.Count == 0)
                    return null;
                return ml;
            }

            public void ReadXml(string rawXml)
            {
                try
                {
                    using (StringReader sr = new StringReader(rawXml))
                    {
                        using (XmlTextReader xtr = new XmlTextReader(sr))
                        {
                            xtr.MoveToContent();

                            string type = xtr.GetAttribute("type");
                            //m_log.DebugFormat("[MOAP]: Loaded media texture entry with type {0}", type);

                            if (type != MEDIA_TEXTURE_TYPE)
                                return;

                            xtr.ReadStartElement("OSMedia");
                            OSD osdp = OSDParser.DeserializeLLSDXml(xtr.ReadInnerXml());
                            if(osdp == null || !(osdp is OSDArray))
                                return;

                            OSDArray osdMeArray = osdp as OSDArray;
                            if(osdMeArray.Count == 0)
                                return;

                            foreach (OSD osdMe in osdMeArray)
                            {
                                MediaEntry me = (osdMe is OSDMap ? MediaEntry.FromOSD(osdMe) : new MediaEntry());
                                Add(me);
                            }
                        }
                    }
                }
                catch
                {
                    m_log.Debug("PrimitiveBaseShape] error decoding MOAP xml" );
                }
            }

            public void ReadXml(XmlReader reader)
            {
                if (reader.IsEmptyElement)
                    return;

                ReadXml(reader.ReadInnerXml());
            }
        }
    }
}
