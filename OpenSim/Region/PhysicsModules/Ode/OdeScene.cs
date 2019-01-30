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

// changes for varsize regions
// note that raycasts need to have limited range
// (even in normal regions)
// or application thread stack may just blowup
// see RayCast(ODERayCastRequest req)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using log4net;
using Nini.Config;
using Mono.Addins;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.PhysicsModules.SharedBase;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Interfaces;

namespace OpenSim.Region.PhysicsModule.ODE
{
    public enum StatusIndicators : int
    {
        Generic = 0,
        Start = 1,
        End = 2
    }

    [Flags]
    public enum CollisionCategories : int
    {
        Disabled = 0,
        Geom = 0x00000001,
        Body = 0x00000002,
        Space = 0x00000004,
        Character = 0x00000008,
        Land = 0x00000010,
        Water = 0x00000020,
        Wind = 0x00000040,
        Sensor = 0x00000080,
        Selected = 0x00000100
    }

    /// <summary>
    /// Material type for a primitive
    /// </summary>
    public enum Material : int
    {
        /// <summary></summary>
        Stone = 0,
        /// <summary></summary>
        Metal = 1,
        /// <summary></summary>
        Glass = 2,
        /// <summary></summary>
        Wood = 3,
        /// <summary></summary>
        Flesh = 4,
        /// <summary></summary>
        Plastic = 5,
        /// <summary></summary>
        Rubber = 6
    }

    public class OdeScene : PhysicsScene
    {
        private readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType.ToString());

        // private Dictionary<string, sCollisionData> m_storedCollisions = new Dictionary<string, sCollisionData>();

        /// <summary>
        /// Provide a sync object so that only one thread calls d.Collide() at a time across all OdeScene instances.
        /// </summary>
        /// <remarks>
        /// With ODE as of r1755 (though also tested on r1860), only one thread can call d.Collide() at a
        /// time, even where physics objects are in entirely different ODE worlds.  This is because generating contacts
        /// uses a static cache at the ODE level.
        ///
        /// Without locking, simulators running multiple regions will eventually crash with a native stack trace similar
        /// to
        ///
        /// mono() [0x489171]
        /// mono() [0x4d154f]
        /// /lib/x86_64-linux-gnu/libpthread.so.0(+0xfc60) [0x7f6ded592c60]
        /// .../opensim/bin/libode-x86_64.so(_ZN6Opcode11OBBCollider8_CollideEPKNS_14AABBNoLeafNodeE+0xd7a) [0x7f6dd822628a]
        ///
        /// ODE provides an experimental option to cache in thread local storage but compiling ODE with this option
        /// causes OpenSimulator to immediately crash with a native stack trace similar to
        ///
        /// mono() [0x489171]
        /// mono() [0x4d154f]
        /// /lib/x86_64-linux-gnu/libpthread.so.0(+0xfc60) [0x7f03c9849c60]
        /// .../opensim/bin/libode-x86_64.so(_Z12dCollideCCTLP6dxGeomS0_iP12dContactGeomi+0x92) [0x7f03b44bcf82]
        /// </remarks>
        internal static object UniversalColliderSyncObject = new object();
        internal static object SimulationLock = new object();

        /// <summary>
        /// Is stats collecting enabled for this ODE scene?
        /// </summary>
        public bool CollectStats { get; set; }

        /// <summary>
        /// Statistics for this scene.
        /// </summary>
        private Dictionary<string, float> m_stats = new Dictionary<string, float>();

        /// <summary>
        /// Stat name for total number of avatars in this ODE scene.
        /// </summary>
        public const string ODETotalAvatarsStatName = "ODETotalAvatars";

        /// <summary>
        /// Stat name for total number of prims in this ODE scene.
        /// </summary>
        public const string ODETotalPrimsStatName = "ODETotalPrims";

        /// <summary>
        /// Stat name for total number of prims with active physics in this ODE scene.
        /// </summary>
        public const string ODEActivePrimsStatName = "ODEActivePrims";

        /// <summary>
        /// Stat name for the total time spent in ODE frame processing.
        /// </summary>
        /// <remarks>
        /// A sanity check for the main scene loop physics time.
        /// </remarks>
        public const string ODETotalFrameMsStatName = "ODETotalFrameMS";

        /// <summary>
        /// Stat name for time spent processing avatar taints per frame
        /// </summary>
        public const string ODEAvatarTaintMsStatName = "ODEAvatarTaintFrameMS";

        /// <summary>
        /// Stat name for time spent processing prim taints per frame
        /// </summary>
        public const string ODEPrimTaintMsStatName = "ODEPrimTaintFrameMS";

        /// <summary>
        /// Stat name for time spent calculating avatar forces per frame.
        /// </summary>
        public const string ODEAvatarForcesFrameMsStatName = "ODEAvatarForcesFrameMS";

        /// <summary>
        /// Stat name for time spent calculating prim forces per frame
        /// </summary>
        public const string ODEPrimForcesFrameMsStatName = "ODEPrimForcesFrameMS";

        /// <summary>
        /// Stat name for time spent fulfilling raycasting requests per frame
        /// </summary>
        public const string ODERaycastingFrameMsStatName = "ODERaycastingFrameMS";

        /// <summary>
        /// Stat name for time spent in native code that actually steps through the simulation.
        /// </summary>
        public const string ODENativeStepFrameMsStatName = "ODENativeStepFrameMS";

        /// <summary>
        /// Stat name for the number of milliseconds that ODE spends in native space collision code.
        /// </summary>
        public const string ODENativeSpaceCollisionFrameMsStatName = "ODENativeSpaceCollisionFrameMS";

        /// <summary>
        /// Stat name for milliseconds that ODE spends in native geom collision code.
        /// </summary>
        public const string ODENativeGeomCollisionFrameMsStatName = "ODENativeGeomCollisionFrameMS";

        /// <summary>
        /// Time spent in collision processing that is not spent in native space or geom collision code.
        /// </summary>
        public const string ODEOtherCollisionFrameMsStatName = "ODEOtherCollisionFrameMS";

        /// <summary>
        /// Stat name for time spent notifying listeners of collisions
        /// </summary>
        public const string ODECollisionNotificationFrameMsStatName = "ODECollisionNotificationFrameMS";

        /// <summary>
        /// Stat name for milliseconds spent updating avatar position and velocity
        /// </summary>
        public const string ODEAvatarUpdateFrameMsStatName = "ODEAvatarUpdateFrameMS";

        /// <summary>
        /// Stat name for the milliseconds spent updating prim position and velocity
        /// </summary>
        public const string ODEPrimUpdateFrameMsStatName = "ODEPrimUpdateFrameMS";

        /// <summary>
        /// Stat name for avatar collisions with another entity.
        /// </summary>
        public const string ODEAvatarContactsStatsName = "ODEAvatarContacts";

        /// <summary>
        /// Stat name for prim collisions with another entity.
        /// </summary>
        public const string ODEPrimContactsStatName = "ODEPrimContacts";

        /// <summary>
        /// Used to hold tick numbers for stat collection purposes.
        /// </summary>
        private int m_nativeCollisionStartTick;

        /// <summary>
        /// A messy way to tell if we need to avoid adding a collision time because this was already done in the callback.
        /// </summary>
        private bool m_inCollisionTiming;

        /// <summary>
        /// A temporary holder for the number of avatar collisions in a frame, so we can work out how many object
        /// collisions occured using the _perloopcontact if stats collection is enabled.
        /// </summary>
        private int m_tempAvatarCollisionsThisFrame;

        /// <summary>
        /// Used in calculating physics frame time dilation
        /// </summary>
        private int tickCountFrameRun;

        /// <summary>
        /// Used in calculating physics frame time dilation
        /// </summary>
        private int latertickcount;

        private Random fluidRandomizer = new Random(Environment.TickCount);

        private uint m_regionWidth = Constants.RegionSize;
        private uint m_regionHeight = Constants.RegionSize;

        private float ODE_STEPSIZE = 0.0178f;
        private float metersInSpace = 29.9f;
        private float m_timeDilation = 1.0f;

        public float gravityx = 0f;
        public float gravityy = 0f;
        public float gravityz = -9.8f;

        public float AvatarTerminalVelocity { get; set; }

        private float contactsurfacelayer = 0.001f;

        private int HashspaceLow = -5;
        private int HashspaceHigh = 12;

        private float waterlevel = 0f;
        private int framecount = 0;
        //private int m_returncollisions = 10;

        private IntPtr contactgroup;

//        internal IntPtr WaterGeom;

        private float nmTerrainContactFriction = 255.0f;
        private float nmTerrainContactBounce = 0.1f;
        private float nmTerrainContactERP = 0.1025f;

        private float mTerrainContactFriction = 75f;
        private float mTerrainContactBounce = 0.1f;
        private float mTerrainContactERP = 0.05025f;

        private float nmAvatarObjectContactFriction = 250f;
        private float nmAvatarObjectContactBounce = 0.1f;

        private float mAvatarObjectContactFriction = 75f;
        private float mAvatarObjectContactBounce = 0.1f;

        private float avPIDD = 3200f;
        private float avPIDP = 1400f;
        private float avCapRadius = 0.37f;
        private float avStandupTensor = 2000000f;

        /// <summary>
        /// true = old compatibility mode with leaning capsule; false = new corrected mode
        /// </summary>
        /// <remarks>
        /// Even when set to false, the capsule still tilts but this is done in a different way.
        /// </remarks>
        public bool IsAvCapsuleTilted { get; private set; }

        private float avDensity = 80f;
        private float avMovementDivisorWalk = 1.3f;
        private float avMovementDivisorRun = 0.8f;
        private float minimumGroundFlightOffset = 3f;
        public float maximumMassObject = 10000.01f;

        public bool meshSculptedPrim = true;
        public bool forceSimplePrimMeshing = false;

        public float meshSculptLOD = 32;
        public float MeshSculptphysicalLOD = 16;

        public float geomDefaultDensity = 10.000006836f;

        public int geomContactPointsStartthrottle = 3;
        public int geomUpdatesPerThrottledUpdate = 15;
        private const int avatarExpectedContacts = 3;

        public float bodyPIDD = 35f;
        public float bodyPIDG = 25;

        public int bodyFramesAutoDisable = 20;

        private bool m_filterCollisions = true;

        private SafeNativeMethods.NearCallback nearCallback;

        /// <summary>
        /// Avatars in the physics scene.
        /// </summary>
        private readonly HashSet<OdeCharacter> _characters = new HashSet<OdeCharacter>();

        /// <summary>
        /// Prims in the physics scene.
        /// </summary>
        private readonly HashSet<OdePrim> _prims = new HashSet<OdePrim>();

        /// <summary>
        /// Prims in the physics scene that are subject to physics, not just collisions.
        /// </summary>
        private readonly HashSet<OdePrim> _activeprims = new HashSet<OdePrim>();

        /// <summary>
        /// Prims that the simulator has created/deleted/updated and so need updating in ODE.
        /// </summary>
        private readonly HashSet<OdePrim> _taintedPrims = new HashSet<OdePrim>();

        /// <summary>
        /// Record a character that has taints to be processed.
        /// </summary>
        private readonly HashSet<OdeCharacter> _taintedActors = new HashSet<OdeCharacter>();

        /// <summary>
        /// Keep record of contacts in the physics loop so that we can remove duplicates.
        /// </summary>
        private readonly List<SafeNativeMethods.ContactGeom> _perloopContact = new List<SafeNativeMethods.ContactGeom>();

        /// <summary>
        /// A dictionary of actors that should receive collision events.
        /// </summary>
        private readonly Dictionary<uint, PhysicsActor> m_collisionEventActors = new Dictionary<uint, PhysicsActor>();

        /// <summary>
        /// A dictionary of collision event changes that are waiting to be processed.
        /// </summary>
        private readonly Dictionary<uint, PhysicsActor> m_collisionEventActorsChanges = new Dictionary<uint, PhysicsActor>();

        /// <summary>
        /// Maps a unique geometry id (a memory location) to a physics actor name.
        /// </summary>
        /// <remarks>
        /// Only actors participating in collisions have geometries.  This has to be maintained separately from
        /// actor_name_map because terrain and water currently don't conceptually have a physics actor of their own
        /// apart from the singleton PANull
        /// </remarks>
        public Dictionary<IntPtr, String> geom_name_map = new Dictionary<IntPtr, String>();

        /// <summary>
        /// Maps a unique geometry id (a memory location) to a physics actor.
        /// </summary>
        /// <remarks>
        /// Only actors participating in collisions have geometries.
        /// </remarks>
        public Dictionary<IntPtr, PhysicsActor> actor_name_map = new Dictionary<IntPtr, PhysicsActor>();

        /// <summary>
        /// Defects list to remove characters that no longer have finite positions due to some other bug.
        /// </summary>
        /// <remarks>
        /// Used repeatedly in Simulate() but initialized once here.
        /// </remarks>
        private readonly List<OdeCharacter> defects = new List<OdeCharacter>();

        private bool m_NINJA_physics_joints_enabled = false;
        //private Dictionary<String, IntPtr> jointpart_name_map = new Dictionary<String,IntPtr>();
        private readonly Dictionary<String, List<PhysicsJoint>> joints_connecting_actor = new Dictionary<String, List<PhysicsJoint>>();
        private SafeNativeMethods.ContactGeom[] contacts;

        /// <summary>
        /// Lock only briefly. accessed by external code (to request new joints) and by OdeScene.Simulate() to move those joints into pending/active
        /// </summary>
        private readonly List<PhysicsJoint> requestedJointsToBeCreated = new List<PhysicsJoint>();

        /// <summary>
        /// can lock for longer. accessed only by OdeScene.
        /// </summary>
        private readonly List<PhysicsJoint> pendingJoints = new List<PhysicsJoint>();

        /// <summary>
        /// can lock for longer. accessed only by OdeScene.
        /// </summary>
        private readonly List<PhysicsJoint> activeJoints = new List<PhysicsJoint>();

        /// <summary>
        /// lock only briefly. accessed by external code (to request deletion of joints) and by OdeScene.Simulate() to move those joints out of pending/active
        /// </summary>
        private readonly List<string> requestedJointsToBeDeleted = new List<string>();

        private Object externalJointRequestsLock = new Object();
        private readonly Dictionary<String, PhysicsJoint> SOPName_to_activeJoint = new Dictionary<String, PhysicsJoint>();
        private readonly Dictionary<String, PhysicsJoint> SOPName_to_pendingJoint = new Dictionary<String, PhysicsJoint>();
        private readonly DoubleDictionary<Vector3, IntPtr, IntPtr> RegionTerrain = new DoubleDictionary<Vector3, IntPtr, IntPtr>();
        private readonly Dictionary<IntPtr,float[]> TerrainHeightFieldHeights = new Dictionary<IntPtr, float[]>();

        private SafeNativeMethods.Contact contact;
        private SafeNativeMethods.Contact TerrainContact;
        private SafeNativeMethods.Contact AvatarMovementprimContact;
        private SafeNativeMethods.Contact AvatarMovementTerrainContact;
        private SafeNativeMethods.Contact WaterContact;
        private SafeNativeMethods.Contact[,] m_materialContacts;

        private int m_physicsiterations = 10;
        private const float m_SkipFramesAtms = 0.40f; // Drop frames gracefully at a 400 ms lag
        private readonly PhysicsActor PANull = new NullPhysicsActor();
        private float step_time = 0.0f;
        public IntPtr world;
        private uint obj2LocalID = 0;
        private OdeCharacter cc1;
        private OdePrim cp1;
        private OdeCharacter cc2;
        private OdePrim cp2;
        private int p1ExpectedPoints = 0;
        private int p2ExpectedPoints = 0;

        public IntPtr space;

        // split static geometry collision handling into spaces of 30 meters
        public IntPtr[,] staticPrimspace;

        /// <summary>
        /// Used to lock the entire physics scene.  Locked during the main part of Simulate()
        /// </summary>
        internal Object OdeLock = new Object();

        private bool _worldInitialized = false;

        public IMesher mesher;

        private IConfigSource m_config;

        public bool physics_logging = false;
        public int physics_logging_interval = 0;
        public bool physics_logging_append_existing_logfile = false;

        private bool avplanted = false;
        private bool av_av_collisions_off = false;

        internal SafeNativeMethods.Vector3 xyz = new SafeNativeMethods.Vector3(128.1640f, 128.3079f, 25.7600f);
        internal SafeNativeMethods.Vector3 hpr = new SafeNativeMethods.Vector3(125.5000f, -17.0000f, 0.0000f);

        private volatile int m_global_contactcount = 0;

        private Vector3 m_worldOffset = Vector3.Zero;
        public Vector2 WorldExtents = new Vector2((int)Constants.RegionSize, (int)Constants.RegionSize);
        private PhysicsScene m_parentScene = null;

