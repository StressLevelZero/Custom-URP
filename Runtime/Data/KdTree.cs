using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class KDTreeNode<T>
{
    public Vector3 point;
    public T data;
    public KDTreeNode<T> left;
    public KDTreeNode<T> right;

    public KDTreeNode(Vector3 point, T data)
    {
        this.point = point;
        this.data = data;
        left = null;
        right = null;
    }
}


[System.Serializable]
public class KdTree<T>
{
    private KDTreeNode<T> _root;
    private List<(float distance, KDTreeNode<T> node)> _nearestNodes;

    public KdTree(List<Vector3> points, List<T> data)
    {
        if (points.Count != data.Count)
        {
            throw new System.ArgumentException("Points and data lists must have the same length.");
        }

        var pointDataPairs = new List<(Vector3, T)>();
        for (int i = 0; i < points.Count; i++)
        {
            pointDataPairs.Add((points[i], data[i]));
        }

        _root = BuildKDTree(pointDataPairs, 0);
        _nearestNodes = new List<(float distance, KDTreeNode<T> node)>();
    }

    private KDTreeNode<T> BuildKDTree(List<(Vector3 point, T data)> pointDataPairs, int depth)
    {
        if (pointDataPairs == null || pointDataPairs.Count == 0)
            return null;

        int axis = depth % 3;

        // Sort point list and choose median as pivot element
        pointDataPairs.Sort((a, b) =>
        {
            if (axis == 0)
                return a.point.x.CompareTo(b.point.x);
            else if (axis == 1)
                return a.point.y.CompareTo(b.point.y);
            else
                return a.point.z.CompareTo(b.point.z);
        });

        int medianIndex = pointDataPairs.Count / 2;
        var medianPointData = pointDataPairs[medianIndex];

        KDTreeNode<T> node = new KDTreeNode<T>(medianPointData.point, medianPointData.data);

        // Recursively build the left and right subtrees
        node.left = BuildKDTree(pointDataPairs.GetRange(0, medianIndex), depth + 1);
        node.right = BuildKDTree(pointDataPairs.GetRange(medianIndex + 1, pointDataPairs.Count - medianIndex - 1), depth + 1);

        return node;
    }

    // Modified method to accept a pre-allocated list
    public void KNearestNeighbors(Vector3 target, int k, List<(Vector3 point, T data)> nearestPointsData)
    {
        nearestPointsData.Clear();
        _nearestNodes.Clear();

        KNearest(_root, target, 0, k);

        foreach (var item in _nearestNodes)
        {
            nearestPointsData.Add((item.node.point, item.node.data));
        }
    }

    private void KNearest(KDTreeNode<T> node, Vector3 target, int depth, int k)
    {
        if (node == null)
            return;

        int axis = depth % 3;
        KDTreeNode<T> nextBranch = null;
        KDTreeNode<T> oppositeBranch = null;

        // Decide which branch to search first
        if ((axis == 0 && target.x < node.point.x) ||
            (axis == 1 && target.y < node.point.y) ||
            (axis == 2 && target.z < node.point.z))
        {
            nextBranch = node.left;
            oppositeBranch = node.right;
        }
        else
        {
            nextBranch = node.right;
            oppositeBranch = node.left;
        }

        // Search the next branch
        KNearest(nextBranch, target, depth + 1, k);

        // Check current node
        float distance = Vector3.Distance(node.point, target);

        if (_nearestNodes.Count < k)
        {
            _nearestNodes.Add((distance, node));
        }
        else
        {
            // Find the node with the maximum distance
            int maxIndex = 0;
            float maxDistance = _nearestNodes[0].distance;
            for (int i = 1; i < _nearestNodes.Count; i++)
            {
                if (_nearestNodes[i].distance > maxDistance)
                {
                    maxDistance = _nearestNodes[i].distance;
                    maxIndex = i;
                }
            }

            if (distance < maxDistance)
            {
                _nearestNodes[maxIndex] = (distance, node);
            }
        }

        // Check if we need to search the opposite branch
        float axisDistance = 0;
        if (axis == 0)
            axisDistance = Mathf.Abs(target.x - node.point.x);
        else if (axis == 1)
            axisDistance = Mathf.Abs(target.y - node.point.y);
        else
            axisDistance = Mathf.Abs(target.z - node.point.z);

        // Find current maximum distance in _nearestNodes
        float currentMaxDistance = (_nearestNodes.Count < k) ? float.MaxValue : float.NegativeInfinity;
        for (int i = 0; i < _nearestNodes.Count; i++)
        {
            if (_nearestNodes[i].distance > currentMaxDistance)
                currentMaxDistance = _nearestNodes[i].distance;
        }

        if (_nearestNodes.Count < k || axisDistance < currentMaxDistance)
        {
            KNearest(oppositeBranch, target, depth + 1, k);
        }
    }
}
