using UnityEngine;

[RequireComponent(typeof(Camera))]
public class MainCameraFollow : MonoBehaviour
{
    [Header("第一人称偏移")]
    public Vector3 firstPersonOffset = new Vector3(0f, 0.18f, 0f);

    [Header("近裁剪面")]
    public float nearClipPlane = 0.01f;

    void Start()
    {
        Transform target = ResolveRobotRoot();
        if (target == null) return;

        transform.SetParent(target);
        transform.localPosition = firstPersonOffset;
        transform.localRotation = Quaternion.identity;

        var cam = GetComponent<Camera>();
        cam.nearClipPlane = nearClipPlane;
    }

    Transform ResolveRobotRoot()
    {
        var bodies = FindObjectsByType<ArticulationBody>(FindObjectsSortMode.None);
        foreach (var ab in bodies)
        {
            if (ab.isRoot)
                return ab.transform;
        }
        return null;
    }
}