        float spacesPerMeterX;
        float spacesPerMeterY;
        int spaceGridMaxX;
        int spaceGridMaxY;

        private ODERayCastRequestManager m_rayCastManager;

        public Scene m_frameWorkScene = null;

        public OdeScene(Scene pscene, IConfigSource psourceconfig, string pname, string pversion)
        {
            SafeNativeMethods.AllocateODEDataForThread(~0U);

            m_config = psourceconfig;
            m_frameWorkScene = pscene;

            EngineType = pname;
            PhysicsSceneName = EngineType + "/" + pscene.RegionInfo.RegionName;
            EngineName = pname + " " + pversion;

            pscene.RegisterModuleInterface<PhysicsScene>(this);
            Vector3 extent = new Vector3(pscene.RegionInfo.RegionSizeX, pscene.RegionInfo.RegionSizeY, pscene.RegionInfo.RegionSizeZ);
            Initialise(extent);
            InitialiseFromConfig(m_config);

            // This may not be that good since terrain may not be avaiable at this point
            base.Initialise(pscene.PhysicsRequestAsset,
                (pscene.Heightmap != null ? pscene.Heightmap.GetFloatsSerialised() : new float[(int)(extent.X * extent.Y)]),
                (float)pscene.RegionInfo.RegionSettings.WaterHeight);

        }

         public void RegionLoaded()
        {
            mesher = m_frameWorkScene.RequestModuleInterface<IMesher>();
            if (mesher == null)
                m_log.WarnFormat("[ODE SCENE]: No mesher in {0}. Things will not work well.", PhysicsSceneName);

            m_frameWorkScene.PhysicsEnabled = true;
        }

        /// <summary>
        /// Initiailizes the scene
        /// Sets many properties that ODE requires to be stable
        /// These settings need to be tweaked 'exactly' right or weird stuff happens.
        /// </summary>
        private void Initialise(Vector3 regionExtent)
        {
            WorldExtents.X = regionExtent.X;
            m_regionWidth = (uint)regionExtent.X;
            WorldExtents.Y = regionExtent.Y;
            m_regionHeight = (uint)regionExtent.Y;

            nearCallback = near;
            m_rayCastManager = new ODERayCastRequestManager(this);

            // Create the world and the first space
            world = SafeNativeMethods.WorldCreate();
            space = SafeNativeMethods.HashSpaceCreate(IntPtr.Zero);
            contactgroup = SafeNativeMethods.JointGroupCreate(0);

            SafeNativeMethods.WorldSetAutoDisableFlag(world, false);
        }

        // Initialize from configs
        private void InitialiseFromConfig(IConfigSource config)
        {
            InitializeExtraStats();

            m_config = config;
            // Defaults

            avPIDD = 2200.0f;
            avPIDP = 900.0f;
            avStandupTensor = 550000f;

            int contactsPerCollision = 80;

            if (m_config != null)
            {
                IConfig physicsconfig = m_config.Configs["ODEPhysicsSettings"];
                if (physicsconfig != null)
                {
                    CollectStats = physicsconfig.GetBoolean("collect_stats", false);

                    gravityx = physicsconfig.GetFloat("world_gravityx", 0f);
                    gravityy = physicsconfig.GetFloat("world_gravityy", 0f);
                    gravityz = physicsconfig.GetFloat("world_gravityz", -9.8f);

                    float avatarTerminalVelocity = physicsconfig.GetFloat("avatar_terminal_velocity", 54f);
                    AvatarTerminalVelocity = Util.Clamp<float>(avatarTerminalVelocity, 0, 255f);
                    if (AvatarTerminalVelocity != avatarTerminalVelocity)
                    {
                        m_log.WarnFormat(
                            "[ODE SCENE]: avatar_terminal_velocity of {0} is invalid.  Clamping to {1}",
                            avatarTerminalVelocity, AvatarTerminalVelocity);
                    }

                    HashspaceLow = physicsconfig.GetInt("world_hashspace_level_low", -5);
                    HashspaceHigh = physicsconfig.GetInt("world_hashspace_level_high", 12);

                    metersInSpace = physicsconfig.GetFloat("meters_in_small_space", 29.9f);

                    contactsurfacelayer = physicsconfig.GetFloat("world_contact_surface_layer", 0.001f);

                    nmTerrainContactFriction = physicsconfig.GetFloat("nm_terraincontact_friction", 255.0f);
                    nmTerrainContactBounce = physicsconfig.GetFloat("nm_terraincontact_bounce", 0.1f);
                    nmTerrainContactERP = physicsconfig.GetFloat("nm_terraincontact_erp", 0.1025f);

                    mTerrainContactFriction = physicsconfig.GetFloat("m_terraincontact_friction", 75f);
                    mTerrainContactBounce = physicsconfig.GetFloat("m_terraincontact_bounce", 0.05f);
                    mTerrainContactERP = physicsconfig.GetFloat("m_terraincontact_erp", 0.05025f);

                    nmAvatarObjectContactFriction = physicsconfig.GetFloat("objectcontact_friction", 250f);
                    nmAvatarObjectContactBounce = physicsconfig.GetFloat("objectcontact_bounce", 0.2f);

                    mAvatarObjectContactFriction = physicsconfig.GetFloat("m_avatarobjectcontact_friction", 75f);
                    mAvatarObjectContactBounce = physicsconfig.GetFloat("m_avatarobjectcontact_bounce", 0.1f);

                    ODE_STEPSIZE = physicsconfig.GetFloat("world_stepsize", ODE_STEPSIZE);
                    m_physicsiterations = physicsconfig.GetInt("world_solver_iterations", 10);

                    avDensity = physicsconfig.GetFloat("av_density", 80f);
//                    avHeightFudgeFactor = physicsconfig.GetFloat("av_height_fudge_factor", 0.52f);
                    avMovementDivisorWalk = physicsconfig.GetFloat("av_movement_divisor_walk", 1.3f);
                    avMovementDivisorRun = physicsconfig.GetFloat("av_movement_divisor_run", 0.8f);
                    avCapRadius = physicsconfig.GetFloat("av_capsule_radius", 0.37f);
                    avplanted = physicsconfig.GetBoolean("av_planted", false);
                    av_av_collisions_off = physicsconfig.GetBoolean("av_av_collisions_off", false);

                    IsAvCapsuleTilted = physicsconfig.GetBoolean("av_capsule_tilted", false);

                    contactsPerCollision = physicsconfig.GetInt("contacts_per_collision", 80);

                    geomContactPointsStartthrottle = physicsconfig.GetInt("geom_contactpoints_start_throttling", 5);
                    geomUpdatesPerThrottledUpdate = physicsconfig.GetInt("geom_updates_before_throttled_update", 15);

                    geomDefaultDensity = physicsconfig.GetFloat("geometry_default_density", 10.000006836f);
                    bodyFramesAutoDisable = physicsconfig.GetInt("body_frames_auto_disable", 20);

                    bodyPIDD = physicsconfig.GetFloat("body_pid_derivative", 35f);
                    bodyPIDG = physicsconfig.GetFloat("body_pid_gain", 25f);

                    forceSimplePrimMeshing = physicsconfig.GetBoolean("force_simple_prim_meshing", forceSimplePrimMeshing);
                    meshSculptedPrim = physicsconfig.GetBoolean("mesh_sculpted_prim", true);
                    meshSculptLOD = physicsconfig.GetFloat("mesh_lod", 32f);
                    MeshSculptphysicalLOD = physicsconfig.GetFloat("mesh_physical_lod", 16f);
                    m_filterCollisions = physicsconfig.GetBoolean("filter_collisions", false);

                    avPIDD = physicsconfig.GetFloat("av_pid_derivative", 2200.0f);
                    avPIDP = physicsconfig.GetFloat("av_pid_proportional", 900.0f);
                    avStandupTensor = physicsconfig.GetFloat("av_capsule_standup_tensor", 550000f);

                    physics_logging = physicsconfig.GetBoolean("physics_logging", false);
                    physics_logging_interval = physicsconfig.GetInt("physics_logging_interval", 0);
                    physics_logging_append_existing_logfile = physicsconfig.GetBoolean("physics_logging_append_existing_logfile", false);

//                    m_NINJA_physics_joints_enabled = physicsconfig.GetBoolean("use_NINJA_physics_joints", false);
                    minimumGroundFlightOffset = physicsconfig.GetFloat("minimum_ground_flight_offset", 3f);
                    maximumMassObject = physicsconfig.GetFloat("maximum_mass_object", 10000.01f);
                }
            }

            contacts = new SafeNativeMethods.ContactGeom[contactsPerCollision];

            spacesPerMeterX = 1.0f / metersInSpace;
            spacesPerMeterY = 1.0f / metersInSpace;

            spaceGridMaxX = (int)(WorldExtents.X * spacesPerMeterX);
            spaceGridMaxY = (int)(WorldExtents.Y * spacesPerMeterY);

            // note: limit number of spaces
            if (spaceGridMaxX > 24)
            {
                spaceGridMaxX = 24;
                spacesPerMeterX =  spaceGridMaxX / WorldExtents.X;
            }
            if (spaceGridMaxY > 24)
            {
                spaceGridMaxY = 24;
                spacesPerMeterY = spaceGridMaxY / WorldExtents.Y;
            }

            staticPrimspace = new IntPtr[spaceGridMaxX, spaceGridMaxY];

            // make this index limits
            spaceGridMaxX--;
            spaceGridMaxY--;

            // Centeral contact friction and bounce
            // ckrinke 11/10/08 Enabling soft_erp but not soft_cfm until I figure out why
            // an avatar falls through in Z but not in X or Y when walking on a prim.
            contact.surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            contact.surface.mu = nmAvatarObjectContactFriction;
            contact.surface.bounce = nmAvatarObjectContactBounce;
            contact.surface.soft_cfm = 0.010f;
            contact.surface.soft_erp = 0.010f;

            // Terrain contact friction and Bounce
            // This is the *non* moving version.   Use this when an avatar
            // isn't moving to keep it in place better
            TerrainContact.surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            TerrainContact.surface.mu = nmTerrainContactFriction;
            TerrainContact.surface.bounce = nmTerrainContactBounce;
            TerrainContact.surface.soft_erp = nmTerrainContactERP;

            WaterContact.surface.mode |= (SafeNativeMethods.ContactFlags.SoftERP | SafeNativeMethods.ContactFlags.SoftCFM);
            WaterContact.surface.mu = 0f; // No friction
            WaterContact.surface.bounce = 0.0f; // No bounce
            WaterContact.surface.soft_cfm = 0.010f;
            WaterContact.surface.soft_erp = 0.010f;

            // Prim contact friction and bounce
            // THis is the *non* moving version of friction and bounce
            // Use this when an avatar comes in contact with a prim
            // and is moving
            AvatarMovementprimContact.surface.mu = mAvatarObjectContactFriction;
            AvatarMovementprimContact.surface.bounce = mAvatarObjectContactBounce;

            // Terrain contact friction bounce and various error correcting calculations
            // Use this when an avatar is in contact with the terrain and moving.
            AvatarMovementTerrainContact.surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            AvatarMovementTerrainContact.surface.mu = mTerrainContactFriction;
            AvatarMovementTerrainContact.surface.bounce = mTerrainContactBounce;
            AvatarMovementTerrainContact.surface.soft_erp = mTerrainContactERP;

            /*
                <summary></summary>
                Stone = 0,
                /// <summary></summary>
                Metal = 1,
                /// <summary></summary>
                Glass = 2,
                /// <summary></summary>
                Wood = 3,
                /// <summary></summary>
                Flesh = 4,
                /// <summary></summary>
                Plastic = 5,
                /// <summary></summary>
                Rubber = 6
             */

            m_materialContacts = new SafeNativeMethods.Contact[7,2];

            m_materialContacts[(int)Material.Stone, 0] = new SafeNativeMethods.Contact();
            m_materialContacts[(int)Material.Stone, 0].surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            m_materialContacts[(int)Material.Stone, 0].surface.mu = nmAvatarObjectContactFriction;
            m_materialContacts[(int)Material.Stone, 0].surface.bounce = nmAvatarObjectContactBounce;
            m_materialContacts[(int)Material.Stone, 0].surface.soft_cfm = 0.010f;
            m_materialContacts[(int)Material.Stone, 0].surface.soft_erp = 0.010f;

            m_materialContacts[(int)Material.Stone, 1] = new SafeNativeMethods.Contact();
            m_materialContacts[(int)Material.Stone, 1].surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            m_materialContacts[(int)Material.Stone, 1].surface.mu = mAvatarObjectContactFriction;
            m_materialContacts[(int)Material.Stone, 1].surface.bounce = mAvatarObjectContactBounce;
            m_materialContacts[(int)Material.Stone, 1].surface.soft_cfm = 0.010f;
            m_materialContacts[(int)Material.Stone, 1].surface.soft_erp = 0.010f;

            m_materialContacts[(int)Material.Metal, 0] = new SafeNativeMethods.Contact();
            m_materialContacts[(int)Material.Metal, 0].surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            m_materialContacts[(int)Material.Metal, 0].surface.mu = nmAvatarObjectContactFriction;
            m_materialContacts[(int)Material.Metal, 0].surface.bounce = nmAvatarObjectContactBounce;
            m_materialContacts[(int)Material.Metal, 0].surface.soft_cfm = 0.010f;
            m_materialContacts[(int)Material.Metal, 0].surface.soft_erp = 0.010f;

            m_materialContacts[(int)Material.Metal, 1] = new SafeNativeMethods.Contact();
            m_materialContacts[(int)Material.Metal, 1].surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            m_materialContacts[(int)Material.Metal, 1].surface.mu = mAvatarObjectContactFriction;
            m_materialContacts[(int)Material.Metal, 1].surface.bounce = mAvatarObjectContactBounce;
            m_materialContacts[(int)Material.Metal, 1].surface.soft_cfm = 0.010f;
            m_materialContacts[(int)Material.Metal, 1].surface.soft_erp = 0.010f;

            m_materialContacts[(int)Material.Glass, 0] = new SafeNativeMethods.Contact();
            m_materialContacts[(int)Material.Glass, 0].surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            m_materialContacts[(int)Material.Glass, 0].surface.mu = 1f;
            m_materialContacts[(int)Material.Glass, 0].surface.bounce = 0.5f;
            m_materialContacts[(int)Material.Glass, 0].surface.soft_cfm = 0.010f;
            m_materialContacts[(int)Material.Glass, 0].surface.soft_erp = 0.010f;

            /*
                private float nmAvatarObjectContactFriction = 250f;
                private float nmAvatarObjectContactBounce = 0.1f;

                private float mAvatarObjectContactFriction = 75f;
                private float mAvatarObjectContactBounce = 0.1f;
            */
            m_materialContacts[(int)Material.Glass, 1] = new SafeNativeMethods.Contact();
            m_materialContacts[(int)Material.Glass, 1].surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            m_materialContacts[(int)Material.Glass, 1].surface.mu = 1f;
            m_materialContacts[(int)Material.Glass, 1].surface.bounce = 0.5f;
            m_materialContacts[(int)Material.Glass, 1].surface.soft_cfm = 0.010f;
            m_materialContacts[(int)Material.Glass, 1].surface.soft_erp = 0.010f;

            m_materialContacts[(int)Material.Wood, 0] = new SafeNativeMethods.Contact();
            m_materialContacts[(int)Material.Wood, 0].surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            m_materialContacts[(int)Material.Wood, 0].surface.mu = nmAvatarObjectContactFriction;
            m_materialContacts[(int)Material.Wood, 0].surface.bounce = nmAvatarObjectContactBounce;
            m_materialContacts[(int)Material.Wood, 0].surface.soft_cfm = 0.010f;
            m_materialContacts[(int)Material.Wood, 0].surface.soft_erp = 0.010f;

            m_materialContacts[(int)Material.Wood, 1] = new SafeNativeMethods.Contact();
            m_materialContacts[(int)Material.Wood, 1].surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            m_materialContacts[(int)Material.Wood, 1].surface.mu = mAvatarObjectContactFriction;
            m_materialContacts[(int)Material.Wood, 1].surface.bounce = mAvatarObjectContactBounce;
            m_materialContacts[(int)Material.Wood, 1].surface.soft_cfm = 0.010f;
            m_materialContacts[(int)Material.Wood, 1].surface.soft_erp = 0.010f;

            m_materialContacts[(int)Material.Flesh, 0] = new SafeNativeMethods.Contact();
            m_materialContacts[(int)Material.Flesh, 0].surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            m_materialContacts[(int)Material.Flesh, 0].surface.mu = nmAvatarObjectContactFriction;
            m_materialContacts[(int)Material.Flesh, 0].surface.bounce = nmAvatarObjectContactBounce;
            m_materialContacts[(int)Material.Flesh, 0].surface.soft_cfm = 0.010f;
            m_materialContacts[(int)Material.Flesh, 0].surface.soft_erp = 0.010f;

            m_materialContacts[(int)Material.Flesh, 1] = new SafeNativeMethods.Contact();
            m_materialContacts[(int)Material.Flesh, 1].surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            m_materialContacts[(int)Material.Flesh, 1].surface.mu = mAvatarObjectContactFriction;
            m_materialContacts[(int)Material.Flesh, 1].surface.bounce = mAvatarObjectContactBounce;
            m_materialContacts[(int)Material.Flesh, 1].surface.soft_cfm = 0.010f;
            m_materialContacts[(int)Material.Flesh, 1].surface.soft_erp = 0.010f;

            m_materialContacts[(int)Material.Plastic, 0] = new SafeNativeMethods.Contact();
            m_materialContacts[(int)Material.Plastic, 0].surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            m_materialContacts[(int)Material.Plastic, 0].surface.mu = nmAvatarObjectContactFriction;
            m_materialContacts[(int)Material.Plastic, 0].surface.bounce = nmAvatarObjectContactBounce;
            m_materialContacts[(int)Material.Plastic, 0].surface.soft_cfm = 0.010f;
            m_materialContacts[(int)Material.Plastic, 0].surface.soft_erp = 0.010f;

            m_materialContacts[(int)Material.Plastic, 1] = new SafeNativeMethods.Contact();
            m_materialContacts[(int)Material.Plastic, 1].surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            m_materialContacts[(int)Material.Plastic, 1].surface.mu = mAvatarObjectContactFriction;
            m_materialContacts[(int)Material.Plastic, 1].surface.bounce = mAvatarObjectContactBounce;
            m_materialContacts[(int)Material.Plastic, 1].surface.soft_cfm = 0.010f;
            m_materialContacts[(int)Material.Plastic, 1].surface.soft_erp = 0.010f;

            m_materialContacts[(int)Material.Rubber, 0] = new SafeNativeMethods.Contact();
            m_materialContacts[(int)Material.Rubber, 0].surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            m_materialContacts[(int)Material.Rubber, 0].surface.mu = nmAvatarObjectContactFriction;
            m_materialContacts[(int)Material.Rubber, 0].surface.bounce = nmAvatarObjectContactBounce;
            m_materialContacts[(int)Material.Rubber, 0].surface.soft_cfm = 0.010f;
            m_materialContacts[(int)Material.Rubber, 0].surface.soft_erp = 0.010f;

            m_materialContacts[(int)Material.Rubber, 1] = new SafeNativeMethods.Contact();
            m_materialContacts[(int)Material.Rubber, 1].surface.mode |= SafeNativeMethods.ContactFlags.SoftERP;
            m_materialContacts[(int)Material.Rubber, 1].surface.mu = mAvatarObjectContactFriction;
            m_materialContacts[(int)Material.Rubber, 1].surface.bounce = mAvatarObjectContactBounce;
            m_materialContacts[(int)Material.Rubber, 1].surface.soft_cfm = 0.010f;
            m_materialContacts[(int)Material.Rubber, 1].surface.soft_erp = 0.010f;

            SafeNativeMethods.HashSpaceSetLevels(space, HashspaceLow, HashspaceHigh);

            // Set the gravity,, don't disable things automatically (we set it explicitly on some things)

            SafeNativeMethods.WorldSetGravity(world, gravityx, gravityy, gravityz);
            SafeNativeMethods.WorldSetContactSurfaceLayer(world, contactsurfacelayer);

            SafeNativeMethods.WorldSetLinearDamping(world, 256f);
            SafeNativeMethods.WorldSetAngularDamping(world, 256f);
            SafeNativeMethods.WorldSetAngularDampingThreshold(world, 256f);
            SafeNativeMethods.WorldSetLinearDampingThreshold(world, 256f);
            SafeNativeMethods.WorldSetMaxAngularSpeed(world, 256f);

            SafeNativeMethods.WorldSetQuickStepNumIterations(world, m_physicsiterations);
            //d.WorldSetContactMaxCorrectingVel(world, 1000.0f);

            for (int i = 0; i < staticPrimspace.GetLength(0); i++)
            {
                for (int j = 0; j < staticPrimspace.GetLength(1); j++)
                {
                    staticPrimspace[i, j] = IntPtr.Zero;
                }
            }

            _worldInitialized = true;
        }

