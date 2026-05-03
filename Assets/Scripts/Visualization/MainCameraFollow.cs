using UnityEngine;

[RequireComponent(typeof(Camera))]
public class MainCameraFollow : MonoBehaviour
{
    [Header("跟随目标")]
    public Transform target;

    [Header("世界空间偏移")]
    public Vector3 worldOffset = new Vector3(0f, 1.2f, -1.8f);

    [Header("平滑系数")]
    [Range(0.01f, 1f)]
    public float followLerp = 0.15f;

    void Start()
    {
        if (target == null) return;
        if (target.GetComponent<ArticulationBody>() != null) return;

        // URDF 根节点可能不随物理移动，跟随第一个 ArticulationBody 更稳定
        var ab = target.GetComponentInChildren<ArticulationBody>();
        if (ab != null && ab.transform != target)
            target = ab.transform;
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desired = target.position + worldOffset;
        transform.position = Vector3.Lerp(transform.position, desired, followLerp);
        transform.LookAt(target.position);
    }
}
