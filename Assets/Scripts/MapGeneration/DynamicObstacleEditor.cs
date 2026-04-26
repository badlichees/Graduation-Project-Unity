using UnityEngine;
using UnityEngine.EventSystems;
using RobotSimulation.MapGeneration;

/// <summary>
/// 运行时右键点击动态增删障碍物：点击障碍物删除，点击空地添加。
/// 挂载到场景中任意活跃 GameObject 上，并在 Inspector 指定 MapGenerator。
/// </summary>
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
            Debug.LogError("DynamicObstacleEditor: 未找到 MapGenerator，请在 Inspector 中指定。");
    }

    void Update()
    {
        if (mapGenerator == null) return;
        if (!Input.GetMouseButtonDown(1)) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (Camera.main == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        // 先用 Physics.Raycast 检测是否点到了障碍物
        if (Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance))
        {
            if (mapGenerator.TryGetObstacleAt(hit.transform, out Coord coord))
            {
                mapGenerator.SetObstacleAtGrid(coord, false);
                return;
            }
        }

        // 没有打到障碍物——用 y=0 平面数学求交放置新障碍物
        // Tile 可能没有 Collider，所以不能依赖 Raycast 来判断地面
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
