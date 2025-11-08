using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;
using RT.Util.Geometry;
using RT.KitchenSink.Geometry;
using RT.Util.ExtensionMethods;

public class ShatteredScript : MonoBehaviour {

    public KMAudio audio;
    public KMSelectable modSel;
    public KMSelectable shardTemplate;

    VoronoiDiagram voronoi;
    PointD[] shardLabelPositions;
    (MeshRenderer mr, MeshFilter mf, MeshFilter highlightMf, KMSelectable sel)[] shards;
    int heldShard = -1;
    const int totalShards = 10;

    static int moduleIdCounter = 1;
    int moduleId;
    private bool moduleSolved;

    void Awake()
    {
        // Update moduleId field for logging purposes
        moduleId = moduleIdCounter++;
    }

    void Start()
    {
        // Generate randomized shattered mirror
        // Achieved with Voronoi diagram code from Voronoi Maze by Timwi
        var rnd = new System.Random(UnityEngine.Random.Range(0, int.MaxValue));

        tryEverythingAgain:
        var pointsList = new List<PointD>();
        while (pointsList.Count < totalShards)
        {
            var newPoint = new PointD(rnd.NextDouble(), rnd.NextDouble());
            if (pointsList.Any(p => p.Distance(newPoint) < 1 / 128d))
                continue;
            pointsList.Add(newPoint);
        }
        voronoi = VoronoiDiagram.GenerateVoronoiDiagram(pointsList.ToArray(), 1, 1, VoronoiDiagramFlags.IncludeEdgePolygons);
        shardLabelPositions = voronoi.Polygons.Select(p => p.GetLabelPoint(.005)).ToArray();

        // Discard unwanted Voronoi diagrams
        if (voronoi.Polygons.Any(poly => poly.Vertices.ConsecutivePairs(true).Any(pair => pair.Item1.Distance(pair.Item2) < .05)) ||
            voronoi.Edges.Any(e => Math.Min(e.edge.Start.Distance(), e.edge.End.Distance()) < .05) ||
            voronoi.Edges.Any(e => e.edge.Distance(shardLabelPositions[e.siteA]) < .025 || e.edge.Distance(shardLabelPositions[e.siteB]) < .025))
            goto tryEverythingAgain;

        var points = pointsList.ToArray();
        var numShards = points.Length;

        shards = new (MeshRenderer mr, MeshFilter mf, MeshFilter highlightMf, KMSelectable sel)[numShards];
        var children = new List<KMSelectable>();
        for (var shardIx = 0; shardIx < points.Length; shardIx++)
        {
            var polygon = voronoi.Polygons[shardIx];
            var selectable = Instantiate(shardTemplate, transform);
            children.Add(selectable);
            var meshFilter = selectable.GetComponent<MeshFilter>();

            // Shard and collider
            var shardMeshTris = new List<int>();
            var colliderMeshTris = new List<Vector3>();
            var edgeLengths = new List<double>();
            for (var i = 0; i < polygon.Vertices.Count; i++)
            {
                var j = (i + 1) % polygon.Vertices.Count;

                const float colliderTop = 0;
                const float colliderBottom = -.01f;
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], colliderBottom));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], colliderTop));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], colliderTop));

                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], colliderTop));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], colliderBottom));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], colliderBottom));

                if (i != 0 && j != 0)
                {
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[0], colliderTop));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], colliderTop));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], colliderTop));

                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[0], colliderBottom));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], colliderBottom));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], colliderBottom));

                    shardMeshTris.Add(0);
                    shardMeshTris.Add(j);
                    shardMeshTris.Add(i);
                }

                edgeLengths.Add(polygon.Vertices[j].Distance(polygon.Vertices[i]));
            }
            var colliderMesh = new Mesh();
            colliderMesh.vertices = colliderMeshTris.ToArray();
            colliderMesh.triangles = Enumerable.Range(0, colliderMeshTris.Count).ToArray();
            colliderMesh.normals = Enumerable.Repeat(new Vector3(0, 1, 0), colliderMeshTris.Count).ToArray();
            selectable.gameObject.GetComponent<MeshCollider>().sharedMesh = colliderMesh;

            var shardMesh = new Mesh();
            shardMesh.vertices = shardMeshTris.Select(pIx => convertPointToVector(polygon.Vertices[pIx], 0)).ToArray();
            shardMesh.triangles = Enumerable.Range(0, shardMeshTris.Count).ToArray();
            shardMesh.normals = Enumerable.Repeat(new Vector3(0, 1, 0), shardMeshTris.Count).ToArray();
            var totalLength = edgeLengths.Sum();
            Vector2 uvVector(double proportion) => new Vector2(Mathf.Cos(2 * Mathf.PI * (float)proportion), Mathf.Sin(2 * Mathf.PI * (float)proportion)) * .5f + new Vector2(.5f, .5f);
            shardMesh.uv = shardMeshTris.Select(ix => uvVector(edgeLengths.Take(ix).Sum() / totalLength)).ToArray();
            meshFilter.sharedMesh = shardMesh;

            shards[shardIx] = (selectable.GetComponent<MeshRenderer>(), selectable.GetComponent<MeshFilter>(), selectable.Highlight.GetComponent<MeshFilter>(), selectable);
            int sIx = shardIx;
            selectable.OnInteract = delegate () { ShardPressed(sIx); return false; };
            selectable.OnInteractEnded = delegate () { ShardReleased(sIx); };
        }

        Destroy(shardTemplate.gameObject);

        modSel.Children = children.ToArray();
        modSel.UpdateChildren();
    }

    const double cf = .0835 / .5;
    PointD convertPoint(PointD orig) => (orig - new PointD(.5, .5)) * cf;
    Vector3 convertPointToVector(PointD orig, float y)
    {
        var p = convertPoint(orig);
        return new Vector3((float)p.X, y, (float)p.Y);
    }

    void Update()
    {
        if (heldShard != -1)
        {
            shards[heldShard].sel.transform.position = Camera.main.ScreenToViewportPoint(new Vector3(Input.mousePosition.x, 0.01f, -Input.mousePosition.z));
        }
    }

    void ShardPressed(int shardIndex)
    {
        audio.PlaySoundAtTransform("shardUp", shards[shardIndex].sel.transform);
        heldShard = shardIndex;
    }

    void ShardReleased(int shardIndex)
    {
        audio.PlaySoundAtTransform("shardDown", shards[shardIndex].sel.transform);
        heldShard = -1;
    }
}