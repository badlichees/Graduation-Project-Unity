using UnityEngine;
using UnityEngine.EventSystems;
using RobotSimulation.MapGeneration;

// 直接改 MapGenerator 的障碍物数据，让地图发布端和场景显示保持同源
public class DynamicObstacleEditor : MonoBehaviour
{
    [Header("References")]
    public MapGenerator mapGenerator;

    [Header("Settings")]
    public float raycastMaxDistance = 200f;

    void Start()
    {
        if (mapGenerator == null)
            mapGenerator = FindFirstObjectByType<MapGenerator>();

        if (mapGenerator == null)
            Debug.LogError("DynamicObstacleEditor: 未找到 MapGenerator，请在 Inspector 中指定");
    }

    void Update()
    {
        if (mapGenerator == null) return;
        if (!Input.GetMouseButtonDown(1)) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (Camera.main == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        // 优先删除真实碰撞到的障碍物，避免射线落点被网格取整误判
        if (Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance))
        {
            if (mapGenerator.TryGetObstacleAt(hit.transform, out Coord coord))
            {
                mapGenerator.SetObstacleAtGrid(coord, false);
                return;
            }
        }

        // 空地没有 Collider，只能和地面平面做数学求交
        if (Mathf.Abs(ray.direction.y) < 1e-6f) return;
        float t = -ray.origin.y / ray.direction.y;
        if (t < 0f) return;
        Vector3 groundPoint = ray.origin + t * ray.direction;
        Coord placeCoord = mapGenerator.WorldToGrid(groundPoint);
        mapGenerator.SetObstacleAtGrid(placeCoord, true);
    }

    void OnGUI()
    {
        GUI.Label(
            new Rect(10, Screen.height - 58, 320, 24),
            "右键: 放置/删除障碍物",
            new GUIStyle(GUI.skin.label) { fontSize = 13 });
    }
}
