using System.Collections.Generic;
using UnityEngine;

public class CollisionCounter : MonoBehaviour
{
    [Header("Robot")]
    public Transform robotRoot;

    [Header("Obstacle Filter")]
    public string obstacleNameContains = "Obstacle";

    public static int TotalCollisions { get; private set; }

    readonly HashSet<int> activeObstacleColliders = new();
    readonly List<CollisionCounterProxy> proxies = new();

    void Awake()
    {
        if (robotRoot == null)
        {
            var controller = FindFirstObjectByType<TurtleBotController>();
            if (controller != null)
                robotRoot = controller.transform.root;
        }

        InstallProxies();
    }

    void OnDestroy()
    {
        foreach (var proxy in proxies)
        {
            if (proxy != null && proxy.Owner == this)
                proxy.Owner = null;
        }
        proxies.Clear();
    }

    public static void ResetAll()
    {
        TotalCollisions = 0;
    }

    void InstallProxies()
    {
        if (robotRoot == null) return;

        foreach (var collider in robotRoot.GetComponentsInChildren<Collider>(true))
        {
            var proxy = collider.GetComponent<CollisionCounterProxy>();
            if (proxy == null)
                proxy = collider.gameObject.AddComponent<CollisionCounterProxy>();

            proxy.Owner = this;
            proxies.Add(proxy);
        }
    }

    internal void NotifyCollisionEnter(Collider other)
    {
        if (other == null || !IsObstacle(other)) return;

        int id = other.GetInstanceID();
        if (!activeObstacleColliders.Add(id)) return;

        TotalCollisions++;
        Debug.Log($"CollisionCounter: 碰撞次数={TotalCollisions}，对象={other.name}");
    }

    internal void NotifyCollisionExit(Collider other)
    {
        if (other == null) return;
        activeObstacleColliders.Remove(other.GetInstanceID());
    }

    bool IsObstacle(Collider other)
    {
        if (!string.IsNullOrEmpty(obstacleNameContains))
        {
            for (Transform t = other.transform; t != null; t = t.parent)
            {
                if (t.name.Contains(obstacleNameContains))
                    return true;
            }
        }

        return false;
    }
}

public class CollisionCounterProxy : MonoBehaviour
{
    public CollisionCounter Owner { get; set; }

    void OnCollisionEnter(Collision collision)
    {
        Owner?.NotifyCollisionEnter(collision.collider);
    }

    void OnCollisionExit(Collision collision)
    {
        Owner?.NotifyCollisionExit(collision.collider);
    }
}
