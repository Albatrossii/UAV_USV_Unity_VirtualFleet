using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Builds the ROS/Gazebo heterogeneous_332 world used by current main.
    /// Motion remains authoritative in Gazebo and arrives through WebSocket.
    /// </summary>
    public sealed class SimulationBootstrap : MonoBehaviour
    {
        public bool useExternalPoseInEditor = true;
        public bool useWebSocketPose = true;
        public string webSocketUrl = "ws://127.0.0.1:8765/uav_usv";

        private ExternalPoseWebSocketClient receiver;
        private RuntimeCollisionSafety collisionSafety;
        private MultiAgentCaptureDefenseScenario localScenario;
        private Transform[] statusUavs;
        private float[] statusUavHomeHeights;
        private GUIStyle titleStyle;
        private GUIStyle actionStyle;
        private GUIStyle bodyStyle;

        private static readonly Color GazeboGrey = new Color(.36f, .37f, .38f);
        private static readonly Color GazeboDark = new Color(.075f, .08f, .085f);

        // Product manuals, August 2026:
        // M3-F900 unfolded propeller envelope: 1.20 x 1.20 x .55 m.
        // USV-M1500 overall dimensions:        1.50 x 1.10 x .60 m.
        // Vehicles remain 1:1.  Only the large, unspecified environment and
        // the spacing between ROS entities use this presentation scale.
        private const float EnvironmentPresentationScale =
            Coordinates.PresentationCoordinateScale;
        private const float UnspecifiedVesselScale =
            EnvironmentPresentationScale;
        private const float UsvCameraForward = .48f;
        private const float UsvCameraHeight = .52f;
        private const float UavCameraHeight = .18f;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            ConfigureVisualQuality();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(.46f, .54f, .56f);
            RenderSettings.fogDensity = .00105f;

            BuildLighting();
            BuildOcean();
            GameObject islandTerrain = CatalinaIslandRuntime.CreateVisualTerrain(
                EnvironmentEnu(0f, 0f, -.8f),
                EnvironmentPresentationScale
            );
            // Green coastal rim remains outside Gazebo's full ocean boundary.
            SydneyCoastRuntime.CreateVisualBackdrop(
                Vector3.zero,
                EnvironmentPresentationScale
            );
            Transform[] uavPads = BuildIslandUavBase();
            BuildShoreCommandBase();

            Vector3[] usvPos =
            {
                PresentationEnu(-120f, -305f, 0f),
                PresentationEnu(-75f, -320f, 0f),
                PresentationEnu(-30f, -305f, 0f)
            };
            float[] usvYaw = { .10f, .05f, -.05f };
            Color usvRed = new Color(.86f, .035f, .025f);

            var usvs = new Transform[3];
            for (int i = 0; i < usvs.Length; i++)
            {
                usvs[i] = BuildUsv("usv_0" + (i + 1), usvRed);
                Place(usvs[i], usvPos[i], usvYaw[i]);
            }

            var uavs = new Transform[3];
            for (int i = 0; i < uavs.Length; i++)
            {
                uavs[i] = BuildUav("uav_0" + (i + 1));
                PlaceUavOnPad(uavs[i], uavPads[i], .559f);
            }
            statusUavs = uavs;
            statusUavHomeHeights = new float[uavs.Length];
            for (int i = 0; i < uavs.Length; i++)
                statusUavHomeHeights[i] = uavs[i].position.y;

            Transform friendly = BuildFriendlyShip();
            Place(friendly, PresentationEnu(-150f, -355f, 0f), .25f);
            Transform enemy = BuildEnemyShip();
            Place(enemy, PresentationEnu(-80f, -345f, 0f), 2.60f);

            bool externalSync =
                !HasArgument("--local-demo") &&
                (
                    HasArgument("--ros-sync") ||
                    HasArgument("--ros-ws") ||
                    (Application.isEditor && useExternalPoseInEditor)
                );
            if (externalSync && useWebSocketPose)
            {
                receiver = gameObject.AddComponent<ExternalPoseWebSocketClient>();
                receiver.serverUrl = ArgumentValue("--ros-ws-url=", webSocketUrl);
                receiver.boat = usvs[0];
                receiver.drone = uavs[0];
                receiver.boats = usvs;
                receiver.drones = uavs;
                receiver.friendlyShip = friendly;
                receiver.targetVessel = enemy;
            }
            else
            {
                // Local demo only: Gazebo wave follower is authoritative in ROS sync.
                foreach (Transform usv in usvs)
                    usv.gameObject.AddComponent<BoatWaveMotion>();
                friendly.gameObject.AddComponent<BoatWaveMotion>();
                enemy.gameObject.AddComponent<BoatWaveMotion>();
            }

            var collisionVessels = new Transform[usvs.Length + 2];
            for (int i = 0; i < usvs.Length; i++)
                collisionVessels[i] = usvs[i];
            collisionVessels[usvs.Length] = friendly;
            collisionVessels[usvs.Length + 1] = enemy;
            collisionSafety = gameObject.AddComponent<RuntimeCollisionSafety>();
            collisionSafety.Configure(
                islandTerrain ? islandTerrain.transform : null,
                collisionVessels,
                uavs,
                externalSync
            );

            BuildCamera(usvs, uavs, friendly, enemy);
            localScenario = FindObjectOfType<MultiAgentCaptureDefenseScenario>();
        }

        private static void ConfigureVisualQuality()
        {
            int highestQuality = QualitySettings.names.Length - 1;
            if (highestQuality >= 0 && QualitySettings.GetQualityLevel() < highestQuality)
                QualitySettings.SetQualityLevel(highestQuality, true);

            QualitySettings.antiAliasing = Application.platform == RuntimePlatform.WebGLPlayer ? 4 : 8;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.pixelLightCount = 6;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowDistance = 720f;
            QualitySettings.shadowNearPlaneOffset = 2f;
            QualitySettings.softParticles = true;
            QualitySettings.realtimeReflectionProbes = true;
            QualitySettings.lodBias = 2f;
        }

        private static void BuildLighting()
        {
            Material skyTemplate =
                Resources.Load<Material>("Sky/PureOceanSky");
            Material sky = skyTemplate ? new Material(skyTemplate) : null;
            if (!sky)
            {
                Shader skyShader = Resources.Load<Shader>("MaritimeSky") ??
                                   Shader.Find("UavUsv/MaritimeSky");
                if (skyShader)
                {
                    sky = new Material(skyShader)
                    {
                        name = "Runtime Maritime Sky"
                    };
                    sky.SetFloat("_CloudSpeed", .012f);
                    sky.SetFloat("_CloudAmount", .62f);
                    sky.SetFloat("_Exposure", 1.15f);
                }
            }

            if (sky)
            {
                sky.name = "heterogeneous_332 Maritime Sky";
                if (sky.HasProperty("_Exposure"))
                    sky.SetFloat("_Exposure", 1.12f);
                if (sky.HasProperty("_Rotation"))
                    sky.SetFloat("_Rotation", 72f);
                if (sky.HasProperty("_CloudAmount"))
                    sky.SetFloat("_CloudAmount", .55f);
                RenderSettings.skybox = sky;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
                RenderSettings.ambientIntensity = 1.05f;
                RenderSettings.defaultReflectionMode =
                    UnityEngine.Rendering.DefaultReflectionMode.Skybox;
                RenderSettings.defaultReflectionResolution = 256;
                RenderSettings.reflectionIntensity = .55f;
                RenderSettings.reflectionBounces = 1;
                DynamicGI.UpdateEnvironment();
            }
            else
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(.68f, .78f, .88f);
                RenderSettings.ambientEquatorColor = new Color(.42f, .52f, .58f);
                RenderSettings.ambientGroundColor = new Color(.16f, .20f, .22f);
            }

            // Direction matches Gazebo sun: direction -0.35 0.2 -0.92
            Light sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(.96f, .94f, .88f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .78f;
            sun.shadowBias = .035f;
            sun.shadowNormalBias = .25f;
            sun.renderMode = LightRenderMode.ForcePixel;
            sun.transform.rotation = Quaternion.LookRotation(
                new Vector3(-.35f, -.92f, .20f).normalized
            );
            RenderSettings.sun = sun;
        }

        private static void BuildOcean()
        {
            var water = new GameObject(
                "ocean_plane",
                typeof(MeshFilter),
                typeof(MeshRenderer),
                typeof(OceanSurface),
                typeof(PlanarWaterReflection)
            );
            water.layer = 4;
            // Match Gazebo waves pose z=0.015.
            water.transform.position = new Vector3(0f, .015f, 0f);
            MeshRenderer renderer = water.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.reflectionProbeUsage =
                UnityEngine.Rendering.ReflectionProbeUsage.Off;
            OceanSurface ocean = water.GetComponent<OceanSurface>();
            // Visual ocean is much larger than Gazebo's 1050 x 900 ops domain so
            // chase / overview cameras never see a clipped water edge (sky gap).
            ocean.width = 2800f;
            ocean.length = 2600f;
            ocean.resolution = 420;
            ocean.edgeIrregularity = 0f;
            ocean.waveAmplitude = .12f;
            ocean.windSpeed = 6.5f;
            ocean.windDirectionDegrees = 35f;

            PlanarWaterReflection reflection =
                water.GetComponent<PlanarWaterReflection>();
            if (reflection)
                Object.Destroy(reflection);

            BuildHorizonOceanFill();
        }

        private static void BuildHorizonOceanFill()
        {
            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Plane);
            fill.name = "horizon_ocean_fill";
            Object.Destroy(fill.GetComponent<Collider>());
            fill.transform.position = new Vector3(0f, -.08f, 0f);
            fill.transform.localScale = new Vector3(600f, 1f, 600f);
            fill.layer = 4;

            var shader = Resources.Load<Shader>("WindOcean") ??
                         Shader.Find("UavUsv/WindOcean");
            Material material;
            if (shader)
            {
                material = new Material(shader)
                {
                    name = "Horizon Ocean Fill"
                };
                material.SetColor("_DeepColor", new Color(.05f, .12f, .16f, 1f));
                material.SetColor("_ShallowColor", new Color(.13f, .21f, .23f, 1f));
                material.SetFloat("_WaveAmplitude", 0f);
            }
            else
            {
                material = new Material(Shader.Find("Unlit/Color"))
                {
                    name = "Horizon Ocean Fill",
                    color = new Color(.05f, .12f, .16f, 1f)
                };
            }

            MeshRenderer fillRenderer = fill.GetComponent<MeshRenderer>();
            fillRenderer.sharedMaterial = material;
            fillRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            fillRenderer.receiveShadows = false;
            fillRenderer.reflectionProbeUsage =
                UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private static Transform[] BuildIslandUavBase()
        {
            Transform root = new GameObject("island_uav_base").transform;
            Place(root, EnvironmentEnu(-75f, -215f, 0f), .559f);
            root.localScale = Vector3.one * EnvironmentPresentationScale;

            Material foundation = Mat("Base foundation", new Color(.28f, .3f, .31f));
            Material deck = Mat("Base flight deck", new Color(.19f, .2f, .21f));
            Material legs = Mat("Base columns", new Color(.32f, .34f, .35f), .25f);
            Material pad = Mat("Landing pads", new Color(.34f, .35f, .35f));
            Material white = Mat("Landing H", Color.white);
            Material edge = Mat("Safety yellow", new Color(.98f, .75f, .04f));

            Box("foundation", root, 0f, 0f, 1.5f, 44f, 15f, 3f, foundation);
            Box("flight_deck", root, 0f, 0f, 19f, 44f, 15f, 1f, deck);
            foreach (float x in new[] { -18f, 0f, 18f })
            foreach (float y in new[] { -5f, 5f })
                Box("support_column", root, x, y, 10.6f, 1.2f, 1.2f, 16.8f, legs);

            var pads = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                float x = -14f + 14f * i;
                Transform p = new GameObject("pad_0" + (i + 1)).transform;
                p.SetParent(root, false);
                p.localPosition = LocalEnu(x, 0f, 19.54f);
                pads[i] = p;
                Cylinder("pad_surface", p, 0f, 0f, 0f, 5.5f, .08f, pad);
                // Match Gazebo sim332_island_uav_base H marks (two uprights + cross).
                Box("H_left", p, -1.8f, 0f, .06f, .8f, 5f, .08f, white);
                Box("H_right", p, 1.8f, 0f, .06f, .8f, 5f, .08f, white);
                Box("H_cross", p, 0f, 0f, .07f, 3.6f, .8f, .08f, white);
                Cylinder("yellow_edge", p, 0f, 0f, .05f, 5.6f, .035f, edge);
            }
            return pads;
        }

        private static void BuildShoreCommandBase()
        {
            Transform root = new GameObject("shore_command_base").transform;
            Place(root, EnvironmentEnu(-35f, -190f, 17.5f), .559f);
            root.localScale = Vector3.one * EnvironmentPresentationScale;

            Material concrete = Mat("Command concrete", new Color(.48f, .47f, .44f));
            Material wall = Mat("Command building", new Color(.74f, .7f, .6f));
            Material roof = Mat("Command roof", new Color(.16f, .18f, .2f));
            Material glass = Mat("Command windows", new Color(.035f, .23f, .34f), .15f, .82f);
            Material metal = Mat("Command metal", new Color(.32f, .34f, .35f), .35f);
            Material yellow = Mat("Command safety rail", new Color(.98f, .75f, .04f));

            Box("foundation", root, 0f, 0f, .5f, 28f, 20f, 1f, concrete);
            Box("building", root, 0f, 0f, 4.2f, 20f, 14f, 7.2f, wall);
            Box("roof", root, 0f, 0f, 8f, 22f, 16f, .5f, roof);
            Box("front_windows", root, 10.04f, 0f, 5.1f, .08f, 9f, 2.2f, glass);
            Box("bridge_to_pad", root, -24f, 0f, -1.5f, 50f, 5f, .5f, concrete, 0f, -.06f, 0f);
            Box("bridge_rail_left", root, -24f, 2.35f, -.65f, 50f, .12f, 1.2f, yellow, 0f, -.06f, 0f);
            Box("bridge_rail_right", root, -24f, -2.35f, -.65f, 50f, .12f, 1.2f, yellow, 0f, -.06f, 0f);
            Cylinder("radar_tower", root, 3f, 0f, 13f, .5f, 10f, metal);
            Cylinder("radar_pedestal", root, 3f, 0f, 18.2f, 1.2f, .5f, metal);
            SceneFactory.Primitive(
                "radar_dish",
                PrimitiveType.Sphere,
                root,
                LocalEnu(3f, 0f, 19.2f),
                new Vector3(3.8f, .45f, 2.3f),
                metal,
                new Vector3(0f, 0f, -22f)
            );
            Cylinder("antenna_mast", root, -5f, 3f, 13.5f, .18f, 10f, metal);
            Box("generator", root, -6f, -5f, 9.1f, 3.8f, 2.6f, 1.8f, roof);
            Box("equipment_console", root, 5f, -4.8f, 9f, 3f, 1.5f, 1.6f, metal);
        }

        private static Transform BuildUsv(string name, Color idColor)
        {
            Transform root = new GameObject(name).transform;
            root.localScale = Vector3.one;
            Material hull = Mat(name + " aluminium hull", new Color(.84f, .87f, .88f), .36f, .56f);
            Material lower = Mat(name + " lower hull", new Color(.055f, .075f, .09f), .22f, .5f);
            Material deck = Mat(name + " deck", new Color(.16f, .18f, .19f), .1f, .4f);
            Material cabin = Mat(name + " equipment bay", new Color(.9f, .91f, .88f), .08f, .34f);
            Material window = Mat(name + " camera glass", new Color(.025f, .25f, .38f), .16f, .9f);
            Material id = Mat(
                name + " ID " + ColorUtility.ToHtmlStringRGB(idColor),
                idColor,
                .12f,
                .55f
            );

            // Twin 1.5 m aluminium hull, overall width 1.1 m.
            foreach (float side in new[] { -.39f, .39f })
            {
                SceneFactory.Primitive(
                    "pontoon_lower",
                    PrimitiveType.Cube,
                    root,
                    new Vector3(-.17f, .10f, side),
                    new Vector3(1.16f, .20f, .30f),
                    lower
                );
                SceneFactory.Cone(
                    "pontoon_bow",
                    root,
                    new Vector3(.58f, .15f, side),
                    .15f,
                    .34f,
                    hull,
                    new Vector3(0f, 0f, -90f)
                );
                SceneFactory.Primitive(
                    "pontoon_cap",
                    PrimitiveType.Cube,
                    root,
                    new Vector3(-.12f, .19f, side),
                    new Vector3(1.25f, .08f, .32f),
                    hull
                );
            }

            SceneFactory.Primitive("cross_deck", PrimitiveType.Cube, root, new Vector3(-.08f, .245f, 0f), new Vector3(1.12f, .07f, .92f), deck);
            SceneFactory.Primitive("equipment_bay", PrimitiveType.Cube, root, new Vector3(-.12f, .37f, 0f), new Vector3(.58f, .20f, .43f), cabin);
            SceneFactory.Primitive("equipment_lid", PrimitiveType.Cube, root, new Vector3(-.12f, .485f, 0f), new Vector3(.62f, .035f, .47f), id);
            SceneFactory.Primitive("d435_camera", PrimitiveType.Cube, root, new Vector3(.19f, .40f, 0f), new Vector3(.045f, .05f, .15f), window);
            SceneFactory.Primitive("jetson_enclosure", PrimitiveType.Cube, root, new Vector3(-.38f, .39f, 0f), new Vector3(.16f, .10f, .22f), deck);
            SceneFactory.Primitive("rtk_antenna", PrimitiveType.Cylinder, root, new Vector3(-.34f, .54f, -.13f), new Vector3(.055f, .04f, .055f), deck);
            SceneFactory.Primitive("mid360s", PrimitiveType.Cylinder, root, new Vector3(-.03f, .56f, .13f), new Vector3(.075f, .04f, .075f), id);

            // Put the lower part of the 0.6 m envelope below the waterline.
            for (int i = 0; i < root.childCount; i++)
                root.GetChild(i).localPosition += Vector3.down * .08f;

            ProductSpecification specification =
                root.gameObject.AddComponent<ProductSpecification>();
            specification.productModel = "USV-M1500";
            specification.overallDimensionsMeters = new Vector3(1.5f, .6f, 1.1f);
            specification.massKilograms = 35f;
            specification.payloadKilograms = 30f;
            specification.maximumSpeedMetersPerSecond = 2f;
            specification.endurance = "1-2 h";
            specification.installedEquipment =
                "Pixhawk6C mini; UM982 RTK; BT-560 x2; Jetson Orin Nano; D435; MID360S";
            specification.source = "无人船产品画册1.pdf · USV-M1500";
            return root;
        }

        private static Transform BuildFriendlyShip()
        {
            Transform root = new GameObject("friendly_ship").transform;
            root.localScale = Vector3.one * UnspecifiedVesselScale;
            Material red = Mat("Friendly red", new Color(.82f, .035f, .02f), .08f);
            Material yellow = Mat("Friendly yellow", new Color(.98f, .65f, .025f));
            Material dark = Mat("Friendly dark", GazeboDark, .2f);
            Material glass = Mat("Friendly windows", new Color(.025f, .16f, .22f), .15f, .88f);

            Box("lower_hull", root, -.3f, 0f, .25f, 13.5f, 4.7f, 1f, red);
            Box("upper_hull", root, -.6f, 0f, .85f, 12.4f, 5.1f, .55f, yellow);
            Box("main_deck", root, -.7f, 0f, 1.18f, 11.5f, 4.3f, .18f, dark);
            Box("cabin", root, -2.1f, 0f, 2.25f, 4.2f, 3.55f, 1.9f, yellow);
            Box("bridge_windows", root, .03f, 0f, 2.55f, .08f, 2.9f, .65f, glass);
            Box("roof", root, -2.2f, 0f, 3.3f, 4.8f, 4f, .22f, red);
            Cylinder("mast", root, -2.1f, 0f, 4.65f, .12f, 2.6f, dark);
            Box("radar", root, -2.1f, 0f, 5.75f, 2.2f, .18f, .12f, yellow);
            Box("port_stripe", root, -.2f, 2.42f, .92f, 9.8f, .08f, .35f, yellow);
            Box("starboard_stripe", root, -.2f, -2.42f, .92f, 9.8f, .08f, .35f, yellow);
            return root;
        }

        private static Transform BuildEnemyShip()
        {
            Transform root = new GameObject("enemy_ship").transform;
            root.localScale = Vector3.one * UnspecifiedVesselScale;
            Material hull = Mat(
                "Enemy near-black hull",
                new Color(.035f, .042f, .05f),
                .28f,
                .5f
            );
            Material upperHull = Mat(
                "Enemy charcoal upper hull",
                new Color(.105f, .115f, .125f),
                .2f,
                .42f
            );
            Material deck = Mat(
                "Enemy graphite deck",
                new Color(.16f, .17f, .18f),
                .12f,
                .32f
            );
            Material cabin = Mat(
                "Enemy gunmetal cabin",
                new Color(.22f, .23f, .235f),
                .24f,
                .46f
            );
            Material trim = Mat(
                "Enemy grey trim",
                new Color(.34f, .35f, .35f),
                .22f,
                .38f
            );
            Material glass = Mat(
                "Enemy blue-black windows",
                new Color(.025f, .12f, .17f),
                .2f,
                .94f
            );

            // A 16.5 x 5.8 m patrol vessel, about 22% longer than the friendly
            // 13.5 m workboat. Dark materials retain enough tonal separation
            // for the hull, bridge and equipment to remain readable at range.
            Box("lower_hull", root, -.45f, 0f, .38f, 15.2f, 4.9f, .95f, hull);
            Box("upper_hull", root, -.75f, 0f, .92f, 14.5f, 5.4f, .62f, upperHull);
            Box("main_deck", root, -.9f, 0f, 1.28f, 13.6f, 4.7f, .2f, deck);
            Box("port_bow", root, 7.25f, 1.25f, .76f, 2.5f, .55f, 1.12f, hull, 0f, 0f, .4f);
            Box("starboard_bow", root, 7.25f, -1.25f, .76f, 2.5f, .55f, 1.12f, hull, 0f, 0f, -.4f);

            Box("bridge", root, -2.25f, 0f, 2.34f, 5.25f, 3.75f, 1.85f, cabin);
            Box("front_windows", root, .4f, 0f, 2.58f, .08f, 3.15f, .66f, glass);
            Box("port_windows", root, -2.15f, 1.9f, 2.58f, 3.8f, .07f, .64f, glass);
            Box("starboard_windows", root, -2.15f, -1.9f, 2.58f, 3.8f, .07f, .64f, glass);
            Box("bridge_roof", root, -2.25f, 0f, 3.38f, 5.85f, 4.2f, .24f, hull);

            Box("aft_equipment", root, -5.45f, 0f, 1.75f, 1.35f, 2.6f, .72f, upperHull);
            Box("fore_equipment", root, 2.25f, 0f, 1.55f, 1.1f, 1.7f, .38f, upperHull);
            Cylinder("mast", root, -2.55f, 0f, 4.72f, .13f, 2.55f, trim);
            Box("radar_bar", root, -2.55f, 0f, 5.85f, 2.65f, .2f, .14f, trim);
            Cylinder("radar_dome", root, -1.25f, 0f, 4.05f, .28f, .3f, trim);

            Box("port_rub_rail", root, -.3f, 2.72f, .92f, 12.8f, .08f, .32f, trim);
            Box("starboard_rub_rail", root, -.3f, -2.72f, .92f, 12.8f, .08f, .32f, trim);
            return root;
        }

        private static Transform BuildUav(string name)
        {
            Transform root = new GameObject(name).transform;
            root.localScale = Vector3.one;
            Material carbon = Mat(name + " carbon", new Color(.025f, .03f, .035f), .35f, .65f);
            Material accent = Mat(name + " red", new Color(.88f, .08f, .02f), .1f, .48f);
            Material sensor = Mat(name + " sensor", new Color(.24f, .27f, .29f), .2f, .58f);
            Material glass = Mat(name + " camera glass", new Color(.03f, .22f, .32f), .18f, .9f);
            var rotors = new List<Transform>();

            Vector3[] motorPositions =
            {
                new Vector3(.318f, .405f, -.318f),
                new Vector3(-.318f, .405f, .318f),
                new Vector3(.318f, .405f, .318f),
                new Vector3(-.318f, .405f, -.318f)
            };

            SceneFactory.Primitive("sealed_carbon_body", PrimitiveType.Cube, root, new Vector3(0f, .325f, 0f), new Vector3(.43f, .16f, .32f), carbon);
            SceneFactory.Primitive("top_cover", PrimitiveType.Cube, root, new Vector3(0f, .425f, 0f), new Vector3(.31f, .05f, .25f), accent);
            SceneFactory.Primitive("jetson_orin_nano", PrimitiveType.Cube, root, new Vector3(-.07f, .235f, 0f), new Vector3(.14f, .055f, .11f), sensor);
            SceneFactory.Primitive("san_60_m2", PrimitiveType.Cube, root, new Vector3(.105f, .225f, 0f), new Vector3(.075f, .07f, .075f), sensor);
            SceneFactory.Primitive("d435", PrimitiveType.Cube, root, new Vector3(.225f, .285f, 0f), new Vector3(.025f, .045f, .09f), glass);
            SceneFactory.Primitive("rtk_antenna", PrimitiveType.Cylinder, root, new Vector3(-.08f, .505f, 0f), new Vector3(.045f, .045f, .045f), sensor);

            for (int i = 0; i < motorPositions.Length; i++)
            {
                Vector3 motor = motorPositions[i];
                float armYaw = Mathf.Atan2(motor.x, motor.z) * Mathf.Rad2Deg;
                SceneFactory.Primitive(
                    "folding_arm_" + i,
                    PrimitiveType.Cube,
                    root,
                    Vector3.Lerp(new Vector3(0f, .37f, 0f), motor, .5f),
                    new Vector3(.042f, .035f, .45f),
                    carbon,
                    new Vector3(0f, armYaw, 0f)
                );
                SceneFactory.Primitive(
                    "motor_" + i,
                    PrimitiveType.Cylinder,
                    root,
                    motor,
                    new Vector3(.075f, .055f, .075f),
                    i % 2 == 0 ? accent : carbon
                );

                Transform rotor = new GameObject("propeller_" + i).transform;
                rotor.SetParent(root, false);
                rotor.localPosition = motor + Vector3.up * .042f;
                SceneFactory.Primitive("blade_a", PrimitiveType.Cube, rotor, Vector3.zero, new Vector3(.56f, .009f, .024f), carbon);
                SceneFactory.Primitive("blade_b", PrimitiveType.Cube, rotor, Vector3.zero, new Vector3(.024f, .009f, .56f), carbon);
                rotors.Add(rotor);
            }

            // Landing gear is entirely below the fuselage; its skids define
            // y=0 so placement on a pad cannot leave the legs hanging through it.
            foreach (float side in new[] { -.19f, .19f })
            {
                SceneFactory.Primitive("landing_leg_front", PrimitiveType.Cube, root, new Vector3(.13f, .145f, side), new Vector3(.025f, .27f, .025f), carbon, new Vector3(0f, 0f, -10f * Mathf.Sign(side)));
                SceneFactory.Primitive("landing_leg_rear", PrimitiveType.Cube, root, new Vector3(-.13f, .145f, side), new Vector3(.025f, .27f, .025f), carbon, new Vector3(0f, 0f, 10f * Mathf.Sign(side)));
                SceneFactory.Primitive("landing_skid", PrimitiveType.Cube, root, new Vector3(0f, .018f, side), new Vector3(.42f, .025f, .025f), carbon);
            }

            DroneVisual visual = root.gameObject.AddComponent<DroneVisual>();
            visual.rotors = rotors.ToArray();
            visual.spinning = true;

            ProductSpecification specification =
                root.gameObject.AddComponent<ProductSpecification>();
            specification.productModel = "M3-F900";
            specification.overallDimensionsMeters = new Vector3(1.2f, .55f, 1.2f);
            specification.massKilograms = 4.1f;
            specification.payloadKilograms = 4.5f;
            specification.maximumSpeedMetersPerSecond = 15f;
            specification.endurance =
                "37 min empty; 18 min @ 3.5 kg; 12 min @ 4.5 kg";
            specification.installedEquipment =
                "Pixhawk6C mini; Jetson Orin Nano 16G; SAN-60 M2; D435";
            specification.source = "无人机产品手册.pdf · M3-F900";
            return root;
        }

        private static void PlaceUavOnPad(
            Transform uav,
            Transform pad,
            float yawRadians)
        {
            if (!uav || !pad)
                return;

            Quaternion rotation = Quaternion.Euler(
                0f,
                -yawRadians * Mathf.Rad2Deg,
                0f
            );
            uav.SetPositionAndRotation(pad.position, rotation);

            Renderer[] renderers = uav.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            // Pad top is 0.08 m above the pad transform in local environment
            // units and inherits the environment presentation scale.
            float padSurface = pad.position.y + .08f *
                               EnvironmentPresentationScale;
            uav.position += Vector3.up * (padSurface - bounds.min.y + .006f);
        }

        private static Transform InstantiatePx4Part(
            GameObject prefab,
            Transform parent,
            string name,
            Vector3 position,
            Quaternion rotation)
        {
            if (!prefab)
                return null;

            GameObject instance = Instantiate(prefab, parent);
            instance.name = name;
            instance.transform.localPosition = position;
            instance.transform.localRotation = rotation;
            RemoveImportedCamerasAndLights(instance);
            return instance.transform;
        }

        private static void RemoveImportedCamerasAndLights(GameObject instance)
        {
            foreach (Camera camera in instance.GetComponentsInChildren<Camera>(true))
                Destroy(camera.gameObject);
            foreach (Light light in instance.GetComponentsInChildren<Light>(true))
                Destroy(light.gameObject);
        }

        private static void CenterImportedMesh(
            Transform instance,
            Vector3 targetWorldPosition)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            instance.position += targetWorldPosition - bounds.center;
        }

        private void BuildCamera(
            Transform[] usvs,
            Transform[] uavs,
            Transform friendly,
            Transform enemy)
        {
            GameObject go = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera camera = go.AddComponent<Camera>();
            camera.fieldOfView = 48f;
            camera.nearClipPlane = .2f;
            camera.farClipPlane = 5500f;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(.42f, .62f, .74f);
            go.transform.position = PresentationEnu(-190f, -455f, 48f);
            go.transform.LookAt(PresentationEnu(-78f, -290f, 4f));

            ChaseCamera chase = go.AddComponent<ChaseCamera>();
            chase.target = usvs[0];
            chase.companion = uavs[0];
            chase.lookAt = enemy;
            chase.distance = 8f;
            chase.height = 4.2f;
            chase.sideOffset = -1.2f;
            chase.minDistance = 5.5f;
            chase.maxDistance = 24f;
            chase.minHeight = 2.8f;
            chase.maxHeight = 10f;
            chase.lookAhead = 2.4f;
            chase.lookHeight = .8f;
            chase.showTacticalInset = false;
            chase.actionYaw = -42f;
            chase.actionPitch = 28f;
            chase.actionWorldPadding = 1.8f;
            chase.actionFitPadding = 1.04f;
            chase.actionMinDistance = 12f;
            chase.actionMaxDistance = 82f;
            chase.actionSecondBoatRadius = 18f;
            chase.actionAllBoatsRadius = 15f;
            chase.actionNearestDroneRadius = 26f;
            chase.actionAllDronesRadius = 20f;
            chase.overviewWorldPadding = 4f;
            chase.overviewMinDistance = 24f;
            chase.overviewMaxDistance = 140f;
            var subjects = new List<Transform>();
            subjects.AddRange(usvs);
            subjects.AddRange(uavs);
            subjects.Add(friendly);
            subjects.Add(enemy);
            chase.SetGroupTargets(subjects.ToArray());

            GazeboComparisonCamera comparison =
                go.AddComponent<GazeboComparisonCamera>();
            comparison.comparisonActive = false;
            comparison.positionEnu = new Vector3(-430f, -560f, 420f);
            comparison.rollRadians = 0f;
            comparison.pitchRadians = .78f;
            comparison.yawRadians = .72f;
            comparison.horizontalFovDegrees = 90f;

            SensorViewPip pip = go.AddComponent<SensorViewPip>();
            pip.poseClient = receiver;
            pip.usvs = usvs;
            pip.uavs = uavs;
            pip.lookAt = enemy;
            pip.visible = true;
            pip.preferGazeboStream = true;
            pip.usvCameraHeight = UsvCameraHeight;
            pip.usvCameraForward = UsvCameraForward;
            pip.uavCameraHeight = UavCameraHeight;
            pip.activeView = SensorViewPip.SensorView.Usv01Forward;
        }

        private void OnGUI()
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return;

            // IMGUI is rendered into the selected Game resolution first. A
            // moderate 4K scale keeps this compact card readable when the Game
            // tab itself is displayed at roughly half size.
            float uiScale = Mathf.Clamp(
                Mathf.Sqrt(Screen.height / 1080f),
                1f,
                1.35f
            );
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(.96f, .98f, 1f) }
            };
            actionStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, .83f, .30f) }
            };
            titleStyle.fontSize = Mathf.RoundToInt(20f * uiScale);
            actionStyle.fontSize = Mathf.RoundToInt(16f * uiScale);
            bodyStyle.fontSize = Mathf.RoundToInt(15f * uiScale);
            actionStyle.alignment = TextAnchor.MiddleLeft;
            bodyStyle.alignment = TextAnchor.MiddleLeft;

            GUI.Box(
                new Rect(
                    16f * uiScale,
                    16f * uiScale,
                    470f * uiScale,
                    194f * uiScale
                ),
                ""
            );
            GUI.Label(
                new Rect(
                    30f * uiScale,
                    25f * uiScale,
                    430f * uiScale,
                    30f * uiScale
                ),
                "M3-F900 ×3  ·  USV-M1500 ×3",
                titleStyle
            );

            string[] statusLines = receiver
                ? new[]
                {
                    CurrentActionDisplay(),
                    collisionSafety
                        ? collisionSafety.StatusDisplay
                        : "安全：碰撞系统未初始化",
                    ConnectionDisplay(receiver),
                    CompactStatus(receiver.fleetStatus, 38),
                    CompactStatus(receiver.cameraStatus, 38)
                }
                : new[]
                {
                    CurrentActionDisplay(),
                    collisionSafety
                        ? collisionSafety.StatusDisplay
                        : "安全：碰撞系统未初始化",
                    "连接：未启用 ROS 同步",
                    "舰队：等待数据",
                    "相机：未连接"
                };
            float lineHeight = 25f * uiScale;
            for (int i = 0; i < statusLines.Length; i++)
            {
                GUI.Label(
                    new Rect(
                        30f * uiScale,
                        (58f + i * 24f) * uiScale,
                        430f * uiScale,
                        lineHeight
                    ),
                    statusLines[i],
                    i == 0 ? actionStyle : bodyStyle
                );
            }
        }

        private string CurrentActionDisplay()
        {
            if (!localScenario)
                localScenario = FindObjectOfType<MultiAgentCaptureDefenseScenario>();

            // In local demonstration mode the scenario component owns the
            // algorithm phase, so display its exact status text.
            if (!receiver && localScenario)
            {
                return "当前行动：" + CompactStatus(
                    localScenario.Status,
                    32
                );
            }

            if (!receiver || !receiver.isConnected)
                return "当前行动：等待 ROS 任务";

            string mission = receiver.missionStatus ?? "";
            if (mission.Contains("防卫成功"))
                return "当前行动：护航防卫完成";
            if (mission.Contains("防卫"))
                return "当前行动：海空编队护航防卫";
            if (mission.Contains("失败"))
                return "当前行动：任务异常 · 等待重新调度";
            if (mission.Contains("成功"))
                return "当前行动：围捕完成 · 目标已锁定";
            if (mission.Contains("保持"))
                return "当前行动：保持围捕编队 · UAV 警戒";
            if (mission.Contains("围捕"))
            {
                return AllStatusUavsAirborne()
                    ? "当前行动：海空协同围捕执行中"
                    : "当前行动：UAV 编队起飞 · USV 缩圈";
            }
            if (mission.Contains("接近"))
                return "当前行动：USV 编队接近目标";
            if (mission.Contains("跟踪"))
                return "当前行动：发现目标 · 持续跟踪上报";
            if (mission.Contains("搜索"))
                return "当前行动：USV 分区搜索目标";
            if (mission.Contains("等待"))
                return "当前行动：等待任务下发";

            return "当前行动：" + CompactStatus(
                mission.Replace("任务：", ""),
                32
            );
        }

        private bool AllStatusUavsAirborne()
        {
            if (statusUavs == null || statusUavHomeHeights == null ||
                statusUavs.Length == 0 ||
                statusUavs.Length != statusUavHomeHeights.Length)
                return false;

            // Presentation-space 0.28 m corresponds to about 1.56 real metres,
            // enough to distinguish a true launch from wave/pose smoothing.
            for (int i = 0; i < statusUavs.Length; i++)
            {
                if (!statusUavs[i] ||
                    statusUavs[i].position.y <
                    statusUavHomeHeights[i] + .28f)
                    return false;
            }
            return true;
        }

        private static string ConnectionDisplay(
            ExternalPoseWebSocketClient client)
        {
            if (!client)
                return "连接：未启用 ROS 同步";
            if (!client.isConnected)
                return "连接：离线 · 自动重连中";

            string raw = client.connectionStatus ?? "";
            int sequence = raw.LastIndexOf("seq ", System.StringComparison.Ordinal);
            return sequence >= 0
                ? "连接：在线 · 数据序号 " + raw.Substring(sequence + 4)
                : "连接：在线 · ROS WebSocket 正常";
        }

        private static string CompactStatus(string value, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "等待状态";

            string compact = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return compact.Length <= maxCharacters
                ? compact
                : compact.Substring(0, maxCharacters - 1) + "…";
        }

        private static Material Mat(
            string name,
            Color color,
            float metallic = 0f,
            float smoothness = .35f)
        {
            return SceneFactory.Material(name, color, metallic, smoothness);
        }

        private static GameObject Box(
            string name,
            Transform parent,
            float x,
            float y,
            float z,
            float sx,
            float sy,
            float sz,
            Material material,
            float roll = 0f,
            float pitch = 0f,
            float yaw = 0f)
        {
            return SceneFactory.Primitive(
                name,
                PrimitiveType.Cube,
                parent,
                LocalEnu(x, y, z),
                new Vector3(sx, sz, sy),
                material,
                new Vector3(-pitch * Mathf.Rad2Deg, -yaw * Mathf.Rad2Deg, roll * Mathf.Rad2Deg)
            );
        }

        private static GameObject Cylinder(
            string name,
            Transform parent,
            float x,
            float y,
            float z,
            float radius,
            float length,
            Material material)
        {
            return SceneFactory.Primitive(
                name,
                PrimitiveType.Cylinder,
                parent,
                LocalEnu(x, y, z),
                new Vector3(radius * 2f, length * .5f, radius * 2f),
                material
            );
        }

        private static Vector3 Enu(float x, float y, float z)
        {
            return Coordinates.ToUnity(x, y, z);
        }

        private static Vector3 PresentationEnu(float x, float y, float z)
        {
            return Coordinates.ToPresentation(x, y, z);
        }

        private static Vector3 EnvironmentEnu(float x, float y, float z)
        {
            return Coordinates.ToPresentationEnvironment(x, y, z);
        }

        private static Vector3 LocalEnu(float x, float y, float z)
        {
            return new Vector3(x, z, y);
        }

        private static void Place(Transform target, Vector3 position, float yawRadians)
        {
            target.position = position;
            target.rotation = Quaternion.Euler(0f, -yawRadians * Mathf.Rad2Deg, 0f);
        }

        private static bool HasArgument(string value)
        {
            foreach (string argument in System.Environment.GetCommandLineArgs())
                if (argument == value)
                    return true;
            return false;
        }

        private static string ArgumentValue(string prefix, string fallback)
        {
            foreach (string argument in System.Environment.GetCommandLineArgs())
                if (argument.StartsWith(prefix))
                    return argument.Substring(prefix.Length);
            return fallback;
        }
    }
}