        #region Collision Detection

        /// <summary>
        /// Collides two geometries.
        /// </summary>
        /// <returns></returns>
        /// <param name='geom1'></param>
        /// <param name='geom2'>/param>
        /// <param name='maxContacts'></param>
        /// <param name='contactsArray'></param>
        /// <param name='contactGeomSize'></param>
        private int CollideGeoms(
            IntPtr geom1, IntPtr geom2, int maxContacts, SafeNativeMethods.ContactGeom[] contactsArray, int contactGeomSize)
        {
            int count;

            lock (OdeScene.UniversalColliderSyncObject)
            {
                // We do this inside the lock so that we don't count any delay in acquiring it
                if (CollectStats)
                    m_nativeCollisionStartTick = Util.EnvironmentTickCount();

                count = SafeNativeMethods.Collide(geom1, geom2, maxContacts, contactsArray, contactGeomSize);
            }

            // We do this outside the lock so that any waiting threads aren't held up, though the effect is probably
            // negligable
            if (CollectStats)
                m_stats[ODENativeGeomCollisionFrameMsStatName]
                    += Util.EnvironmentTickCountSubtract(m_nativeCollisionStartTick);

            return count;
        }

        /// <summary>
        /// Collide two spaces or a space and a geometry.
        /// </summary>
        /// <param name='space1'></param>
        /// <param name='space2'>/param>
        /// <param name='data'></param>
        private void CollideSpaces(IntPtr space1, IntPtr space2, IntPtr data)
        {
            if (CollectStats)
            {
                m_inCollisionTiming = true;
                m_nativeCollisionStartTick = Util.EnvironmentTickCount();
            }

            SafeNativeMethods.SpaceCollide2(space1, space2, data, nearCallback);

            if (CollectStats && m_inCollisionTiming)
            {
                m_stats[ODENativeSpaceCollisionFrameMsStatName]
                    += Util.EnvironmentTickCountSubtract(m_nativeCollisionStartTick);
                m_inCollisionTiming = false;
            }
        }

        /// <summary>
        /// This is our near callback.  A geometry is near a body
        /// </summary>
        /// <param name="space">The space that contains the geoms.  Remember, spaces are also geoms</param>
        /// <param name="g1">a geometry or space</param>
        /// <param name="g2">another geometry or space</param>
        private void near(IntPtr space, IntPtr g1, IntPtr g2)
        {
            if (CollectStats && m_inCollisionTiming)
            {
                m_stats[ODENativeSpaceCollisionFrameMsStatName]
                    += Util.EnvironmentTickCountSubtract(m_nativeCollisionStartTick);
                m_inCollisionTiming = false;
            }

//            m_log.DebugFormat("[PHYSICS]: Colliding {0} and {1} in {2}", g1, g2, space);
            //  no lock here!  It's invoked from within Simulate(), which is thread-locked

            // Test if we're colliding a geom with a space.
            // If so we have to drill down into the space recursively

            if (SafeNativeMethods.GeomIsSpace(g1) || SafeNativeMethods.GeomIsSpace(g2))
            {
                if (g1 == IntPtr.Zero || g2 == IntPtr.Zero)
                    return;

                // Separating static prim geometry spaces.
                // We'll be calling near recursivly if one
                // of them is a space to find all of the
                // contact points in the space
                try
                {
                    CollideSpaces(g1, g2, IntPtr.Zero);
                }
                catch (AccessViolationException)
                {
                    m_log.Error("[ODE SCENE]: Unable to collide test a space");
                    return;
                }
                //Colliding a space or a geom with a space or a geom. so drill down

                //Collide all geoms in each space..
                //if (d.GeomIsSpace(g1)) d.SpaceCollide(g1, IntPtr.Zero, nearCallback);
                //if (d.GeomIsSpace(g2)) d.SpaceCollide(g2, IntPtr.Zero, nearCallback);
                return;
            }

            if (g1 == IntPtr.Zero || g2 == IntPtr.Zero)
                return;

            IntPtr b1 = SafeNativeMethods.GeomGetBody(g1);
            IntPtr b2 = SafeNativeMethods.GeomGetBody(g2);

            // d.GeomClassID id = d.GeomGetClass(g1);

            String name1 = null;
            String name2 = null;

            if (!geom_name_map.TryGetValue(g1, out name1))
            {
                name1 = "null";
            }
            if (!geom_name_map.TryGetValue(g2, out name2))
            {
                name2 = "null";
            }

            // Figure out how many contact points we have
            int count = 0;

            try
            {
                // Colliding Geom To Geom
                // This portion of the function 'was' blatantly ripped off from BoxStack.cs

                if (g1 == g2)
                    return; // Can't collide with yourself

                if (b1 != IntPtr.Zero && b2 != IntPtr.Zero && SafeNativeMethods.AreConnectedExcluding(b1, b2, SafeNativeMethods.JointType.Contact))
                    return;

                count = CollideGeoms(g1, g2, contacts.Length, contacts, SafeNativeMethods.ContactGeom.unmanagedSizeOf);

                // All code after this is only relevant if we have any collisions
                if (count <= 0)
                    return;

                if (count > contacts.Length)
                    m_log.Error("[ODE SCENE]: Got " + count + " contacts when we asked for a maximum of " + contacts.Length);
            }
            catch (SEHException)
            {
                m_log.Error(
                    "[ODE SCENE]: The Operating system shut down ODE because of corrupt memory.  This could be a result of really irregular terrain.  If this repeats continuously, restart using Basic Physics and terrain fill your terrain.  Restarting the sim.");
                base.TriggerPhysicsBasedRestart();
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[ODE SCENE]: Unable to collide test an object: {0}", e.Message);
                return;
            }

            PhysicsActor p1;
            PhysicsActor p2;

            p1ExpectedPoints = 0;
            p2ExpectedPoints = 0;

            if (!actor_name_map.TryGetValue(g1, out p1))
            {
                p1 = PANull;
            }

            if (!actor_name_map.TryGetValue(g2, out p2))
            {
                p2 = PANull;
            }

            ContactPoint maxDepthContact = new ContactPoint();
            if (p1.CollisionScore + count >= float.MaxValue)
                p1.CollisionScore = 0;
            p1.CollisionScore += count;

            if (p2.CollisionScore + count >= float.MaxValue)
                p2.CollisionScore = 0;
            p2.CollisionScore += count;

            for (int i = 0; i < count; i++)
            {
                SafeNativeMethods.ContactGeom curContact = contacts[i];

                if (curContact.depth > maxDepthContact.PenetrationDepth)
                {
                    maxDepthContact = new ContactPoint(
                        new Vector3(curContact.pos.X, curContact.pos.Y, curContact.pos.Z),
                        new Vector3(curContact.normal.X, curContact.normal.Y, curContact.normal.Z),
                        curContact.depth
                    );
                }

                //m_log.Warn("[CCOUNT]: " + count);
                IntPtr joint;
                // If we're colliding with terrain, use 'TerrainContact' instead of contact.
                // allows us to have different settings

                // We only need to test p2 for 'jump crouch purposes'
                if (p2 is OdeCharacter && p1.PhysicsActorType == (int)ActorTypes.Prim)
                {
                    // Testing if the collision is at the feet of the avatar

                    //m_log.DebugFormat("[PHYSICS]: {0} - {1} - {2} - {3}", curContact.pos.Z, p2.Position.Z, (p2.Position.Z - curContact.pos.Z), (p2.Size.Z * 0.6f));
                    if ((p2.Position.Z - curContact.pos.Z) > (p2.Size.Z * 0.6f))
                        p2.IsColliding = true;
                }
                else
                {
                    p2.IsColliding = true;
                }

                //if ((framecount % m_returncollisions) == 0)

                switch (p1.PhysicsActorType)
                {
                    case (int)ActorTypes.Agent:
                        p1ExpectedPoints = avatarExpectedContacts;
                        p2.CollidingObj = true;
                        break;
                    case (int)ActorTypes.Prim:
                        if (p1 != null && p1 is OdePrim)
                            p1ExpectedPoints = ((OdePrim) p1).ExpectedCollisionContacts;

                        if (p2.Velocity.LengthSquared() > 0.0f)
                            p2.CollidingObj = true;
                        break;
                    case (int)ActorTypes.Unknown:
                        p2.CollidingGround = true;
                        break;
                    default:
                        p2.CollidingGround = true;
                        break;
                }

                // we don't want prim or avatar to explode

                #region InterPenetration Handling - Unintended physics explosions

                if (curContact.depth >= 0.08f)
                {
                    if (curContact.depth >= 1.00f)
                    {
                        //m_log.Info("[P]: " + contact.depth.ToString());
                        if ((p2.PhysicsActorType == (int) ActorTypes.Agent &&
                             p1.PhysicsActorType == (int) ActorTypes.Unknown) ||
                            (p1.PhysicsActorType == (int) ActorTypes.Agent &&
                             p2.PhysicsActorType == (int) ActorTypes.Unknown))
                        {
                            if (p2.PhysicsActorType == (int) ActorTypes.Agent)
                            {
                                if (p2 is OdeCharacter)
                                {
                                    OdeCharacter character = (OdeCharacter) p2;

                                    //p2.CollidingObj = true;
                                    curContact.depth = 0.00000003f;
                                    p2.Velocity = p2.Velocity + new Vector3(0f, 0f, 0.5f);
                                    curContact.pos =
                                        new SafeNativeMethods.Vector3(curContact.pos.X + (p1.Size.X/2),
                                                      curContact.pos.Y + (p1.Size.Y/2),
                                                      curContact.pos.Z + (p1.Size.Z/2));
                                    character.SetPidStatus(true);
                                }
                            }

                            if (p1.PhysicsActorType == (int) ActorTypes.Agent)
                            {
                                if (p1 is OdeCharacter)
                                {
                                    OdeCharacter character = (OdeCharacter) p1;

                                    //p2.CollidingObj = true;
                                    curContact.depth = 0.00000003f;
                                    p1.Velocity = p1.Velocity + new Vector3(0f, 0f, 0.5f);
                                    curContact.pos =
                                        new SafeNativeMethods.Vector3(curContact.pos.X + (p1.Size.X/2),
                                                      curContact.pos.Y + (p1.Size.Y/2),
                                                      curContact.pos.Z + (p1.Size.Z/2));
                                    character.SetPidStatus(true);
                                }
                            }
                        }
                    }
                }

                #endregion

                // Logic for collision handling
                // Note, that if *all* contacts are skipped (VolumeDetect)
                // The prim still detects (and forwards) collision events but
                // appears to be phantom for the world
                Boolean skipThisContact = false;

                if ((p1 is OdePrim) && (((OdePrim)p1).m_isVolumeDetect))
                    skipThisContact = true;   // No collision on volume detect prims

                if (av_av_collisions_off)
                    if ((p1 is OdeCharacter) && (p2 is OdeCharacter))
                        skipThisContact = true;

                if (!skipThisContact && (p2 is OdePrim) && (((OdePrim)p2).m_isVolumeDetect))
                    skipThisContact = true;   // No collision on volume detect prims

                if (!skipThisContact && curContact.depth < 0f)
                    skipThisContact = true;

                if (!skipThisContact && checkDupe(curContact, p2.PhysicsActorType))
                    skipThisContact = true;

                const int maxContactsbeforedeath = 4000;
                joint = IntPtr.Zero;

                if (!skipThisContact)
                {
                    _perloopContact.Add(curContact);

                    if (name1 == "Terrain" || name2 == "Terrain")
                    {
                        if ((p2.PhysicsActorType == (int) ActorTypes.Agent) &&
                            (Math.Abs(p2.Velocity.X) > 0.01f || Math.Abs(p2.Velocity.Y) > 0.01f))
                        {
                            p2ExpectedPoints = avatarExpectedContacts;
                            // Avatar is moving on terrain, use the movement terrain contact
                            AvatarMovementTerrainContact.geom = curContact;

                            if (m_global_contactcount < maxContactsbeforedeath)
                            {
                                joint = SafeNativeMethods.JointCreateContact(world, contactgroup, ref AvatarMovementTerrainContact);
                                m_global_contactcount++;
                            }
                        }
                        else
                        {
                            if (p2.PhysicsActorType == (int)ActorTypes.Agent)
                            {
                                p2ExpectedPoints = avatarExpectedContacts;
                                // Avatar is standing on terrain, use the non moving terrain contact
                                TerrainContact.geom = curContact;

                                if (m_global_contactcount < maxContactsbeforedeath)
                                {
                                    joint = SafeNativeMethods.JointCreateContact(world, contactgroup, ref TerrainContact);
                                    m_global_contactcount++;
                                }
                            }
                            else
                            {
                                if (p2.PhysicsActorType == (int)ActorTypes.Prim && p1.PhysicsActorType == (int)ActorTypes.Prim)
                                {
                                    // prim prim contact
                                    // int pj294950 = 0;
                                    int movintYN = 0;
                                    int material = (int) Material.Wood;
                                    // prim terrain contact
                                    if (Math.Abs(p2.Velocity.X) > 0.01f || Math.Abs(p2.Velocity.Y) > 0.01f)
                                    {
                                        movintYN = 1;
                                    }

                                    if (p2 is OdePrim)
                                    {
                                        material = ((OdePrim) p2).m_material;
                                        p2ExpectedPoints = ((OdePrim)p2).ExpectedCollisionContacts;
                                    }

                                    // Unnessesary because p1 is defined above
                                    //if (p1 is OdePrim)
                                    // {
                                    //     p1ExpectedPoints = ((OdePrim)p1).ExpectedCollisionContacts;
                                    // }
                                    //m_log.DebugFormat("Material: {0}", material);

                                    m_materialContacts[material, movintYN].geom = curContact;

                                    if (m_global_contactcount < maxContactsbeforedeath)
                                    {
                                        joint = SafeNativeMethods.JointCreateContact(world, contactgroup, ref m_materialContacts[material, movintYN]);
                                        m_global_contactcount++;
                                    }
                                }
                                else
                                {
                                    int movintYN = 0;
                                    // prim terrain contact
                                    if (Math.Abs(p2.Velocity.X) > 0.01f || Math.Abs(p2.Velocity.Y) > 0.01f)
                                    {
                                        movintYN = 1;
                                    }

                                    int material = (int)Material.Wood;

                                    if (p2 is OdePrim)
                                    {
                                        material = ((OdePrim)p2).m_material;
                                        p2ExpectedPoints = ((OdePrim)p2).ExpectedCollisionContacts;
                                    }

                                    //m_log.DebugFormat("Material: {0}", material);
                                    m_materialContacts[material, movintYN].geom = curContact;

                                    if (m_global_contactcount < maxContactsbeforedeath)
                                    {
                                        joint = SafeNativeMethods.JointCreateContact(world, contactgroup, ref m_materialContacts[material, movintYN]);
                                        m_global_contactcount++;
                                    }
                                }
                            }
                        }
                        //if (p2.PhysicsActorType == (int)ActorTypes.Prim)
                        //{
                        //m_log.Debug("[PHYSICS]: prim contacting with ground");
                        //}
                    }
                    else if (name1 == "Water" || name2 == "Water")
                    {
                        /*
                        if ((p2.PhysicsActorType == (int) ActorTypes.Prim))
                        {
                        }
                        else
                        {
                        }
                        */
                        //WaterContact.surface.soft_cfm = 0.0000f;
                        //WaterContact.surface.soft_erp = 0.00000f;
                        if (curContact.depth > 0.1f)
                        {
                            curContact.depth *= 52;
                            //contact.normal = new d.Vector3(0, 0, 1);
                            //contact.pos = new d.Vector3(0, 0, contact.pos.Z - 5f);
                        }

                        WaterContact.geom = curContact;

                        if (m_global_contactcount < maxContactsbeforedeath)
                        {
                            joint = SafeNativeMethods.JointCreateContact(world, contactgroup, ref WaterContact);
                            m_global_contactcount++;
                        }
                        //m_log.Info("[PHYSICS]: Prim Water Contact" + contact.depth);
                    }
                    else
                    {
                        if ((p2.PhysicsActorType == (int)ActorTypes.Agent))
                        {
                            p2ExpectedPoints = avatarExpectedContacts;
                            if ((Math.Abs(p2.Velocity.X) > 0.01f || Math.Abs(p2.Velocity.Y) > 0.01f))
                            {
                                // Avatar is moving on a prim, use the Movement prim contact
                                AvatarMovementprimContact.geom = curContact;

                                if (m_global_contactcount < maxContactsbeforedeath)
                                {
                                    joint = SafeNativeMethods.JointCreateContact(world, contactgroup, ref AvatarMovementprimContact);
                                    m_global_contactcount++;
                                }
                            }
                            else
                            {
                                // Avatar is standing still on a prim, use the non movement contact
                                contact.geom = curContact;

                                if (m_global_contactcount < maxContactsbeforedeath)
                                {
                                    joint = SafeNativeMethods.JointCreateContact(world, contactgroup, ref contact);
                                    m_global_contactcount++;
                                }
                            }
                        }
                        else if (p2.PhysicsActorType == (int)ActorTypes.Prim)
                        {
                            //p1.PhysicsActorType
                            int material = (int)Material.Wood;

                            if (p2 is OdePrim)
                            {
                                material = ((OdePrim)p2).m_material;
                                p2ExpectedPoints = ((OdePrim)p2).ExpectedCollisionContacts;
                            }

                            //m_log.DebugFormat("Material: {0}", material);
                            m_materialContacts[material, 0].geom = curContact;

                            if (m_global_contactcount < maxContactsbeforedeath)
                            {
                                joint = SafeNativeMethods.JointCreateContact(world, contactgroup, ref m_materialContacts[material, 0]);
                                m_global_contactcount++;
                            }
                        }
                    }

                    if (m_global_contactcount < maxContactsbeforedeath && joint != IntPtr.Zero) // stack collide!
                    {
                        SafeNativeMethods.JointAttach(joint, b1, b2);
                        m_global_contactcount++;
                    }
                }

                collision_accounting_events(p1, p2, maxDepthContact);

                if (count > ((p1ExpectedPoints + p2ExpectedPoints) * 0.25) + (geomContactPointsStartthrottle))
                {
                    // If there are more then 3 contact points, it's likely
                    // that we've got a pile of objects, so ...
                    // We don't want to send out hundreds of terse updates over and over again
                    // so lets throttle them and send them again after it's somewhat sorted out.
                    p2.ThrottleUpdates = true;
                }
                //m_log.Debug(count.ToString());
                //m_log.Debug("near: A collision was detected between {1} and {2}", 0, name1, name2);
            }
        }

