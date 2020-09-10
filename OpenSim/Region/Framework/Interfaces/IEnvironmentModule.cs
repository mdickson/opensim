﻿/*
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

using OpenMetaverse;
using OpenSim.Framework;

namespace OpenSim.Region.Framework.Interfaces
{
    public interface IEnvironmentModule
    {
        void StoreOnRegion(ViewerEnvironment VEnv);
        void ResetEnvironmentSettings(UUID regionUUID);
        void FromLightShare(RegionLightShareData wl);
        RegionLightShareData ToLightShare();
        byte[] GetDefaultAssetData(int type);
        UUID GetDefaultAsset(int type);
        void WindlightRefresh(int interpolate, bool notforparcel = true);
        void WindlightRefreshForced(IScenePresence sp, int interpolate);

        ViewerEnvironment GetRegionEnvironment();

        float GetRegionDayFractionTime();
        int GetRegionDayLength();
        int GetRegionDayOffset();
        Vector3 GetRegionSunDir(float altitude);
        Quaternion GetRegionSunRot(float altitude);
        Vector3 GetRegionMoonDir(float altitude);
        Quaternion GetRegionMoonRot(float altitude);
        int GetDayLength(Vector3 pos);
        int GetDayOffset(Vector3 pos);
        Vector3 GetSunDir(Vector3 pos);
        Quaternion GetSunRot(Vector3 pos);
        Vector3 GetMoonDir(Vector3 pos);
        Quaternion GetMoonRot(Vector3 pos);
    }
}
