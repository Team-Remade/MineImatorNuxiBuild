using System.Numerics;

namespace MineImatorSimplyRemade.core.graphics;

internal static class NuxTBvhBuilder
{
    private const int BinCount = 12;
    private const int LeafSize = 6;
    private const int MaxDepth = 64;
    private const int ParallelDepth = 2;
    private const int NodePageShift = 12;
    private const int NodePageSize = 1 << NodePageShift;

    internal static (NuxTBvhNode[] Nodes, int[] Order) Build(Vector3[] minimums, Vector3[] maximums,
        CancellationToken cancellationToken)
    {
        int count = minimums.Length;
        if (maximums.Length != count)
            throw new ArgumentException("BVH primitive bounds must have matching lengths.");
        if (count == 0)
            return ([], []);

        Vector3[] centroids = new Vector3[count];
        int[] order = new int[count];
        for (int i = 0; i < count; i++)
        {
            if ((i & 2047) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            centroids[i] = (minimums[i] + maximums[i]) * 0.5f;
            order[i] = i;
        }

        int maximumNodeCount = checked(count * 2 - 1);
        var nodePages = new NuxTBvhNode[(maximumNodeCount + NodePageSize - 1) >> NodePageShift][];
        var nodePageGate = new object();
        int nextNode = 1;
        int maxDegree = Math.Clamp(Environment.ProcessorCount - 1, 1, 8);
        GetNodePage(0);
        BuildNode(0, 0, count, 0);
        cancellationToken.ThrowIfCancellationRequested();
        var nodes = new NuxTBvhNode[nextNode];
        for (int pageIndex = 0; pageIndex < nodePages.Length; pageIndex++)
        {
            NuxTBvhNode[]? page = Volatile.Read(ref nodePages[pageIndex]);
            if (page is null)
                continue;
            int copyCount = Math.Min(page.Length, nodes.Length - (pageIndex << NodePageShift));
            if (copyCount > 0)
                Array.Copy(page, 0, nodes, pageIndex << NodePageShift, copyCount);
        }
        return (nodes, order);

        void BuildNode(int nodeIndex, int start, int primitiveCount, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetBounds(start, primitiveCount, out Vector3 minimum, out Vector3 maximum,
                out Vector3 centroidMinimum, out Vector3 centroidMaximum);
            if (primitiveCount <= LeafSize || depth >= MaxDepth || !TryFindSplit(start, primitiveCount,
                    minimum, maximum, centroidMinimum, centroidMaximum, out int axis, out int splitBin))
            {
                SetNode(nodeIndex, new NuxTBvhNode(minimum, maximum, start, primitiveCount, -1, -1));
                return;
            }

            float centroidExtent = Component(centroidMaximum - centroidMinimum, axis);
            int left = start;
            int right = start + primitiveCount - 1;
            while (left <= right)
            {
                int bin = GetBin(centroids[order[left]], centroidMinimum, centroidExtent, axis);
                if (bin <= splitBin)
                    left++;
                else
                {
                    int temporary = order[left];
                    order[left] = order[right];
                    order[right] = temporary;
                    right--;
                }
            }

            int leftCount = left - start;
            if (leftCount <= 0 || leftCount >= primitiveCount)
                leftCount = primitiveCount / 2;

            int leftNode = AllocateNode();
            int rightNode = AllocateNode();
            SetNode(nodeIndex, new NuxTBvhNode(minimum, maximum, 0, 0, leftNode, rightNode));
            if (depth < ParallelDepth && primitiveCount >= 1024 && maxDegree > 1)
            {
                Parallel.Invoke(new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = maxDegree
                },
                    () => BuildNode(leftNode, start, leftCount, depth + 1),
                    () => BuildNode(rightNode, start + leftCount, primitiveCount - leftCount, depth + 1));
            }
            else
            {
                BuildNode(leftNode, start, leftCount, depth + 1);
                BuildNode(rightNode, start + leftCount, primitiveCount - leftCount, depth + 1);
            }
        }

        int AllocateNode() => Interlocked.Increment(ref nextNode) - 1;

        void SetNode(int nodeIndex, NuxTBvhNode node) => GetNodePage(nodeIndex)[nodeIndex & (NodePageSize - 1)] = node;

        NuxTBvhNode[] GetNodePage(int nodeIndex)
        {
            int pageIndex = nodeIndex >> NodePageShift;
            NuxTBvhNode[]? page = Volatile.Read(ref nodePages[pageIndex]);
            if (page is not null)
                return page;
            lock (nodePageGate)
            {
                page = nodePages[pageIndex];
                if (page is null)
                {
                    int pageLength = Math.Min(NodePageSize, maximumNodeCount - (pageIndex << NodePageShift));
                    page = new NuxTBvhNode[pageLength];
                    Volatile.Write(ref nodePages[pageIndex], page);
                }
            }
            return page;
        }

        bool TryFindSplit(int start, int primitiveCount, Vector3 minimum, Vector3 maximum,
            Vector3 centroidMinimum, Vector3 centroidMaximum, out int bestAxis, out int bestSplitBin)
        {
            bestAxis = -1;
            bestSplitBin = -1;
            float parentArea = SurfaceArea(minimum, maximum);
            float bestCost = parentArea * primitiveCount;
            Vector3 extent = centroidMaximum - centroidMinimum;

            Span<int> counts = stackalloc int[BinCount];
            Span<Vector3> binMinimums = stackalloc Vector3[BinCount];
            Span<Vector3> binMaximums = stackalloc Vector3[BinCount];
            Span<int> leftCounts = stackalloc int[BinCount - 1];
            Span<int> rightCounts = stackalloc int[BinCount - 1];
            Span<Vector3> leftMinimums = stackalloc Vector3[BinCount - 1];
            Span<Vector3> leftMaximums = stackalloc Vector3[BinCount - 1];
            Span<Vector3> rightMinimums = stackalloc Vector3[BinCount - 1];
            Span<Vector3> rightMaximums = stackalloc Vector3[BinCount - 1];

            for (int axis = 0; axis < 3; axis++)
            {
                float axisExtent = Component(extent, axis);
                if (!(axisExtent > 1e-12f))
                    continue;
                counts.Clear();
                for (int bin = 0; bin < BinCount; bin++)
                {
                    binMinimums[bin] = new Vector3(float.PositiveInfinity);
                    binMaximums[bin] = new Vector3(float.NegativeInfinity);
                }

                for (int i = start; i < start + primitiveCount; i++)
                {
                    if (((i - start) & 2047) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    int primitive = order[i];
                    int bin = GetBin(centroids[primitive], centroidMinimum, axisExtent, axis);
                    counts[bin]++;
                    binMinimums[bin] = Vector3.Min(binMinimums[bin], minimums[primitive]);
                    binMaximums[bin] = Vector3.Max(binMaximums[bin], maximums[primitive]);
                }

                int leftCount = 0;
                Vector3 leftMin = new(float.PositiveInfinity);
                Vector3 leftMax = new(float.NegativeInfinity);
                for (int bin = 0; bin < BinCount - 1; bin++)
                {
                    leftCount += counts[bin];
                    leftMin = Vector3.Min(leftMin, binMinimums[bin]);
                    leftMax = Vector3.Max(leftMax, binMaximums[bin]);
                    leftCounts[bin] = leftCount;
                    leftMinimums[bin] = leftMin;
                    leftMaximums[bin] = leftMax;
                }

                int rightCount = 0;
                Vector3 rightMin = new(float.PositiveInfinity);
                Vector3 rightMax = new(float.NegativeInfinity);
                for (int bin = BinCount - 1; bin > 0; bin--)
                {
                    rightCount += counts[bin];
                    rightMin = Vector3.Min(rightMin, binMinimums[bin]);
                    rightMax = Vector3.Max(rightMax, binMaximums[bin]);
                    rightCounts[bin - 1] = rightCount;
                    rightMinimums[bin - 1] = rightMin;
                    rightMaximums[bin - 1] = rightMax;
                }

                for (int bin = 0; bin < BinCount - 1; bin++)
                {
                    int leftSideCount = leftCounts[bin];
                    int rightSideCount = rightCounts[bin];
                    if (leftSideCount == 0 || rightSideCount == 0)
                        continue;
                    float cost = SurfaceArea(leftMinimums[bin], leftMaximums[bin]) * leftSideCount +
                                 SurfaceArea(rightMinimums[bin], rightMaximums[bin]) * rightSideCount;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestAxis = axis;
                        bestSplitBin = bin;
                    }
                }
            }
            return bestAxis >= 0;
        }

        void GetBounds(int start, int primitiveCount, out Vector3 minimum, out Vector3 maximum,
            out Vector3 centroidMinimum, out Vector3 centroidMaximum)
        {
            minimum = new Vector3(float.PositiveInfinity);
            maximum = new Vector3(float.NegativeInfinity);
            centroidMinimum = new Vector3(float.PositiveInfinity);
            centroidMaximum = new Vector3(float.NegativeInfinity);
            for (int i = start; i < start + primitiveCount; i++)
            {
                if (((i - start) & 2047) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                int primitive = order[i];
                minimum = Vector3.Min(minimum, minimums[primitive]);
                maximum = Vector3.Max(maximum, maximums[primitive]);
                centroidMinimum = Vector3.Min(centroidMinimum, centroids[primitive]);
                centroidMaximum = Vector3.Max(centroidMaximum, centroids[primitive]);
            }
        }
    }

    private static int GetBin(Vector3 centroid, Vector3 centroidMinimum, float extent, int axis) =>
        Math.Clamp((int)((Component(centroid, axis) - Component(centroidMinimum, axis)) / extent * BinCount), 0,
            BinCount - 1);

    private static float SurfaceArea(Vector3 minimum, Vector3 maximum)
    {
        Vector3 extent = Vector3.Max(maximum - minimum, Vector3.Zero);
        return 2f * (extent.X * extent.Y + extent.Y * extent.Z + extent.Z * extent.X);
    }

    private static float Component(Vector3 vector, int axis) => axis switch { 0 => vector.X, 1 => vector.Y, _ => vector.Z };
}