        private bool checkDupe(SafeNativeMethods.ContactGeom contactGeom, int atype)
        {
            if (!m_filterCollisions)
                return false;

            bool result = false;

            ActorTypes at = (ActorTypes)atype;

            foreach (SafeNativeMethods.ContactGeom contact in _perloopContact)
            {
                //if ((contact.g1 == contactGeom.g1 && contact.g2 == contactGeom.g2))
                //{
                    // || (contact.g2 == contactGeom.g1 && contact.g1 == contactGeom.g2)
                if (at == ActorTypes.Agent)
                {
                    if (((Math.Abs(contactGeom.normal.X - contact.normal.X) < 1.026f)
                        && (Math.Abs(contactGeom.normal.Y - contact.normal.Y) < 0.303f)
                        && (Math.Abs(contactGeom.normal.Z - contact.normal.Z) < 0.065f)))
                    {
                        if (Math.Abs(contact.depth - contactGeom.depth) < 0.052f)
                        {
                             result = true;
                             break;
                        }
                    }
                }
                else if (at == ActorTypes.Prim)
                {
                    if (((Math.Abs(contactGeom.normal.X - contact.normal.X) < 1.026f) && (Math.Abs(contactGeom.normal.Y - contact.normal.Y) < 0.303f) && (Math.Abs(contactGeom.normal.Z - contact.normal.Z) < 0.065f)))
                    {
                        if (contactGeom.normal.X == contact.normal.X && contactGeom.normal.Y == contact.normal.Y && contactGeom.normal.Z == contact.normal.Z)
                        {
                            if (Math.Abs(contact.depth - contactGeom.depth) < 0.272f)
                            {
                                result = true;
                                break;
                            }
                        }
                        //m_log.DebugFormat("[Collision]: Depth {0}", Math.Abs(contact.depth - contactGeom.depth));
                        //m_log.DebugFormat("[Collision]: <{0},{1},{2}>", Math.Abs(contactGeom.normal.X - contact.normal.X), Math.Abs(contactGeom.normal.Y - contact.normal.Y), Math.Abs(contactGeom.normal.Z - contact.normal.Z));
                    }
                }
            }

            return result;
        }

        private void collision_accounting_events(PhysicsActor p1, PhysicsActor p2, ContactPoint contact)
        {
            // obj1LocalID = 0;
            //returncollisions = false;
            obj2LocalID = 0;
            //ctype = 0;
            //cStartStop = 0;
//            if (!p2.SubscribedEvents() && !p1.SubscribedEvents())
//                return;
            bool p1events = p1.SubscribedEvents();
            bool p2events = p2.SubscribedEvents();

            if (p1.IsVolumeDtc)
                p2events = false;
            if (p2.IsVolumeDtc)
                p1events = false;

            if (!p2events && !p1events)
                return;

            Vector3 vel = Vector3.Zero;
            if (p2 != null && p2.IsPhysical)
                vel = p2.Velocity;

            if (p1 != null && p1.IsPhysical)
                vel -= p1.Velocity;

            contact.RelativeSpeed = Vector3.Dot(vel, contact.SurfaceNormal);

            switch ((ActorTypes)p2.PhysicsActorType)
            {
                case ActorTypes.Agent:
                    cc2 = (OdeCharacter)p2;

                    // obj1LocalID = cc2.m_localID;
                    switch ((ActorTypes)p1.PhysicsActorType)
                    {
                        case ActorTypes.Agent:
                            cc1 = (OdeCharacter)p1;
                            obj2LocalID = cc1.LocalID;
                            cc1.AddCollisionEvent(cc2.LocalID, contact);
                            break;

                        case ActorTypes.Prim:
                            if (p1 is OdePrim)
                            {
                                cp1 = (OdePrim) p1;
                                obj2LocalID = cp1.LocalID;
                                cp1.AddCollisionEvent(cc2.LocalID, contact);
                            }
                            break;

                        case ActorTypes.Ground:
                        case ActorTypes.Unknown:
                            obj2LocalID = 0;
                            break;
                    }

                    cc2.AddCollisionEvent(obj2LocalID, contact);
                    break;

                case ActorTypes.Prim:

                    if (p2 is OdePrim)
                    {
                        cp2 = (OdePrim) p2;

                        // obj1LocalID = cp2.m_localID;
                        switch ((ActorTypes) p1.PhysicsActorType)
                        {
                            case ActorTypes.Agent:
                                if (p1 is OdeCharacter)
                                {
                                    cc1 = (OdeCharacter) p1;
                                    obj2LocalID = cc1.LocalID;
                                    cc1.AddCollisionEvent(cp2.LocalID, contact);
                                }
                                break;
                            case ActorTypes.Prim:

                                if (p1 is OdePrim)
                                {
                                    cp1 = (OdePrim) p1;
                                    obj2LocalID = cp1.LocalID;
                                    cp1.AddCollisionEvent(cp2.LocalID, contact);
                                }
                                break;

                            case ActorTypes.Ground:
                            case ActorTypes.Unknown:
                                obj2LocalID = 0;
                                break;
                        }

                        cp2.AddCollisionEvent(obj2LocalID, contact);
                    }
                    break;
            }
        }
        /// <summary>
        /// This is our collision testing routine in ODE
        /// </summary>
        private void collision_optimized()
        {
            _perloopContact.Clear();

            foreach (OdeCharacter chr in _characters)
            {
                // Reset the collision values to false
                // since we don't know if we're colliding yet
                if (chr.Shell == IntPtr.Zero || chr.Body == IntPtr.Zero)
                    continue;

                chr.IsColliding = false;
                chr.CollidingGround = false;
                chr.CollidingObj = false;

                // Test the avatar's geometry for collision with the space
                // This will return near and the space that they are the closest to
                // And we'll run this again against the avatar and the space segment
                // This will return with a bunch of possible objects in the space segment
                // and we'll run it again on all of them.
                try
                {
                    CollideSpaces(space, chr.Shell, IntPtr.Zero);
                }
                catch (AccessViolationException)
                {
                    m_log.ErrorFormat("[ODE SCENE]: Unable to space collide {0}", PhysicsSceneName);
                }

                //float terrainheight = GetTerrainHeightAtXY(chr.Position.X, chr.Position.Y);
                //if (chr.Position.Z + (chr.Velocity.Z * timeStep) < terrainheight + 10)
                //{
                //chr.Position.Z = terrainheight + 10.0f;
                //forcedZ = true;
                //}
            }

            if (CollectStats)
            {
                m_tempAvatarCollisionsThisFrame = _perloopContact.Count;
                m_stats[ODEAvatarContactsStatsName] += m_tempAvatarCollisionsThisFrame;
            }

            List<OdePrim> removeprims = null;
            foreach (OdePrim chr in _activeprims)
            {
                if (chr.Body != IntPtr.Zero && SafeNativeMethods.BodyIsEnabled(chr.Body) && (!chr.m_disabled))
                {
                    try
                    {
                        lock (chr)
                        {
                            if (space != IntPtr.Zero && chr.prim_geom != IntPtr.Zero && chr.m_taintremove == false)
                            {
                                CollideSpaces(space, chr.prim_geom, IntPtr.Zero);
                            }
                            else
                            {
                                if (removeprims == null)
                                {
                                    removeprims = new List<OdePrim>();
                                }
                                removeprims.Add(chr);
                                m_log.Error(
                                    "[ODE SCENE]: unable to collide test active prim against space.  The space was zero, the geom was zero or it was in the process of being removed.  Removed it from the active prim list.  This needs to be fixed!");
                            }
                        }
                    }
                    catch (AccessViolationException)
                    {
                        m_log.Error("[ODE SCENE]: Unable to space collide");
                    }
                }
            }

            if (CollectStats)
                m_stats[ODEPrimContactsStatName] += _perloopContact.Count - m_tempAvatarCollisionsThisFrame;

            if (removeprims != null)
            {
                foreach (OdePrim chr in removeprims)
                {
                    _activeprims.Remove(chr);
                }
            }
        }

        #endregion

        // Recovered for use by fly height. Kitto Flora
        internal float GetTerrainHeightAtXY(float x, float y)
        {
            IntPtr heightFieldGeom = IntPtr.Zero;
            int offsetX = 0;
            int offsetY = 0;

            if(RegionTerrain.TryGetValue(new Vector3(offsetX,offsetY,0), out heightFieldGeom))
            {
                if (heightFieldGeom != IntPtr.Zero)
                {
                    if (TerrainHeightFieldHeights.ContainsKey(heightFieldGeom))
                    {

                        int index;


                        if ((int)x > WorldExtents.X || (int)y > WorldExtents.Y ||
                            (int)x < 0.001f || (int)y < 0.001f)
                            return 0;

                        x = x - offsetX + 1f;
                        y = y - offsetY + 1f;

                        // map is rotated
                        index = (int)x * ((int)m_regionHeight + 3) + (int)y;

                        if (index < TerrainHeightFieldHeights[heightFieldGeom].Length)
                        {
                            //m_log.DebugFormat("x{0} y{1} = {2}", x, y, (float)TerrainHeightFieldHeights[heightFieldGeom][index]);
                            return (float)TerrainHeightFieldHeights[heightFieldGeom][index];
                        }

                        else
                            return 0f;
                    }
                    else
                    {
                        return 0f;
                    }

                }
                else
                {
                    return 0f;
                }

            }
            else
            {
                return 0f;
            }
        }
// End recovered. Kitto Flora

        /// <summary>
        /// Add actor to the list that should receive collision events in the simulate loop.
        /// </summary>
        /// <param name="obj"></param>
        internal void AddCollisionEventReporting(PhysicsActor obj)
        {
//            m_log.DebugFormat("[PHYSICS]: Adding {0} {1} to collision event reporting", obj.SOPName, obj.LocalID);

            lock (m_collisionEventActorsChanges)
                m_collisionEventActorsChanges[obj.LocalID] = obj;
        }

        /// <summary>
        /// Remove actor from the list that should receive collision events in the simulate loop.
        /// </summary>
        /// <param name="obj"></param>
        internal void RemoveCollisionEventReporting(PhysicsActor obj)
        {
//            m_log.DebugFormat("[PHYSICS]: Removing {0} {1} from collision event reporting", obj.SOPName, obj.LocalID);

            lock (m_collisionEventActorsChanges)
                m_collisionEventActorsChanges[obj.LocalID] = null;
        }

        #region Add/Remove Entities

