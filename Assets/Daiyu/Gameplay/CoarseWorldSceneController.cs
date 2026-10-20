using System;
using System.Collections.Generic;
using System.IO;
using Daiyu.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Daiyu.Gameplay
{
    /// <summary>Small formal-scene adapter for the coarse playable world. The world owns inventory; these objects only mirror it.</summary>
    public sealed class CoarseWorldSceneController : MonoBehaviour
    {
        [SerializeField] uint seed = 20261021;
        [SerializeField] int radius = 3;
        [SerializeField] float spacing = 2.1f;
        [SerializeField] float terrainHeight = .002f;
        [SerializeField] float panSpeed = 10f;
        [SerializeField] float zoomSpeed = 2f;
        [SerializeField] float minCameraDistance = 12f;
        [SerializeField] float maxCameraDistance = 36f;

        CoarseWorld world;
        readonly Dictionary<GameObject, int> nodes = new Dictionary<GameObject, int>();
        readonly List<GameObject> visuals = new List<GameObject>();
        readonly List<GameObject> terrainVisuals = new List<GameObject>();
        readonly List<Renderer> terrainRenderers = new List<Renderer>();
        readonly List<GameObject> vegetationVisuals = new List<GameObject>();
        readonly List<Renderer> vegetationRenderers = new List<Renderer>();
        readonly List<int> vegetationNodes = new List<int>();
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<LineRenderer> routes = new List<LineRenderer>();
        readonly List<LineRenderer> flowMarkers = new List<LineRenderer>();
        readonly List<Material> materials = new List<Material>();
        readonly HashSet<int> previewEdges = new HashSet<int>();
        int selected = -1;
        int hovered = -1;
        int[] plannedPath;
        string status = "Click a wet node, then a downhill neighbor to plan water.";
        long visualTick = -1;
        Vector3 cameraFocus;
        float cameraDistance = 22f;
        bool paused;
        bool home = true;
        string SavePath => Path.Combine(Application.persistentDataPath, "Daiyu2", "slot0.json");

        void Start()
        {
            world = CoarseWorld.Generate(seed, radius);
            EnsureCameraAndLight();
            RebuildVisuals();
        }

        void Update()
        {
            if (world == null) return;
            UpdatePointerPreview();
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) Pick();
            if (Keyboard.current != null)
            {
                if (Keyboard.current.f5Key.wasPressedThisFrame) Save();
                if (Keyboard.current.lKey.wasPressedThisFrame) Load();
                if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.nKey.wasPressedThisFrame)
                    StartNewGame();
                if (Keyboard.current.pKey.wasPressedThisFrame && !home) TogglePause();
                if (Keyboard.current.hKey.wasPressedThisFrame) ReturnHome();
                if (Keyboard.current.escapeKey.wasPressedThisFrame) ClearSelection();
                if (Keyboard.current.rKey.wasPressedThisFrame && !home) SummonRain();
            }
            if (home) return;
            HandleCamera();
            if (!paused)
                world.Advance(Mathf.Clamp(Mathf.RoundToInt(Time.deltaTime * 1000f), 0, 1000));
            if (visualTick != world.Tick) RefreshVisuals();
        }

        void UpdatePointerPreview()
        {
            if (Camera.main == null || Mouse.current == null) return;
            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            int nextHovered = -1;
            if (Physics.Raycast(ray, out RaycastHit hit) && nodes.TryGetValue(hit.collider.gameObject, out int target))
                nextHovered = target;
            bool changed = nextHovered != hovered;
            hovered = nextHovered;
            int[] nextPath = null;
            if (selected >= 0 && hovered >= 0 && hovered != selected)
                world.TryFindPath(selected, hovered, out nextPath);
            if (!PathsEqual(plannedPath, nextPath)) changed = true;
            plannedPath = nextPath;
            if (changed) RefreshVisuals();
        }

        static bool PathsEqual(int[] a, int[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        void ClearSelection()
        {
            selected = -1;
            plannedPath = null;
            status = "Selection cleared.";
            RefreshVisuals();
        }

        void SummonRain()
        {
            int target = selected >= 0 ? selected : hovered;
            if (target < 0) { status = "Hover a node before summoning rain."; return; }
            status = world.TrySummonRain(target) ? "Rain summoned from the local atmosphere." :
                "Not enough local atmospheric water or surface capacity.";
            RefreshVisuals();
        }

        void TogglePause()
        {
            paused = !paused;
            status = paused ? "Paused. Press P to resume." : "Simulation resumed.";
        }

        void ReturnHome()
        {
            home = true;
            paused = false;
            selected = -1;
            plannedPath = null;
            status = "Home. Press Enter or N to start a new game.";
            RefreshVisuals();
        }

        void StartNewGame()
        {
            seed = (uint)UnityEngine.Random.Range(1, int.MaxValue);
            world = CoarseWorld.Generate(seed, radius);
            selected = -1;
            paused = false;
            home = false;
            status = "New game started.";
            RebuildVisuals();
        }

        void Pick()
        {
            if (Camera.main == null) return;
            Vector2 pointer = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(pointer);
            if (!Physics.Raycast(ray, out RaycastHit hit)) return;
            if (!nodes.TryGetValue(hit.collider.gameObject, out int target)) return;
            HandleNodeClick(target);
        }

        void HandleNodeClick(int target)
        {
            if (selected < 0)
            {
                if (world.WaterAt(target) <= 0) { status = "That node has no water."; return; }
                selected = target;
                status = "Source selected. Move over a reachable node to preview a route.";
                RefreshVisuals();
                return;
            }
            if (selected == target) { ClearSelection(); return; }
            if (plannedPath == null || plannedPath.Length < 2)
                world.TryFindPath(selected, target, out plannedPath);
            if (plannedPath == null || plannedPath.Length < 2)
            {
                status = "No legal downhill route from the selected source.";
                return;
            }
            if (world.CommitPath(plannedPath)) status = "Water route committed; real inventory is moving.";
            else status = "Route is already committed or no longer valid.";
            selected = -1;
            plannedPath = null;
            RefreshVisuals();
        }

        void RebuildVisuals()
        {
            foreach (GameObject visual in visuals) if (visual != null) Destroy(visual);
            foreach (GameObject visual in terrainVisuals) if (visual != null) Destroy(visual);
            foreach (GameObject visual in vegetationVisuals) if (visual != null) Destroy(visual);
            foreach (LineRenderer route in routes) if (route != null) Destroy(route.gameObject);
            foreach (LineRenderer marker in flowMarkers) if (marker != null) Destroy(marker.gameObject);
            foreach (Material material in materials) if (material != null) Destroy(material);
            foreach (Mesh mesh in meshes) if (mesh != null) Destroy(mesh);
            visuals.Clear(); nodes.Clear();
            terrainVisuals.Clear(); terrainRenderers.Clear();
            vegetationVisuals.Clear(); vegetationRenderers.Clear(); vegetationNodes.Clear();
            meshes.Clear();
            routes.Clear();
            flowMarkers.Clear();
            materials.Clear();
            previewEdges.Clear();
            plannedPath = null;
            hovered = -1;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Mesh terrainMesh = CreateHexMesh(spacing * .6f);
            meshes.Add(terrainMesh);
            for (int i = 0; i < world.NodeCount; i++)
            {
                CoarseNode node = world.Node(i);
                CreateTerrainTile(node, i, shader, terrainMesh);
                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                visual.name = "WaterNode_" + i;
                visual.transform.SetParent(transform, false);
                visual.transform.position = NodePosition(node);
                visual.transform.localScale = new Vector3(.72f, .12f, .72f);
                Renderer renderer = visual.GetComponent<Renderer>();
                if (shader != null) { renderer.material = new Material(shader); materials.Add(renderer.material); }
                nodes.Add(visual, i); visuals.Add(visual);
                if (i % 3 == 0) CreateVegetation(node, i, shader);
            }
            for (int i = 0; i < world.EdgeCount; i++)
            {
                GameObject routeObject = new GameObject("WaterRoute_" + i);
                routeObject.transform.SetParent(transform, false);
                LineRenderer route = routeObject.AddComponent<LineRenderer>();
                route.positionCount = 2; route.startWidth = .09f; route.endWidth = .09f;
                if (shader != null) { route.material = new Material(shader); materials.Add(route.material); }
                routes.Add(route);

                GameObject markerObject = new GameObject("FlowMarker_" + i);
                markerObject.transform.SetParent(transform, false);
                LineRenderer marker = markerObject.AddComponent<LineRenderer>();
                marker.positionCount = 5;
                marker.startWidth = .055f;
                marker.endWidth = .055f;
                if (shader != null) { marker.material = new Material(shader); materials.Add(marker.material); }
                flowMarkers.Add(marker);
            }
            FrameCamera();
            RefreshVisuals();
        }

        Vector3 NodePosition(CoarseNode node)
        {
            return new Vector3((node.Q + node.R * .5f) * spacing,
                node.Height * terrainHeight, node.R * spacing * .86f);
        }

        void CreateTerrainTile(CoarseNode node, int index, Shader shader, Mesh mesh)
        {
            GameObject tile = new GameObject("TerrainTile_" + index);
            tile.transform.SetParent(transform, false);
            Vector3 position = NodePosition(node);
            position.y -= .14f;
            tile.transform.position = position;
            tile.AddComponent<MeshFilter>().sharedMesh = mesh;
            Renderer renderer = tile.AddComponent<MeshRenderer>();
            if (shader != null)
            {
                renderer.material = new Material(shader);
                materials.Add(renderer.material);
            }
            terrainVisuals.Add(tile);
            terrainRenderers.Add(renderer);
        }

        void CreateVegetation(CoarseNode node, int index, Shader shader)
        {
            GameObject vegetation = GameObject.CreatePrimitive(PrimitiveType.Quad);
            vegetation.name = "Vegetation_" + index;
            vegetation.transform.SetParent(transform, false);
            vegetation.transform.position = NodePosition(node) + new Vector3(.34f, .62f, .24f);
            vegetation.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            vegetation.transform.localScale = new Vector3(.68f, 1.05f, 1f);
            Collider collider = vegetation.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer renderer = vegetation.GetComponent<Renderer>();
            if (shader != null)
            {
                renderer.material = new Material(shader);
                materials.Add(renderer.material);
            }
            vegetationVisuals.Add(vegetation);
            vegetationRenderers.Add(renderer);
            vegetationNodes.Add(index);
        }

        static Mesh CreateHexMesh(float radius)
        {
            var mesh = new Mesh { name = "DaiyuHexTerrain" };
            var vertices = new Vector3[7];
            vertices[0] = Vector3.zero;
            for (int i = 0; i < 6; i++)
            {
                float angle = Mathf.PI / 6f + i * Mathf.PI / 3f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            }
            var triangles = new int[18];
            for (int i = 0; i < 6; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 2 > 6) ? 1 : i + 2;
                triangles[i * 3 + 2] = i + 1;
            }
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        void RefreshVisuals()
        {
            previewEdges.Clear();
            if (plannedPath != null)
                for (int i = 1; i < plannedPath.Length; i++)
                    if (TryFindEdge(plannedPath[i - 1], plannedPath[i], out int edge)) previewEdges.Add(edge);
            for (int i = 0; i < visuals.Count; i++)
            {
                long water = world.WaterAt(i);
                Renderer renderer = visuals[i].GetComponent<Renderer>();
                float ecology = world.EcologyAt(i) / 1000f;
                Color dry = DryLandColor(ecology);
                terrainRenderers[i].material.color = SurfaceColor(world.Node(i), water, ecology);
                renderer.material.color = i == selected ? new Color(1f, .8f, .1f) : i == hovered ? new Color(1f, .95f, .45f) :
                    water > 0 ? WaterNodeColor(world.Node(i), water) : dry;
                visuals[i].transform.localScale = new Vector3(.72f, .12f + water / 10000f * .22f, .72f);
            }
            for (int i = 0; i < vegetationRenderers.Count; i++)
            {
                int node = vegetationNodes[i];
                float ecology = world.EcologyAt(node) / 1000f;
                vegetationRenderers[i].material.color = Color.Lerp(new Color(.34f, .28f, .12f),
                    new Color(.18f, .68f, .24f), ecology);
                vegetationVisuals[i].transform.localScale = new Vector3(.68f,
                    .7f + ecology * .7f, 1f);
            }
            for (int i = 0; i < routes.Count; i++)
            {
                CoarseEdge edge = world.Edge(i);
                Vector3 from = visuals[edge.From].transform.position + Vector3.up * .08f;
                Vector3 to = visuals[edge.To].transform.position + Vector3.up * .08f;
                routes[i].SetPosition(0, from); routes[i].SetPosition(1, to);
                bool preview = previewEdges.Contains(i);
                routes[i].enabled = edge.Enabled || preview;
                bool flowing = edge.Enabled && world.LastFlow(i) > 0;
                bool wetRoute = world.WaterAt(edge.From) > 0 || world.WaterAt(edge.To) > 0;
                bool dryRoute = edge.Enabled && !flowing && !wetRoute;
                routes[i].startWidth = routes[i].endWidth = preview && !edge.Enabled ? .14f : flowing ? .13f : dryRoute ? .065f : .09f;
                routes[i].startColor = routes[i].endColor = edge.Enabled ?
                    (flowing ? new Color(.05f, .85f, 1f) : dryRoute ? new Color(.38f, .38f, .42f) : new Color(.05f, .35f, .72f)) :
                    preview ? new Color(1f, .72f, .12f) : Color.clear;

                LineRenderer marker = flowMarkers[i];
                marker.enabled = flowing;
                if (flowing)
                {
                    Vector3 direction = to - from;
                    direction.y = 0f;
                    direction.Normalize();
                    Vector3 side = new Vector3(-direction.z, 0f, direction.x);
                    Vector3 tip = Vector3.Lerp(from, to, .63f) + Vector3.up * .07f;
                    Vector3 basePoint = tip - direction * .2f;
                    marker.SetPosition(0, tip);
                    marker.SetPosition(1, basePoint + side * .12f);
                    marker.SetPosition(2, tip);
                    marker.SetPosition(3, basePoint - side * .12f);
                    marker.SetPosition(4, tip);
                    marker.startColor = marker.endColor = new Color(.8f, .98f, 1f);
                }
            }
            visualTick = world.Tick;
        }

        static Color DryLandColor(float ecology)
        {
            return Color.Lerp(new Color(.58f, .38f, .2f), new Color(.3f, .72f, .28f), ecology);
        }

        static Color SurfaceColor(CoarseNode node, long water, float ecology)
        {
            if (water <= 0) return DryLandColor(ecology);
            float depth = Mathf.Clamp01((float)water / node.Capacity);
            return Color.Lerp(new Color(.28f, .72f, .78f), new Color(.04f, .28f, .62f), depth);
        }

        static Color WaterNodeColor(CoarseNode node, long water)
        {
            float depth = Mathf.Clamp01((float)water / node.Capacity);
            return Color.Lerp(new Color(.2f, .78f, 1f), new Color(.03f, .32f, .9f), depth);
        }

        bool TryFindEdge(int from, int to, out int edgeId)
        {
            for (int i = 0; i < world.EdgeCount; i++)
            {
                CoarseEdge edge = world.Edge(i);
                if (edge.From == from && edge.To == to) { edgeId = i; return true; }
            }
            edgeId = -1;
            return false;
        }

        void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(SavePath);
                Directory.CreateDirectory(directory);
                string temporary = SavePath + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(world.CaptureSnapshot(), true));
                if (File.Exists(SavePath)) File.Replace(temporary, SavePath, null); else File.Move(temporary, SavePath);
                status = "Saved Daiyu2 slot 0.";
            }
            catch (Exception error) { status = "Save failed: " + error.Message; }
        }

        void Load()
        {
            try
            {
                if (!File.Exists(SavePath)) { status = "No Daiyu2 slot 0 exists."; return; }
                CoarseWorldSnapshot snapshot = JsonUtility.FromJson<CoarseWorldSnapshot>(File.ReadAllText(SavePath));
                CoarseWorld candidate = CoarseWorld.Restore(snapshot);
                world = candidate; selected = -1; paused = false; home = false; RebuildVisuals(); status = "Restored Daiyu2 slot 0.";
            }
            catch (Exception error) { status = "Load failed; current world retained: " + error.Message; }
        }

        void EnsureCameraAndLight()
        {
            if (Camera.main == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.tag = "MainCamera";
            }
            if (FindFirstObjectByType<Light>() == null)
            {
                GameObject lightObject = new GameObject("Directional Light");
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.15f;
                light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            }
        }

        void FrameCamera()
        {
            if (Camera.main == null) return;
            cameraFocus = Vector3.zero;
            cameraDistance = Mathf.Clamp(cameraDistance, minCameraDistance, maxCameraDistance);
            Camera.main.transform.position = cameraFocus + new Vector3(0f, cameraDistance * .91f, -cameraDistance);
            Camera.main.transform.LookAt(cameraFocus);
            Camera.main.orthographic = false;
            Camera.main.fieldOfView = 32f;
        }

        void HandleCamera()
        {
            if (Camera.main == null || Keyboard.current == null) return;
            Vector2 movement = Vector2.zero;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) movement.x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) movement.x += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) movement.y -= 1f;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) movement.y += 1f;
            cameraFocus += new Vector3(movement.x, 0f, movement.y) * (panSpeed * Time.unscaledDeltaTime);
            if (Mouse.current != null)
                cameraDistance = Mathf.Clamp(cameraDistance - Mouse.current.scroll.ReadValue().y * zoomSpeed * .01f,
                    minCameraDistance, maxCameraDistance);
            Camera.main.transform.position = cameraFocus + new Vector3(0f, cameraDistance * .91f, -cameraDistance);
            Camera.main.transform.LookAt(cameraFocus);
        }

        void OnGUI()
        {
            if (world == null) return;
            if (home)
            {
                GUI.Box(new Rect(16, 16, 470, 170), "Daiyu2 · 粗粒度水世界");
                GUI.Label(new Rect(30, 48, 440, 22), "欢迎来到 Daiyu2。");
                GUI.Label(new Rect(30, 76, 440, 22), "Enter / N 开始随机新局");
                GUI.Label(new Rect(30, 104, 440, 22), "L 读取独立存档   H 保持在首页");
                GUI.Label(new Rect(30, 132, 440, 22), "固定斜向镜头；进入新局后可引水、催雨和暂停。");
                return;
            }
            GUI.Box(new Rect(16, 16, 470, 247), "Daiyu2 · 粗粒度水世界");
            GUI.Label(new Rect(30, 42, 400, 22), "库存: " + world.TotalWater + " / " + world.InitialWater + "    Tick: " + world.Tick);
            GUI.Label(new Rect(30, 64, 440, 22), "天气: " + world.Weather + "   云量: " + world.CloudCover + "%");
            GUI.Label(new Rect(30, 86, 440, 22), status);
            GUI.Label(new Rect(30, 108, 440, 22), "WASD/方向键 平移   滚轮 缩放   R 催雨");
            GUI.Label(new Rect(30, 130, 440, 22), "F5 保存   L 读取   P 暂停/继续   N 新局   H 首页   Esc 清除");
            GUI.Label(new Rect(30, 152, 440, 22), "黄线预览 · 蓝线有水 · 灰线干涸 · 青线正在来水");
            int nextLine = 174;
            if (paused)
            {
                GUI.Label(new Rect(30, nextLine, 440, 22), "模拟已暂停；镜头仍可移动，按 P 继续");
                nextLine += 22;
            }
            if (plannedPath != null)
            {
                GUI.Label(new Rect(30, nextLine, 440, 22), "预览路径: " + plannedPath.Length + " 个节点（黄色）");
                nextLine += 22;
            }
            int focus = selected >= 0 ? selected : hovered;
            if (focus >= 0)
                GUI.Label(new Rect(30, nextLine, 440, 22), "节点 " + focus + " · 地表 " + world.WaterAt(focus) +
                    " · 土壤 " + world.SoilLifeAt(focus) + " · 地下 " + world.GroundwaterAt(focus) +
                    " · 供泉 " + world.LastSpringFlowAt(focus) + " · 生态 " + world.EcologyAt(focus) +
                    " · 视野 " + world.VisibilityAt(focus));
        }
    }
}
