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
using System.Runtime.CompilerServices;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace OpenSim.Framework
{
    // legacy lightshare
    public class RegionLightShareData
    {
        public Vector3 waterColor = new Vector3(4.0f, 38.0f, 64.0f);
        public float waterFogDensityExponent = 4.0f;
        public float underwaterFogModifier = 0.25f;
        public Vector3 reflectionWaveletScale = new Vector3(2.0f, 2.0f, 2.0f);
        public float fresnelScale = 0.40f;
        public float fresnelOffset = 0.50f;
        public float refractScaleAbove = 0.03f;
        public float refractScaleBelow = 0.20f;
        public float blurMultiplier = 0.040f;
        public Vector2 bigWaveDirection = new Vector2(1.05f, -0.42f);
        public Vector2 littleWaveDirection = new Vector2(1.11f, -1.16f);
        public UUID normalMapTexture = new UUID("822ded49-9a6c-f61c-cb89-6df54f42cdf4");
        public Vector4 horizon = new Vector4(0.25f, 0.25f, 0.32f, 0.32f);
        public float hazeHorizon = 0.19f;
        public Vector4 blueDensity = new Vector4(0.12f, 0.22f, 0.38f, 0.38f);
        public float hazeDensity = 0.70f;
        public float densityMultiplier = 0.18f;
        public float distanceMultiplier = 0.8f;
        public UInt16 maxAltitude = 1605;
        public Vector4 sunMoonColor = new Vector4(0.24f, 0.26f, 0.30f, 0.30f);
        public float sunMoonPosition = 0.317f;
        public Vector4 ambient = new Vector4(0.35f, 0.35f, 0.35f, 0.35f);
        public float eastAngle = 0.0f;
        public float sunGlowFocus = 0.10f;
        public float sunGlowSize = 1.75f;
        public float sceneGamma = 1.0f;
        public float starBrightness = 0.0f;
        public Vector4 cloudColor = new Vector4(0.41f, 0.41f, 0.41f, 0.41f);
        public Vector3 cloudXYDensity = new Vector3(1.00f, 0.53f, 1.00f);
        public float cloudCoverage = 0.27f;
        public float cloudScale = 0.42f;
        public Vector3 cloudDetailXYDensity = new Vector3(1.00f, 0.53f, 0.12f);
        public float cloudScrollX = 0.20f;
        public bool cloudScrollXLock = false;
        public float cloudScrollY = 0.01f;
        public bool cloudScrollYLock = false;
        public bool drawClassicClouds = true;
    }

    public class ViewerEnvironment
    {
        DayCycle Cycle = new DayCycle();
        public int DayLength = 14400;
        public int DayOffset = 57600;
        public int Flags = 0;
 
        public float[] Altitudes = new float[3] {1000f, 2000f, 3000f };

        //DayHash;
        public bool IsLegacy = false;
        public string DayCycleName;

        public int version = 0;

        public void FromWLOSD(OSD osd)
        {
            OSDArray array = osd as OSDArray;
            if(osd != null)
            {
                Cycle = new DayCycle();
                Cycle.FromWLOSD(array);
            }

            InvalidateCaches();
        }

        public OSD ToWLOSD(UUID message, UUID region)
        {
            OSDArray array = new OSDArray(4) { null, null, null, null };
            array[0] = new OSDMap { {"messageID", message }, { "regionID", region } };
            Cycle.ToWLOSD(ref array);
            return array;
        }

        private static Quaternion AzAlToRot(float az, float al)
        {
            if (Utils.ApproxEqual(al, 0, 1e-3f) || Utils.ApproxEqual(Math.Abs(al), Utils.TWO_PI, 1e-3f))
            {
                az *= 0.5f;
                return new Quaternion(0, 0, (float)Math.Sin(az), (float)Math.Cos(az));
            }

            if (Utils.ApproxEqual(az, 0, 1e-3f) || Utils.ApproxEqual(Math.Abs(az), Utils.TWO_PI, 1e-3f))
            {
                al *= 0.5f;
                return new Quaternion(0, -(float)Math.Sin(al), 0, (float)Math.Cos(al));
            }

            az *= 0.5f;
            float sz = (float)Math.Sin(az);
            float cz = (float)Math.Cos(az);
            al *= 0.5f;
            float sl = (float)Math.Sin(al);
            float cl = (float)Math.Cos(al);

            Quaternion rot = new Quaternion(sl * sz, -sl * cz, cl * sz, cl * cz);
            rot.Normalize();
            return rot;
        }

        public static void convertFromAngles(SkyData sky, float sun_angle, float east_angle)
        {
            float az = -east_angle;
            float al = sun_angle;

            sky.sun_rotation = AzAlToRot(az, al);
            sky.moon_rotation = AzAlToRot(az, al + (float)Math.PI);
        }

        public static Vector3 Xrot(Quaternion rot)
        {
            rot.Normalize(); // just in case
            return new Vector3(2 * (rot.X * rot.X + rot.W * rot.W) - 1,
                               2 * (rot.X * rot.Y + rot.Z * rot.W),
                               2 * (rot.X * rot.Z - rot.Y * rot.W));
        }

        public static void convertToAngles(SkyData sky, out float sun_angle, out float east_angle, out Vector4 lightnorm)
        {
            Vector3 v = Xrot(sky.sun_rotation);
            v.Normalize();
            if(v.Z >= 0)
                lightnorm = new Vector4(v.Y, v.Z, v.X, 1);
            else if (v.Z > -0.12)
            {
                float m = v.Y * v.Y + v.Z * v.Z;
                m = 1/(float)Math.Sqrt(m);
                lightnorm = new Vector4(v.Y * m, 0, v.X * m, 1);
            }
            else
                lightnorm = new Vector4(-v.Y, -v.Z, -v.X, 1);

            sun_angle = (float)Math.Asin(v.Z);
            east_angle = -(float)Math.Atan2(v.Y, v.X);


            if (Math.Abs(east_angle) < 1e-6)
                east_angle = 0;
            else if (east_angle < 0)
                east_angle = Utils.TWO_PI + east_angle;

            // this is just a case on one example daycyles, as wrong as any
            /*
            if (Utils.ApproxEqual(east_angle, Utils.PI, 1e-4f))
            {
                east_angle = 0;
                sun_angle = Utils.PI - sun_angle;
            }
            */
            if (Math.Abs(sun_angle) < 1e-6)
                sun_angle = 0;
            else if (sun_angle < 0)
                sun_angle = Utils.TWO_PI + sun_angle;

        }

        public void FromLightShare(RegionLightShareData ls)
        {
            WaterData water = new WaterData();

            water.waterFogColor = ls.waterColor / 256f;
            water.waterFogDensity = (float)Math.Pow(2.0f, ls.waterFogDensityExponent);
            //water.waterFogDensity = ls.waterFogDensityExponent;
            water.underWaterFogMod = ls.underwaterFogModifier;
            water.normScale = ls.reflectionWaveletScale;
            water.fresnelScale = ls.fresnelScale;
            water.fresnelOffset = ls.fresnelOffset;
            water.scaleAbove = ls.refractScaleAbove;
            water.scaleBelow = ls.refractScaleBelow;
            water.blurMultiplier = ls.blurMultiplier;
            water.wave1Dir = ls.littleWaveDirection;
            water.wave2Dir = ls.bigWaveDirection;
            water.normalMap = ls.normalMapTexture;
            water.Name = "LightshareWater";

            SkyData sky = new SkyData();
            convertFromAngles(sky, 2.0f * (float)Math.PI * ls.sunMoonPosition, 2.0f * (float)Math.PI * ls.eastAngle);
            sky.sunlight_color = ls.sunMoonColor * 3.0f;
            sky.ambient = new Vector3(ls.ambient.X * 3.0f, ls.ambient.Y * 3.0f, ls.ambient.Z * 3.0f);
            sky.blue_horizon = new Vector3(ls.horizon.X * 2.0f, ls.horizon.Y * 2.0f, ls.horizon.Z * 2.0f);
            sky.blue_density = new Vector3(ls.blueDensity.X * 2.0f, ls.blueDensity.Y * 2.0f, ls.blueDensity.Z * 2.0f);;
            sky.haze_horizon = ls.hazeHorizon;
            sky.haze_density = ls.hazeDensity;
            sky.cloud_shadow = ls.cloudCoverage;
            sky.density_multiplier = ls.densityMultiplier / 1000.0f;
            sky.distance_multiplier = ls.distanceMultiplier;
            sky.max_y = ls.maxAltitude;
            sky.cloud_color = new Vector3(ls.cloudColor.X, ls.cloudColor.Y, ls.cloudColor.Z);
            sky.cloud_pos_density1 = ls.cloudXYDensity;
            sky.cloud_pos_density2 = ls.cloudDetailXYDensity;
            sky.cloud_scale = ls.cloudScale;
            sky.gamma=ls.sceneGamma;
            sky.glow = new Vector3((2f - ls.sunGlowSize) * 20f, 0f, -ls.sunGlowFocus * 5f);
            sky.cloud_scroll_rate = new Vector2(ls.cloudScrollX, ls.cloudScrollY);
            if (ls.cloudScrollXLock)
                sky.cloud_scroll_rate.X = 0;
            if (ls.cloudScrollYLock)
                sky.cloud_scroll_rate.Y = 0;
            sky.star_brightness = ls.starBrightness * 250f;
            sky.Name = "LightshareSky";

            Cycle = new DayCycle();
            Cycle.Name = "Lightshare";
            Cycle.waterframes.Add(water.Name, water);
            DayCycle.TrackEntry track = new DayCycle.TrackEntry(-1, water.Name);
            Cycle.waterTrack.Add(track);

            Cycle.skyframes.Add(sky.Name, sky);
            track = new DayCycle.TrackEntry(-1, sky.Name);
            Cycle.skyTrack0.Add(track);

            InvalidateCaches();
        }

        public RegionLightShareData ToLightShare()
        {
            RegionLightShareData ls = new RegionLightShareData();

            DayCycle.TrackEntry te;
            if (Cycle.waterTrack.Count > 0)
            {
                te = Cycle.waterTrack[0];
                if (Cycle.waterframes.TryGetValue(te.frameName, out WaterData water))
                {
                    ls.waterColor = water.waterFogColor * 256f;
                    ls.waterFogDensityExponent = (float)Math.Sqrt(water.waterFogDensity);
                    //ls.waterFogDensityExponent = water.waterFogDensity;
                    ls.underwaterFogModifier = water.underWaterFogMod;
                    ls.reflectionWaveletScale = water.normScale;
                    ls.fresnelScale = water.fresnelScale;
                    ls.fresnelOffset = water.fresnelOffset;
                    ls.refractScaleAbove = water.scaleAbove;
                    ls.refractScaleBelow = water.scaleBelow;
                    ls.blurMultiplier = water.blurMultiplier;
                    ls.littleWaveDirection = water.wave1Dir;
                    ls.bigWaveDirection = water.wave2Dir;
                    ls.normalMapTexture = water.normalMap;
                }
            }

            if (Cycle.skyTrack0.Count > 0)
            {
                te = Cycle.skyTrack0[0];
                if (Cycle.skyframes.TryGetValue(te.frameName, out SkyData sky))
                {
                    Vector4 lightnorm;
                    convertToAngles(sky, out ls.sunMoonPosition, out ls.eastAngle, out lightnorm);
                    ls.sunMoonPosition *= 0.5f / (float)Math.PI;
                    ls.eastAngle *= 0.5f / (float)Math.PI;
                    ls.sunMoonColor = sky.sunlight_color / 3f;
                    ls.ambient = new Vector4(sky.ambient.X / 3.0f, sky.ambient.Y / 3.0f, sky.ambient.Z / 3.0f, 1);
                    ls.horizon = new Vector4(sky.blue_horizon.X / 2.0f, sky.blue_horizon.Y / 2.0f, sky.blue_horizon.Z / 2.0f, 1);
                    ls.blueDensity = new Vector4(sky.blue_density.X / 2.0f, sky.blue_density.Y / 2.0f, sky.blue_density.Z / 2.0f, 1);
                    ls.hazeHorizon = sky.haze_horizon;
                    ls.hazeDensity = sky.haze_density;
                    ls.cloudCoverage = sky.cloud_shadow;
                    ls.densityMultiplier = 1000f * sky.density_multiplier;
                    ls.distanceMultiplier = sky.distance_multiplier;
                    ls.maxAltitude = (ushort)sky.max_y;
                    ls.cloudColor = new Vector4(sky.cloud_color.X, sky.cloud_color.Y, sky.cloud_color.Z, 1);
                    ls.cloudXYDensity = sky.cloud_pos_density1;
                    ls.cloudDetailXYDensity = sky.cloud_pos_density2;
                    ls.cloudScale = sky.cloud_scale;
                    ls.sceneGamma = sky.gamma;
                    ls.sunGlowSize = (2f - sky.glow.X) / 20f;
                    ls.sunGlowFocus = -sky.glow.Z / 5f;
                    ls.cloudScrollX = sky.cloud_scroll_rate.X;
                    ls.cloudScrollY = sky.cloud_scroll_rate.Y;
                    ls.cloudScrollXLock = ls.cloudScrollX == 0f;
                    ls.cloudScrollYLock = ls.cloudScrollY == 0f;
                    ls.starBrightness = sky.star_brightness / 250f;
                }
            }
            return ls;
        }

        public void FromOSD(OSD osd)
        {
            OSDMap map = osd as OSDMap;
            if (map == null)
                return;

            OSD otmp;

            if (map.TryGetValue("day_cycle", out otmp) && otmp is OSDMap)
            {
                Cycle = new DayCycle();
                Cycle.FromOSD(otmp as OSDMap);
            }
            if (Cycle == null)
                Cycle = new DayCycle();

            if (map.TryGetValue("day_length", out otmp))
                DayLength = otmp;
            if (map.TryGetValue("day_offset", out otmp))
                DayOffset = otmp;
            if (map.TryGetValue("flags", out otmp))
                Flags = otmp;
            if (map.TryGetValue("env_version", out otmp))
                version = otmp;
            else
                ++version;

            if (map.TryGetValue("track_altitudes", out otmp) && otmp is OSDArray)
            {
                OSDArray alt = otmp as OSDArray;

                for(int i = 0; i < alt.Count && i < 3; ++i)
                    Altitudes[i] = alt[i];

                SortAltitudes();
            }

            IsLegacy = false;
            InvalidateCaches();
        }

        public void SortAltitudes()
        {
            for (int i = 0; i < 2; ++i)
            {
                float h = Altitudes[i];
                for (int j = i + 1; j < 3; ++j)
                {
                    if (h > Altitudes[j])
                    {
                        Altitudes[i] = Altitudes[j];
                        Altitudes[j] = h;
                        List<DayCycle.TrackEntry> tet = Cycle.skyTracks[i];
                        Cycle.skyTracks[i] = Cycle.skyTracks[j];
                        Cycle.skyTracks[j] = tet;
                        h = Altitudes[i];
                    }
                }
            }
        }

        public bool CycleFromOSD(OSD osd)
        {
            OSDMap map = osd as OSDMap;
            if (map == null)
                return false;
            if (!map.TryGetValue("type", out OSD tmp))
                return false;
            string type = tmp.AsString();
            if (type != "daycycle")
                return false;
            Cycle = new DayCycle();
            Cycle.FromOSD(map);

            InvalidateCaches();

            return true;
        }

        public bool FromAssetOSD(string name, OSD osd)
        {
            OSDMap map = osd as OSDMap;
            if (map == null)
                return false;
            if (!map.TryGetValue("type", out OSD tmp))
                return false;
            string type = tmp.AsString();

            bool ok = false;
            if (type == "water")
            {
                if (Cycle == null)
                    Cycle = new DayCycle();
                ok = Cycle.replaceWaterFromOSD(name, map);
            }
            else
            {
                if (type == "daycycle")
                {
                    Cycle = new DayCycle();
                    Cycle.FromOSD(map);
                    ok = true;
                }
                else if(type == "sky")
                {
                    if (Cycle == null)
                        Cycle = new DayCycle();
                    ok = Cycle.replaceSkyFromOSD(name, map);
                }
            }
            if(ok && !string.IsNullOrWhiteSpace(name))
                Cycle.Name = name;

            InvalidateCaches();
            return ok;
        }

        public OSD ToOSD()
        {
            OSDMap env = new OSDMap();
            env["day_cycle"] = Cycle.ToOSD();
            env["day_length"] = DayLength;
            env["day_offset"] = DayOffset;
            env["flags"] = Flags;
            env["env_version"] = version;

            OSDArray alt = new OSDArray();
            alt.Add(Altitudes[0]);
            alt.Add(Altitudes[1]);
            alt.Add(Altitudes[2]);
            env["track_altitudes"] = alt;
            return env;
        }

        public readonly object m_cachedbytesLock = new object();
        public byte[] m_cachedbytes = null;
        public byte[] m_cachedWLbytes = null;

        public void InvalidateCaches()
        {
            lock (m_cachedbytesLock)
            {
                m_cachedbytes = null;
                m_cachedWLbytes = null;
            }
        }

        public byte[] ToCapBytes(UUID regionID, int parcelID)
        {
            //byte[] ret = m_cachedbytes;
            //if(ret != null)
            //    return ret;

            lock (m_cachedbytesLock)
            {
                byte[] ret = m_cachedbytes;
                if (ret == null)
                {
                    OSDMap map = new OSDMap();
                    OSDMap cenv = (OSDMap)ToOSD();
                    cenv["parcel_id"] = parcelID;
                    cenv["region_id"] = regionID;
                    map["environment"] = cenv;
                    map["parcel_id"] = parcelID;
                    map["success"] = true;
                    ret = OSDParser.SerializeLLSDXmlToBytes(map);
                    m_cachedbytes = ret;
                }

                return ret;
            }
        }

        public byte[] ToCapWLBytes(UUID messageID, UUID regionID)
        {
            //byte[] ret = m_cachedWLbytes;
            //if (ret != null)
            //    return ret;

            lock (m_cachedbytesLock)
            {
                byte[] ret = m_cachedWLbytes;
                if (ret == null)
                {
                    OSD d = ToWLOSD(messageID, regionID);
                    ret = OSDParser.SerializeLLSDXmlToBytes(d);
                    m_cachedWLbytes = ret;
                }
                return ret;
            }
        }

        public static ViewerEnvironment FromOSDString(string s)
        {
            try
            {
                OSD eosd = OSDParser.Deserialize(s);
                ViewerEnvironment VEnv = new ViewerEnvironment();
                VEnv.FromOSD(eosd);
                return VEnv;
            }
            catch
            {
            }
            return null;
        }

        public static string ToOSDString(ViewerEnvironment VEnv, bool xml = false)
        {
            try
            {
                OSD  eosd= VEnv.ToOSD();
                if(xml)
                    return OSDParser.SerializeLLSDXmlString(eosd);
                else
                    return OSDParser.SerializeLLSDNotationFull(eosd);
            }
            catch {}
            return String.Empty;
        }

        public ViewerEnvironment Clone()
        {
            // im lazy need to proper clone later
            OSD osd = ToOSD();
            ViewerEnvironment VEnv = new ViewerEnvironment();
            VEnv.FromOSD(osd);
            return VEnv;
        }

        public static OSD DefaultToOSD(UUID regionID, int parcel)
        {
            OSDMap top = new OSDMap();
            OSDMap env = new OSDMap();
            env["is_default"] = true;
            if (parcel >= 0)
                env["parcel_id"] = parcel;
            env["region_id"] = regionID;
            OSDArray alt = new OSDArray();
            alt.Add(1000f);
            alt.Add(2000f);
            alt.Add(3000f);
            env["track_altitudes"] = alt;
            top["environment"] = env;
            if (parcel >= 0)
                top["parcel_id"] = parcel;
            top["success"] = true;
            return top;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<DayCycle.TrackEntry> FindTrack(float altitude)
        {
            if (altitude < Altitudes[0])
                return Cycle.skyTrack0;

            int altindx = 1;
            for (; altindx < Altitudes.Length; ++altindx)
            {
                if (Altitudes[altindx] > altitude)
                    break;
            }

            List<DayCycle.TrackEntry> track = null;
            while (--altindx >= 0)
            {
                track = Cycle.skyTracks[altindx];
                if (track != null && track.Count > 0)
                    break;
            }
            return track;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool FindSkies(List<DayCycle.TrackEntry> track, float dayfrac, out float skyfrac, out SkyData sky1, out SkyData sky2)
        {
            sky1 = null;
            sky2 = null;
            skyfrac = dayfrac;

            if (track.Count == 1 || track[0].time < 0)
            {
                if (!Cycle.skyframes.TryGetValue(track[0].frameName, out sky1) || sky1 == null)
                    return false;
                return true;
            }

            int i = 0;
            while (i < track.Count)
            {
                if (track[i].time > dayfrac)
                    break;
                ++i;
            }

            float firstFrac;
            float secondFrac;
            string first;
            string second;

            int ntracks = track.Count;
            if (i == 0 || i == ntracks)
            {
                --ntracks;
                firstFrac = track[ntracks].time;
                first = track[ntracks].frameName;
                secondFrac = track[0].time + 1f;
                second = track[0].frameName;
            }
            else
            {
                secondFrac = track[i].time;
                second = track[i].frameName;
                --i;
                firstFrac = track[i].time;
                first = track[i].frameName;
            }

            if (!Cycle.skyframes.TryGetValue(first, out sky1) || sky1 == null)
                firstFrac = -1;
            if (!Cycle.skyframes.TryGetValue(second, out sky2) || sky2 == null)
                secondFrac = -1;

            if (firstFrac < 0)
            {
                if (secondFrac < 0)
                    return false;

                sky1 = sky2;
                sky2 = null;
                return true;
            }

            if (secondFrac < 0 || secondFrac == firstFrac)
            {
                sky2 = null;
                return true;
            }

            dayfrac -= firstFrac;
            secondFrac -= firstFrac;
            dayfrac /= secondFrac;
            skyfrac = Utils.Clamp(dayfrac, 0, 1f);
            return true;
        }

        public bool getPositions(float altitude, float dayfrac, out Vector3 sundir, out Vector3 moondir,
                out Quaternion sunrot, out Quaternion moonrot)
        {
            sundir = Vector3.Zero;
            moondir = Vector3.Zero;
            sunrot = Quaternion.Identity;
            moonrot = Quaternion.Identity;

            List<DayCycle.TrackEntry> track = FindTrack(altitude);
            if (track == null || track.Count == 0)
                return false;

            if (!FindSkies(track, dayfrac, out dayfrac, out SkyData sky1, out SkyData sky2))
                return false;

            if (sky2 == null)
            {
                moonrot = sky1.moon_rotation;
                moondir = Xrot(moonrot);
                sunrot = sky1.sun_rotation;
                sundir = Xrot(sunrot);
                return true;
            }

            moonrot = Quaternion.Slerp(sky1.moon_rotation, sky2.moon_rotation, dayfrac);
            moondir = Xrot(moonrot);

            sunrot = Quaternion.Slerp(sky1.sun_rotation, sky2.sun_rotation, dayfrac);
            sundir = Xrot(sunrot);
            return true;
        }

        /* not needed for wl viewers
        public bool getWLPositions(float altitude, float dayfrac, out Vector3 sundir)
        {
            sundir = Vector3.Zero;

            List<DayCycle.TrackEntry> track = track = FindTrack(altitude);
            if (track == null || track.Count == 0)
                return false;

            if (!FindSkies(track, dayfrac, out dayfrac, out SkyData sky1, out SkyData sky2))
                return false;

            Quaternion sunrot;
            if (sky2 == null)
            {
                sunrot = sky1.sun_rotation;
                sundir = Xrot(sunrot);
                return true;
            }

            sunrot = Quaternion.Slerp(sky1.sun_rotation, sky2.sun_rotation, dayfrac);
            sundir = Xrot(sunrot);
            return true;
        }
        */

        public void GatherAssets(Dictionary<UUID, sbyte> uuids)
        {
            if (Cycle != null)
                Cycle.GatherAssets(uuids);
        }
    }
}