        public override PhysicsActor AddAvatar(string avName, Vector3 position, Vector3 velocity, Vector3 size, bool isFlying)
        {
            SafeNativeMethods.AllocateODEDataForThread(0);

            OdeCharacter newAv
                = new OdeCharacter(
                    avName, this, position, velocity, size, avPIDD, avPIDP,
                    avCapRadius, avStandupTensor, avDensity,
                    avMovementDivisorWalk, avMovementDivisorRun);

            newAv.Flying = isFlying;
            newAv.MinimumGroundFlightOffset = minimumGroundFlightOffset;
            newAv.m_avatarplanted = avplanted;

            return newAv;
        }

        public override void RemoveAvatar(PhysicsActor actor)
        {
//            m_log.DebugFormat(
//                "[ODE SCENE]: Removing physics character {0} {1} from physics scene {2}",
//                actor.Name, actor.LocalID, Name);

            lock (OdeLock)
            {
                SafeNativeMethods.AllocateODEDataForThread(0);

                ((OdeCharacter) actor).Destroy();
            }
        }

        internal void AddCharacter(OdeCharacter chr)
        {
            chr.m_avatarplanted = avplanted;
            if (!_characters.Contains(chr))
            {
                _characters.Add(chr);

//                m_log.DebugFormat(
//                    "[ODE SCENE]: Adding physics character {0} {1} to physics scene {2}.  Count now {3}",
//                    chr.Name, chr.LocalID, Name, _characters.Count);

                if (chr.bad)
                    m_log.ErrorFormat("[ODE SCENE]: Added BAD actor {0} to characters list", chr.m_uuid);
            }
            else
            {
                m_log.ErrorFormat(
                    "[ODE SCENE]: Tried to add character {0} {1} but they are already in the set!",
                    chr.Name, chr.LocalID);
            }
        }

        internal void RemoveCharacter(OdeCharacter chr)
        {
            if (_characters.Contains(chr))
            {
                _characters.Remove(chr);

//                m_log.DebugFormat(
//                    "[ODE SCENE]: Removing physics character {0} {1} from physics scene {2}.  Count now {3}",
//                    chr.Name, chr.LocalID, Name, _characters.Count);
            }
            else
            {
                m_log.ErrorFormat(
                    "[ODE SCENE]: Tried to remove character {0} {1} but they are not in the list!",
                    chr.Name, chr.LocalID);
            }
        }

        private PhysicsActor AddPrim(String name, Vector3 position, Vector3 size, Quaternion rotation,
                                     PrimitiveBaseShape pbs, bool isphysical, uint localID)
        {
            Vector3 pos = position;
            Vector3 siz = size;
            Quaternion rot = rotation;


            OdePrim newPrim;
            lock (OdeLock)
            {
                SafeNativeMethods.AllocateODEDataForThread(0);
                newPrim = new OdePrim(name, this, pos, siz, rot, pbs, isphysical);

                lock (_prims)
                    _prims.Add(newPrim);
            }
            newPrim.LocalID = localID;
            return newPrim;
        }

        /// <summary>
        /// Make this prim subject to physics.
        /// </summary>
        /// <param name="prim"></param>
        internal void ActivatePrim(OdePrim prim)
        {
            // adds active prim..   (ones that should be iterated over in collisions_optimized
            if (!_activeprims.Contains(prim))
                _activeprims.Add(prim);
            //else
              //  m_log.Warn("[PHYSICS]: Double Entry in _activeprims detected, potential crash immenent");
        }

        public override PhysicsActor AddPrimShape(string primName, PrimitiveBaseShape pbs, Vector3 position,
                                                  Vector3 size, Quaternion rotation, bool isPhysical, uint localid)
        {
//            m_log.DebugFormat("[ODE SCENE]: Adding physics prim {0} {1} to physics scene {2}", primName, localid, Name);

            return AddPrim(primName, position, size, rotation, pbs, isPhysical, localid);
        }

        public override float TimeDilation
        {
            get { return m_timeDilation; }
        }

        public override bool SupportsNINJAJoints
        {
            get { return m_NINJA_physics_joints_enabled; }
        }

        // internal utility function: must be called within a lock (OdeLock)
        private void InternalAddActiveJoint(PhysicsJoint joint)
        {
            activeJoints.Add(joint);
            SOPName_to_activeJoint.Add(joint.ObjectNameInScene, joint);
        }

        // internal utility function: must be called within a lock (OdeLock)
        private void InternalAddPendingJoint(OdePhysicsJoint joint)
        {
            pendingJoints.Add(joint);
            SOPName_to_pendingJoint.Add(joint.ObjectNameInScene, joint);
        }

        // internal utility function: must be called within a lock (OdeLock)
        private void InternalRemovePendingJoint(PhysicsJoint joint)
        {
            pendingJoints.Remove(joint);
            SOPName_to_pendingJoint.Remove(joint.ObjectNameInScene);
        }

        // internal utility function: must be called within a lock (OdeLock)
        private void InternalRemoveActiveJoint(PhysicsJoint joint)
        {
            activeJoints.Remove(joint);
            SOPName_to_activeJoint.Remove(joint.ObjectNameInScene);
        }

        public override void DumpJointInfo()
        {
            string hdr = "[NINJA] JOINTINFO: ";
            foreach (PhysicsJoint j in pendingJoints)
            {
                m_log.Debug(hdr + " pending joint, Name: " + j.ObjectNameInScene + " raw parms:" + j.RawParams);
            }
            m_log.Debug(hdr + pendingJoints.Count + " total pending joints");
            foreach (string jointName in SOPName_to_pendingJoint.Keys)
            {
                m_log.Debug(hdr + " pending joints dict contains Name: " + jointName);
            }
            m_log.Debug(hdr + SOPName_to_pendingJoint.Keys.Count + " total pending joints dict entries");
            foreach (PhysicsJoint j in activeJoints)
            {
                m_log.Debug(hdr + " active joint, Name: " + j.ObjectNameInScene + " raw parms:" + j.RawParams);
            }
            m_log.Debug(hdr + activeJoints.Count + " total active joints");
            foreach (string jointName in SOPName_to_activeJoint.Keys)
            {
                m_log.Debug(hdr + " active joints dict contains Name: " + jointName);
            }
            m_log.Debug(hdr + SOPName_to_activeJoint.Keys.Count + " total active joints dict entries");

            m_log.Debug(hdr + " Per-body joint connectivity information follows.");
            m_log.Debug(hdr + joints_connecting_actor.Keys.Count + " bodies are connected by joints.");
            foreach (string actorName in joints_connecting_actor.Keys)
            {
                m_log.Debug(hdr + " Actor " + actorName + " has the following joints connecting it");
                foreach (PhysicsJoint j in joints_connecting_actor[actorName])
                {
                    m_log.Debug(hdr + " * joint Name: " + j.ObjectNameInScene + " raw parms:" + j.RawParams);
                }
                m_log.Debug(hdr + joints_connecting_actor[actorName].Count + " connecting joints total for this actor");
            }
        }

        public override void RequestJointDeletion(string ObjectNameInScene)
        {
            lock (externalJointRequestsLock)
            {
                if (!requestedJointsToBeDeleted.Contains(ObjectNameInScene)) // forbid same deletion request from entering twice to prevent spurious deletions processed asynchronously
                {
                    requestedJointsToBeDeleted.Add(ObjectNameInScene);
                }
            }
        }

        private void DeleteRequestedJoints()
        {
            List<string> myRequestedJointsToBeDeleted;
            lock (externalJointRequestsLock)
            {
                // make a local copy of the shared list for processing (threading issues)
                myRequestedJointsToBeDeleted = new List<string>(requestedJointsToBeDeleted);
            }

            foreach (string jointName in myRequestedJointsToBeDeleted)
            {
                lock (OdeLock)
                {
                    //m_log.Debug("[NINJA] trying to deleting requested joint " + jointName);
                    if (SOPName_to_activeJoint.ContainsKey(jointName) || SOPName_to_pendingJoint.ContainsKey(jointName))
                    {
                        OdePhysicsJoint joint = null;
                        if (SOPName_to_activeJoint.ContainsKey(jointName))
                        {
                            joint = SOPName_to_activeJoint[jointName] as OdePhysicsJoint;
                            InternalRemoveActiveJoint(joint);
                        }
                        else if (SOPName_to_pendingJoint.ContainsKey(jointName))
                        {
                            joint = SOPName_to_pendingJoint[jointName] as OdePhysicsJoint;
                            InternalRemovePendingJoint(joint);
                        }

                        if (joint != null)
                        {
                            //m_log.Debug("joint.BodyNames.Count is " + joint.BodyNames.Count + " and contents " + joint.BodyNames);
                            for (int iBodyName = 0; iBodyName < 2; iBodyName++)
                            {
                                string bodyName = joint.BodyNames[iBodyName];
                                if (bodyName != "NULL")
                                {
                                    joints_connecting_actor[bodyName].Remove(joint);
                                    if (joints_connecting_actor[bodyName].Count == 0)
                                    {
                                        joints_connecting_actor.Remove(bodyName);
                                    }
                                }
                            }

                            DoJointDeactivated(joint);
                            if (joint.jointID != IntPtr.Zero)
                            {
                                SafeNativeMethods.JointDestroy(joint.jointID);
                                joint.jointID = IntPtr.Zero;
                                //DoJointErrorMessage(joint, "successfully destroyed joint " + jointName);
                            }
                            else
                            {
                                //m_log.Warn("[NINJA] Ignoring re-request to destroy joint " + jointName);
                            }
                        }
                        else
                        {
                            // DoJointErrorMessage(joint, "coult not find joint to destroy based on name " + jointName);
                        }
                    }
                    else
                    {
                        // DoJointErrorMessage(joint, "WARNING - joint removal failed, joint " + jointName);
                    }
                }
            }

            // remove processed joints from the shared list
            lock (externalJointRequestsLock)
            {
                foreach (string jointName in myRequestedJointsToBeDeleted)
                {
                    requestedJointsToBeDeleted.Remove(jointName);
                }
            }
        }

        // for pending joints we don't know if their associated bodies exist yet or not.
        // the joint is actually created during processing of the taints
        private void CreateRequestedJoints()
        {
            List<PhysicsJoint> myRequestedJointsToBeCreated;
            lock (externalJointRequestsLock)
            {
                // make a local copy of the shared list for processing (threading issues)
                myRequestedJointsToBeCreated = new List<PhysicsJoint>(requestedJointsToBeCreated);
            }

            foreach (PhysicsJoint joint in myRequestedJointsToBeCreated)
            {
                lock (OdeLock)
                {
                    if (SOPName_to_pendingJoint.ContainsKey(joint.ObjectNameInScene) && SOPName_to_pendingJoint[joint.ObjectNameInScene] != null)
                    {
                        DoJointErrorMessage(joint, "WARNING: ignoring request to re-add already pending joint Name:" + joint.ObjectNameInScene + " type:" + joint.Type + " parms: " + joint.RawParams + " pos: " + joint.Position + " rot:" + joint.Rotation);
                        continue;
                    }
                    if (SOPName_to_activeJoint.ContainsKey(joint.ObjectNameInScene) && SOPName_to_activeJoint[joint.ObjectNameInScene] != null)
                    {
                        DoJointErrorMessage(joint, "WARNING: ignoring request to re-add already active joint Name:" + joint.ObjectNameInScene + " type:" + joint.Type + " parms: " + joint.RawParams + " pos: " + joint.Position + " rot:" + joint.Rotation);
                        continue;
                    }

                    InternalAddPendingJoint(joint as OdePhysicsJoint);

                    if (joint.BodyNames.Count >= 2)
                    {
                        for (int iBodyName = 0; iBodyName < 2; iBodyName++)
                        {
                            string bodyName = joint.BodyNames[iBodyName];
                            if (bodyName != "NULL")
                            {
                                if (!joints_connecting_actor.ContainsKey(bodyName))
                                {
                                    joints_connecting_actor.Add(bodyName, new List<PhysicsJoint>());
                                }
                                joints_connecting_actor[bodyName].Add(joint);
                            }
                        }
                    }
                }
            }

            // remove processed joints from shared list
            lock (externalJointRequestsLock)
            {
                foreach (PhysicsJoint joint in myRequestedJointsToBeCreated)
                {
                    requestedJointsToBeCreated.Remove(joint);
                }
            }
        }

        /// <summary>
        /// Add a request for joint creation.
        /// </summary>
        /// <remarks>
        /// this joint will just be added to a waiting list that is NOT processed during the main
        /// Simulate() loop (to avoid deadlocks). After Simulate() is finished, we handle unprocessed joint requests.
        /// </remarks>
        /// <param name="objectNameInScene"></param>
        /// <param name="jointType"></param>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
        /// <param name="parms"></param>
        /// <param name="bodyNames"></param>
        /// <param name="trackedBodyName"></param>
        /// <param name="localRotation"></param>
        /// <returns></returns>
        public override PhysicsJoint RequestJointCreation(
            string objectNameInScene, PhysicsJointType jointType, Vector3 position,
            Quaternion rotation, string parms, List<string> bodyNames, string trackedBodyName, Quaternion localRotation)
        {
            OdePhysicsJoint joint = new OdePhysicsJoint();
            joint.ObjectNameInScene = objectNameInScene;
            joint.Type = jointType;
            joint.Position = position;
            joint.Rotation = rotation;
            joint.RawParams = parms;
            joint.BodyNames = new List<string>(bodyNames);
            joint.TrackedBodyName = trackedBodyName;
            joint.LocalRotation = localRotation;
            joint.jointID = IntPtr.Zero;
            joint.ErrorMessageCount = 0;

            lock (externalJointRequestsLock)
            {
                if (!requestedJointsToBeCreated.Contains(joint)) // forbid same creation request from entering twice
                {
                    requestedJointsToBeCreated.Add(joint);
                }
            }

            return joint;
        }

        private void RemoveAllJointsConnectedToActor(PhysicsActor actor)
        {
            //m_log.Debug("RemoveAllJointsConnectedToActor: start");
            if (actor.SOPName != null && joints_connecting_actor.ContainsKey(actor.SOPName) && joints_connecting_actor[actor.SOPName] != null)
            {
                List<PhysicsJoint> jointsToRemove = new List<PhysicsJoint>();
                //TODO: merge these 2 loops (originally it was needed to avoid altering a list being iterated over, but it is no longer needed due to the joint request queue mechanism)
                foreach (PhysicsJoint j in joints_connecting_actor[actor.SOPName])
                {
                    jointsToRemove.Add(j);
                }
                foreach (PhysicsJoint j in jointsToRemove)
                {
                    //m_log.Debug("RemoveAllJointsConnectedToActor: about to request deletion of " + j.ObjectNameInScene);
                    RequestJointDeletion(j.ObjectNameInScene);
                    //m_log.Debug("RemoveAllJointsConnectedToActor: done request deletion of " + j.ObjectNameInScene);
                    j.TrackedBodyName = null; // *IMMEDIATELY* prevent any further movement of this joint (else a deleted actor might cause spurious tracking motion of the joint for a few frames, leading to the joint proxy object disappearing)
                }
            }
        }

        public override void RemoveAllJointsConnectedToActorThreadLocked(PhysicsActor actor)
        {
            //m_log.Debug("RemoveAllJointsConnectedToActorThreadLocked: start");
            lock (OdeLock)
            {
                //m_log.Debug("RemoveAllJointsConnectedToActorThreadLocked: got lock");
                RemoveAllJointsConnectedToActor(actor);
            }
        }

        // normally called from within OnJointMoved, which is called from within a lock (OdeLock)
        public override Vector3 GetJointAnchor(PhysicsJoint joint)
        {
            Debug.Assert(joint.IsInPhysicsEngine);
            SafeNativeMethods.Vector3 pos = new SafeNativeMethods.Vector3();

            if (!(joint is OdePhysicsJoint))
            {
                DoJointErrorMessage(joint, "warning: non-ODE joint requesting anchor: " + joint.ObjectNameInScene);
            }
            else
            {
                OdePhysicsJoint odeJoint = (OdePhysicsJoint)joint;
                switch (odeJoint.Type)
                {
                    case PhysicsJointType.Ball:
                        SafeNativeMethods.JointGetBallAnchor(odeJoint.jointID, out pos);
                        break;
                    case PhysicsJointType.Hinge:
                        SafeNativeMethods.JointGetHingeAnchor(odeJoint.jointID, out pos);
                        break;
                }
            }
            return new Vector3(pos.X, pos.Y, pos.Z);
        }

