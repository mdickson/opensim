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
using System.Data;
using System.Drawing;
using System.Reflection;
using log4net;
using MySql.Data.MySqlClient;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Data.MySQL
{
    /// <summary>
    /// A MySQL Interface for the Region Server
    /// </summary>
    public class MySQLSimulationData : ISimulationDataStore
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static string LogHeader = "[REGION DB MYSQL]";

        private string m_connectionString;

        /// <summary>
        /// This lock was being used to serialize database operations when the connection was shared, but this has
        /// been unnecessary for a long time after we switched to using MySQL's underlying connection pooling instead.
        /// FIXME: However, the locks remain in many places since they are effectively providing a level of
        /// transactionality.  This should be replaced by more efficient database transactions which would not require
        /// unrelated operations to block each other or unrelated operations on the same tables from blocking each
        /// other.
        /// </summary>
        private object m_dbLock = new object();

        protected virtual Assembly Assembly
        {
            get { return GetType().Assembly; }
        }

        public MySQLSimulationData()
        {
        }

        public MySQLSimulationData(string connectionString)
        {
            Initialise(connectionString);
        }

        public virtual void Initialise(string connectionString)
        {
            m_connectionString = connectionString;

            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                // Apply new Migrations
                //
                Migration m = new Migration(dbcon, Assembly, "RegionStore");
                m.Update();
                dbcon.Close();
            }
        }

        private IDataReader ExecuteReader(MySqlCommand c)
        {
            IDataReader r = null;

            try
            {
                r = c.ExecuteReader();
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} MySQL error in ExecuteReader: {1}", LogHeader, e);
                throw;
            }

            return r;
        }

        private void ExecuteNonQuery(MySqlCommand c)
        {
            try
            {
                c.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                m_log.Error("[REGION DB]: MySQL error in ExecuteNonQuery: " + e.Message);
                throw;
            }
        }

        public void Dispose() { }

        public virtual void StoreObject(SceneObjectGroup obj, UUID regionUUID)
        {
            uint flags = obj.RootPart.GetEffectiveObjectFlags();

            // Eligibility check
            //
            if ((flags & (uint)PrimFlags.TemporaryOnRez) != 0)
                return;

            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        foreach (SceneObjectPart prim in obj.Parts)
                        {
                            cmd.Parameters.Clear();

                            cmd.CommandText = "replace into prims (" +
                                    "UUID, CreationDate, " +
                                    "Name, Text, Description, " +
                                    "SitName, TouchName, ObjectFlags, " +
                                    "OwnerMask, NextOwnerMask, GroupMask, " +
                                    "EveryoneMask, BaseMask, " +
                                    "PositionX, PositionY, PositionZ, " +
                                    "GroupPositionX, GroupPositionY, GroupPositionZ, " +
                                    "VelocityX, VelocityY, VelocityZ, " +
                                    "AngularVelocityX, AngularVelocityY, AngularVelocityZ, " +
                                    "AccelerationX, AccelerationY, AccelerationZ, " +
                                    "standtargetx, standtargety, standtargetz, " +
                                    "RotationX, RotationY, RotationZ, RotationW, " +
                                    "SitTargetOffsetX, SitTargetOffsetY, SitTargetOffsetZ, " +
                                    "SitTargetOrientW, SitTargetOrientX, " +
                                    "SitTargetOrientY, SitTargetOrientZ, " +
                                    "RegionUUID, CreatorID, " +
                                    "OwnerID, GroupID, " +
                                    "LastOwnerID, RezzerID, SceneGroupID, " +
                                    "PayPrice, PayButton1, " +
                                    "PayButton2, PayButton3, " +
                                    "PayButton4, LoopedSound, " +
                                    "LoopedSoundGain, TextureAnimation, " +
                                    "OmegaX, OmegaY, OmegaZ, " +
                                    "CameraEyeOffsetX, CameraEyeOffsetY, " +
                                    "CameraEyeOffsetZ, CameraAtOffsetX, " +
                                    "CameraAtOffsetY, CameraAtOffsetZ, " +
                                    "ForceMouselook, ScriptAccessPin, " +
                                    "AllowedDrop, DieAtEdge, " +
                                    "SalePrice, SaleType, " +
                                    "ColorR, ColorG, ColorB, ColorA, " +
                                    "ParticleSystem, ClickAction, Material, " +
                                    "CollisionSound, CollisionSoundVolume, " +
                                    "PassTouches, " +
                                    "PassCollisions, " +
                                    "LinkNumber, MediaURL, KeyframeMotion, AttachedPosX, " +
                                    "AttachedPosY, AttachedPosZ, " +
                                    "PhysicsShapeType, Density, GravityModifier, " +
                                    "Friction, Restitution, Vehicle, PhysInertia, DynAttrs, " +
                                    "RotationAxisLocks, sopanims, sitactrange, pseudocrc" + 
                                    ") values (" + "?UUID, " +
                                    "?CreationDate, ?Name, ?Text, " +
                                    "?Description, ?SitName, ?TouchName, " +
                                    "?ObjectFlags, ?OwnerMask, ?NextOwnerMask, " +
                                    "?GroupMask, ?EveryoneMask, ?BaseMask, " +
                                    "?PositionX, ?PositionY, ?PositionZ, " +
                                    "?GroupPositionX, ?GroupPositionY, ?GroupPositionZ, " +
                                    "?VelocityX, ?VelocityY, ?VelocityZ, "+
                                    "?AngularVelocityX, ?AngularVelocityY, ?AngularVelocityZ, " +
                                    "?AccelerationX, ?AccelerationY, ?AccelerationZ, " +
                                    "?standtargetx, ?standtargety, ?standtargetz, " +
                                    "?RotationX, ?RotationY, ?RotationZ, " +
                                    "?RotationW, ?SitTargetOffsetX, " +
                                    "?SitTargetOffsetY, ?SitTargetOffsetZ, " +
                                    "?SitTargetOrientW, ?SitTargetOrientX, " +
                                    "?SitTargetOrientY, ?SitTargetOrientZ, " +
                                    "?RegionUUID, ?CreatorID, ?OwnerID, " +
                                    "?GroupID, ?LastOwnerID, ?RezzerID, ?SceneGroupID, " +
                                    "?PayPrice, ?PayButton1, ?PayButton2, " +
                                    "?PayButton3, ?PayButton4, ?LoopedSound, " +
                                    "?LoopedSoundGain, ?TextureAnimation, " +
                                    "?OmegaX, ?OmegaY, ?OmegaZ, " +
                                    "?CameraEyeOffsetX, ?CameraEyeOffsetY, " +
                                    "?CameraEyeOffsetZ, ?CameraAtOffsetX, " +
                                    "?CameraAtOffsetY, ?CameraAtOffsetZ, " +
                                    "?ForceMouselook, ?ScriptAccessPin, " +
                                    "?AllowedDrop, ?DieAtEdge, ?SalePrice, " +
                                    "?SaleType, ?ColorR, ?ColorG, " +
                                    "?ColorB, ?ColorA, ?ParticleSystem, " +
                                    "?ClickAction, ?Material, ?CollisionSound, " +
                                    "?CollisionSoundVolume, ?PassTouches, ?PassCollisions, " +
                                    "?LinkNumber, ?MediaURL, ?KeyframeMotion, ?AttachedPosX, " +
                                    "?AttachedPosY, ?AttachedPosZ, " +
                                    "?PhysicsShapeType, ?Density, ?GravityModifier, " +
                                    "?Friction, ?Restitution, ?Vehicle, ?PhysInertia, ?DynAttrs," +
                                    "?RotationAxisLocks, ?sopanims, ?sitactrange, ?pseudocrc)";

                            FillPrimCommand(cmd, prim, obj.UUID, regionUUID);

                            ExecuteNonQuery(cmd);

                            cmd.Parameters.Clear();

                            cmd.CommandText = "replace into primshapes (" +
                                    "UUID, Shape, ScaleX, ScaleY, " +
                                    "ScaleZ, PCode, PathBegin, PathEnd, " +
                                    "PathScaleX, PathScaleY, PathShearX, " +
                                    "PathShearY, PathSkew, PathCurve, " +
                                    "PathRadiusOffset, PathRevolutions, " +
                                    "PathTaperX, PathTaperY, PathTwist, " +
                                    "PathTwistBegin, ProfileBegin, ProfileEnd, " +
                                    "ProfileCurve, ProfileHollow, Texture, " +
                                    "ExtraParams, State, LastAttachPoint, Media) " +
                                    "values (?UUID, " +
                                    "?Shape, ?ScaleX, ?ScaleY, ?ScaleZ, " +
                                    "?PCode, ?PathBegin, ?PathEnd, " +
                                    "?PathScaleX, ?PathScaleY, " +
                                    "?PathShearX, ?PathShearY, " +
                                    "?PathSkew, ?PathCurve, ?PathRadiusOffset, " +
                                    "?PathRevolutions, ?PathTaperX, " +
                                    "?PathTaperY, ?PathTwist, " +
                                    "?PathTwistBegin, ?ProfileBegin, " +
                                    "?ProfileEnd, ?ProfileCurve, " +
                                    "?ProfileHollow, ?Texture, ?ExtraParams, " +
                                    "?State, ?LastAttachPoint, ?Media)";

                            FillShapeCommand(cmd, prim);

                            ExecuteNonQuery(cmd);
                        }
                    }
                    dbcon.Close();
                }
            }
        }

        public virtual void RemoveObject(UUID obj, UUID regionUUID)
        {
            //            m_log.DebugFormat("[REGION DB]: Deleting scene object {0} from {1} in database", obj, regionUUID);

            List<UUID> uuids = new List<UUID>();

            // Formerly, this used to check the region UUID.
            // That makes no sense, as we remove the contents of a prim
            // unconditionally, but the prim dependent on the region ID.
            // So, we would destroy an object and cause hard to detect
            // issues if we delete the contents only. Deleting it all may
            // cause the loss of a prim, but is cleaner.
            // It's also faster because it uses the primary key.
            //
            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        cmd.CommandText = "select UUID from prims where SceneGroupID= ?UUID";
                        cmd.Parameters.AddWithValue("UUID", obj.ToString());

                        using (IDataReader reader = ExecuteReader(cmd))
                        {
                            while (reader.Read())
                                uuids.Add(DBGuid.FromDB(reader["UUID"].ToString()));
                        }

                        // delete the main prims
                        cmd.CommandText = "delete from prims where SceneGroupID= ?UUID";
                        ExecuteNonQuery(cmd);
                    }
                    dbcon.Close();
                }
            }

            // there is no way this should be < 1 unless there is
            // a very corrupt database, but in that case be extra
            // safe anyway.
            if (uuids.Count > 0)
            {
                RemoveShapes(uuids);
                RemoveItems(uuids);
            }
        }

        /// <summary>
        /// Remove all persisted items of the given prim.
        /// The caller must acquire the necessrary synchronization locks
        /// </summary>
        /// <param name="uuid">the Item UUID</param>
        private void RemoveItems(UUID uuid)
        {
            // locked by caller
            //            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        cmd.CommandText = "delete from primitems where PrimID = ?PrimID";
                        cmd.Parameters.AddWithValue("PrimID", uuid.ToString());

                        ExecuteNonQuery(cmd);
                    }
                    dbcon.Close();
                }
            }
        }

        /// <summary>
        /// Remove all persisted shapes for a list of prims
        /// The caller must acquire the necessrary synchronization locks
        /// </summary>
        /// <param name="uuids">the list of UUIDs</param>
        private void RemoveShapes(List<UUID> uuids)
        {
            lock (m_dbLock)
            {
                string sql = "delete from primshapes where ";
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        for (int i = 0; i < uuids.Count; i++)
                        {
                            if ((i + 1) == uuids.Count)
                            {// end of the list
                                sql += "(UUID = ?UUID" + i + ")";
                            }
                            else
                            {
                                sql += "(UUID = ?UUID" + i + ") or ";
                            }
                        }
                        cmd.CommandText = sql;

                        for (int i = 0; i < uuids.Count; i++)
                            cmd.Parameters.AddWithValue("UUID" + i, uuids[i].ToString());

                        ExecuteNonQuery(cmd);
                    }
                    dbcon.Close();
                }
            }
        }

        /// <summary>
        /// Remove all persisted items for a list of prims
        /// The caller must acquire the necessrary synchronization locks
        /// </summary>
        /// <param name="uuids">the list of UUIDs</param>
        private void RemoveItems(List<UUID> uuids)
        {
            lock (m_dbLock)
            {
                string sql = "delete from primitems where ";
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        for (int i = 0; i < uuids.Count; i++)
                        {
                            if ((i + 1) == uuids.Count)
                            {
                                // end of the list
                                sql += "(PrimID = ?PrimID" + i + ")";
                            }
                            else
                            {
                                sql += "(PrimID = ?PrimID" + i + ") or ";
                            }
                        }
                        cmd.CommandText = sql;

                        for (int i = 0; i < uuids.Count; i++)
                            cmd.Parameters.AddWithValue("PrimID" + i, uuids[i].ToString());

                        ExecuteNonQuery(cmd);
                    }
                    dbcon.Close();
                }
            }
        }

        public virtual List<SceneObjectGroup> LoadObjects(UUID regionID)
        {
            const int ROWS_PER_QUERY = 5000;

            Dictionary<UUID, SceneObjectPart> prims = new Dictionary<UUID, SceneObjectPart>(ROWS_PER_QUERY);
            Dictionary<UUID, SceneObjectGroup> objects = new Dictionary<UUID, SceneObjectGroup>();
            int count = 0;

            #region Prim Loading

            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        cmd.CommandText =
                            "SELECT * FROM prims LEFT JOIN primshapes ON prims.UUID = primshapes.UUID WHERE RegionUUID = ?RegionUUID";
                        cmd.Parameters.AddWithValue("RegionUUID", regionID.ToString());
                        cmd.CommandTimeout = 3600;

                        using (IDataReader reader = ExecuteReader(cmd))
                        {
                            while (reader.Read())
                            {
                                SceneObjectPart prim = BuildPrim(reader);
                                if (reader["Shape"] is DBNull)
                                    prim.Shape = PrimitiveBaseShape.Default;
                                else
                                    prim.Shape = BuildShape(reader);

                                UUID parentID = DBGuid.FromDB(reader["SceneGroupID"].ToString());
                                if (parentID != prim.UUID)
                                    prim.ParentUUID = parentID;

                                prims[prim.UUID] = prim;

                                ++count;
                                if (count % ROWS_PER_QUERY == 0)
                                    m_log.Debug("[REGION DB]: Loaded " + count + " prims...");
                            }
                        }
                    }
                    dbcon.Close();
                }
            }

            #endregion Prim Loading

            #region SceneObjectGroup Creation

            // Create all of the SOGs from the root prims first
            foreach (SceneObjectPart prim in prims.Values)
            {
                if (prim.ParentUUID == UUID.Zero)
                {
                    objects[prim.UUID] = new SceneObjectGroup(prim);
                }
            }

            // Add all of the children objects to the SOGs
            foreach (SceneObjectPart prim in prims.Values)
            {
                SceneObjectGroup sog;
                if (prim.UUID != prim.ParentUUID)
                {
                    if (objects.TryGetValue(prim.ParentUUID, out sog))
                    {
                        int originalLinkNum = prim.LinkNum;

                        sog.AddPart(prim);

                        // SceneObjectGroup.AddPart() tries to be smart and automatically set the LinkNum.
                        // We override that here
                        if (originalLinkNum != 0)
                            prim.LinkNum = originalLinkNum;
                    }
                    else
                    {
                        m_log.WarnFormat(
                            "[REGION DB]: Database contains an orphan child prim {0} {1} in region {2} pointing to missing parent {3}.  This prim will not be loaded.",
                            prim.Name, prim.UUID, regionID, prim.ParentUUID);
                    }
                }
            }

            #endregion SceneObjectGroup Creation

            m_log.DebugFormat("[REGION DB]: Loaded {0} objects using {1} prims", objects.Count, prims.Count);

            #region Prim Inventory Loading

            // Instead of attempting to LoadItems on every prim,
            // most of which probably have no items... get a
            // list from DB of all prims which have items and
            // LoadItems only on those
            List<SceneObjectPart> primsWithInventory = new List<SceneObjectPart>();
            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand itemCmd = dbcon.CreateCommand())
                    {
                        itemCmd.CommandText = "SELECT DISTINCT primID FROM primitems";
                        using (IDataReader itemReader = ExecuteReader(itemCmd))
                        {
                            while (itemReader.Read())
                            {
                                if (!(itemReader["primID"] is DBNull))
                                {
                                    UUID primID = DBGuid.FromDB(itemReader["primID"].ToString());
                                    if (prims.ContainsKey(primID))
                                        primsWithInventory.Add(prims[primID]);
                                }
                            }
                        }
                    }
                    dbcon.Close();
                }
            }

            foreach (SceneObjectPart prim in primsWithInventory)
            {
                LoadItems(prim);
            }

            #endregion Prim Inventory Loading

            m_log.DebugFormat("[REGION DB]: Loaded inventory from {0} objects", primsWithInventory.Count);

            return new List<SceneObjectGroup>(objects.Values);
        }

        /// <summary>
        /// Load in a prim's persisted inventory.
        /// </summary>
        /// <param name="prim">The prim</param>
        private void LoadItems(SceneObjectPart prim)
        {
            lock (m_dbLock)
            {
                List<TaskInventoryItem> inventory = new List<TaskInventoryItem>();

                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        cmd.CommandText = "select * from primitems where PrimID = ?PrimID";
                        cmd.Parameters.AddWithValue("PrimID", prim.UUID.ToString());

                        using (IDataReader reader = ExecuteReader(cmd))
                        {
                            while (reader.Read())
                            {
                                TaskInventoryItem item = BuildItem(reader);

                                item.ParentID = prim.UUID; // Values in database are often wrong
                                inventory.Add(item);
                            }
                        }
                    }
                    dbcon.Close();
                }

                prim.Inventory.RestoreInventoryItems(inventory);
            }
        }

        // Legacy entry point for when terrain was always a 256x256 hieghtmap
        public void StoreTerrain(double[,] ter, UUID regionID)
        {
            StoreTerrain(new TerrainData(ter), regionID);
        }

        public void StoreTerrain(TerrainData terrData, UUID regionID)
        {
            Util.FireAndForget(delegate (object x)
            {
                m_log.Info("[REGION DB]: Storing terrain");

                int terrainDBRevision;
                Array terrainDBblob;
                terrData.GetDatabaseBlob(out terrainDBRevision, out terrainDBblob);

                lock (m_dbLock)
                {
                    using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                    {
                        dbcon.Open();

                        using (MySqlCommand cmd = dbcon.CreateCommand())
                        {
                            cmd.CommandText = "delete from terrain where RegionUUID = ?RegionUUID";
                            cmd.Parameters.AddWithValue("RegionUUID", regionID.ToString());

                            using (MySqlCommand cmd2 = dbcon.CreateCommand())
                            {
                                try
                                {
                                    cmd2.CommandText = "insert into terrain (RegionUUID, " +
                                            "Revision, Heightfield) values (?RegionUUID, " +
                                            "?Revision, ?Heightfield)";

                                    cmd2.Parameters.AddWithValue("RegionUUID", regionID.ToString());
                                    cmd2.Parameters.AddWithValue("Revision", terrainDBRevision);
                                    cmd2.Parameters.AddWithValue("Heightfield", terrainDBblob);

                                    ExecuteNonQuery(cmd);
                                    ExecuteNonQuery(cmd2);
                                }
                                catch (Exception e)
                                {
                                    m_log.ErrorFormat(e.ToString());
                                }
                            }
                        }
                        dbcon.Close();
                    }
                }
            });
        }

        public void StoreBakedTerrain(TerrainData terrData, UUID regionID)
        {
            Util.FireAndForget(delegate (object x)
            {
                m_log.Info("[REGION DB]: Storing Baked terrain");

                int terrainDBRevision;
                Array terrainDBblob;
                terrData.GetDatabaseBlob(out terrainDBRevision, out terrainDBblob);

                lock (m_dbLock)
                {
                    using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                    {
                        dbcon.Open();

                        using (MySqlCommand cmd = dbcon.CreateCommand())
                        {
                            cmd.CommandText = "delete from bakedterrain where RegionUUID = ?RegionUUID";
                            cmd.Parameters.AddWithValue("RegionUUID", regionID.ToString());

                            using (MySqlCommand cmd2 = dbcon.CreateCommand())
                            {
                                try
                                {
                                    cmd2.CommandText = "insert into bakedterrain (RegionUUID, " +
                                            "Revision, Heightfield) values (?RegionUUID, " +
                                            "?Revision, ?Heightfield)";

                                    cmd2.Parameters.AddWithValue("RegionUUID", regionID.ToString());
                                    cmd2.Parameters.AddWithValue("Revision", terrainDBRevision);
                                    cmd2.Parameters.AddWithValue("Heightfield", terrainDBblob);

                                    ExecuteNonQuery(cmd);
                                    ExecuteNonQuery(cmd2);
                                }
                                catch (Exception e)
                                {
                                    m_log.ErrorFormat(e.ToString());
                                }
                            }
                        }
                        dbcon.Close();
                    }
                }
            });
        }

        // Legacy region loading
        public virtual double[,] LoadTerrain(UUID regionID)
        {
            double[,] ret = null;
            TerrainData terrData = LoadTerrain(regionID, (int)Constants.RegionSize, (int)Constants.RegionSize, (int)Constants.RegionHeight);
            if (terrData != null)
                ret = terrData.GetDoubles();
            return ret;
        }

        // Returns 'null' if region not found
        public TerrainData LoadTerrain(UUID regionID, int pSizeX, int pSizeY, int pSizeZ)
        {
            TerrainData terrData = null;
            byte[] blob = null;
            int rev = 0;

            lock (m_dbLock)
            {

                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        cmd.CommandText = "select RegionUUID, Revision, Heightfield " +
                            "from terrain where RegionUUID = ?RegionUUID " +
                            "order by Revision desc limit 1";
                        cmd.Parameters.AddWithValue("RegionUUID", regionID.ToString());

                        using (IDataReader reader = ExecuteReader(cmd))
                        {
                            while (reader.Read())
                            {
                                rev = Convert.ToInt32(reader["Revision"]);
                                if ((reader["Heightfield"] != DBNull.Value))
                                {
                                    blob = (byte[])reader["Heightfield"];
                                }
                            }
                        }
                    }
                    dbcon.Close();
                }
            }

            if (blob != null)
                terrData = TerrainData.CreateFromDatabaseBlobFactory(pSizeX, pSizeY, pSizeZ, rev, blob);

            return terrData;
        }

        public TerrainData LoadBakedTerrain(UUID regionID, int pSizeX, int pSizeY, int pSizeZ)
        {
            TerrainData terrData = null;
            byte[] blob = null;
            int rev = 0;

            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        cmd.CommandText = "select RegionUUID, Revision, Heightfield " +
                            "from bakedterrain where RegionUUID = ?RegionUUID ";
                        cmd.Parameters.AddWithValue("RegionUUID", regionID.ToString());

                        using (IDataReader reader = ExecuteReader(cmd))
                        {
                            while (reader.Read())
                            {
                                rev = Convert.ToInt32(reader["Revision"]);
                                if ((reader["Heightfield"] != DBNull.Value))
                                {
                                    blob = (byte[])reader["Heightfield"];
                                }
                            }
                        }
                    }
                    dbcon.Close();
                }
            }
            if (blob != null)
                terrData = TerrainData.CreateFromDatabaseBlobFactory(pSizeX, pSizeY, pSizeZ, rev, blob);

            return terrData;
        }

        public virtual void RemoveLandObject(UUID globalID)
        {
            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        cmd.CommandText = "delete from land where UUID = ?UUID";
                        cmd.Parameters.AddWithValue("UUID", globalID.ToString());

                        ExecuteNonQuery(cmd);
                    }
                    dbcon.Close();
                }
            }
        }

        public virtual void StoreLandObject(ILandObject parcel)
        {
            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        cmd.CommandText = "replace into land (UUID, RegionUUID, " +
                            "LocalLandID, Bitmap, Name, Description, " +
                            "OwnerUUID, IsGroupOwned, Area, AuctionID, " +
                            "Category, ClaimDate, ClaimPrice, GroupUUID, " +
                            "SalePrice, LandStatus, LandFlags, LandingType, " +
                            "MediaAutoScale, MediaTextureUUID, MediaURL, " +
                            "MusicURL, PassHours, PassPrice, SnapshotUUID, " +
                            "UserLocationX, UserLocationY, UserLocationZ, " +
                            "UserLookAtX, UserLookAtY, UserLookAtZ, " +
                            "AuthbuyerID, OtherCleanTime, Dwell, MediaType, MediaDescription, " +
                            "MediaSize, MediaLoop, ObscureMusic, ObscureMedia, " +
                            "SeeAVs, AnyAVSounds, GroupAVSounds, environment) values (" +
                            "?UUID, ?RegionUUID, " +
                            "?LocalLandID, ?Bitmap, ?Name, ?Description, " +
                            "?OwnerUUID, ?IsGroupOwned, ?Area, ?AuctionID, " +
                            "?Category, ?ClaimDate, ?ClaimPrice, ?GroupUUID, " +
                            "?SalePrice, ?LandStatus, ?LandFlags, ?LandingType, " +
                            "?MediaAutoScale, ?MediaTextureUUID, ?MediaURL, " +
                            "?MusicURL, ?PassHours, ?PassPrice, ?SnapshotUUID, " +
                            "?UserLocationX, ?UserLocationY, ?UserLocationZ, " +
                            "?UserLookAtX, ?UserLookAtY, ?UserLookAtZ, " +
                            "?AuthbuyerID, ?OtherCleanTime, ?Dwell, ?MediaType, ?MediaDescription, " +
                            "CONCAT(?MediaWidth, ',', ?MediaHeight), ?MediaLoop, ?ObscureMusic, ?ObscureMedia, " +
                            "?SeeAVs, ?AnyAVSounds, ?GroupAVSounds, ?environment)";

                        FillLandCommand(cmd, parcel.LandData, parcel.RegionUUID);

                        ExecuteNonQuery(cmd);

                        cmd.CommandText = "delete from landaccesslist where LandUUID = ?UUID";

                        ExecuteNonQuery(cmd);

                        cmd.Parameters.Clear();
                        cmd.CommandText = "insert into landaccesslist (LandUUID, " +
                                "AccessUUID, Flags, Expires) values (?LandUUID, ?AccessUUID, " +
                                "?Flags, ?Expires)";

                        foreach (LandAccessEntry entry in parcel.LandData.ParcelAccessList)
                        {
                            FillLandAccessCommand(cmd, entry, parcel.LandData.GlobalID);
                            ExecuteNonQuery(cmd);
                            cmd.Parameters.Clear();
                        }
                    }
                    dbcon.Close();
                }
            }
        }

        public virtual RegionSettings LoadRegionSettings(UUID regionUUID)
        {
            RegionSettings rs = null;
            bool needStore = false;

            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        cmd.CommandText = "select * from regionsettings where regionUUID = ?RegionUUID";
                        cmd.Parameters.AddWithValue("regionUUID", regionUUID);

                        using (IDataReader reader = ExecuteReader(cmd))
                        {
                            if (reader.Read())
                            {
                                rs = BuildRegionSettings(reader);
                                rs.OnSave += StoreRegionSettings;
                            }
                            else
                            {
                                rs = new RegionSettings();
                                rs.RegionUUID = regionUUID;
                                rs.OnSave += StoreRegionSettings;

                                needStore = true;
                            }
                        }
                    }
                    dbcon.Close();
                }
            }

            if (needStore)
                StoreRegionSettings(rs);

            LoadSpawnPoints(rs);

            return rs;
        }

        #region RegionEnvironmentSettings
        public string LoadRegionEnvironmentSettings(UUID regionUUID)
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                string command = "select * from `regionenvironment` where region_id = ?region_id";

                using (MySqlCommand cmd = new MySqlCommand(command))
                {
                    cmd.Connection = dbcon;

                    cmd.Parameters.AddWithValue("?region_id", regionUUID.ToString());

                    using (IDataReader result = ExecuteReader(cmd))
                    {
                        if (!result.Read())
                        {
                            dbcon.Close();
                            return String.Empty;
                        }
                        else
                        {
                            string ret = Convert.ToString(result["llsd_settings"]);
                            dbcon.Close();
                            return ret;
                        }
                    }
                }
            }
        }

        public void StoreRegionEnvironmentSettings(UUID regionUUID, string settings)
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = dbcon.CreateCommand())
                {
                    cmd.CommandText = "REPLACE INTO `regionenvironment` (`region_id`, `llsd_settings`) VALUES (?region_id, ?llsd_settings)";

                    cmd.Parameters.AddWithValue("region_id", regionUUID);
                    cmd.Parameters.AddWithValue("llsd_settings", settings);

                    ExecuteNonQuery(cmd);
                }
                dbcon.Close();
            }
        }

        public void RemoveRegionEnvironmentSettings(UUID regionUUID)
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = dbcon.CreateCommand())
                {
                    cmd.CommandText = "delete from `regionenvironment` where region_id = ?region_id";
                    cmd.Parameters.AddWithValue("?region_id", regionUUID.ToString());
                    ExecuteNonQuery(cmd);
                }
                dbcon.Close();
            }
        }
        #endregion

        public virtual void StoreRegionSettings(RegionSettings rs)
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = dbcon.CreateCommand())
                {
                    cmd.CommandText = "replace into regionsettings (regionUUID, " +
                         "block_terraform, block_fly, allow_damage, " +
                         "restrict_pushing, allow_land_resell, " +
                         "allow_land_join_divide, block_show_in_search, " +
                         "agent_limit, object_bonus, maturity, " +
                         "disable_scripts, disable_collisions, " +
                         "disable_physics, terrain_texture_1, " +
                         "terrain_texture_2, terrain_texture_3, " +
                         "terrain_texture_4, elevation_1_nw, " +
                         "elevation_2_nw, elevation_1_ne, " +
                         "elevation_2_ne, elevation_1_se, " +
                         "elevation_2_se, elevation_1_sw, " +
                         "elevation_2_sw, water_height, " +
                         "terrain_raise_limit, terrain_lower_limit, " +
                         "use_estate_sun, fixed_sun, sun_position, " +
                         "covenant, covenant_datetime, Sandbox, sunvectorx, sunvectory, " +
                         "sunvectorz, loaded_creation_datetime, " +
                         "loaded_creation_id, map_tile_ID, block_search, casino, " +
                         "TelehubObject, parcel_tile_ID, cacheID) " +
                          "values (?RegionUUID, ?BlockTerraform, " +
                         "?BlockFly, ?AllowDamage, ?RestrictPushing, " +
                         "?AllowLandResell, ?AllowLandJoinDivide, " +
                         "?BlockShowInSearch, ?AgentLimit, ?ObjectBonus, " +
                         "?Maturity, ?DisableScripts, ?DisableCollisions, " +
                         "?DisablePhysics, ?TerrainTexture1, " +
                         "?TerrainTexture2, ?TerrainTexture3, " +
                         "?TerrainTexture4, ?Elevation1NW, ?Elevation2NW, " +
                         "?Elevation1NE, ?Elevation2NE, ?Elevation1SE, " +
                         "?Elevation2SE, ?Elevation1SW, ?Elevation2SW, " +
                         "?WaterHeight, ?TerrainRaiseLimit, " +
                         "?TerrainLowerLimit, ?UseEstateSun, ?FixedSun, " +
                         "?SunPosition, ?Covenant, ?CovenantChangedDateTime, ?Sandbox, " +
                         "?SunVectorX, ?SunVectorY, ?SunVectorZ, " +
                         "?LoadedCreationDateTime, ?LoadedCreationID, " +
                         "?TerrainImageID, ?block_search, ?casino, " +
                         "?TelehubObject, ?ParcelImageID, ?cacheID)";

                    FillRegionSettingsCommand(cmd, rs);
                    ExecuteNonQuery(cmd);
                }
                dbcon.Close();
                SaveSpawnPoints(rs);
            }
        }

        public virtual List<LandData> LoadLandObjects(UUID regionUUID)
        {
            List<LandData> landData = new List<LandData>();

            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        cmd.CommandText = "select * from land where RegionUUID = ?RegionUUID";
                        cmd.Parameters.AddWithValue("RegionUUID", regionUUID.ToString());

                        using (IDataReader reader = ExecuteReader(cmd))
                        {
                            while (reader.Read())
                            {
                                LandData newLand = BuildLandData(reader);
                                landData.Add(newLand);
                            }
                        }
                    }

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        foreach (LandData land in landData)
                        {
                            cmd.Parameters.Clear();
                            cmd.CommandText = "select * from landaccesslist where LandUUID = ?LandUUID";
                            cmd.Parameters.AddWithValue("LandUUID", land.GlobalID.ToString());

                            using (IDataReader reader = ExecuteReader(cmd))
                            {
                                while (reader.Read())
                                {
                                    land.ParcelAccessList.Add(BuildLandAccessData(reader));
                                }
                            }
                        }
                    }
                    dbcon.Close();
                }
            }

            return landData;
        }

        public void Shutdown()
        {
        }

        private SceneObjectPart BuildPrim(IDataReader row)
        {
            SceneObjectPart prim = new SceneObjectPart();

            // depending on the MySQL connector version, CHAR(36) may be already converted to Guid!
            prim.UUID = DBGuid.FromDB(row["UUID"]);
            prim.CreatorIdentification = (string)row["CreatorID"];
            prim.OwnerID = DBGuid.FromDB(row["OwnerID"]);
            prim.GroupID = DBGuid.FromDB(row["GroupID"]);
            prim.LastOwnerID = DBGuid.FromDB(row["LastOwnerID"]);
            if (row["RezzerID"] != DBNull.Value)
                prim.RezzerID = DBGuid.FromDB(row["RezzerID"]);
            else
                prim.RezzerID = UUID.Zero;

            // explicit conversion of integers is required, which sort
            // of sucks.  No idea if there is a shortcut here or not.
            prim.CreationDate = (int)row["CreationDate"];
            if (row["Name"] != DBNull.Value)
                prim.Name = (string)row["Name"];
            else
                prim.Name = String.Empty;
            // Various text fields
            prim.Text = (string)row["Text"];
            prim.Color = Color.FromArgb((int)row["ColorA"],
                                        (int)row["ColorR"],
                                        (int)row["ColorG"],
                                        (int)row["ColorB"]);
            prim.Description = (string)row["Description"];
            prim.SitName = (string)row["SitName"];
            prim.TouchName = (string)row["TouchName"];
            // Permissions
            prim.Flags = (PrimFlags)(int)row["ObjectFlags"];
            prim.OwnerMask = (uint)(int)row["OwnerMask"];
            prim.NextOwnerMask = (uint)(int)row["NextOwnerMask"];
            prim.GroupMask = (uint)(int)row["GroupMask"];
            prim.EveryoneMask = (uint)(int)row["EveryoneMask"];
            prim.BaseMask = (uint)(int)row["BaseMask"];

            // Vectors
            prim.OffsetPosition = new Vector3(
                (float)row["PositionX"],
                (float)row["PositionY"],
                (float)row["PositionZ"]
                );
            prim.GroupPosition = new Vector3(
                (float)row["GroupPositionX"],
                (float)row["GroupPositionY"],
                (float)row["GroupPositionZ"]
                );
            prim.Velocity = new Vector3(
                (float)row["VelocityX"],
                (float)row["VelocityY"],
                (float)row["VelocityZ"]
                );
            prim.AngularVelocity = new Vector3(
                (float)row["AngularVelocityX"],
                (float)row["AngularVelocityY"],
                (float)row["AngularVelocityZ"]
                );
            prim.Acceleration = new Vector3(
                (float)row["AccelerationX"],
                (float)row["AccelerationY"],
                (float)row["AccelerationZ"]
                );
            // quaternions
            prim.RotationOffset = new Quaternion(
                (float)row["RotationX"],
                (float)row["RotationY"],
                (float)row["RotationZ"],
                (float)row["RotationW"]
                );
            prim.SitTargetPositionLL = new Vector3(
                (float)row["SitTargetOffsetX"],
                (float)row["SitTargetOffsetY"],
                (float)row["SitTargetOffsetZ"]
                );
            prim.SitTargetOrientationLL = new Quaternion(
                (float)row["SitTargetOrientX"],
                (float)row["SitTargetOrientY"],
                (float)row["SitTargetOrientZ"],
                (float)row["SitTargetOrientW"]
                );

            prim.StandOffset = new Vector3(
                (float)row["standtargetx"],
                (float)row["standtargety"],
                (float)row["standtargetz"]
                );

            prim.SitActiveRange = (float)row["sitactrange"];

            prim.PayPrice[0] = (int)row["PayPrice"];
            prim.PayPrice[1] = (int)row["PayButton1"];
            prim.PayPrice[2] = (int)row["PayButton2"];
            prim.PayPrice[3] = (int)row["PayButton3"];
            prim.PayPrice[4] = (int)row["PayButton4"];

            prim.Sound = DBGuid.FromDB(row["LoopedSound"].ToString());
            prim.SoundGain = (float)row["LoopedSoundGain"];
            if (prim.Sound != UUID.Zero)
                prim.SoundFlags = 1; // If it's persisted at all, it's looped
            else
                prim.SoundFlags = 0;

            if (!(row["TextureAnimation"] is DBNull))
                prim.TextureAnimation = (byte[])row["TextureAnimation"];
            if (!(row["ParticleSystem"] is DBNull))
                prim.ParticleSystem = (byte[])row["ParticleSystem"];

            prim.AngularVelocity = new Vector3(
                (float)row["OmegaX"],
                (float)row["OmegaY"],
                (float)row["OmegaZ"]
                );

            prim.SetCameraEyeOffset(new Vector3(
                (float)row["CameraEyeOffsetX"],
                (float)row["CameraEyeOffsetY"],
                (float)row["CameraEyeOffsetZ"]
                ));

            prim.SetCameraAtOffset(new Vector3(
                (float)row["CameraAtOffsetX"],
                (float)row["CameraAtOffsetY"],
                (float)row["CameraAtOffsetZ"]
                ));

            prim.SetForceMouselook((sbyte)row["ForceMouselook"] != 0);
            prim.ScriptAccessPin = (int)row["ScriptAccessPin"];
            prim.AllowedDrop = ((sbyte)row["AllowedDrop"] != 0);
            prim.DIE_AT_EDGE = ((sbyte)row["DieAtEdge"] != 0);

            prim.SalePrice = (int)row["SalePrice"];
            prim.ObjectSaleType = unchecked((byte)(sbyte)row["SaleType"]);

            prim.Material = unchecked((byte)(sbyte)row["Material"]);

            if (!(row["ClickAction"] is DBNull))
                prim.ClickAction = unchecked((byte)(sbyte)row["ClickAction"]);

            prim.CollisionSound = DBGuid.FromDB(row["CollisionSound"]);
            prim.CollisionSoundVolume = (float)(double)row["CollisionSoundVolume"];

            prim.PassTouches = ((sbyte)row["PassTouches"] != 0);
            prim.PassCollisions = ((sbyte)row["PassCollisions"] != 0);
            prim.LinkNum = (int)row["LinkNumber"];

            if (!(row["MediaURL"] is System.DBNull))
                prim.MediaUrl = (string)row["MediaURL"];

            if (!(row["AttachedPosX"] is System.DBNull))
            {
                prim.AttachedPos = new Vector3(
                    (float)row["AttachedPosX"],
                    (float)row["AttachedPosY"],
                    (float)row["AttachedPosZ"]
                    );
            }

            if (!(row["DynAttrs"] is System.DBNull))
                prim.DynAttrs = DAMap.FromXml((string)row["DynAttrs"]);
            else
                prim.DynAttrs = null;

            if (!(row["KeyframeMotion"] is DBNull))
            {
                Byte[] data = (byte[])row["KeyframeMotion"];
                if (data.Length > 0)
                    prim.KeyframeMotion = KeyframeMotion.FromData(null, data);
                else
                    prim.KeyframeMotion = null;
            }
            else
            {
                prim.KeyframeMotion = null;
            }

            prim.PhysicsShapeType = (byte)Convert.ToInt32(row["PhysicsShapeType"].ToString());
            prim.Density = (float)row["Density"];
            prim.GravityModifier = (float)row["GravityModifier"];
            prim.Friction = (float)row["Friction"];
            prim.Restitution = (float)row["Restitution"];
            prim.RotationAxisLocks = (byte)Convert.ToInt32(row["RotationAxisLocks"].ToString());

            SOPVehicle vehicle = null;

            if (row["Vehicle"].ToString() != String.Empty)
            {
                vehicle = SOPVehicle.FromXml2(row["Vehicle"].ToString());
                if (vehicle != null)
                    prim.VehicleParams = vehicle;
            }

            PhysicsInertiaData pdata = null;
            if (row["PhysInertia"].ToString() != String.Empty)
                pdata = PhysicsInertiaData.FromXml2(row["PhysInertia"].ToString());
            prim.PhysicsInertia = pdata;

            if (!(row["sopanims"] is DBNull))
            {
                Byte[] data = (byte[])row["sopanims"];
                if (data.Length > 0)
                    prim.DeSerializeAnimations(data);
                else
                    prim.Animations = null;
            }
            else
            {
                prim.Animations = null;
            }

            prim.SitActiveRange = (float)row["sitactrange"];

            int pseudocrc = (int)row["pseudocrc"];
            if(pseudocrc != 0)
                prim.PseudoCRC = pseudocrc;

            return prim;
        }

        /// <summary>
        /// Build a prim inventory item from the persisted data.
        /// </summary>
        /// <param name="row"></param>
        /// <returns></returns>
        private static TaskInventoryItem BuildItem(IDataReader row)
        {
            try
            {
                TaskInventoryItem taskItem = new TaskInventoryItem();

                taskItem.ItemID = DBGuid.FromDB(row["itemID"]);
                taskItem.ParentPartID = DBGuid.FromDB(row["primID"]);
                taskItem.AssetID = DBGuid.FromDB(row["assetID"]);
                taskItem.ParentID = DBGuid.FromDB(row["parentFolderID"]);

                taskItem.InvType = Convert.ToInt32(row["invType"]);
                taskItem.Type = Convert.ToInt32(row["assetType"]);

                taskItem.Name = (String)row["name"];
                taskItem.Description = (String)row["description"];
                taskItem.CreationDate = Convert.ToUInt32(row["creationDate"]);
                taskItem.CreatorIdentification = (String)row["creatorID"];
                taskItem.OwnerID = DBGuid.FromDB(row["ownerID"]);
                taskItem.LastOwnerID = DBGuid.FromDB(row["lastOwnerID"]);
                taskItem.GroupID = DBGuid.FromDB(row["groupID"]);

                taskItem.NextPermissions = Convert.ToUInt32(row["nextPermissions"]);
                taskItem.CurrentPermissions = Convert.ToUInt32(row["currentPermissions"]);
                taskItem.BasePermissions = Convert.ToUInt32(row["basePermissions"]);
                taskItem.EveryonePermissions = Convert.ToUInt32(row["everyonePermissions"]);
                taskItem.GroupPermissions = Convert.ToUInt32(row["groupPermissions"]);
                taskItem.Flags = Convert.ToUInt32(row["flags"]);

                return taskItem;
            }
            catch
            {
                m_log.ErrorFormat("[MYSQL DB]: Error reading task inventory: itemID was {0}, primID was {1}", row["itemID"].ToString(), row["primID"].ToString());
                throw;
            }
        }

        private static RegionSettings BuildRegionSettings(IDataReader row)
        {
            RegionSettings newSettings = new RegionSettings();

            newSettings.RegionUUID = DBGuid.FromDB(row["regionUUID"]);
            newSettings.BlockTerraform = Convert.ToBoolean(row["block_terraform"]);
            newSettings.AllowDamage = Convert.ToBoolean(row["allow_damage"]);
            newSettings.BlockFly = Convert.ToBoolean(row["block_fly"]);
            newSettings.RestrictPushing = Convert.ToBoolean(row["restrict_pushing"]);
            newSettings.AllowLandResell = Convert.ToBoolean(row["allow_land_resell"]);
            newSettings.AllowLandJoinDivide = Convert.ToBoolean(row["allow_land_join_divide"]);
            newSettings.BlockShowInSearch = Convert.ToBoolean(row["block_show_in_search"]);
            newSettings.AgentLimit = Convert.ToInt32(row["agent_limit"]);
            newSettings.ObjectBonus = Convert.ToDouble(row["object_bonus"]);
            newSettings.Maturity = Convert.ToInt32(row["maturity"]);
            newSettings.DisableScripts = Convert.ToBoolean(row["disable_scripts"]);
            newSettings.DisableCollisions = Convert.ToBoolean(row["disable_collisions"]);
            newSettings.DisablePhysics = Convert.ToBoolean(row["disable_physics"]);
            newSettings.TerrainTexture1 = DBGuid.FromDB(row["terrain_texture_1"]);
            newSettings.TerrainTexture2 = DBGuid.FromDB(row["terrain_texture_2"]);
            newSettings.TerrainTexture3 = DBGuid.FromDB(row["terrain_texture_3"]);
            newSettings.TerrainTexture4 = DBGuid.FromDB(row["terrain_texture_4"]);
            newSettings.Elevation1NW = Convert.ToDouble(row["elevation_1_nw"]);
            newSettings.Elevation2NW = Convert.ToDouble(row["elevation_2_nw"]);
            newSettings.Elevation1NE = Convert.ToDouble(row["elevation_1_ne"]);
            newSettings.Elevation2NE = Convert.ToDouble(row["elevation_2_ne"]);
            newSettings.Elevation1SE = Convert.ToDouble(row["elevation_1_se"]);
            newSettings.Elevation2SE = Convert.ToDouble(row["elevation_2_se"]);
            newSettings.Elevation1SW = Convert.ToDouble(row["elevation_1_sw"]);
            newSettings.Elevation2SW = Convert.ToDouble(row["elevation_2_sw"]);
            newSettings.WaterHeight = Convert.ToDouble(row["water_height"]);
            newSettings.TerrainRaiseLimit = Convert.ToDouble(row["terrain_raise_limit"]);
            newSettings.TerrainLowerLimit = Convert.ToDouble(row["terrain_lower_limit"]);
            newSettings.UseEstateSun = Convert.ToBoolean(row["use_estate_sun"]);
            newSettings.Sandbox = Convert.ToBoolean(row["Sandbox"]);
            newSettings.SunVector = new Vector3(
                                                 Convert.ToSingle(row["sunvectorx"]),
                                                 Convert.ToSingle(row["sunvectory"]),
                                                 Convert.ToSingle(row["sunvectorz"])
                                                 );
            newSettings.FixedSun = Convert.ToBoolean(row["fixed_sun"]);
            newSettings.SunPosition = Convert.ToDouble(row["sun_position"]);
            newSettings.Covenant = DBGuid.FromDB(row["covenant"]);
            newSettings.CovenantChangedDateTime = Convert.ToInt32(row["covenant_datetime"]);
            newSettings.LoadedCreationDateTime = Convert.ToInt32(row["loaded_creation_datetime"]);

            if (row["loaded_creation_id"] is DBNull)
                newSettings.LoadedCreationID = "";
            else
                newSettings.LoadedCreationID = (String)row["loaded_creation_id"];

            newSettings.TerrainImageID = DBGuid.FromDB(row["map_tile_ID"]);
            newSettings.ParcelImageID = DBGuid.FromDB(row["parcel_tile_ID"]);
            newSettings.TelehubObject = DBGuid.FromDB(row["TelehubObject"]);

            newSettings.GodBlockSearch = Convert.ToBoolean(row["block_search"]);
            newSettings.Casino = Convert.ToBoolean(row["casino"]);

            if (!(row["cacheID"] is DBNull))
                newSettings.CacheID = DBGuid.FromDB(row["cacheID"]);


            return newSettings;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="row"></param>
        /// <returns></returns>
        private static LandData BuildLandData(IDataReader row)
        {
            LandData newData = new LandData();

            newData.GlobalID = DBGuid.FromDB(row["UUID"]);
            newData.LocalID = Convert.ToInt32(row["LocalLandID"]);

            // Bitmap is a byte[512]
            newData.Bitmap = (Byte[])row["Bitmap"];

            newData.Name = (String)row["Name"];
            newData.Description = (String)row["Description"];
            newData.OwnerID = DBGuid.FromDB(row["OwnerUUID"]);
            newData.IsGroupOwned = Convert.ToBoolean(row["IsGroupOwned"]);
            newData.Area = Convert.ToInt32(row["Area"]);
            newData.AuctionID = Convert.ToUInt32(row["AuctionID"]); //Unimplemented
            newData.Category = (ParcelCategory)Convert.ToInt32(row["Category"]);
            //Enum libsecondlife.Parcel.ParcelCategory
            newData.ClaimDate = Convert.ToInt32(row["ClaimDate"]);
            newData.ClaimPrice = Convert.ToInt32(row["ClaimPrice"]);
            newData.GroupID = DBGuid.FromDB(row["GroupUUID"]);
            newData.SalePrice = Convert.ToInt32(row["SalePrice"]);
            newData.Status = (ParcelStatus)Convert.ToInt32(row["LandStatus"]);
            //Enum. libsecondlife.Parcel.ParcelStatus
            newData.Flags = Convert.ToUInt32(row["LandFlags"]);
            newData.LandingType = Convert.ToByte(row["LandingType"]);
            newData.MediaAutoScale = Convert.ToByte(row["MediaAutoScale"]);
            newData.MediaID = DBGuid.FromDB(row["MediaTextureUUID"]);
            newData.MediaURL = (String)row["MediaURL"];
            newData.MusicURL = (String)row["MusicURL"];
            newData.PassHours = Convert.ToSingle(row["PassHours"]);
            newData.PassPrice = Convert.ToInt32(row["PassPrice"]);
            UUID authedbuyer = UUID.Zero;
            UUID snapshotID = UUID.Zero;

            UUID.TryParse((string)row["AuthBuyerID"], out authedbuyer);
            UUID.TryParse((string)row["SnapshotUUID"], out snapshotID);
            newData.OtherCleanTime = Convert.ToInt32(row["OtherCleanTime"]);
            newData.Dwell = Convert.ToSingle(row["Dwell"]);

            newData.AuthBuyerID = authedbuyer;
            newData.SnapshotID = snapshotID;
            try
            {
                newData.UserLocation =
                    new Vector3(Convert.ToSingle(row["UserLocationX"]), Convert.ToSingle(row["UserLocationY"]),
                                  Convert.ToSingle(row["UserLocationZ"]));
                newData.UserLookAt =
                    new Vector3(Convert.ToSingle(row["UserLookAtX"]), Convert.ToSingle(row["UserLookAtY"]),
                                  Convert.ToSingle(row["UserLookAtZ"]));
            }
            catch (InvalidCastException)
            {
                newData.UserLocation = Vector3.Zero;
                newData.UserLookAt = Vector3.Zero;
                m_log.ErrorFormat("[PARCEL]: unable to get parcel telehub settings for {1}", newData.Name);
            }

            newData.MediaDescription = (string)row["MediaDescription"];
            newData.MediaType = (string)row["MediaType"];
            newData.MediaWidth = Convert.ToInt32((((string)row["MediaSize"]).Split(','))[0]);
            newData.MediaHeight = Convert.ToInt32((((string)row["MediaSize"]).Split(','))[1]);
            newData.MediaLoop = Convert.ToBoolean(row["MediaLoop"]);
            newData.ObscureMusic = Convert.ToBoolean(row["ObscureMusic"]);
            newData.ObscureMedia = Convert.ToBoolean(row["ObscureMedia"]);

            newData.ParcelAccessList = new List<LandAccessEntry>();

            if (!(row["SeeAVs"] is System.DBNull))
                newData.SeeAVs = Convert.ToInt32(row["SeeAVs"]) != 0 ? true : false;
            if (!(row["AnyAVSounds"] is System.DBNull))
                newData.AnyAVSounds = Convert.ToInt32(row["AnyAVSounds"]) != 0 ? true : false;
            if (!(row["GroupAVSounds"] is System.DBNull))
                newData.GroupAVSounds = Convert.ToInt32(row["GroupAVSounds"]) != 0 ? true : false;

            if (row["environment"] is DBNull)
            {
                newData.Environment = null;
                newData.EnvironmentVersion = -1;
            }
            else
            {
                string env = (string)row["environment"];
                if(string.IsNullOrEmpty(env))
                {
                    newData.Environment = null;
                    newData.EnvironmentVersion = -1;
                }
                else
                {
                    try
                    {
                        ViewerEnvironment VEnv = ViewerEnvironment.FromOSDString(env);
                        newData.Environment = VEnv;
                        newData.EnvironmentVersion = VEnv.version;
                    }
                    catch
                    {
                        newData.Environment = null;
                        newData.EnvironmentVersion = -1;
                    }
                }
            }
            return newData;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="row"></param>
        /// <returns></returns>
        private static LandAccessEntry BuildLandAccessData(IDataReader row)
        {
            LandAccessEntry entry = new LandAccessEntry();
            entry.AgentID = DBGuid.FromDB(row["AccessUUID"]);
            entry.Flags = (AccessList)Convert.ToInt32(row["Flags"]);
            entry.Expires = Convert.ToInt32(row["Expires"]);
            return entry;
        }

        /// <summary>
        /// Fill the prim command with prim values
        /// </summary>
        /// <param name="row"></param>
        /// <param name="prim"></param>
        /// <param name="sceneGroupID"></param>
        /// <param name="regionUUID"></param>
        private void FillPrimCommand(MySqlCommand cmd, SceneObjectPart prim, UUID sceneGroupID, UUID regionUUID)
        {
            cmd.Parameters.AddWithValue("UUID", prim.UUID.ToString());
            cmd.Parameters.AddWithValue("RegionUUID", regionUUID.ToString());
            cmd.Parameters.AddWithValue("CreationDate", prim.CreationDate);
            cmd.Parameters.AddWithValue("Name", prim.Name);
            cmd.Parameters.AddWithValue("SceneGroupID", sceneGroupID.ToString());
            // the UUID of the root part for this SceneObjectGroup
            // various text fields
            cmd.Parameters.AddWithValue("Text", prim.Text);
            cmd.Parameters.AddWithValue("ColorR", prim.Color.R);
            cmd.Parameters.AddWithValue("ColorG", prim.Color.G);
            cmd.Parameters.AddWithValue("ColorB", prim.Color.B);
            cmd.Parameters.AddWithValue("ColorA", prim.Color.A);
            cmd.Parameters.AddWithValue("Description", prim.Description);
            cmd.Parameters.AddWithValue("SitName", prim.SitName);
            cmd.Parameters.AddWithValue("TouchName", prim.TouchName);
            // permissions
            cmd.Parameters.AddWithValue("ObjectFlags", (uint)prim.Flags);
            cmd.Parameters.AddWithValue("CreatorID", prim.CreatorIdentification.ToString());
            cmd.Parameters.AddWithValue("OwnerID", prim.OwnerID.ToString());
            cmd.Parameters.AddWithValue("GroupID", prim.GroupID.ToString());
            cmd.Parameters.AddWithValue("LastOwnerID", prim.LastOwnerID.ToString());
            cmd.Parameters.AddWithValue("RezzerID", prim.RezzerID.ToString());
            cmd.Parameters.AddWithValue("OwnerMask", prim.OwnerMask);
            cmd.Parameters.AddWithValue("NextOwnerMask", prim.NextOwnerMask);
            cmd.Parameters.AddWithValue("GroupMask", prim.GroupMask);
            cmd.Parameters.AddWithValue("EveryoneMask", prim.EveryoneMask);
            cmd.Parameters.AddWithValue("BaseMask", prim.BaseMask);
            // vectors
            cmd.Parameters.AddWithValue("PositionX", prim.OffsetPosition.X);
            cmd.Parameters.AddWithValue("PositionY", prim.OffsetPosition.Y);
            cmd.Parameters.AddWithValue("PositionZ", prim.OffsetPosition.Z);
            cmd.Parameters.AddWithValue("GroupPositionX", prim.GroupPosition.X);
            cmd.Parameters.AddWithValue("GroupPositionY", prim.GroupPosition.Y);
            cmd.Parameters.AddWithValue("GroupPositionZ", prim.GroupPosition.Z);
            cmd.Parameters.AddWithValue("VelocityX", prim.Velocity.X);
            cmd.Parameters.AddWithValue("VelocityY", prim.Velocity.Y);
            cmd.Parameters.AddWithValue("VelocityZ", prim.Velocity.Z);
            cmd.Parameters.AddWithValue("AngularVelocityX", prim.AngularVelocity.X);
            cmd.Parameters.AddWithValue("AngularVelocityY", prim.AngularVelocity.Y);
            cmd.Parameters.AddWithValue("AngularVelocityZ", prim.AngularVelocity.Z);
            cmd.Parameters.AddWithValue("AccelerationX", prim.Acceleration.X);
            cmd.Parameters.AddWithValue("AccelerationY", prim.Acceleration.Y);
            cmd.Parameters.AddWithValue("AccelerationZ", prim.Acceleration.Z);

            cmd.Parameters.AddWithValue("standtargetx", prim.StandOffset.X);
            cmd.Parameters.AddWithValue("standtargety", prim.StandOffset.Y);
            cmd.Parameters.AddWithValue("standtargetz", prim.StandOffset.Z);

            // quaternions
            cmd.Parameters.AddWithValue("RotationX", prim.RotationOffset.X);
            cmd.Parameters.AddWithValue("RotationY", prim.RotationOffset.Y);
            cmd.Parameters.AddWithValue("RotationZ", prim.RotationOffset.Z);
            cmd.Parameters.AddWithValue("RotationW", prim.RotationOffset.W);

            // Sit target
            Vector3 sitTargetPos = prim.SitTargetPositionLL;
            cmd.Parameters.AddWithValue("SitTargetOffsetX", sitTargetPos.X);
            cmd.Parameters.AddWithValue("SitTargetOffsetY", sitTargetPos.Y);
            cmd.Parameters.AddWithValue("SitTargetOffsetZ", sitTargetPos.Z);

            Quaternion sitTargetOrient = prim.SitTargetOrientationLL;
            cmd.Parameters.AddWithValue("SitTargetOrientW", sitTargetOrient.W);
            cmd.Parameters.AddWithValue("SitTargetOrientX", sitTargetOrient.X);
            cmd.Parameters.AddWithValue("SitTargetOrientY", sitTargetOrient.Y);
            cmd.Parameters.AddWithValue("SitTargetOrientZ", sitTargetOrient.Z);

            cmd.Parameters.AddWithValue("PayPrice", prim.PayPrice[0]);
            cmd.Parameters.AddWithValue("PayButton1", prim.PayPrice[1]);
            cmd.Parameters.AddWithValue("PayButton2", prim.PayPrice[2]);
            cmd.Parameters.AddWithValue("PayButton3", prim.PayPrice[3]);
            cmd.Parameters.AddWithValue("PayButton4", prim.PayPrice[4]);

            if ((prim.SoundFlags & 1) != 0) // Looped
            {
                cmd.Parameters.AddWithValue("LoopedSound", prim.Sound.ToString());
                cmd.Parameters.AddWithValue("LoopedSoundGain", (float)prim.SoundGain);
            }
            else
            {
                cmd.Parameters.AddWithValue("LoopedSound", UUID.Zero);
                cmd.Parameters.AddWithValue("LoopedSoundGain", 0.0f);
            }

            cmd.Parameters.AddWithValue("TextureAnimation", prim.TextureAnimation);
            cmd.Parameters.AddWithValue("ParticleSystem", prim.ParticleSystem);

            cmd.Parameters.AddWithValue("OmegaX", prim.AngularVelocity.X);
            cmd.Parameters.AddWithValue("OmegaY", prim.AngularVelocity.Y);
            cmd.Parameters.AddWithValue("OmegaZ", prim.AngularVelocity.Z);

            cmd.Parameters.AddWithValue("CameraEyeOffsetX", prim.GetCameraEyeOffset().X);
            cmd.Parameters.AddWithValue("CameraEyeOffsetY", prim.GetCameraEyeOffset().Y);
            cmd.Parameters.AddWithValue("CameraEyeOffsetZ", prim.GetCameraEyeOffset().Z);

            cmd.Parameters.AddWithValue("CameraAtOffsetX", prim.GetCameraAtOffset().X);
            cmd.Parameters.AddWithValue("CameraAtOffsetY", prim.GetCameraAtOffset().Y);
            cmd.Parameters.AddWithValue("CameraAtOffsetZ", prim.GetCameraAtOffset().Z);

            if (prim.GetForceMouselook())
                cmd.Parameters.AddWithValue("ForceMouselook", 1);
            else
                cmd.Parameters.AddWithValue("ForceMouselook", 0);

            cmd.Parameters.AddWithValue("ScriptAccessPin", prim.ScriptAccessPin);

            if (prim.AllowedDrop)
                cmd.Parameters.AddWithValue("AllowedDrop", 1);
            else
                cmd.Parameters.AddWithValue("AllowedDrop", 0);

            if (prim.DIE_AT_EDGE)
                cmd.Parameters.AddWithValue("DieAtEdge", 1);
            else
                cmd.Parameters.AddWithValue("DieAtEdge", 0);

            cmd.Parameters.AddWithValue("SalePrice", prim.SalePrice);
            cmd.Parameters.AddWithValue("SaleType", unchecked((sbyte)(prim.ObjectSaleType)));

            byte clickAction = prim.ClickAction;
            cmd.Parameters.AddWithValue("ClickAction", unchecked((sbyte)(clickAction)));

            cmd.Parameters.AddWithValue("Material", unchecked((sbyte)(prim.Material)));

            cmd.Parameters.AddWithValue("CollisionSound", prim.CollisionSound.ToString());
            cmd.Parameters.AddWithValue("CollisionSoundVolume", prim.CollisionSoundVolume);

            if (prim.PassTouches)
                cmd.Parameters.AddWithValue("PassTouches", 1);
            else
                cmd.Parameters.AddWithValue("PassTouches", 0);

            if (prim.PassCollisions)
                cmd.Parameters.AddWithValue("PassCollisions", 1);
            else
                cmd.Parameters.AddWithValue("PassCollisions", 0);

            cmd.Parameters.AddWithValue("LinkNumber", prim.LinkNum);
            cmd.Parameters.AddWithValue("MediaURL", prim.MediaUrl);
            if (prim.AttachedPos != null)
            {
                cmd.Parameters.AddWithValue("AttachedPosX", prim.AttachedPos.X);
                cmd.Parameters.AddWithValue("AttachedPosY", prim.AttachedPos.Y);
                cmd.Parameters.AddWithValue("AttachedPosZ", prim.AttachedPos.Z);
            }

            if (prim.KeyframeMotion != null)
                cmd.Parameters.AddWithValue("KeyframeMotion", prim.KeyframeMotion.Serialize());
            else
                cmd.Parameters.AddWithValue("KeyframeMotion", new Byte[0]);

            if (prim.PhysicsInertia != null)
                cmd.Parameters.AddWithValue("PhysInertia", prim.PhysicsInertia.ToXml2());
            else
                cmd.Parameters.AddWithValue("PhysInertia", String.Empty);

            if (prim.VehicleParams != null)
                cmd.Parameters.AddWithValue("Vehicle", prim.VehicleParams.ToXml2());
            else
                cmd.Parameters.AddWithValue("Vehicle", String.Empty);

            if (prim.DynAttrs != null && prim.DynAttrs.CountNamespaces > 0)
                cmd.Parameters.AddWithValue("DynAttrs", prim.DynAttrs.ToXml());
            else
                cmd.Parameters.AddWithValue("DynAttrs", null);

            cmd.Parameters.AddWithValue("PhysicsShapeType", prim.PhysicsShapeType);
            cmd.Parameters.AddWithValue("Density", prim.Density);
            cmd.Parameters.AddWithValue("GravityModifier", prim.GravityModifier);
            cmd.Parameters.AddWithValue("Friction", prim.Friction);
            cmd.Parameters.AddWithValue("Restitution", prim.Restitution);
            cmd.Parameters.AddWithValue("RotationAxisLocks", prim.RotationAxisLocks);

            if (prim.Animations != null)
                cmd.Parameters.AddWithValue("sopanims", prim.SerializeAnimations());
            else
                cmd.Parameters.AddWithValue("sopanims", null);

            cmd.Parameters.AddWithValue("sitactrange", prim.SitActiveRange);
            cmd.Parameters.AddWithValue("pseudocrc", prim.PseudoCRC);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="row"></param>
        /// <param name="taskItem"></param>
        private static void FillItemCommand(MySqlCommand cmd, TaskInventoryItem taskItem)
        {
            cmd.Parameters.AddWithValue("itemID", taskItem.ItemID);
            cmd.Parameters.AddWithValue("primID", taskItem.ParentPartID);
            cmd.Parameters.AddWithValue("assetID", taskItem.AssetID);
            cmd.Parameters.AddWithValue("parentFolderID", taskItem.ParentID);

            cmd.Parameters.AddWithValue("invType", taskItem.InvType);
            cmd.Parameters.AddWithValue("assetType", taskItem.Type);

            cmd.Parameters.AddWithValue("name", taskItem.Name);
            cmd.Parameters.AddWithValue("description", taskItem.Description);
            cmd.Parameters.AddWithValue("creationDate", taskItem.CreationDate);
            cmd.Parameters.AddWithValue("creatorID", taskItem.CreatorIdentification);
            cmd.Parameters.AddWithValue("ownerID", taskItem.OwnerID);
            cmd.Parameters.AddWithValue("lastOwnerID", taskItem.LastOwnerID);
            cmd.Parameters.AddWithValue("groupID", taskItem.GroupID);
            cmd.Parameters.AddWithValue("nextPermissions", taskItem.NextPermissions);
            cmd.Parameters.AddWithValue("currentPermissions", taskItem.CurrentPermissions);
            cmd.Parameters.AddWithValue("basePermissions", taskItem.BasePermissions);
            cmd.Parameters.AddWithValue("everyonePermissions", taskItem.EveryonePermissions);
            cmd.Parameters.AddWithValue("groupPermissions", taskItem.GroupPermissions);
            cmd.Parameters.AddWithValue("flags", taskItem.Flags);
        }

        /// <summary>
        ///
        /// </summary>
        private static void FillRegionSettingsCommand(MySqlCommand cmd, RegionSettings settings)
        {
            cmd.Parameters.AddWithValue("RegionUUID", settings.RegionUUID.ToString());
            cmd.Parameters.AddWithValue("BlockTerraform", settings.BlockTerraform);
            cmd.Parameters.AddWithValue("BlockFly", settings.BlockFly);
            cmd.Parameters.AddWithValue("AllowDamage", settings.AllowDamage);
            cmd.Parameters.AddWithValue("RestrictPushing", settings.RestrictPushing);
            cmd.Parameters.AddWithValue("AllowLandResell", settings.AllowLandResell);
            cmd.Parameters.AddWithValue("AllowLandJoinDivide", settings.AllowLandJoinDivide);
            cmd.Parameters.AddWithValue("BlockShowInSearch", settings.BlockShowInSearch);
            cmd.Parameters.AddWithValue("AgentLimit", settings.AgentLimit);
            cmd.Parameters.AddWithValue("ObjectBonus", settings.ObjectBonus);
            cmd.Parameters.AddWithValue("Maturity", settings.Maturity);
            cmd.Parameters.AddWithValue("DisableScripts", settings.DisableScripts);
            cmd.Parameters.AddWithValue("DisableCollisions", settings.DisableCollisions);
            cmd.Parameters.AddWithValue("DisablePhysics", settings.DisablePhysics);
            cmd.Parameters.AddWithValue("TerrainTexture1", settings.TerrainTexture1.ToString());
            cmd.Parameters.AddWithValue("TerrainTexture2", settings.TerrainTexture2.ToString());
            cmd.Parameters.AddWithValue("TerrainTexture3", settings.TerrainTexture3.ToString());
            cmd.Parameters.AddWithValue("TerrainTexture4", settings.TerrainTexture4.ToString());
            cmd.Parameters.AddWithValue("Elevation1NW", settings.Elevation1NW);
            cmd.Parameters.AddWithValue("Elevation2NW", settings.Elevation2NW);
            cmd.Parameters.AddWithValue("Elevation1NE", settings.Elevation1NE);
            cmd.Parameters.AddWithValue("Elevation2NE", settings.Elevation2NE);
            cmd.Parameters.AddWithValue("Elevation1SE", settings.Elevation1SE);
            cmd.Parameters.AddWithValue("Elevation2SE", settings.Elevation2SE);
            cmd.Parameters.AddWithValue("Elevation1SW", settings.Elevation1SW);
            cmd.Parameters.AddWithValue("Elevation2SW", settings.Elevation2SW);
            cmd.Parameters.AddWithValue("WaterHeight", settings.WaterHeight);
            cmd.Parameters.AddWithValue("TerrainRaiseLimit", settings.TerrainRaiseLimit);
            cmd.Parameters.AddWithValue("TerrainLowerLimit", settings.TerrainLowerLimit);
            cmd.Parameters.AddWithValue("UseEstateSun", settings.UseEstateSun);
            cmd.Parameters.AddWithValue("Sandbox", settings.Sandbox);
            cmd.Parameters.AddWithValue("SunVectorX", settings.SunVector.X);
            cmd.Parameters.AddWithValue("SunVectorY", settings.SunVector.Y);
            cmd.Parameters.AddWithValue("SunVectorZ", settings.SunVector.Z);
            cmd.Parameters.AddWithValue("FixedSun", settings.FixedSun);
            cmd.Parameters.AddWithValue("SunPosition", settings.SunPosition);
            cmd.Parameters.AddWithValue("Covenant", settings.Covenant.ToString());
            cmd.Parameters.AddWithValue("CovenantChangedDateTime", settings.CovenantChangedDateTime);
            cmd.Parameters.AddWithValue("LoadedCreationDateTime", settings.LoadedCreationDateTime);
            cmd.Parameters.AddWithValue("LoadedCreationID", settings.LoadedCreationID);
            cmd.Parameters.AddWithValue("TerrainImageID", settings.TerrainImageID);
            cmd.Parameters.AddWithValue("block_search", settings.GodBlockSearch);
            cmd.Parameters.AddWithValue("casino", settings.Casino);

            cmd.Parameters.AddWithValue("ParcelImageID", settings.ParcelImageID);
            cmd.Parameters.AddWithValue("TelehubObject", settings.TelehubObject);
            cmd.Parameters.AddWithValue("cacheID", settings.CacheID);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="row"></param>
        /// <param name="land"></param>
        /// <param name="regionUUID"></param>
        private static void FillLandCommand(MySqlCommand cmd, LandData land, UUID regionUUID)
        {
            cmd.Parameters.AddWithValue("UUID", land.GlobalID.ToString());
            cmd.Parameters.AddWithValue("RegionUUID", regionUUID.ToString());
            cmd.Parameters.AddWithValue("LocalLandID", land.LocalID);

            // Bitmap is a byte[512]
            cmd.Parameters.AddWithValue("Bitmap", land.Bitmap);

            cmd.Parameters.AddWithValue("Name", land.Name);
            cmd.Parameters.AddWithValue("Description", land.Description);
            cmd.Parameters.AddWithValue("OwnerUUID", land.OwnerID.ToString());
            cmd.Parameters.AddWithValue("IsGroupOwned", land.IsGroupOwned);
            cmd.Parameters.AddWithValue("Area", land.Area);
            cmd.Parameters.AddWithValue("AuctionID", land.AuctionID); //Unemplemented
            cmd.Parameters.AddWithValue("Category", land.Category); //Enum libsecondlife.Parcel.ParcelCategory
            cmd.Parameters.AddWithValue("ClaimDate", land.ClaimDate);
            cmd.Parameters.AddWithValue("ClaimPrice", land.ClaimPrice);
            cmd.Parameters.AddWithValue("GroupUUID", land.GroupID.ToString());
            cmd.Parameters.AddWithValue("SalePrice", land.SalePrice);
            cmd.Parameters.AddWithValue("LandStatus", land.Status); //Enum. libsecondlife.Parcel.ParcelStatus
            cmd.Parameters.AddWithValue("LandFlags", land.Flags);
            cmd.Parameters.AddWithValue("LandingType", land.LandingType);
            cmd.Parameters.AddWithValue("MediaAutoScale", land.MediaAutoScale);
            cmd.Parameters.AddWithValue("MediaTextureUUID", land.MediaID.ToString());
            cmd.Parameters.AddWithValue("MediaURL", land.MediaURL);
            cmd.Parameters.AddWithValue("MusicURL", land.MusicURL);
            cmd.Parameters.AddWithValue("PassHours", land.PassHours);
            cmd.Parameters.AddWithValue("PassPrice", land.PassPrice);
            cmd.Parameters.AddWithValue("SnapshotUUID", land.SnapshotID.ToString());
            cmd.Parameters.AddWithValue("UserLocationX", land.UserLocation.X);
            cmd.Parameters.AddWithValue("UserLocationY", land.UserLocation.Y);
            cmd.Parameters.AddWithValue("UserLocationZ", land.UserLocation.Z);
            cmd.Parameters.AddWithValue("UserLookAtX", land.UserLookAt.X);
            cmd.Parameters.AddWithValue("UserLookAtY", land.UserLookAt.Y);
            cmd.Parameters.AddWithValue("UserLookAtZ", land.UserLookAt.Z);
            cmd.Parameters.AddWithValue("AuthBuyerID", land.AuthBuyerID);
            cmd.Parameters.AddWithValue("OtherCleanTime", land.OtherCleanTime);
            cmd.Parameters.AddWithValue("Dwell", land.Dwell);
            cmd.Parameters.AddWithValue("MediaDescription", land.MediaDescription);
            cmd.Parameters.AddWithValue("MediaType", land.MediaType);
            cmd.Parameters.AddWithValue("MediaWidth", land.MediaWidth);
            cmd.Parameters.AddWithValue("MediaHeight", land.MediaHeight);
            cmd.Parameters.AddWithValue("MediaLoop", land.MediaLoop);
            cmd.Parameters.AddWithValue("ObscureMusic", land.ObscureMusic);
            cmd.Parameters.AddWithValue("ObscureMedia", land.ObscureMedia);
            cmd.Parameters.AddWithValue("SeeAVs", land.SeeAVs ? 1 : 0);
            cmd.Parameters.AddWithValue("AnyAVSounds", land.AnyAVSounds ? 1 : 0);
            cmd.Parameters.AddWithValue("GroupAVSounds", land.GroupAVSounds ? 1 : 0);
            if (land.Environment == null)
                cmd.Parameters.AddWithValue("environment", "");
            else
            {
                try
                {
                    cmd.Parameters.AddWithValue("environment", ViewerEnvironment.ToOSDString(land.Environment));
                }
                catch
                {
                    cmd.Parameters.AddWithValue("environment", "");
                }
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="row"></param>
        /// <param name="entry"></param>
        /// <param name="parcelID"></param>
        private static void FillLandAccessCommand(MySqlCommand cmd, LandAccessEntry entry, UUID parcelID)
        {
            cmd.Parameters.AddWithValue("LandUUID", parcelID.ToString());
            cmd.Parameters.AddWithValue("AccessUUID", entry.AgentID.ToString());
            cmd.Parameters.AddWithValue("Flags", entry.Flags);
            cmd.Parameters.AddWithValue("Expires", entry.Expires.ToString());
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="row"></param>
        /// <returns></returns>
        private PrimitiveBaseShape BuildShape(IDataReader row)
        {
            PrimitiveBaseShape s = new PrimitiveBaseShape();
            s.Scale = new Vector3(
                (float)(double)row["ScaleX"],
                (float)(double)row["ScaleY"],
                (float)(double)row["ScaleZ"]
            );
            // paths
            s.PCode = (byte)(int)row["PCode"];
            s.PathBegin = (ushort)(int)row["PathBegin"];
            s.PathEnd = (ushort)(int)row["PathEnd"];
            s.PathScaleX = (byte)(int)row["PathScaleX"];
            s.PathScaleY = (byte)(int)row["PathScaleY"];
            s.PathShearX = (byte)(int)row["PathShearX"];
            s.PathShearY = (byte)(int)row["PathShearY"];
            s.PathSkew = (sbyte)(int)row["PathSkew"];
            s.PathCurve = (byte)(int)row["PathCurve"];
            s.PathRadiusOffset = (sbyte)(int)row["PathRadiusOffset"];
            s.PathRevolutions = (byte)(int)row["PathRevolutions"];
            s.PathTaperX = (sbyte)(int)row["PathTaperX"];
            s.PathTaperY = (sbyte)(int)row["PathTaperY"];
            s.PathTwist = (sbyte)(int)row["PathTwist"];
            s.PathTwistBegin = (sbyte)(int)row["PathTwistBegin"];
            // profile
            s.ProfileBegin = (ushort)(int)row["ProfileBegin"];
            s.ProfileEnd = (ushort)(int)row["ProfileEnd"];
            s.ProfileCurve = (byte)(int)row["ProfileCurve"];
            s.ProfileHollow = (ushort)(int)row["ProfileHollow"];
            s.TextureEntry = (byte[])row["Texture"];

            s.ExtraParams = (byte[])row["ExtraParams"];

            s.State = (byte)(int)row["State"];
            s.LastAttachPoint = (byte)(int)row["LastAttachPoint"];

            if (!(row["Media"] is System.DBNull))
                s.Media = PrimitiveBaseShape.MediaList.FromXml((string)row["Media"]);

            return s;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="row"></param>
        /// <param name="prim"></param>
        private void FillShapeCommand(MySqlCommand cmd, SceneObjectPart prim)
        {
            PrimitiveBaseShape s = prim.Shape;
            cmd.Parameters.AddWithValue("UUID", prim.UUID.ToString());
            // shape is an enum
            cmd.Parameters.AddWithValue("Shape", 0);
            // vectors
            cmd.Parameters.AddWithValue("ScaleX", (double)s.Scale.X);
            cmd.Parameters.AddWithValue("ScaleY", (double)s.Scale.Y);
            cmd.Parameters.AddWithValue("ScaleZ", (double)s.Scale.Z);
            // paths
            cmd.Parameters.AddWithValue("PCode", s.PCode);
            cmd.Parameters.AddWithValue("PathBegin", s.PathBegin);
            cmd.Parameters.AddWithValue("PathEnd", s.PathEnd);
            cmd.Parameters.AddWithValue("PathScaleX", s.PathScaleX);
            cmd.Parameters.AddWithValue("PathScaleY", s.PathScaleY);
            cmd.Parameters.AddWithValue("PathShearX", s.PathShearX);
            cmd.Parameters.AddWithValue("PathShearY", s.PathShearY);
            cmd.Parameters.AddWithValue("PathSkew", s.PathSkew);
            cmd.Parameters.AddWithValue("PathCurve", s.PathCurve);
            cmd.Parameters.AddWithValue("PathRadiusOffset", s.PathRadiusOffset);
            cmd.Parameters.AddWithValue("PathRevolutions", s.PathRevolutions);
            cmd.Parameters.AddWithValue("PathTaperX", s.PathTaperX);
            cmd.Parameters.AddWithValue("PathTaperY", s.PathTaperY);
            cmd.Parameters.AddWithValue("PathTwist", s.PathTwist);
            cmd.Parameters.AddWithValue("PathTwistBegin", s.PathTwistBegin);
            // profile
            cmd.Parameters.AddWithValue("ProfileBegin", s.ProfileBegin);
            cmd.Parameters.AddWithValue("ProfileEnd", s.ProfileEnd);
            cmd.Parameters.AddWithValue("ProfileCurve", s.ProfileCurve);
            cmd.Parameters.AddWithValue("ProfileHollow", s.ProfileHollow);
            cmd.Parameters.AddWithValue("Texture", s.TextureEntry);
            cmd.Parameters.AddWithValue("ExtraParams", s.ExtraParams);
            cmd.Parameters.AddWithValue("State", s.State);
            cmd.Parameters.AddWithValue("LastAttachPoint", s.LastAttachPoint);
            cmd.Parameters.AddWithValue("Media", null == s.Media ? null : s.Media.ToXml());
        }

        public virtual void StorePrimInventory(UUID primID, ICollection<TaskInventoryItem> items)
        {
            lock (m_dbLock)
            {
                RemoveItems(primID);

                if (items.Count == 0)
                    return;

                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        cmd.CommandText = "insert into primitems (" +
                                "invType, assetType, name, " +
                                "description, creationDate, nextPermissions, " +
                                "currentPermissions, basePermissions, " +
                                "everyonePermissions, groupPermissions, " +
                                "flags, itemID, primID, assetID, " +
                                "parentFolderID, creatorID, ownerID, " +
                                "groupID, lastOwnerID) values (?invType, " +
                                "?assetType, ?name, ?description, " +
                                "?creationDate, ?nextPermissions, " +
                                "?currentPermissions, ?basePermissions, " +
                                "?everyonePermissions, ?groupPermissions, " +
                                "?flags, ?itemID, ?primID, ?assetID, " +
                                "?parentFolderID, ?creatorID, ?ownerID, " +
                                "?groupID, ?lastOwnerID)";

                        foreach (TaskInventoryItem item in items)
                        {
                            cmd.Parameters.Clear();

                            FillItemCommand(cmd, item);

                            ExecuteNonQuery(cmd);
                        }
                    }
                    dbcon.Close();
                }
            }
        }

        public UUID[] GetObjectIDs(UUID regionID)
        {
            List<UUID> uuids = new List<UUID>();

            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        cmd.CommandText = "select UUID from prims where RegionUUID = ?RegionUUID and SceneGroupID = UUID";
                        cmd.Parameters.AddWithValue("RegionUUID", regionID.ToString());

                        using (IDataReader reader = ExecuteReader(cmd))
                        {
                            while (reader.Read())
                            {
                                UUID id = new UUID(reader["UUID"].ToString());

                                uuids.Add(id);
                            }
                        }
                    }
                    dbcon.Close();
                }
            }

            return uuids.ToArray();
        }

        private void LoadSpawnPoints(RegionSettings rs)
        {
            rs.ClearSpawnPoints();

            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        cmd.CommandText = "select Yaw, Pitch, Distance from spawn_points where RegionID = ?RegionID";
                        cmd.Parameters.AddWithValue("?RegionID", rs.RegionUUID.ToString());

                        using (IDataReader r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                SpawnPoint sp = new SpawnPoint();

                                sp.Yaw = (float)r["Yaw"];
                                sp.Pitch = (float)r["Pitch"];
                                sp.Distance = (float)r["Distance"];

                                rs.AddSpawnPoint(sp);
                            }
                        }
                    }
                    dbcon.Close();
                }
            }
        }

        private void SaveSpawnPoints(RegionSettings rs)
        {
            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = dbcon.CreateCommand())
                    {
                        cmd.CommandText = "delete from spawn_points where RegionID = ?RegionID";
                        cmd.Parameters.AddWithValue("?RegionID", rs.RegionUUID.ToString());

                        cmd.ExecuteNonQuery();

                        cmd.Parameters.Clear();

                        cmd.CommandText = "insert into spawn_points (RegionID, Yaw, Pitch, Distance) values ( ?RegionID, ?Yaw, ?Pitch, ?Distance)";

                        foreach (SpawnPoint p in rs.SpawnPoints())
                        {
                            cmd.Parameters.AddWithValue("?RegionID", rs.RegionUUID.ToString());
                            cmd.Parameters.AddWithValue("?Yaw", p.Yaw);
                            cmd.Parameters.AddWithValue("?Pitch", p.Pitch);
                            cmd.Parameters.AddWithValue("?Distance", p.Distance);

                            cmd.ExecuteNonQuery();
                            cmd.Parameters.Clear();
                        }
                    }
                    dbcon.Close();
                }
            }
        }

        public void SaveExtra(UUID regionID, string name, string val)
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = dbcon.CreateCommand())
                {
                    cmd.CommandText = "replace into regionextra values (?RegionID, ?Name, ?value)";
                    cmd.Parameters.AddWithValue("?RegionID", regionID.ToString());
                    cmd.Parameters.AddWithValue("?Name", name);
                    cmd.Parameters.AddWithValue("?value", val);

                    cmd.ExecuteNonQuery();
                }
                dbcon.Close();
            }
        }

        public void RemoveExtra(UUID regionID, string name)
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = dbcon.CreateCommand())
                {
                    cmd.CommandText = "delete from regionextra where RegionID=?RegionID and Name=?Name";
                    cmd.Parameters.AddWithValue("?RegionID", regionID.ToString());
                    cmd.Parameters.AddWithValue("?Name", name);

                    cmd.ExecuteNonQuery();
                }
                dbcon.Close();
            }
        }

        public Dictionary<string, string> GetExtra(UUID regionID)
        {
            Dictionary<string, string> ret = new Dictionary<string, string>();

            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                using (MySqlCommand cmd = dbcon.CreateCommand())
                {
                    cmd.CommandText = "select * from regionextra where RegionID=?RegionID";
                    cmd.Parameters.AddWithValue("?RegionID", regionID.ToString());
                    using (IDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            ret[r["Name"].ToString()] = r["value"].ToString();
                        }
                    }
                }
                dbcon.Close();
            }

            return ret;
        }
    }
}
