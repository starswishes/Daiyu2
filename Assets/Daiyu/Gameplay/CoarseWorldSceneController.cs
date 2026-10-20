using System;
using System.Collections.Generic;
using System.IO;
using Daiyu.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Daiyu.Gameplay
{
    /// <summary>Small formal-scene adapter for B. The world owns inventory; these objects only mirror it.</summary>
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
        readonly List<LineRenderer> routes = new List<LineRenderer>();
        readonly List<Material> materials = new List<Material>();
        int selected = -1;
        string status = "Click a wet node, then a downhill neighbor to plan water.";
        long visualTick = -1;
        Vector3 cameraFocus;
        float cameraDistance = 22f;
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
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) Pick();
            if (Keyboard.current != null)
            {
                if (Keyboard.current.f5Key.wasPressedThisFrame) Save();
                if (Keyboard.current.lKey.wasPressedThisFrame) Load();
                if (Keyboard.current.escapeKey.wasPressedThisFrame) { selected = -1; status = "Selection cleared."; }
            }
            HandleCamera();
            world.Advance(Mathf.Clamp(Mathf.RoundToInt(Time.deltaTime * 1000f), 0, 1000));
            if (visualTick != world.Tick) RefreshVisuals();
        }

        void Pick()
        {
            if (Camera.main == null) return;
            Vector2 pointer = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(pointer);
            if (!Physics.Raycast(ray, out RaycastHit hit)) return;
            if (!nodes.TryGetValue(hit.collider.gameObject, out int target)) return;
            if (selected < 0)
            {
                if (world.WaterAt(target) <= 0) { status = "That node has no water."; return; }
                selected = target;
                status = "Source selected. Click a downhill adjacent node.";
                RefreshVisuals();
                return;
            }
            if (selected == target) { selected = -1; status = "Selection cleared."; return; }
            if (!world.TryPlan(selected, target, out int edge))
            {
                status = "No legal downhill connection from the selected source.";
                return;
            }
            if (world.CommitPlan(edge)) status = "Water route committed; real inventory is moving.";
            selected = -1;
            RefreshVisuals();
        }

        void RebuildVisuals()
        {
            foreach (GameObject visual in visuals) if (visual != null) Destroy(visual);
            foreach (LineRenderer route in routes) if (route != null) Destroy(route.gameObject);
            foreach (Material material in materials) if (material != null) Destroy(material);
            visuals.Clear(); nodes.Clear();
            routes.Clear();
            materials.Clear();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            for (int i = 0; i < world.NodeCount; i++)
            {
                CoarseNode node = world.Node(i);
                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                visual.name = "WaterNode_" + i;
                visual.transform.SetParent(transform, false);
                visual.transform.position = new Vector3((node.Q + node.R * .5f) * spacing, node.Height * terrainHeight, node.R * spacing * .86f);
                visual.transform.localScale = new Vector3(.72f, .12f, .72f);
                Renderer renderer = visual.GetComponent<Renderer>();
                if (shader != null) { renderer.material = new Material(shader); materials.Add(renderer.material); }
                nodes.Add(visual, i); visuals.Add(visual);
            }
            for (int i = 0; i < world.EdgeCount; i++)
            {
                GameObject routeObject = new GameObject("WaterRoute_" + i);
                routeObject.transform.SetParent(transform, false);
                LineRenderer route = routeObject.AddComponent<LineRenderer>();
                route.positionCount = 2; route.startWidth = .09f; route.endWidth = .09f;
                if (shader != null) { route.material = new Material(shader); materials.Add(route.material); }
                routes.Add(route);
            }
            FrameCamera();
            RefreshVisuals();
        }

        void RefreshVisuals()
        {
            for (int i = 0; i < visuals.Count; i++)
            {
                long water = world.WaterAt(i);
                Renderer renderer = visuals[i].GetComponent<Renderer>();
                renderer.material.color = i == selected ? new Color(1f, .8f, .1f) :
                    water > 0 ? new Color(.1f, .65f, .95f) : new Color(.3f, .72f, .28f);
                visuals[i].transform.localScale = new Vector3(.72f, .12f + water / 10000f * .22f, .72f);
            }
            for (int i = 0; i < routes.Count; i++)
            {
                CoarseEdge edge = world.Edge(i);
                Vector3 from = visuals[edge.From].transform.position + Vector3.up * .08f;
                Vector3 to = visuals[edge.To].transform.position + Vector3.up * .08f;
                routes[i].SetPosition(0, from); routes[i].SetPosition(1, to);
                routes[i].enabled = edge.Enabled;
                routes[i].startColor = routes[i].endColor = edge.Enabled ? new Color(.05f, .45f, 1f) : Color.clear;
            }
            visualTick = world.Tick;
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
                world = candidate; selected = -1; RebuildVisuals(); status = "Restored Daiyu2 slot 0.";
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
            GUI.Box(new Rect(16, 16, 430, 96), "Daiyu2 · 粗粒度水世界");
            GUI.Label(new Rect(30, 42, 400, 22), "库存: " + world.TotalWater + " / " + world.InitialWater + "    Tick: " + world.Tick);
            GUI.Label(new Rect(30, 64, 400, 22), status);
            GUI.Label(new Rect(30, 86, 400, 22), "WASD/方向键 平移   滚轮 缩放   F5 保存   L 读取   Esc 清除");
        }
    }
}