        /// <summary>
        /// Get joint axis.
        /// </summary>
        /// <remarks>
        /// normally called from within OnJointMoved, which is called from within a lock (OdeLock)
        /// WARNING: ODE sometimes returns <0,0,0> as the joint axis! Therefore this function
        /// appears to be unreliable. Fortunately we can compute the joint axis ourselves by
        /// keeping track of the joint's original orientation relative to one of the involved bodies.
        /// </remarks>
        /// <param name="joint"></param>
        /// <returns></returns>
        public override Vector3 GetJointAxis(PhysicsJoint joint)
        {
            Debug.Assert(joint.IsInPhysicsEngine);
            SafeNativeMethods.Vector3 axis = new SafeNativeMethods.Vector3();

            if (!(joint is OdePhysicsJoint))
            {
                DoJointErrorMessage(joint, "warning: non-ODE joint requesting anchor: " + joint.ObjectNameInScene);
            }
            else
            {
                OdePhysicsJoint odeJoint = (OdePhysicsJoint)joint;
                switch (odeJoint.Type)
                {
                    case PhysicsJointType.Ball:
                        DoJointErrorMessage(joint, "warning - axis requested for ball joint: " + joint.ObjectNameInScene);
                        break;
                    case PhysicsJointType.Hinge:
                        SafeNativeMethods.JointGetHingeAxis(odeJoint.jointID, out axis);
                        break;
                }
            }
            return new Vector3(axis.X, axis.Y, axis.Z);
        }

        /// <summary>
        /// Stop this prim being subject to physics
        /// </summary>
        /// <param name="prim"></param>
        internal void DeactivatePrim(OdePrim prim)
        {
            _activeprims.Remove(prim);
        }

        public override void RemovePrim(PhysicsActor prim)
        {
            // As with all ODE physics operations, we don't remove the prim immediately but signal that it should be
            // removed in the next physics simulate pass.
            if (prim is OdePrim)
            {
                lock (OdeLock)
                {
                    OdePrim p = (OdePrim) prim;

                    p.setPrimForRemoval();
                    AddPhysicsActorTaint(prim);
                }
            }
        }

        /// <summary>
        /// This is called from within simulate but outside the locked portion
        /// We need to do our own locking here
        /// (Note: As of 20110801 this no longer appears to be true - this is being called within lock (odeLock) in
        /// Simulate() -- justincc).
        ///
        /// Essentially, we need to remove the prim from our space segment, whatever segment it's in.
        ///
        /// If there are no more prim in the segment, we need to empty (spacedestroy)the segment and reclaim memory
        /// that the space was using.
        /// </summary>
        /// <param name="prim"></param>
        internal void RemovePrimThreadLocked(OdePrim prim)
        {
//            m_log.DebugFormat("[ODE SCENE]: Removing physical prim {0} {1}", prim.Name, prim.LocalID);

            lock (prim)
            {
                RemoveCollisionEventReporting(prim);

                if (prim.prim_geom != IntPtr.Zero)
                {
                    prim.ResetTaints();

                    if (prim.IsPhysical)
                    {
                        prim.disableBody();
                        if (prim.childPrim)
                        {
                            prim.childPrim = false;
                            prim.Body = IntPtr.Zero;
                            prim.m_disabled = true;
                            prim.IsPhysical = false;
                        }


                    }
                    prim.m_targetSpace = IntPtr.Zero;
                    if (!prim.RemoveGeom())
                        m_log.Warn("[ODE SCENE]: Unable to remove prim from physics scene");

                    lock (_prims)
                        _prims.Remove(prim);


                    if (SupportsNINJAJoints)
                        RemoveAllJointsConnectedToActorThreadLocked(prim);
                }
            }
        }

        #endregion

        #region Space Separation Calculation

        /// <summary>
        /// Takes a space pointer and zeros out the array we're using to hold the spaces
        /// </summary>
        /// <param name="pSpace"></param>
        private void resetSpaceArrayItemToZero(IntPtr pSpace)
        {
            for (int x = 0; x < staticPrimspace.GetLength(0); x++)
            {
                for (int y = 0; y < staticPrimspace.GetLength(1); y++)
                {
                    if (staticPrimspace[x, y] == pSpace)
                        staticPrimspace[x, y] = IntPtr.Zero;
                }
            }
        }

//        private void resetSpaceArrayItemToZero(int arrayitemX, int arrayitemY)
//        {
//            staticPrimspace[arrayitemX, arrayitemY] = IntPtr.Zero;
//        }

        /// <summary>
        /// Called when a static prim moves.  Allocates a space for the prim based on its position
        /// </summary>
        /// <param name="geom">the pointer to the geom that moved</param>
        /// <param name="pos">the position that the geom moved to</param>
        /// <param name="currentspace">a pointer to the space it was in before it was moved.</param>
        /// <returns>a pointer to the new space it's in</returns>
        internal IntPtr recalculateSpaceForGeom(IntPtr geom, Vector3 pos, IntPtr currentspace)
        {
            // Called from setting the Position and Size of an ODEPrim so
            // it's already in locked space.

            // we don't want to remove the main space
            // we don't need to test physical here because this function should
            // never be called if the prim is physical(active)

            // All physical prim end up in the root space
            //Thread.Sleep(20);
            if (currentspace != space)
            {
                //m_log.Info("[SPACE]: C:" + currentspace.ToString() + " g:" + geom.ToString());
                //if (currentspace == IntPtr.Zero)
               //{
                    //int adfadf = 0;
                //}
                if (SafeNativeMethods.SpaceQuery(currentspace, geom) && currentspace != IntPtr.Zero)
                {
                    if (SafeNativeMethods.GeomIsSpace(currentspace))
                    {
//                        waitForSpaceUnlock(currentspace);
                        SafeNativeMethods.SpaceRemove(currentspace, geom);
                    }
                    else
                    {
                        m_log.Info("[ODE SCENE]: Invalid Scene passed to 'recalculatespace':" + currentspace +
                                   " Geom:" + geom);
                    }
                }
                else
                {
                    IntPtr sGeomIsIn = SafeNativeMethods.GeomGetSpace(geom);
                    if (sGeomIsIn != IntPtr.Zero)
                    {
                        if (SafeNativeMethods.GeomIsSpace(currentspace))
                        {
//                            waitForSpaceUnlock(sGeomIsIn);
                            SafeNativeMethods.SpaceRemove(sGeomIsIn, geom);
                        }
                        else
                        {
                            m_log.Info("[ODE SCENE]: Invalid Scene passed to 'recalculatespace':" +
                                       sGeomIsIn + " Geom:" + geom);
                        }
                    }
                }

                //If there are no more geometries in the sub-space, we don't need it in the main space anymore
                if (SafeNativeMethods.SpaceGetNumGeoms(currentspace) == 0)
                {
                    if (currentspace != IntPtr.Zero)
                    {
                        if (SafeNativeMethods.GeomIsSpace(currentspace))
                        {
                            SafeNativeMethods.SpaceRemove(space, currentspace);
                            // free up memory used by the space.

                            resetSpaceArrayItemToZero(currentspace);
                        }
                        else
                        {
                            m_log.Info("[ODE SCENE]: Invalid Scene passed to 'recalculatespace':" +
                                       currentspace + " Geom:" + geom);
                        }
                    }
                }
            }
            else
            {
                // this is a physical object that got disabled. ;.;
                if (currentspace != IntPtr.Zero && geom != IntPtr.Zero)
                {
                    if (SafeNativeMethods.SpaceQuery(currentspace, geom))
                    {
                        if (SafeNativeMethods.GeomIsSpace(currentspace))
                        {
//                            waitForSpaceUnlock(currentspace);
                            SafeNativeMethods.SpaceRemove(currentspace, geom);
                        }
                        else
                        {
                            m_log.Info("[ODE SCENE]: Invalid Scene passed to 'recalculatespace':" +
                                       currentspace + " Geom:" + geom);
                        }
                    }
                    else
                    {
                        IntPtr sGeomIsIn = SafeNativeMethods.GeomGetSpace(geom);
                        if (sGeomIsIn != IntPtr.Zero)
                        {
                            if (SafeNativeMethods.GeomIsSpace(sGeomIsIn))
                            {
//                                waitForSpaceUnlock(sGeomIsIn);
                                SafeNativeMethods.SpaceRemove(sGeomIsIn, geom);
                            }
                            else
                            {
                                m_log.Info("[ODE SCENE]: Invalid Scene passed to 'recalculatespace':" +
                                           sGeomIsIn + " Geom:" + geom);
                            }
                        }
                    }
                }
            }

            // The routines in the Position and Size sections do the 'inserting' into the space,
            // so all we have to do is make sure that the space that we're putting the prim into
            // is in the 'main' space.
            int[] iprimspaceArrItem = calculateSpaceArrayItemFromPos(pos);
            IntPtr newspace = calculateSpaceForGeom(pos);

            if (newspace == IntPtr.Zero)
            {
                newspace = createprimspace(iprimspaceArrItem[0], iprimspaceArrItem[1]);
                SafeNativeMethods.HashSpaceSetLevels(newspace, HashspaceLow, HashspaceHigh);
            }

            return newspace;
        }

        /// <summary>
        /// Creates a new space at X Y
        /// </summary>
        /// <param name="iprimspaceArrItemX"></param>
        /// <param name="iprimspaceArrItemY"></param>
        /// <returns>A pointer to the created space</returns>
        internal IntPtr createprimspace(int iprimspaceArrItemX, int iprimspaceArrItemY)
        {
            // creating a new space for prim and inserting it into main space.
            staticPrimspace[iprimspaceArrItemX, iprimspaceArrItemY] = SafeNativeMethods.HashSpaceCreate(IntPtr.Zero);
            SafeNativeMethods.GeomSetCategoryBits(staticPrimspace[iprimspaceArrItemX, iprimspaceArrItemY], (int)CollisionCategories.Space);
//            waitForSpaceUnlock(space);
            SafeNativeMethods.SpaceSetSublevel(space, 1);
            SafeNativeMethods.SpaceAdd(space, staticPrimspace[iprimspaceArrItemX, iprimspaceArrItemY]);

            return staticPrimspace[iprimspaceArrItemX, iprimspaceArrItemY];
        }

        /// <summary>
        /// Calculates the space the prim should be in by its position
        /// </summary>
        /// <param name="pos"></param>
        /// <returns>a pointer to the space. This could be a new space or reused space.</returns>
        internal IntPtr calculateSpaceForGeom(Vector3 pos)
        {
            int[] xyspace = calculateSpaceArrayItemFromPos(pos);
            //m_log.Info("[Physics]: Attempting to use arrayItem: " + xyspace[0].ToString() + "," + xyspace[1].ToString());
            return staticPrimspace[xyspace[0], xyspace[1]];
        }

        /// <summary>
        /// Holds the space allocation logic
        /// </summary>
        /// <param name="pos"></param>
        /// <returns>an array item based on the position</returns>
        internal int[] calculateSpaceArrayItemFromPos(Vector3 pos)
        {
            int[] returnint = new int[2];

            returnint[0] = (int) (pos.X * spacesPerMeterX);

            if (returnint[0] > spaceGridMaxX)
                returnint[0] = spaceGridMaxX;
            if (returnint[0] < 0)
                returnint[0] = 0;

            returnint[1] = (int)(pos.Y * spacesPerMeterY);
            if (returnint[1] > spaceGridMaxY)
                returnint[1] = spaceGridMaxY;
            if (returnint[1] < 0)
                returnint[1] = 0;

            return returnint;
        }

        #endregion

        /// <summary>
        /// Routine to figure out if we need to mesh this prim with our mesher
        /// </summary>
        /// <param name="pbs"></param>
        /// <returns></returns>
        internal bool needsMeshing(PrimitiveBaseShape pbs)
        {
            // most of this is redundant now as the mesher will return null if it cant mesh a prim
            // but we still need to check for sculptie meshing being enabled so this is the most
            // convenient place to do it for now...

        //    //if (pbs.PathCurve == (byte)Primitive.PathCurve.Circle && pbs.ProfileCurve == (byte)Primitive.ProfileCurve.Circle && pbs.PathScaleY <= 0.75f)
        //    //m_log.Debug("needsMeshing: " + " pathCurve: " + pbs.PathCurve.ToString() + " profileCurve: " + pbs.ProfileCurve.ToString() + " pathScaleY: " + Primitive.UnpackPathScale(pbs.PathScaleY).ToString());
            int iPropertiesNotSupportedDefault = 0;

            if (pbs.SculptEntry && !meshSculptedPrim)
            {
#if SPAM
                m_log.Warn("NonMesh");
#endif
                return false;
            }

            // if it's a standard box or sphere with no cuts, hollows, twist or top shear, return false since ODE can use an internal representation for the prim
            if (!forceSimplePrimMeshing && !pbs.SculptEntry)
            {
                if ((pbs.ProfileShape == ProfileShape.Square && pbs.PathCurve == (byte)Extrusion.Straight)
                    || (pbs.ProfileShape == ProfileShape.HalfCircle && pbs.PathCurve == (byte)Extrusion.Curve1
                    && pbs.Scale.X == pbs.Scale.Y && pbs.Scale.Y == pbs.Scale.Z))
                {

                    if (pbs.ProfileBegin == 0 && pbs.ProfileEnd == 0
                        && pbs.ProfileHollow == 0
                        && pbs.PathTwist == 0 && pbs.PathTwistBegin == 0
                        && pbs.PathBegin == 0 && pbs.PathEnd == 0
                        && pbs.PathTaperX == 0 && pbs.PathTaperY == 0
                        && pbs.PathScaleX == 100 && pbs.PathScaleY == 100
                        && pbs.PathShearX == 0 && pbs.PathShearY == 0)
                    {
#if SPAM
                    m_log.Warn("NonMesh");
#endif
                        return false;
                    }
                }
            }

            if (pbs.ProfileHollow != 0)
                iPropertiesNotSupportedDefault++;

            if ((pbs.PathBegin != 0) || pbs.PathEnd != 0)
                iPropertiesNotSupportedDefault++;

            if ((pbs.PathTwistBegin != 0) || (pbs.PathTwist != 0))
                iPropertiesNotSupportedDefault++;

            if ((pbs.ProfileBegin != 0) || pbs.ProfileEnd != 0)
                iPropertiesNotSupportedDefault++;

            if ((pbs.PathScaleX != 100) || (pbs.PathScaleY != 100))
                iPropertiesNotSupportedDefault++;

            if ((pbs.PathShearX != 0) || (pbs.PathShearY != 0))
                iPropertiesNotSupportedDefault++;

            if (pbs.ProfileShape == ProfileShape.Circle && pbs.PathCurve == (byte)Extrusion.Straight)
                iPropertiesNotSupportedDefault++;

            if (pbs.ProfileShape == ProfileShape.HalfCircle && pbs.PathCurve == (byte)Extrusion.Curve1 && (pbs.Scale.X != pbs.Scale.Y || pbs.Scale.Y != pbs.Scale.Z || pbs.Scale.Z != pbs.Scale.X))
                iPropertiesNotSupportedDefault++;

            if (pbs.ProfileShape == ProfileShape.HalfCircle && pbs.PathCurve == (byte) Extrusion.Curve1)
                iPropertiesNotSupportedDefault++;

            // test for torus
            if ((pbs.ProfileCurve & 0x07) == (byte)ProfileShape.Square)
            {
                if (pbs.PathCurve == (byte)Extrusion.Curve1)
                {
                    iPropertiesNotSupportedDefault++;
                }
            }
            else if ((pbs.ProfileCurve & 0x07) == (byte)ProfileShape.Circle)
            {
                if (pbs.PathCurve == (byte)Extrusion.Straight)
                {
                    iPropertiesNotSupportedDefault++;
                }

                // ProfileCurve seems to combine hole shape and profile curve so we need to only compare against the lower 3 bits
                else if (pbs.PathCurve == (byte)Extrusion.Curve1)
                {
                    iPropertiesNotSupportedDefault++;
                }
            }
            else if ((pbs.ProfileCurve & 0x07) == (byte)ProfileShape.HalfCircle)
            {
                if (pbs.PathCurve == (byte)Extrusion.Curve1 || pbs.PathCurve == (byte)Extrusion.Curve2)
                {
                    iPropertiesNotSupportedDefault++;
                }
            }
            else if ((pbs.ProfileCurve & 0x07) == (byte)ProfileShape.EquilateralTriangle)
            {
                if (pbs.PathCurve == (byte)Extrusion.Straight)
                {
                    iPropertiesNotSupportedDefault++;
                }
                else if (pbs.PathCurve == (byte)Extrusion.Curve1)
                {
                    iPropertiesNotSupportedDefault++;
                }
            }

            if (pbs.SculptEntry && meshSculptedPrim)
                iPropertiesNotSupportedDefault++;

            if (iPropertiesNotSupportedDefault == 0)
            {
#if SPAM
                m_log.Warn("NonMesh");
#endif
                return false;
            }
#if SPAM
            m_log.Debug("Mesh");
#endif
            return true;
        }

        /// <summary>
        /// Called after our prim properties are set Scale, position etc.
        /// </summary>
        /// <remarks>
        /// We use this event queue like method to keep changes to the physical scene occuring in the threadlocked mutex
        /// This assures us that we have no race conditions
        /// </remarks>
        /// <param name="actor"></param>
        public override void AddPhysicsActorTaint(PhysicsActor actor)
        {
            if (actor is OdePrim)
            {
                OdePrim taintedprim = ((OdePrim)actor);
                lock (_taintedPrims)
                    _taintedPrims.Add(taintedprim);
            }
            else if (actor is OdeCharacter)
            {
                OdeCharacter taintedchar = ((OdeCharacter)actor);
                lock (_taintedActors)
                {
                    _taintedActors.Add(taintedchar);
                    if (taintedchar.bad)
                        m_log.ErrorFormat("[ODE SCENE]: Added BAD actor {0} to tainted actors", taintedchar.m_uuid);
                }
            }
        }

        // does all pending changes generated during region load process
        public override void ProcessPreSimulation()
        {
            lock (OdeLock)
            {
                if (world == IntPtr.Zero)
                {
                    _taintedPrims.Clear();;
                    return;
                }

                int donechanges = 0;
                if (_taintedPrims.Count > 0)
                {

                    m_log.InfoFormat("[Ode] start processing pending actor operations");
                    int tstart = Util.EnvironmentTickCount();

                    SafeNativeMethods.AllocateODEDataForThread(0);

                    lock (_taintedPrims)
                    {
                        foreach (OdePrim prim in _taintedPrims)
                        {
                            if (prim.m_taintremove)
                                RemovePrimThreadLocked(prim);
                            else
                                prim.ProcessTaints();

                            prim.m_collisionscore = 0;
                            donechanges++;
                        }
                        _taintedPrims.Clear();
                    }

                    int time = Util.EnvironmentTickCountSubtract(tstart);
                    m_log.InfoFormat("[Ode] finished {0} operations in {1}ms", donechanges, time);
                }
                m_log.InfoFormat("[Ode] {0} prim actors loaded",_prims.Count);
            }
        }


        /// <summary>
        /// This is our main simulate loop
        /// </summary>
        /// <remarks>
        /// It's thread locked by a Mutex in the scene.
        /// It holds Collisions, it instructs ODE to step through the physical reactions
        /// It moves the objects around in memory
        /// It calls the methods that report back to the object owners.. (scenepresence, SceneObjectGroup)
        /// </remarks>
        /// <param name="timeStep"></param>
        /// <returns>The number of frames simulated over that period.</returns>
        public override float Simulate(float timeStep)
        {
            if (!_worldInitialized)
                return 1.0f;

            int startFrameTick = CollectStats ? Util.EnvironmentTickCount() : 0;
            int tempTick = 0, tempTick2 = 0;

            if (framecount >= int.MaxValue)
                framecount = 0;

            framecount++;

            float fps = 0;

            step_time += timeStep;

            float HalfOdeStep = ODE_STEPSIZE * 0.5f;
            if (step_time < HalfOdeStep)
                return 0;


            // We change _collisionEventPrimChanges to avoid locking _collisionEventPrim itself and causing potential
            // deadlock if the collision event tries to lock something else later on which is already locked by a
            // caller that is adding or removing the collision event.
            lock (m_collisionEventActorsChanges)
            {
                foreach (KeyValuePair<uint, PhysicsActor> kvp in m_collisionEventActorsChanges)
                {
                    if (kvp.Value == null)
                        m_collisionEventActors.Remove(kvp.Key);
                    else
                        m_collisionEventActors[kvp.Key] = kvp.Value;
                }

                m_collisionEventActorsChanges.Clear();
            }

            if (SupportsNINJAJoints)
            {
                DeleteRequestedJoints(); // this must be outside of the lock (OdeLock) to avoid deadlocks
                CreateRequestedJoints(); // this must be outside of the lock (OdeLock) to avoid deadlocks
            }


            lock (OdeLock)
            {
                SafeNativeMethods.AllocateODEDataForThread(~0U);

                 while (step_time > HalfOdeStep)
                {
                    try
                    {
                        if (CollectStats)
                            tempTick = Util.EnvironmentTickCount();

                        lock (_taintedActors)
                        {
                            foreach (OdeCharacter character in _taintedActors)
                                character.ProcessTaints();

                            _taintedActors.Clear();
                        }

                        if (CollectStats)
                        {
                            tempTick2 = Util.EnvironmentTickCount();
                            m_stats[ODEAvatarTaintMsStatName] += Util.EnvironmentTickCountSubtract(tempTick2, tempTick);
                            tempTick = tempTick2;
                        }

                        lock (_taintedPrims)
                        {
                            foreach (OdePrim prim in _taintedPrims)
                            {
                                if (prim.m_taintremove)
                                {
//                                    Console.WriteLine("Simulate calls RemovePrimThreadLocked for {0}", prim.Name);
                                    RemovePrimThreadLocked(prim);
                                }
                                else
                                {
//                                    Console.WriteLine("Simulate calls ProcessTaints for {0}", prim.Name);
                                    prim.ProcessTaints();
                                }

                                prim.m_collisionscore = 0;

                                // This loop can block up the Heartbeat for a very long time on large regions.
                                // We need to let the Watchdog know that the Heartbeat is not dead
                                // NOTE: This is currently commented out, but if things like OAR loading are
                                // timing the heartbeat out we will need to uncomment it
                                //Watchdog.UpdateThread();
                            }

                            if (SupportsNINJAJoints)
                                SimulatePendingNINJAJoints();

                            _taintedPrims.Clear();
                        }

                        if (CollectStats)
                        {
                            tempTick2 = Util.EnvironmentTickCount();
                            m_stats[ODEPrimTaintMsStatName] += Util.EnvironmentTickCountSubtract(tempTick2, tempTick);
                            tempTick = tempTick2;
                        }

                        // Move characters
                        foreach (OdeCharacter actor in _characters)
                            actor.Move(defects);

                        if (defects.Count != 0)
                        {
                            foreach (OdeCharacter actor in defects)
                            {
                                m_log.ErrorFormat(
                                    "[ODE SCENE]: Removing physics character {0} {1} from physics scene {2} due to defect found when moving",
                                    actor.Name, actor.LocalID, PhysicsSceneName);

                                RemoveCharacter(actor);
                                actor.DestroyOdeStructures();
                            }

                            defects.Clear();
                        }

                        if (CollectStats)
                        {
                            tempTick2 = Util.EnvironmentTickCount();
                            m_stats[ODEAvatarForcesFrameMsStatName] += Util.EnvironmentTickCountSubtract(tempTick2, tempTick);
                            tempTick = tempTick2;
                        }

                        // Move other active objects
                        foreach (OdePrim prim in _activeprims)
                        {
                            prim.m_collisionscore = 0;
                            prim.Move(timeStep);
                        }

                        if (CollectStats)
                        {
                            tempTick2 = Util.EnvironmentTickCount();
                            m_stats[ODEPrimForcesFrameMsStatName] += Util.EnvironmentTickCountSubtract(tempTick2, tempTick);
                            tempTick = tempTick2;
                        }

                        m_rayCastManager.ProcessQueuedRequests();

                        if (CollectStats)
                        {
                            tempTick2 = Util.EnvironmentTickCount();
                            m_stats[ODERaycastingFrameMsStatName] += Util.EnvironmentTickCountSubtract(tempTick2, tempTick);
                            tempTick = tempTick2;
                        }

                        collision_optimized();

                        if (CollectStats)
                        {
                            tempTick2 = Util.EnvironmentTickCount();
                            m_stats[ODEOtherCollisionFrameMsStatName] += Util.EnvironmentTickCountSubtract(tempTick2, tempTick);
                            tempTick = tempTick2;
                        }

                        foreach (PhysicsActor obj in m_collisionEventActors.Values)
                        {
                            // m_log.DebugFormat("[PHYSICS]: Assessing {0} {1} for collision events", obj.SOPName, obj.LocalID);

                            switch ((ActorTypes)obj.PhysicsActorType)
                            {
                                case ActorTypes.Agent:
                                    OdeCharacter cobj = (OdeCharacter)obj;
                                    cobj.AddCollisionFrameTime(100);
                                    cobj.SendCollisions();
                                    break;

                                case ActorTypes.Prim:
                                    OdePrim pobj = (OdePrim)obj;
                                    pobj.SendCollisions();
                                    break;
                            }
                        }

//                        if (m_global_contactcount > 0)
//                            m_log.DebugFormat(
//                                "[PHYSICS]: Collision contacts to process this frame = {0}", m_global_contactcount);

                        m_global_contactcount = 0;

                        if (CollectStats)
                        {
                            tempTick2 = Util.EnvironmentTickCount();
                            m_stats[ODECollisionNotificationFrameMsStatName] += Util.EnvironmentTickCountSubtract(tempTick2, tempTick);
                            tempTick = tempTick2;
                        }

                        lock(SimulationLock)
                            SafeNativeMethods.WorldQuickStep(world, ODE_STEPSIZE);

                        if (CollectStats)
                            m_stats[ODENativeStepFrameMsStatName] += Util.EnvironmentTickCountSubtract(tempTick);

                        SafeNativeMethods.JointGroupEmpty(contactgroup);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[ODE SCENE]: {0}, {1}, {2}", e.Message, e.TargetSite, e);
                    }

                    step_time -= ODE_STEPSIZE;
                    fps += ODE_STEPSIZE;
                }

                if (CollectStats)
                    tempTick = Util.EnvironmentTickCount();

                foreach (OdeCharacter actor in _characters)
                {
                    if (actor.bad)
                        m_log.ErrorFormat("[ODE SCENE]: BAD Actor {0} in _characters list was not removed?", actor.m_uuid);

                    actor.UpdatePositionAndVelocity(defects);
                }

                if (defects.Count != 0)
                {
                    foreach (OdeCharacter actor in defects)
                    {
                        m_log.ErrorFormat(
                            "[ODE SCENE]: Removing physics character {0} {1} from physics scene {2} due to defect found when updating position and velocity",
                            actor.Name, actor.LocalID, PhysicsSceneName);

                        RemoveCharacter(actor);
                        actor.DestroyOdeStructures();
                    }

                    defects.Clear();
                }

                if (CollectStats)
                {
                    tempTick2 = Util.EnvironmentTickCount();
                    m_stats[ODEAvatarUpdateFrameMsStatName] += Util.EnvironmentTickCountSubtract(tempTick2, tempTick);
                    tempTick = tempTick2;
                }

                //if (timeStep < 0.2f)

                foreach (OdePrim prim in _activeprims)
                {
                    if (prim.IsPhysical && (SafeNativeMethods.BodyIsEnabled(prim.Body) || !prim._zeroFlag))
                    {
                        prim.UpdatePositionAndVelocity();

                        if (SupportsNINJAJoints)
                            SimulateActorPendingJoints(prim);
                    }
                }

                if (CollectStats)
                    m_stats[ODEPrimUpdateFrameMsStatName] += Util.EnvironmentTickCountSubtract(tempTick);

                //DumpJointInfo();

                // Finished with all sim stepping. If requested, dump world state to file for debugging.
                // TODO: This call to the export function is already inside lock (OdeLock) - but is an extra lock needed?
                // TODO: This overwrites all dump files in-place. Should this be a growing logfile, or separate snapshots?
                if (physics_logging && (physics_logging_interval > 0) && (framecount % physics_logging_interval == 0))
                {
                    string fname = "state-" + world.ToString() + ".DIF"; // give each physics world a separate filename
                    string prefix = "world" + world.ToString(); // prefix for variable names in exported .DIF file

                    if (physics_logging_append_existing_logfile)
                    {
                        string header = "-------------- START OF PHYSICS FRAME " + framecount.ToString() + " --------------";
                        TextWriter fwriter = File.AppendText(fname);
                        fwriter.WriteLine(header);
                        fwriter.Close();
                    }

                    SafeNativeMethods.WorldExportDIF(world, fname, physics_logging_append_existing_logfile, prefix);
                }

                latertickcount = Util.EnvironmentTickCountSubtract(tickCountFrameRun);

                // OpenSimulator above does 10 fps.  10 fps = means that the main thread loop and physics
                // has a max of 100 ms to run theoretically.
                // If the main loop stalls, it calls Simulate later which makes the tick count ms larger.
                // If Physics stalls, it takes longer which makes the tick count ms larger.

                if (latertickcount < 100)
                {
                    m_timeDilation = 1.0f;
                }
                else
                {
                    m_timeDilation = 100f / latertickcount;
                    //m_timeDilation = Math.Min((Math.Max(100 - (Util.EnvironmentTickCount() - tickCountFrameRun), 1) / 100f), 1.0f);
                }

                tickCountFrameRun = Util.EnvironmentTickCount();

                if (CollectStats)
                    m_stats[ODETotalFrameMsStatName] += Util.EnvironmentTickCountSubtract(startFrameTick);
            }

            fps *= 1.0f/timeStep;
            return fps;
        }

        /// <summary>
        /// Simulate pending NINJA joints.
        /// </summary>
        /// <remarks>
        /// Called by the main Simulate() loop if NINJA joints are active.  Should not be called from anywhere else.
        /// </remarks>
        private void SimulatePendingNINJAJoints()
        {
            // Create pending joints, if possible

            // joints can only be processed after ALL bodies are processed (and exist in ODE), since creating
            // a joint requires specifying the body id of both involved bodies
            if (pendingJoints.Count > 0)
            {
                List<PhysicsJoint> successfullyProcessedPendingJoints = new List<PhysicsJoint>();
                //DoJointErrorMessage(joints_connecting_actor, "taint: " + pendingJoints.Count + " pending joints");
                foreach (PhysicsJoint joint in pendingJoints)
                {
                    //DoJointErrorMessage(joint, "taint: time to create joint with parms: " + joint.RawParams);
                    string[] jointParams = joint.RawParams.Split(" ".ToCharArray(), System.StringSplitOptions.RemoveEmptyEntries);
                    List<IntPtr> jointBodies = new List<IntPtr>();
                    bool allJointBodiesAreReady = true;
                    foreach (string jointParam in jointParams)
                    {
                        if (jointParam == "NULL")
                        {
                            //DoJointErrorMessage(joint, "attaching NULL joint to world");
                            jointBodies.Add(IntPtr.Zero);
                        }
                        else
                        {
                            //DoJointErrorMessage(joint, "looking for prim name: " + jointParam);
                            bool foundPrim = false;
                            lock (_prims)
                            {
                                foreach (OdePrim prim in _prims) // FIXME: inefficient
                                {
                                    if (prim.SOPName == jointParam)
                                    {
                                        //DoJointErrorMessage(joint, "found for prim name: " + jointParam);
                                        if (prim.IsPhysical && prim.Body != IntPtr.Zero)
                                        {
                                            jointBodies.Add(prim.Body);
                                            foundPrim = true;
                                            break;
                                        }
                                        else
                                        {
                                            DoJointErrorMessage(joint, "prim name " + jointParam +
                                                " exists but is not (yet) physical; deferring joint creation. " +
                                                "IsPhysical property is " + prim.IsPhysical +
                                                " and body is " + prim.Body);
                                            foundPrim = false;
                                            break;
                                        }
                                    }
                                }
                            }
                            if (foundPrim)
                            {
                                // all is fine
                            }
                            else
                            {
                                allJointBodiesAreReady = false;
                                break;
                            }
                        }
                    }

                    if (allJointBodiesAreReady)
                    {
                        //DoJointErrorMessage(joint, "allJointBodiesAreReady for " + joint.ObjectNameInScene + " with parms " + joint.RawParams);
                        if (jointBodies[0] == jointBodies[1])
                        {
                            DoJointErrorMessage(joint, "ERROR: joint cannot be created; the joint bodies are the same, body1==body2. Raw body is " + jointBodies[0] + ". raw parms: " + joint.RawParams);
                        }
                        else
                        {
                            switch (joint.Type)
                            {
                                case PhysicsJointType.Ball:
                                    {
                                        IntPtr odeJoint;
                                        //DoJointErrorMessage(joint, "ODE creating ball joint ");
                                        odeJoint = SafeNativeMethods.JointCreateBall(world, IntPtr.Zero);
                                        //DoJointErrorMessage(joint, "ODE attaching ball joint: " + odeJoint + " with b1:" + jointBodies[0] + " b2:" + jointBodies[1]);
                                        SafeNativeMethods.JointAttach(odeJoint, jointBodies[0], jointBodies[1]);
                                        //DoJointErrorMessage(joint, "ODE setting ball anchor: " + odeJoint + " to vec:" + joint.Position);
                                        SafeNativeMethods.JointSetBallAnchor(odeJoint,
                                                            joint.Position.X,
                                                            joint.Position.Y,
                                                            joint.Position.Z);
                                        //DoJointErrorMessage(joint, "ODE joint setting OK");
                                        //DoJointErrorMessage(joint, "The ball joint's bodies are here: b0: ");
                                        //DoJointErrorMessage(joint, "" + (jointBodies[0] != IntPtr.Zero ? "" + d.BodyGetPosition(jointBodies[0]) : "fixed environment"));
                                        //DoJointErrorMessage(joint, "The ball joint's bodies are here: b1: ");
                                        //DoJointErrorMessage(joint, "" + (jointBodies[1] != IntPtr.Zero ? "" + d.BodyGetPosition(jointBodies[1]) : "fixed environment"));

                                        if (joint is OdePhysicsJoint)
                                        {
                                            ((OdePhysicsJoint)joint).jointID = odeJoint;
                                        }
                                        else
                                        {
                                            DoJointErrorMessage(joint, "WARNING: non-ode joint in ODE!");
                                        }
                                    }
                                    break;
                                case PhysicsJointType.Hinge:
                                    {
                                        IntPtr odeJoint;
                                        //DoJointErrorMessage(joint, "ODE creating hinge joint ");
                                        odeJoint = SafeNativeMethods.JointCreateHinge(world, IntPtr.Zero);
                                        //DoJointErrorMessage(joint, "ODE attaching hinge joint: " + odeJoint + " with b1:" + jointBodies[0] + " b2:" + jointBodies[1]);
                                        SafeNativeMethods.JointAttach(odeJoint, jointBodies[0], jointBodies[1]);
                                        //DoJointErrorMessage(joint, "ODE setting hinge anchor: " + odeJoint + " to vec:" + joint.Position);
                                        SafeNativeMethods.JointSetHingeAnchor(odeJoint,
                                                              joint.Position.X,
                                                              joint.Position.Y,
                                                              joint.Position.Z);
                                        // We use the orientation of the x-axis of the joint's coordinate frame
                                        // as the axis for the hinge.

                                        // Therefore, we must get the joint's coordinate frame based on the
                                        // joint.Rotation field, which originates from the orientation of the
                                        // joint's proxy object in the scene.

                                        // The joint's coordinate frame is defined as the transformation matrix
                                        // that converts a vector from joint-local coordinates into world coordinates.
                                        // World coordinates are defined as the XYZ coordinate system of the sim,
                                        // as shown in the top status-bar of the viewer.

                                        // Once we have the joint's coordinate frame, we extract its X axis (AtAxis)
                                        // and use that as the hinge axis.

                                        //joint.Rotation.Normalize();
                                        Matrix4 proxyFrame = Matrix4.CreateFromQuaternion(joint.Rotation);

                                        // Now extract the X axis of the joint's coordinate frame.

                                        // Do not try to use proxyFrame.AtAxis or you will become mired in the
                                        // tar pit of transposed, inverted, and generally messed-up orientations.
                                        // (In other words, Matrix4.AtAxis() is borked.)
                                        // Vector3 jointAxis = proxyFrame.AtAxis; <--- this path leadeth to madness

                                        // Instead, compute the X axis of the coordinate frame by transforming
                                        // the (1,0,0) vector. At least that works.

                                        //m_log.Debug("PHY: making axis: complete matrix is " + proxyFrame);
                                        Vector3 jointAxis = Vector3.Transform(Vector3.UnitX, proxyFrame);
                                        //m_log.Debug("PHY: making axis: hinge joint axis is " + jointAxis);
                                        //DoJointErrorMessage(joint, "ODE setting hinge axis: " + odeJoint + " to vec:" + jointAxis);
                                        SafeNativeMethods.JointSetHingeAxis(odeJoint,
                                                            jointAxis.X,
                                                            jointAxis.Y,
                                                            jointAxis.Z);
                                        //d.JointSetHingeParam(odeJoint, (int)dParam.CFM, 0.1f);
                                        if (joint is OdePhysicsJoint)
                                        {
                                            ((OdePhysicsJoint)joint).jointID = odeJoint;
                                        }
                                        else
                                        {
                                            DoJointErrorMessage(joint, "WARNING: non-ode joint in ODE!");
                                        }
                                    }
                                    break;
                            }
                            successfullyProcessedPendingJoints.Add(joint);
                        }
                    }
                    else
                    {
                        DoJointErrorMessage(joint, "joint could not yet be created; still pending");
                    }
                }

                foreach (PhysicsJoint successfullyProcessedJoint in successfullyProcessedPendingJoints)
                {
                    //DoJointErrorMessage(successfullyProcessedJoint, "finalizing succesfully procsssed joint " + successfullyProcessedJoint.ObjectNameInScene + " parms " + successfullyProcessedJoint.RawParams);
                    //DoJointErrorMessage(successfullyProcessedJoint, "removing from pending");
                    InternalRemovePendingJoint(successfullyProcessedJoint);
                    //DoJointErrorMessage(successfullyProcessedJoint, "adding to active");
                    InternalAddActiveJoint(successfullyProcessedJoint);
                    //DoJointErrorMessage(successfullyProcessedJoint, "done");
                }
            }
        }

        /// <summary>
        /// Simulate the joint proxies of a NINJA actor.
        /// </summary>
        /// <remarks>
        /// Called as part of the Simulate() loop if NINJA physics is active.  Must only be called from there.
        /// </remarks>
        /// <param name="actor"></param>
        private void SimulateActorPendingJoints(OdePrim actor)
        {
            // If an actor moved, move its joint proxy objects as well.
            // There seems to be an event PhysicsActor.OnPositionUpdate that could be used
            // for this purpose but it is never called! So we just do the joint
            // movement code here.

            if (actor.SOPName != null &&
                joints_connecting_actor.ContainsKey(actor.SOPName) &&
                joints_connecting_actor[actor.SOPName] != null &&
                joints_connecting_actor[actor.SOPName].Count > 0)
            {
                foreach (PhysicsJoint affectedJoint in joints_connecting_actor[actor.SOPName])
                {
                    if (affectedJoint.IsInPhysicsEngine)
                    {
                        DoJointMoved(affectedJoint);
                    }
                    else
                    {
                        DoJointErrorMessage(affectedJoint, "a body connected to a joint was moved, but the joint doesn't exist yet! this will lead to joint error. joint was: " + affectedJoint.ObjectNameInScene + " parms:" + affectedJoint.RawParams);
                    }
                }
            }
        }

        public override void SetTerrain(float[] heightMap)
        {
            if (m_worldOffset != Vector3.Zero && m_parentScene != null)
            {
                if (m_parentScene is OdeScene)
                {
                    ((OdeScene)m_parentScene).SetTerrain(heightMap, m_worldOffset);
                }
            }
            else
            {
                SetTerrain(heightMap, m_worldOffset);
            }
        }

        private void SetTerrain(float[] heightMap, Vector3 pOffset)
        {
            int startTime = Util.EnvironmentTickCount();
            m_log.DebugFormat("[ODE SCENE]: Setting terrain for {0} with offset {1}", PhysicsSceneName, pOffset);


            float[] _heightmap;

            // ok im lasy this are just a aliases
            uint regionsizeX = m_regionWidth;
            uint regionsizeY = m_regionHeight;

            // map is rotated
            uint heightmapWidth = regionsizeY + 2;
            uint heightmapHeight = regionsizeX + 2;

            uint heightmapWidthSamples = heightmapWidth + 1;
            uint heightmapHeightSamples = heightmapHeight + 1;

            _heightmap = new float[heightmapWidthSamples * heightmapHeightSamples];

            const float scale = 1.0f;
            const float offset = 0.0f;
            const float thickness = 10f;
            const int wrap = 0;


            float hfmin = float.MaxValue;
            float hfmax = float.MinValue;
            float val;
            uint xx;
            uint yy;

            uint maxXX = regionsizeX - 1;
            uint maxYY = regionsizeY - 1;

            // flipping map adding one margin all around so things don't fall in edges

            uint xt = 0;
            xx = 0;


            for (uint x = 0; x < heightmapWidthSamples; x++)
            {
                if (x > 1 && xx < maxXX)
                    xx++;
                yy = 0;
                for (uint y = 0; y < heightmapHeightSamples; y++)
                {
                    if (y > 1 && y < maxYY)
                        yy += regionsizeX;

                    val = heightMap[yy + xx];
                    if (val < 0.0f)
                        val = 0.0f;
                    _heightmap[xt + y] = val;

                    if (hfmin > val)
                        hfmin = val;
                    if (hfmax < val)
                        hfmax = val;
                }
                xt += heightmapHeightSamples;
            }

            lock (OdeLock)
            {
                SafeNativeMethods.AllocateODEDataForThread(~0U);

                IntPtr GroundGeom = IntPtr.Zero;
                if (RegionTerrain.TryGetValue(pOffset, out GroundGeom))
                {
                    RegionTerrain.Remove(pOffset);
                    if (GroundGeom != IntPtr.Zero)
                    {
                        if (TerrainHeightFieldHeights.ContainsKey(GroundGeom))
                        {
                            TerrainHeightFieldHeights.Remove(GroundGeom);
                        }
                        SafeNativeMethods.SpaceRemove(space, GroundGeom);
                        SafeNativeMethods.GeomDestroy(GroundGeom);
                    }

                }
                IntPtr HeightmapData = SafeNativeMethods.GeomHeightfieldDataCreate();
                SafeNativeMethods.GeomHeightfieldDataBuildSingle(HeightmapData, _heightmap, 0,
                            heightmapWidth, heightmapHeight,
                            (int)heightmapWidthSamples,
                            (int)heightmapHeightSamples,
                            scale, offset, thickness, wrap);

                SafeNativeMethods.GeomHeightfieldDataSetBounds(HeightmapData, hfmin - 1, hfmax + 1);
                GroundGeom = SafeNativeMethods.CreateHeightfield(space, HeightmapData, 1);
                if (GroundGeom != IntPtr.Zero)
                {
                    SafeNativeMethods.GeomSetCategoryBits(GroundGeom, (int)(CollisionCategories.Land));
                    SafeNativeMethods.GeomSetCollideBits(GroundGeom, (int)(CollisionCategories.Space));

                }
                geom_name_map[GroundGeom] = "Terrain";

                SafeNativeMethods.Matrix3 R = new SafeNativeMethods.Matrix3();

                Quaternion q1 = Quaternion.CreateFromAxisAngle(new Vector3(1, 0, 0), 1.5707f);
                Quaternion q2 = Quaternion.CreateFromAxisAngle(new Vector3(0, 1, 0), 1.5707f);

                q1 = q1 * q2;
                Vector3 v3;
                float angle;
                q1.GetAxisAngle(out v3, out angle);

                SafeNativeMethods.RFromAxisAndAngle(out R, v3.X, v3.Y, v3.Z, angle);
                SafeNativeMethods.GeomSetRotation(GroundGeom, ref R);
                SafeNativeMethods.GeomSetPosition(GroundGeom, pOffset.X + regionsizeX * 0.5f, pOffset.Y + regionsizeY * 0.5f, 0.0f);
                IntPtr testGround = IntPtr.Zero;
                if (RegionTerrain.TryGetValue(pOffset, out testGround))
                {
                    RegionTerrain.Remove(pOffset);
                }
                RegionTerrain.Add(pOffset, GroundGeom, GroundGeom);
                TerrainHeightFieldHeights.Add(GroundGeom,_heightmap);
            }

            m_log.DebugFormat(
                "[ODE SCENE]: Setting terrain for {0} took {1}ms", PhysicsSceneName, Util.EnvironmentTickCountSubtract(startTime));
        }

        public override void DeleteTerrain()
        {
        }

        internal float GetWaterLevel()
        {
            return waterlevel;
        }

        public override void SetWaterLevel(float baseheight)
        {
            waterlevel = baseheight;
        }

        [HandleProcessCorruptedStateExceptions]
        public override void Dispose()
        {
            lock(SimulationLock)
                lock(OdeLock)
            {
                if(world == IntPtr.Zero)
                    return;

                _worldInitialized = false;

                SafeNativeMethods.AllocateODEDataForThread(~0U);

                if (m_rayCastManager != null)
                {
                    m_rayCastManager.Dispose();
                    m_rayCastManager = null;
                }

                lock (_prims)
                {
                    foreach (OdePrim prm in _prims)
                    {
                        RemovePrim(prm);
                    }
                }

                //foreach (OdeCharacter act in _characters)
                //{
                    //RemoveAvatar(act);
                //}
                IntPtr GroundGeom = IntPtr.Zero;
                if (RegionTerrain.TryGetValue(m_worldOffset, out GroundGeom))
                {
                    RegionTerrain.Remove(m_worldOffset);
                    if (GroundGeom != IntPtr.Zero)
                    {
                        if (TerrainHeightFieldHeights.ContainsKey(GroundGeom))
                            TerrainHeightFieldHeights.Remove(GroundGeom);
                        SafeNativeMethods.GeomDestroy(GroundGeom);
                    }
                 }

                try
                {
                    SafeNativeMethods.WorldDestroy(world);
                    world = IntPtr.Zero;
                }
                catch (AccessViolationException e)
                {
                    m_log.ErrorFormat("[ODE SCENE]: exception {0}", e.Message);
                }
            }
        }

        private int compareByCollisionsDesc(OdePrim A, OdePrim B)
        {
            return -A.CollisionScore.CompareTo(B.CollisionScore);
        }

        public override Dictionary<uint, float> GetTopColliders()
        {
            Dictionary<uint, float> topColliders;

            lock (_prims)
            {
                List<OdePrim> orderedPrims = new List<OdePrim>(_prims);
                orderedPrims.Sort(compareByCollisionsDesc);
                topColliders = orderedPrims.Take(25).ToDictionary(p => p.LocalID, p => p.CollisionScore);

                foreach (OdePrim p in _prims)
                    p.CollisionScore = 0;
            }

            return topColliders;
        }

        public override bool SupportsRayCast()
        {
            return true;
        }

        public override void RaycastWorld(Vector3 position, Vector3 direction, float length, RaycastCallback retMethod)
        {
            if (retMethod != null)
            {
                m_rayCastManager.QueueRequest(position, direction, length, retMethod);
            }
        }

        public override void RaycastWorld(Vector3 position, Vector3 direction, float length, int Count, RayCallback retMethod)
        {
            if (retMethod != null)
            {
                m_rayCastManager.QueueRequest(position, direction, length, Count, retMethod);
            }
        }

        public override List<ContactResult> RaycastWorld(Vector3 position, Vector3 direction, float length, int Count)
        {
            ContactResult[] ourResults = null;
            RayCallback retMethod = delegate(List<ContactResult> results)
            {
                ourResults = new ContactResult[results.Count];
                results.CopyTo(ourResults, 0);
            };
            int waitTime = 0;
            m_rayCastManager.QueueRequest(position, direction, length, Count, retMethod);
            while (ourResults == null && waitTime < 1000)
            {
                Thread.Sleep(1);
                waitTime++;
            }
            if (ourResults == null)
                return new List<ContactResult> ();
            return new List<ContactResult>(ourResults);
        }

        public override Dictionary<string, float> GetStats()
        {
            if (!CollectStats)
                return null;

            Dictionary<string, float> returnStats;

            lock (OdeLock)
            {
                returnStats = new Dictionary<string, float>(m_stats);

                // FIXME: This is a SUPER DUMB HACK until we can establish stats that aren't subject to a division by
                // 3 from the SimStatsReporter.
                returnStats[ODETotalAvatarsStatName] = _characters.Count * 3;
                returnStats[ODETotalPrimsStatName] = _prims.Count * 3;
                returnStats[ODEActivePrimsStatName] = _activeprims.Count * 3;

                InitializeExtraStats();
            }

            returnStats[ODEOtherCollisionFrameMsStatName]
                = returnStats[ODEOtherCollisionFrameMsStatName]
                    - returnStats[ODENativeSpaceCollisionFrameMsStatName]
                    - returnStats[ODENativeGeomCollisionFrameMsStatName];

            return returnStats;
        }

        private void InitializeExtraStats()
        {
            m_stats[ODETotalFrameMsStatName] = 0;
            m_stats[ODEAvatarTaintMsStatName] = 0;
            m_stats[ODEPrimTaintMsStatName] = 0;
            m_stats[ODEAvatarForcesFrameMsStatName] = 0;
            m_stats[ODEPrimForcesFrameMsStatName] = 0;
            m_stats[ODERaycastingFrameMsStatName] = 0;
            m_stats[ODENativeStepFrameMsStatName] = 0;
            m_stats[ODENativeSpaceCollisionFrameMsStatName] = 0;
            m_stats[ODENativeGeomCollisionFrameMsStatName] = 0;
            m_stats[ODEOtherCollisionFrameMsStatName] = 0;
            m_stats[ODECollisionNotificationFrameMsStatName] = 0;
            m_stats[ODEAvatarContactsStatsName] = 0;
            m_stats[ODEPrimContactsStatName] = 0;
            m_stats[ODEAvatarUpdateFrameMsStatName] = 0;
            m_stats[ODEPrimUpdateFrameMsStatName] = 0;
        }
    }
}
